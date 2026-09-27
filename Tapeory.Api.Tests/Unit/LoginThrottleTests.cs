using Tapeory.Api.Auth;

namespace Tapeory.Api.Tests.Unit;

public class LoginThrottleTests
{
    private readonly ManualTime _time = new(DateTimeOffset.Parse("2026-09-27T10:00:00Z"));

    [Fact]
    public void BlocksAnAccount_AfterTooManyFailures_UntilTheWindowPasses()
    {
        var throttle = new LoginThrottle(_time);

        for (var i = 0; i < LoginThrottle.MaxFailuresPerAccount; i++)
        {
            Assert.Null(throttle.RetryAfter("10.0.0.5", "ada"));
            throttle.RecordFailure("10.0.0.5", "ada");
        }

        Assert.Equal(LoginThrottle.Window, throttle.RetryAfter("10.0.0.5", "ADA "));
        // Another address, or another account from this one, isn't affected.
        Assert.Null(throttle.RetryAfter("10.0.0.6", "ada"));
        Assert.Null(throttle.RetryAfter("10.0.0.5", "grace"));

        _time.Advance(LoginThrottle.Window);
        Assert.Null(throttle.RetryAfter("10.0.0.5", "ada"));
    }

    [Fact]
    public void BlocksAnAddress_GuessingAcrossManyAccounts()
    {
        var throttle = new LoginThrottle(_time);

        for (var i = 0; i < LoginThrottle.MaxFailuresPerAddress; i++)
        {
            throttle.RecordFailure("10.0.0.5", $"user{i}");
        }

        Assert.NotNull(throttle.RetryAfter("10.0.0.5", "someone-new"));
        Assert.Null(throttle.RetryAfter("10.0.0.6", "someone-new"));
    }

    [Fact]
    public void ACorrectPassword_ClearsThatAccountsFailures()
    {
        var throttle = new LoginThrottle(_time);

        for (var i = 0; i < LoginThrottle.MaxFailuresPerAccount - 1; i++)
        {
            throttle.RecordFailure("10.0.0.5", "ada");
        }

        throttle.RecordSuccess("10.0.0.5", "ada");
        throttle.RecordFailure("10.0.0.5", "ada");

        Assert.Null(throttle.RetryAfter("10.0.0.5", "ada"));
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
