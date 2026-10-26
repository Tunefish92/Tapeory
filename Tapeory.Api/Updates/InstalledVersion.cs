using Microsoft.EntityFrameworkCore;
using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Updates;

/// <param name="Version">The version that runs now.</param>
/// <param name="Updated">This installation ran an earlier version before; false for a new one.</param>
/// <param name="PreviousVersion">The version before the update; null when it isn't known (an
/// update from 0.5.0 or older, which didn't record their version).</param>
/// <param name="UpdatedAt">When this version first started.</param>
public sealed record InstalledVersionResponse(string Version, bool Updated, string? PreviousVersion, DateTimeOffset? UpdatedAt);

/// <summary>
/// Remembers which version last ran against the database, so the web app can tell after an
/// update that it was updated (and show what's new) — and stays quiet on a new installation.
/// </summary>
public static class InstalledVersion
{
    public const string VersionKey = "app.version";
    public const string PreviousVersionKey = "app.previousVersion";

    /// <summary>Applies the migrations, then records <paramref name="currentVersion"/>.</summary>
    public static async Task MigrateAsync(AppDbContext db, string currentVersion, CancellationToken cancellationToken)
    {
        bool existingDatabase;
        try
        {
            existingDatabase = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).Any();
        }
        catch (Exception)
        {
            // The database itself doesn't exist yet.
            existingDatabase = false;
        }

        await db.Database.MigrateAsync(cancellationToken);
        await RecordAsync(db, currentVersion, existingDatabase, cancellationToken);
    }

    /// <param name="existingDatabase">The database had Tapeory's tables before this start. With
    /// no version recorded, that means an update from a version that didn't record it.</param>
    public static async Task RecordAsync(
        AppDbContext db, string currentVersion, bool existingDatabase, CancellationToken cancellationToken)
    {
        var settings = await db.ApplicationSettings
            .Where(s => s.Key == VersionKey || s.Key == PreviousVersionKey)
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
            .Where(s => s.Key == VersionKey || s.Key == PreviousVersionKey)
            .ToDictionaryAsync(s => s.Key, cancellationToken);

        var version = settings.GetValueOrDefault(VersionKey);
        var previous = settings.GetValueOrDefault(PreviousVersionKey);

        // Only an update to the version that runs now counts; anything else (no record yet, or a
        // database another instance has moved on) reads as "not updated".
        var updated = version?.Value == currentVersion && previous is not null;

        return new InstalledVersionResponse(
            currentVersion,
            updated,
            updated && !string.IsNullOrEmpty(previous!.Value) ? previous.Value : null,
            updated ? version!.UpdatedAt : null);
    }
}
