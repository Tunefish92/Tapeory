using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Tapeory.Api.Backups;
using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Storage;
using Tapeory.Api.Uploads;

namespace Tapeory.Api.Templates;

/// <summary>Templates to and from ".tapeory" files (see <see cref="TemplateFile"/>).</summary>
public sealed class TemplateFileService(AppDbContext db, FileStorageService fileStorage, ImageStore images, TemplateService templates)
{
    private static readonly string CreatedWith =
        $"Tapeory {typeof(TemplateFileService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?"}";

    /// <summary>The template's current version with its images, as the file's text.</summary>
    public async Task<string> ExportAsync(Template template, CancellationToken cancellationToken)
    {
        var fileIds = LabelDocumentImages.FindImageFileIds(template.CurrentVersion?.EditorJson ?? string.Empty);
        var files = await db.UploadedFiles.AsNoTracking()
            .Where(file => fileIds.Contains(file.Id) && file.Category == FileStorageCategory.Image)
            .OrderBy(file => file.Id)
            .ToListAsync(cancellationToken);
        var embedded = new Dictionary<int, TemplateFileImage>();

        foreach (var file in files)
        {
            try
            {
                await using var stream = fileStorage.OpenRead(file.RelativePath);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                embedded[file.Id] = new TemplateFileImage($"image-{embedded.Count + 1}", file.OriginalFileName, file.ContentType, buffer.ToArray());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The file is gone from the storage folder: the template goes out without that picture.
            }
        }

        return TemplateFile.Write(template, embedded, CreatedWith);
    }

    /// <summary>Stores the file's images and creates the template; or says why not, leaving
    /// nothing behind.</summary>
    public async Task<(Template? Template, string? Error)> ImportAsync(
        TemplateFileContents contents, int? ownerUserId, CancellationToken cancellationToken)
    {
        var stored = new List<UploadedFile>();
        var fileIds = new Dictionary<string, int>();

        foreach (var image in contents.Images)
        {
            var result = await images.StoreAsync(image.Content, image.ContentType, image.FileName, ownerUserId, cancellationToken);

            if (result.File is null)
            {
                foreach (var file in stored)
                {
                    fileStorage.Delete(file.RelativePath);
                }

                db.UploadedFiles.RemoveRange(stored);
                await db.SaveChangesAsync(cancellationToken);

                return (null, $"The image '{image.FileName}' in the file can't be used: {result.Error}");
            }

            stored.Add(result.File);
            fileIds[image.Id] = result.File.Id;
        }

        var template = await templates.CreateAsync(
            new CreateTemplateRequest(
                contents.Name!,
                contents.Description,
                contents.Category,
                contents.Tags,
                contents.WidthMm,
                contents.HeightMm,
                contents.EditorJson(fileIds),
                contents.Fields),
            cancellationToken);

        return (template, null);
    }
}
