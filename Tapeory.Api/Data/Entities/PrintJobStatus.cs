namespace Tapeory.Api.Data.Entities;

public enum PrintJobStatus
{
    Queued = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Sending = 4,
    Printing = 5,

    /// <summary>Not printed: stopped by the user, or left out after an earlier label failed.</summary>
    Cancelled = 6
}
