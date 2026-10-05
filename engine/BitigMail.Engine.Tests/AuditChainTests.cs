using BitigMail.Engine.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class AuditChainTests
{
    private static AuditEvent Event => new(new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero),
        "usr_123", "archive.export", "cmp_123", "prj_123", "job_123", AuditOutcome.Succeeded);
    private static AuditChainEntry[] Chain()
    {
        var first = AuditChain.Append(AuditChain.Empty, Event);
        return new[] { first, AuditChain.Append(new(first.Sequence, first.Hash), Event with { Outcome = AuditOutcome.Denied }) };
    }
    private static AuditChainHead Head(AuditChainEntry[] chain) => new(chain[^1].Sequence, chain[^1].Hash);

    [Fact]
    public void ValidChainHasStableHashAndHead()
    {
        var chain = Chain();
        Assert.Equal(chain[0].Hash, Chain()[0].Hash);
        Assert.Equal(Head(chain), AuditChain.Verify(chain, Head(chain)));
        Assert.Equal(AuditChain.Empty, AuditChain.Verify(Array.Empty<AuditChainEntry>(), AuditChain.Empty));
    }

    [Fact]
    public void ChangedActionAndScopeAreDetected()
    {
        var chain = Chain(); var expected = Head(chain);
        chain[1] = chain[1] with { Event = Event with { CompanyId = "cmp_other" } };
        Assert.Throws<InvalidDataException>(() => AuditChain.Verify(chain, expected));
        chain = Chain(); chain[0] = chain[0] with { Event = Event with { ActionCode = "archive.delete" } };
        Assert.Throws<InvalidDataException>(() => AuditChain.Verify(chain, expected));
    }

    [Fact]
    public void MissingSuffixWholeFileReorderingAndDuplicateAreDetected()
    {
        var chain = Chain(); var expected = Head(chain);
        Assert.Throws<InvalidDataException>(() => AuditChain.Verify(chain.Take(1), expected));
        Assert.Throws<InvalidDataException>(() => AuditChain.Verify(Array.Empty<AuditChainEntry>(), expected));
        Assert.Throws<InvalidDataException>(() => AuditChain.Verify(chain.Reverse(), expected));
        Assert.Throws<InvalidDataException>(() => AuditChain.Verify(new[] { chain[0], chain[0] }, expected));
    }

    [Theory]
    [InlineData("someone@example.com")]
    [InlineData("C:\\private\\data")]
    [InlineData("Bearer token")]
    [InlineData("line\nsecond")]
    [InlineData("")]
    public void FreeTextCannotBeUsedAsAnIdentifier(string value)
        => Assert.Throws<InvalidDataException>(() => AuditChain.Append(AuditChain.Empty, Event with { ActorId = value }));

    [Fact]
    public void InvalidDateEnumHeadAndBoundsFailClosed()
    {
        Assert.Throws<InvalidDataException>(() => AuditChain.Append(AuditChain.Empty, Event with { AtUtc = Event.AtUtc.ToOffset(TimeSpan.FromHours(3)) }));
        Assert.Throws<InvalidDataException>(() => AuditChain.Append(AuditChain.Empty, Event with { Outcome = (AuditOutcome)88 }));
        Assert.Throws<InvalidDataException>(() => AuditChain.Append(new(0, new string('A', 64)), Event));
        var chain = Chain();
        Assert.Throws<InvalidDataException>(() => AuditChain.Verify(chain, Head(chain), maximumEntries: 1));
        Assert.ThrowsAny<OperationCanceledException>(() => AuditChain.Verify(chain, Head(chain), new CancellationToken(true)));
    }

    [Fact]
    public void LocalHeadDoesNotAuthenticatePublisher()
    {
        // A privileged writer replacing BOTH chain and head can construct a consistent different history.
        var changed = AuditChain.Append(AuditChain.Empty, Event with { ActionCode = "different.action" });
        Assert.Equal(new(changed.Sequence, changed.Hash), AuditChain.Verify(new[] { changed }, new(changed.Sequence, changed.Hash)));
    }
}
