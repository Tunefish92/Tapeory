using SkiaSharp;
using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Printing;

public enum PrintStage
{
    Sending,
    Printing
}

/// <param name="Confirmed">True when the printer's own status confirmed the labels came out;
/// false when it doesn't report status and the job only got as far as being sent.</param>
public sealed record PrintOutcome(bool IsSuccess, string? ErrorMessage, bool Confirmed)
{
    public static PrintOutcome Printed() => new(true, null, true);

    public static PrintOutcome SentUnconfirmed() => new(true, null, false);

    public static PrintOutcome Failure(string errorMessage) => new(false, errorMessage, false);
}

/// <summary>
/// Prints on Brother PT label printers with a 128-pin, 180 dpi head (PT-P750W, PT-E550W,
/// PT-P710BT and relatives), as Brother raster data.
///
/// Directly connected printers get the job on their raw port; their SNMP status then shows when
/// they start printing, whether they stop on an error such as no tape or an open cover, and, from
/// the label counter, when the labels have really come out. A print server with a queue name gets
/// it over IPP (CUPS) as a raw document that its driver passes through untouched, and the job's
/// state on the server tells when it's done.
/// </summary>
public sealed class BrotherPtPrinterDriver(
    PrinterRawSocketSender sender,
    IPrinterStatusReader statusReader,
    IppClient ipp,
    ILogger<BrotherPtPrinterDriver> logger)
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How long to wait for the printer to finish: a base allowance plus a little per
    /// label, since the PT-P750W prints at about 30 mm/s (slower in high resolution).</summary>
    public TimeSpan BaseTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan TimeoutPerLabel { get; init; } = TimeSpan.FromSeconds(3);

    public async Task<PrintOutcome> PrintAsync(
        Printer printer,
        IReadOnlyList<SKBitmap> labels,
        BrotherPtTape tape,
        PrintResolution resolution,
        CutMode cutMode,
        Func<PrintStage, Task> onStage,
        CancellationToken cancellationToken)
    {
        if (printer.ConnectionType == PrinterConnectionType.Usb)
        {
            return PrintOutcome.Failure("Printing to USB printers isn't supported yet.");
        }

        if (printer.Model?.Contains("QL", StringComparison.OrdinalIgnoreCase) == true)
        {
            return PrintOutcome.Failure("Brother QL printers aren't supported yet; Tapeory prints on P-touch (PT) models.");
        }

        var target = PrinterNetworkResolver.Resolve(printer);

        if (target is null)
        {
            return PrintOutcome.Failure("No address is configured for this printer.");
        }

        var data = BrotherPtRasterEncoder.Encode(labels, tape, resolution.Quality == PrintQuality.High, cutMode);

        if (printer is { ConnectionType: PrinterConnectionType.PrintServer, QueueName: { } queue })
        {
            var queueUri = IppClient.QueueUri(target.Host, target.Port, queue);
            return await PrintViaIppAsync(queueUri, data, labels.Count, onStage, cancellationToken);
        }

        var before = await statusReader.ReadAsync(target.Host, cancellationToken);

        if (before?.BlockingError is { } problem)
        {
            return PrintOutcome.Failure($"The printer reports a problem: {problem}.");
        }

        await onStage(PrintStage.Sending);
        var sent = await sender.SendAsync(printer, data, cancellationToken);

        if (!sent.IsSuccess)
        {
            return PrintOutcome.Failure(sent.ErrorMessage ?? "Could not send to the printer.");
        }

        if (before is null)
        {
            logger.LogInformation(
                "Printer {Host} doesn't report SNMP status; the job was sent but can't be confirmed.", target.Host);
            return PrintOutcome.SentUnconfirmed();
        }

        await onStage(PrintStage.Printing);
        return await WaitUntilPrintedAsync(target.Host, before, labels.Count, tape, cancellationToken);
    }

    private async Task<PrintOutcome> PrintViaIppAsync(
        Uri queueUri, byte[] data, int labelCount, Func<PrintStage, Task> onStage, CancellationToken cancellationToken)
    {
        try
        {
            var queue = await ipp.GetPrinterAttributesAsync(queueUri, cancellationToken);

            if (!queue.IsSuccess)
            {
                return PrintOutcome.Failure(
                    $"The print server refused the queue: {queue.Text("status-message") ?? "unknown error"}.");
            }

            if (queue.Integer("printer-state") == IppPrinterStopped
                || queue.Attributes.GetValueOrDefault("printer-is-accepting-jobs") is [false])
            {
                return PrintOutcome.Failure(
                    $"The print server's queue is stopped or not accepting jobs: {DescribeQueue(queue)}.");
            }

            await onStage(PrintStage.Sending);
            var submitted = await ipp.PrintJobAsync(queueUri, "Tapeory label", data, cancellationToken);

            if (!submitted.IsSuccess || submitted.Integer("job-id") is not { } jobId)
            {
                return PrintOutcome.Failure(
                    $"The print server rejected the job: {submitted.Text("status-message") ?? "unknown error"}.");
            }

            await onStage(PrintStage.Printing);
            var deadline = DateTimeOffset.UtcNow + BaseTimeout + TimeoutPerLabel * labelCount;

            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(PollInterval, cancellationToken);
                var job = await ipp.GetJobAttributesAsync(queueUri, jobId, cancellationToken);
                var message = job.Text("job-state-message") is { Length: > 0 } text
                    ? text
                    : string.Join(", ", job.Texts("job-state-reasons"));

                switch (job.Integer("job-state"))
                {
                    case IppJobCompleted:
                        return PrintOutcome.Printed();
                    case IppJobCanceled or IppJobAborted:
                        return PrintOutcome.Failure($"The print server stopped the job: {message}.");
                    case IppJobStopped:
                        return PrintOutcome.Failure($"The print server's printer stopped: {message}.");
                }
            }

            // Don't leave it queued: it would print whenever the server recovers, and again if
            // the user retries.
            var stuck = await ipp.GetPrinterAttributesAsync(queueUri, cancellationToken);
            await ipp.CancelJobAsync(queueUri, jobId, cancellationToken);

            return PrintOutcome.Failure(
                $"The print server couldn't print the job, so Tapeory cancelled it. It reports: {DescribeQueue(stuck)}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException
                                       or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return PrintOutcome.Failure($"Could not reach the print server at {queueUri.Authority}: {ex.Message}");
        }
    }

    private static string DescribeQueue(IppResponse queue) =>
        queue.Text("printer-state-message") is { Length: > 0 } message
            ? message
            : string.Join(", ", queue.Texts("printer-state-reasons"));

    // IPP printer-state and job-state values (RFC 8011).
    private const int IppPrinterStopped = 5;
    private const int IppJobStopped = 6;
    private const int IppJobCanceled = 7;
    private const int IppJobAborted = 8;
    private const int IppJobCompleted = 9;

    private async Task<PrintOutcome> WaitUntilPrintedAsync(
        string host, PrinterStatusSnapshot before, int labelCount, BrotherPtTape tape, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + BaseTimeout + TimeoutPerLabel * labelCount;
        var seenPrinting = false;
        PrinterStatusSnapshot? last = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, cancellationToken);
            var now = await statusReader.ReadAsync(host, cancellationToken);

            if (now is null)
            {
                continue; // busy printers sometimes skip a reply
            }

            last = now;

            if (now.BlockingError is { } problem)
            {
                return PrintOutcome.Failure($"The printer stopped: {problem}.");
            }

            seenPrinting |= now.IsPrinting;

            var finished = before.LabelCount is { } startCount && now.LabelCount is { } count
                ? count - startCount >= labelCount
                : seenPrinting; // no counter: settle for "it printed and went idle again"

            if (finished && !now.IsPrinting)
            {
                return PrintOutcome.Printed();
            }
        }

        var display = last?.Display.Trim();
        return PrintOutcome.Failure(
            $"The printer accepted the job but didn't finish printing it. Check that {tape.WidthMm} mm tape is loaded"
            + (string.IsNullOrEmpty(display) ? "." : $" (printer display: {display})."));
    }
}
