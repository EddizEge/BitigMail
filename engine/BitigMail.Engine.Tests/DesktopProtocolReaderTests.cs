using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Distribution;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class DesktopProtocolReaderTests
{
    [Fact]
    public async Task KeepsConsecutiveLinesAndSignalsParentEof()
    {
        using var reader = new StringReader("READY\r\nEXIT\nlast");
        Assert.Equal("READY", await DesktopProtocolReader.ReadLineAsync(reader));
        Assert.Equal("EXIT", await DesktopProtocolReader.ReadLineAsync(reader));
        Assert.Equal("last", await DesktopProtocolReader.ReadLineAsync(reader));
        Assert.Null(await DesktopProtocolReader.ReadLineAsync(reader));
    }

    [Fact]
    public async Task RejectsOversizedInputBeforeConsumingWholeLine()
    {
        using var reader = new StringReader(new string('x', 100_000));
        await Assert.ThrowsAsync<InvalidDataException>(() => DesktopProtocolReader.ReadLineAsync(reader, maximumCharacters: 128));
        Assert.Equal('x', reader.Peek());
    }

    [Fact]
    public async Task HonorsCancellationBeforeReading()
    {
        using var reader = new StringReader("EXIT\n");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DesktopProtocolReader.ReadLineAsync(reader, new CancellationToken(true)));
        Assert.Equal('E', reader.Peek());
    }
}
