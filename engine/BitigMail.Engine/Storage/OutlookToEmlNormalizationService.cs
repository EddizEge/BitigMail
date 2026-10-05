using System.Security.Cryptography;
using System.Text.Json;
using Aspose.Email.Mapi;
using Aspose.Email.Storage.Olm;
using Aspose.Email.Storage.Pst;
using MimeKit;

namespace BitigMail.Engine.Storage;

public sealed record OutlookEmlItem(int PhysicalOrdinal, string FolderPath, string SourceIdentity,
    string OutputRelativePath, string OutputSha256, int AttachmentCount, int RawAttachmentsRestored,
    string Qualification, string? ProvenanceRelativePath = null, OutlookFidelityComparison? Fidelity = null,
    string? OriginalXmlSha256 = null);
public sealed record OutlookFidelityComparison(IReadOnlyList<string> ExactFields, IReadOnlyList<string> QualifiedDifferences,
    IReadOnlyList<string> UnmeasuredFields, int SourceAttachments, int ExactAttachments,
    string? SourceSentTimeLiteral = null, string? SourceReceivedTimeLiteral = null, string? OutputDateValue = null);
public sealed record OutlookEmlFailure(int PhysicalOrdinal, string FolderPath, string SourceIdentity, string ErrorType, string ErrorMessage);
public sealed record OutlookEmlReport(string Status, string SourceFormat, string SourceSha256Before,
    string SourceSha256After, string OutputPath, int MailItems, int ExcludedNonMailItems, int FailedItems,
    IReadOnlyList<OutlookEmlItem> Items, IReadOnlyList<OutlookEmlFailure> Failures, IReadOnlyList<string> Warnings,
    int SchemaVersion = 1);

public sealed class OutlookToEmlNormalizationService
{
    internal Func<int, string, Exception?>? BeforeItemWriteHook { get; set; }
    internal Func<int, string, Exception?>? BeforeProvenanceWriteHook { get; set; }
    public OutlookEmlReport Normalize(string sourcePath, string outputRoot, string jobId,
        IDiskCapacityProbe capacityProbe, CancellationToken ct = default, string? expectedSourceSha256 = null, string? expectedFormat = null)
    {
        sourcePath = Path.GetFullPath(sourcePath); outputRoot = Path.GetFullPath(outputRoot);
        RejectChain(sourcePath); RejectChain(outputRoot);
        var sourceInfo = new FileInfo(sourcePath);
        if (!sourceInfo.Exists) throw new FileNotFoundException("Outlook kaynak dosyası bulunamadı.");
        string format = sourceInfo.Extension.ToLowerInvariant() switch { ".pst" => "pst", ".ost" => "ost", ".olm" => "olm", _ => throw new InvalidDataException("Yalnız PST, OST veya OLM kaynağı desteklenir.") };
        string before = HashFile(sourcePath, ct);
        if (expectedFormat is not null && !string.Equals(format, expectedFormat, StringComparison.Ordinal) ||
            expectedSourceSha256 is not null && !string.Equals(before, expectedSourceSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Kaynak dosya önizlemeden sonra değişti veya kimliği uyuşmuyor.");
        string? blocker = DiskCapacityPlanning.CapacityBlocker(checked(sourceInfo.Length * 3 + 256L * 1024 * 1024), capacityProbe.Probe(outputRoot));
        if (blocker is not null) throw new InvalidOperationException(blocker);
        string finalPath = Path.Combine(outputRoot, $"{format.ToUpperInvariant()}-normalized-{jobId}");
        string staging = Path.Combine(outputRoot, $".{format}-{jobId}.{Guid.NewGuid():N}.tmp");
        if (Directory.Exists(finalPath) || File.Exists(finalPath)) throw new IOException("Çıktı hedefi zaten mevcut; üzerine yazılmaz.");
        Directory.CreateDirectory(staging);
        try
        {
            (List<OutlookEmlItem> items, List<OutlookEmlFailure> failures, int excluded) = format == "olm"
                ? ExtractOlm(sourcePath, staging, ct) : ExtractPst(sourcePath, staging, ct);
            string after = HashFile(sourcePath, ct);
            if (before != after) throw new InvalidDataException("Kaynak dosya işlem sırasında değişti.");
            var warnings = format == "olm"
                ? new List<string> { "OLM özgün XML ve tarih metinleri provenance olarak saklandı.", "Aspose öğlen saat ayrıştırma belirsizliği nedeniyle tarih alanları tam sadakat olarak onaylanmadı.", "Deneme sürümü ekleri kaldırılmadı." }
                : new List<string> { "MAPI→MIME dönüşümü whole-message byte-exact değildir.", "Deneme sürümü ekleri kaldırılmadı." };
            string status = items.Count == 0 ? "failed" : failures.Count > 0 ? "partially_completed_with_qualification" : "completed_with_qualification";
            var report = new OutlookEmlReport(status, format, before, after,
                finalPath, items.Count, excluded, failures.Count, items, failures, warnings);
            WriteNew(Path.Combine(staging, "bitigmail-outlook-eml-manifest.json"), JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true }));
            Directory.Move(staging, finalPath);
            return report;
        }
        catch { try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { } throw; }
    }

    private (List<OutlookEmlItem>, List<OutlookEmlFailure>, int) ExtractPst(string source, string staging, CancellationToken ct)
    {
        var items = new List<OutlookEmlItem>(); var failures = new List<OutlookEmlFailure>(); int ordinal = 0, excluded = 0;
        using var fs = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var storage = PersonalStorage.FromStream(new NonClosingStream(fs), new PersonalStorageLoadOptions { Writable = false, LeaveStreamOpen = true });
        var folderMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void ValidateTree(FolderInfo folder, string parent)
        {
            if (!string.IsNullOrEmpty(folder.DisplayName)) ValidateSourceComponent(folder.DisplayName);
            else if (!string.IsNullOrEmpty(parent)) throw new InvalidDataException("Kaynak klasör adı boş olamaz.");
            string folderPath = CombineFolder(parent, folder.DisplayName);
            RegisterFolderMapping(folderMappings, folderPath);
            foreach (FolderInfo child in folder.GetSubFolders()) ValidateTree(child, folderPath);
        }
        void Visit(FolderInfo folder, string parent)
        {
            ct.ThrowIfCancellationRequested(); string folderPath = CombineFolder(parent, folder.DisplayName);
            foreach (var info in folder.EnumerateMessages())
            {
                ct.ThrowIfCancellationRequested(); int physicalOrdinal = ++ordinal; string identity = info.EntryIdString ?? physicalOrdinal.ToString();
                try
                {
                    using var message = storage.ExtractMessage(info);
                    if (!string.Equals(message.MessageClass, "IPM.Note", StringComparison.OrdinalIgnoreCase)) { excluded++; continue; }
                    items.Add(WriteMapi(message, staging, physicalOrdinal, folderPath, identity, 0, null, "MAPI_TO_MIME; GENERIC_FIELDS_AND_ATTACHMENTS_COMPARED; QUALIFIED_DIFFERENCES_REPORTED", ct));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { failures.Add(new(physicalOrdinal, folderPath, identity, ex.GetType().Name, ex.Message)); }
            }
            foreach (FolderInfo child in folder.GetSubFolders()) Visit(child, folderPath);
        }
        ValidateTree(storage.RootFolder, "");
        Visit(storage.RootFolder, ""); return (items, failures, excluded);
    }

    private (List<OutlookEmlItem>, List<OutlookEmlFailure>, int) ExtractOlm(string source, string staging, CancellationToken ct)
    {
        using var rawStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var catalog = new OlmRawAttachmentCatalog(rawStream);
        var raw = catalog.EnumerateMessages(ct).ToArray();
        var rawGroups = raw.GroupBy(r => Key(RawFolder(r.XmlEntryPath), r.MessageId)).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        if (rawGroups.Any(g => g.Value.Length != 1)) throw new InvalidDataException("OLM ham kaynakta klasör + Message-ID eşlemesi belirsiz.");
        int excluded = 0; var sdkCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        using (var storage = new OlmStorage(source))
        {
            ValidateOlmFolderMappings(storage);
            VisitOlm(storage, (folder, message) =>
            {
                if (!string.Equals(message.MessageClass, "IPM.Note", StringComparison.OrdinalIgnoreCase)) { excluded++; return; }
                string key = Key(folder, message.InternetMessageId); sdkCounts[key] = sdkCounts.GetValueOrDefault(key) + 1;
            }, ct);
        }
        ValidateOlmIdentityCardinality(rawGroups.ToDictionary(x => x.Key, x => x.Value.Length, StringComparer.Ordinal), sdkCounts);
        var items = new List<OutlookEmlItem>(); var failures = new List<OutlookEmlFailure>(); int ordinal = 0;
        using (var storage = new OlmStorage(source)) VisitOlm(storage, (folder, message) =>
        {
            if (!string.Equals(message.MessageClass, "IPM.Note", StringComparison.OrdinalIgnoreCase)) return;
            int physicalOrdinal = ++ordinal; string identity = message.InternetMessageId ?? physicalOrdinal.ToString(); string? provenancePath = null; string? emittedPath = null;
            try
            {
                var original = rawGroups[Key(folder, message.InternetMessageId)][0];
                if (message.Attachments.Count != original.Attachments.Count)
                    throw new InvalidDataException("OLM SDK ve ham kaynak ek kardinalitesi eşleşmiyor.");
                var rawPayloads = original.Attachments.Select(a => catalog.ReadPayload(a, ct)).ToArray();
                string provenance = Path.Combine(SafeFolder(folder), $"{physicalOrdinal:D6}.olm-source.xml");
                var item = WriteMapi(message, staging, physicalOrdinal, folder, identity, original.Attachments.Count,
                    (mime) => RestoreRawAttachments(mime, original, rawPayloads), "OLM_MAPI_TO_MIME; RAW_ATTACHMENTS_EXACT; DATE_FIDELITY_UNRESOLVED", ct, provenance, rawPayloads, original.Attachments,
                    original.SentTimeLiteral, original.ReceivedTimeLiteral);
                emittedPath = ResolveUnder(staging, item.OutputRelativePath);
                provenancePath = ResolveUnder(staging, provenance); Directory.CreateDirectory(Path.GetDirectoryName(provenancePath)!);
                var provenanceFailure = BeforeProvenanceWriteHook?.Invoke(physicalOrdinal, identity); if (provenanceFailure is not null) throw provenanceFailure;
                byte[] originalXml = catalog.ReadSourceXml(original, ct); WriteNew(provenancePath, originalXml);
                items.Add(item with { OriginalXmlSha256 = Convert.ToHexString(SHA256.HashData(originalXml)) });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                if (provenancePath is not null && File.Exists(provenancePath)) File.Delete(provenancePath);
                if (emittedPath is not null && File.Exists(emittedPath)) File.Delete(emittedPath);
                failures.Add(new(physicalOrdinal, folder, identity, ex.GetType().Name, ex.Message));
            }
        }, ct);
        return (items, failures, excluded);
    }

    private static void VisitOlm(OlmStorage storage, Action<string, MapiMessage> action, CancellationToken ct)
    {
        void Visit(IEnumerable<OlmFolder> folders, string parent, int depth)
        {
            if (depth > 100) throw new InvalidDataException("OLM klasör derinliği sınırı aşıldı.");
            foreach (var folder in folders) { string path = CombineFolder(parent, folder.Name); foreach (var message in storage.EnumerateMessages(folder)) { ct.ThrowIfCancellationRequested(); using (message) action(path, message); } Visit(folder.SubFolders, path, depth + 1); }
        }
        Visit(storage.FolderHierarchy, "", 0);
    }

    private static void ValidateOlmFolderMappings(OlmStorage storage)
    {
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Visit(IEnumerable<OlmFolder> folders, string parent, int depth)
        {
            if (depth > 100) throw new InvalidDataException("OLM klasör derinliği sınırı aşıldı.");
            foreach (var folder in folders)
            {
                ValidateSourceComponent(folder.Name);
                string path = CombineFolder(parent, folder.Name);
                RegisterFolderMapping(mappings, path);
                Visit(folder.SubFolders, path, depth + 1);
            }
        }
        Visit(storage.FolderHierarchy, "", 0);
    }

    private OutlookEmlItem WriteMapi(MapiMessage message, string root, int ordinal, string folder, string identity,
        int rawCount, Action<MimeMessage>? mutate, string qualification, CancellationToken ct, string? provenance = null,
        IReadOnlyList<byte[]>? expectedRawPayloads = null, IReadOnlyList<OlmRawAttachment>? expectedRawMetadata = null,
        string? sourceSentTimeLiteral = null, string? sourceReceivedTimeLiteral = null)
    {
        string relative = Path.Combine(SafeFolder(folder), $"{ordinal:D6}-message.eml"); string path = ResolveUnder(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var injected = BeforeItemWriteHook?.Invoke(ordinal, identity); if (injected is not null) throw injected;
        try
        {
            using var converted = new MemoryStream(); message.Save(converted, Aspose.Email.SaveOptions.DefaultEml); converted.Position = 0;
            var mime = MimeMessage.Load(converted, ct); mutate?.Invoke(mime);
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { mime.WriteTo(output, ct); output.Flush(true); }
            using var verify = File.OpenRead(path); var reopened = MimeMessage.Load(verify, ct);
            if (expectedRawPayloads is not null && expectedRawMetadata is not null) VerifyRawAttachments(path, expectedRawPayloads, expectedRawMetadata, ct);
            var fidelity = CompareGenericFields(message, reopened, expectedRawPayloads, expectedRawMetadata, qualification.Contains("DATE_FIDELITY_UNRESOLVED", StringComparison.Ordinal), ct, sourceSentTimeLiteral, sourceReceivedTimeLiteral);
            return new(ordinal, folder, identity, relative, HashFile(path, ct), CountAttachments(reopened), rawCount, qualification, provenance, fidelity);
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }

    private static OutlookFidelityComparison CompareGenericFields(MapiMessage source, MimeMessage emitted, IReadOnlyList<byte[]>? restoredPayloads,
        IReadOnlyList<OlmRawAttachment>? restoredMetadata, bool dateUnresolved, CancellationToken ct,
        string? sourceSentTimeLiteral, string? sourceReceivedTimeLiteral)
    {
        var exact = new List<string>(); var differences = new List<string>();
        void Compare(string field, string sourceValue, string emittedValue)
        { if (string.Equals(sourceValue, emittedValue, StringComparison.Ordinal)) exact.Add(field); else differences.Add($"{field}:representation-difference"); }
        Compare("subject", source.Subject ?? "", emitted.Subject ?? "");
        Compare("message-id", NormalizeMessageId(source.InternetMessageId), NormalizeMessageId(emitted.MessageId));
        Compare("sender-address", source.SenderEmailAddress ?? "", emitted.From.Mailboxes.FirstOrDefault()?.Address ?? "");
        Compare("sender-name", source.SenderName ?? "", emitted.From.Mailboxes.FirstOrDefault()?.Name ?? "");
        Compare("to", RecipientSignature(source, MapiRecipientType.MAPI_TO), AddressSignature(emitted.To.Mailboxes));
        Compare("cc", RecipientSignature(source, MapiRecipientType.MAPI_CC), AddressSignature(emitted.Cc.Mailboxes));
        Compare("bcc", RecipientSignature(source, MapiRecipientType.MAPI_BCC), AddressSignature(emitted.Bcc.Mailboxes));
        string sourceBody = NormalizeText(source.Body ?? ""); string emittedBody = NormalizeText(emitted.TextBody ?? emitted.HtmlBody ?? "");
        if (sourceBody == emittedBody) exact.Add("body"); else if (sourceBody.Length > 0 && emittedBody.Contains(sourceBody, StringComparison.Ordinal)) differences.Add("body:source-retained-with-representation-additions"); else differences.Add("body:representation-difference");
        DateTime? sourceDate = OstSelectionEngine.ExtractMessageDate(source);
        if (dateUnresolved) differences.Add("date:semantic-fidelity-unresolved-source-literals-preserved");
        else if (sourceDate.HasValue && Math.Abs((emitted.Date.UtcDateTime - sourceDate.Value.ToUniversalTime()).TotalSeconds) <= 1) exact.Add("date");
        else differences.Add("date:representation-difference");
        var emittedPayloads = ReadAttachmentPayloads(emitted, ct);
        var sourceAttachments = source.Attachments.Cast<MapiAttachment>().ToArray();
        bool hasUnknownSourcePayload = restoredPayloads is null && sourceAttachments.Any(a => a.BinaryData is null);
        var sourcePayloads = restoredPayloads ?? sourceAttachments.Where(a => a.BinaryData is not null).Select(a => a.BinaryData!).ToArray();
        bool payloadsMatch = AttachmentPayloadsMatch(sourcePayloads, emittedPayloads, hasUnknownSourcePayload);
        int exactAttachments = payloadsMatch ? sourcePayloads.Count : 0;
        if (payloadsMatch) exact.Add("attachment-payloads"); else differences.Add(hasUnknownSourcePayload ? "attachment-payloads:source-binary-unavailable" : "attachment-payloads:difference");
        var emittedMetadata = emitted.BodyParts.OfType<MimePart>().Where(p => !string.IsNullOrEmpty(p.FileName) || p.IsAttachment || !string.IsNullOrEmpty(p.ContentId))
            .Select(p => $"{p.FileName ?? ""}\0{(p.ContentId ?? "").Trim('<', '>')}\0{(p.ContentDisposition?.Disposition == ContentDisposition.Inline ? "inline" : "attachment")}").OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var sourceMetadata = restoredMetadata is not null
            ? restoredMetadata.Select(a => $"{a.Name}\0{a.ContentId.Trim('<', '>')}\0{(string.IsNullOrWhiteSpace(a.ContentId) ? "attachment" : "inline")}").OrderBy(x => x, StringComparer.Ordinal).ToArray()
            : sourceAttachments.Select(a => $"{a.LongFileName ?? a.FileName ?? a.DisplayName ?? ""}\0{(AttachmentHelper.GetAttachmentContentId(a) ?? "").Trim('<', '>')}\0{(a.IsInline ? "inline" : "attachment")}").OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (sourceMetadata.SequenceEqual(emittedMetadata, StringComparer.Ordinal)) exact.Add("attachment-name-cid-disposition"); else differences.Add("attachment-name-cid-disposition:difference");
        var unmeasured = new List<string> { "MAPI named properties", "RTF compressed body", "storage entry identifiers", "attachment MAPI flags beyond inline disposition" };
        string sourceHtml = source.BodyHtml ?? "";
        if (string.IsNullOrEmpty(sourceHtml)) unmeasured.Add("HTML body absent in source MAPI view");
        else if (NormalizeText(sourceHtml) == NormalizeText(emitted.HtmlBody ?? "")) exact.Add("html-body"); else differences.Add("html-body:representation-difference");
        if (hasUnknownSourcePayload) unmeasured.Add("attachment payload with unavailable SDK BinaryData");
        return new(exact, differences, unmeasured, restoredPayloads?.Count ?? sourceAttachments.Length, exactAttachments,
            sourceSentTimeLiteral, sourceReceivedTimeLiteral, emitted.Date.ToString("O"));
    }
    private static string NormalizeMessageId(string? value) => (value ?? "").Trim().Trim('<', '>');
    private static string RecipientSignature(MapiMessage message, MapiRecipientType type) => JsonSerializer.Serialize(message.Recipients.Cast<MapiRecipient>().Where(r => r.RecipientType == type).Select(r => new[] { r.DisplayName ?? "", r.EmailAddress ?? "" }).ToArray());
    private static string AddressSignature(IEnumerable<MailboxAddress> addresses) => JsonSerializer.Serialize(addresses.Select(a => new[] { a.Name ?? "", a.Address ?? "" }).ToArray());
    private static string NormalizeText(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\r', '\n');
    private static IReadOnlyList<byte[]> ReadAttachmentPayloads(MimeMessage message, CancellationToken ct)
    {
        var payloads = new List<byte[]>();
        foreach (var part in message.BodyParts.OfType<MimePart>().Where(p => !string.IsNullOrEmpty(p.FileName) || p.IsAttachment || !string.IsNullOrEmpty(p.ContentId)))
        { using var decoded = new MemoryStream(); part.Content?.DecodeTo(decoded, ct); payloads.Add(decoded.ToArray()); }
        return payloads;
    }
    private static IEnumerable<string> MultisetHashes(IEnumerable<byte[]> payloads) => payloads.Select(p => Convert.ToHexString(SHA256.HashData(p))).OrderBy(x => x, StringComparer.Ordinal);
    internal static bool AttachmentPayloadsMatch(IReadOnlyList<byte[]> sourcePayloads, IReadOnlyList<byte[]> emittedPayloads, bool hasUnknownSourcePayload) =>
        !hasUnknownSourcePayload && sourcePayloads.Count == emittedPayloads.Count && MultisetHashes(sourcePayloads).SequenceEqual(MultisetHashes(emittedPayloads), StringComparer.Ordinal);

    private static void RestoreRawAttachments(MimeMessage mime, OlmRawMessage raw, IReadOnlyList<byte[]> payloads)
    {
        static bool Attachment(MimeEntity entity) => entity is MessagePart || entity is MimePart p &&
            (!string.IsNullOrEmpty(p.FileName) || p.IsAttachment || !string.IsNullOrEmpty(p.ContentId));
        static void RemoveAttachments(Multipart multipart)
        {
            foreach (var entity in multipart.ToArray())
            {
                if (Attachment(entity)) multipart.Remove(entity);
                else if (entity is Multipart nested) RemoveAttachments(nested);
            }
        }
        if (mime.Body is Multipart existing) RemoveAttachments(existing);
        var mixed = new Multipart("mixed"); if (mime.Body is not null) mixed.Add(mime.Body); mime.Body = mixed;
        for (int index = 0; index < raw.Attachments.Count; index++)
        {
            var source = raw.Attachments[index]; byte[] bytes = payloads[index];
            ContentType type;
            try { type = ContentType.Parse(source.ContentType); } catch { type = new ContentType("application", "octet-stream"); }
            var part = new MimePart(type) { Content = new MimeContent(new MemoryStream(bytes, false)), FileName = source.Name,
                ContentDisposition = new ContentDisposition(string.IsNullOrWhiteSpace(source.ContentId) ? ContentDisposition.Attachment : ContentDisposition.Inline), ContentTransferEncoding = ContentEncoding.Binary };
            if (!string.IsNullOrWhiteSpace(source.ContentId)) part.ContentId = source.ContentId.Trim('<', '>');
            mixed.Add(part);
        }
    }
    private static void VerifyRawAttachments(string path, IReadOnlyList<byte[]> expected, IReadOnlyList<OlmRawAttachment> metadata, CancellationToken ct)
    {
        var options = ParserOptions.Default.Clone(); options.RegisterMimeType("message/rfc822", typeof(OpaqueAttachedMessage));
        using var stream = File.OpenRead(path); var message = MimeMessage.Load(options, stream, ct);
        var parts = message.BodyParts.OfType<MimePart>().Where(p => !string.IsNullOrEmpty(p.FileName) || p.IsAttachment || !string.IsNullOrEmpty(p.ContentId)).ToArray();
        if (parts.Length != expected.Count) throw new InvalidDataException("Yazılmış EML özgün ek kardinalitesiyle eşleşmiyor.");
        for (int i = 0; i < parts.Length; i++)
        {
            using var decoded = new MemoryStream(); (parts[i].Content ?? throw new InvalidDataException("Yazılmış EML ek içeriği eksik.")).DecodeTo(decoded, ct);
            string expectedCid = metadata[i].ContentId.Trim('<', '>');
            if (!decoded.ToArray().AsSpan().SequenceEqual(expected[i]) || parts[i].FileName != metadata[i].Name ||
                parts[i].ContentType.MimeType != metadata[i].ContentType || (parts[i].ContentId ?? "") != expectedCid ||
                parts[i].ContentDisposition?.Disposition != (string.IsNullOrEmpty(expectedCid) ? ContentDisposition.Attachment : ContentDisposition.Inline))
                throw new InvalidDataException("Yazılmış EML özgün OLM ek baytları veya metadata alanlarıyla eşleşmiyor.");
        }
    }
    public sealed class OpaqueAttachedMessage(MimeEntityConstructorArgs args) : MimePart(args);
    private static string RawFolder(string xmlPath)
    {
        const string marker = "/com.microsoft.__Messages/"; int at = xmlPath.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) throw new InvalidDataException("OLM ham klasör yolu çözümlenemedi.");
        string tail = xmlPath[(at + marker.Length)..]; string? dir = Path.GetDirectoryName(tail.Replace('/', Path.DirectorySeparatorChar));
        return CombineFolder("", (dir ?? "").Replace(Path.DirectorySeparatorChar, '/'));
    }
    private static string Key(string folder, string? id) => folder + "\0" + (id ?? "");
    private static void ValidateOlmIdentityCardinality(IReadOnlyDictionary<string, int> rawCounts, IReadOnlyDictionary<string, int> sdkCounts)
    {
        if (rawCounts.Any(k => k.Value != 1) || sdkCounts.Any(k => k.Value != 1) || sdkCounts.Count != rawCounts.Count ||
            sdkCounts.Keys.Any(k => !rawCounts.ContainsKey(k)) || rawCounts.Keys.Any(k => !sdkCounts.ContainsKey(k)))
            throw new InvalidDataException("OLM SDK ve ham kaynak klasör + Message-ID bire bir eşleşmiyor.");
    }
    private static string CombineFolder(string parent, string? name) => "/" + string.Join('/', (parent + "/" + (name ?? "")).Split('/', StringSplitOptions.RemoveEmptyEntries));
    private static string SafeFolder(string folder) => string.Join(Path.DirectorySeparatorChar, folder.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(EncodeComponent));
    private static void ValidateSourceComponent(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Contains('/') || value.Contains('\\'))
            throw new InvalidDataException("Kaynak klasör adı boş olamaz veya dizin ayırıcı içeremez.");
    }
    private static void RegisterFolderMapping(Dictionary<string, string> mappings, string sourceFolder)
    {
        string outputFolder = SafeFolder(sourceFolder);
        if (mappings.TryGetValue(outputFolder, out string? existing) && !string.Equals(existing, sourceFolder, StringComparison.Ordinal))
            throw new InvalidDataException("Farklı kaynak klasörleri aynı güvenli çıktı klasörüne eşleniyor.");
        mappings[outputFolder] = sourceFolder;
    }
    private static string EncodeComponent(string value)
    {
        if (value is "." or "..") throw new InvalidDataException("Nokta/traversal klasör bileşeni kabul edilmez.");
        string encoded = string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsControl(c) || c == '%' ? $"%{(int)c:X4}" : c.ToString()));
        while (encoded.EndsWith(' ') || encoded.EndsWith('.')) { char trailing = encoded[^1]; encoded = encoded[..^1] + $"%{(int)trailing:X4}"; }
        if (string.IsNullOrEmpty(encoded)) throw new InvalidDataException("Boş klasör bileşeni kabul edilmez.");
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "LPT1" }.Contains(encoded, StringComparer.OrdinalIgnoreCase)) encoded = $"%{(int)encoded[0]:X4}" + encoded[1..];
        return encoded;
    }
    private static string ResolveUnder(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("Köklü çıktı yolu kabul edilmez.");
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Çıktı yolu hedef kökün dışına çıkıyor.");
        return full;
    }
    private static int CountAttachments(MimeMessage message) => MimeAttachmentInventory.CountTopLevelAttachments(message);
    private static void WriteNew(string path, byte[] bytes) { using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); fs.Write(bytes); fs.Flush(true); }
    private static string HashFile(string path, CancellationToken ct) { using var fs = File.OpenRead(path); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] b = new byte[81920]; int n; while ((n = fs.Read(b)) > 0) { ct.ThrowIfCancellationRequested(); hash.AppendData(b, 0, n); } return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(); }
    private static void RejectChain(string path) { FileSystemInfo? x = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path); while (x is not null) { if ((x.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse kaynak/hedef kabul edilmez."); x = x is DirectoryInfo d ? d.Parent : ((FileInfo)x).Directory; } }
}
