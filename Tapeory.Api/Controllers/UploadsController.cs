using Tapeory.Api.Auth;
using Tapeory.Api.Backups;
using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Storage;
using Tapeory.Api.Templates;
using Tapeory.Api.Uploads;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Tapeory.Api.Controllers;

[ApiController]
[Route("api/uploads")]
public sealed class UploadsController(AppDbContext db, FileStorageService fileStorage) : ControllerBase
{
    [HttpPost("images")]
    [RequestSizeLimit(ImageUploadValidator.MaxSizeBytes)]
    public async Task<IActionResult> UploadImage(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return Problem("No file was uploaded.", statusCode: StatusCodes.Status400BadRequest);
        }

        var validation = ImageUploadValidator.Validate(file.ContentType, file.Length);

        if (!validation.IsValid)
        {
            return Problem(validation.Error, statusCode: StatusCodes.Status400BadRequest);
        }

        Stream contentStream = file.OpenReadStream();
        var contentType = file.ContentType;
        var fileName = file.FileName;

        if (ImageUploadValidator.NeedsConversionToPng(file.ContentType))
        {
            using var buffer = new MemoryStream();
            await contentStream.CopyToAsync(buffer, cancellationToken);
            await contentStream.DisposeAsync();

            var png = RasterImageConverter.ToPng(buffer.ToArray());

            if (png is null)
            {
                return Problem("The image could not be read.", statusCode: StatusCodes.Status400BadRequest);
            }

            contentStream = new MemoryStream(png);
            contentType = "image/png";
            fileName = Path.ChangeExtension(file.FileName, ".png");
        }

        if (string.Equals(file.ContentType, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
        {
            using var buffer = new MemoryStream();
            await contentStream.CopyToAsync(buffer, cancellationToken);
            await contentStream.DisposeAsync();

            var sanitized = SvgSanitizer.Sanitize(buffer.ToArray());

            if (!sanitized.IsValid)
            {
                return Problem(sanitized.Error, statusCode: StatusCodes.Status400BadRequest);
            }

            contentStream = new MemoryStream(sanitized.SanitizedContent!);
        }

        await using var stream = contentStream;
        var stored = await fileStorage.SaveAsync(stream, fileName, FileStorageCategory.Image, cancellationToken);

        var uploadedFile = new UploadedFile
        {
            FileName = stored.FileName,
            OriginalFileName = fileName,
            ContentType = contentType,
            SizeBytes = stored.SizeBytes,
            Category = FileStorageCategory.Image,
            RelativePath = stored.RelativePath,
            OwnerUserId = AuthClaims.UserId(User)
        };

        db.UploadedFiles.Add(uploadedFile);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetImage),
            new { id = uploadedFile.Id },
            ToResponse(uploadedFile));
    }

    [HttpGet("images/{id:int}")]
    public async Task<IActionResult> GetImage(int id, CancellationToken cancellationToken)
    {
        var uploadedFile = await db.UploadedFiles
            .SingleOrDefaultAsync(f => f.Id == id && f.Category == FileStorageCategory.Image, cancellationToken);

        var access = TemplateAccess.For(User);

        if (uploadedFile is null
            || !(access.IsAdmin || uploadedFile.OwnerUserId == access.UserId || await IsInVisibleTemplateAsync(id, access, cancellationToken)))
        {
            return NotFound();
        }

        var stream = fileStorage.OpenRead(uploadedFile.RelativePath);
        return File(stream, uploadedFile.ContentType, uploadedFile.OriginalFileName);
    }

    /// <summary>Whether image <paramref name="id"/> is part of a template the account can see:
    /// in any of its versions' designs, or as a preview image.</summary>
    private async Task<bool> IsInVisibleTemplateAsync(int id, TemplateAccess access, CancellationToken cancellationToken)
    {
        var versions = access.Visible(db.Templates.Where(template => template.DeletedAt == null))
            .SelectMany(template => template.Versions);

        if (await versions.AnyAsync(version => version.PreviewImageFileId == id, cancellationToken))
        {
            return true;
        }

        // The database narrows it down; parsing the design decides (id 12 isn't id 123).
        var marker = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var designs = await versions
            .Where(version => version.EditorJson.Contains(marker))
            .Select(version => version.EditorJson)
            .ToListAsync(cancellationToken);

        return designs.Any(design => LabelDocumentImages.FindImageFileIds(design).Contains(id));
    }

    private static UploadedImageResponse ToResponse(UploadedFile file) => new(
        file.Id,
        file.FileName,
        file.OriginalFileName,
        file.ContentType,
        file.SizeBytes,
        $"/api/uploads/images/{file.Id}",
        file.CreatedAt);
}
