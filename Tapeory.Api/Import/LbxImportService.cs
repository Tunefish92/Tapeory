using System.Xml.Linq;
using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Storage;
using Tapeory.Api.Templates;
using Tapeory.Api.Uploads;

namespace Tapeory.Api.Import;

public sealed class LbxImportService(AppDbContext db, FileStorageService fileStorage, IHttpContextAccessor? http = null)
{
    /// <summary>Imports a .lbx upload: the original bytes are always stored first and preserved
    /// unchanged, regardless of whether the file could be parsed — so no source data is ever
    /// lost even for a file this importer can't make sense of.</summary>
    public async Task<Template> ImportAsync(
        Stream fileStream,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        await fileStream.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var stored = await fileStorage.SaveAsync(
            buffer, originalFileName, FileStorageCategory.OriginalLbx, cancellationToken);

        var sourceFile = new UploadedFile
        {
            FileName = stored.FileName,
            OriginalFileName = originalFileName,
            ContentType = "application/octet-stream",
            SizeBytes = stored.SizeBytes,
            Category = FileStorageCategory.OriginalLbx,
            RelativePath = stored.RelativePath
        };

        db.UploadedFiles.Add(sourceFile);

        buffer.Position = 0;
        var (conversionResult, suggestedName, embeddedFiles) = ParseSafely(buffer, originalFileName);
        await StoreImagesAsync(conversionResult, embeddedFiles, cancellationToken);

        var template = new Template
        {
            Name = suggestedName,
            SourceLbxFile = sourceFile
        };
        TemplateAccess.For(http?.HttpContext?.User ?? new System.Security.Claims.ClaimsPrincipal()).ClaimNew(template);

        var version = new TemplateVersion
        {
            VersionNumber = 1,
            WidthMm = conversionResult.WidthMm,
            HeightMm = conversionResult.HeightMm,
            EditorJson = conversionResult.EditorJson,
            Fields = [.. conversionResult.Fields.Select(field => new TemplateField
            {
                Name = field.Name,
                Label = field.Label,
                DefaultValue = field.DefaultValue,
                Required = field.Required
            })]
        };

        template.Versions.Add(version);

        foreach (var warning in conversionResult.Warnings)
        {
            template.ConversionWarnings.Add(new TemplateConversionWarning { Message = warning });
        }

        db.Templates.Add(template);
        // See TemplateService.CreateAsync for why this needs two SaveChanges calls: Template and
        // TemplateVersion are both brand-new rows that reference each other by generated key.
        await db.SaveChangesAsync(cancellationToken);

        template.CurrentVersion = version;
        await db.SaveChangesAsync(cancellationToken);
        await db.Entry(template).Reference(t => t.Owner).LoadAsync(cancellationToken);

        return template;
    }

    // Must match the Template.Name column's HasMaxLength(200) in AppDbContext. This name is
    // system-derived (from prop.xml's title or the uploaded filename), not typed by a user, so
    // truncating silently is the right behavior — there's no form to reject it back to.
    private const int MaxNameLength = 200;

    /// <summary>
    /// Stores each embedded image as a PNG upload (P-touch Editor keeps them as BMP or TIFF,
    /// which browsers can't show) and points its object at it. An image whose file is missing or
    /// can't be decoded is dropped with a warning, like any other object that can't be converted.
    /// </summary>
    private async Task StoreImagesAsync(
        LbxConversionResult result, IReadOnlyDictionary<string, byte[]> embeddedFiles, CancellationToken cancellationToken)
    {
        var stored = new List<(ImportedImageObject Image, UploadedFile File)>();

        foreach (var image in result.Images.ToList())
        {
            var png = embeddedFiles.TryGetValue(image.SourceFileName, out var data)
                ? RasterImageConverter.ToPng(data)
                : null;

            if (png is null)
            {
                result.Document.Objects.Remove(image);
                result.Warnings.Add(data is null
                    ? $"'{image.Name}' (image) was not converted — its file {image.SourceFileName} is missing from the archive."
                    : $"'{image.Name}' (image) was not converted — its file {image.SourceFileName} isn't an image Tapeory can read.");
                continue;
            }

            var fileName = Path.ChangeExtension(image.SourceFileName, ".png");
            var saved = await fileStorage.SaveAsync(
                new MemoryStream(png), fileName, FileStorageCategory.Image, cancellationToken);
            var file = new UploadedFile
            {
                FileName = saved.FileName,
                OriginalFileName = fileName,
                ContentType = "image/png",
                SizeBytes = saved.SizeBytes,
                Category = FileStorageCategory.Image,
                RelativePath = saved.RelativePath,
                OwnerUserId = Auth.AuthClaims.UserId(http?.HttpContext?.User ?? new System.Security.Claims.ClaimsPrincipal())
            };

            db.UploadedFiles.Add(file);
            stored.Add((image, file));
        }

        if (stored.Count == 0)
        {
            return;
        }

        // The objects need the files' database ids before the editor JSON is written.
        await db.SaveChangesAsync(cancellationToken);

        foreach (var (image, file) in stored)
        {
            image.UploadedFileId = file.Id;
            image.Url = $"/api/uploads/images/{file.Id}";
        }
    }

    private static (LbxConversionResult Result, string SuggestedName, IReadOnlyDictionary<string, byte[]> Files) ParseSafely(
        Stream zipStream, string originalFileName)
    {
        var fallbackName = Truncate(Path.GetFileNameWithoutExtension(originalFileName));

        if (string.IsNullOrWhiteSpace(fallbackName))
        {
            fallbackName = "Imported Template";
        }

        try
        {
            var archive = LbxArchiveReader.Read(zipStream);
            var result = LbxObjectConverter.Convert(archive.LabelXml);
            var name = Truncate(ReadTitleFromProp(archive.PropXml) ?? fallbackName);
            return (result, name, archive.Files);
        }
        catch (LbxParseException ex)
        {
            return (LbxObjectConverter.EmptyResult(ex.Message), fallbackName, new Dictionary<string, byte[]>());
        }
    }

    private static string Truncate(string value) =>
        value.Length > MaxNameLength ? value[..MaxNameLength] : value;

    private static string? ReadTitleFromProp(XDocument? propXml)
    {
        if (propXml?.Root is null)
        {
            return null;
        }

        var title = propXml.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value;
        return string.IsNullOrWhiteSpace(title) ? null : title.Trim();
    }
}
