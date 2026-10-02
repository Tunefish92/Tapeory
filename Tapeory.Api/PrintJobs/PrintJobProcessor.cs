using System.Text.Json;
using Tapeory.Api.Barcodes;
using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Instances;
using Tapeory.Api.Printing;
using Tapeory.Api.Rendering;
using Tapeory.Api.Setup;
using Tapeory.Api.Storage;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Tapeory.Api.PrintJobs;

/// <summary>
/// Polls for queued print jobs and renders each item to a PNG preview. When the job has an
/// enabled printer attached, it also prints each item (as many copies as its quantity) through
/// BrotherPrinterDriver, and records the stages as they happen: Sending while the data goes
/// out, Printing while the printer works, then Completed once the printer confirms the labels
/// came out, or Failed with the printer's reason. A job with many items goes to the printer in
/// batches; after a failed batch, or when the user stops the job, the rest isn't sent and is
/// marked Cancelled. A job with no printer attached (or a disabled one) just renders and stops
/// there.
/// </summary>
public sealed class PrintJobProcessor(
    IServiceScopeFactory scopeFactory,
    DatabaseConfigStore databaseConfig,
    TapeoryInstance instance,
    PrintJobCancellations cancellations,
    ILogger<PrintJobProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>How many labels go to the printer in one transmission.</summary>
    public const int LabelsPerBatch = 25;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedAJob = await ProcessNextJobAsync(stoppingToken);

                if (processedAJob)
                {
                    continue; // immediately look for the next queued job rather than waiting
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unexpected error while processing print jobs.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    /// <returns>true if a job was found and processed (whether it succeeded or failed).</returns>
    private async Task<bool> ProcessNextJobAsync(CancellationToken cancellationToken)
    {
        if (!databaseConfig.IsConfigured)
        {
            return false; // first-run setup not done yet — nothing to poll
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var renderer = scope.ServiceProvider.GetRequiredService<LabelRenderer>();
        var fileStorage = scope.ServiceProvider.GetRequiredService<FileStorageService>();
        var imageResolver = scope.ServiceProvider.GetRequiredService<UploadedFileImageResolver>();
        var driver = scope.ServiceProvider.GetRequiredService<BrotherPrinterDriver>();

        // Only this installation's jobs (and old ones from before installations had ids): when the
        // desktop app and a server share a database, each prints what was submitted to it.
        var nextJobId = await db.PrintJobs
            .Where(j => j.Status == PrintJobStatus.Queued && (j.InstanceId == null || j.InstanceId == instance.Id))
            .OrderBy(j => j.CreatedAt)
            .Select(j => (int?)j.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (nextJobId is null)
        {
            return false;
        }

        // Claim the job with one conditional UPDATE, so that when several instances poll the same
        // database only the one whose update changes the row prints it; the others move on.
        var claimed = await db.PrintJobs
            .Where(j => j.Id == nextJobId && j.Status == PrintJobStatus.Queued)
            .ExecuteUpdateAsync(setters => setters.SetProperty(j => j.Status, PrintJobStatus.Processing), cancellationToken);

        if (claimed == 0)
        {
            return true; // another instance got there first; look for the next job
        }

        var job = await db.PrintJobs
            .Include(j => j.Template!)
                .ThenInclude(template => template.Versions)
                    .ThenInclude(version => version.Fields)
            .Include(j => j.Items)
            .Include(j => j.Printer)
            .SingleAsync(j => j.Id == nextJobId, cancellationToken);

        var version = job.Template?.Versions.SingleOrDefault(v => v.VersionNumber == job.TemplateVersionNumber);

        if (version is null)
        {
            FailJob(job, $"Template version {job.TemplateVersionNumber} no longer exists.");
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        try
        {
            // The version's own WidthMm/HeightMm columns are authoritative (validated
            // separately on every save); whatever's embedded in editorJson is not trusted for
            // this.
            var document = LabelDocumentParser.Parse(version.EditorJson) with
            {
                WidthMm = version.WidthMm,
                HeightMm = version.HeightMm
            };
            var printing = job.Printer is { Enabled: true };
            var resolution = printing ? PrinterCapabilities.Resolve(job.Printer!.Model, job.Quality) : null;
            var items = job.Items.OrderBy(item => item.Id).ToList();
            var anyItemFailed = false;
            var stoppedByUser = false;
            var stoppedByError = false;
            var next = 0;

            // Rows go to the printer in batches: one transmission and one confirmation per batch
            // instead of per label, and progress is saved after each one.
            while (next < items.Count && !stoppedByError)
            {
                if (cancellations.IsRequested(job.Id))
                {
                    stoppedByUser = true;
                    break;
                }

                var batch = new List<PrintJobItem>();
                var labels = new List<SKBitmap>();
                var bitmaps = new List<SKBitmap>();
                BrotherMedia? media = null;

                try
                {
                    while (next < items.Count && (labels.Count == 0 || labels.Count + items[next].Quantity <= LabelsPerBatch) && batch.Count < LabelsPerBatch)
                    {
                        var item = items[next++];

                        try
                        {
                            var submitted = JsonSerializer.Deserialize<Dictionary<string, string>>(item.FieldValuesJson) ?? [];
                            var resolvedValues = FieldValueValidator.ResolveValues(version.Fields, submitted);

                            if (BarcodeValidation.Validate(document, resolvedValues) is [_, ..] barcodeErrors)
                            {
                                anyItemFailed = true;
                                item.Status = PrintJobStatus.Failed;
                                item.ErrorMessage = string.Join(" ", barcodeErrors);
                                continue;
                            }
                            var pngBytes = renderer.RenderPng(document, resolvedValues, imageResolver.AsDelegate());

                            await using var pngStream = new MemoryStream(pngBytes);
                            var stored = await fileStorage.SaveAsync(
                                pngStream, $"print-job-{job.Id}-item-{item.Id}.png", FileStorageCategory.PrintJobOutput, cancellationToken);

                            var uploadedFile = new UploadedFile
                            {
                                FileName = stored.FileName,
                                OriginalFileName = stored.FileName,
                                ContentType = "image/png",
                                SizeBytes = stored.SizeBytes,
                                Category = FileStorageCategory.PrintJobOutput,
                                RelativePath = stored.RelativePath
                            };

                            db.UploadedFiles.Add(uploadedFile);
                            item.RenderedImageFile = uploadedFile;

                            if (!printing)
                            {
                                item.Status = PrintJobStatus.Completed;
                                batch.Add(item);
                                continue;
                            }

                            var (label, labelMedia) = BrotherLabelRaster.Render(
                                renderer, document, resolvedValues, imageResolver.AsDelegate(),
                                BrotherCatalog.Find(job.Printer!.Model), resolution!);
                            bitmaps.Add(label);
                            media = labelMedia;
                            labels.AddRange(Enumerable.Repeat(label, item.Quantity));
                            batch.Add(item);
                        }
                        catch (Exception ex)
                        {
                            anyItemFailed = true;
                            item.Status = PrintJobStatus.Failed;
                            item.ErrorMessage = "This item failed to render. Check the server logs for details.";
                            logger.LogError(ex, "Failed to render print job {JobId} item {ItemId}.", job.Id, item.Id);
                        }
                    }

                    if (printing && labels.Count > 0)
                    {
                        var outcome = await driver.PrintAsync(
                            job.Printer!,
                            labels,
                            media!,
                            resolution!,
                            job.CutMode,
                            async stage =>
                            {
                                var status = stage == PrintStage.Sending ? PrintJobStatus.Sending : PrintJobStatus.Printing;
                                batch.ForEach(item => item.Status = status);
                                job.Status = status;
                                await db.SaveChangesAsync(cancellationToken);
                            },
                            cancellationToken);

                        foreach (var item in batch)
                        {
                            if (!outcome.IsSuccess)
                            {
                                // Rendering succeeded (and is kept, so the user can inspect/download
                                // it) even though printing failed.
                                item.Status = PrintJobStatus.Failed;
                                item.ErrorMessage = $"Rendered successfully but printing failed: {outcome.ErrorMessage}";
                                continue;
                            }

                            item.Status = PrintJobStatus.Completed;
                            item.ErrorMessage = outcome.Confirmed
                                ? null
                                : "Sent to the printer. It doesn't report its status, so Tapeory can't confirm the labels came out.";
                        }

                        if (!outcome.IsSuccess)
                        {
                            // The printer needs attention; sending the rest would only fail again
                            // (or print onto the wrong tape).
                            anyItemFailed = true;
                            stoppedByError = true;
                        }
                    }
                }
                finally
                {
                    bitmaps.ForEach(bitmap => bitmap.Dispose());
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            foreach (var item in items.Skip(next))
            {
                item.Status = PrintJobStatus.Cancelled;
                item.ErrorMessage = stoppedByUser
                    ? "Not printed: the job was stopped."
                    : "Not printed: the job stopped after an earlier label failed.";
            }

            job.Status = anyItemFailed ? PrintJobStatus.Failed : stoppedByUser ? PrintJobStatus.Cancelled : PrintJobStatus.Completed;
            job.ErrorMessage = anyItemFailed
                ? next < items.Count
                    ? "A label failed, and the rest of the job wasn't printed. See each item for details."
                    : "One or more items failed. See each item for details."
                : stoppedByUser ? "The job was stopped before all labels were printed." : null;
            job.CompletedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            // Anything unexpected here (most plausibly a malformed editorJson) must still leave
            // the job in a terminal state — otherwise it's stuck "Processing" forever with no
            // way for the caller to know why. The specific exception is logged server-side only;
            // it's an internal detail (e.g. a raw JSON parser message) that isn't actionable for
            // whoever submitted the print job.
            FailJob(job, "Failed to render this template. Check the server logs for details.");
            logger.LogError(ex, "Failed to process print job {JobId}.", job.Id);
        }

        cancellations.Clear(job.Id);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static void FailJob(PrintJob job, string message)
    {
        job.Status = PrintJobStatus.Failed;
        job.ErrorMessage = message;
        job.CompletedAt = DateTimeOffset.UtcNow;

        foreach (var item in job.Items)
        {
            item.Status = PrintJobStatus.Failed;
            item.ErrorMessage = message;
        }
    }
}
