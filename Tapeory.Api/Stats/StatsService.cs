using System.Globalization;
using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Tapeory.Api.Stats;

public sealed record DashboardStatsResponse(
    int TemplateCount,
    int PrinterCount,
    int PrintJobCount,
    int CompletedPrintJobCount,
    int FailedPrintJobCount,
    int LabelsPrinted,
    decimal TotalPrintedLengthMm,
    decimal TotalPrintedAreaMm2,
    string? MostPrintedTemplateName,
    int MostPrintedTemplateCount,
    DateTimeOffset? LastPrintedAt,
    // Null = all-time statistics; otherwise only print jobs created at or after this moment count.
    DateTimeOffset? StatsSince);

public sealed class StatsService(AppDbContext db)
{
    /// <summary>ApplicationSettings key holding the moment the statistics were last reset.</summary>
    public const string ResetAtSettingKey = "stats.resetAt";

    public async Task<DashboardStatsResponse> GetDashboardStatsAsync(CancellationToken cancellationToken)
    {
        var since = await GetResetAtAsync(cancellationToken);

        // Template and printer counts describe what exists right now, so a reset doesn't touch
        // them. Everything print-related only counts jobs created since the last reset. Jobs
        // deleted from the print history still count: they're kept (without their field values)
        // for exactly this.
        var jobs = since is { } from ? db.PrintJobs.Where(j => j.CreatedAt >= from) : db.PrintJobs;

        var templateCount = await db.Templates.CountAsync(t => t.DeletedAt == null, cancellationToken);
        var printerCount = await db.Printers.CountAsync(cancellationToken);
        var printJobCount = await jobs.CountAsync(cancellationToken);
        var completedJobCount = await jobs.CountAsync(j => j.Status == PrintJobStatus.Completed, cancellationToken);
        var failedJobCount = await jobs.CountAsync(j => j.Status == PrintJobStatus.Failed, cancellationToken);
        var lastPrintedAt = await jobs
            .Where(j => j.Status == PrintJobStatus.Completed)
            .MaxAsync(j => (DateTimeOffset?)j.CompletedAt, cancellationToken);

        // Only successfully printed items count. Dimensions come from the exact template version
        // the job rendered, not the template's current version, so later edits don't rewrite history.
        var printed = await (
                from item in db.PrintJobItems
                join job in jobs on item.PrintJobId equals job.Id
                join version in db.TemplateVersions
                    on new { job.TemplateId, VersionNumber = job.TemplateVersionNumber }
                    equals new { version.TemplateId, version.VersionNumber }
                join template in db.Templates on job.TemplateId equals template.Id
                where item.Status == PrintJobStatus.Completed
                select new
                {
                    item.Quantity,
                    version.WidthMm,
                    version.HeightMm,
                    job.TemplateId,
                    TemplateName = template.Name
                })
            .ToListAsync(cancellationToken);

        var mostPrinted = printed
            .GroupBy(p => new { p.TemplateId, p.TemplateName })
            .Select(g => new { g.Key.TemplateName, Count = g.Sum(p => p.Quantity) })
            .OrderByDescending(g => g.Count)
            .FirstOrDefault();

        return new DashboardStatsResponse(
            templateCount,
            printerCount,
            printJobCount,
            completedJobCount,
            failedJobCount,
            LabelsPrinted: printed.Sum(p => p.Quantity),
            // Tape printers feed labels along their width, so width is the tape length consumed.
            TotalPrintedLengthMm: printed.Sum(p => p.Quantity * p.WidthMm),
            TotalPrintedAreaMm2: printed.Sum(p => p.Quantity * p.WidthMm * p.HeightMm),
            mostPrinted?.TemplateName,
            mostPrinted?.Count ?? 0,
            lastPrintedAt,
            since);
    }

    /// <summary>Starts counting from zero now. Non-destructive: print jobs are kept, so
    /// <see cref="RestoreAllTimeAsync"/> can bring the old totals back.</summary>
    public async Task<DashboardStatsResponse> ResetAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var value = now.ToString("O", CultureInfo.InvariantCulture);

        var setting = await db.ApplicationSettings.SingleOrDefaultAsync(s => s.Key == ResetAtSettingKey, cancellationToken);
        if (setting is null)
        {
            db.ApplicationSettings.Add(new ApplicationSetting { Key = ResetAtSettingKey, Value = value, UpdatedAt = now });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = now;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (setting is null)
        {
            // Another reset inserted the row first (unique key); both mean "reset now", so just
            // overwrite theirs with ours.
            db.ChangeTracker.Clear();
            await db.ApplicationSettings
                .Where(s => s.Key == ResetAtSettingKey)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Value, value).SetProperty(s => s.UpdatedAt, now), cancellationToken);
        }

        return await GetDashboardStatsAsync(cancellationToken);
    }

    /// <summary>Undoes a reset: statistics cover the whole print history again.</summary>
    public async Task<DashboardStatsResponse> RestoreAllTimeAsync(CancellationToken cancellationToken)
    {
        await db.ApplicationSettings.Where(s => s.Key == ResetAtSettingKey).ExecuteDeleteAsync(cancellationToken);
        return await GetDashboardStatsAsync(cancellationToken);
    }

    private async Task<DateTimeOffset?> GetResetAtAsync(CancellationToken cancellationToken)
    {
        var value = await db.ApplicationSettings
            .Where(s => s.Key == ResetAtSettingKey)
            .Select(s => s.Value)
            .SingleOrDefaultAsync(cancellationToken);

        // A hand-edited or corrupt value shouldn't take the dashboard down; fall back to all-time.
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}
