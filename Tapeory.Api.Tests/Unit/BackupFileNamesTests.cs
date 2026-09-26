using Tapeory.Api.Backups;

namespace Tapeory.Api.Tests.Unit;

public sealed class BackupFileNamesTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 25, 14, 30, 5, 123, TimeSpan.FromHours(2));

    [Theory]
    [InlineData(BackupKind.Database, false, "tapeory-db-20260925-123005-123.sql")]
    [InlineData(BackupKind.Database, true, "tapeory-db-20260925-123005-123-before-restore.sql")]
    [InlineData(BackupKind.Labels, false, "tapeory-labels-20260925-123005-123.zip")]
    [InlineData(BackupKind.Labels, true, "tapeory-labels-20260925-123005-123-before-restore.zip")]
    public void Create_UsesUtcTimestamp_AndMarksBeforeRestoreBackups(BackupKind kind, bool beforeRestore, string expected)
    {
        var fileName = BackupFileNames.Create(kind, CreatedAt, beforeRestore);

        Assert.Equal(expected, fileName);
        Assert.True(BackupFileNames.IsValid(kind, fileName));
        Assert.Equal(beforeRestore, BackupFileNames.IsBeforeRestore(fileName));
        Assert.Equal(CreatedAt, BackupFileNames.GetCreatedAt(kind, fileName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tapeory-db-20260925-123005-123.zip")]
    [InlineData("tapeory-labels-20260925-123005-123.sql")]
    [InlineData("../tapeory-db-20260925-123005-123.sql")]
    [InlineData("..\\tapeory-db-20260925-123005-123.sql")]
    [InlineData("tapeory-db-20260925-123005-123.sql.partial")]
    [InlineData("tapeory-db-2026-09-25.sql")]
    [InlineData("config/database.json")]
    public void IsValid_RejectsAnythingThatIsNotABackupOfThatKind(string? fileName)
    {
        Assert.False(BackupFileNames.IsValid(BackupKind.Database, fileName));
        Assert.False(BackupFileNames.IsValid(BackupKind.Labels, fileName));
    }

    [Theory]
    [InlineData("database", BackupKind.Database)]
    [InlineData("Labels", BackupKind.Labels)]
    public void TryParseKind_AcceptsKnownKinds(string value, BackupKind expected)
    {
        Assert.True(BackupFileNames.TryParseKind(value, out var kind));
        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("files")]
    [InlineData("0")]
    public void TryParseKind_RejectsUnknownKinds(string? value)
    {
        Assert.False(BackupFileNames.TryParseKind(value, out _));
    }
}
