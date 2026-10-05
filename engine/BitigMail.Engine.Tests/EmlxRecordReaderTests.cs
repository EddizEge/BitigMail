using System.Text;
using BitigMail.Engine.Storage;
using Xunit;

namespace BitigMail.Engine.Tests;

public class EmlxRecordReaderTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void PreservesUtf8AndMixedLineEndingsAndOpaqueMetadata(string delimiter)
    {
        byte[] mime = Encoding.UTF8.GetBytes("Subject: İstanbul\r\nContent-Type: text/plain; charset=utf-8\n\nİleti\r\n\0Son");
        byte[] metadata = Encoding.UTF8.GetBytes("\n<?xml version=\"1.0\"?><!DOCTYPE plist SYSTEM \"https://invalid.test/never-fetch\"><plist/>\n");
        using var stream = Input(mime, metadata, delimiter);
        var record = EmlxRecordReader.Read(stream);
        Assert.Equal(mime, record.RawMimeBytes);
        Assert.Equal(metadata, record.AppleMetadataBytes);
        Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("0\n")]
    [InlineData("-1\n")]
    [InlineData("+1\nx")]
    [InlineData(" 1\nx")]
    [InlineData("1 \nx")]
    [InlineData("١\nx")]
    [InlineData("1\rx")]
    [InlineData("999999999999999999999999999\n")]
    [InlineData("000000000000000000001\nx")]
    public void RejectsMalformedAndExcessiveCountWithoutAllocating(string text)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        Assert.Throws<InvalidDataException>(() => EmlxRecordReader.Read(stream));
    }

    [Fact]
    public void RejectsTruncationAndConfiguredSizeLimits()
    {
        using var truncated = new MemoryStream("9\nshort"u8.ToArray());
        Assert.Throws<InvalidDataException>(() => EmlxRecordReader.Read(truncated));
        using var excessive = new MemoryStream("11\n"u8.ToArray());
        Assert.Throws<InvalidDataException>(() => EmlxRecordReader.Read(excessive, maxMessageBytes: 10));
        using var metadata = new MemoryStream("1\nx12"u8.ToArray());
        Assert.Throws<InvalidDataException>(() => EmlxRecordReader.Read(metadata, maxMetadataBytes: 1));
    }

    [Fact]
    public void SupportsUnseekableShortReadsAndNoMetadata()
    {
        using var stream = new ShortReadStream("4\nabcd"u8.ToArray());
        var record = EmlxRecordReader.Read(stream, maxMessageBytes: 4, maxMetadataBytes: 0);
        Assert.Equal("abcd"u8.ToArray(), record.RawMimeBytes);
        Assert.Empty(record.AppleMetadataBytes);
    }

    [Fact]
    public void RejectsPartialFileBeforeAttemptingToOpenIt() =>
        Assert.Throws<InvalidDataException>(() => EmlxRecordReader.ReadFile("missing.partial.emlx"));

    [Fact]
    public void CancellationLeavesCallerStreamOpen()
    {
        using var stream = new MemoryStream("1\nx"u8.ToArray());
        Assert.Throws<OperationCanceledException>(() => EmlxRecordReader.Read(stream, cancellationToken: new CancellationToken(true)));
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void SyntheticCorpusPreservesAll12PhysicalMessagesAndMetadata()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "fixtures", "emlx-corpus-v1");
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        var messages = manifest.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(12, messages.Length);
        var contentHashes = new List<string>();
        foreach (var entry in messages)
        {
            string path = Path.Combine(root, entry.GetProperty("relativePath").GetString()!);
            var record = EmlxRecordReader.ReadFile(path);
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(record.RawMimeBytes));
            contentHashes.Add(hash);
            Assert.Equal(entry.GetProperty("sourceSha256").GetString(), hash);
            Assert.Equal(entry.GetProperty("metadataSha256").GetString(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(record.AppleMetadataBytes)));
            Assert.Equal(entry.GetProperty("wrapperSha256").GetString(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
        }
        Assert.Equal(11, contentHashes.Distinct().Count());
    }

    private static MemoryStream Input(byte[] mime, byte[] metadata, string delimiter) =>
        new(Encoding.ASCII.GetBytes(mime.Length + delimiter).Concat(mime).Concat(metadata).ToArray());

    private sealed class ShortReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 1));
    }
}
