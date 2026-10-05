using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class VerifiedStreamCopyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(131073)]
    public async Task CopiesVerifiedBytes(int size)
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(size);
        using var source = new MemoryStream(bytes);
        using var destination = new MemoryStream();
        await VerifiedStreamCopy.CopyAsync(source, destination, size, Convert.ToHexString(SHA256.HashData(bytes)));
        Assert.Equal(bytes, destination.ToArray());
    }

    [Theory]
    [InlineData(9)]
    [InlineData(11)]
    public async Task RejectsChangedLength(long expected)
    {
        byte[] bytes = new byte[10];
        using var source = new MemoryStream(bytes);
        using var destination = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => VerifiedStreamCopy.CopyAsync(source, destination, expected, Convert.ToHexString(SHA256.HashData(bytes))));
    }

    [Fact]
    public async Task RejectsSameLengthTamperAndCancellation()
    {
        using var source = new MemoryStream(new byte[10]);
        using var destination = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => VerifiedStreamCopy.CopyAsync(source, destination, 10, new string('0', 64)));
        source.Position = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VerifiedStreamCopy.CopyAsync(source, destination, 10, new string('0', 64), new CancellationToken(true)));
    }
}
