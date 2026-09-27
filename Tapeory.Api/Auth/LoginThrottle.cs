using System.Collections.Concurrent;

namespace Tapeory.Api.Auth;

/// <summary>
/// Slows down password guessing: after too many wrong passwords for one account from one address,
/// or from one address overall, sign-in attempts are refused for a while. Only failures count, and
/// nothing locks the account itself, so nobody can lock an admin out by guessing wrong on purpose.
/// </summary>
public sealed class LoginThrottle(TimeProvider time)
{
    public const int MaxFailuresPerAccount = 5;
    public const int MaxFailuresPerAddress = 20;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, Failures> _failures = new();

    /// <summary>How long until another attempt is allowed; null when it is allowed now.</summary>
    public TimeSpan? RetryAfter(string? address, string account)
    {
        var now = time.GetUtcNow();

        return Blocked(AccountKey(address, account), MaxFailuresPerAccount, now)
            ?? Blocked(AddressKey(address), MaxFailuresPerAddress, now);
    }

    public void RecordFailure(string? address, string account)
    {
        var now = time.GetUtcNow();
        Prune(now);
        Add(AccountKey(address, account), now);
        Add(AddressKey(address), now);
    }

    /// <summary>A correct password clears that account's failures (not the address's).</summary>
    public void RecordSuccess(string? address, string account) =>
        _failures.TryRemove(AccountKey(address, account), out _);

    private TimeSpan? Blocked(string key, int max, DateTimeOffset now)
    {
        if (!_failures.TryGetValue(key, out var failures) || now - failures.Since >= Window)
        {
            return null;
        }

        return failures.Count >= max ? failures.Since + Window - now : null;
    }

    private void Add(string key, DateTimeOffset now) =>
        _failures.AddOrUpdate(
            key,
            _ => new Failures(1, now),
            (_, failures) => now - failures.Since >= Window ? new Failures(1, now) : failures with { Count = failures.Count + 1 });

    private void Prune(DateTimeOffset now)
    {
        if (_failures.Count < 1000)
        {
            return;
        }

        foreach (var (key, failures) in _failures)
        {
            if (now - failures.Since >= Window)
            {
                _failures.TryRemove(key, out _);
            }
        }
    }

    private static string AccountKey(string? address, string account) =>
        $"{address ?? "?"}|{account.Trim().ToLowerInvariant()}";

    private static string AddressKey(string? address) => address ?? "?";

    private sealed record Failures(int Count, DateTimeOffset Since);
}
