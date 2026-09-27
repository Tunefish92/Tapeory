using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Tapeory.Api.Settings;

/// <summary>User preferences for the whole installation. Null means "not chosen yet", so the
/// browser can fall back to its own default (e.g. the OS color scheme) until someone picks.</summary>
public sealed record AppSettingsResponse(string? Language, string? Theme, string? Unit);

/// <summary>Partial update: only non-null properties are changed.</summary>
public sealed record UpdateAppSettingsRequest(string? Language, string? Theme, string? Unit);

/// <summary>
/// Stores the app-wide preferences (language, theme, measurement unit) as rows in the
/// ApplicationSettings key/value table, so every browser and device sees the same configuration.
/// </summary>
public sealed class AppSettingsService(AppDbContext db)
{
    public const string LanguageKey = "ui.language";
    public const string ThemeKey = "ui.theme";
    public const string UnitKey = "ui.unit";

    public static readonly IReadOnlyList<string> Languages = ["en", "de", "it", "fr", "es"];
    public static readonly IReadOnlyList<string> Themes = ["light", "dark", "system"];
    public static readonly IReadOnlyList<string> Units = ["mm", "inch"];

    public async Task<AppSettingsResponse> GetAsync(CancellationToken cancellationToken)
    {
        var values = await db.ApplicationSettings
            .Where(s => s.Key == LanguageKey || s.Key == ThemeKey || s.Key == UnitKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        // Anything unrecognized (hand-edited row, value from a future version) reads as unset
        // rather than breaking the UI.
        return new AppSettingsResponse(
            Allowed(values.GetValueOrDefault(LanguageKey), Languages),
            Allowed(values.GetValueOrDefault(ThemeKey), Themes),
            Allowed(values.GetValueOrDefault(UnitKey), Units));
    }

    /// <summary>Returns the validation errors keyed by property name; empty when valid.</summary>
    public static Dictionary<string, string[]> Validate(UpdateAppSettingsRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        Check(nameof(request.Language), request.Language, Languages);
        Check(nameof(request.Theme), request.Theme, Themes);
        Check(nameof(request.Unit), request.Unit, Units);

        return errors;

        void Check(string name, string? value, IReadOnlyList<string> allowed)
        {
            if (value is not null && !allowed.Contains(value))
            {
                errors[name] = [$"{name} must be one of: {string.Join(", ", allowed)}."];
            }
        }
    }

    public async Task<AppSettingsResponse> UpdateAsync(UpdateAppSettingsRequest request, CancellationToken cancellationToken)
    {
        var changes = new Dictionary<string, string>();
        if (request.Language is not null) changes[LanguageKey] = request.Language;
        if (request.Theme is not null) changes[ThemeKey] = request.Theme;
        if (request.Unit is not null) changes[UnitKey] = request.Unit;

        if (changes.Count > 0)
        {
            var now = DateTimeOffset.UtcNow;
            var existing = await db.ApplicationSettings
                .Where(s => changes.Keys.Contains(s.Key))
                .ToDictionaryAsync(s => s.Key, cancellationToken);

            foreach (var (key, value) in changes)
            {
                if (existing.TryGetValue(key, out var setting))
                {
                    setting.Value = value;
                    setting.UpdatedAt = now;
                }
                else
                {
                    db.ApplicationSettings.Add(new ApplicationSetting { Key = key, Value = value, UpdatedAt = now });
                }
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Two tabs saved a brand-new key at the same moment (unique key). The other
                // write won the insert; apply ours on top of it.
                db.ChangeTracker.Clear();
                foreach (var (key, value) in changes)
                {
                    await db.ApplicationSettings
                        .Where(s => s.Key == key)
                        .ExecuteUpdateAsync(u => u.SetProperty(s => s.Value, value).SetProperty(s => s.UpdatedAt, now), cancellationToken);
                }
            }
        }

        return await GetAsync(cancellationToken);
    }

    private static string? Allowed(string? value, IReadOnlyList<string> allowed) =>
        value is not null && allowed.Contains(value) ? value : null;
}
