using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Printers;

public static class PrinterMapper
{
    public static PrinterResponse ToResponse(Printer printer) => new(
        printer.Id,
        printer.Name,
        printer.Model,
        printer.ConnectionType.ToString(),
        printer.Address,
        printer.Port,
        printer.PrintServerAddress,
        printer.UsbIdentifier,
        printer.LabelMediaWidthMm,
        printer.LabelMediaHeightMm,
        printer.IsDefault,
        printer.Enabled,
        printer.LastConnectionStatus.ToString(),
        printer.LastConnectionCheckedAt,
        printer.LastErrorMessage,
        printer.CreatedAt,
        printer.UpdatedAt);
}
