namespace Tapeory.Api.Data.Entities;

public sealed class Printer
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public string? Model { get; set; }

    public PrinterConnectionType ConnectionType { get; set; }

    public string? Address { get; set; }

    public int Port { get; set; }

    public string? PrintServerAddress { get; set; }

    public string? UsbIdentifier { get; set; }

    public decimal? LabelMediaWidthMm { get; set; }

    public decimal? LabelMediaHeightMm { get; set; }

    public bool IsDefault { get; set; }

    public bool Enabled { get; set; }

    public PrinterConnectionStatus LastConnectionStatus { get; set; } = PrinterConnectionStatus.Unknown;

    public DateTimeOffset? LastConnectionCheckedAt { get; set; }

    public string? LastErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
