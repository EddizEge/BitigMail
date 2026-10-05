using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class AuthenticatedSessionRegistryTests
{
    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Now;
        public void Advance(TimeSpan duration) => Now += duration.Ticks;
    }
    [Fact] public void IssuesUniquePrincipalBoundTokensAndRejectsModifiedToken()
    {
        var registry = new AuthenticatedSessionRegistry(); var one = registry.Issue("operator", 1); var two = registry.Issue("operator", 1);
        Assert.NotEqual(one.Token, two.Token); Assert.NotEqual(one.Principal.SessionId, two.Principal.SessionId);
        Assert.DoesNotContain(one.Token, one.ToString());
        Assert.Equal(one.Principal, registry.Validate(one.Token, _ => 1));
        Assert.Null(registry.Validate(" " + one.Token, _ => 1));
        Assert.Null(registry.Validate(new string('A', 64), _ => 1));
        Assert.Null(new AuthenticatedSessionRegistry().Validate(one.Token, _ => 1));
    }
    [Fact] public void IdleExpiryIsExactAndActivityExtendsOnlyIdleDeadline()
    {
        var clock = new Clock(); var registry = new AuthenticatedSessionRegistry(clock); var token = registry.Issue("u", 1).Token;
        clock.Advance(TimeSpan.FromMinutes(29)); Assert.NotNull(registry.Validate(token, _ => 1));
        clock.Advance(TimeSpan.FromMinutes(30)); Assert.Null(registry.Validate(token, _ => 1));
    }
    [Fact] public void ActiveSessionsStillExpireAtEightHours()
    {
        var clock = new Clock(); var registry = new AuthenticatedSessionRegistry(clock); var token = registry.Issue("u", 1).Token;
        for (int i = 0; i < 47; i++) { clock.Advance(TimeSpan.FromMinutes(10)); Assert.NotNull(registry.Validate(token, _ => 1)); }
        clock.Advance(TimeSpan.FromMinutes(10)); Assert.Null(registry.Validate(token, _ => 1));
    }
    [Fact] public void SecurityVersionChangeOrDeletedUserRevokesSessions()
    {
        var registry = new AuthenticatedSessionRegistry(); var token = registry.Issue("u", 1).Token;
        Assert.Null(registry.Validate(token, _ => 2)); Assert.Null(registry.Validate(token, _ => 1));
        token = registry.Issue("u", 2).Token; Assert.Null(registry.Validate(token, _ => null));
    }
    [Fact] public void LogoutDuringCatalogReadCannotResurrectSession()
    {
        var registry = new AuthenticatedSessionRegistry(); var session = registry.Issue("u", 1);
        Assert.Null(registry.Validate(session.Token, _ => { registry.Revoke(session.Token); return 1; }));
    }
    [Fact] public void UserRevocationDoesNotRevokeAnotherUser()
    {
        var registry = new AuthenticatedSessionRegistry(); var one = registry.Issue("a", 1); var two = registry.Issue("b", 1);
        registry.RevokeUser("a"); Assert.Null(registry.Validate(one.Token, _ => 1)); Assert.NotNull(registry.Validate(two.Token, _ => 1));
    }
    [Fact] public void CapacityIsBoundedAndExpiredSlotsAreReclaimed()
    {
        var clock = new Clock(); var registry = new AuthenticatedSessionRegistry(clock, 1); registry.Issue("u", 1);
        Assert.Throws<InvalidOperationException>(() => registry.Issue("v", 1));
        clock.Advance(TimeSpan.FromMinutes(30)); Assert.NotNull(registry.Issue("v", 1));
    }
    [Fact] public void PerUserLimitIsEnforcedUnderConcurrentIssue()
    {
        var registry = new AuthenticatedSessionRegistry(); int issued = 0;
        Parallel.For(0, 30, _ => { try { registry.Issue("u", 1); Interlocked.Increment(ref issued); } catch (InvalidOperationException) { } });
        Assert.Equal(10, issued);
    }
    [Fact] public void BackgroundSessionCheckDoesNotExtendIdleLifetimeOrAcceptAlteredPrincipal()
    {
        var clock = new Clock(); var registry = new AuthenticatedSessionRegistry(clock); var session = registry.Issue("user", 1);
        Assert.True(registry.IsActive(session.Principal));
        Assert.False(registry.IsActive(session.Principal with { UserId = "other" }));
        Assert.False(registry.IsActive(session.Principal with { SecurityVersion = 2 }));
        clock.Advance(TimeSpan.FromMinutes(29)); Assert.True(registry.IsActive(session.Principal));
        clock.Advance(TimeSpan.FromMinutes(1)); Assert.False(registry.IsActive(session.Principal));
        var fresh = registry.Issue("user", 1); registry.Revoke(fresh.Token); Assert.False(registry.IsActive(fresh.Principal));
    }
}
