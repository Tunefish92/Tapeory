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
    bool Enabled);

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
    bool Enabled);

public sealed record PrinterResponse(
    int Id,
    string Name,
    string? Model,
    string ConnectionType,
    string? Address,
    int Port,
    string? PrintServerAddress,
    string? UsbIdentifier,
    decimal? LabelMediaWidthMm,
    decimal? LabelMediaHeightMm,
    bool IsDefault,
    bool Enabled,
    string LastConnectionStatus,
    DateTimeOffset? LastConnectionCheckedAt,
    string? LastErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TestConnectionResponse(bool IsSuccess, string? ErrorMessage);

public sealed record TestPrintResponse(bool IsSuccess, string? ErrorMessage);
