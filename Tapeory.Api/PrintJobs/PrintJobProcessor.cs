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

namespace Tapeory.Api.PrintJobs;

/// <summary>
/// Polls for queued print jobs and renders each item to a PNG preview. When the job has an
/// enabled printer attached, it also prints each item (as many copies as its quantity) through
/// BrotherPrinterDriver, and records the stages as they happen: Sending while the data goes
/// out, Printing while the printer works, then Completed once the printer confirms the labels
/// came out, or Failed with the printer's reason. A job with no printer attached (or a disabled
/// one) just renders and stops there.
/// </summary>
public sealed class PrintJobProcessor(
    IServiceScopeFactory scopeFactory,
    DatabaseConfigStore databaseConfig,
    TapeoryInstance instance,
    ILogger<PrintJobProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

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
            var anyItemFailed = false;

            foreach (var item in job.Items)
            {
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

                    if (job.Printer is { Enabled: true })
                    {
                        var resolution = PrinterCapabilities.Resolve(job.Printer.Model, job.Quality);
                        var (label, media) = BrotherLabelRaster.Render(
                            renderer, document, resolvedValues, imageResolver.AsDelegate(),
                            BrotherCatalog.Find(job.Printer.Model), resolution);
                        using var _ = label;

                        var outcome = await driver.PrintAsync(
                            job.Printer,
                            Enumerable.Repeat(label, item.Quantity).ToList(),
                            media,
                            resolution,
                            job.CutMode,
                            async stage =>
                            {
                                var status = stage == PrintStage.Sending ? PrintJobStatus.Sending : PrintJobStatus.Printing;
                                item.Status = status;
                                job.Status = status;
                                await db.SaveChangesAsync(cancellationToken);
                            },
                            cancellationToken);

                        if (!outcome.IsSuccess)
                        {
                            // Rendering succeeded (and is kept, so the user can inspect/download
                            // it) even though printing failed.
                            anyItemFailed = true;
                            item.Status = PrintJobStatus.Failed;
                            item.ErrorMessage = $"Rendered successfully but printing failed: {outcome.ErrorMessage}";
                            continue;
                        }

                        if (!outcome.Confirmed)
                        {
                            item.ErrorMessage =
                                "Sent to the printer. It doesn't report its status, so Tapeory can't confirm the labels came out.";
                        }
                    }

                    item.Status = PrintJobStatus.Completed;
                }
                catch (Exception ex)
                {
                    anyItemFailed = true;
                    item.Status = PrintJobStatus.Failed;
                    item.ErrorMessage = "This item failed to render. Check the server logs for details.";
                    logger.LogError(ex, "Failed to render print job {JobId} item {ItemId}.", job.Id, item.Id);
                }
            }

            job.Status = anyItemFailed ? PrintJobStatus.Failed : PrintJobStatus.Completed;
            job.ErrorMessage = anyItemFailed ? "One or more items failed. See each item for details." : null;
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
