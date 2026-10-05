using System.Threading;
using System.Threading.Tasks;
using BitigMail.LocalHost.Imap.Transfer;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class CompositeTargetVerificationTests
{
    [Fact]
    public async Task DefaultFallback_PreservesThreeOperationsAndOrder()
    {
        var client = new BridgeFakeTransferClient();

        var result = await ((IImapTransferClient)client).VerifyTargetItemAsync("Target", 99, "token", CancellationToken.None);

        Assert.Equal(client.UidValidity, result.UidValidity);
        Assert.False(result.Message.Exists);
        Assert.Empty(result.MatchingKeywordUids);
        Assert.Equal(new[] { "uidvalidity", "fetch", "search" }, client.VerificationOperations);
        Assert.Equal(1, client.UidValidityCallCount);
        Assert.Equal(1, client.TargetVerificationCallCount);
        Assert.Equal(1, client.KeywordSearchCallCount);
    }

    [Fact]
    public async Task DefaultFallback_PropagatesCancellationBeforeAnyOperation()
    {
        var client = new BridgeFakeTransferClient();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ((IImapTransferClient)client).VerifyTargetItemAsync("Target", 99, "token", cts.Token));

        Assert.Empty(client.VerificationOperations);
    }
}
