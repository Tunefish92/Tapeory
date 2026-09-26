using System.Globalization;
using System.Text.RegularExpressions;

namespace Tapeory.Api.Backups;

public enum BackupKind
{
    Database,
    Labels
}

/// <summary>
/// Backup files are named "tapeory-db-20260925-143000-123.sql" / "tapeory-labels-….zip" (UTC),
/// with "-before-restore" added to the automatic backups taken right before a restore. Every file
/// name coming from a request is checked against these patterns before it touches the disk, so
/// only backups Tapeory wrote itself can be restored, downloaded or deleted.
/// </summary>
public static partial class BackupFileNames
{
    private const string TimestampFormat = "yyyyMMdd-HHmmss-fff";
    private const string BeforeRestoreSuffix = "-before-restore";

    public static bool TryParseKind(string? value, out BackupKind kind)
    {
        switch (value?.ToLowerInvariant())
        {
            case "database":
                kind = BackupKind.Database;
                return true;
            case "labels":
                kind = BackupKind.Labels;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    /// <summary>Folder relative to the storage root, e.g. "backups/database".</summary>
    public static string RelativeDirectory(BackupKind kind) => kind switch
    {
        BackupKind.Database => "backups/database",
        BackupKind.Labels => "backups/labels",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown backup kind.")
    };

    public static string Create(BackupKind kind, DateTimeOffset createdAt, bool beforeRestore) =>
        $"{Prefix(kind)}-{createdAt.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture)}"
        + (beforeRestore ? BeforeRestoreSuffix : string.Empty)
        + Extension(kind);

    public static bool IsValid(BackupKind kind, string? fileName) =>
        fileName is not null && Pattern(kind).IsMatch(fileName);

    public static bool IsBeforeRestore(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName).EndsWith(BeforeRestoreSuffix, StringComparison.Ordinal);

    /// <summary>The creation time encoded in a valid backup file name.</summary>
    public static DateTimeOffset GetCreatedAt(BackupKind kind, string fileName)
    {
        var timestamp = fileName.Substring(Prefix(kind).Length + 1, TimestampFormat.Length);
        return DateTimeOffset.ParseExact(
            timestamp, TimestampFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static string Prefix(BackupKind kind) => kind == BackupKind.Database ? "tapeory-db" : "tapeory-labels";

    private static string Extension(BackupKind kind) => kind == BackupKind.Database ? ".sql" : ".zip";

    private static Regex Pattern(BackupKind kind) => kind == BackupKind.Database ? DatabasePattern() : LabelsPattern();

    [GeneratedRegex(@"^tapeory-db-\d{8}-\d{6}-\d{3}(-before-restore)?\.sql$")]
    private static partial Regex DatabasePattern();

    [GeneratedRegex(@"^tapeory-labels-\d{8}-\d{6}-\d{3}(-before-restore)?\.zip$")]
    private static partial Regex LabelsPattern();
}
