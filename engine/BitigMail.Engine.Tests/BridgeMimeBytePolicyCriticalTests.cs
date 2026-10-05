using System.Security.Cryptography;
using System.Text;
using BitigMail.Engine.Storage;
using BitigMail.Engine.Imap;
using MimeKit;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class BridgeMimeBytePolicyCriticalTests
{
    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    [Fact]
    public void Canonicalization_PreservesHeaderFoldingTrailingBlankLinesAndInput()
    {
        var raw = Bytes("Subject: İstanbul\n\tfolded\nX-Unknown: value\n\nbody\n\n\n");
        var copy = raw.ToArray();
        var result = BridgeMimeBytePolicy.CanonicalizeForImap(raw);
        Assert.Equal(Bytes("Subject: İstanbul\r\n\tfolded\r\nX-Unknown: value\r\n\r\nbody\r\n\r\n\r\n"), result.Bytes);
        Assert.Equal(copy, raw);
        Assert.True(result.ConvertedLfToCrLf);
        Assert.False(result.AddedTerminalNewline);
        Assert.NotEqual(result.OriginalSha256, result.CanonicalSha256);
    }

    [Fact]
    public void CrLfAlreadyCanonical_IsExactCopyAndReportsNoChanges()
    {
        var raw = Bytes("Subject: x\r\n\r\nFrom a\r\n\r\n");
        var result = BridgeMimeBytePolicy.CanonicalizeForImap(raw);
        Assert.Equal(raw, result.Bytes);
        Assert.NotSame(raw, result.Bytes);
        Assert.Equal(result.OriginalSha256, result.CanonicalSha256);
        Assert.False(result.ConvertedLfToCrLf);
        Assert.False(result.AddedTerminalNewline);
    }

    [Theory]
    [InlineData("Subject: x\n\nbody", "Subject: x\r\n\r\nbody\r\n")]
    [InlineData("Subject: x\r\n\r\nbody", "Subject: x\r\n\r\nbody\r\n")]
    public void MissingFinalNewline_IsAddedExactlyOnceAndReported(string input, string expected)
    {
        var result = BridgeMimeBytePolicy.CanonicalizeForImap(Bytes(input));
        Assert.Equal(Bytes(expected), result.Bytes);
        Assert.True(result.AddedTerminalNewline);
        Assert.False(BridgeMimeBytePolicy.CanonicalizeForImap(result.Bytes).AddedTerminalNewline);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Subject: x\n\nbody\r")]
    [InlineData("Subject: x\n\nbody\0")]
    [InlineData("Subject: x\nbody")]
    [InlineData("From broken\nSubject: x\n\nbody\n")]
    [InlineData(" folded\n\nbody\n")]
    [InlineData("Bad Name: x\n\nbody\n")]
    public void AmbiguousContent_FailsBeforeWritingAnyMboxBytes(string input)
    {
        var raw = Bytes(input);
        Assert.Throws<InvalidDataException>(() => BridgeMimeBytePolicy.CanonicalizeForImap(raw));
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => BridgeMimeBytePolicy.WriteMboxrdRecord(output, raw, DateTimeOffset.UnixEpoch));
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void MboxEscaping_PreservesFromDepthDuplicateMessagesAndTrailingBlankLines(string nl)
    {
        string source = string.Join(nl, new[] { "Subject: From x", "X-Test: >From header", "", "From a", ">From b", ">>From c", "Fromage", " tail", "", "" });
        var raw = Bytes(source);
        using var output = new MemoryStream();
        var a = BridgeMimeBytePolicy.WriteMboxrdRecord(output, raw, DateTimeOffset.UnixEpoch);
        BridgeMimeBytePolicy.WriteMboxrdRecord(output, raw, DateTimeOffset.UnixEpoch);
        var written = Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("X-Test: >From header" + nl, written);
        Assert.Contains(">From a" + nl + ">>From b" + nl + ">>>From c" + nl + "Fromage", written);
        output.Position = 0;
        var records = MboxrdRecordReader.EnumerateRecords(output).ToArray();
        Assert.Equal(2, records.Length);
        Assert.All(records, r => Assert.Equal(raw, r.RawMimeBytes));
        Assert.Equal(a.OriginalSha256, a.StoredSha256);
        Assert.False(a.AddedTerminalNewline);
        Assert.Equal(raw.Length, a.OriginalLength);
    }

    [Fact]
    public void MboxMissingFinalNewline_RecordsOriginalAndStoredHashesSeparately()
    {
        var raw = Bytes("Subject: x\n\n>From tail");
        using var output = new MemoryStream();
        var record = BridgeMimeBytePolicy.WriteMboxrdRecord(output, raw, DateTimeOffset.UnixEpoch);
        output.Position = 0;
        var stored = Assert.Single(MboxrdRecordReader.EnumerateRecords(output)).RawMimeBytes;
        Assert.Equal(Bytes("Subject: x\n\n>From tail\n"), stored);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(stored)).ToLowerInvariant(), record.StoredSha256);
        Assert.NotEqual(record.OriginalSha256, record.StoredSha256);
        Assert.True(record.AddedTerminalNewline);
    }

    [Fact]
    public void MixedCrLfAndLf_OnlyBareLfChangesAndMboxKeepsRawRepresentation()
    {
        var raw = Bytes("Subject: x\r\n\r\nline1\n>From tail\r\n\n");
        var canonical = BridgeMimeBytePolicy.CanonicalizeForImap(raw);
        Assert.Equal(Bytes("Subject: x\r\n\r\nline1\r\n>From tail\r\n\r\n"), canonical.Bytes);
        Assert.True(canonical.ConvertedLfToCrLf);
        using var output = new MemoryStream();
        BridgeMimeBytePolicy.WriteMboxrdRecord(output, raw, DateTimeOffset.UnixEpoch);
        output.Position = 0;
        Assert.Equal(raw, Assert.Single(MboxrdRecordReader.EnumerateRecords(output)).RawMimeBytes);
    }

    private static string FixtureRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null)
        {
            var candidate = Path.Combine(root.FullName, "fixtures", "mail-corpus-v1");
            if (Directory.Exists(candidate)) return candidate;
            root = root.Parent;
        }
        throw new DirectoryNotFoundException("Authoritative synthetic corpus is required.");
    }

    [Fact]
    public void OriginalTwelveEmls_MailKitAppendSerializationEqualsExplicitCanonicalBytes()
    {
        var files = Directory.GetFiles(Path.Combine(FixtureRoot(), "eml"), "*.eml");
        Assert.Equal(12, files.Length);
        foreach (var path in files)
        {
            var raw = File.ReadAllBytes(path);
            var normalized = BridgeMimeBytePolicy.CanonicalizeForImap(raw);
            using var input = new MemoryStream(raw);
            using var message = MimeMessage.Load(input);
            Assert.Equal(normalized.Bytes, ImapSerializationAssumptions.Serialize(message));
            Assert.Equal(raw, File.ReadAllBytes(path));
        }
    }

    [Fact]
    public void OriginalMboxRecords_RoundTripWithoutDroppingPhysicalCopiesOrTrailingBlankLines()
    {
        using var input = File.OpenRead(Path.Combine(FixtureRoot(), "corpus.mbox"));
        var records = MboxrdRecordReader.EnumerateRecords(input).ToArray();
        Assert.Equal(12, records.Length);
        using var output = new MemoryStream();
        foreach (var record in records)
            BridgeMimeBytePolicy.WriteMboxrdRecord(output, record.RawMimeBytes, DateTimeOffset.UnixEpoch);
        output.Position = 0;
        var reopened = MboxrdRecordReader.EnumerateRecords(output).ToArray();
        Assert.Equal(records.Length, reopened.Length);
        for (int i = 0; i < records.Length; i++)
            Assert.Equal(records[i].RawMimeBytes, reopened[i].RawMimeBytes);
    }

    [Fact]
    public void OriginalMboxRecords_MailKitAppendPreservesTheirOwnTrailingBlankLines()
    {
        using var input = File.OpenRead(Path.Combine(FixtureRoot(), "corpus.mbox"));
        int count = 0;
        foreach (var record in MboxrdRecordReader.EnumerateRecords(input))
        {
            var expected = BridgeMimeBytePolicy.CanonicalizeForImap(record.RawMimeBytes);
            using var stream = new MemoryStream(record.RawMimeBytes);
            using var message = MimeMessage.Load(stream);
            Assert.Equal(expected.Bytes, ImapSerializationAssumptions.Serialize(message));
            count++;
        }
        Assert.Equal(12, count);
    }
}
