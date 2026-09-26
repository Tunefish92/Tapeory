using System.Net.Sockets;
using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Printing;

/// <summary>
/// Sends raw bytes to a printer's network address over a plain TCP socket — the conventional
/// "raw"/JetDirect-style printing port (commonly 9100) that many network printers accept
/// unformatted data on.
///
/// What's actually verified here is the socket mechanics: connect, write, timeout, and error
/// handling are unit tested against a real local TCP listener. What is NOT verified — and cannot
/// be, without access to real hardware — is whether a genuine Brother printer interprets the
/// bytes it receives as a valid print job. Brother printers generally expect their own
/// raster/command protocol rather than a bare image, so this should be treated as an
/// experimental transport, not confirmed Brother-compatible output. Real compatibility (the
/// "Brother compatibility provider" from the project plan) is a separate, more involved piece of
/// work that needs testing against actual target models.
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
