namespace Tapeory.Api.Templates;

public sealed record TemplateFieldDto(string Name, string? Label, string? DefaultValue, bool Required);
