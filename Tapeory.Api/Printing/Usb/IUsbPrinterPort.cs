namespace Tapeory.Api.Printing.Usb;

/// <param name="Identifier">What a printer's USB identifier is set to: the device path on Linux
/// (/dev/usb/lp0), the printer's name in Windows.</param>
/// <param name="Name">For the printer form, e.g. "Brother PT-P750W".</param>
/// <param name="Model">The model the printer reports, when it does (e.g. "PT-P750W").</param>
public sealed record UsbPrinterInfo(string Identifier, string Name, string? Model);

/// <summary>
/// Printers connected to this computer by USB: listing them, and sending them raw Brother raster
/// data. Tape detection isn't available this way, so USB jobs count as done once sent.
/// </summary>
public interface IUsbPrinterPort
{
    IReadOnlyList<UsbPrinterInfo> List();

    Task<RawSendResult> SendAsync(string identifier, byte[] data, CancellationToken cancellationToken);

    // Windows (WindowsUsbPrinterPort) is switched off for now, see Tapeory.Api.csproj.
    public static IUsbPrinterPort ForThisSystem() =>
        OperatingSystem.IsLinux() ? new LinuxUsbPrinterPort() : new UnsupportedUsbPrinterPort();
}

internal sealed class UnsupportedUsbPrinterPort : IUsbPrinterPort
{
    public IReadOnlyList<UsbPrinterInfo> List() => [];

    public Task<RawSendResult> SendAsync(string identifier, byte[] data, CancellationToken cancellationToken) =>
        Task.FromResult(RawSendResult.Failure("USB printing is only available on Linux for now."));
}
