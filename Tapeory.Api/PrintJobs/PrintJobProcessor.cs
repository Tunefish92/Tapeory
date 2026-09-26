using System.Text.Json;
using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Printing;
using Tapeory.Api.Rendering;
using Tapeory.Api.Setup;
using Tapeory.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Tapeory.Api.PrintJobs;

/// <summary>
/// Polls for queued print jobs, renders each item to PNG, and — when the job has an enabled
/// printer attached — sends the rendered bytes over a raw socket. This is the "Tapeory template
/// provider" from the project's printer-provider abstraction (renders native templates, replaces
/// dynamic fields, produces printer-compatible output). A job with no printer attached (or a
/// disabled one) just renders and stops there, same as before printer management existed.
///
/// See PrinterRawSocketSender's remarks: the socket transport is verified, but whether a real
/// Brother printer interprets the bytes it receives as a valid print job is not — that would
/// need a proper Brother command encoder tested against actual hardware (the "Brother
/// compatibility provider" from the project plan), which is a separate, more involved piece of
/// work this phase does not attempt.
/// </summary>
public sealed class PrintJobProcessor(
    IServiceScopeFactory scopeFactory,
    DatabaseConfigStore databaseConfig,
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
        var rawSender = scope.ServiceProvider.GetRequiredService<PrinterRawSocketSender>();

        var job = await db.PrintJobs
            .Include(j => j.Template!)
                .ThenInclude(template => template.Versions)
                    .ThenInclude(version => version.Fields)
            .Include(j => j.Items)
            .Include(j => j.Printer)
            .Where(j => j.Status == PrintJobStatus.Queued)
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return false;
        }

        // Assumes a single API instance/replica. Multiple instances polling the same database
        // could both pick up this job before either commits the Processing status below — there
        // is no row-level locking here. Fine for the single-container deployment this project
        // ships today; would need addressing before running more than one API replica.
        job.Status = PrintJobStatus.Processing;
        await db.SaveChangesAsync(cancellationToken);

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
                        var sendResult = await rawSender.SendAsync(job.Printer, pngBytes, cancellationToken);

                        if (!sendResult.IsSuccess)
                        {
                            // Rendering succeeded (and is kept, so the user can inspect/download
                            // it) even though delivery to the printer failed.
                            anyItemFailed = true;
                            item.Status = PrintJobStatus.Failed;
                            item.ErrorMessage = $"Rendered successfully but could not send to the printer: {sendResult.ErrorMessage}";
                            continue;
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
            job.ErrorMessage = anyItemFailed ? "One or more items failed to render." : null;
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
