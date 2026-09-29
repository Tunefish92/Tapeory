using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace Tapeory.Api.Updates;

public sealed record UpdateCheckResponse(
    string CurrentVersion,
    string? LatestVersion,
    bool UpdateAvailable,
    string? ReleaseUrl,
    DateTimeOffset? CheckedAt,
    string? ErrorMessage,
    IReadOnlyList<ReleaseAsset>? Assets = null);

/// <summary>A file attached to the release (e.g. the desktop app's installer), which the desktop
/// app downloads to update itself.</summary>
/// <param name="Sha256">The file's SHA-256 as hex, from GitHub's asset digest; null when GitHub
/// doesn't give one.</param>
public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size, string? Sha256);

/// <summary>
/// Asks GitHub for Tapeory's latest release. The answer is cached for a few hours, so opening the
/// settings page doesn't call GitHub every time (its API allows 60 requests an hour without a token).
/// </summary>
public sealed class UpdateChecker(HttpClient http, TimeProvider time, string? feedUrl = null)
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/Tunefish92/Tapeory/releases/latest";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private UpdateCheckResponse? _cached;

    public static string CurrentVersion { get; } = ReadCurrentVersion();

    public async Task<UpdateCheckResponse> CheckAsync(bool refresh, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            var now = time.GetUtcNow();
            if (!refresh && _cached?.CheckedAt is { } checkedAt && now - checkedAt < CacheDuration)
            {
                return _cached;
            }

            _cached = await FetchAsync(now, cancellationToken);
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<UpdateCheckResponse> FetchAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, string.IsNullOrWhiteSpace(feedUrl) ? LatestReleaseUrl : feedUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Tapeory", CurrentVersion));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Failed($"GitHub answered {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var tag = json.RootElement.GetProperty("tag_name").GetString();
            var url = json.RootElement.TryGetProperty("html_url", out var link) ? link.GetString() : null;
            var latest = tag?.TrimStart('v', 'V');

            return new UpdateCheckResponse(
                CurrentVersion,
                latest,
                IsNewer(latest, CurrentVersion),
                url,
                now,
                null,
                ReadAssets(json.RootElement));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return Failed("GitHub couldn't be reached.");
        }
    }

    private static List<ReleaseAsset> ReadAssets(JsonElement release)
    {
        var assets = new List<ReleaseAsset>();
        if (!release.TryGetProperty("assets", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return assets;
        }

        foreach (var asset in list.EnumerateArray())
        {
            if (asset.TryGetProperty("name", out var name) && name.GetString() is { } fileName
                && asset.TryGetProperty("browser_download_url", out var download) && download.GetString() is { } downloadUrl)
            {
                var size = asset.TryGetProperty("size", out var bytes) && bytes.TryGetInt64(out var value) ? value : 0;
                var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() : null;
                var sha256 = digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? digest["sha256:".Length..] : null;
                assets.Add(new ReleaseAsset(fileName, downloadUrl, size, sha256));
            }
        }

        return assets;
    }

    // A failed check isn't cached, so the next visit tries again.
    private static UpdateCheckResponse Failed(string message) =>
        new(CurrentVersion, null, false, null, null, message);

    /// <summary>Whether <paramref name="latest"/> is a newer release than <paramref name="current"/>.</summary>
    public static bool IsNewer(string? latest, string current) =>
        TryParse(latest, out var latestVersion)
        && TryParse(current, out var currentVersion)
        && latestVersion > currentVersion;

    // Ignores a pre-release or build suffix ("0.3.0-beta", "0.2.1+abc123").
    private static bool TryParse(string? value, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(value)) return false;

        var core = value.TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(core, out version!);
    }

    private static string ReadCurrentVersion()
    {
        var assembly = typeof(UpdateChecker).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return (informational ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0").Split('+')[0];
    }
}
