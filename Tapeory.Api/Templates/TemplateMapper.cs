using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Templates;

public static class TemplateMapper
{
    public static string[] ParseTags(string? tagsCsv) =>
        string.IsNullOrWhiteSpace(tagsCsv)
            ? []
            : tagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string? SerializeTags(string[]? tags) =>
        tags is null || tags.Length == 0
            ? null
            : string.Join(',', tags.Select(tag => tag.Trim()).Where(tag => tag.Length > 0));

    public static string? PreviewImageUrl(int? previewImageFileId) =>
        previewImageFileId is null ? null : $"/api/uploads/images/{previewImageFileId}";

    public static string? SourceLbxUrl(Template template) =>
        template.SourceLbxFileId is null ? null : $"/api/templates/{template.Id}/original-lbx";

    public static TemplateFieldDto ToDto(TemplateField field) =>
        new(field.Name, field.Label, field.DefaultValue, field.Required);

    public static TemplateVersionResponse ToVersionResponse(TemplateVersion version) => new(
        version.VersionNumber,
        version.WidthMm,
        version.HeightMm,
        version.EditorJson,
        [.. version.Fields.Select(ToDto)],
        PreviewImageUrl(version.PreviewImageFileId),
        version.CreatedAt);

    public static TemplateSummaryResponse ToSummary(Template template)
    {
        var current = template.CurrentVersion
            ?? throw new InvalidOperationException($"Template {template.Id} has no current version loaded.");

        return new TemplateSummaryResponse(
            template.Id,
            template.Name,
            template.Description,
            template.Category,
            ParseTags(template.TagsCsv),
            template.Status.ToString(),
            current.VersionNumber,
            current.WidthMm,
            current.HeightMm,
            PreviewImageUrl(current.PreviewImageFileId),
            SourceLbxUrl(template),
            template.CreatedAt,
            template.UpdatedAt);
    }

    public static TemplateDetailResponse ToDetail(Template template)
    {
        var current = template.CurrentVersion
            ?? throw new InvalidOperationException($"Template {template.Id} has no current version loaded.");

        return new TemplateDetailResponse(
            template.Id,
            template.Name,
            template.Description,
            template.Category,
            ParseTags(template.TagsCsv),
            template.Status.ToString(),
            SourceLbxUrl(template),
            [.. template.ConversionWarnings.Select(warning => warning.Message)],
            template.CreatedAt,
            template.UpdatedAt,
            ToVersionResponse(current));
    }

    public static NativeTemplateExport ToExport(Template template)
    {
        var current = template.CurrentVersion
            ?? throw new InvalidOperationException($"Template {template.Id} has no current version loaded.");

        return new NativeTemplateExport(
            1,
            template.Name,
            template.Description,
            template.Category,
            ParseTags(template.TagsCsv),
            current.WidthMm,
            current.HeightMm,
            current.EditorJson,
            [.. current.Fields.Select(ToDto)]);
    }
}
