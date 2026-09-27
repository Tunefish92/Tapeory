using System.Text.Json;
using Tapeory.Api.Barcodes;
using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Printing;
using Tapeory.Api.Rendering;
using Tapeory.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Tapeory.Api.PrintJobs;

public sealed class PrintJobService(AppDbContext db, FileStorageService fileStorage, ILogger<PrintJobService> logger)
{
    public async Task<CreatePrintJobResult> CreateAsync(CreatePrintJobRequest request, CancellationToken cancellationToken)
    {
        var template = await db.Templates
            .Include(t => t.CurrentVersion!)
                .ThenInclude(version => version.Fields)
            .SingleOrDefaultAsync(t => t.Id == request.TemplateId && t.DeletedAt == null, cancellationToken);

        if (template?.CurrentVersion is null)
        {
            return CreatePrintJobResult.NotFound();
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return CreatePrintJobResult.Invalid(["At least one item is required."]);
        }

        var errors = new List<string>();
        var fields = template.CurrentVersion.Fields;
        // A document that can't be parsed isn't rejected here: the print queue fails that job with
        // its own message.
        RenderableDocument? document;
        try
        {
            document = LabelDocumentParser.Parse(template.CurrentVersion.EditorJson);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            document = null;
        }

        for (var i = 0; i < request.Items.Count; i++)
        {
            var item = request.Items[i];
            var values = item.FieldValues ?? [];
            var validation = FieldValueValidator.Validate(fields, values);

            if (!validation.IsValid)
            {
                errors.AddRange(validation.Errors.Select(error => $"Item {i + 1}: {error}"));
            }
            else if (document is not null)
            {
                // Every barcode must be able to encode the value it would print.
                var resolved = FieldValueValidator.ResolveValues(fields, values);
                errors.AddRange(BarcodeValidation.Validate(document, resolved).Select(error => $"Item {i + 1}: {error}"));
            }

            if (item.Quantity <= 0)
            {
                errors.Add($"Item {i + 1}: Quantity must be greater than zero.");
            }
        }

        Printer? printer = null;

        if (request.PrinterId is not null)
        {
            printer = await db.Printers.SingleOrDefaultAsync(p => p.Id == request.PrinterId, cancellationToken);

            if (printer is null)
            {
                errors.Add($"Printer {request.PrinterId} was not found.");
            }
        }

        var quality = PrintQuality.Standard;

        if (request.Quality is not null
            && (!Enum.TryParse(request.Quality, ignoreCase: true, out quality) || !Enum.IsDefined(quality)))
        {
            errors.Add($"Unknown print quality '{request.Quality}'.");
        }
        else if (printer is not null
                 && PrinterCapabilities.Resolutions(printer.Model).All(resolution => resolution.Quality != quality))
        {
            errors.Add($"Printer '{printer.Name}' doesn't support {quality} print quality.");
        }

        // Without a choice, the printer's first cutting option (auto cut, or cut marks on a
        // printer without a cutter).
        var cutMode = printer is null ? CutMode.AutoCut : PrinterCapabilities.CutModes(printer.Model)[0];

        if (request.CutMode is not null
            && (!Enum.TryParse(request.CutMode, ignoreCase: true, out cutMode) || !Enum.IsDefined(cutMode)))
        {
            errors.Add($"Unknown cut mode '{request.CutMode}'.");
        }
        else if (printer is not null && !PrinterCapabilities.CutModes(printer.Model).Contains(cutMode))
        {
            errors.Add($"Printer '{printer.Name}' doesn't support the {cutMode} cutting option.");
        }

        if (errors.Count > 0)
        {
            return CreatePrintJobResult.Invalid(errors);
        }

        var job = new PrintJob
        {
            TemplateId = template.Id,
            TemplateVersionNumber = template.CurrentVersion.VersionNumber,
            PrinterId = printer?.Id,
            PrinterName = printer?.Name ?? request.PrinterName,
            Quality = quality,
            CutMode = cutMode
        };

        foreach (var item in request.Items)
        {
            job.Items.Add(new PrintJobItem
            {
                FieldValuesJson = JsonSerializer.Serialize(item.FieldValues ?? []),
                Quantity = item.Quantity
            });
        }

        db.PrintJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        return CreatePrintJobResult.Created(job.Id);
    }

    public Task<PrintJob?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        PrintJobsWithDetails().SingleOrDefaultAsync(job => job.Id == id, cancellationToken);

    public async Task<List<PrintJob>> ListAsync(int? templateId, CancellationToken cancellationToken)
    {
        var query = PrintJobsWithDetails();

        if (templateId is not null)
        {
            query = query.Where(job => job.TemplateId == templateId);
        }

        return await query.OrderByDescending(job => job.CreatedAt).ToListAsync(cancellationToken);
    }

    /// <summary>Removes one job from the print history. See <see cref="PrintJob.DeletedAt"/> for
    /// why this is a soft delete.</summary>
    public async Task<DeletePrintJobResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var job = await PrintJobsWithRenderedFiles()
            .SingleOrDefaultAsync(j => j.Id == id, cancellationToken);

        if (job is null)
        {
            return DeletePrintJobResult.NotFound;
        }

        if (IsInProgress(job))
        {
            return DeletePrintJobResult.StillPrinting;
        }

        var filesToDelete = MarkDeleted([job]);
        await db.SaveChangesAsync(cancellationToken);
        DeleteFiles(filesToDelete);

        return DeletePrintJobResult.Deleted;
    }

    /// <summary>Clears the whole print history, except jobs still queued or printing.</summary>
    public async Task<DeleteAllPrintJobsResponse> DeleteAllAsync(CancellationToken cancellationToken)
    {
        var jobs = await PrintJobsWithRenderedFiles().ToListAsync(cancellationToken);
        var finished = jobs.Where(job => !IsInProgress(job)).ToList();

        var filesToDelete = MarkDeleted(finished);
        await db.SaveChangesAsync(cancellationToken);
        DeleteFiles(filesToDelete);

        return new DeleteAllPrintJobsResponse(finished.Count, jobs.Count - finished.Count);
    }

    // Queued jobs are about to be picked up by PrintJobProcessor, and Processing, Sending and
    // Printing ones are being worked on right now; changing them underneath it would race the
    // processor.
    private static bool IsInProgress(PrintJob job) =>
        job.Status is PrintJobStatus.Queued or PrintJobStatus.Processing
            or PrintJobStatus.Sending or PrintJobStatus.Printing;

    /// <summary>Hides the jobs from the history and drops what they printed (field values and
    /// rendered images), keeping only what the statistics need. Returns the image files to
    /// remove from disk once the database change is saved.</summary>
    private List<string> MarkDeleted(IEnumerable<PrintJob> jobs)
    {
        var now = DateTimeOffset.UtcNow;
        var files = new List<string>();

        foreach (var job in jobs)
        {
            job.DeletedAt = now;

            foreach (var item in job.Items)
            {
                item.FieldValuesJson = "{}";

                if (item.RenderedImageFile is { } rendered)
                {
                    files.Add(rendered.RelativePath);
                    item.RenderedImageFile = null;
                    db.UploadedFiles.Remove(rendered);
                }
            }
        }

        return files;
    }

    // Runs after SaveChanges, so a failure here only leaves an orphaned file behind — never a
    // history entry pointing at an image that's gone.
    private void DeleteFiles(List<string> relativePaths)
    {
        foreach (var path in relativePaths)
        {
            try
            {
                fileStorage.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not delete rendered print output {Path}.", path);
            }
        }
    }

    private IQueryable<PrintJob> PrintJobsWithRenderedFiles() =>
        db.PrintJobs
            .Where(job => job.DeletedAt == null)
            .Include(job => job.Items)
                .ThenInclude(item => item.RenderedImageFile);

    private IQueryable<PrintJob> PrintJobsWithDetails() =>
        db.PrintJobs
            .Where(job => job.DeletedAt == null)
            .Include(job => job.Template)
            .Include(job => job.Items);
}
