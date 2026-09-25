using System.Net.Sockets;
using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Printing;

/// <summary>Checks whether a printer's configured network address is reachable, by opening (and
/// immediately closing) a TCP connection. This is protocol-agnostic — it confirms something is
/// listening on that host:port, not that it's specifically a Brother printer or that it will
/// accept a print job correctly.</summary>
public sealed class PrinterConnectionTester
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public async Task<ConnectionTestResult> TestAsync(
        Printer printer, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        if (printer.ConnectionType == PrinterConnectionType.Usb)
        {
            return ConnectionTestResult.Failure(
                "USB connection testing isn't supported yet — verify the printer is connected and powered on.");
        }

        var target = PrinterNetworkResolver.Resolve(printer);

        if (target is null)
        {
            return ConnectionTestResult.Failure("No address is configured for this printer.");
        }

        using var client = new TcpClient();
        using var timeoutCts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await client.ConnectAsync(target.Host, target.Port, linkedCts.Token);
            return ConnectionTestResult.Success();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ConnectionTestResult.Failure($"Connection to {target.Host}:{target.Port} timed out.");
        }
        catch (SocketException ex)
        {
            return ConnectionTestResult.Failure($"Could not connect to {target.Host}:{target.Port}: {ex.Message}");
        }
    }
}
