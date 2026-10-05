using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Archive;
using BitigMail.Engine.Bridge;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;

namespace BitigMail.LocalHost.Archive;

public sealed class ArchiveIngestWorker
{
    private readonly ArchiveStorageManager _storageManager;
    private readonly ArchiveSearchIndex _searchIndex;
    private readonly ArchivePlanStore _planStore;
    private readonly FileHandleRegistry _handleRegistry;
    private readonly JobManager _jobManager;
    private readonly Action<LocalJobRecord> _saveJobRecord;
    private readonly MimeSourceInspector _inspector = new();
    private readonly IDiskCapacityProbe _capacityProbe;

    public ArchiveIngestWorker(
        ArchiveStorageManager storageManager,
        ArchiveSearchIndex searchIndex,
        ArchivePlanStore planStore,
        FileHandleRegistry handleRegistry,
        JobManager jobManager,
        Action<LocalJobRecord> saveJobRecord,
        IDiskCapacityProbe? capacityProbe = null)
    {
        _storageManager = storageManager ?? throw new ArgumentNullException(nameof(storageManager));
        _searchIndex = searchIndex ?? throw new ArgumentNullException(nameof(searchIndex));
        _planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
        _handleRegistry = handleRegistry ?? throw new ArgumentNullException(nameof(handleRegistry));
        _jobManager = jobManager ?? throw new ArgumentNullException(nameof(jobManager));
        _saveJobRecord = saveJobRecord ?? throw new ArgumentNullException(nameof(saveJobRecord));
        _capacityProbe = capacityProbe ?? new WindowsDiskCapacityProbe();
    }

    public async Task ExecuteIngestAsync(LocalJobRecord record, ArchiveIngestPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(plan);

        try
        {
            string archiveBase = Path.GetFullPath(_storageManager.ArchivesBaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            string indexBase = Path.GetFullPath(Path.GetDirectoryName(_searchIndex.DatabasePath)!).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(archiveBase, indexBase, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("[DİSK KAPASİTESİ ENGELİ] Arşiv ve indeks farklı hedef dizinlerde; güvenli birleşik kapasite hesabı desteklenmiyor.");
            long requiredBytes = plan.EstimatedRequiredBytes ?? DiskCapacityPlanning.EstimateArchive(plan.TotalSizeBytes, plan.TotalItems);
            string? capacityBlocker = DiskCapacityPlanning.CapacityBlocker(requiredBytes, _capacityProbe.Probe(archiveBase));
            if (capacityBlocker != null)
                throw new InvalidOperationException(capacityBlocker + " Arşiv çıktısı oluşturulmadı; mevcut veri silinmedi.");

            record.Status = "converting";
            record.Stage = "Arşivleniyor";
            record.StartedAt ??= DateTimeOffset.UtcNow;
            record.ArchiveId = plan.ArchiveId;
            record.ArchiveName = plan.ArchiveName;
            _saveJobRecord(record);

            // 1. Check if archive is already completed and published in managed storage
            var existingPublishedManifest = _storageManager.GetArchiveManifest(plan.ArchiveId);
            ArchiveManifest finalManifest;

            if (existingPublishedManifest != null)
            {
                // Raw MIME items already completely stored and published, proceed straight to indexing
                finalManifest = existingPublishedManifest;
                record.ItemsWritten = finalManifest.TotalItems;
                record.ItemsRead = finalManifest.TotalItems;
            }
            else
            {
                // 2. Perform raw ingestion into staging directory
                finalManifest = await IngestToStagingAndPublishAsync(record, plan, ct);
            }

            // 3. Indexing
            record.Status = "verifying";
            record.Stage = "İndeksleniyor";
            record.PercentComplete = 90;
            _saveJobRecord(record);

            await _searchIndex.IndexArchiveAsync(
                finalManifest,
                _storageManager,
                (indexed, total) =>
                {
                    if (total > 0)
                    {
                        record.PercentComplete = 90 + Math.Min(9, (int)((double)indexed / total * 9));
                        _saveJobRecord(record);
                    }
                },
                ct);

            // 4. Mark completed
            record.Status = "completed";
            record.Stage = "Tamamlandı";
            record.PercentComplete = 100;
            record.CompletedAt = DateTimeOffset.UtcNow;
            record.ItemsWritten = finalManifest.TotalItems;
            record.ItemsRead = finalManifest.TotalItems;
            record.ArchiveId = finalManifest.ArchiveId;
            record.ArchiveName = finalManifest.ArchiveName;
            _saveJobRecord(record);
        }
        catch (Exception ex)
        {
            record.Status = "failed";
            record.Stage = "Hata";
            record.ErrorMessage = ex.Message;
            record.CompletedAt = DateTimeOffset.UtcNow;
            _saveJobRecord(record);
            throw;
        }
    }

    private async Task<ArchiveManifest> IngestToStagingAndPublishAsync(
        LocalJobRecord record,
        ArchiveIngestPlan plan,
        CancellationToken ct)
    {
        // 1. Validate entire physical source against frozen plan BEFORE any staging mutation
        await ValidateSourceAgainstPlanAsync(plan, ct);

        // 2. Ensure staging directory exists WITHOUT deleting partial staging
        string stagingDir = _storageManager.EnsureStagingDirectory(plan.ArchiveId);
        var manifestItems = new List<ArchiveManifestItem>();

        bool isMbox = string.Equals(plan.SourceKind, "mbox", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(plan.Dialect, "mboxrd", StringComparison.OrdinalIgnoreCase);

        bool isBridgeExport = string.Equals(plan.SourceKind, "bridge-export", StringComparison.OrdinalIgnoreCase) ||
                              !string.IsNullOrWhiteSpace(plan.SourceJobId);

        if (isBridgeExport)
        {
            string exportDir = plan.SourceRootPath ?? string.Empty;
            if (string.IsNullOrEmpty(exportDir) || !Directory.Exists(exportDir))
            {
                if (!string.IsNullOrEmpty(plan.SourceJobId))
                {
                    var job = _jobManager.GetJob(plan.SourceJobId);
                    if (job != null && !string.IsNullOrEmpty(job.OutputPath) && Directory.Exists(job.OutputPath))
                    {
                        exportDir = job.OutputPath;
                    }
                }
            }

            if (string.IsNullOrEmpty(exportDir) || !Directory.Exists(exportDir))
            {
                throw new DirectoryNotFoundException($"[BÜTÜNLÜK ENGELİ] Dışa aktarım çıktı dizini bulunamadı: {exportDir}");
            }

            string manifestPath = Path.Combine(exportDir, "manifest.json");
            BridgeExportManifest exportManifest;
            using (var fs = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                exportManifest = JsonSerializer.Deserialize<BridgeExportManifest>(fs, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                    ?? throw new InvalidOperationException("[BÜTÜNLÜK ENGELİ] Export manifesti boş.");
            }

            if (isMbox)
            {
                var containerGroups = exportManifest.Items
                    .GroupBy(i => i.RelativeOutputPath, StringComparer.Ordinal)
                    .ToList();

                int processedCount = 0;
                foreach (var group in containerGroups)
                {
                    string containerRelPath = group.Key;
                    string containerFullPath = Path.Combine(exportDir, containerRelPath);

                    List<MboxrdRecord> records;
                    using (var fs = new FileStream(containerFullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        records = MboxrdRecordReader.EnumerateRecords(
                            fs,
                            ArchiveMimeParser.MaxRawMessageSizeBytes,
                            MboxrdRecordReader.DefaultMaxLineLengthBytes).ToList();
                    }

                    var expectedItems = group.ToList();
                    for (int i = 0; i < records.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var mboxRecord = records[i];
                        var expectedItem = expectedItems[i];
                        var plannedItem = plan.Items[processedCount];

                        if (_storageManager.TryGetVerifiedStagedItem(
                            stagingDir,
                            plannedItem.Ordinal,
                            plannedItem.ItemId,
                            plannedItem.MappedFolder,
                            expectedItem.StoredSha256,
                            plannedItem.SizeBytes,
                            plannedItem.SourceInternalDateUtc,
                            mboxRecord.EnvelopeFrom,
                            out var existingItem) && existingItem != null)
                        {
                            manifestItems.Add(existingItem);
                        }
                        else
                        {
                            using var rawStream = new MemoryStream(mboxRecord.RawMimeBytes, writable: false);
                            var manifestItem = await _storageManager.StoreMimeItemAsync(
                                stagingDir,
                                plannedItem.Ordinal,
                                plannedItem.ItemId,
                                plannedItem.MappedFolder,
                                rawStream,
                                expectedSha256: expectedItem.StoredSha256,
                                envelopeFrom: mboxRecord.EnvelopeFrom,
                                ct,
                                sourceInternalDateUtc: plannedItem.SourceInternalDateUtc);

                            manifestItems.Add(manifestItem);
                        }

                        processedCount++;
                        record.ItemsRead = processedCount;
                        record.ItemsWritten = manifestItems.Count;
                        if (plan.TotalItems > 0)
                        {
                            record.PercentComplete = Math.Min(89, (int)((double)processedCount / plan.TotalItems * 85));
                        }
                        _saveJobRecord(record);
                    }
                }
            }
            else
            {
                int count = 0;
                foreach (var item in plan.Items)
                {
                    ct.ThrowIfCancellationRequested();
                    count++;

                    string itemFullPath = item.SourcePath;
                    if (!File.Exists(itemFullPath))
                    {
                        itemFullPath = Path.Combine(exportDir, item.RelativePath ?? Path.GetFileName(item.SourcePath));
                    }

                    if (_storageManager.TryGetVerifiedStagedItem(
                        stagingDir,
                        item.Ordinal,
                        item.ItemId,
                        item.MappedFolder,
                        item.SourceSha256,
                        item.SizeBytes,
                        item.SourceInternalDateUtc,
                        null,
                        out var existingItem) && existingItem != null)
                    {
                        manifestItems.Add(existingItem);
                    }
                    else
                    {
                        using var fileStream = new FileStream(itemFullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        var manifestItem = await _storageManager.StoreMimeItemAsync(
                            stagingDir,
                            item.Ordinal,
                            item.ItemId,
                            item.MappedFolder,
                            fileStream,
                            expectedSha256: item.SourceSha256,
                            envelopeFrom: null,
                            ct,
                            sourceInternalDateUtc: item.SourceInternalDateUtc);

                        manifestItems.Add(manifestItem);
                    }

                    record.ItemsRead = count;
                    record.ItemsWritten = manifestItems.Count;
                    if (plan.TotalItems > 0)
                    {
                        record.PercentComplete = Math.Min(89, (int)((double)count / plan.TotalItems * 85));
                    }
                    _saveJobRecord(record);
                }
            }
        }
        else if (isMbox)
        {
            string mboxPath = !string.IsNullOrEmpty(plan.SourceRootPath) && File.Exists(plan.SourceRootPath)
                ? plan.SourceRootPath
                : (plan.Items.FirstOrDefault()?.SourcePath ?? string.Empty);

            using var fs = new FileStream(mboxPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            int count = 0;
            foreach (var mboxRecord in MboxrdRecordReader.EnumerateRecords(
                fs,
                ArchiveMimeParser.MaxRawMessageSizeBytes,
                MboxrdRecordReader.DefaultMaxLineLengthBytes))
            {
                ct.ThrowIfCancellationRequested();
                count++;

                var plannedItem = plan.Items.FirstOrDefault(i => i.Ordinal == count)
                    ?? throw new InvalidOperationException($"Planda {count}. kayıt için öğe bulunamadı.");

                if (_storageManager.TryGetVerifiedStagedItem(
                    stagingDir,
                    count,
                    plannedItem.ItemId,
                    plannedItem.MappedFolder,
                    plannedItem.SourceSha256,
                    plannedItem.SizeBytes,
                    plannedItem.SourceInternalDateUtc,
                    mboxRecord.EnvelopeFrom,
                    out var existingItem) && existingItem != null)
                {
                    manifestItems.Add(existingItem);
                }
                else
                {
                    using var rawStream = new MemoryStream(mboxRecord.RawMimeBytes, writable: false);
                    var manifestItem = await _storageManager.StoreMimeItemAsync(
                        stagingDir,
                        count,
                        plannedItem.ItemId,
                        plannedItem.MappedFolder,
                        rawStream,
                        expectedSha256: plannedItem.SourceSha256,
                        envelopeFrom: mboxRecord.EnvelopeFrom,
                        ct,
                        sourceInternalDateUtc: plannedItem.SourceInternalDateUtc);

                    manifestItems.Add(manifestItem);
                }

                record.ItemsRead = count;
                record.ItemsWritten = manifestItems.Count;
                if (plan.TotalItems > 0)
                {
                    record.PercentComplete = Math.Min(89, (int)((double)count / plan.TotalItems * 85));
                }
                _saveJobRecord(record);
            }
        }
        else
        {
            int count = 0;
            foreach (var item in plan.Items)
            {
                ct.ThrowIfCancellationRequested();
                count++;

                if (_storageManager.TryGetVerifiedStagedItem(
                    stagingDir,
                    item.Ordinal,
                    item.ItemId,
                    item.MappedFolder,
                    item.SourceSha256,
                    item.SizeBytes,
                    item.SourceInternalDateUtc,
                    null,
                    out var existingItem) && existingItem != null)
                {
                    manifestItems.Add(existingItem);
                }
                else
                {
                    using var fileStream = new FileStream(item.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var manifestItem = await _storageManager.StoreMimeItemAsync(
                        stagingDir,
                        item.Ordinal,
                        item.ItemId,
                        item.MappedFolder,
                        fileStream,
                        expectedSha256: item.SourceSha256,
                        envelopeFrom: null,
                        ct,
                        sourceInternalDateUtc: item.SourceInternalDateUtc);

                    manifestItems.Add(manifestItem);
                }

                record.ItemsRead = count;
                record.ItemsWritten = manifestItems.Count;
                if (plan.TotalItems > 0)
                {
                    record.PercentComplete = Math.Min(89, (int)((double)count / plan.TotalItems * 85));
                }
                _saveJobRecord(record);
            }
        }

        // 4. Validate entire physical source against frozen plan AGAIN immediately before publication
        await ValidateSourceAgainstPlanAsync(plan, ct);

        // 5. Build immutable manifest and publish
        var manifest = new ArchiveManifest
        {
            ArchiveId = plan.ArchiveId,
            ArchiveName = plan.ArchiveName,
            CompanyId = plan.CompanyId,
            CompanyName = plan.CompanyName,
            ProjectId = plan.ProjectId,
            ProjectName = plan.ProjectName,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            SourceKind = plan.SourceKind,
            Dialect = plan.Dialect,
            SourceFingerprint = plan.SourceFingerprint,
            TotalItems = manifestItems.Count,
            TotalSizeBytes = manifestItems.Sum(i => i.ByteLength),
            TruncatedItemsCount = manifestItems.Count(i => i.IsBodyTruncated),
            Folders = plan.Folders,
            Items = manifestItems,
            QualificationFingerprint = plan.QualificationFingerprint,
            DateFilterBlocked = plan.DateFilterBlocked,
            QualificationIsPartial = plan.QualificationIsPartial,
            QualificationWarnings = plan.QualificationWarnings
        };

        await _storageManager.WriteManifestAsync(stagingDir, manifest, ct);
        _storageManager.PublishStagingToManagedArchive(stagingDir, plan.ArchiveId);

        return manifest;
    }

    private async Task ValidateSourceAgainstPlanAsync(ArchiveIngestPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Items == null || plan.Items.Count == 0)
        {
            throw new InvalidOperationException("[BÜTÜNLÜK ENGELİ] Dondurulmuş planda kaynak öğe bulunmuyor.");
        }

        if (plan.QualificationFingerprint is not null)
        {
            var qualification = new NormalizedSourceQualificationReader().ReadManifestSource(plan.SourceKind, plan.SourceRootPath ?? throw new InvalidDataException("Qualification kaynak yolu eksik."), plan.QualificationSourcePaths, ct);
            if (!string.Equals(qualification?.Fingerprint, plan.QualificationFingerprint, StringComparison.Ordinal) ||
                qualification?.DateFilterBlocked != plan.DateFilterBlocked || qualification?.IsPartial != plan.QualificationIsPartial)
                throw new InvalidOperationException("[BÜTÜNLÜK ENGELİ] Kaynak qualification bilgisi önizlemeden sonra değişti.");
        }

        // Check if SourceHandle entry exists in registry (if present in this process)
        if (!string.IsNullOrWhiteSpace(plan.SourceHandle))
        {
            var entry = _handleRegistry.GetMimeSourceEntry(plan.SourceHandle);
            if (entry?.Manifest != null &&
                !string.Equals(entry.Manifest.AggregateFingerprint, plan.SourceFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("[GÜVENLİK ENGELİ] Kaynak içerik parmak izi değiştiği için işlem sürdürülemez.");
            }
        }

        if (string.Equals(plan.SourceKind, "bridge-export", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(plan.SourceJobId))
        {
            await ValidateBridgeExportSourceAsync(plan, ct);
        }
        else if (string.Equals(plan.SourceKind, "mbox", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(plan.Dialect, "mboxrd", StringComparison.OrdinalIgnoreCase))
        {
            await ValidateMboxSourceAsync(plan, ct);
        }
        else if (string.Equals(plan.SourceKind, "eml-tree", StringComparison.OrdinalIgnoreCase))
        {
            await ValidateEmlTreeSourceAsync(plan, ct);
        }
        else
        {
            await ValidateEmlFilesSourceAsync(plan, ct);
        }
    }

    private async Task ValidateEmlFilesSourceAsync(ArchiveIngestPlan plan, CancellationToken ct)
    {
        var entries = new List<MimeSourceEntry>();
        using var sha = SHA256.Create();

        foreach (var item in plan.Items)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(item.SourcePath) || !File.Exists(item.SourcePath))
            {
                throw new FileNotFoundException($"[BÜTÜNLÜK ENGELİ] Kaynak EML dosyası bulunamadı veya silinmiş: {item.SourcePath}");
            }

            var fi = new FileInfo(item.SourcePath);
            if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Kaynak dosya reparse point içeriyor: {item.SourcePath}");
            }

            if (fi.Length != item.SizeBytes)
            {
                throw new InvalidOperationException(
                    $"[BÜTÜNLÜK ENGELİ] Kaynak dosya bayt uzunluğu değişmiş ({item.SourcePath}). Beklenen: {item.SizeBytes}, Mevcut: {fi.Length}");
            }

            byte[] fileBytes = await File.ReadAllBytesAsync(item.SourcePath, ct);
            string computedSha = Convert.ToHexString(sha.ComputeHash(fileBytes)).ToLowerInvariant();
            if (!string.Equals(computedSha, item.SourceSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"[BÜTÜNLÜK ENGELİ] Kaynak dosya hash uyuşmazlığı ({item.SourcePath}). Beklenen: {item.SourceSha256}, Mevcut: {computedSha}");
            }

            entries.Add(new MimeSourceEntry
            {
                CanonicalPath = item.SourcePath,
                RelativePath = item.RelativePath ?? Path.GetFileName(item.SourcePath),
                MappedFolder = item.MappedFolder,
                SizeBytes = fi.Length,
                Sha256 = computedSha,
                PhysicalOrdinal = item.Ordinal
            });
        }

        var manifest = new MimeSourceManifest
        {
            SourceKind = "eml-files",
            Dialect = plan.Dialect ?? "rfc822",
            RootPath = plan.SourceRootPath ?? string.Empty,
            Entries = entries,
            TotalFiles = entries.Count,
            IgnoredNonEmlFilesCount = 0
        };

        string aggregateFingerprint = MimeSourceInspector.ComputeManifestFingerprint(manifest);
        if (!string.Equals(aggregateFingerprint, plan.SourceFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("[BÜTÜNLÜK ENGELİ] Kaynak içerik parmak izi dondurulmuş plan ile uyuşmuyor.");
        }
    }

    private async Task ValidateEmlTreeSourceAsync(ArchiveIngestPlan plan, CancellationToken ct)
    {
        string rootDir = plan.SourceRootPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rootDir) || !Directory.Exists(rootDir))
        {
            throw new DirectoryNotFoundException($"[BÜTÜNLÜK ENGELİ] Kaynak EML klasörü bulunamadı veya silinmiş: {rootDir}");
        }

        var rootDi = new DirectoryInfo(rootDir);
        if (rootDi.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Kaynak kök klasörü reparse point içeriyor: {rootDir}");
        }

        using var sha = SHA256.Create();
        foreach (var item in plan.Items)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(item.SourcePath) || !File.Exists(item.SourcePath))
            {
                throw new FileNotFoundException($"[BÜTÜNLÜK ENGELİ] Kaynak EML dosyası bulunamadı veya silinmiş: {item.SourcePath}");
            }

            var fi = new FileInfo(item.SourcePath);
            if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Kaynak dosya reparse point içeriyor: {item.SourcePath}");
            }

            string fullPath = Path.GetFullPath(item.SourcePath);
            if (!fullPath.StartsWith(Path.GetFullPath(rootDir), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Kaynak dosya kök dizin dışına taşıyor: {item.SourcePath}");
            }

            if (fi.Length != item.SizeBytes)
            {
                throw new InvalidOperationException(
                    $"[BÜTÜNLÜK ENGELİ] Kaynak dosya bayt uzunluğu değişmiş ({item.SourcePath}). Beklenen: {item.SizeBytes}, Mevcut: {fi.Length}");
            }

            byte[] fileBytes = await File.ReadAllBytesAsync(item.SourcePath, ct);
            string computedSha = Convert.ToHexString(sha.ComputeHash(fileBytes)).ToLowerInvariant();
            if (!string.Equals(computedSha, item.SourceSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"[BÜTÜNLÜK ENGELİ] Kaynak dosya hash uyuşmazlığı ({item.SourcePath}). Beklenen: {item.SourceSha256}, Mevcut: {computedSha}");
            }
        }

        var treeManifest = _inspector.BuildEmlDirectoryManifest(rootDir);
        if (!string.Equals(treeManifest.AggregateFingerprint, plan.SourceFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("[BÜTÜNLÜK ENGELİ] Kaynak klasör parmak izi dondurulmuş plan ile uyuşmuyor.");
        }
    }

    private Task ValidateMboxSourceAsync(ArchiveIngestPlan plan, CancellationToken ct)
    {
        string mboxPath = !string.IsNullOrEmpty(plan.SourceRootPath) && File.Exists(plan.SourceRootPath)
            ? plan.SourceRootPath
            : (plan.Items.FirstOrDefault()?.SourcePath ?? string.Empty);

        if (string.IsNullOrWhiteSpace(mboxPath) || !File.Exists(mboxPath))
        {
            throw new FileNotFoundException($"[BÜTÜNLÜK ENGELİ] Kaynak MBOX dosyası bulunamadı veya silinmiş: {mboxPath}");
        }

        var fi = new FileInfo(mboxPath);
        if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Kaynak MBOX dosyası reparse point içeriyor: {mboxPath}");
        }

        BridgeMboxrdValidator.ValidateStrictMboxrd(mboxPath);

        List<MboxrdRecord> records;
        using (var fs = new FileStream(mboxPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            records = MboxrdRecordReader.EnumerateRecords(
                fs,
                ArchiveMimeParser.MaxRawMessageSizeBytes,
                MboxrdRecordReader.DefaultMaxLineLengthBytes).ToList();
        }

        if (records.Count != plan.Items.Count)
        {
            throw new InvalidOperationException(
                $"[BÜTÜNLÜK ENGELİ] Kaynak MBOX kayıt sayısı ({records.Count}) dondurulmuş plan ({plan.Items.Count}) ile uyuşmuyor.");
        }

        using var sha = SHA256.Create();
        for (int i = 0; i < records.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var rec = records[i];
            var item = plan.Items[i];

            if (rec.RawMimeBytes.Length != item.SizeBytes)
            {
                throw new InvalidOperationException(
                    $"[BÜTÜNLÜK ENGELİ] MBOX {i + 1}. kayıt bayt uzunluğu değişmiş. Beklenen: {item.SizeBytes}, Mevcut: {rec.RawMimeBytes.Length}");
            }

            string recSha = Convert.ToHexString(sha.ComputeHash(rec.RawMimeBytes)).ToLowerInvariant();
            if (!string.Equals(recSha, item.SourceSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"[BÜTÜNLÜK ENGELİ] MBOX {i + 1}. kayıt hash uyuşmazlığı. Beklenen: {item.SourceSha256}, Mevcut: {recSha}");
            }
        }

        var mboxManifest = _inspector.BuildMboxManifest(mboxPath);
        if (!string.Equals(mboxManifest.AggregateFingerprint, plan.SourceFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("[BÜTÜNLÜK ENGELİ] Kaynak MBOX parmak izi dondurulmuş plan ile uyuşmuyor.");
        }

        return Task.CompletedTask;
    }

    private async Task ValidateBridgeExportSourceAsync(ArchiveIngestPlan plan, CancellationToken ct)
    {
        string? exportDir = plan.SourceRootPath;
        if (string.IsNullOrEmpty(exportDir) || !Directory.Exists(exportDir))
        {
            if (!string.IsNullOrEmpty(plan.SourceJobId))
            {
                var job = _jobManager.GetJob(plan.SourceJobId);
                if (job != null && !string.IsNullOrEmpty(job.OutputPath) && Directory.Exists(job.OutputPath))
                {
                    exportDir = job.OutputPath;
                }
            }
        }

        if (string.IsNullOrEmpty(exportDir) || !Directory.Exists(exportDir))
        {
            throw new DirectoryNotFoundException($"[BÜTÜNLÜK ENGELİ] Dışa aktarım çıktı dizini bulunamadı: {exportDir}");
        }

        var di = new DirectoryInfo(exportDir);
        if (di.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Dışa aktarım dizini reparse point içeriyor: {exportDir}");
        }

        string manifestPath = Path.Combine(exportDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"[BÜTÜNLÜK ENGELİ] Dışa aktarım manifest.json bulunamadı: {manifestPath}");
        }

        BridgeExportManifest exportManifest;
        using (var fs = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            exportManifest = JsonSerializer.Deserialize<BridgeExportManifest>(fs, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                ?? throw new InvalidOperationException("[BÜTÜNLÜK ENGELİ] Export manifesti boş.");
        }

        if (!string.Equals(exportManifest.CompanyId, plan.CompanyId, StringComparison.Ordinal) ||
            !string.Equals(exportManifest.ProjectId, plan.ProjectId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("[GÜVENLİK ENGELİ] Export manifesti müşteri/proje bağlamı uyuşmuyor.");
        }

        if (!string.IsNullOrEmpty(plan.SourceJobId) && !string.Equals(exportManifest.JobId, plan.SourceJobId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("[GÜVENLİK ENGELİ] Export manifesti iş kimliği uyuşmuyor.");
        }

        string itemsSummary = string.Join(";", exportManifest.Items.Select(i => $"{i.ItemId}:{i.SourceSha256}:{i.StoredSha256}"));
        string hashesDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(itemsSummary))).ToLowerInvariant();
        string expectedFingerprint = $"bridge-export:{exportManifest.JobId}:{exportManifest.CompletedAtUtc:o}:{hashesDigest}";
        if (!string.Equals(plan.SourceFingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("[BÜTÜNLÜK ENGELİ] Kaynak içerik parmak izi değiştiği için işlem sürdürülemez.");
        }

        bool isMbox = string.Equals(plan.Dialect, "mbox", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(plan.Dialect, "mboxrd", StringComparison.OrdinalIgnoreCase);

        if (isMbox)
        {
            var containerGroups = exportManifest.Items
                .GroupBy(i => i.RelativeOutputPath, StringComparer.Ordinal)
                .ToList();

            foreach (var group in containerGroups)
            {
                string containerRelPath = group.Key;
                string containerFullPath = Path.Combine(exportDir, containerRelPath);
                if (!File.Exists(containerFullPath))
                {
                    throw new FileNotFoundException($"[BÜTÜNLÜK ENGELİ] MBOX konteyner dosyası bulunamadı: {containerRelPath}");
                }

                var fi = new FileInfo(containerFullPath);
                if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Reparse point MBOX konteyner dosyası: {containerFullPath}");
                }

                List<MboxrdRecord> records;
                using (var fs = new FileStream(containerFullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    records = MboxrdRecordReader.EnumerateRecords(
                        fs,
                        ArchiveMimeParser.MaxRawMessageSizeBytes,
                        MboxrdRecordReader.DefaultMaxLineLengthBytes).ToList();
                }

                var expectedItems = group.ToList();
                if (records.Count != expectedItems.Count)
                {
                    throw new InvalidOperationException(
                        $"[BÜTÜNLÜK ENGELİ] MBOX konteynerindeki kayıt sayısı ({records.Count}) manifest ile ({expectedItems.Count}) uyuşmuyor: {containerRelPath}");
                }

                for (int i = 0; i < records.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    string recSha = Convert.ToHexString(SHA256.HashData(records[i].RawMimeBytes)).ToLowerInvariant();
                    if (!string.Equals(recSha, expectedItems[i].StoredSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"[BÜTÜNLÜK ENGELİ] MBOX kayıt hash uyuşmazlığı. Beklenen: {expectedItems[i].StoredSha256}, Hesaplanan: {recSha}");
                    }
                }
            }
        }
        else
        {
            foreach (var item in exportManifest.Items)
            {
                ct.ThrowIfCancellationRequested();
                string itemFullPath = Path.Combine(exportDir, item.RelativeOutputPath);
                if (!File.Exists(itemFullPath))
                {
                    throw new FileNotFoundException($"[BÜTÜNLÜK ENGELİ] Dışa aktarım dosyası bulunamadı: {item.RelativeOutputPath}");
                }

                var fi = new FileInfo(itemFullPath);
                if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Dışa aktarım dosyası reparse point içeriyor: {itemFullPath}");
                }

                byte[] bytes = await File.ReadAllBytesAsync(itemFullPath, ct);
                string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!string.Equals(sha, item.StoredSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"[BÜTÜNLÜK ENGELİ] Dışa aktarım dosyası hash uyuşmazlığı: {item.RelativeOutputPath}");
                }
            }
        }
    }

    public async Task ExecuteReindexAsync(LocalJobRecord record, string archiveId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveId);

        try
        {
            record.Status = "verifying";
            record.Stage = "Yeniden İndeksleniyor";
            record.StartedAt ??= DateTimeOffset.UtcNow;
            record.ArchiveId = archiveId;
            _saveJobRecord(record);

            var manifest = _storageManager.GetArchiveManifest(archiveId);
            if (manifest == null)
            {
                throw new InvalidOperationException($"Arşiv bulunamadı veya manifest.json eksik: {archiveId}");
            }

            record.ArchiveName = manifest.ArchiveName;
            record.TotalItems = manifest.TotalItems;
            _saveJobRecord(record);

            await _searchIndex.IndexArchiveAsync(
                manifest,
                _storageManager,
                (indexed, total) =>
                {
                    if (total > 0)
                    {
                        record.ItemsWritten = indexed;
                        record.PercentComplete = Math.Min(99, (int)((double)indexed / total * 100));
                        _saveJobRecord(record);
                    }
                },
                ct);

            record.Status = "completed";
            record.Stage = "Tamamlandı";
            record.PercentComplete = 100;
            record.CompletedAt = DateTimeOffset.UtcNow;
            record.ItemsWritten = manifest.TotalItems;
            record.ItemsRead = manifest.TotalItems;
            _saveJobRecord(record);
        }
        catch (Exception ex)
        {
            record.Status = "failed";
            record.Stage = "Hata";
            record.ErrorMessage = ex.Message;
            record.CompletedAt = DateTimeOffset.UtcNow;
            _saveJobRecord(record);
            throw;
        }
    }
}
