using System;
using System.Collections.Concurrent;
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
using MimeKit;

namespace BitigMail.LocalHost.Archive;

public sealed class ArchiveCatalogService
{
    private readonly ArchiveStorageManager _storageManager;
    private readonly ArchiveSearchIndex _searchIndex;
    private readonly ArchivePlanStore _planStore;
    private readonly FileHandleRegistry _handleRegistry;
    private readonly JobManager _jobManager;
    private readonly IDiskCapacityProbe _capacityProbe;

    public ArchiveCatalogService(
        ArchiveStorageManager storageManager,
        ArchiveSearchIndex searchIndex,
        ArchivePlanStore planStore,
        FileHandleRegistry handleRegistry,
        JobManager jobManager,
        IDiskCapacityProbe? capacityProbe = null)
    {
        _storageManager = storageManager;
        _searchIndex = searchIndex;
        _planStore = planStore;
        _handleRegistry = handleRegistry;
        _jobManager = jobManager;
        _capacityProbe = capacityProbe ?? new WindowsDiskCapacityProbe();

        ReconcileOnStartup();
    }

    public ArchiveStorageManager StorageManager => _storageManager;
    public ArchiveSearchIndex SearchIndex => _searchIndex;
    public ArchivePlanStore PlanStore => _planStore;

    public void ReconcileOnStartup()
    {
        try
        {
            var diskManifests = _storageManager.GetAllArchiveManifests();
            if (diskManifests.Count == 0) return;

            var dbItems = _searchIndex.GetCatalogItems();
            var dbArchiveIds = new HashSet<string>(dbItems.Select(i => i.ArchiveId), StringComparer.Ordinal);

            bool needsRebuild = false;
            foreach (var manifest in diskManifests)
            {
                if (!dbArchiveIds.Contains(manifest.ArchiveId))
                {
                    needsRebuild = true;
                    break;
                }
            }

            if (needsRebuild)
            {
                _searchIndex.RebuildDatabaseAsync(_storageManager, CancellationToken.None).GetAwaiter().GetResult();
            }
        }
        catch { }
    }

    public IReadOnlyDictionary<string, ArchiveManifest> GetRegisteredManifests()
    {
        var manifests = _storageManager.GetAllArchiveManifests();
        return manifests.ToDictionary(m => m.ArchiveId, m => m, StringComparer.Ordinal);
    }

    public ArchiveIngestPreviewResponse CreatePreview(ArchiveIngestPreviewRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);

        if (string.IsNullOrWhiteSpace(req.ArchiveName))
            throw new ArgumentException("Arşiv adı zorunludur.");
        if (string.IsNullOrWhiteSpace(req.CompanyId) || string.IsNullOrWhiteSpace(req.ProjectId))
            throw new ArgumentException("Müşteri kimliği (companyId) ve proje kimliği (projectId) zorunludur.");

        bool hasSourceHandle = !string.IsNullOrWhiteSpace(req.SourceHandle);
        bool hasSourceJobId = !string.IsNullOrWhiteSpace(req.SourceJobId);

        if (hasSourceHandle == hasSourceJobId)
        {
            throw new ArgumentException("Tam olarak bir kaynak tanıtıcısı (sourceHandle) veya tamamlanmış iş kimliği (sourceJobId) belirtilmelidir.");
        }

        string previewId = "aprv_" + Guid.NewGuid().ToString("N");
        string archiveId = "arc_" + Guid.NewGuid().ToString("N")[..16];

        string sourceKind;
        string dialect;
        string sourceFingerprint;
        string? sourceRootPath = null;
        int totalFiles = 0;
        int totalItems = 0;
        long totalSizeBytes = 0;
        var folderList = new List<ArchiveFolderSummary>();
        var plannedItems = new List<ArchiveIngestPlannedItem>();
        bool canIngest = true;
        string? blockerReason = null;
        NormalizedSourceQualification? qualification = null;

        if (hasSourceHandle)
        {
            var entry = _handleRegistry.GetMimeSourceEntry(req.SourceHandle!);
            if (entry == null || entry.Manifest == null)
            {
                throw new InvalidOperationException("Kaynak tanıtıcısı bulunamadı veya süresi dolmuş.");
            }

            var manifest = entry.Manifest;
            sourceKind = manifest.SourceKind;
            dialect = manifest.Dialect;
            sourceFingerprint = manifest.AggregateFingerprint;
            sourceRootPath = manifest.RootPath;
            qualification = new NormalizedSourceQualificationReader().Read(manifest);
            totalFiles = manifest.TotalFiles > 0 ? manifest.TotalFiles : manifest.Entries.Count;

            // Strict mboxrd verification if mbox
            if (string.Equals(manifest.SourceKind, "mbox", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(manifest.Dialect, "mboxrd", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(manifest.RootPath))
                {
                    try
                    {
                        BridgeMboxrdValidator.ValidateStrictMboxrd(manifest.RootPath);
                    }
                    catch (Exception ex)
                    {
                        canIngest = false;
                        blockerReason = $"Geçersiz veya bozuk mboxrd arşivi: {ex.Message}";
                    }
                }
            }

            int ordinal = 0;
            foreach (var e in manifest.Entries)
            {
                ordinal++;
                if (e.SizeBytes > ArchiveMimeParser.MaxRawMessageSizeBytes)
                {
                    canIngest = false;
                    blockerReason = $"İleti boyutu izin verilen tavanı (64 MiB) aşıyor: {e.SizeBytes} bayt.";
                }

                plannedItems.Add(new ArchiveIngestPlannedItem
                {
                    Ordinal = ordinal,
                    ItemId = $"msg_{ordinal:D8}",
                    SourcePath = e.CanonicalPath,
                    RelativePath = e.RelativePath,
                    MappedFolder = string.IsNullOrWhiteSpace(e.MappedFolder) ? "INBOX" : e.MappedFolder,
                    SizeBytes = e.SizeBytes,
                    SourceSha256 = e.Sha256
                });
            }

            totalItems = plannedItems.Count;
            totalSizeBytes = plannedItems.Sum(i => i.SizeBytes);

            folderList = plannedItems
                .GroupBy(i => i.MappedFolder, StringComparer.Ordinal)
                .Select(g => new ArchiveFolderSummary
                {
                    FolderName = g.Key,
                    ItemCount = g.Count(),
                    TotalSizeBytes = g.Sum(i => i.SizeBytes)
                })
                .OrderBy(f => f.FolderName, StringComparer.Ordinal)
                .ToList();
        }
        else
        {
            // Source is completed TASK-017 Bridge Export Job
            var job = _jobManager.GetJob(req.SourceJobId!);
            if (job == null)
            {
                throw new InvalidOperationException($"Kaynak iş kaydı bulunamadı: {req.SourceJobId}");
            }
            if (job.Status != "completed")
            {
                throw new InvalidOperationException("Kaynak iş henüz tamamlanmamış.");
            }
            if (string.IsNullOrEmpty(job.OutputPath) || !Directory.Exists(job.OutputPath))
            {
                throw new InvalidOperationException("Kaynak işin çıktı dizini bulunamadı.");
            }

            // Verify company and project context cannot switch
            if (!string.Equals(job.ClientContext.CompanyId, req.CompanyId, StringComparison.Ordinal) ||
                !string.Equals(job.ClientContext.ProjectId, req.ProjectId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Aktarım işinin şirket veya proje bağlamı istek ile uyuşmuyor (bağlam değiştirilemez).");
            }

            string manifestPath = Path.Combine(job.OutputPath, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException("Kaynak işin manifest.json dosyası bulunamadı.");
            }

            BridgeExportManifest exportManifest;
            try
            {
                using var fs = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                exportManifest = JsonSerializer.Deserialize<BridgeExportManifest>(fs, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                    ?? throw new InvalidOperationException("Export manifesti boş.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Export manifesti okunamadı: {ex.Message}");
            }

            // Completed job/manifest identity verification
            if (!string.Equals(exportManifest.JobId, job.JobId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Export manifestindeki iş kimliği ile kayıt kimliği uyuşmuyor.");
            }
            if (!string.Equals(exportManifest.CompanyId, req.CompanyId, StringComparison.Ordinal) ||
                !string.Equals(exportManifest.ProjectId, req.ProjectId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Export manifestindeki şirket veya proje bağlamı uyuşmuyor.");
            }

            // Validate safe paths for all relative output paths
            foreach (var item in exportManifest.Items)
            {
                if (string.IsNullOrWhiteSpace(item.RelativeOutputPath) ||
                    Path.IsPathRooted(item.RelativeOutputPath) ||
                    item.RelativeOutputPath.Contains("..") ||
                    item.RelativeOutputPath.StartsWith('/') ||
                    item.RelativeOutputPath.StartsWith('\\'))
                {
                    throw new InvalidOperationException($"Güvenli olmayan bağıl yol tespit edildi: {item.RelativeOutputPath}");
                }
            }

            // Freeze actual source hashes into sourceFingerprint
            string itemsSummary = string.Join(";", exportManifest.Items.Select(i => $"{i.ItemId}:{i.SourceSha256}:{i.StoredSha256}"));
            string hashesDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(itemsSummary))).ToLowerInvariant();

            sourceKind = "bridge-export";
            dialect = exportManifest.TargetFormat;
            sourceFingerprint = $"bridge-export:{req.SourceJobId}:{exportManifest.CompletedAtUtc:o}:{hashesDigest}";
            sourceRootPath = job.OutputPath;

            bool isMbox = string.Equals(dialect, "mbox", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(dialect, "mboxrd", StringComparison.OrdinalIgnoreCase);

            if (isMbox)
            {
                var distinctContainers = exportManifest.Items
                    .Select(i => i.RelativeOutputPath)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                totalFiles = distinctContainers.Count;

                foreach (var containerRel in distinctContainers)
                {
                    string containerFullPath = Path.Combine(job.OutputPath, containerRel);
                    if (!File.Exists(containerFullPath))
                    {
                        canIngest = false;
                        blockerReason = $"MBOX konteyner dosyası bulunamadı: {containerRel}";
                        break;
                    }
                }

                int ordinal = 0;
                foreach (var item in exportManifest.Items)
                {
                    ordinal++;
                    string containerFullPath = Path.Combine(job.OutputPath, item.RelativeOutputPath);
                    long itemSize = item.OriginalLength > 0 ? item.OriginalLength : 0;
                    if (itemSize > ArchiveMimeParser.MaxRawMessageSizeBytes)
                    {
                        canIngest = false;
                        blockerReason = $"İleti boyutu izin verilen tavanı (64 MiB) aşıyor: {itemSize} bayt.";
                    }

                    plannedItems.Add(new ArchiveIngestPlannedItem
                    {
                        Ordinal = ordinal,
                        ItemId = $"msg_{ordinal:D8}",
                        SourcePath = containerFullPath,
                        RelativePath = item.RelativeOutputPath,
                        MappedFolder = string.IsNullOrWhiteSpace(item.OriginalFolder) ? "INBOX" : item.OriginalFolder,
                        SizeBytes = itemSize,
                        SourceSha256 = item.StoredSha256,
                        SourceInternalDateUtc = item.InternalDateUtc
                    });
                }
            }
            else
            {
                totalFiles = exportManifest.Items.Count;
                int ordinal = 0;
                foreach (var item in exportManifest.Items)
                {
                    ordinal++;
                    string itemFullPath = Path.Combine(job.OutputPath, item.RelativeOutputPath);
                    if (!File.Exists(itemFullPath))
                    {
                        canIngest = false;
                        blockerReason = $"Dışa aktarım dosyası bulunamadı: {item.RelativeOutputPath}";
                        break;
                    }

                    var fi = new FileInfo(itemFullPath);
                    if (fi.Length > ArchiveMimeParser.MaxRawMessageSizeBytes)
                    {
                        canIngest = false;
                        blockerReason = $"İleti boyutu izin verilen tavanı (64 MiB) aşıyor: {fi.Length} bayt.";
                    }

                    plannedItems.Add(new ArchiveIngestPlannedItem
                    {
                        Ordinal = ordinal,
                        ItemId = $"msg_{ordinal:D8}",
                        SourcePath = itemFullPath,
                        RelativePath = item.RelativeOutputPath,
                        MappedFolder = string.IsNullOrWhiteSpace(item.OriginalFolder) ? "INBOX" : item.OriginalFolder,
                        SizeBytes = fi.Length,
                        SourceSha256 = item.StoredSha256,
                        SourceInternalDateUtc = item.InternalDateUtc
                    });
                }
            }

            totalItems = plannedItems.Count;
            totalSizeBytes = plannedItems.Sum(i => i.SizeBytes);

            folderList = plannedItems
                .GroupBy(i => i.MappedFolder, StringComparer.Ordinal)
                .Select(g => new ArchiveFolderSummary
                {
                    FolderName = g.Key,
                    ItemCount = g.Count(),
                    TotalSizeBytes = g.Sum(i => i.SizeBytes)
                })
                .OrderBy(f => f.FolderName, StringComparer.Ordinal)
                .ToList();
        }

        long? estimatedRequiredBytes = null;
        long? availableFreeBytes = null;
        string estimateBasis = "Aynı hacimde ham staging+yayımlanmış arşiv ile SQLite indeks/WAL geçici alan toplamı";
        try
        {
            string archiveBase = Path.GetFullPath(_storageManager.ArchivesBaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            string indexBase = Path.GetFullPath(Path.GetDirectoryName(_searchIndex.DatabasePath)!).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(archiveBase, indexBase, StringComparison.OrdinalIgnoreCase))
            {
                if (canIngest)
                {
                    canIngest = false;
                    blockerReason = "[DİSK KAPASİTESİ ENGELİ] Arşiv ve indeks farklı hedef dizinlerde; bu yapı için güvenli birleşik kapasite hesabı desteklenmiyor.";
                }
            }
            else
            {
                estimatedRequiredBytes = DiskCapacityPlanning.EstimateArchive(totalSizeBytes, totalItems);
                var capacity = _capacityProbe.Probe(archiveBase);
                availableFreeBytes = capacity.IsAvailable ? capacity.AvailableBytes : null;
                string? capacityBlocker = DiskCapacityPlanning.CapacityBlocker(estimatedRequiredBytes.Value, capacity);
                if (canIngest && capacityBlocker != null)
                {
                    canIngest = false;
                    blockerReason = capacityBlocker + " Arşivleme başlatılamaz.";
                }
            }
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
        {
            if (canIngest)
            {
                canIngest = false;
                blockerReason = "[DİSK KAPASİTESİ ENGELİ] Arşiv için tahmini gereken alan güvenli sayı aralığını aşıyor.";
            }
        }

        var plan = new ArchiveIngestPlan
        {
            PlanId = previewId,
            PreviewId = previewId,
            ArchiveId = archiveId,
            ArchiveName = req.ArchiveName.Trim(),
            CompanyId = req.CompanyId,
            ProjectId = req.ProjectId,
            CompanyName = req.CompanyName,
            ProjectName = req.ProjectName,
            SourceHandle = req.SourceHandle,
            SourceJobId = req.SourceJobId,
            SourceKind = sourceKind,
            Dialect = dialect,
            SourceFingerprint = sourceFingerprint,
            SourceRootPath = sourceRootPath,
            TotalItems = totalItems,
            TotalSizeBytes = totalSizeBytes,
            Folders = folderList,
            Items = plannedItems,
            EstimatedRequiredBytes = estimatedRequiredBytes,
            EstimateBasis = estimateBasis,
            AvailableFreeBytes = availableFreeBytes,
            CanIngest = canIngest,
            BlockerReason = blockerReason
            ,QualificationFingerprint = qualification?.Fingerprint
            ,DateFilterBlocked = qualification?.DateFilterBlocked ?? false
            ,QualificationIsPartial = qualification?.IsPartial ?? false
            ,QualificationWarnings = qualification?.Warnings.ToList() ?? new()
            ,QualificationSourcePaths = hasSourceHandle ? _handleRegistry.GetMimeSourceEntry(req.SourceHandle!)!.Manifest!.Entries.Select(x => x.CanonicalPath).ToList() : new()
        };

        _planStore.SavePlan(plan);

        return new ArchiveIngestPreviewResponse
        {
            PreviewId = previewId,
            ArchiveName = plan.ArchiveName,
            CompanyId = plan.CompanyId,
            ProjectId = plan.ProjectId,
            CompanyName = plan.CompanyName,
            ProjectName = plan.ProjectName,
            SourceKind = plan.SourceKind,
            Dialect = plan.Dialect,
            SourceFingerprint = plan.SourceFingerprint,
            TotalFiles = totalFiles,
            TotalItems = totalItems,
            TotalSizeBytes = totalSizeBytes,
            Folders = folderList,
            CanIngest = canIngest,
            BlockerReason = blockerReason,
            EstimatedRequiredBytes = estimatedRequiredBytes,
            AvailableFreeBytes = availableFreeBytes,
            EstimateBasis = estimateBasis
            ,DateFilterBlocked = plan.DateFilterBlocked
            ,QualificationIsPartial = plan.QualificationIsPartial
            ,QualificationWarnings = plan.QualificationWarnings
        };
    }

    public ArchiveIngestPreviewResponse GetPreview(string previewId)
    {
        var plan = _planStore.GetPlan(previewId)
            ?? throw new InvalidOperationException($"Arşiv önizleme planı bulunamadı: {previewId}");

        return new ArchiveIngestPreviewResponse
        {
            PreviewId = plan.PreviewId,
            ArchiveName = plan.ArchiveName,
            CompanyId = plan.CompanyId,
            ProjectId = plan.ProjectId,
            CompanyName = plan.CompanyName,
            ProjectName = plan.ProjectName,
            SourceKind = plan.SourceKind,
            Dialect = plan.Dialect,
            SourceFingerprint = plan.SourceFingerprint,
            TotalFiles = plan.TotalItems,
            TotalItems = plan.TotalItems,
            TotalSizeBytes = plan.TotalSizeBytes,
            Folders = plan.Folders,
            CanIngest = plan.CanIngest,
            BlockerReason = plan.BlockerReason,
            EstimatedRequiredBytes = plan.EstimatedRequiredBytes,
            AvailableFreeBytes = plan.AvailableFreeBytes,
            EstimateBasis = plan.EstimateBasis
            ,DateFilterBlocked = plan.DateFilterBlocked
            ,QualificationIsPartial = plan.QualificationIsPartial
            ,QualificationWarnings = plan.QualificationWarnings
        };
    }

    public ArchiveSearchResponse Search(ArchiveSearchRequest req, CancellationToken cancellationToken = default)
    {
        var manifests = GetRegisteredManifests();
        if ((!string.IsNullOrWhiteSpace(req.StartDate) || !string.IsNullOrWhiteSpace(req.EndDate)) &&
            req.SelectedScopes.Any(scope => manifests.TryGetValue(scope.ArchiveId, out var manifest) && manifest.DateFilterBlocked))
            throw new ArchiveSearchPolicyException("Seçilen arşivlerden en az birinin özgün tarih sadakati doğrulanmadı; tarih filtresi kullanılamaz.");
        var response = req.AdvancedFilter is null
            ? _searchIndex.Search(req, manifests)
            : SearchWithAdvancedFilter(req, manifests, cancellationToken);
        var warnings = req.SelectedScopes
            .Where(scope => manifests.ContainsKey(scope.ArchiveId))
            .SelectMany(scope => manifests[scope.ArchiveId].QualificationWarnings)
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return response with { QualificationWarnings = warnings };
    }

    private ArchiveSearchResponse SearchWithAdvancedFilter(ArchiveSearchRequest req, IReadOnlyDictionary<string, ArchiveManifest> manifests, CancellationToken cancellationToken)
    {
        bool dateBlocked = req.SelectedScopes.Any(scope => manifests.TryGetValue(scope.ArchiveId, out var manifest) && manifest.DateFilterBlocked);
        var compiled = AdvancedMailFilter.Compile(req.AdvancedFilter!, dateFidelityUnresolved: dateBlocked);
        const int scanPageSize = ArchiveSearchPolicy.MaxPageSize;
        long wantedStart = ArchiveSearchPolicy.ValidatePageAndGetOffset(req.Page, req.PageSize);
        int matched = 0;
        int unknown = 0;
        var pageItems = new List<ArchiveSearchResultItem>(req.PageSize);
        var itemLookup = req.SelectedScopes
            .Select(scope => scope.ArchiveId)
            .Distinct(StringComparer.Ordinal)
            .Where(manifests.ContainsKey)
            .SelectMany(archiveId => manifests[archiveId].Items.Select(item =>
                new KeyValuePair<string, (ArchiveManifest Manifest, ArchiveManifestItem Item)>($"{archiveId}:{item.ItemId}", (manifests[archiveId], item))))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        ArchiveSearchResponse? first = null;
        for (int scanPage = 1; ; scanPage++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = _searchIndex.Search(req with { AdvancedFilter = null, Page = scanPage, PageSize = scanPageSize }, manifests);
            first ??= batch;
            foreach (var item in batch.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MailFilterMatch result = EvaluateArchiveItem(item.MessageId, manifests, compiled, cancellationToken, itemLookup);
                if (result == MailFilterMatch.UnknownMetadata) { unknown++; continue; }
                if (result != MailFilterMatch.Match) continue;
                if (matched >= wantedStart && pageItems.Count < req.PageSize) pageItems.Add(item);
                matched++;
            }
            if (scanPage * scanPageSize >= batch.TotalCount) break;
        }
        return new ArchiveSearchResponse
        {
            TotalCount = matched,
            Page = req.Page,
            PageSize = req.PageSize,
            Items = pageItems,
            IndexHealthy = first?.IndexHealthy ?? true,
            Warning = first?.Warning,
            AdvancedFilterFingerprint = compiled.Fingerprint,
            AdvancedFilterUnknownCount = unknown
        };
    }

    private MailFilterMatch EvaluateArchiveItem(string physicalId, IReadOnlyDictionary<string, ArchiveManifest> manifests, CompiledMailFilter compiled, CancellationToken cancellationToken, IReadOnlyDictionary<string, (ArchiveManifest Manifest, ArchiveManifestItem Item)>? itemLookup = null)
    {
        int separator = physicalId.IndexOf(':');
        if (separator <= 0) throw new ArchiveIntegrityException("Arşiv ileti kimliği geçersiz.");
        string archiveId = physicalId[..separator], itemId = physicalId[(separator + 1)..];
        ArchiveManifest manifest;
        ArchiveManifestItem item;
        if (itemLookup is not null)
        {
            if (!itemLookup.TryGetValue(physicalId, out var located)) throw new ArchiveIntegrityException("Arşiv öğesi bulunamadı.");
            (manifest, item) = located;
        }
        else
        {
            if (!manifests.TryGetValue(archiveId, out manifest!)) throw new ArchiveIntegrityException("Arşiv manifestosu bulunamadı.");
            item = manifest.Items.SingleOrDefault(x => x.ItemId == itemId) ?? throw new ArchiveIntegrityException("Arşiv öğesi bulunamadı.");
        }
        using var stream = _storageManager.OpenRawEmlStream(archiveId, item.RelativeEmlPath);
        if (stream.Length != item.ByteLength) throw new ArchiveIntegrityException("Arşiv ileti boyutu manifest ile uyuşmuyor.");
        if (stream.Length > ArchiveMimeParser.MaxRawMessageSizeBytes) throw new ArchiveSearchPolicyException("Gelişmiş filtre için ileti boyutu 64 MiB sınırını aşıyor; arama kapsamını daraltın.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024]; int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0) { cancellationToken.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
        string actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!actual.Equals(item.StoredSha256, StringComparison.OrdinalIgnoreCase)) throw new ArchiveIntegrityException("Arşiv ileti özeti manifest ile uyuşmuyor.");
        stream.Position = 0;
        using var message = MimeMessage.Load(stream, cancellationToken);
        return MimeAdvancedFilterAdapter.Evaluate(compiled, message, stream.Length);
    }

    public ArchiveMessagePreviewResponse GetMessagePreview(ArchiveMessagePreviewRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);
        if (string.IsNullOrWhiteSpace(req.MessageId))
            throw new ArgumentException("İleti kimliği (messageId) zorunludur.");
        ArgumentNullException.ThrowIfNull(req.SearchRequest);

        req.SearchRequest.ResolveField();

        var registered = GetRegisteredManifests();
        var match = _searchIndex.VerifyMessageInSearchScope(req.MessageId, req.SearchRequest, registered);

        if (match == null)
        {
            throw new ArchiveSearchPolicyException("İleti seçili arama kapsamında veya filtreye uygun bulunmuyor.");
        }

        var (archiveId, relativeEmlPath, itemId) = match.Value;

        if (!registered.TryGetValue(archiveId, out var manifest))
        {
            throw new FileNotFoundException("Arşiv manifestosu bulunamadı.");
        }

        var manifestItem = manifest.Items.FirstOrDefault(i =>
            string.Equals(i.ItemId, itemId, StringComparison.Ordinal) ||
            string.Equals(i.RelativeEmlPath, relativeEmlPath, StringComparison.OrdinalIgnoreCase));

        if (manifestItem == null)
        {
            throw new ArchiveIntegrityException("İleti kaydı arşiv manifestosunda bulunamadı.");
        }

        if (req.SearchRequest.AdvancedFilter is not null)
        {
            bool dateBlocked = req.SearchRequest.SelectedScopes.Any(scope => registered.TryGetValue(scope.ArchiveId, out var selectedManifest) && selectedManifest.DateFilterBlocked);
            var compiled = AdvancedMailFilter.Compile(req.SearchRequest.AdvancedFilter, dateFidelityUnresolved: dateBlocked);
            if (EvaluateArchiveItem(req.MessageId, registered, compiled, CancellationToken.None) != MailFilterMatch.Match)
                throw new ArchiveSearchPolicyException("İleti gelişmiş filtreye uygun bulunmuyor.");
        }

        using var emlStream = _storageManager.OpenRawEmlStream(archiveId, relativeEmlPath);

        // Verification after scope/filter validation but before reading/returning preview body
        if (emlStream.Length != manifestItem.ByteLength)
        {
            throw new ArchiveIntegrityException(
                $"[GÜVENLİK ENGELİ] İleti bayt uzunluğu manifest ile uyuşmuyor. Beklenen: {manifestItem.ByteLength}, Mevcut: {emlStream.Length}");
        }

        using (var sha = SHA256.Create())
        {
            byte[] computedHash = sha.ComputeHash(emlStream);
            string actualSha256 = Convert.ToHexString(computedHash).ToLowerInvariant();
            if (!string.Equals(actualSha256, manifestItem.StoredSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArchiveIntegrityException(
                    $"[GÜVENLİK ENGELİ] İleti SHA-256 hash uyuşmazlığı. Beklenen: {manifestItem.StoredSha256}, Mevcut: {actualSha256}");
            }
        }

        emlStream.Position = 0;
        var parsed = ArchiveMimeParser.Parse(emlStream, emlStream.Length);

        return new ArchiveMessagePreviewResponse
        {
            MessageId = $"{archiveId}:{itemId}",
            ArchiveId = archiveId,
            Subject = parsed.Subject,
            From = !string.IsNullOrWhiteSpace(parsed.SenderDisplay) ? $"{parsed.SenderDisplay} <{parsed.SenderAddress}>" : parsed.SenderAddress,
            To = parsed.RecipientsDisplay,
            Cc = null,
            Bcc = null,
            DateUtc = parsed.DateUtc,
            SourceInternalDateUtc = manifestItem.SourceInternalDateUtc,
            MessageIdHeader = parsed.MessageIdHeader,
            BodyText = parsed.BodyText,
            IsBodyTruncated = parsed.IsBodyTruncated,
            Attachments = parsed.Attachments,
            RawSizeBytes = parsed.RawSizeBytes,
            Sha256 = parsed.Sha256
        };
    }

    public IReadOnlyList<ArchiveCatalogItemDto> GetCatalog()
    {
        return _searchIndex.GetCatalogItems();
    }
}
