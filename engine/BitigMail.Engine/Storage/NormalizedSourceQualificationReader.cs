using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BitigMail.Engine.Models;

namespace BitigMail.Engine.Storage;

public sealed record NormalizedSourceQualification(
    string Fingerprint, int VerifiedOutputCount, bool DateFilterBlocked, bool IsPartial,
    IReadOnlyList<string> Warnings);

/// <summary>Verifies our local normalization provenance. Integrity is not publisher authenticity.</summary>
public sealed class NormalizedSourceQualificationReader
{
    public const int MaximumEntries = 100_000;
    public const int MaximumManifestBytes = 32 * 1024 * 1024;
    private static readonly string[] ManifestNames =
        ["bitigmail-outlook-eml-manifest.json", "bitigmail-emlx-manifest.json", "bitigmail-pop-snapshot-manifest.json", "bitigmail-recovery-manifest.json", "bitigmail-selected-archive-manifest.json"];

    public NormalizedSourceQualification? Read(MimeSourceManifest manifest, CancellationToken ct = default) =>
        ReadManifestSource(manifest.SourceKind, manifest.RootPath, manifest.Entries.Select(x => x.CanonicalPath), ct);

    public NormalizedSourceQualification? ReadManifestSource(string sourceKind, string rootPath, IEnumerable<string> selectedPaths, CancellationToken ct = default)
    {
        if (sourceKind.Equals("mbox", StringComparison.OrdinalIgnoreCase)) return null;
        if (!sourceKind.Equals("eml-files", StringComparison.OrdinalIgnoreCase)) return Read(rootPath, ct);
        var parts = selectedPaths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(path => (Path: path, Qualification: Read(path, ct))).Where(x => x.Qualification is not null).ToArray();
        if (parts.Length == 0) return null;
        string fingerprintInput = string.Join("\n", parts.Select(x => x.Path + "\n" + x.Qualification!.Fingerprint));
        return new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput))), parts.Sum(x => x.Qualification!.VerifiedOutputCount),
            parts.Any(x => x.Qualification!.DateFilterBlocked), parts.Any(x => x.Qualification!.IsPartial),
            parts.SelectMany(x => x.Qualification!.Warnings).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    public NormalizedSourceQualification? Read(string selectedPath, CancellationToken ct = default)
    {
        string selected = Path.GetFullPath(selectedPath);
        RejectReparseChain(selected);
        bool directory = Directory.Exists(selected);
        if (!directory && !File.Exists(selected)) throw new FileNotFoundException("Seçilen kaynak bulunamadı.");
        var emls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int visited = 0;
        void Walk(string path, int depth)
        {
            ct.ThrowIfCancellationRequested();
            if (depth > 100) throw new InvalidDataException("Kaynak klasör derinliği sınırı aşıldı.");
            RejectReparseChain(path);
            foreach (string child in Directory.EnumerateFileSystemEntries(path))
            {
                ct.ThrowIfCancellationRequested();
                if (++visited > MaximumEntries) throw new InvalidDataException("Kaynak tarama sınırı aşıldı.");
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Bağlantılı kaynak yolları desteklenmez.");
                if (Directory.Exists(child)) Walk(child, depth + 1);
                else if (ManifestNames.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase)) manifests.Add(Path.GetFullPath(child));
                else if (Path.GetExtension(child).Equals(".eml", StringComparison.OrdinalIgnoreCase)) emls.Add(Path.GetFullPath(child));
                if (emls.Count + manifests.Count > MaximumEntries) throw new InvalidDataException("Kaynak öğe sınırı aşıldı.");
            }
        }
        if (directory) Walk(selected, 0);
        else if (Path.GetExtension(selected).Equals(".eml", StringComparison.OrdinalIgnoreCase)) emls.Add(selected);
        else return null;

        // A selection inside a generated tree must not lose its parent's qualifications.
        for (string? parent = directory ? selected : Path.GetDirectoryName(selected); parent is not null; parent = Directory.GetParent(parent)?.FullName)
            foreach (string name in ManifestNames)
            {
                string candidate = Path.Combine(parent, name);
                if (File.Exists(candidate)) manifests.Add(candidate);
            }
        if (manifests.Count == 0) return null;
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        var fingerprints = new List<string>();
        bool datesBlocked = false, partial = false;
        int totalEntries = 0;
        foreach (string manifestPath in manifests.OrderBy(x => x, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested(); RejectReparseChain(manifestPath);
            using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumManifestBytes) throw new InvalidDataException("Dönüşüm kayıt boyutu sınırı aşıldı.");
            using var bytes = new MemoryStream(); stream.CopyTo(bytes);
            if (bytes.Length > MaximumManifestBytes) throw new InvalidDataException("Dönüşüm kaydı büyüdü.");
            using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            JsonElement root = document.RootElement;
            RejectDuplicateProperties(root);
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Geçersiz dönüşüm kaydı.");
            // Current historical manifests have implicit schema 0. Only explicit schema 1 is forward supported.
            bool explicitSchemaOne = root.TryGetProperty("SchemaVersion", out var schema);
            if (explicitSchemaOne && (!schema.TryGetInt32(out int version) || version != 1))
                throw new InvalidDataException("Dönüşüm kayıt sürümü desteklenmiyor.");
            string manifestRoot = Path.GetDirectoryName(manifestPath)!;
            bool outlook = Path.GetFileName(manifestPath).Equals(ManifestNames[0], StringComparison.OrdinalIgnoreCase);
            bool pop = Path.GetFileName(manifestPath).Equals(ManifestNames[2], StringComparison.OrdinalIgnoreCase);
            bool recovery = Path.GetFileName(manifestPath).Equals(ManifestNames[3], StringComparison.OrdinalIgnoreCase);
            bool selectedArchive = Path.GetFileName(manifestPath).Equals(ManifestNames[4], StringComparison.OrdinalIgnoreCase);
            if (selectedArchive && !explicitSchemaOne) throw new InvalidDataException("Seçili arşiv kayıt sürümü eksik.");
            if (recovery && !explicitSchemaOne) throw new InvalidDataException("Kurtarma kayıt sürümü eksik.");
            string status = RequiredString(root, recovery ? "Outcome" : "Status");
            string format = selectedArchive ? "selected-archive" : outlook ? RequiredString(root, "SourceFormat") : recovery ? "recovery" : pop ? "pop" : "emlx";
            if (outlook && format is not ("pst" or "ost" or "olm")) throw new InvalidDataException("Dönüşüm kaynak biçimi geçersiz.");
            if (recovery ? status is not ("healthy_extraction" or "partial_recovered") : selectedArchive ? status != "completed_with_qualification" : outlook ? status is not ("completed_with_qualification" or "partially_completed_with_qualification") : pop ? status != "completed_with_qualification" : status != "completed")
                throw new InvalidDataException("Başarılı çıktısı olmayan dönüşüm yeniden kullanılamaz.");
            string arrayName = recovery ? "Messages" : outlook || pop ? "Items" : "Entries";
            if (!root.TryGetProperty(arrayName, out var entries) || entries.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Dönüşüm öğe listesi eksik.");
            totalEntries = checked(totalEntries + entries.GetArrayLength());
            if (totalEntries > MaximumEntries) throw new InvalidDataException("Dönüşüm öğe sayısı sınırı aşıldı.");
            string countName = outlook ? "MailItems" : pop ? "WrittenItems" : "ItemsWritten";
            if (!recovery && (!root.TryGetProperty(countName, out var count) || !count.TryGetInt32(out int expectedCount) || expectedCount != entries.GetArrayLength()))
                throw new InvalidDataException("Dönüşüm öğe sayısı kayıtla uyuşmuyor.");
            partial |= status.StartsWith("partially_", StringComparison.Ordinal) || status == "partial_recovered";
            datesBlocked |= format is "olm" or "emlx" or "recovery";
            if (selectedArchive)
            {
                if (!root.TryGetProperty("DateFilterBlocked", out var blocked) || blocked.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                    !root.TryGetProperty("IsPartial", out var selectedPartial) || selectedPartial.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                    !root.TryGetProperty("QualificationWarnings", out var selectedWarnings) || selectedWarnings.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Seçili arşiv yeterlilik kaydı eksik.");
                datesBlocked |= blocked.GetBoolean();
                partial |= selectedPartial.GetBoolean();
                foreach (var warning in selectedWarnings.EnumerateArray())
                    if (warning.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(warning.GetString())) throw new InvalidDataException("Seçili arşiv uyarısı geçersiz.");
                    else warnings.Add(warning.GetString()!);
            }
            if (recovery)
            {
                if (!root.TryGetProperty("EnumerationComplete", out var enumeration) || enumeration.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                    !root.TryGetProperty("Failures", out var failures) || failures.ValueKind != JsonValueKind.Array ||
                    status == "healthy_extraction" && (!enumeration.GetBoolean() || failures.GetArrayLength() != 0) ||
                    entries.GetArrayLength() == 0)
                    throw new InvalidDataException("Kurtarma kapsam kaydı tutarsız.");
                warnings.Add("Hasarlı PST/OST kurtarma çıktısı: özgün toplam ve tarih sadakati kesin değildir; tarih filtresi kullanılamaz.");
                warnings.Add("Yalnız doğrulanan kurtarma çıktıları seçilmiştir; okunamayan bölgeler tamamlanmış sayılmaz.");
            }
            if (!selectedArchive)
                warnings.Add(format switch { "olm" => "OLM tarih anlamı doğrulanmadı; özgün XML ve tarih kayıtları korunmuştur. Tarih filtresi kullanılamaz.", "emlx" => "Apple ek bilgileri yorumlanmadan korunmuştur; özgün tarih ve işaret aktarımı doğrulanmadı. Tarih filtresi kullanılamaz.", "pop" => "POP kaynağında klasör, sunucu internal date ve read flag bulunmaz; RFC ileti tarihi dışında bu metadata üretilmedi.", _ => "PST/OST dönüşümü birebir ham ileti kopyası değildir; deneme sürümü ve alan temsil farkları bulunabilir." });
            if (pop) { warnings.Add("POP RETR akışı özgün mailbox-on-disk baytları olarak sunulmaz."); warnings.Add("Kaynak postalar sunucuda bırakıldı; DELE kullanılmadı."); }
            if (partial) warnings.Add("Kaynak dönüşüm kısmi sonuç içeriyor; yalnız doğrulanan çıktılar yeniden kullanılabilir.");
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                string output = ResolveRelative(manifestRoot, RequiredString(entry, pop || recovery ? "RelativePath" : "OutputRelativePath"));
                string hash = RequiredString(entry, pop || recovery ? "Sha256" : "OutputSha256");
                if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c)) || !Path.GetExtension(output).Equals(".eml", StringComparison.OrdinalIgnoreCase) || !declared.Add(output))
                    throw new InvalidDataException("Dönüşüm çıktı yolu veya hash kaydı geçersiz/çakışıyor.");
                if (!emls.Contains(output))
                {
                    if (directory && Within(output, selected)) throw new InvalidDataException("Kayıtlı dönüşüm çıktısı eksik.");
                    continue;
                }
                if (!covered.Add(output)) throw new InvalidDataException("Bir ileti birden fazla dönüşüm kaydıyla eşleşiyor.");
                RejectReparseChain(output);
                using var raw = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (raw.Length > EmlxRecordReader.DefaultMaxMessageBytes) throw new InvalidDataException("İleti boyutu sınırı aşıldı.");
                string actual = Hash(raw, ct);
                if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Dönüştürülmüş ileti kayıtlı hash ile uyuşmuyor.");
                fingerprints.Add(output + "\n" + actual);
                if (format is "olm" or "emlx")
                {
                    string sidecar = ResolveRelative(manifestRoot, RequiredString(entry, outlook ? "ProvenanceRelativePath" : "MetadataRelativePath"));
                    RejectReparseChain(sidecar);
                    using var metadata = new FileStream(sidecar, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (metadata.Length > (outlook ? 16 * 1024 * 1024 : EmlxRecordReader.DefaultMaxMetadataBytes))
                        throw new InvalidDataException("Dönüşüm ek kayıt boyutu sınırı aşıldı.");
                    string metadataHash = Hash(metadata, ct);
                    if (!outlook && !metadataHash.Equals(RequiredString(entry, "MetadataSha256"), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Apple ek kayıt hash değeri uyuşmuyor.");
                    if (outlook && explicitSchemaOne && !metadataHash.Equals(RequiredString(entry, "OriginalXmlSha256"), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("OLM özgün XML hash değeri uyuşmuyor.");
                    // Legacy OLM reports have no original XML hash. Freeze current sidecar bytes, never call this original authenticity.
                    fingerprints.Add(sidecar + "\n" + metadataHash);
                }
            }
            foreach (string eml in emls)
                if (Within(eml, manifestRoot) && !declared.Contains(eml)) throw new InvalidDataException("Dönüşüm klasöründe kayıtsız ileti bulundu.");
            fingerprints.Add(manifestPath + "\n" + Convert.ToHexString(SHA256.HashData(bytes.ToArray())));
        }
        if (fingerprints.Count == 0) return null;
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", fingerprints.OrderBy(x => x, StringComparer.Ordinal)))));
        return new(fingerprint, covered.Count, datesBlocked, partial, warnings.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    private static string RequiredString(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(field.GetString())
            ? field.GetString()! : throw new InvalidDataException($"Dönüşüm kayıt alanı eksik: {name}");

    private static string ResolveRelative(string root, string relative)
    {
        var parts = relative.Split(['/', '\\']);
        if (Path.IsPathRooted(relative) || parts.Any(p => p.Length == 0 || p is "." or ".." || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || p.EndsWith('.') || p.EndsWith(' ')))
            throw new InvalidDataException("Dönüşüm çıktı yolu güvenli değil.");
        string full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!Within(full, root)) throw new InvalidDataException("Dönüşüm çıktı yolu kaynak dışına çıkıyor.");
        return full;
    }
    private static bool Within(string path, string root) => path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void RejectReparseChain(string path)
    {
        for (string? part = path; part is not null; part = Directory.GetParent(part)?.FullName)
            if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Bağlantılı kaynak yolları desteklenmez.");
    }
    private static string Hash(Stream stream, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024]; int count;
        while ((count = stream.Read(buffer)) != 0) { ct.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Dönüşüm kaydı yinelenen alan içeriyor.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) RejectDuplicateProperties(child);
    }
}
