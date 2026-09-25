namespace Tapeory.Api.Data.Entities;

public sealed class ApplicationSetting
{
    public int Id { get; set; }

    public required string Key { get; set; }

    public string? Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
