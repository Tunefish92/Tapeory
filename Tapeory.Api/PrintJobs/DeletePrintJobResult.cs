namespace Tapeory.Api.PrintJobs;

public enum DeletePrintJobResult
{
    Deleted,
    NotFound,

    /// <summary>The job is queued or printing; deleting it now would race the processor.</summary>
    StillPrinting
}
