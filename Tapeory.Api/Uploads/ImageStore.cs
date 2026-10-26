using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Storage;

namespace Tapeory.Api.Uploads;

/// <param name="File">The stored image, or null when it was refused.</param>
public sealed record StoredImage(UploadedFile? File, string? Error);

/// <summary>
/// Takes an image into Tapeory's storage: checks type and size, turns formats browsers can't show
/// into PNG, and strips anything active out of an SVG. Every image goes through here, whether it
/// was uploaded in the editor or came inside an imported template file.
/// </summary>
public sealed class ImageStore(AppDbContext db, FileStorageService fileStorage)
{
    public async Task<StoredImage> StoreAsync(
        byte[] content, string? contentType, string fileName, int? ownerUserId, CancellationToken cancellationToken)
    {
        var validation = ImageUploadValidator.Validate(contentType, content.Length);

        if (!validation.IsValid)
        {
            return new StoredImage(null, validation.Error);
        }

        if (ImageUploadValidator.NeedsConversionToPng(contentType))
        {
            if (RasterImageConverter.ToPng(content) is not { } png)
            {
                return new StoredImage(null, "The image could not be read.");
            }

            content = png;
            contentType = "image/png";
            fileName = Path.ChangeExtension(fileName, ".png");
        }

        if (string.Equals(contentType, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
        {
            var sanitized = SvgSanitizer.Sanitize(content);

            if (!sanitized.IsValid)
            {
                return new StoredImage(null, sanitized.Error);
            }

            content = sanitized.SanitizedContent!;
        }

        using var stream = new MemoryStream(content);
        var stored = await fileStorage.SaveAsync(stream, fileName, FileStorageCategory.Image, cancellationToken);

        var uploadedFile = new UploadedFile
        {
            FileName = stored.FileName,
            OriginalFileName = fileName,
            ContentType = contentType!,
            SizeBytes = stored.SizeBytes,
            Category = FileStorageCategory.Image,
            RelativePath = stored.RelativePath,
            OwnerUserId = ownerUserId
        };

        db.UploadedFiles.Add(uploadedFile);
        await db.SaveChangesAsync(cancellationToken);

        return new StoredImage(uploadedFile, null);
    }
}
