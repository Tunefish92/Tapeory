using Tapeory.Api.Import;
using Tapeory.Api.Rendering;
using Tapeory.Api.Storage;
using Tapeory.Api.Templates;
using Microsoft.AspNetCore.Mvc;

namespace Tapeory.Api.Controllers;

[ApiController]
[Route("api/templates")]
public sealed class TemplatesController(
    TemplateService templates,
    LbxImportService lbxImportService,
    FileStorageService fileStorage,
    LabelRenderer labelRenderer,
    UploadedFileImageResolver imageResolver) : ControllerBase
{
    private const long MaxLbxSizeBytes = 20 * 1024 * 1024; // 20 MB

    // Must match the Template.Name column's HasMaxLength(200) in AppDbContext, so an overlong
    // name is rejected with a clean 400 instead of failing the SQL insert/update.
    private const int MaxNameLength = 200;

    // Matches ASP.NET Core's default (camelCase) API formatter so the exported file uses the
    // same casing as every other JSON response instead of System.Text.Json's PascalCase default.
    private static readonly System.Text.Json.JsonSerializerOptions ExportJsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    [HttpGet]
    public async Task<IActionResult> GetTemplates(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var results = await templates.ListAsync(search, category, status, cancellationToken);
        return Ok(results.Select(template => TemplateMapper.ToSummary(template, templates.Access)));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetTemplate(int id, CancellationToken cancellationToken)
    {
        var template = await templates.GetByIdAsync(id, cancellationToken);
        return template is null ? NotFound() : Ok(TemplateMapper.ToDetail(template, templates.Access));
    }

    [HttpPost]
    public async Task<IActionResult> CreateTemplate(
        [FromBody] CreateTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var nameError = ValidateName(request.Name) ?? TemplateGroups.Validate(request.Category);

        if (nameError is not null)
        {
            return Problem(nameError, statusCode: StatusCodes.Status400BadRequest);
        }

        var validationError = ValidateVersionFields(request.WidthMm, request.HeightMm, request.EditorJson);

        if (validationError is not null)
        {
            return Problem(validationError, statusCode: StatusCodes.Status400BadRequest);
        }

        var template = await templates.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetTemplate), new { id = template.Id }, TemplateMapper.ToDetail(template, templates.Access));
    }

    [HttpPost("{id:int}/duplicate")]
    public async Task<IActionResult> DuplicateTemplate(
        int id,
        [FromBody] DuplicateTemplateRequest? request,
        CancellationToken cancellationToken)
    {
        var source = await templates.GetByIdAsync(id, cancellationToken);

        if (source is null)
        {
            return NotFound();
        }

        var name = string.IsNullOrWhiteSpace(request?.Name) ? CopyName(source.Name) : request.Name.Trim();
        var nameError = ValidateName(name);

        if (nameError is not null)
        {
            return Problem(nameError, statusCode: StatusCodes.Status400BadRequest);
        }

        var copy = await templates.DuplicateAsync(id, name, cancellationToken);

        return copy is null
            ? NotFound()
            : CreatedAtAction(nameof(GetTemplate), new { id = copy.Id }, TemplateMapper.ToSummary(copy, templates.Access));
    }

    /// <summary>"Name (copy)", shortening the name so the result still fits.</summary>
    private static string CopyName(string name)
    {
        const string suffix = " (copy)";
        return name[..Math.Min(name.Length, MaxNameLength - suffix.Length)] + suffix;
    }

    [HttpPost("groups/rename")]
    public async Task<IActionResult> RenameGroup(
        [FromBody] RenameGroupRequest request,
        CancellationToken cancellationToken)
    {
        if (TemplateGroups.Normalize(request.From) is null)
        {
            return Problem("The group to rename is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var groupError = TemplateGroups.Validate(request.To);

        if (groupError is not null)
        {
            return Problem(groupError, statusCode: StatusCodes.Status400BadRequest);
        }

        var updated = await templates.RenameGroupAsync(request.From, request.To, cancellationToken);
        return Ok(new RenameGroupResponse(updated));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateTemplate(
        int id,
        [FromBody] UpdateTemplateMetadataRequest request,
        CancellationToken cancellationToken)
    {
        var nameError = ValidateName(request.Name) ?? TemplateGroups.Validate(request.Category);

        if (nameError is not null)
        {
            return Problem(nameError, statusCode: StatusCodes.Status400BadRequest);
        }

        if (await DenyEditAsync(id, cancellationToken) is { } denied)
        {
            return denied;
        }

        try
        {
            var template = await templates.UpdateMetadataAsync(id, request, cancellationToken);
            return template is null ? NotFound() : Ok(TemplateMapper.ToDetail(template, templates.Access));
        }
        catch (ArgumentException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Removes the template from everywhere except print history and statistics — see
    /// Template.DeletedAt.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteTemplate(int id, CancellationToken cancellationToken) =>
        await DenyEditAsync(id, cancellationToken) ?? (await templates.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound());

    /// <summary>Makes a template public (every account sees it) or private (only its owner and
    /// administrators). Only the owner or an administrator may, and only once accounts exist.</summary>
    [HttpPut("{id:int}/visibility")]
    public async Task<IActionResult> SetVisibility(
        int id,
        [FromBody] SetVisibilityRequest request,
        CancellationToken cancellationToken)
    {
        if (templates.Access.UserId is null)
        {
            return Problem(
                "Templates are shared with everyone until the first account is created.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (await DenyEditAsync(id, cancellationToken) is { } denied)
        {
            return denied;
        }

        var template = await templates.SetVisibilityAsync(id, request.IsPublic, cancellationToken);
        return template is null ? NotFound() : Ok(TemplateMapper.ToDetail(template, templates.Access));
    }

    [HttpPost("{id:int}/versions")]
    public async Task<IActionResult> CreateVersion(
        int id,
        [FromBody] CreateTemplateVersionRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateVersionFields(request.WidthMm, request.HeightMm, request.EditorJson);

        if (validationError is not null)
        {
            return Problem(validationError, statusCode: StatusCodes.Status400BadRequest);
        }

        if (await DenyEditAsync(id, cancellationToken) is { } denied)
        {
            return denied;
        }

        var version = await templates.AddVersionAsync(id, request, cancellationToken);

        return version is null ? NotFound() : Ok(TemplateMapper.ToVersionResponse(version));
    }

    [HttpGet("{id:int}/versions")]
    public async Task<IActionResult> GetVersions(int id, CancellationToken cancellationToken)
    {
        if (!await templates.ExistsAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var versions = await templates.ListVersionsAsync(id, cancellationToken);
        return Ok(versions.Select(TemplateMapper.ToVersionResponse));
    }

    [HttpPut("{id:int}/preview-image")]
    public async Task<IActionResult> SetPreviewImage(
        int id,
        [FromBody] SetPreviewImageRequest request,
        CancellationToken cancellationToken)
    {
        if (await DenyEditAsync(id, cancellationToken) is { } denied)
        {
            return denied;
        }

        var result = await templates.SetPreviewImageAsync(id, request.UploadedFileId, cancellationToken);

        return result switch
        {
            SetPreviewImageResult.Success => NoContent(),
            SetPreviewImageResult.TemplateNotFound => NotFound(),
            SetPreviewImageResult.FileNotFound => Problem(
                $"Uploaded file {request.UploadedFileId} was not found.",
                statusCode: StatusCodes.Status400BadRequest),
            _ => Problem("Unexpected result.", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    [HttpGet("{id:int}/export")]
    public async Task<IActionResult> ExportTemplate(int id, CancellationToken cancellationToken)
    {
        var template = await templates.GetByIdAsync(id, cancellationToken);

        if (template is null)
        {
            return NotFound();
        }

        var export = TemplateMapper.ToExport(template);
        var fileName = $"{SanitizeForFileName(template.Name)}.tapeory.json";

        return File(
            System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(export, ExportJsonOptions)),
            "application/json",
            fileName);
    }

    [HttpPost("import")]
    public async Task<IActionResult> ImportTemplate(
        [FromBody] NativeTemplateExport export,
        CancellationToken cancellationToken)
    {
        var nameError = ValidateName(export.Name) ?? TemplateGroups.Validate(export.Category);

        if (nameError is not null)
        {
            return Problem(nameError, statusCode: StatusCodes.Status400BadRequest);
        }

        var validationError = ValidateVersionFields(export.WidthMm, export.HeightMm, export.EditorJson);

        if (validationError is not null)
        {
            return Problem(validationError, statusCode: StatusCodes.Status400BadRequest);
        }

        var template = await templates.ImportAsync(export, cancellationToken);

        return CreatedAtAction(nameof(GetTemplate), new { id = template.Id }, TemplateMapper.ToDetail(template, templates.Access));
    }

    [HttpPost("import-lbx")]
    [RequestSizeLimit(MaxLbxSizeBytes)]
    public async Task<IActionResult> ImportLbxTemplate(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return Problem("No file was uploaded.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (file.Length == 0)
        {
            return Problem("The uploaded file is empty.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (file.Length > MaxLbxSizeBytes)
        {
            return Problem(
                $"The uploaded file exceeds the {MaxLbxSizeBytes / (1024 * 1024)} MB limit.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!Path.GetExtension(file.FileName).Equals(".lbx", StringComparison.OrdinalIgnoreCase))
        {
            return Problem("Only .lbx files are supported.", statusCode: StatusCodes.Status400BadRequest);
        }

        await using var stream = file.OpenReadStream();
        var template = await lbxImportService.ImportAsync(stream, file.FileName, cancellationToken);

        return CreatedAtAction(nameof(GetTemplate), new { id = template.Id }, TemplateMapper.ToDetail(template, templates.Access));
    }

    [HttpPost("{id:int}/preview")]
    public async Task<IActionResult> PreviewTemplate(
        int id,
        [FromBody] PreviewRequest? request,
        CancellationToken cancellationToken)
    {
        var template = await templates.GetByIdAsync(id, cancellationToken);

        if (template?.CurrentVersion is null)
        {
            return NotFound();
        }

        var resolvedValues = FieldValueValidator.ResolveValues(
            template.CurrentVersion.Fields, request?.FieldValues ?? []);
        // The version's own WidthMm/HeightMm columns are authoritative (validated separately on
        // every save); whatever's embedded in editorJson is not trusted for this.
        var document = LabelDocumentParser.Parse(template.CurrentVersion.EditorJson) with
        {
            WidthMm = template.CurrentVersion.WidthMm,
            HeightMm = template.CurrentVersion.HeightMm
        };

        if (string.Equals(request?.Format, "pdf", StringComparison.OrdinalIgnoreCase))
        {
            var pdfBytes = labelRenderer.RenderPdf(document, resolvedValues, imageResolver.AsDelegate());
            return File(pdfBytes, "application/pdf", $"{SanitizeForFileName(template.Name)}-preview.pdf");
        }

        var pngBytes = labelRenderer.RenderPng(document, resolvedValues, imageResolver.AsDelegate());
        return File(pngBytes, "image/png");
    }

    /// <summary>A PNG of the current version for list/card previews. GET (unlike POST /preview) so
    /// it can be an &lt;img src&gt;: the browser lazy-loads and caches it. Callers append
    /// ?v={currentVersionNumber}, which changes on every save, so a long cache lifetime is safe.</summary>
    [HttpGet("{id:int}/thumbnail")]
    public async Task<IActionResult> GetThumbnail(int id, CancellationToken cancellationToken)
    {
        var template = await templates.GetByIdAsync(id, cancellationToken);

        if (template?.CurrentVersion is null)
        {
            return NotFound();
        }

        // Same fallback the editor's preview mode uses: a field's default, else its name as a
        // visible placeholder, so a card never shows a blank where a field sits.
        var sampleValues = template.CurrentVersion.Fields.ToDictionary(
            field => field.Name,
            field => string.IsNullOrEmpty(field.DefaultValue) ? $"[{field.Name}]" : field.DefaultValue);

        byte[] pngBytes;

        try
        {
            var document = LabelDocumentParser.Parse(template.CurrentVersion.EditorJson) with
            {
                WidthMm = template.CurrentVersion.WidthMm,
                HeightMm = template.CurrentVersion.HeightMm
            };
            pngBytes = labelRenderer.RenderPng(document, sampleValues, imageResolver.AsDelegate());
        }
        catch (System.Text.Json.JsonException)
        {
            return Problem("This template's layout could not be rendered.", statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        Response.Headers.CacheControl = "private, max-age=86400";
        return File(pngBytes, "image/png");
    }

    [HttpGet("{id:int}/original-lbx")]
    public async Task<IActionResult> DownloadOriginalLbx(int id, CancellationToken cancellationToken)
    {
        var sourceFile = await templates.GetSourceLbxFileAsync(id, cancellationToken);

        if (sourceFile is null)
        {
            return NotFound();
        }

        var stream = fileStorage.OpenRead(sourceFile.RelativePath);
        return File(stream, "application/octet-stream", sourceFile.OriginalFileName);
    }

    private static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Name is required.";
        }

        if (name.Length > MaxNameLength)
        {
            return $"Name must be {MaxNameLength} characters or fewer.";
        }

        return null;
    }

    private static string? ValidateVersionFields(decimal widthMm, decimal heightMm, string editorJson)
    {
        if (widthMm <= 0 || heightMm <= 0)
        {
            return "Width and height must be greater than zero.";
        }

        if (string.IsNullOrWhiteSpace(editorJson))
        {
            return "EditorJson is required.";
        }

        return null;
    }

    private static string SanitizeForFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string([.. name.Select(c => invalidChars.Contains(c) ? '-' : c)]).Trim();
        return sanitized.Length == 0 ? "template" : sanitized;
    }

    /// <summary>Null when the signed-in account may change template <paramref name="id"/>;
    /// otherwise 404 (it can't see it) or 403 (it's someone else's public template).</summary>
    private async Task<IActionResult?> DenyEditAsync(int id, CancellationToken cancellationToken) =>
        await templates.CheckEditAsync(id, cancellationToken) switch
        {
            TemplateEditCheck.NotFound => NotFound(),
            TemplateEditCheck.Forbidden => Problem(
                "Only the template's owner or an administrator can change it. Duplicate it to make your own copy.",
                statusCode: StatusCodes.Status403Forbidden),
            _ => null
        };
}
