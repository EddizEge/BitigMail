using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MimeKit;

namespace BitigMail.Engine.Storage;

public sealed record EmlxManifestEntry(int PhysicalOrdinal, string CanonicalPath, string RelativePath,
    long SizeBytes, string SourceSha256, string MimeSha256, string MetadataSha256, int AttachmentCount);

public sealed record EmlxSourceManifest(string RootPath, IReadOnlyList<EmlxManifestEntry> Entries,
    string AggregateFingerprint, long TotalBytes, string Qualification);

public sealed record EmlxOutputEntry(int PhysicalOrdinal, string SourceRelativePath, string OutputRelativePath,
    string MetadataRelativePath, string OutputSha256, string MetadataSha256, int AttachmentCount);

public sealed record EmlxNormalizationReport(string JobId, string Status, string SourceFingerprint,
    string OutputPath, int ItemsWritten, IReadOnlyList<EmlxOutputEntry> Entries, string Qualification,
    IReadOnlyList<string> Warnings);

public sealed class EmlxNormalizationService
{
    private const long MaxPhysicalEmlxBytes = EmlxRecordReader.DefaultMaxMessageBytes + EmlxRecordReader.DefaultMaxMetadataBytes + 32L;
    public EmlxSourceManifest BuildDirectoryManifest(string rootPath, CancellationToken ct = default) =>
        BuildManifest(EnumerateTree(rootPath), Path.GetFullPath(rootPath), ct);

    public EmlxSourceManifest BuildFilesManifest(IEnumerable<string> paths, CancellationToken ct = default)
    {
        var files = paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) throw new InvalidDataException("En az bir EMLX dosyası seçilmelidir.");
        string root = CommonRoot(files);
        return BuildManifest(files, root, ct);
    }

    public EmlxNormalizationReport Normalize(EmlxSourceManifest manifest, string selectedOutputDirectory,
        string jobId, IDiskCapacityProbe capacityProbe, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(capacityProbe);
        string outputRoot = Path.GetFullPath(selectedOutputDirectory);
        if (!Directory.Exists(outputRoot)) throw new DirectoryNotFoundException("Seçilen çıktı klasörü bulunamadı.");
        RejectReparseChain(outputRoot);
        if (string.Equals(outputRoot.TrimEnd(Path.DirectorySeparatorChar), manifest.RootPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
            IsWithin(outputRoot, manifest.RootPath) || IsWithin(manifest.RootPath, outputRoot))
            throw new InvalidOperationException("Kaynak ve çıktı klasörleri birbirinin içinde olamaz.");
        Revalidate(manifest, ct);
        long required = checked(manifest.TotalBytes * 2 + 4L * 1024 * 1024);
        string? blocker = DiskCapacityPlanning.CapacityBlocker(required, capacityProbe.Probe(outputRoot));
        if (blocker is not null) throw new InvalidOperationException(blocker);

        string finalName = $"EMLX-normalized-{jobId}";
        string finalPath = Path.Combine(outputRoot, finalName);
        string staging = Path.Combine(outputRoot, $".{finalName}.{Guid.NewGuid():N}.tmp");
        if (Directory.Exists(finalPath) || File.Exists(finalPath))
            throw new IOException("Çıktı hedefi zaten mevcut; var olan çıktı üzerine yazılmaz.");
        Directory.CreateDirectory(staging);
        try
        {
            var outputs = new List<EmlxOutputEntry>();
            foreach (var entry in manifest.Entries)
            {
                ct.ThrowIfCancellationRequested();
                EnsureEntryUnchanged(entry);
                EmlxRecord record = EmlxRecordReader.ReadFile(entry.CanonicalPath, ct);
                using (var ms = new MemoryStream(record.RawMimeBytes, writable: false)) _ = MimeMessage.Load(ms, ct);
                string folder = SafeFolder(Path.GetDirectoryName(entry.RelativePath) ?? string.Empty);
                string stem = SafeName(Path.GetFileNameWithoutExtension(entry.RelativePath));
                string baseName = $"{entry.PhysicalOrdinal:D6}-{stem}";
                string relativeEml = Path.Combine(folder, baseName + ".eml");
                string relativeMeta = Path.Combine(folder, baseName + ".apple.plist");
                string emlPath = Path.Combine(staging, relativeEml);
                Directory.CreateDirectory(Path.GetDirectoryName(emlPath)!);
                WriteCreateOnly(emlPath, record.RawMimeBytes);
                WriteCreateOnly(Path.Combine(staging, relativeMeta), record.AppleMetadataBytes);
                string outputHash = HashFile(emlPath);
                if (!string.Equals(outputHash, entry.MimeSha256, StringComparison.Ordinal))
                    throw new InvalidDataException("EML çıktı baytları kaynak MIME bölümüyle eşleşmiyor.");
                string metadataHash = HashFile(Path.Combine(staging, relativeMeta));
                if (!string.Equals(metadataHash, entry.MetadataSha256, StringComparison.Ordinal))
                    throw new InvalidDataException("Apple metadata sidecar baytları kaynakla eşleşmiyor.");
                using var verify = File.OpenRead(emlPath);
                var parsed = MimeMessage.Load(verify, ct);
                int attachmentCount = CountAttachments(parsed);
                if (attachmentCount != entry.AttachmentCount) throw new InvalidDataException("MIME ek sayısı doğrulanamadı.");
                outputs.Add(new(entry.PhysicalOrdinal, entry.RelativePath, relativeEml, relativeMeta,
                    outputHash, metadataHash, attachmentCount));
            }
            Revalidate(manifest, ct);
            var report = new EmlxNormalizationReport(jobId, "completed", manifest.AggregateFingerprint,
                finalPath, outputs.Count, outputs,
                "RAW_MIME_EXACT; APPLE_METADATA_OPAQUE_SIDECAR; FLAGS_DATES_NOT_PROPAGATED",
                ["Apple Mail harici ek depoları desteklenmez.", "Apple plist baytları yorumlanmadan sidecar olarak korunur."]);
            WriteCreateOnly(Path.Combine(staging, "bitigmail-emlx-manifest.json"),
                JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true }));
            Directory.Move(staging, finalPath);
            return report;
        }
        catch
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { }
            throw;
        }
    }

    public void Revalidate(EmlxSourceManifest manifest, CancellationToken ct = default)
    {
        foreach (var entry in manifest.Entries) { ct.ThrowIfCancellationRequested(); EnsureEntryUnchanged(entry); }
    }

    private static EmlxSourceManifest BuildManifest(IEnumerable<string> paths, string root, CancellationToken ct)
    {
        root = Path.GetFullPath(root);
        RejectReparseChain(root);
        var ordered = paths.Select(Path.GetFullPath).OrderBy(p => Path.GetRelativePath(root, p), StringComparer.OrdinalIgnoreCase).ToArray();
        if (ordered.Length == 0) throw new InvalidDataException("Seçimde tamamlanmış .emlx dosyası bulunamadı.");
        if (ordered.Any(p => p.EndsWith(".partial.emlx", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Kısmi .partial.emlx dosyası bulundu; normalizasyon engellendi.");
        var entries = new List<EmlxManifestEntry>();
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int ordinal = 0;
        foreach (string path in ordered)
        {
            ct.ThrowIfCancellationRequested();
            if (!path.EndsWith(".emlx", StringComparison.OrdinalIgnoreCase)) continue;
            RejectReparse(path);
            string relative = Path.GetRelativePath(root, path);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
                throw new InvalidDataException("Kaynak kökü dışındaki EMLX dosyası kabul edilmez.");
            RejectReparseChain(path);
            var info = new FileInfo(path);
            if (info.Length > MaxPhysicalEmlxBytes)
                throw new InvalidDataException("EMLX fiziksel dosyası izin verilen ileti ve metadata sınırını aşıyor.");
            string physicalHash = HashFile(path);
            EmlxRecord record;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) record = EmlxRecordReader.Read(stream, cancellationToken: ct);
            int attachments;
            using (var ms = new MemoryStream(record.RawMimeBytes, writable: false)) attachments = CountAttachments(MimeMessage.Load(ms, ct));
            var item = new EmlxManifestEntry(++ordinal, path, relative, info.Length, physicalHash,
                Hash(record.RawMimeBytes), Hash(record.AppleMetadataBytes), attachments);
            entries.Add(item);
            aggregate.AppendData(Encoding.UTF8.GetBytes($"{item.PhysicalOrdinal}\0{item.RelativePath}\0{item.SizeBytes}\0{item.SourceSha256}\n"));
        }
        if (entries.Count == 0) throw new InvalidDataException("Seçimde tamamlanmış .emlx dosyası bulunamadı.");
        return new(root, entries, Convert.ToHexString(aggregate.GetHashAndReset()).ToLowerInvariant(),
            entries.Sum(e => e.SizeBytes), "RAW_MIME_EXACT; APPLE_METADATA_OPAQUE_SIDECAR");
    }

    private static IEnumerable<string> EnumerateTree(string rootPath)
    {
        string root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("EMLX kaynak klasörü bulunamadı.");
        RejectReparseChain(root);
        var all = EnumerateDirectorySafely(root).ToArray();
        if (all.Any(p => p.EndsWith(".partial.emlx", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Kısmi .partial.emlx dosyası bulundu; normalizasyon engellendi.");
        if (all.Any(p => p.EndsWith(".emlxpart", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Apple Mail harici ek deposu algılandı; bu kapsamda desteklenmiyor.");
        return all.Where(p => p.EndsWith(".emlx", StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureEntryUnchanged(EmlxManifestEntry entry)
    {
        RejectReparseChain(entry.CanonicalPath);
        var fi = new FileInfo(entry.CanonicalPath);
        if (!fi.Exists || fi.Length != entry.SizeBytes || !string.Equals(HashFile(fi.FullName), entry.SourceSha256, StringComparison.Ordinal))
            throw new InvalidDataException($"Kaynak değişti veya kayboldu: {entry.RelativePath}");
    }

    private static IEnumerable<string> EnumerateDirectorySafely(string root)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            string current = pending.Pop(); RejectReparse(current);
            foreach (string file in Directory.EnumerateFiles(current)) { RejectReparse(file); yield return file; }
            foreach (string dir in Directory.EnumerateDirectories(current).OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase))
            {
                RejectReparse(dir);
                if (Path.GetFileName(dir).Equals("Attachments", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Apple Mail harici ek deposu algılandı; bu kapsamda desteklenmiyor.");
                pending.Push(dir);
            }
        }
    }
    private static void RejectReparse(string path)
    {
        var attr = File.GetAttributes(path);
        if ((attr & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Bağlantılı/reparse kaynak kabul edilmez.");
    }
    private static void RejectReparseChain(string path)
    {
        FileSystemInfo? current = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Bağlantılı/reparse çıktı klasörü kabul edilmez.");
            current = current switch { DirectoryInfo directory => directory.Parent, FileInfo file => file.Directory, _ => null };
        }
    }
    private static bool IsWithin(string candidate, string root) => Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar)
        .StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string CommonRoot(string[] files)
    {
        string[] roots = files.Select(Path.GetPathRoot).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()!;
        if (roots.Length != 1) throw new InvalidDataException("Farklı disk/volume üzerindeki EMLX dosyaları tek seçimde desteklenmez.");
        string common = files.Length == 1 ? Path.GetDirectoryName(files[0])! : files.Select(Path.GetDirectoryName).Aggregate((a, b) => PathExtensions.GetCommonPath(a!, b!))!;
        if (string.IsNullOrWhiteSpace(common)) throw new InvalidDataException("EMLX dosyaları için ortak güvenli kaynak kökü bulunamadı.");
        return common;
    }
    private static string SafeFolder(string value) => string.Join(Path.DirectorySeparatorChar,
        value.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).Select(SafeName));
    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string safe = new(value.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray());
        safe = safe.Trim().TrimEnd('.'); return string.IsNullOrEmpty(safe) ? "unnamed" : safe;
    }
    private static void WriteCreateOnly(string path, byte[] bytes)
    { using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); fs.Write(bytes); fs.Flush(true); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string HashFile(string path) { using var fs = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant(); }
    private static int CountAttachments(MimeMessage message) => MimeAttachmentInventory.CountTopLevelAttachments(message);
}

internal static class PathExtensions
{
    public static string GetCommonPath(string first, string second)
    {
        var a = Path.GetFullPath(first).Split(Path.DirectorySeparatorChar);
        var b = Path.GetFullPath(second).Split(Path.DirectorySeparatorChar);
        int i = 0; while (i < Math.Min(a.Length, b.Length) && string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase)) i++;
        return string.Join(Path.DirectorySeparatorChar, a.Take(i));
    }
}
