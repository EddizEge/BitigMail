using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BitigMail.Engine.Distribution;

namespace BitigMail.Engine.Security;

public sealed record ArchiveZipPayloadFile(string Path, long Length, string Sha256);

/// <summary>
/// Validates an already-open ZIP directory and streams verified entries into caller-owned staging.
/// Not a publisher check, extractor, manifest parser, or limit on ZipArchive's initial directory allocation.
/// </summary>
public static class ArchiveZipPayloadVerifier
{
    public const int MaximumEntries = 100_000;
    public const long MaximumEntryBytes = 64L * 1024 * 1024;
    public const long MaximumTotalBytes = 1024L * 1024 * 1024 * 1024;

    public static IReadOnlyDictionary<string, ZipArchiveEntry> MatchDirectory(ZipArchive archive,
        IReadOnlyList<ArchiveZipPayloadFile> expected)
    {
        ArgumentNullException.ThrowIfNull(archive); ArgumentNullException.ThrowIfNull(expected);
        if (expected.Count is < 1 or > MaximumEntries || archive.Entries.Count != expected.Count)
            throw new InvalidDataException("Yedek paketinin dosya sayısı geçersiz.");
        var manifest = new Dictionary<string, ArchiveZipPayloadFile>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in expected)
        {
            ValidateFile(file);
            if (!manifest.TryAdd(file.Path.Normalize(NormalizationForm.FormC), file)) throw new InvalidDataException("Yedek dosya yolu yineleniyor.");
            total = checked(total + file.Length);
            if (total > MaximumTotalBytes) throw new InvalidDataException("Yedek toplam boyut sınırını aşıyor.");
        }
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            PackagePayloadValidator.ValidateRelativePath(entry.FullName);
            string normalized = entry.FullName.Normalize(NormalizationForm.FormC);
            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType is not (0 or 0x8000) || (entry.ExternalAttributes & (int)(FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 ||
                !manifest.TryGetValue(normalized, out var file) || !entries.TryAdd(normalized, entry) ||
                !string.Equals(entry.FullName, file.Path, StringComparison.Ordinal) || entry.Length != file.Length ||
                entry.CompressedLength < 0 || entry.Length > 0 && entry.CompressedLength == 0 ||
                entry.Length > 1_048_576 && entry.Length / Math.Max(1, entry.CompressedLength) > 10_000)
                throw new InvalidDataException("Yedek paketi eksik, kayıt dışı, bağlantılı veya aşırı sıkıştırılmış dosya içeriyor.");
        }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, ZipArchiveEntry>(entries);
    }

    public static async Task CopyVerifiedAsync(ZipArchiveEntry entry, ArchiveZipPayloadFile expected,
        Stream unpublishedDestination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry); ArgumentNullException.ThrowIfNull(unpublishedDestination);
        ValidateFile(expected);
        if (entry.FullName != expected.Path || entry.Length != expected.Length) throw new InvalidDataException("Yedek dosyası kayıtla uyuşmuyor.");
        cancellationToken.ThrowIfCancellationRequested();
        await using var source = entry.Open();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024]; long written = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            written = checked(written + read);
            if (written > expected.Length) throw new InvalidDataException("Yedek dosyası bildirilen boyutu aşıyor.");
            digest.AppendData(buffer, 0, read);
            await unpublishedDestination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (written != expected.Length || !Convert.ToHexString(digest.GetHashAndReset()).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Yedek dosyası eksik veya içeriği değişmiş.");
    }

    private static void ValidateFile(ArchiveZipPayloadFile file)
    {
        if (file is null) throw new InvalidDataException("Yedek dosyası eksik.");
        PackagePayloadValidator.ValidateRelativePath(file.Path);
        if (file.Length < 0 || file.Length > MaximumEntryBytes || file.Sha256 is not { Length: 64 } || !file.Sha256.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("Yedek dosya boyutu veya özeti geçersiz.");
    }
}
