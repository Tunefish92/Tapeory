namespace Tapeory.Api.Printers;

public sealed record CreatePrinterRequest(
    string Name,
    string? Model,
    string ConnectionType,
    string? Address,
    int? Port,
    string? PrintServerAddress,
    string? UsbIdentifier,
    decimal? LabelMediaWidthMm,
    decimal? LabelMediaHeightMm,
    bool Enabled,
    string? QueueName = null);

public sealed record UpdatePrinterRequest(
    string Name,
    string? Model,
    string ConnectionType,
    string? Address,
    int? Port,
    string? PrintServerAddress,
    string? UsbIdentifier,
    decimal? LabelMediaWidthMm,
    decimal? LabelMediaHeightMm,
    bool Enabled,
    string? QueueName = null);

public sealed record PrinterResponse(
    int Id,
    string Name,
    string? Model,
    string ConnectionType,
    string? Address,
    int Port,
    string? PrintServerAddress,
    string? UsbIdentifier,
    string? QueueName,
    decimal? LabelMediaWidthMm,
    decimal? LabelMediaHeightMm,
    bool IsDefault,
    bool Enabled,
    string LastConnectionStatus,
    DateTimeOffset? LastConnectionCheckedAt,
    string? LastErrorMessage,
    // The print resolutions this model supports, as Brother documents them; always at least Standard.
    List<PrintResolutionResponse> Resolutions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PrintResolutionResponse(string Quality, int HorizontalDpi, int VerticalDpi);

/// <param name="LoadedTapeMm">The tape the printer reports loaded, when it says (directly
/// connected printers with SNMP).</param>
public sealed record TestConnectionResponse(bool IsSuccess, string? ErrorMessage, decimal? LoadedTapeMm = null);

/// <summary>What a directly connected printer reports right now. Everything is null when it
/// can't be asked (USB, print servers) or doesn't answer SNMP.</summary>
public sealed record PrinterStatusResponse(bool Available, decimal? LoadedTapeMm, string? Display, string? Problem);

public sealed record TestPrintResponse(bool IsSuccess, string? ErrorMessage);
