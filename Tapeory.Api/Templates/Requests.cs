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

/// <summary>Name is the copy's name; omitted, it's the original's name plus " (copy)".</summary>
public sealed record DuplicateTemplateRequest(string? Name);

/// <summary>Format is "png" (default) or "pdf". FieldValues omitted or missing entries fall back
/// to each field's configured default.</summary>
public sealed record PreviewRequest(Dictionary<string, string>? FieldValues, string? Format);
