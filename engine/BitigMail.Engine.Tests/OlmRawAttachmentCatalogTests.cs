using System.IO.Compression;
using System.Text;
using BitigMail.Engine.Storage;
using Xunit;

namespace BitigMail.Engine.Tests;

public class OlmRawAttachmentCatalogTests
{
    private const string XmlPath = "Local/com.microsoft.__Messages/Inbox/message_0.xml";
    private const string AttachmentPath = "Local/com.microsoft.__Messages/Inbox/com.microsoft.__Attachments/a";
    private static string Xml(string attachment = AttachmentPath) =>
        $"<emails><email><OPFMessageCopyMessageID>&lt;duplicate@test&gt;</OPFMessageCopyMessageID><OPFMessageCopyAttachmentList><messageAttachment OPFAttachmentURL=\"{attachment}\" OPFAttachmentName=\"mail.eml\" OPFAttachmentContentType=\"message/rfc822\"/></OPFMessageCopyAttachmentList></email></emails>";

    [Fact]
    public void PreservesEmbeddedMessageBytesAndPhysicalDuplicates()
    {
        byte[] payload = Encoding.UTF8.GetBytes("Subject: İstanbul\r\n\r\nİçerik\n");
        using var zip = Create((XmlPath, Encoding.UTF8.GetBytes(Xml())),
            (XmlPath.Replace("_0", "_1"), Encoding.UTF8.GetBytes(Xml())), (AttachmentPath, payload));
        using (var catalog = new OlmRawAttachmentCatalog(zip))
        {
            var messages = catalog.EnumerateMessages().ToArray();
            Assert.Equal(2, messages.Length);
            Assert.NotEqual(messages[0].XmlEntryPath, messages[1].XmlEntryPath);
            Assert.All(messages, m => Assert.Equal(payload, catalog.ReadPayload(Assert.Single(m.Attachments))));
        }
        Assert.True(zip.CanRead);
    }

    [Fact]
    public void PreservesOriginalDateLiteralsCidAndExactSourceXml()
    {
        string xml = Xml().Replace("<OPFMessageCopyAttachmentList>",
            "<OPFMessageCopySentTime>2018-10-17T12:16:53</OPFMessageCopySentTime><OPFMessageCopyReceivedTime>2018-10-17T12:17:03</OPFMessageCopyReceivedTime><OPFMessageCopyAttachmentList>")
            .Replace("OPFAttachmentName=", "OPFAttachmentContentID=\"&lt;image@local&gt;\" OPFAttachmentName=");
        byte[] original = Encoding.UTF8.GetBytes(xml);
        using var zip = Create((XmlPath, original), (AttachmentPath, "payload"u8.ToArray()));
        using var catalog = new OlmRawAttachmentCatalog(zip);
        var message = Assert.Single(catalog.EnumerateMessages());
        Assert.Equal("2018-10-17T12:16:53", message.SentTimeLiteral);
        Assert.Equal("2018-10-17T12:17:03", message.ReceivedTimeLiteral);
        Assert.Equal("<image@local>", Assert.Single(message.Attachments).ContentId);
        Assert.Equal(original, catalog.ReadSourceXml(message));
        Assert.Throws<InvalidDataException>(() => catalog.ReadSourceXml(message with { SourceXmlSha256 = new string('0', 64) }));
    }

    [Fact]
    public void RejectsDuplicatePhysicalFileNames()
    {
        using var zip = Create((XmlPath, "a"u8.ToArray()), (XmlPath, "b"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => new OlmRawAttachmentCatalog(zip));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("C:/secret")]
    [InlineData("Local/../escape")]
    [InlineData("Local\\escape")]
    public void RejectsUnsafeArchivePaths(string path)
    {
        using var zip = Create((path, "a"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => new OlmRawAttachmentCatalog(zip));
    }

    [Fact]
    public void MissingAttachmentNeverDisappearsSilently()
    {
        using var zip = Create((XmlPath, Encoding.UTF8.GetBytes(Xml())));
        using var catalog = new OlmRawAttachmentCatalog(zip);
        Assert.Throws<InvalidDataException>(() => catalog.EnumerateMessages().ToArray());
    }

    [Fact]
    public void RejectsXmlEntitiesWithoutResolvingExternalContent()
    {
        using var zip = Create((XmlPath, Encoding.UTF8.GetBytes("<!DOCTYPE emails [<!ENTITY x SYSTEM 'file:///secret'>]><emails><email>&x;</email></emails>")));
        using var catalog = new OlmRawAttachmentCatalog(zip);
        Assert.Throws<System.Xml.XmlException>(() => catalog.EnumerateMessages().ToArray());
    }

    [Fact]
    public void BoundsDecompressionAndHonorsCancellation()
    {
        using var zip = Create((XmlPath, Encoding.UTF8.GetBytes(Xml())), (AttachmentPath, new byte[1000]));
        using var catalog = new OlmRawAttachmentCatalog(zip, maxPayloadBytes: 100);
        var attachment = Assert.Single(Assert.Single(catalog.EnumerateMessages()).Attachments);
        Assert.Throws<InvalidDataException>(() => catalog.ReadPayload(attachment));
        Assert.Throws<OperationCanceledException>(() => catalog.EnumerateMessages(new CancellationToken(true)).ToArray());
    }

    [Fact]
    public void PublicVendorFixtureHas22EmailsAnd38OriginalPayloads()
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "fixtures", "vendor-olm", "SampleOLM.olm"));
        using var catalog = new OlmRawAttachmentCatalog(file);
        var messages = catalog.EnumerateMessages().ToArray();
        Assert.Equal(22, messages.Length);
        var attachments = messages.SelectMany(m => m.Attachments).ToArray();
        Assert.Equal(38, attachments.Length);
        Assert.All(attachments, a => Assert.NotEmpty(catalog.ReadPayload(a)));
        var expected = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "vendor-olm", "expected-payload-hashes.json")))!;
        var actual = attachments.Select(a => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(catalog.ReadPayload(a))))
            .OrderBy(hash => hash, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);
    }

    private static MemoryStream Create(params (string Path, byte[] Bytes)[] entries)
    {
        var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var entry in entries)
                using (var stream = archive.CreateEntry(entry.Path).Open()) stream.Write(entry.Bytes);
        output.Position = 0;
        return output;
    }
}
