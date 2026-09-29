using System.Net.Sockets;
using Tapeory.Api.Data.Entities;

using Tapeory.Api.Printing.Usb;

namespace Tapeory.Api.Printing;

/// <summary>Checks whether a printer's configured network address is reachable, by opening (and
/// immediately closing) a TCP connection. This is protocol-agnostic — it confirms something is
/// listening on that host:port, not that it's specifically a Brother printer or that it will
/// accept a print job correctly.</summary>
public sealed class PrinterConnectionTester(IppClient ipp, IUsbPrinterPort? usb = null)
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public async Task<ConnectionTestResult> TestAsync(
        Printer printer, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        if (printer.ConnectionType == PrinterConnectionType.Usb)
        {
            // Connected means the system lists it right now.
            return usb?.List().Any(found => found.Identifier == printer.UsbIdentifier) == true
                ? ConnectionTestResult.Success()
                : ConnectionTestResult.Failure(
                    $"{printer.UsbIdentifier} isn't connected. Check the USB cable and that the printer is switched on.");
        }

        var target = PrinterNetworkResolver.Resolve(printer);

        if (target is null)
        {
            return ConnectionTestResult.Failure("No address is configured for this printer.");
        }

        if (printer is { ConnectionType: PrinterConnectionType.PrintServer, QueueName: { } queue })
        {
            return await TestQueueAsync(IppClient.QueueUri(target.Host, target.Port, queue), cancellationToken, timeout);
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

    /// <summary>Asks the print server for the queue's status, which also proves the queue exists.</summary>
    private async Task<ConnectionTestResult> TestQueueAsync(
        Uri queueUri, CancellationToken cancellationToken, TimeSpan? timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var queue = await ipp.GetPrinterAttributesAsync(queueUri, linkedCts.Token);

            return queue.IsSuccess
                ? ConnectionTestResult.Success()
                : ConnectionTestResult.Failure(
                    $"The print server at {queueUri.Authority} says: {queue.Text("status-message") ?? "unknown error"}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ConnectionTestResult.Failure($"The print server at {queueUri.Authority} didn't answer in time.");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
        {
            return ConnectionTestResult.Failure($"Could not reach the print server at {queueUri.Authority}: {ex.Message}");
        }
    }
}
