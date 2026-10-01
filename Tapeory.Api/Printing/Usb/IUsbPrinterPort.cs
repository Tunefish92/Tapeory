namespace Tapeory.Api.Printing.Usb;

/// <param name="Identifier">What a printer's USB identifier is set to: the device path on Linux
/// (/dev/usb/lp0), the printer's name in Windows.</param>
/// <param name="Name">For the printer form, e.g. "Brother PT-P750W".</param>
/// <param name="Model">The model the printer reports, when it does (e.g. "PT-P750W").</param>
public sealed record UsbPrinterInfo(string Identifier, string Name, string? Model);

/// <param name="Confirmed">The printer itself reported every label as printed.</param>
public sealed record UsbPrintResult(bool IsSuccess, bool Confirmed, string? ErrorMessage)
{
    public static UsbPrintResult Printed() => new(true, true, null);

    public static UsbPrintResult Sent() => new(true, false, null);

    public static UsbPrintResult Failure(string errorMessage) => new(false, false, errorMessage);
}

/// <summary>
/// Printers connected to this computer by USB: listing them, and sending them raw Brother raster
/// data. Tape detection isn't available this way, so USB jobs count as done once sent.
/// </summary>
public interface IUsbPrinterPort
{
    IReadOnlyList<UsbPrinterInfo> List();

    Task<RawSendResult> SendAsync(string identifier, byte[] data, CancellationToken cancellationToken);

    /// <summary>The printer's own status (loaded tape, errors), asked over the USB connection; null
    /// where it can't be asked or doesn't answer.</summary>
    Task<PrinterStatusSnapshot?> ReadStatusAsync(string identifier, CancellationToken cancellationToken) =>
        Task.FromResult<PrinterStatusSnapshot?>(null);

    /// <summary>Sends a print job of <paramref name="labelCount"/> labels and, where the printer
    /// reports back, follows it until they're out or it stops with an error.</summary>
    async Task<UsbPrintResult> PrintAsync(string identifier, byte[] data, int labelCount, CancellationToken cancellationToken)
    {
        var sent = await SendAsync(identifier, data, cancellationToken);
        return sent.IsSuccess ? UsbPrintResult.Sent() : UsbPrintResult.Failure(sent.ErrorMessage ?? "Could not send to the printer.");
    }

    /// <summary>Why this printer can't be written to (e.g. no permission), or null if it can; nothing
    /// is sent. For the connection test, so it doesn't say "connected" to a printer that then
    /// refuses every job.</summary>
    string? WriteProblem(string identifier) => null;

    public static IUsbPrinterPort ForThisSystem() =>
        OperatingSystem.IsWindows() ? new WindowsUsbPrinterPort()
        : OperatingSystem.IsLinux() ? new LinuxUsbPrinterPort()
        : new UnsupportedUsbPrinterPort();
}

internal sealed class UnsupportedUsbPrinterPort : IUsbPrinterPort
{
    public IReadOnlyList<UsbPrinterInfo> List() => [];

    public Task<RawSendResult> SendAsync(string identifier, byte[] data, CancellationToken cancellationToken) =>
        Task.FromResult(RawSendResult.Failure("USB printing is available on Windows and Linux."));
}
