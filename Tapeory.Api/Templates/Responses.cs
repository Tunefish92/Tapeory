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
    DateTimeOffset UpdatedAt);

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
    TemplateVersionResponse CurrentVersion);

/// <summary>The portable, native Tapeory template envelope used for export/import.</summary>
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
