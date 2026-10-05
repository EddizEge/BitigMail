using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BitigMail.Engine.Models;
using MimeKit;

namespace BitigMail.Engine.Storage;

public class MimeSourceInspector
{
    public const int MaxItemsPerFolderTrialLimit = 50;
    public const long MaxSingleMessageSizeBytes = 64 * 1024 * 1024; // 64 MiB

    public static string ComputeManifestFingerprint(MimeSourceManifest manifest)
    {
        using var sha = SHA256.Create();
        var sb = new StringBuilder();
        sb.Append(manifest.SourceKind).Append('|');
        sb.Append(manifest.Dialect).Append('|');
        sb.Append(manifest.IgnoredNonEmlFilesCount).Append('|');

        foreach (var entry in manifest.Entries.OrderBy(e => e.PhysicalOrdinal))
        {
            sb.Append(entry.PhysicalOrdinal).Append(':');
            sb.Append(entry.RelativePath).Append(':');
            sb.Append(entry.MappedFolder).Append(':');
            sb.Append(entry.SizeBytes).Append(':');
            sb.Append(entry.Sha256).Append(';');
        }

        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return System.Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeFolderId(string manifestFingerprint, string folderPath)
    {
        using var sha = SHA256.Create();
        byte[] bytes = Encoding.UTF8.GetBytes(manifestFingerprint.ToLowerInvariant() + ":" + folderPath.ToLowerInvariant());
        return "fld_" + System.Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant()[..16];
    }

    public MimeSourceManifest BuildEmlFilesManifest(IEnumerable<string> filePaths, string? defaultFolder = null)
        => BuildEmlFilesManifest(filePaths, defaultFolder, maxFileSizeBytes: 0);

    public MimeSourceManifest BuildEmlFilesManifest(IEnumerable<string> filePaths, string? defaultFolder, long maxFileSizeBytes)
    {
        var filesList = filePaths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (filesList.Count == 0)
        {
            throw new ArgumentException("En az bir EML dosyası seçilmelidir.", nameof(filePaths));
        }

        string folder = !string.IsNullOrWhiteSpace(defaultFolder) ? defaultFolder : "Corpus";
        var entries = new List<MimeSourceEntry>();
        int ordinal = 0;

        foreach (var file in filesList)
        {
            if (!File.Exists(file))
            {
                throw new FileNotFoundException($"Kaynak EML dosyası bulunamadı: {file}", file);
            }

            var fi = new FileInfo(file);
            if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Reparse point / symlink dosyaları kabul edilmez: {file}");
            }

            if (maxFileSizeBytes > 0 && fi.Length > maxFileSizeBytes)
            {
                throw new InvalidDataException($"[GÜVENLİK ENGELİ] Dosya boyutu izin verilen sınırı ({maxFileSizeBytes} bayt) aşıyor: {file}");
            }

            ordinal++;
            string hash = ComputeFileSha256(file);
            entries.Add(new MimeSourceEntry
            {
                CanonicalPath = file,
                RelativePath = fi.Name,
                MappedFolder = folder,
                SizeBytes = fi.Length,
                Sha256 = hash,
                PhysicalOrdinal = ordinal
            });
        }

        var manifest = new MimeSourceManifest
        {
            SourceKind = "eml-files",
            Dialect = "rfc822",
            RootPath = Path.GetDirectoryName(filesList[0]) ?? string.Empty,
            Entries = entries,
            TotalFiles = entries.Count,
            IgnoredNonEmlFilesCount = 0
        };

        manifest.AggregateFingerprint = ComputeManifestFingerprint(manifest);
        return manifest;
    }

    public MimeSourceManifest BuildEmlDirectoryManifest(string directoryPath)
        => BuildEmlDirectoryManifest(directoryPath, maxFileSizeBytes: 0);

    public MimeSourceManifest BuildEmlDirectoryManifest(string directoryPath, long maxFileSizeBytes)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Kaynak EML klasörü bulunamadı: {directoryPath}");
        }

        string rootCanonical = Path.GetFullPath(directoryPath);
        var rootDi = new DirectoryInfo(rootCanonical);
        if (rootDi.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Reparse point / symlink dizinleri kabul edilmez: {rootCanonical}");
        }

        string defaultRootFolder = rootDi.Name;
        int ignoredNonEmlCount = 0;
        var entries = new List<MimeSourceEntry>();

        void ScanDirectory(DirectoryInfo currentDi)
        {
            if (currentDi.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Reparse point / symlink alt dizini tespit edildi: {currentDi.FullName}");
            }

            string currentFull = Path.GetFullPath(currentDi.FullName);
            if (!currentFull.StartsWith(rootCanonical, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Klasör hiyerarşisi dışına kaçış tespit edildi: {currentFull}");
            }

            foreach (var file in currentDi.EnumerateFiles())
            {
                if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Reparse point dosyası tespit edildi: {file.FullName}");
                }

                if (file.Extension.Equals(".eml", StringComparison.OrdinalIgnoreCase))
                {
                    if (maxFileSizeBytes > 0 && file.Length > maxFileSizeBytes)
                    {
                        throw new InvalidDataException($"[GÜVENLİK ENGELİ] Dosya boyutu izin verilen sınırı ({maxFileSizeBytes} bayt) aşıyor: {file.FullName}");
                    }

                    string relPath = Path.GetRelativePath(rootCanonical, file.FullName).Replace('\\', '/');
                    string? relDir = Path.GetDirectoryName(relPath)?.Replace('\\', '/');
                    string mappedFolder = string.IsNullOrEmpty(relDir) || relDir == "."
                        ? defaultRootFolder
                        : relDir;

                    entries.Add(new MimeSourceEntry
                    {
                        CanonicalPath = file.FullName,
                        RelativePath = relPath,
                        MappedFolder = mappedFolder,
                        SizeBytes = file.Length,
                        Sha256 = ComputeFileSha256(file.FullName),
                        PhysicalOrdinal = 0 // Assigned after sort
                    });
                }
                else
                {
                    ignoredNonEmlCount++;
                }
            }

            foreach (var subDi in currentDi.EnumerateDirectories())
            {
                ScanDirectory(subDi);
            }
        }

        ScanDirectory(rootDi);

        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"Seçilen klasörde hiç .eml dosyası bulunamadı ({ignoredNonEmlCount} adet EML dışı dosya yok sayıldı).");
        }

        // Deterministic sort by relative path
        entries.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < entries.Count; i++)
        {
            entries[i].PhysicalOrdinal = i + 1;
        }

        var manifest = new MimeSourceManifest
        {
            SourceKind = "eml-tree",
            Dialect = "rfc822",
            RootPath = rootCanonical,
            Entries = entries,
            TotalFiles = entries.Count,
            IgnoredNonEmlFilesCount = ignoredNonEmlCount
        };

        manifest.AggregateFingerprint = ComputeManifestFingerprint(manifest);
        return manifest;
    }

    public MimeSourceManifest BuildMboxManifest(string mboxFilePath, string? defaultFolder = null)
        => BuildMboxManifest(mboxFilePath, defaultFolder, maxRecordSizeBytes: 0);

    public MimeSourceManifest BuildMboxManifest(string mboxFilePath, string? defaultFolder, long maxRecordSizeBytes)
    {
        if (string.IsNullOrWhiteSpace(mboxFilePath) || !File.Exists(mboxFilePath))
        {
            throw new FileNotFoundException($"Kaynak MBOX dosyası bulunamadı: {mboxFilePath}", mboxFilePath);
        }

        string fullPath = Path.GetFullPath(mboxFilePath);
        var fi = new FileInfo(fullPath);
        if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Reparse point / symlink MBOX dosyaları kabul edilmez: {fullPath}");
        }

        string folderName = !string.IsNullOrWhiteSpace(defaultFolder)
            ? defaultFolder
            : Path.GetFileNameWithoutExtension(fullPath);

        var entries = new List<MimeSourceEntry>();
        using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            using var sha = SHA256.Create();
            var records = maxRecordSizeBytes > 0
                ? MboxrdRecordReader.EnumerateRecords(fs, maxRecordSizeBytes, MboxrdRecordReader.DefaultMaxLineLengthBytes)
                : MboxrdRecordReader.EnumerateRecords(fs);
            foreach (var record in records)
            {
                byte[] hashBytes = sha.ComputeHash(record.RawMimeBytes);
                string recordSha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();

                entries.Add(new MimeSourceEntry
                {
                    CanonicalPath = fullPath,
                    RelativePath = $"{fi.Name}#record-{record.Ordinal}",
                    MappedFolder = folderName,
                    SizeBytes = record.RawMimeBytes.Length,
                    Sha256 = recordSha256,
                    PhysicalOrdinal = record.Ordinal
                });
            }
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"Kaynak MBOX dosyasında hiç e-posta kaydı bulunamadı: {mboxFilePath}");
        }

        var manifest = new MimeSourceManifest
        {
            SourceKind = "mbox",
            Dialect = "mboxrd",
            RootPath = fullPath,
            Entries = entries,
            TotalFiles = entries.Count,
            IgnoredNonEmlFilesCount = 0
        };

        manifest.AggregateFingerprint = ComputeManifestFingerprint(manifest);
        return manifest;
    }

    public void RevalidateManifest(MimeSourceManifest manifest)
        => RevalidateManifest(manifest, maxRecordSizeBytes: 0);

    public void RevalidateManifest(MimeSourceManifest manifest, long maxRecordSizeBytes)
    {
        if (manifest == null || manifest.Entries.Count == 0)
        {
            throw new InvalidOperationException("[BÜTÜNLÜK ENGELİ] Doğrulanacak geçerli kaynak manifestosu bulunamadı.");
        }

        if (manifest.SourceKind == "mbox")
        {
            string mboxPath = manifest.RootPath;
            if (!File.Exists(mboxPath))
            {
                throw new FileNotFoundException($"[BÜTÜNLÜK ENGELİ] Kaynak MBOX dosyası artık mevcut değil: {mboxPath}");
            }
            var fi = new FileInfo(mboxPath);
            if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException($"[GÜVENLİK ENGELİ] MBOX dosyası reparse point'e dönüştürülmüş: {mboxPath}");
            }

            // Quick revalidation: re-count records and check first/last hashes
            using var fs = new FileStream(mboxPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sha = SHA256.Create();
            int count = 0;
            var records = maxRecordSizeBytes > 0
                ? MboxrdRecordReader.EnumerateRecords(fs, maxRecordSizeBytes, MboxrdRecordReader.DefaultMaxLineLengthBytes)
                : MboxrdRecordReader.EnumerateRecords(fs);
            foreach (var record in records)
            {
                count++;
                if (count <= manifest.Entries.Count)
                {
                    var expectedEntry = manifest.Entries[count - 1];
                    string currentSha = Convert.ToHexString(sha.ComputeHash(record.RawMimeBytes)).ToLowerInvariant();
                    if (!string.Equals(currentSha, expectedEntry.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] MBOX {count}. kayıt içeriğinde değişiklik tespit edildi.");
                    }
                }
            }

            if (count != manifest.Entries.Count)
            {
                throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] MBOX kayıt sayısı değişti (beklenen: {manifest.Entries.Count}, güncel: {count}).");
            }
        }
        else
        {
            // For EML files or tree: re-check all files exist, sizes, and hashes
            if (manifest.SourceKind == "eml-tree")
            {
                if (!Directory.Exists(manifest.RootPath))
                {
                    throw new DirectoryNotFoundException($"[BÜTÜNLÜK ENGELİ] Kaynak klasör bulunamadı: {manifest.RootPath}");
                }
                var rootDi = new DirectoryInfo(manifest.RootPath);
                if (rootDi.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Kaynak klasör reparse point'e dönüştürülmüş.");
                }

                // Check file count did not change in directory tree
                var currentEmlFiles = rootDi.EnumerateFiles("*.eml", SearchOption.AllDirectories).ToList();
                if (currentEmlFiles.Count != manifest.Entries.Count)
                {
                    throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] Kaynak klasördeki EML dosya sayısı değişti (beklenen: {manifest.Entries.Count}, güncel: {currentEmlFiles.Count}). Lütfen analizi yenileyin.");
                }
            }

            foreach (var entry in manifest.Entries)
            {
                if (!File.Exists(entry.CanonicalPath))
                {
                    throw new FileNotFoundException($"[BÜTÜNLÜK ENGELİ] Kaynak dosya silinmiş veya erişilemez: {entry.CanonicalPath}");
                }

                var fi = new FileInfo(entry.CanonicalPath);
                if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Kaynak dosya reparse point'e dönüştürülmüş: {entry.CanonicalPath}");
                }

                if (fi.Length != entry.SizeBytes)
                {
                    throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] Dosya boyutu değişmiş: {entry.CanonicalPath}");
                }

                string currentHash = ComputeFileSha256(entry.CanonicalPath);
                if (!string.Equals(currentHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] Dosya içeriği değişmiş (hash uyuşmazlığı): {entry.CanonicalPath}");
                }
            }
        }
    }

    public Task<MimeAnalysisResult> AnalyzeAsync(MimeSourceManifest manifest, string handle = "")
        => AnalyzeAsync(manifest, handle, maxRecordSizeBytes: 0);

    public Task<MimeAnalysisResult> AnalyzeAsync(MimeSourceManifest manifest, string handle, long maxRecordSizeBytes)
    {
        return Task.Run(() => Analyze(manifest, handle, maxRecordSizeBytes));
    }

    public MimeAnalysisResult Analyze(MimeSourceManifest manifest, string handle = "")
        => Analyze(manifest, handle, maxRecordSizeBytes: 0);

    public MimeAnalysisResult Analyze(MimeSourceManifest manifest, string handle, long maxRecordSizeBytes)
    {
        RevalidateManifest(manifest, maxRecordSizeBytes);

        var preflight = new PreflightCheckResult
        {
            CanConvert = true,
            HasTrialBlocker = false
        };

        var folderGrouped = manifest.Entries
            .GroupBy(e => e.MappedFolder, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var folders = new List<FolderSummary>();
        int totalAttachments = 0;
        var sampleMessages = new List<SampleMessageSummary>();

        // Check 50-item evaluation limit per folder
        foreach (var (folderName, entries) in folderGrouped)
        {
            int itemCount = entries.Count;
            if (itemCount > MaxItemsPerFolderTrialLimit)
            {
                preflight.CanConvert = false;
                preflight.HasTrialBlocker = true;
                preflight.TrialBlockerReason = $"[ÖN KONTROL ENGELİ] Değerlendirme lisansı klasör başına en fazla {MaxItemsPerFolderTrialLimit} öğe desteklemektedir. '{folderName}' klasöründe {itemCount} öğe bulunmaktadır. Dönüştürme engellendi.";
                preflight.Blockers.Add(preflight.TrialBlockerReason);
            }

            string folderId = ComputeFolderId(manifest.AggregateFingerprint, folderName);
            folders.Add(new FolderSummary
            {
                FolderId = folderId,
                FolderPath = folderName,
                DisplayName = folderName,
                ItemCount = itemCount,
                SubFolderCount = 0,
                Category = itemCount > 0 ? "Active" : "Empty",
                IsIpmFolder = true
            });
        }

        // Sample up to 10 messages across the source
        int sampleLimit = Math.Min(10, manifest.Entries.Count);
        if (manifest.SourceKind == "mbox")
        {
            using var fs = new FileStream(manifest.RootPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            int idx = 0;
            var records = maxRecordSizeBytes > 0
                ? MboxrdRecordReader.EnumerateRecords(fs, maxRecordSizeBytes, MboxrdRecordReader.DefaultMaxLineLengthBytes)
                : MboxrdRecordReader.EnumerateRecords(fs);
            foreach (var record in records)
            {
                idx++;
                using var ms = new MemoryStream(record.RawMimeBytes);
                var mime = MimeMessage.Load(ms);
                int attCount = MimeAttachmentInventory.CountTopLevelAttachments(mime);
                totalAttachments += attCount;

                if (sampleMessages.Count < sampleLimit)
                {
                    sampleMessages.Add(new SampleMessageSummary
                    {
                        EntryId = $"rec_{record.Ordinal}",
                        FolderPath = manifest.Entries[idx - 1].MappedFolder,
                        Subject = mime.Subject ?? "(Konusuz)",
                        Sender = mime.From.ToString(),
                        DisplayTo = mime.To.ToString(),
                        DateUtc = ExtractSafeDateUtc(mime),
                        HasAttachments = attCount > 0,
                        AttachmentCount = attCount
                    });
                }
            }
        }
        else
        {
            for (int i = 0; i < manifest.Entries.Count; i++)
            {
                var entry = manifest.Entries[i];
                if (maxRecordSizeBytes > 0 && entry.SizeBytes > maxRecordSizeBytes)
                {
                    throw new InvalidDataException($"[GÜVENLİK ENGELİ] Dosya boyutu izin verilen sınırı ({maxRecordSizeBytes} bayt) aşıyor: {entry.CanonicalPath}");
                }
                using var fs = new FileStream(entry.CanonicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var mime = MimeMessage.Load(fs);
                int attCount = MimeAttachmentInventory.CountTopLevelAttachments(mime);
                totalAttachments += attCount;

                if (sampleMessages.Count < sampleLimit)
                {
                    sampleMessages.Add(new SampleMessageSummary
                    {
                        EntryId = $"msg_{entry.PhysicalOrdinal}",
                        FolderPath = entry.MappedFolder,
                        Subject = mime.Subject ?? "(Konusuz)",
                        Sender = mime.From.ToString(),
                        DisplayTo = mime.To.ToString(),
                        DateUtc = ExtractSafeDateUtc(mime),
                        HasAttachments = attCount > 0,
                        AttachmentCount = attCount
                    });
                }
            }
        }

        long totalSize = manifest.Entries.Sum(e => e.SizeBytes);
        string displaySource = manifest.SourceKind == "mbox"
            ? Path.GetFileName(manifest.RootPath)
            : $"{manifest.TotalFiles} EML Dosyası";

        return new MimeAnalysisResult
        {
            SourceHandle = handle,
            SourceFileName = displaySource,
            SourceKind = manifest.SourceKind,
            Dialect = manifest.Dialect,
            SourceSizeBytes = totalSize,
            SourceSha256 = manifest.AggregateFingerprint,
            SourceFingerprint = manifest.AggregateFingerprint,
            TotalFolders = folders.Count,
            ActiveFoldersCount = folders.Count(f => f.ItemCount > 0),
            EmptyFoldersCount = folders.Count(f => f.ItemCount == 0),
            SystemFoldersCount = 0,
            TotalItems = manifest.Entries.Count,
            PhysicalTotalItems = manifest.Entries.Count,
            TotalAttachments = totalAttachments,
            Folders = folders,
            SampleMessages = sampleMessages,
            Preflight = preflight,
            IgnoredNonEmlFilesCount = manifest.IgnoredNonEmlFilesCount
        };
    }

    public static bool HasExplicitTimeZone(string? rawDate)
    {
        if (string.IsNullOrWhiteSpace(rawDate)) return false;
        string clean = rawDate.Trim();
        while (clean.EndsWith(')'))
        {
            int idx = clean.LastIndexOf('(');
            if (idx >= 0) clean = clean.Substring(0, idx).Trim();
            else break;
        }
        return System.Text.RegularExpressions.Regex.IsMatch(
            clean,
            @"(?:[+-]\d{2}:?\d{2}|\b(?:UT|UTC|GMT|EST|EDT|CST|CDT|MST|MDT|PST|PDT|[A-Z])\b)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string ExtractSafeDateUtc(MimeMessage mime) => MimeFidelityPolicy.OriginalDate(mime)?.ToString("o") ?? string.Empty;

    private static string ComputeFileSha256(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        return System.Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }
}
