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
public sealed class UploadsController(AppDbContext db, FileStorageService fileStorage, ImageStore images) : ControllerBase
{
    [HttpPost("images")]
    [RequestSizeLimit(ImageUploadValidator.MaxSizeBytes)]
    public async Task<IActionResult> UploadImage(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return Problem("No file was uploaded.", statusCode: StatusCodes.Status400BadRequest);
        }

        // Checked before reading, so an oversized upload isn't taken into memory first.
        var validation = ImageUploadValidator.Validate(file.ContentType, file.Length);

        if (!validation.IsValid)
        {
            return Problem(validation.Error, statusCode: StatusCodes.Status400BadRequest);
        }

        using var buffer = new MemoryStream();
        await using (var content = file.OpenReadStream())
        {
            await content.CopyToAsync(buffer, cancellationToken);
        }

        var stored = await images.StoreAsync(
            buffer.ToArray(), file.ContentType, file.FileName, AuthClaims.UserId(User), cancellationToken);

        if (stored.File is not { } uploadedFile)
        {
            return Problem(stored.Error, statusCode: StatusCodes.Status400BadRequest);
        }

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
