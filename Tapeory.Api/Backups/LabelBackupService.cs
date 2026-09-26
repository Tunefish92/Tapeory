using System.IO.Compression;
using System.Text.Json;
using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Storage;
using Tapeory.Api.Templates;
using Microsoft.EntityFrameworkCore;

namespace Tapeory.Api.Backups;

public sealed class InvalidBackupException(string message) : Exception(message);

public sealed record LabelRestoreResult(int RestoredTemplates, int ReplacedTemplates);

/// <summary>
/// Label backups: one .zip with every template (metadata and current version) plus the files they
/// use — images, preview images and imported .lbx originals. Restoring replaces the current
/// templates with the ones in the backup; the replaced ones are deleted the same way as from the
/// templates page, so print history keeps working.
///
/// Callers must hold <see cref="BackupStore.TryBeginOperation"/> for the duration of a call.
/// </summary>
public sealed class LabelBackupService(
    AppDbContext db,
    FileStorageService fileStorage,
    BackupStore store,
    TimeProvider time,
    ILogger<LabelBackupService> logger)
{
    private const string FilesFolder = "files/";

    public async Task<BackupInfo> CreateAsync(bool beforeRestore, CancellationToken cancellationToken)
    {
        var templates = await db.Templates
            .AsNoTracking()
            .Where(template => template.DeletedAt == null && template.CurrentVersion != null)
            .Include(template => template.CurrentVersion!)
                .ThenInclude(version => version.Fields)
            .Include(template => template.ConversionWarnings)
            .OrderBy(template => template.Id)
            .ToListAsync(cancellationToken);

        var fileIds = new HashSet<int>();

        foreach (var template in templates)
        {
            var version = template.CurrentVersion!;
            fileIds.UnionWith(LabelDocumentImages.FindImageFileIds(version.EditorJson));

            if (version.PreviewImageFileId is { } previewId)
            {
                fileIds.Add(previewId);
            }

            if (template.SourceLbxFileId is { } lbxId)
            {
                fileIds.Add(lbxId);
            }
        }

        var files = await db.UploadedFiles
            .AsNoTracking()
            .Where(file => fileIds.Contains(file.Id) && file.Category != FileStorageCategory.PrintJobOutput)
            .OrderBy(file => file.Id)
            .ToListAsync(cancellationToken);

        return await store.WriteAsync(BackupKind.Labels, beforeRestore, async path =>
        {
            await using var zipStream = File.Create(path);
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Create);

            var backedUpFiles = new List<LabelBackupFile>();

            foreach (var file in files)
            {
                var archivePath = $"{FilesFolder}{file.Id}{SafeFileNaming.GetSafeExtension(file.FileName)}";

                try
                {
                    await using var source = fileStorage.OpenRead(file.RelativePath);
                    await using var entry = zip.CreateEntry(archivePath, CompressionLevel.Fastest).Open();
                    await source.CopyToAsync(entry, cancellationToken);
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    // The template still restores; it just shows the same missing image it does now.
                    logger.LogWarning("File {Path} is missing from the storage folder and was left out of the label backup.", file.RelativePath);
                    continue;
                }

                backedUpFiles.Add(new LabelBackupFile(file.Id, file.OriginalFileName, file.ContentType, file.Category, archivePath));
            }

            var contents = new LabelBackupContents(
                LabelBackupContents.CurrentFormatVersion,
                time.GetUtcNow(),
                [.. templates.Select(ToBackup)],
                backedUpFiles);

            await using var manifest = zip.CreateEntry(LabelBackupContents.EntryName, CompressionLevel.Optimal).Open();
            await JsonSerializer.SerializeAsync(manifest, contents, LabelBackupContents.JsonOptions, cancellationToken);
        });
    }

    /// <summary>Not cancellable once it starts writing: stopping halfway would leave some
    /// templates replaced and others not.</summary>
    public async Task<LabelRestoreResult> RestoreAsync(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var contents = await ReadContentsAsync(zip);

        var storedPaths = new List<string>();
        var obsoleteLbxPaths = new List<string>();

        await using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // 1. Files first, so the templates can point at their new ids.
            var restoredFiles = new Dictionary<int, UploadedFile>();

            foreach (var file in contents.Files)
            {
                if (file.Category is not (FileStorageCategory.Image or FileStorageCategory.OriginalLbx)
                    || zip.GetEntry(file.ArchivePath) is not { } entry)
                {
                    continue;
                }

                await using var source = entry.Open();
                var stored = await fileStorage.SaveAsync(source, file.OriginalFileName, file.Category);
                storedPaths.Add(stored.RelativePath);

                var uploadedFile = new UploadedFile
                {
                    FileName = stored.FileName,
                    OriginalFileName = file.OriginalFileName,
                    ContentType = file.ContentType,
                    SizeBytes = stored.SizeBytes,
                    Category = file.Category,
                    RelativePath = stored.RelativePath
                };

                db.UploadedFiles.Add(uploadedFile);
                restoredFiles[file.Id] = uploadedFile;
            }

            await db.SaveChangesAsync();

            var newFileIds = restoredFiles.ToDictionary(pair => pair.Key, pair => pair.Value.Id);
            int? NewFileId(int? oldId) => oldId is { } id && newFileIds.TryGetValue(id, out var newId) ? newId : null;

            // 2. Retire the current templates, like deleting them from the templates page does.
            var now = time.GetUtcNow();
            var current = await db.Templates
                .Include(template => template.SourceLbxFile)
                .Where(template => template.DeletedAt == null)
                .ToListAsync();

            foreach (var template in current)
            {
                template.DeletedAt = now;

                if (template.SourceLbxFile is { } lbx)
                {
                    obsoleteLbxPaths.Add(lbx.RelativePath);
                    template.SourceLbxFile = null;
                    db.UploadedFiles.Remove(lbx);
                }
            }

            // 3. The templates from the backup. Template.CurrentVersionId and
            // TemplateVersion.TemplateId form a cycle between new rows, so this takes two saves
            // (see TemplateService.CreateAsync).
            var restored = contents.Templates.Select(backup =>
            {
                var version = new TemplateVersion
                {
                    VersionNumber = 1,
                    WidthMm = backup.WidthMm,
                    HeightMm = backup.HeightMm,
                    EditorJson = LabelDocumentImages.RemapImageFileIds(backup.EditorJson, newFileIds),
                    PreviewImageFileId = NewFileId(backup.PreviewImageFileId),
                    Fields = [.. backup.Fields.Select(field => new TemplateField
                    {
                        Name = field.Name,
                        Label = field.Label,
                        DefaultValue = field.DefaultValue,
                        Required = field.Required
                    })],
                    CreatedAt = backup.UpdatedAt
                };

                var template = new Template
                {
                    Name = backup.Name,
                    Description = backup.Description,
                    Category = TemplateGroups.Normalize(backup.Category),
                    TagsCsv = TemplateMapper.SerializeTags(backup.Tags),
                    Status = backup.Status,
                    SourceLbxFileId = NewFileId(backup.SourceLbxFileId),
                    ConversionWarnings = [.. backup.ConversionWarnings.Select(message => new TemplateConversionWarning { Message = message })],
                    CreatedAt = backup.CreatedAt,
                    UpdatedAt = backup.UpdatedAt
                };

                template.Versions.Add(version);
                return (template, version);
            }).ToList();

            db.Templates.AddRange(restored.Select(pair => pair.template));
            await db.SaveChangesAsync();

            foreach (var (template, version) in restored)
            {
                template.CurrentVersion = version;
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            DeleteFiles(obsoleteLbxPaths);
            logger.LogInformation(
                "Restored {Restored} templates from {Path}, replacing {Replaced}.", restored.Count, path, current.Count);

            return new LabelRestoreResult(restored.Count, current.Count);
        }
        catch
        {
            await transaction.RollbackAsync();
            DeleteFiles(storedPaths);
            throw;
        }
    }

    private static async Task<LabelBackupContents> ReadContentsAsync(ZipArchive zip)
    {
        var entry = zip.GetEntry(LabelBackupContents.EntryName)
            ?? throw new InvalidBackupException($"The archive has no {LabelBackupContents.EntryName}.");

        LabelBackupContents? contents;

        try
        {
            await using var stream = entry.Open();
            contents = await JsonSerializer.DeserializeAsync<LabelBackupContents>(stream, LabelBackupContents.JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidBackupException($"{LabelBackupContents.EntryName} can't be read: {ex.Message}");
        }

        if (contents is null || contents.FormatVersion != LabelBackupContents.CurrentFormatVersion)
        {
            throw new InvalidBackupException(
                $"Unsupported label backup format version {contents?.FormatVersion}; this Tapeory version reads version {LabelBackupContents.CurrentFormatVersion}.");
        }

        return contents with { Templates = contents.Templates ?? [], Files = contents.Files ?? [] };
    }

    private static LabelBackupTemplate ToBackup(Template template)
    {
        var version = template.CurrentVersion!;

        return new LabelBackupTemplate(
            template.Name,
            template.Description,
            template.Category,
            TemplateMapper.ParseTags(template.TagsCsv),
            template.Status,
            version.WidthMm,
            version.HeightMm,
            version.EditorJson,
            [.. version.Fields.Select(TemplateMapper.ToDto)],
            version.PreviewImageFileId,
            template.SourceLbxFileId,
            [.. template.ConversionWarnings.Select(warning => warning.Message)],
            template.CreatedAt,
            template.UpdatedAt);
    }

    private void DeleteFiles(IEnumerable<string> relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            try
            {
                fileStorage.Delete(relativePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not delete {Path}.", relativePath);
            }
        }
    }
}
