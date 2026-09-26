namespace Tapeory.Api.Templates;

public sealed record CreateTemplateRequest(
    string Name,
    string? Description,
    string? Category,
    string[]? Tags,
    decimal WidthMm,
    decimal HeightMm,
    string EditorJson,
    TemplateFieldDto[]? Fields);

public sealed record UpdateTemplateMetadataRequest(
    string Name,
    string? Description,
    string? Category,
    string[]? Tags,
    string? Status);

public sealed record CreateTemplateVersionRequest(
    decimal WidthMm,
    decimal HeightMm,
    string EditorJson,
    TemplateFieldDto[]? Fields);

public sealed record SetPreviewImageRequest(int UploadedFileId);

/// <summary>Format is "png" (default) or "pdf". FieldValues omitted or missing entries fall back
/// to each field's configured default.</summary>
public sealed record PreviewRequest(Dictionary<string, string>? FieldValues, string? Format);
