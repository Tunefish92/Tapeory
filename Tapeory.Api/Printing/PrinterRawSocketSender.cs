using System.Net.Sockets;
using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Printing;

/// <summary>
/// Sends raw bytes to a printer's network address over a plain TCP socket — the conventional
/// "raw"/JetDirect-style printing port (commonly 9100). It's only the transport: what the bytes
/// mean is up to the caller (BrotherPtPrinterDriver sends Brother raster data through it).
/// </summary>
public sealed class PrinterRawSocketSender
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public async Task<RawSendResult> SendAsync(
        Printer printer, byte[] data, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        if (printer.ConnectionType == PrinterConnectionType.Usb)
        {
            return RawSendResult.Failure("Sending to USB printers isn't supported yet.");
        }

        var target = PrinterNetworkResolver.Resolve(printer);

        if (target is null)
        {
            return RawSendResult.Failure("No address is configured for this printer.");
        }

        using var client = new TcpClient();
        using var timeoutCts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await client.ConnectAsync(target.Host, target.Port, linkedCts.Token);

            var stream = client.GetStream();
            await stream.WriteAsync(data, linkedCts.Token);
            await stream.FlushAsync(linkedCts.Token);

            return RawSendResult.Success();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RawSendResult.Failure($"Sending to {target.Host}:{target.Port} timed out.");
        }
        catch (SocketException ex)
        {
            return RawSendResult.Failure($"Could not send to {target.Host}:{target.Port}: {ex.Message}");
        }
        catch (IOException ex)
        {
            return RawSendResult.Failure($"Could not send to {target.Host}:{target.Port}: {ex.Message}");
        }
    }
}
