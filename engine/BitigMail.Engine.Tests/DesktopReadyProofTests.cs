using System;
using System.Security.Cryptography;
using BitigMail.Engine.Distribution;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class DesktopReadyProofTests
{
    [Fact]
    public void ReadyMessageBindsOwnedStartupAndPortWithoutPublishingProof()
    {
        byte[] proof = RandomNumberGenerator.GetBytes(32);
        string message = DesktopReadyProof.CreateLine(proof, 6274);
        Assert.True(DesktopReadyProof.VerifyLine(proof, 6274, message));
        Assert.False(DesktopReadyProof.VerifyLine(proof, 6275, message));
        Assert.False(DesktopReadyProof.VerifyLine(RandomNumberGenerator.GetBytes(32), 6274, message));
        Assert.DoesNotContain(Convert.ToHexString(proof), message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("BITIGMAIL_READY 6274")]
    [InlineData("HTTP/1.1 200 OK")]
    [InlineData("BITIGMAIL_READY 6274 ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    public void RejectsUnprovenReadiness(string? message) => Assert.False(DesktopReadyProof.VerifyLine(new byte[32], 6274, message));

    [Fact]
    public void RejectsModifiedOrExtraProtocolText()
    {
        byte[] proof = RandomNumberGenerator.GetBytes(32);
        string message = DesktopReadyProof.CreateLine(proof, 6274);
        Assert.False(DesktopReadyProof.VerifyLine(proof, 6274, message + "\n"));
        Assert.False(DesktopReadyProof.VerifyLine(proof, 6274, " " + message));
        Assert.False(DesktopReadyProof.VerifyLine(proof, 6274, message[..^1] + (message[^1] == '0' ? '1' : '0')));
    }
}
