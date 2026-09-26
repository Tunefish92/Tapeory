namespace Tapeory.Api.Data.Entities;

public sealed class TemplateField
{
    public int Id { get; set; }

    public int TemplateVersionId { get; set; }

    public TemplateVersion? TemplateVersion { get; set; }

    /// <summary>The key used in the template's dynamic field placeholder syntax, e.g. "customerName".</summary>
    public required string Name { get; set; }

    public string? Label { get; set; }

    public string? DefaultValue { get; set; }

    public bool Required { get; set; } = true;
}
