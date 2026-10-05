using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace BitigMail.Engine.Distribution;

public sealed record PackagePayloadFile(string RelativePath, long Length, string Sha256);
public sealed record PackagePayloadManifest(int SchemaVersion, string Product, string Version,
    int MinimumDataSchema, int MaximumDataSchema, IReadOnlyList<PackagePayloadFile> Files);
public sealed record ValidatedPackagePayload(string Version, int FileCount, long TotalBytes,
    string PublisherQualification);

/// <summary>Integrity checking only. Does not authenticate the publisher or install/delete files.</summary>
public sealed class PackagePayloadValidator
{
    public async Task<ValidatedPackagePayload> ValidateAsync(string payloadDirectory,
        PackagePayloadManifest manifest, int currentDataSchema, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entries = ValidateManifest(manifest, currentDataSchema);
        string root = Path.GetFullPath(payloadDirectory);
        CheckExistingAncestors(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Paket dizini bulunamadı.");
        var expected = entries.ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase);
        long total = entries.Sum(e => e.Length);
        // Inspect before descending. Never traverse a directory junction or symbolic link.
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int directories = 0;
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckExistingAncestors(directory.Path);
            foreach (string path in Directory.EnumerateFileSystemEntries(directory.Path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Paket bağlantı içermemeli.");
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                ValidateRelativePath(relative);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (directory.Depth >= 31 || ++directories > 100_000)
                        throw new InvalidDataException("Paket dizin sınırı aşıldı.");
                    pending.Push((path, directory.Depth + 1));
                    continue;
                }
                if (!seen.Add(relative) || !expected.TryGetValue(relative, out var entry))
                    throw new InvalidDataException("Paket kayıt dışı veya yinelenen dosya içeriyor.");
                CheckExistingAncestors(path);
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (stream.Length != entry.Length) throw new InvalidDataException("Paket dosya boyutu değişmiş.");
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Paket dosya içeriği değişmiş.");
            }
        }
        if (seen.Count != expected.Count) throw new InvalidDataException("Paket dosyaları eksik.");
        return new(manifest.Version, seen.Count, total, "INTEGRITY_ONLY_PUBLISHER_NOT_VERIFIED");
    }

    /// <summary>Semantic admission without filesystem mutation or publisher authentication.</summary>
    public static IReadOnlyList<PackagePayloadFile> ValidateManifest(PackagePayloadManifest manifest, int currentDataSchema)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != 1 || manifest.Product != "BitigMail" ||
            manifest.Version is null || !Regex.IsMatch(manifest.Version, @"\A[0-9]{1,4}\.[0-9]{1,4}\.[0-9]{1,4}(?:\.[0-9]{1,4})?\z") ||
            manifest.MinimumDataSchema < 1 || manifest.MaximumDataSchema < manifest.MinimumDataSchema ||
            currentDataSchema < manifest.MinimumDataSchema || currentDataSchema > manifest.MaximumDataSchema)
            throw new InvalidDataException("Paket kimliği, sürümü veya veri şeması uyumsuz.");
        if (manifest.Files is null || manifest.Files.Count is < 1 or > 100_000)
            throw new InvalidDataException("Paket dosya listesi sınır dışında.");
        // Copy before awaiting: a caller must not mutate the accepted manifest during hashing.
        var entries = manifest.Files.ToArray();
        var expected = new Dictionary<string, PackagePayloadFile>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in entries)
        {
            if (entry is null) throw new InvalidDataException("Paket dosyası eksik.");
            ValidateRelativePath(entry.RelativePath);
            if (entry.Length < 0 || entry.Length > 4L * 1024 * 1024 * 1024 ||
                !Regex.IsMatch(entry.Sha256 ?? "", @"\A[0-9A-Fa-f]{64}\z") ||
                !expected.TryAdd(entry.RelativePath, entry))
                throw new InvalidDataException("Paket boyutu, özeti veya dosya adı geçersiz.");
            total = checked(total + entry.Length);
            if (total > 16L * 1024 * 1024 * 1024) throw new InvalidDataException("Paket toplam boyutu sınır dışında.");
        }
        return Array.AsReadOnly(entries);
    }

    public static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 240 || path.Contains('\\') || path.Contains(':') ||
            path.StartsWith('/') || path.EndsWith('/'))
            throw new InvalidDataException("Paket yolu geçersiz.");
        foreach (string segment in path.Split('/'))
        {
            if (segment.Length is 0 or > 100 || segment is "." or ".." ||
                segment.EndsWith('.') || segment.EndsWith(' ') || segment.Any(c => c < 32 || "<>\"|?*".Contains(c)))
                throw new InvalidDataException("Paket yol bileşeni geçersiz.");
            string stem = segment.Split('.')[0].TrimEnd(' ');
            if (Regex.IsMatch(stem, @"\A(?:CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|CLOCK\$|COM[1-9¹²³]|LPT[1-9¹²³])\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new InvalidDataException("Paket yolu Windows aygıt adı içeriyor.");
        }
    }

    internal static void CheckExistingAncestors(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Paket yolu bağlantı içeriyor.");
            current = Path.GetDirectoryName(current);
        }
    }
}
