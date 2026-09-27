namespace Tapeory.Api.PrintJobs;

public sealed record PrintJobItemRequest(Dictionary<string, string>? FieldValues, int Quantity);

/// <summary>PrinterId selects a configured Printer (its name is snapshotted onto the job).
/// PrinterName is used as-is only when PrinterId is omitted — e.g. no printers are configured
/// yet, or the user just wants a label for a manual/offline print. Quality ("Standard" or
/// "High") must be one the printer supports; it defaults to Standard. CutMode ("AutoCut",
/// "HalfCut", "CutAtEnd", "ChainPrinting" or "CutMarks") defaults to AutoCut.</summary>
public sealed record CreatePrintJobRequest(
    int TemplateId,
    int? PrinterId,
    string? PrinterName,
    List<PrintJobItemRequest>? Items,
    string? Quality = null,
    string? CutMode = null);

/// <summary>Jobs still queued or printing are left alone and counted in SkippedInProgress.</summary>
public sealed record DeleteAllPrintJobsResponse(int Deleted, int SkippedInProgress);

public sealed record PrintJobItemResponse(
    int Id,
    Dictionary<string, string> FieldValues,
    int Quantity,
    string Status,
    string? ErrorMessage,
    string? PreviewUrl);

public sealed record PrintJobResponse(
    int Id,
    int TemplateId,
    string TemplateName,
    // The template has since been deleted, so it can't be opened or printed again.
    bool TemplateDeleted,
    int TemplateVersionNumber,
    int? PrinterId,
    string? PrinterName,
    string Quality,
    string CutMode,
    string Status,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    List<PrintJobItemResponse> Items);
