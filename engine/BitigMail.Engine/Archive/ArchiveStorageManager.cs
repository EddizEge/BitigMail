using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BitigMail.Engine.Archive;

public sealed class ArchiveStorageManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _archivesBaseDir;
    private readonly string _stagingBaseDir;
    private readonly object _storageLock = new();

    public ArchiveStorageManager(string runtimeDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDir);
        string baseDir = Path.Combine(Path.GetFullPath(runtimeDir), "archives");
        _archivesBaseDir = baseDir;
        _stagingBaseDir = Path.Combine(baseDir, ".staging");

        Directory.CreateDirectory(_archivesBaseDir);
        Directory.CreateDirectory(_stagingBaseDir);
    }

    public string ArchivesBaseDirectory => _archivesBaseDir;
    public string StagingBaseDirectory => _stagingBaseDir;

    public string CreateStagingDirectory(string stagingId)
    {
        return EnsureStagingDirectory(stagingId);
    }

    public string EnsureStagingDirectory(string stagingId)
    {
        ValidateSafeId(stagingId);
        string path = Path.Combine(_stagingBaseDir, stagingId);
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, "messages"));
        return path;
    }

    public void CleanupStagingDirectory(string stagingId)
    {
        try
        {
            string path = Path.Combine(_stagingBaseDir, stagingId);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch { }
    }

    public bool TryGetVerifiedStagedItem(
        string stagingDir,
        int ordinal,
        string itemId,
        string mappedFolder,
        string? expectedSha256,
        long expectedLength,
        DateTimeOffset? sourceInternalDateUtc,
        string? envelopeFrom,
        out ArchiveManifestItem? manifestItem)
    {
        manifestItem = null;
        ValidateDirectorySafety(stagingDir);

        string relativeEmlPath = Path.Combine("messages", $"{ordinal:D6}.eml");
        string fullEmlPath = Path.Combine(stagingDir, relativeEmlPath);

        if (!File.Exists(fullEmlPath))
        {
            return false;
        }

        var fi = new FileInfo(fullEmlPath);
        if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return false;
        }

        if (expectedLength > 0 && fi.Length != expectedLength)
        {
            return false;
        }

        using (var sha = SHA256.Create())
        using (var fs = new FileStream(fullEmlPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            byte[] hash = sha.ComputeHash(fs);
            string computedSha = Convert.ToHexString(hash).ToLowerInvariant();
            if (!string.IsNullOrEmpty(expectedSha256) &&
                !string.Equals(computedSha, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            fs.Position = 0;
            DateTimeOffset? mimeDate = null;
            string? messageIdHeader = null;
            bool isBodyTruncated = false;

            try
            {
                var parsed = ArchiveMimeParser.Parse(fs, fi.Length);
                mimeDate = parsed.DateUtc;
                messageIdHeader = parsed.MessageIdHeader;
                isBodyTruncated = parsed.IsBodyTruncated;
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
            }

            manifestItem = new ArchiveManifestItem
            {
                Ordinal = ordinal,
                ItemId = itemId,
                RelativeEmlPath = relativeEmlPath.Replace('\\', '/'),
                SourceSha256 = expectedSha256 ?? computedSha,
                StoredSha256 = computedSha,
                ByteLength = fi.Length,
                OriginalFolder = mappedFolder,
                OriginalMimeDateUtc = mimeDate,
                SourceInternalDateUtc = sourceInternalDateUtc,
                MessageIdHeader = messageIdHeader,
                EnvelopeFrom = envelopeFrom,
                IsBodyTruncated = isBodyTruncated
            };
            return true;
        }
    }

    public async Task<ArchiveManifestItem> StoreMimeItemAsync(
        string stagingDir,
        int ordinal,
        string itemId,
        string originalFolder,
        Stream rawStream,
        string? expectedSha256,
        string? envelopeFrom,
        CancellationToken ct,
        DateTimeOffset? sourceInternalDateUtc = null)
    {
        ValidateDirectorySafety(stagingDir);
        string messagesDir = Path.Combine(stagingDir, "messages");
        Directory.CreateDirectory(messagesDir);

        string relativeEmlPath = Path.Combine("messages", $"{ordinal:D6}.eml");
        string fullEmlPath = Path.Combine(stagingDir, relativeEmlPath);

        string tmpEml = fullEmlPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string computedSha256;
        long byteLength;

        try
        {
            using (var sha = SHA256.Create())
            using (var fileStream = new FileStream(tmpEml, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[81920];
                int read;
                while ((read = await rawStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    sha.TransformBlock(buffer, 0, read, null, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                await fileStream.FlushAsync(ct);
                byteLength = fileStream.Length;
                computedSha256 = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
            }

            if (!string.IsNullOrEmpty(expectedSha256) &&
                !string.Equals(computedSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"[GÜVENLİK ENGELİ] SHA-256 hash uyuşmazlığı. Beklenen: {expectedSha256}, Hesaplanan: {computedSha256}");
            }

            if (File.Exists(fullEmlPath))
            {
                File.Delete(fullEmlPath);
            }
            File.Move(tmpEml, fullEmlPath, overwrite: false);
        }
        finally
        {
            try { if (File.Exists(tmpEml)) File.Delete(tmpEml); } catch { }
        }

        // Parse minimal headers from the stored EML for manifest
        DateTimeOffset? mimeDate = null;
        string? messageIdHeader = null;
        bool isBodyTruncated = false;

        try
        {
            using var readFs = new FileStream(fullEmlPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var parsed = ArchiveMimeParser.Parse(readFs, byteLength);
            mimeDate = parsed.DateUtc;
            messageIdHeader = parsed.MessageIdHeader;
            isBodyTruncated = parsed.IsBodyTruncated;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // If body parsing encounters non-fatal issue, keep raw EML and record null headers
        }

        return new ArchiveManifestItem
        {
            Ordinal = ordinal,
            ItemId = itemId,
            RelativeEmlPath = relativeEmlPath.Replace('\\', '/'),
            SourceSha256 = expectedSha256 ?? computedSha256,
            StoredSha256 = computedSha256,
            ByteLength = byteLength,
            OriginalFolder = originalFolder,
            OriginalMimeDateUtc = mimeDate,
            SourceInternalDateUtc = sourceInternalDateUtc,
            MessageIdHeader = messageIdHeader,
            EnvelopeFrom = envelopeFrom,
            IsBodyTruncated = isBodyTruncated
        };
    }

    public async Task WriteManifestAsync(string stagingDir, ArchiveManifest manifest, CancellationToken ct)
    {
        ValidateDirectorySafety(stagingDir);
        string manifestPath = Path.Combine(stagingDir, "manifest.json");
        string tmpPath = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            using (var fs = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(fs, manifest, JsonOptions, ct);
                await fs.FlushAsync(ct);
            }
            File.Move(tmpPath, manifestPath, overwrite: false);
        }
        finally
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
        }
    }

    public void PublishStagingToManagedArchive(string stagingDir, string archiveId)
    {
        ValidateSafeId(archiveId);
        ValidateDirectorySafety(stagingDir);

        string targetDir = Path.Combine(_archivesBaseDir, archiveId);

        lock (_storageLock)
        {
            if (Directory.Exists(targetDir))
            {
                string manifestPath = Path.Combine(targetDir, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    // Already published
                    CleanupStagingDirectory(Path.GetFileName(stagingDir));
                    return;
                }
                Directory.Delete(targetDir, recursive: true);
            }

            Directory.Move(stagingDir, targetDir);
        }
    }

    // Restore publication is create-only. The caller verifies payload hashes and persists
    // its recovery receipt before calling; this method never removes either directory.
    public void PublishFreshStagingToManagedArchive(string stagingDir, string archiveId)
    {
        ValidateSafeId(archiveId);
        string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingDir));
        if (!string.Equals(Path.GetDirectoryName(source), _stagingBaseDir, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Staging must be an immediate managed staging child.");
        ValidateSafeId(Path.GetFileName(source));
        string target = Path.GetFullPath(Path.Combine(_archivesBaseDir, archiveId));
        lock (_storageLock)
        {
            RequireNoReparseAncestors(source);
            RequireNoReparseAncestors(target);
            if (Path.Exists(target)) throw new IOException("Archive destination already exists.");
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException("Staging is missing.");
            var pending = new Stack<string>();
            pending.Push(source);
            int entries = 0;
            while (pending.Count > 0)
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
                {
                    if (++entries > 100_000) throw new InvalidDataException("Staging entry limit exceeded.");
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Staging links are forbidden.");
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                }
            }
            using (var manifestStream = new FileStream(Path.Combine(source, "manifest.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (manifestStream.Length > 64L * 1024 * 1024) throw new InvalidDataException("Manifest limit exceeded.");
                var manifest = JsonSerializer.Deserialize<ArchiveManifest>(manifestStream, JsonOptions);
                if (manifest is null || !string.Equals(manifest.ArchiveId, archiveId, StringComparison.Ordinal))
                    throw new InvalidDataException("Archive identity does not match destination.");
            }
            // Directory.Move itself refuses collisions, including a target created after the check.
            Directory.Move(source, target);
        }
    }

    private static void RequireNoReparseAncestors(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Archive links are forbidden.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    public ArchiveManifest? GetArchiveManifest(string archiveId)
    {
        if (string.IsNullOrWhiteSpace(archiveId)) return null;
        ValidateSafeId(archiveId);

        string targetDir = Path.Combine(_archivesBaseDir, archiveId);
        if (!Directory.Exists(targetDir)) return null;

        string manifestPath = Path.Combine(targetDir, "manifest.json");
        if (!File.Exists(manifestPath)) return null;

        using var fs = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return JsonSerializer.Deserialize<ArchiveManifest>(fs, JsonOptions);
    }

    public IReadOnlyList<ArchiveManifest> GetAllArchiveManifests()
    {
        var result = new List<ArchiveManifest>();
        if (!Directory.Exists(_archivesBaseDir)) return result;

        foreach (var dir in Directory.GetDirectories(_archivesBaseDir))
        {
            string dirName = Path.GetFileName(dir);
            if (dirName.StartsWith('.')) continue; // skip .staging

            string manifestPath = Path.Combine(dir, "manifest.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    using var fs = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var manifest = JsonSerializer.Deserialize<ArchiveManifest>(fs, JsonOptions);
                    if (manifest != null)
                    {
                        result.Add(manifest);
                    }
                }
                catch { }
            }
        }

        return result;
    }

    public Stream OpenRawEmlStream(string archiveId, string relativeEmlPath)
    {
        ValidateSafeId(archiveId);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeEmlPath);

        string archiveDir = Path.GetFullPath(Path.Combine(_archivesBaseDir, archiveId));
        ValidateDirectorySafety(archiveDir);

        string fullEmlPath = Path.GetFullPath(Path.Combine(archiveDir, relativeEmlPath));

        // Enforce boundary check: full path must start with archiveDir
        if (!fullEmlPath.StartsWith(archiveDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("[GÜVENLİK ENGELİ] Dizin aşma (traversal) girişimi engellendi.");
        }

        if (!File.Exists(fullEmlPath))
        {
            throw new FileNotFoundException($"Arşiv ileti dosyası bulunamadı: {relativeEmlPath}");
        }

        var fi = new FileInfo(fullEmlPath);
        if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("[GÜVENLİK ENGELİ] Reparse point / symlink dosya erişimi engellendi.");
        }

        return new FileStream(fullEmlPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static void ValidateSafeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 ||
            !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new ArgumentException("Geçersiz arşiv kimliği.");
        }
    }

    private static void ValidateDirectorySafety(string dirPath)
    {
        if (!Directory.Exists(dirPath)) return;
        var di = new DirectoryInfo(dirPath);
        if (di.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("[GÜVENLİK ENGELİ] Reparse point / symlink dizin erişimi engellendi.");
        }
    }
}
