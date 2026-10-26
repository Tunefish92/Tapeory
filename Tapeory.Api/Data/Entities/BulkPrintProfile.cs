namespace Tapeory.Api.Data.Entities;

/// <summary>A saved bulk print setup for one template: the data file, how its columns fill the
/// label's fields, and the print settings. It belongs to the account that saved it.</summary>
public sealed class BulkPrintProfile
{
    public int Id { get; set; }

    public int TemplateId { get; set; }

    public Template? Template { get; set; }

    /// <summary>Null while Tapeory has no accounts (and in the desktop app's own database).</summary>
    public int? OwnerUserId { get; set; }

    public required string Name { get; set; }

    /// <summary>JSON-serialized <c>BulkPrintProfileSettings</c>.</summary>
    public required string SettingsJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
