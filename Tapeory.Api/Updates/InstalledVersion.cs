using Microsoft.EntityFrameworkCore;
using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Updates;

/// <param name="Version">The version that runs now.</param>
/// <param name="Updated">This installation ran an earlier version before; false for a new one.</param>
/// <param name="PreviousVersion">The version before the update; null when it isn't known (an
/// update from 0.5.0 or older, which didn't record their version).</param>
/// <param name="ChangesSince">The web app shows the changes of every version after this one: the
/// previous version, or for an unknown one the oldest version it can have been.</param>
/// <param name="UpdatedAt">When this version first started.</param>
public sealed record InstalledVersionResponse(
    string Version, bool Updated, string? PreviousVersion, string? ChangesSince, DateTimeOffset? UpdatedAt);

/// <summary>
/// Remembers which version last ran against the database, so the web app can tell after an
/// update that it was updated (and show what's new) — and stays quiet on a new installation.
/// </summary>
public static class InstalledVersion
{
    public const string VersionKey = "app.version";
    public const string PreviousVersionKey = "app.previousVersion";
    public const string ChangesSinceKey = "app.changesSince";

    /// <summary>The first version that records itself is 0.6.0; anything older is 0.5.0 or before.</summary>
    private const string LastUnrecordedVersion = "0.5.0";

    /// <summary>Applies the migrations, then records <paramref name="currentVersion"/>.</summary>
    public static async Task MigrateAsync(AppDbContext db, string currentVersion, CancellationToken cancellationToken)
    {
        string? lastMigration;
        bool existingDatabase;
        try
        {
            var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
            existingDatabase = applied.Count > 0;
            lastMigration = applied.LastOrDefault();
        }
        catch (Exception)
        {
            // The database itself doesn't exist yet.
            existingDatabase = false;
            lastMigration = null;
        }

        await db.Database.MigrateAsync(cancellationToken);
        await RecordAsync(db, currentVersion, existingDatabase, cancellationToken, EstimateUnrecordedVersion(lastMigration));
    }

    /// <summary>
    /// For a database from before 0.6.0, which didn't record its version: the oldest release
    /// whose newest migration is <paramref name="lastMigration"/>, so that no change is left out.
    /// </summary>
    public static string EstimateUnrecordedVersion(string? lastMigration)
    {
        // Migration ids start with a timestamp, so they compare in the order they were made.
        // Newer ones than AddInstanceBinding (0.5.0) come from the 0.6.0 betas, which didn't record
        // their version either; their changes are part of 0.6.0, so they count as 0.5.0.
        if (lastMigration is null) return "0.2.0";
        if (string.CompareOrdinal(lastMigration, "20260927200113") >= 0) return LastUnrecordedVersion;
        // The SQLite migrations (desktop app) began with 0.5.0.
        if (lastMigration == "20260927195852_InitialCreate") return LastUnrecordedVersion;
        if (string.CompareOrdinal(lastMigration, "20260927172004") >= 0) return "0.4.0";
        // AddUsers and AddTemplateOwnerAndVisibility came between 0.3.0 and 0.4.0.
        if (string.CompareOrdinal(lastMigration, "20260927142629") >= 0) return "0.3.0";
        // 0.2.0, 0.2.1 and 0.3.0 all end with AddPrintOptionsAndPrintServerQueue.
        return "0.2.0";
    }

    /// <param name="existingDatabase">The database had Tapeory's tables before this start. With
    /// no version recorded, that means an update from a version that didn't record it.</param>
    /// <param name="unrecordedVersion">For that case: the oldest version it can have been.</param>
    public static async Task RecordAsync(
        AppDbContext db, string currentVersion, bool existingDatabase, CancellationToken cancellationToken,
        string unrecordedVersion = LastUnrecordedVersion)
    {
        var settings = await db.ApplicationSettings
            .Where(s => s.Key == VersionKey || s.Key == PreviousVersionKey || s.Key == ChangesSinceKey)
            .ToDictionaryAsync(s => s.Key, cancellationToken);

        var recorded = settings.GetValueOrDefault(VersionKey)?.Value;
        if (recorded == currentVersion)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        Set(VersionKey, currentVersion);

        if (recorded is not null || existingDatabase)
        {
            // An empty value: updated, but from a version that isn't known.
            Set(PreviousVersionKey, recorded ?? "");
            Set(ChangesSinceKey, recorded ?? unrecordedVersion);
        }

        await db.SaveChangesAsync(cancellationToken);

        void Set(string key, string value)
        {
            if (settings.TryGetValue(key, out var setting))
            {
                setting.Value = value;
                setting.UpdatedAt = now;
            }
            else
            {
                db.ApplicationSettings.Add(new ApplicationSetting { Key = key, Value = value, UpdatedAt = now });
            }
        }
    }

    public static async Task<InstalledVersionResponse> GetAsync(
        AppDbContext db, string currentVersion, CancellationToken cancellationToken)
    {
        var settings = await db.ApplicationSettings
            .Where(s => s.Key == VersionKey || s.Key == PreviousVersionKey || s.Key == ChangesSinceKey)
            .ToDictionaryAsync(s => s.Key, cancellationToken);

        var version = settings.GetValueOrDefault(VersionKey);
        var previous = settings.GetValueOrDefault(PreviousVersionKey);

        // Only an update to the version that runs now counts; anything else (no record yet, or a
        // database another instance has moved on) reads as "not updated".
        var updated = version?.Value == currentVersion && previous is not null;
        if (!updated)
        {
            return new InstalledVersionResponse(currentVersion, false, null, null, null);
        }

        var previousVersion = string.IsNullOrEmpty(previous!.Value) ? null : previous.Value;

        // 0.6.0 and 0.6.1 recorded no ChangesSince; their unknown previous version was 0.5.0 or older.
        var changesSince = settings.GetValueOrDefault(ChangesSinceKey)?.Value is { Length: > 0 } since
            ? since
            : previousVersion ?? LastUnrecordedVersion;

        return new InstalledVersionResponse(currentVersion, true, previousVersion, changesSince, version!.UpdatedAt);
    }
}
