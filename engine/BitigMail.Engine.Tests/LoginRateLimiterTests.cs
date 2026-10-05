using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class LoginRateLimiterTests
{
    [Fact]
    public void PerUserDelayAndSuccessReset()
    {
        var time = new Clock(); var limiter = new LoginRateLimiter(time);
        Assert.True(limiter.IsAllowed("user")); limiter.Failed("user");
        Assert.False(limiter.IsAllowed("user")); Assert.True(limiter.IsAllowed("other"));
        time.Advance(TimeSpan.FromSeconds(1)); Assert.True(limiter.IsAllowed("user"));
        limiter.Failed("user"); limiter.Succeeded("user"); Assert.True(limiter.IsAllowed("user"));
    }

    [Fact]
    public void RotatingUserNamesCannotBypassProfileThrottleOrClearItWithSuccess()
    {
        var time = new Clock(); var limiter = new LoginRateLimiter(time);
        for (int i = 0; i < 10; i++) limiter.Failed("user" + i);
        Assert.False(limiter.IsAllowed("new-user"));
        limiter.Succeeded("own-account"); Assert.False(limiter.IsAllowed("new-user"));
        time.Advance(TimeSpan.FromSeconds(1)); Assert.True(limiter.IsAllowed("new-user"));
    }

    [Fact]
    public void CapacityIsBoundedAndExpiredHistoryIsReclaimed()
    {
        var time = new Clock(); var limiter = new LoginRateLimiter(time);
        for (int i = 0; i < 3000; i++) limiter.Failed("user" + i);
        time.Advance(TimeSpan.FromSeconds(61)); Assert.False(limiter.IsAllowed("new-user"));
        time.Advance(TimeSpan.FromMinutes(15)); Assert.True(limiter.IsAllowed("new-user"));
    }

    [Fact]
    public void WallClockChangesCannotShortenThrottleAndInvalidKeysAreRejected()
    {
        var time = new Clock(); var limiter = new LoginRateLimiter(time);
        limiter.Failed("user"); time.WallClock += TimeSpan.FromDays(3);
        Assert.False(limiter.IsAllowed("user"));
        Assert.False(limiter.IsAllowed(new string('a', 257))); Assert.False(limiter.IsAllowed(""));
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public DateTimeOffset WallClock = DateTimeOffset.UnixEpoch;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => WallClock;
        public void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
    }
}
