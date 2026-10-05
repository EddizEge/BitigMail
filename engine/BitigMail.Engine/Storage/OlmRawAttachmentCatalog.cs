using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace BitigMail.Engine.Storage;

public sealed record OlmRawAttachment(string EntryPath, string Name, string ContentType, string ContentId = "");
public sealed record OlmRawMessage(string XmlEntryPath, int PhysicalOrdinal, string MessageId,
    IReadOnlyList<OlmRawAttachment> Attachments, string SentTimeLiteral = "", string ReceivedTimeLiteral = "",
    string SourceXmlSha256 = "");

/// <summary>
/// Physical attachment preservation companion, not a complete OLM-to-MIME converter.
/// Never extracts ZIP entry paths onto the filesystem or reserializes embedded emails.
/// </summary>
public sealed class OlmRawAttachmentCatalog : IDisposable
{
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries;
    private readonly int _maxPayloadBytes;
    private const int MaxXmlBytes = 16 * 1024 * 1024;

    public OlmRawAttachmentCatalog(Stream source, int maxPayloadBytes = 64 * 1024 * 1024,
        int maxEntries = 250_000, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (maxPayloadBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));
        if (maxEntries < 1) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("OLM kaynağı okunabilir ve aranabilir olmalıdır.", nameof(source));
        _maxPayloadBytes = maxPayloadBytes;
        _archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen);
        try
        {
            if (_archive.Entries.Count > maxEntries)
                throw new InvalidDataException("OLM kayıt sayısı izin verilen sınırı aşıyor.");
            _entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            foreach (var entry in _archive.Entries)
            {
                // Repeated directory markers occur in real OLM files; no payload ambiguity there.
                if (entry.FullName.EndsWith('/')) continue;
                ValidateEntryPath(entry.FullName);
                if (!_entries.TryAdd(entry.FullName, entry))
                    throw new InvalidDataException("OLM aynı yolda birden fazla fiziksel içerik barındırıyor.");
            }
        }
        catch
        {
            _archive.Dispose();
            throw;
        }
    }

    public IEnumerable<OlmRawMessage> EnumerateMessages(CancellationToken cancellationToken = default)
    {
        foreach (var entry in _entries.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.FullName.Contains("/com.microsoft.__Messages/", StringComparison.Ordinal) ||
                !entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.Contains("/com.microsoft.__Attachments/", StringComparison.Ordinal)) continue;
            var bytes = ReadBounded(entry, MaxXmlBytes, cancellationToken);
            using var input = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = MaxXmlBytes, MaxCharactersFromEntities = 0
            });
            var document = XDocument.Load(reader, LoadOptions.None);
            if (document.Root?.Name != "emails")
                throw new InvalidDataException("OLM ileti XML yapısı desteklenmiyor.");
            int ordinal = 0;
            foreach (var email in document.Root.Elements("email"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ordinal++;
                var attachments = new List<OlmRawAttachment>();
                foreach (var attachment in email.Elements("OPFMessageCopyAttachmentList").Elements("messageAttachment"))
                {
                    string path = (string?)attachment.Attribute("OPFAttachmentURL") ?? "";
                    ValidateEntryPath(path);
                    if (!_entries.ContainsKey(path))
                        throw new InvalidDataException("OLM özgün ek içeriği bulunamadı.");
                    attachments.Add(new(path, (string?)attachment.Attribute("OPFAttachmentName") ?? "",
                        (string?)attachment.Attribute("OPFAttachmentContentType") ?? "application/octet-stream",
                        (string?)attachment.Attribute("OPFAttachmentContentID") ?? ""));
                }
                yield return new(entry.FullName, ordinal,
                    (string?)email.Element("OPFMessageCopyMessageID") ?? "", attachments,
                    (string?)email.Element("OPFMessageCopySentTime") ?? "",
                    (string?)email.Element("OPFMessageCopyReceivedTime") ?? "",
                    Convert.ToHexString(SHA256.HashData(bytes)));
            }
        }
    }

    /// <summary>Exact XML file bytes for a provenance sidecar, not reconstructed XML or an inferred timezone.</summary>
    public byte[] ReadSourceXml(OlmRawMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateEntryPath(message.XmlEntryPath);
        if (!_entries.TryGetValue(message.XmlEntryPath, out var entry))
            throw new InvalidDataException("OLM kaynak XML kaydı bulunamadı.");
        byte[] bytes = ReadBounded(entry, MaxXmlBytes, cancellationToken);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), message.SourceXmlSha256, StringComparison.Ordinal))
            throw new InvalidDataException("OLM kaynak XML kaydı katalogla eşleşmiyor.");
        return bytes;
    }

    public byte[] ReadPayload(OlmRawAttachment attachment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        ValidateEntryPath(attachment.EntryPath);
        if (!_entries.TryGetValue(attachment.EntryPath, out var entry))
            throw new InvalidDataException("OLM özgün ek içeriği bulunamadı.");
        return ReadBounded(entry, _maxPayloadBytes, cancellationToken);
    }

    private static byte[] ReadBounded(ZipArchiveEntry entry, int limit, CancellationToken ct)
    {
        if (entry.Length > limit) throw new InvalidDataException("OLM içeriği izin verilen boyutu aşıyor.");
        using var stream = entry.Open();
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            if (output.Length + read > limit) throw new InvalidDataException("OLM açılmış içeriği izin verilen boyutu aşıyor.");
            output.Write(buffer, 0, read);
        }
        if (output.Length != entry.Length) throw new InvalidDataException("OLM içeriği kesilmiş veya boyutu uyuşmuyor.");
        return output.ToArray();
    }

    private static void ValidateEntryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || path.StartsWith('/') ||
            path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl) ||
            path.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("OLM içerik yolu güvenli değil.");
    }

    public void Dispose() => _archive.Dispose();
}
