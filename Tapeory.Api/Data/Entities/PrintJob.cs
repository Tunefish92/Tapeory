namespace Tapeory.Api.Data.Entities;

public sealed class PrintJob
{
    public int Id { get; set; }

    public int TemplateId { get; set; }

    public Template? Template { get; set; }

    /// <summary>The version rendered at the time this job was queued, so editing the template
    /// afterward can't change what an already-queued job prints.</summary>
    public int TemplateVersionNumber { get; set; }

    public int? PrinterId { get; set; }

    public Printer? Printer { get; set; }

    /// <summary>Snapshotted from the selected Printer at submission time (or typed directly when
    /// no PrinterId is given — e.g. no printers are configured yet, or a manual/offline print),
    /// so a job's record of which printer it went to survives that printer later being renamed or
    /// deleted.</summary>
    public string? PrinterName { get; set; }

    /// <summary>The account that submitted the job; null when Tapeory had no accounts yet, or the
    /// account was deleted since.</summary>
    public int? PrintedByUserId { get; set; }

    public User? PrintedByUser { get; set; }

    /// <summary>Snapshotted display name of <see cref="PrintedByUser"/>, so the history keeps it
    /// after the account is renamed or deleted.</summary>
    public string? PrintedByName { get; set; }

    public PrintJobStatus Status { get; set; } = PrintJobStatus.Queued;

    public PrintQuality Quality { get; set; } = PrintQuality.Standard;

    public CutMode CutMode { get; set; } = CutMode.AutoCut;

    public string? ErrorMessage { get; set; }

    public List<PrintJobItem> Items { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Set when the user deletes the job from the print history. The row is kept (with
    /// its field values wiped and rendered images removed) only so the print statistics keep
    /// counting it; everywhere else it no longer exists.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
