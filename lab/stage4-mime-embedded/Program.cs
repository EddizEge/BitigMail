using BitigMail.Engine.Storage;
using MimeKit;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Length != 2) throw new ArgumentException("source OLM and new evidence JSON required");
using var input = File.OpenRead(args[0]);
using var catalog = new OlmRawAttachmentCatalog(input);
var payloads = catalog.EnumerateMessages().SelectMany(m => m.Attachments)
    .Where(a => a.ContentType.Equals("message/rfc822", StringComparison.OrdinalIgnoreCase))
    .Select(a => (a.Name, Bytes: catalog.ReadPayload(a))).ToList();
payloads.Add(("synthetic-mixed.eml", Encoding.UTF8.GetBytes("From: sender@example.test\r\nSubject: İstanbul\n\tcontinued\r\n\r\nMixed\nline\r\nending\n")));
var results = new List<object>();
foreach (var payload in payloads)
{
    const string boundary = "bitig-embedded-probe-boundary";
    using var sourceStream = new MemoryStream(payload.Bytes, writable: false);
    var part = new MimePart("message", "rfc822")
    {
        Content = new MimeContent(sourceStream, ContentEncoding.Default),
        ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
        ContentTransferEncoding = ContentEncoding.Binary,
        FileName = payload.Name
    };
    var multipart = new Multipart("mixed") { Boundary = boundary };
    multipart.Add(new TextPart("plain") { Text = "Read-only fidelity probe" });
    multipart.Add(part);
    using var message = new MimeMessage { Subject = "probe", Body = multipart };
    using var output = new MemoryStream();
    var options = FormatOptions.Default.Clone();
    options.NewLineFormat = NewLineFormat.Dos;
    message.WriteTo(options, output);
    byte[] serialized = output.ToArray();
    int occurrences = 0;
    for (int p = 0; p <= serialized.Length - payload.Bytes.Length; p++)
        if (serialized.AsSpan(p, payload.Bytes.Length).SequenceEqual(payload.Bytes)) occurrences++;
    output.Position = 0;
    using var reopened = MimeMessage.Load(output);
    var reopenedPart = reopened.Attachments.Single();
    var parserOptions = ParserOptions.Default.Clone();
    parserOptions.RegisterMimeType("message/rfc822", typeof(OpaqueAttachedMessage));
    output.Position = 0;
    using var opaqueReopened = MimeMessage.Load(parserOptions, output);
    var opaquePart = (MimePart)opaqueReopened.Attachments.Single();
    using var opaqueBytes = new MemoryStream();
    (opaquePart.Content ?? throw new InvalidDataException("Opaque attached message has no content")).DecodeTo(opaqueBytes);
    results.Add(new { payload.Name, bytes = payload.Bytes.Length,
        sourceHash = Convert.ToHexString(SHA256.HashData(payload.Bytes)),
        exactRawOccurrences = occurrences, parsedType = reopenedPart.GetType().Name,
        opaqueDecodedExact = opaqueBytes.ToArray().AsSpan().SequenceEqual(payload.Bytes),
        opaqueDecodedBytes = opaqueBytes.Length,
        contentType = reopenedPart.ContentType.MimeType,
        qualification = "Binary message/rfc822 raw serialization probe only; service transport acceptance not tested." });
}
using var evidence = new FileStream(args[1], FileMode.CreateNew, FileAccess.Write);
JsonSerializer.Serialize(evidence, results, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(JsonSerializer.Serialize(results));

public sealed class OpaqueAttachedMessage(MimeEntityConstructorArgs args) : MimePart(args);
