using System.Text;
using BitigMail.Engine.Distribution;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class PackageManifestReaderTests
{
    private const string Valid = """{"SchemaVersion":1,"Product":"BitigMail","Version":"1.0.0","MinimumDataSchema":1,"MaximumDataSchema":1,"Files":[{"RelativePath":"app.exe","Length":3,"Sha256":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}]}""";
    private static Task<PackagePayloadManifest> Read(string json) => PackageManifestReader.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public async Task ExactManifestFreezesFileList()
    {
        var manifest = await Read(Valid);
        Assert.Equal("app.exe", Assert.Single(manifest.Files).RelativePath);
        Assert.Throws<NotSupportedException>(() => ((IList<PackagePayloadFile>)manifest.Files)[0] = new("other.exe", 1, "bad"));
    }

    [Theory]
    [InlineData("\"SchemaVersion\":1", "\"SchemaVersion\":1,\"SchemaVersion\":2")]
    [InlineData("\"Length\":3", "\"Length\":3,\"Length\":4")]
    [InlineData("\"Length\":3", "\"Length\":null")]
    [InlineData("\"Length\":3,", "")]
    [InlineData("\"Length\":3", "\"length\":3")]
    [InlineData("\"Length\":3", "\"Length\":3,\"ExecutableApproved\":true")]
    [InlineData("\"Product\":\"BitigMail\"", "\"Product\":null")]
    [InlineData("\"RelativePath\":\"app.exe\"", "\"RelativePath\":null")]
    [InlineData("\"SchemaVersion\":1", "\"SchemaVersion\":\"1\"")]
    public async Task AmbiguousOrIncompleteInputRejected(string oldValue, string replacement)
        => await Assert.ThrowsAsync<InvalidDataException>(() => Read(Valid.Replace(oldValue, replacement, StringComparison.Ordinal)));

    [Fact]
    public async Task NullFileAndTrailingDataRejected()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(Valid[..Valid.IndexOf("[{", StringComparison.Ordinal)] + "[null]}"));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(Valid + "{}"));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(""));
    }

    [Fact]
    public async Task NonseekableOversizeInputStopsAtBound()
    {
        using var source = new RepeatingStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => PackageManifestReader.ReadAsync(source));
        Assert.Equal(PackageManifestReader.MaximumBytes + 1L, source.BytesRead);
    }

    [Fact]
    public async Task CancellationStopsBeforeParsing()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(Valid));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PackageManifestReader.ReadAsync(source, new CancellationToken(true)));
    }

    private sealed class RepeatingStream : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { Array.Fill(buffer, (byte)' ', offset, count); BytesRead += count; return count; }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
