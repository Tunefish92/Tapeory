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
    IReadOnlyList<ReleaseAsset>? Assets = null,
    bool PreRelease = false);

/// <summary>A file attached to the release (e.g. the desktop app's installer), which the desktop
/// app downloads to update itself.</summary>
/// <param name="Sha256">The file's SHA-256 as hex, from GitHub's asset digest; null when GitHub
/// doesn't give one.</param>
public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size, string? Sha256);

/// <summary>
/// Asks GitHub for Tapeory's latest release, or for its newest one including pre-releases. The answer is cached for a few hours, so opening the
/// settings page doesn't call GitHub every time (its API allows 60 requests an hour without a token).
/// </summary>
public sealed class UpdateChecker(HttpClient http, TimeProvider time, string? feedUrl = null)
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/Tunefish92/Tapeory/releases/latest";

    /// <summary>The newest releases, pre-releases included ("latest" above never is one).</summary>
    public const string ReleasesUrl = "https://api.github.com/repos/Tunefish92/Tapeory/releases?per_page=30";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    private readonly SemaphoreSlim _lock = new(1, 1);
    // One answer with pre-releases and one without.
    private readonly UpdateCheckResponse?[] _cached = new UpdateCheckResponse?[2];

    public static string CurrentVersion { get; } = ReadCurrentVersion();

    /// <param name="preReleases">Also consider pre-releases (beta versions).</param>
    public async Task<UpdateCheckResponse> CheckAsync(bool refresh, CancellationToken cancellationToken, bool preReleases = false)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            var slot = preReleases ? 1 : 0;
            var now = time.GetUtcNow();
            if (!refresh && _cached[slot]?.CheckedAt is { } checkedAt && now - checkedAt < CacheDuration)
            {
                return _cached[slot]!;
            }

            _cached[slot] = await FetchAsync(now, preReleases, cancellationToken);
            return _cached[slot]!;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<UpdateCheckResponse> FetchAsync(DateTimeOffset now, bool preReleases, CancellationToken cancellationToken)
    {
        try
        {
            var address = !string.IsNullOrWhiteSpace(feedUrl) ? feedUrl : preReleases ? ReleasesUrl : LatestReleaseUrl;
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Tapeory", CurrentVersion));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Failed($"GitHub answered {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            // One release, or a list of them: then the one with the highest version.
            var release = json.RootElement.ValueKind == JsonValueKind.Array ? Newest(json.RootElement, preReleases) : json.RootElement;
            if (release is not { } newest)
            {
                return Failed("GitHub lists no release.");
            }

            var tag = newest.GetProperty("tag_name").GetString();
            var url = newest.TryGetProperty("html_url", out var link) ? link.GetString() : null;
            var latest = tag?.TrimStart('v', 'V');

            return new UpdateCheckResponse(
                CurrentVersion,
                latest,
                IsNewer(latest, CurrentVersion),
                url,
                now,
                null,
                ReadAssets(newest),
                IsPreRelease(latest));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return Failed("GitHub couldn't be reached.");
        }
    }

    private static JsonElement? Newest(JsonElement releases, bool preReleases)
    {
        JsonElement? newest = null;
        string? newestTag = null;

        foreach (var release in releases.EnumerateArray())
        {
            var tag = release.TryGetProperty("tag_name", out var name) ? name.GetString() : null;
            var draft = release.TryGetProperty("draft", out var isDraft) && isDraft.ValueKind == JsonValueKind.True;
            var marked = release.TryGetProperty("prerelease", out var isPre) && isPre.ValueKind == JsonValueKind.True;

            if (tag is null || draft || !TryParse(tag, out _, out var suffix) || (!preReleases && (marked || suffix.Length > 0)))
            {
                continue;
            }

            if (newestTag is null || Compare(tag, newestTag) > 0)
            {
                (newest, newestTag) = (release, tag);
            }
        }

        return newest;
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

    /// <summary>Whether <paramref name="latest"/> is a newer release than <paramref name="current"/>.
    /// A pre-release ("0.6.0-beta.1") is older than the release it leads to ("0.6.0"), and
    /// "beta.2" is newer than "beta.1".</summary>
    public static bool IsNewer(string? latest, string current) =>
        TryParse(latest, out _, out _) && TryParse(current, out _, out _) && Compare(latest!, current) > 0;

    public static bool IsPreRelease(string? version) => TryParse(version, out _, out var suffix) && suffix.Length > 0;

    /// <summary>Orders two versions as semantic versioning does.</summary>
    private static int Compare(string a, string b)
    {
        TryParse(a, out var versionA, out var suffixA);
        TryParse(b, out var versionB, out var suffixB);

        var numbers = versionA.CompareTo(versionB);
        if (numbers != 0) return numbers;

        // Without a suffix it is the full release, which comes after its pre-releases.
        if (suffixA.Length == 0 || suffixB.Length == 0) return suffixB.Length.CompareTo(suffixA.Length);

        // "beta.2" against "beta.10": part by part, numbers as numbers.
        var (partsA, partsB) = (suffixA.Split('.'), suffixB.Split('.'));
        for (var i = 0; i < Math.Min(partsA.Length, partsB.Length); i++)
        {
            var (isNumberA, isNumberB) = (int.TryParse(partsA[i], out var numberA), int.TryParse(partsB[i], out var numberB));
            var part = isNumberA && isNumberB
                ? numberA.CompareTo(numberB)
                : isNumberA != isNumberB ? (isNumberA ? -1 : 1) : string.CompareOrdinal(partsA[i], partsB[i]);
            if (part != 0) return part;
        }

        return partsA.Length.CompareTo(partsB.Length);
    }

    // The numbers, and the pre-release suffix after "-" ("beta.1"); a build suffix ("+abc123") is dropped.
    private static bool TryParse(string? value, out Version version, out string preRelease)
    {
        version = new Version();
        preRelease = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var trimmed = value.TrimStart('v', 'V').Split('+')[0];
        var dash = trimmed.IndexOf('-');
        preRelease = dash < 0 ? string.Empty : trimmed[(dash + 1)..];
        return Version.TryParse(dash < 0 ? trimmed : trimmed[..dash], out version!);
    }

    private static string ReadCurrentVersion()
    {
        var assembly = typeof(UpdateChecker).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return (informational ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0").Split('+')[0];
    }
}
