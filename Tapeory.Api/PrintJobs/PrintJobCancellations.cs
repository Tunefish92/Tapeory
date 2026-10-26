using System.Collections.Concurrent;

namespace Tapeory.Api.PrintJobs;

/// <summary>Print jobs the user asked to stop while they were being printed. The processor looks
/// here between batches; a job only ever runs in the installation it was submitted to, so this
/// doesn't need to be in the database.</summary>
public sealed class PrintJobCancellations
{
    private readonly ConcurrentDictionary<int, bool> _requested = new();

    public void Request(int jobId) => _requested[jobId] = true;

    public bool IsRequested(int jobId) => _requested.ContainsKey(jobId);

    public void Clear(int jobId) => _requested.TryRemove(jobId, out _);
}
