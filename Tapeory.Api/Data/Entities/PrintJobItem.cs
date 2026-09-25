namespace Tapeory.Api.Data.Entities;

/// <summary>One label design within a job — usually one, but a job can print a batch with
/// different field values per copy (e.g. a mail-merge run).</summary>
public sealed class PrintJobItem
{
    public int Id { get; set; }

    public int PrintJobId { get; set; }

    public PrintJob? PrintJob { get; set; }

    /// <summary>JSON-serialized Dictionary&lt;string, string&gt; of dynamic field values for
    /// this item.</summary>
    public required string FieldValuesJson { get; set; }

    public int Quantity { get; set; } = 1;

    public PrintJobStatus Status { get; set; } = PrintJobStatus.Queued;

    public string? ErrorMessage { get; set; }

    public int? RenderedImageFileId { get; set; }

    public UploadedFile? RenderedImageFile { get; set; }
}
