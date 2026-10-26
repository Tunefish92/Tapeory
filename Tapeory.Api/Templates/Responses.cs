namespace Tapeory.Api.Templates;

public sealed record TemplateVersionResponse(
    int VersionNumber,
    decimal WidthMm,
    decimal HeightMm,
    string EditorJson,
    TemplateFieldDto[] Fields,
    string? PreviewImageUrl,
    DateTimeOffset CreatedAt);

public sealed record TemplateSummaryResponse(
    int Id,
    string Name,
    string? Description,
    string? Category,
    string[] Tags,
    string Status,
    int CurrentVersionNumber,
    decimal WidthMm,
    decimal HeightMm,
    string? PreviewImageUrl,
    string? SourceLbxUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsPublic = true,
    // Null for a shared template from before accounts existed.
    string? OwnerName = null,
    bool CanEdit = true,
    // Owned by the signed-in account.
    bool IsMine = false);

public sealed record TemplateDetailResponse(
    int Id,
    string Name,
    string? Description,
    string? Category,
    string[] Tags,
    string Status,
    string? SourceLbxUrl,
    string[] ConversionWarnings,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    TemplateVersionResponse CurrentVersion,
    bool IsPublic = true,
    string? OwnerName = null,
    bool CanEdit = true,
    // Owned by the signed-in account.
    bool IsMine = false);

/// <summary>A template file in the first format (".tapeory.json"): the design as a JSON string and
/// no images. Still read on import; see <see cref="TemplateFile"/> for what is written now.</summary>
public sealed record NativeTemplateExport(
    int FormatVersion,
    string Name,
    string? Description,
    string? Category,
    string[] Tags,
    decimal WidthMm,
    decimal HeightMm,
    string EditorJson,
    TemplateFieldDto[] Fields);
