namespace Tapeory.Api.Data.Entities;

/// <summary>A note about an object from an imported source file (e.g. .lbx) that couldn't be
/// converted, so the user can see what to check or recreate manually.</summary>
public sealed class TemplateConversionWarning
{
    public int Id { get; set; }

    public int TemplateId { get; set; }

    public Template? Template { get; set; }

    public required string Message { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
