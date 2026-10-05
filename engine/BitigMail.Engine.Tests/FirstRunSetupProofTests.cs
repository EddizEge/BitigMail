using System.Security.Cryptography;
using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;
public sealed class FirstRunSetupProofTests
{
    private sealed class Clock : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
    }
    [Fact] public void ValidProofHasExactlyOneWinnerAcrossConcurrentRequests()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32); using var authority = new FirstRunSetupProof(bytes);
        string token = Convert.ToHexString(bytes).ToLowerInvariant(); int winners = 0;
        Parallel.For(0, 32, _ => { if (authority.TryConsume(token)) Interlocked.Increment(ref winners); });
        Assert.Equal(1, winners); Assert.False(authority.TryConsume(token));
        Assert.DoesNotContain(token, authority.ToString());
    }
    [Fact] public void InvalidProofDoesNotConsumeTheValidProof()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32); using var authority = new FirstRunSetupProof(bytes);
        Assert.False(authority.TryConsume(new string('0', 64)));
        Assert.True(authority.TryConsume(Convert.ToHexString(bytes).ToLowerInvariant()));
    }
    [Theory] [InlineData(null)] [InlineData("")] [InlineData("abc")]
    public void MalformedProofIsRejected(string? candidate)
    {
        using var authority = new FirstRunSetupProof(RandomNumberGenerator.GetBytes(32));
        Assert.False(authority.TryConsume(candidate));
    }
    [Fact] public void ExpiryIsExactAndRestartCannotReuseOldProof()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32); var clock = new Clock();
        using var authority = new FirstRunSetupProof(bytes, clock); string token = Convert.ToHexString(bytes).ToLowerInvariant();
        clock.Ticks = TimeSpan.FromMinutes(5).Ticks; Assert.False(authority.TryConsume(token));
        using var restarted = new FirstRunSetupProof(RandomNumberGenerator.GetBytes(32)); Assert.False(restarted.TryConsume(token));
    }
    [Fact] public void NativeInputIsSnapshottedAndDisposalRevokes()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32); string token = Convert.ToHexString(bytes).ToLowerInvariant();
        using var authority = new FirstRunSetupProof(bytes); CryptographicOperations.ZeroMemory(bytes);
        Assert.True(authority.TryConsume(token));
        using var disposed = new FirstRunSetupProof(RandomNumberGenerator.GetBytes(32)); disposed.Dispose();
        Assert.False(disposed.TryConsume(token));
    }
}
