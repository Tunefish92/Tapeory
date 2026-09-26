using Tapeory.Api.Backups;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Tapeory.Api.Tests.Unit;

public sealed class BackupStoreTests : IDisposable
{
    private readonly string _storagePath = Directory.CreateTempSubdirectory("tapeory-backups-").FullName;
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
    private readonly BackupStore _store;

    public BackupStoreTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TAPEORY_STORAGE_PATH"] = _storagePath })
            .Build();

        _store = new BackupStore(new StorageService(configuration, new FakeHostEnvironment(_storagePath)), _time);
    }

    public void Dispose() => Directory.Delete(_storagePath, recursive: true);

    [Fact]
    public async Task WriteAsync_StoresTheBackup_InItsKindsFolder()
    {
        var backup = await _store.WriteAsync(BackupKind.Database, beforeRestore: false, path => File.WriteAllTextAsync(path, "-- dump"));

        Assert.Equal("tapeory-db-20260925-100000-000.sql", backup.FileName);
        Assert.Equal(7, backup.SizeBytes);
        Assert.False(backup.BeforeRestore);
        Assert.True(File.Exists(Path.Combine(_storagePath, "backups", "database", backup.FileName)));
        Assert.Equal(Path.Combine(_storagePath, "backups", "database", backup.FileName), _store.Find(BackupKind.Database, backup.FileName));
    }

    [Fact]
    public async Task WriteAsync_LeavesNothingBehind_WhenWritingFails()
    {
        await Assert.ThrowsAsync<IOException>(() => _store.WriteAsync(BackupKind.Labels, beforeRestore: false, async path =>
        {
            await File.WriteAllTextAsync(path, "half");
            throw new IOException("disk full");
        }));

        Assert.Empty(_store.List(BackupKind.Labels));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_storagePath, "backups", "labels")));
    }

    [Fact]
    public async Task List_ShowsNewestFirst_AndOnlyBackupsOfThatKind()
    {
        await _store.WriteAsync(BackupKind.Database, false, path => File.WriteAllTextAsync(path, "1"));
        _time.Advance(TimeSpan.FromMinutes(1));
        await _store.WriteAsync(BackupKind.Database, true, path => File.WriteAllTextAsync(path, "2"));
        await _store.WriteAsync(BackupKind.Labels, false, path => File.WriteAllTextAsync(path, "3"));
        await File.WriteAllTextAsync(Path.Combine(_storagePath, "backups", "database", "notes.txt"), "not a backup");

        var backups = _store.List(BackupKind.Database);

        Assert.Equal(2, backups.Count);
        Assert.True(backups[0].BeforeRestore);
        Assert.Equal(new DateTimeOffset(2026, 9, 25, 10, 1, 0, TimeSpan.Zero), backups[0].CreatedAt);
        Assert.False(backups[1].BeforeRestore);
    }

    [Fact]
    public async Task FindAndDelete_RefuseNamesThatAreNotBackups()
    {
        var backup = await _store.WriteAsync(BackupKind.Database, false, path => File.WriteAllTextAsync(path, "x"));
        await File.WriteAllTextAsync(Path.Combine(_storagePath, "secret.txt"), "keep me");

        Assert.Null(_store.Find(BackupKind.Labels, backup.FileName));
        Assert.Null(_store.Find(BackupKind.Database, "../../secret.txt"));
        Assert.False(_store.Delete(BackupKind.Database, "../../secret.txt"));
        Assert.True(File.Exists(Path.Combine(_storagePath, "secret.txt")));

        Assert.True(_store.Delete(BackupKind.Database, backup.FileName));
        Assert.Null(_store.Find(BackupKind.Database, backup.FileName));
        Assert.False(_store.Delete(BackupKind.Database, backup.FileName));
    }

    [Fact]
    public void TryBeginOperation_AllowsOneOperationAtATime()
    {
        var first = _store.TryBeginOperation();
        Assert.NotNull(first);
        Assert.Null(_store.TryBeginOperation());

        first.Dispose();
        first.Dispose(); // releasing twice must not let two operations in later

        using var second = _store.TryBeginOperation();
        Assert.NotNull(second);
        Assert.Null(_store.TryBeginOperation());
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FakeHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Tapeory.Api.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
