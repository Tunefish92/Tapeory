using Tapeory.Api.Storage;

namespace Tapeory.Api.Backups;

public sealed record BackupInfo(string FileName, long SizeBytes, DateTimeOffset CreatedAt, bool BeforeRestore);

/// <summary>
/// The backup files in the storage folder ("backups/database", "backups/labels"). The folder
/// itself is the list of backups — deliberately not a database table, which a database restore
/// would roll back along with everything else.
///
/// Also serializes backup work: only one backup, restore or delete runs at a time, so a restore
/// can't read a file that's still being written or deleted.
/// </summary>
public sealed class BackupStore(StorageService storage, TimeProvider time)
{
    private const string PartialSuffix = ".partial";

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Null when another backup operation is running.</summary>
    public IDisposable? TryBeginOperation() => _gate.Wait(0) ? new Release(_gate) : null;

    public IReadOnlyList<BackupInfo> List(BackupKind kind) =>
        [.. Directory.EnumerateFiles(GetDirectory(kind))
            .Select(Path.GetFileName)
            .Where(name => BackupFileNames.IsValid(kind, name))
            .Select(name => Describe(kind, name!))
            .OrderByDescending(backup => backup.CreatedAt)];

    /// <summary>Full path of an existing backup, or null if the name isn't a backup of this kind
    /// or the file doesn't exist.</summary>
    public string? Find(BackupKind kind, string fileName)
    {
        if (!BackupFileNames.IsValid(kind, fileName))
        {
            return null;
        }

        var path = StoragePathGuard.ResolveWithinRoot(GetDirectory(kind), fileName);
        return File.Exists(path) ? path : null;
    }

    public BackupInfo Describe(BackupKind kind, string fileName)
    {
        var path = StoragePathGuard.ResolveWithinRoot(GetDirectory(kind), fileName);

        return new BackupInfo(
            fileName,
            new FileInfo(path).Length,
            BackupFileNames.GetCreatedAt(kind, fileName),
            BackupFileNames.IsBeforeRestore(fileName));
    }

    public bool Delete(BackupKind kind, string fileName)
    {
        var path = Find(kind, fileName);

        if (path is null)
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    /// <summary>
    /// Runs <paramref name="write"/> against a temporary file and only gives it its backup name
    /// once writing succeeded, so a failed or half-written backup never shows up in the list.
    /// </summary>
    public async Task<BackupInfo> WriteAsync(BackupKind kind, bool beforeRestore, Func<string, Task> write, string? extension = null)
    {
        var fileName = BackupFileNames.Create(kind, time.GetUtcNow(), beforeRestore, extension);
        var path = StoragePathGuard.ResolveWithinRoot(GetDirectory(kind), fileName);
        var partialPath = path + PartialSuffix;

        try
        {
            await write(partialPath);
            File.Move(partialPath, path);
        }
        finally
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
        }

        return Describe(kind, fileName);
    }

    private string GetDirectory(BackupKind kind)
    {
        var directory = StoragePathGuard.ResolveWithinRoot(storage.RootPath, BackupFileNames.RelativeDirectory(kind));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
