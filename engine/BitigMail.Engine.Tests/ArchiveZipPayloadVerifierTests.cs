using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BitigMail.Engine.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class ArchiveZipPayloadVerifierTests
{
    private static byte[] Zip(params (string Name, string Content, int Attributes)[] entries)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            foreach (var entry in entries)
            {
                var item = zip.CreateEntry(entry.Name); item.ExternalAttributes = entry.Attributes;
                using var output = item.Open(); output.Write(Encoding.UTF8.GetBytes(entry.Content));
            }
        return bytes.ToArray();
    }
    private static ArchiveZipPayloadFile Expected(string name, string content) => new(name, Encoding.UTF8.GetByteCount(content), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))));

    [Fact]
    public async Task StreamsExactVerifiedBytesWithoutExtractingToFilesystem()
    {
        using var zip = new ZipArchive(new MemoryStream(Zip(("raw/one.eml", "İleti\r\nbody", 0))), ZipArchiveMode.Read);
        var expected = Expected("raw/one.eml", "İleti\r\nbody");
        var entries = ArchiveZipPayloadVerifier.MatchDirectory(zip, new[] { expected });
        using var output = new MemoryStream();
        await ArchiveZipPayloadVerifier.CopyVerifiedAsync(entries[expected.Path], expected, output);
        Assert.Equal("İleti\r\nbody", Encoding.UTF8.GetString(output.ToArray()));
    }

    [Theory]
    [InlineData("../outside.eml")] [InlineData("/absolute.eml")]
    [InlineData("C:/outside.eml")] [InlineData("raw/a.eml:stream")]
    [InlineData("raw/CON.eml")] [InlineData("raw/a.eml.")]
    [InlineData("raw\\one.eml")]
    public void UnsafeEntryPathIsRejected(string name)
    {
        using var zip = new ZipArchive(new MemoryStream(Zip((name, "mail", 0))), ZipArchiveMode.Read);
        Assert.Throws<InvalidDataException>(() => ArchiveZipPayloadVerifier.MatchDirectory(zip, new[] { Expected(name, "mail") }));
    }

    [Fact]
    public void CaseAndUnicodeNormalizedDuplicatesAreRejected()
    {
        foreach (var names in new[] { new[] { "raw/one.eml", "raw/ONE.eml" }, new[] { "raw/é.eml", "raw/e\u0301.eml" } })
        {
            using var zip = new ZipArchive(new MemoryStream(Zip((names[0], "mail", 0), (names[1], "mail", 0))), ZipArchiveMode.Read);
            Assert.Throws<InvalidDataException>(() => ArchiveZipPayloadVerifier.MatchDirectory(zip, names.Select(n => Expected(n, "mail")).ToArray()));
        }
    }

    [Theory]
    [InlineData(0xA0000000u)] [InlineData(0x400)] [InlineData(0x10)]
    public void LinksAndDirectoryPayloadsAreRejected(uint attributes)
    {
        using var zip = new ZipArchive(new MemoryStream(Zip(("raw/one.eml", "mail", unchecked((int)attributes)))), ZipArchiveMode.Read);
        Assert.Throws<InvalidDataException>(() => ArchiveZipPayloadVerifier.MatchDirectory(zip, new[] { Expected("raw/one.eml", "mail") }));
    }

    [Fact]
    public async Task SameLengthTamperingCannotPassPublicationCheck()
    {
        using var zip = new ZipArchive(new MemoryStream(Zip(("raw/one.eml", "evil", 0))), ZipArchiveMode.Read);
        var expected = Expected("raw/one.eml", "mail");
        var entry = ArchiveZipPayloadVerifier.MatchDirectory(zip, new[] { expected })[expected.Path];
        using var unpublished = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => ArchiveZipPayloadVerifier.CopyVerifiedAsync(entry, expected, unpublished));
    }

    [Fact]
    public async Task ExtraMissingWrongSizeAndCancelledContentFailClosed()
    {
        using var zip = new ZipArchive(new MemoryStream(Zip(("raw/one.eml", "mail", 0))), ZipArchiveMode.Read);
        var expected = Expected("raw/one.eml", "mail");
        Assert.Throws<InvalidDataException>(() => ArchiveZipPayloadVerifier.MatchDirectory(zip, Array.Empty<ArchiveZipPayloadFile>()));
        Assert.Throws<InvalidDataException>(() => ArchiveZipPayloadVerifier.MatchDirectory(zip, new[] { expected with { Path = "raw/missing.eml" } }));
        Assert.Throws<InvalidDataException>(() => ArchiveZipPayloadVerifier.MatchDirectory(zip, new[] { expected with { Length = 5 } }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArchiveZipPayloadVerifier.CopyVerifiedAsync(zip.Entries[0], expected, new MemoryStream(), new CancellationToken(true)));
    }
}
