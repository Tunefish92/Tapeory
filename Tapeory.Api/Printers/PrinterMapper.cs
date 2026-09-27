using Tapeory.Api.Data.Entities;
using Tapeory.Api.Printing;

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
        printer.QueueName,
        printer.LabelMediaWidthMm,
        printer.LabelMediaHeightMm,
        printer.IsDefault,
        printer.Enabled,
        printer.LastConnectionStatus.ToString(),
        printer.LastConnectionCheckedAt,
        printer.LastErrorMessage,
        PrinterCapabilities.Resolutions(printer.Model)
            .Select(resolution => new PrintResolutionResponse(
                resolution.Quality.ToString(), resolution.HorizontalDpi, resolution.VerticalDpi))
            .ToList(),
        PrinterCapabilities.CutModes(printer.Model).Select(mode => mode.ToString()).ToList(),
        printer.CreatedAt,
        printer.UpdatedAt);
}
