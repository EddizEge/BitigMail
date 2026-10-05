using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using BitigMail.Engine.Archive;
using BitigMail.Engine.Models;
using BitigMail.Engine.Planning;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Security;
using Microsoft.AspNetCore.Http;

namespace BitigMail.LocalHost.Archive;

public sealed record ArchiveSelectedPlan(string PlanId, string OwnerFingerprint, string SourceMetadataFingerprint, ArchiveSearchRequest SearchRequest, FrozenArchiveSearchSelection Frozen, IReadOnlyList<FolderMappingRule> FolderMappings, string MappingFingerprint, DuplicatePolicy DuplicatePolicy, DateTimeOffset CreatedAt,string? ActorUserId=null,string? ActorSessionId=null,long? ActorSecurityVersion=null);

public sealed class ArchiveSelectedJobService
{
    private readonly ArchiveCatalogService _catalog;
    private readonly JobManager _jobs;
    private readonly IHttpContextAccessor? _http;
    private readonly IdentityCatalog? _identities;
    private readonly TransientResourceOwnershipRegistry? _owners;
    private readonly ConcurrentDictionary<string, ArchiveSelectedPlan> _plans = new();

    public ArchiveSelectedJobService(ArchiveCatalogService catalog, JobManager jobs,IHttpContextAccessor? http=null,IdentityCatalog? identities=null,TransientResourceOwnershipRegistry? owners=null) { _catalog = catalog; _jobs = jobs;_http=http;_identities=identities;_owners=owners; }

    public ArchiveSelectedPlan Preview(ArchiveSearchRequest search, IReadOnlyList<string> selectedIds, IReadOnlyList<FolderMappingRule> mappings, DuplicatePolicy duplicatePolicy, string companyId, string projectId)
    {
        if (_http?.HttpContext is { } context && _identities is not null &&
            !ArchiveScopeAuthorization.Allows(context, _identities, search.SelectedScopes)) throw new KeyNotFoundException("Arşiv bulunamadı.");
        if (selectedIds.Count is < 1 or > 10_000 || selectedIds.Distinct(StringComparer.Ordinal).Count() != selectedIds.Count) throw new InvalidDataException("Seçili arama öğeleri geçersiz.");
        string owner = Hash(companyId + "\n" + projectId);
        var items = new List<ArchiveSearchSelectionItem>();
        var folders = new HashSet<string>(StringComparer.Ordinal);
        var manifests = _catalog.GetRegisteredManifests();
        foreach (string id in selectedIds)
        {
            var preview = _catalog.GetMessagePreview(new() { MessageId = id, SearchRequest = search });
            var manifest = manifests[preview.ArchiveId];
            if (manifest.CompanyId != companyId || manifest.ProjectId != projectId) throw new InvalidDataException("Seçili ileti sahiplik kapsamı uyuşmuyor.");
            items.Add(new(id, preview.Sha256));
            var manifestItem = manifest.Items.Single(x => id.EndsWith(":" + x.ItemId, StringComparison.Ordinal));
            folders.Add(MappingKey(manifest.ArchiveId, manifestItem.OriginalFolder));
        }
        var mapping = FolderMappingPolicy.Validate(mappings);
        if (folders.Any(x => !mapping.Rules.ContainsKey(x))) throw new InvalidDataException("Her seçili kaynak klasörü açıkça eşlenmelidir.");
        var frozenSearch = JsonSerializer.Deserialize<ArchiveSearchRequest>(JsonSerializer.Serialize(search)) ?? throw new InvalidDataException();
        var frozenMappings = mappings.Select(x => new FolderMappingRule(x.SourceFolderId, x.TargetFolderPath)).ToArray();
        var frozen = ArchiveSearchSelectionPolicy.Freeze(owner, Hash(JsonSerializer.Serialize(frozenSearch)), items);
        var actor=_http?.HttpContext?.Items[typeof(AuthenticatedSessionPrincipal)] as AuthenticatedSessionPrincipal;
        if(actor is not null&&_identities is not null&&search.SelectedScopes.Any(scope=>!_identities.CanAccess(actor,scope.CompanyId,scope.ProjectId)))throw new UnauthorizedAccessException();
        var plan = new ArchiveSelectedPlan("aselp_" + Guid.NewGuid().ToString("N")[..16], owner, SourceMetadataFingerprint(items.Select(x => x.PhysicalItemId), manifests), frozenSearch, frozen, Array.AsReadOnly(frozenMappings), mapping.Fingerprint, duplicatePolicy, DateTimeOffset.UtcNow,actor?.UserId,actor?.SessionId,actor?.SecurityVersion);
        _plans[plan.PlanId] = plan;
        _owners?.Bind(plan.PlanId);
        return plan;
    }

    public LocalJobRecord Start(string planId, string outputDirectory, ClientProjectContext owner)
    {
        if (!_plans.TryGetValue(planId, out var plan)) throw new KeyNotFoundException();
        var actor=_http?.HttpContext?.Items[typeof(AuthenticatedSessionPrincipal)] as AuthenticatedSessionPrincipal;if(plan.ActorUserId is not null&&(actor is null||actor.UserId!=plan.ActorUserId||actor.SessionId!=plan.ActorSessionId||actor.SecurityVersion!=plan.ActorSecurityVersion))throw new KeyNotFoundException();if(actor is not null&&_identities is not null&&!_identities.CanAccess(actor,owner.CompanyId,owner.ProjectId))throw new KeyNotFoundException();
        ValidateFrozenPlan(plan);
        ArchiveSearchSelectionPolicy.Revalidate(plan.Frozen, Hash(owner.CompanyId + "\n" + owner.ProjectId), CurrentHashes(plan));
        if (FolderMappingPolicy.Validate(plan.FolderMappings).Fingerprint != plan.MappingFingerprint) throw new InvalidDataException("Klasör eşlemesi değişti.");
        var manifests=_catalog.GetRegisteredManifests();
        var requiredScopes=plan.Frozen.Items.Select(item=>manifests[item.PhysicalItemId.Split(':',2)[0]]).Select(manifest=>new JobAuthorizationScope(manifest.CompanyId,manifest.ProjectId)).Distinct().ToArray();
        if(actor is not null&&_identities is not null&&requiredScopes.Any(scope=>!_identities.CanAccess(actor,scope.CompanyId,scope.ProjectId)))throw new KeyNotFoundException();
        return _jobs.ScheduleStage7Job("archive-selected-export", "Seçili arşiv sonuçları", "Doğrulanmış EML klasörü", owner, record => Execute(record, plan, Path.GetFullPath(outputDirectory)),requiredScopes:requiredScopes);
    }

    private Dictionary<string, string> CurrentHashes(ArchiveSelectedPlan plan)
    {
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in plan.Frozen.Items) current[item.PhysicalItemId] = _catalog.GetMessagePreview(new() { MessageId = item.PhysicalItemId, SearchRequest = plan.SearchRequest }).Sha256;
        return current;
    }

    private async Task Execute(LocalJobRecord record, ArchiveSelectedPlan plan, string outputRoot)
    {
        string staging = Path.Combine(outputRoot, $".stage-{record.JobId}-{Guid.NewGuid():N}");
        string final = Path.Combine(outputRoot, record.JobId);
        try
        {
            RejectLinks(outputRoot);
            ValidateFrozenPlan(plan);
            ArchiveSearchSelectionPolicy.Revalidate(plan.Frozen, plan.OwnerFingerprint, CurrentHashes(plan));
            var mapping = FolderMappingPolicy.Validate(plan.FolderMappings);
            if (mapping.Fingerprint != plan.MappingFingerprint) throw new InvalidDataException("Klasör eşlemesi değişti.");
            Directory.CreateDirectory(staging);
            RejectLinks(staging);
            var manifests = _catalog.GetRegisteredManifests();
            var frozenById = plan.Frozen.Items.ToDictionary(x => x.PhysicalItemId, StringComparer.Ordinal);
            var candidates = new List<(DuplicateCandidate Candidate, ArchiveManifest Manifest, ArchiveManifestItem Item)>();
            foreach (var selected in plan.Frozen.Items)
            {
                string[] parts = selected.PhysicalItemId.Split(':', 2);
                var manifest = manifests[parts[0]];
                if (Hash(manifest.CompanyId + "\n" + manifest.ProjectId) != plan.OwnerFingerprint) throw new InvalidDataException("Kuyrukta beklerken sahiplik kapsamı değişti.");
                var item = manifest.Items.Single(x => x.ItemId == parts[1]);
                if (item.StoredSha256 != selected.RawContentSha256) throw new InvalidDataException("Kuyrukta beklerken arşiv içeriği değişti.");
                string metadata = Hash(string.Join("\n", item.OriginalFolder, item.OriginalMimeDateUtc?.ToUniversalTime().ToString("O"), item.SourceInternalDateUtc?.ToUniversalTime().ToString("O"), item.EnvelopeFrom, item.MessageIdHeader));
                candidates.Add((new(selected.PhysicalItemId, selected.RawContentSha256, metadata), manifest, item));
            }
            var decision = DuplicatePolicyEvaluator.Apply(candidates.Select(x => x.Candidate), plan.DuplicatePolicy);
            var include = decision.Included.Select(x => x.PhysicalItemId).ToHashSet(StringComparer.Ordinal);
            var outputEntries = new List<object>();
            int written = 0;
            foreach (var row in candidates.Where(x => include.Contains(x.Candidate.PhysicalItemId)))
            {
                string logicalFolder = mapping.Rules[MappingKey(row.Manifest.ArchiveId, row.Item.OriginalFolder)];
                string dir = Path.Combine(staging, logicalFolder.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(dir);
                string stableName = Hash(row.Candidate.PhysicalItemId)[..16] + "-" + row.Item.ItemId + ".eml";
                string target = Path.Combine(dir, stableName);
                await using var source = _catalog.StorageManager.OpenRawEmlStream(row.Manifest.ArchiveId, row.Item.RelativeEmlPath);
                await using var dest = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await source.CopyToAsync(dest);
                await dest.FlushAsync();
                dest.Close();
                string actualHash = FileHash(target);
                if (actualHash != frozenById[row.Candidate.PhysicalItemId].RawContentSha256) throw new InvalidDataException("Yayımlanan EML özeti uyuşmuyor.");
                outputEntries.Add(new { OutputRelativePath = Path.GetRelativePath(staging, target).Replace('\\', '/'), OutputSha256 = actualHash, LogicalFolder = logicalFolder, SourcePhysicalItemId = row.Candidate.PhysicalItemId });
                written++;
            }
            var sourceManifests = candidates.Select(x => x.Manifest).DistinctBy(x => x.ArchiveId).ToArray();
            var warnings = sourceManifests.SelectMany(x => x.QualificationWarnings).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var qualificationManifest = new
            {
                SchemaVersion = 1,
                Status = "completed_with_qualification",
                ItemsWritten = written,
                DateFilterBlocked = sourceManifests.Any(x => x.DateFilterBlocked),
                IsPartial = sourceManifests.Any(x => x.QualificationIsPartial),
                QualificationWarnings = warnings,
                SourceQualificationFingerprints = sourceManifests.Select(x => x.QualificationFingerprint).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                Entries = outputEntries
            };
            await File.WriteAllTextAsync(Path.Combine(staging, "bitigmail-selected-archive-manifest.json"), JsonSerializer.Serialize(qualificationManifest, new JsonSerializerOptions { WriteIndented = true }));
            if (Directory.Exists(final) || File.Exists(final)) throw new IOException("İş çıktı klasörü zaten var.");
            Directory.Move(staging, final);
            record.Status = "completed";
            record.Stage = "Seçili arşiv çıktısı doğrulandı";
            record.ItemsRead = candidates.Count;
            record.ItemsWritten = written;
            record.TotalItems = candidates.Count;
            record.OutputDirectoryPath = final;
            record.FolderMappingFingerprint = mapping.Fingerprint;
            record.DuplicatePolicy = plan.DuplicatePolicy.ToString();
            record.SkippedDuplicateItemIds = decision.SkippedPhysicalItemIds.ToList();
            record.FrozenSelectionFingerprint = plan.Frozen.Fingerprint;
            record.DateFilterBlocked = qualificationManifest.DateFilterBlocked;
            record.QualificationWarnings = warnings.ToList();
            record.CompletedAt = DateTimeOffset.UtcNow;
            _jobs.UpdateRecovery(record);
        }
        catch (Exception)
        {
            record.Status = "failed";
            record.Stage = "Seçili arşiv işi başarısız";
            record.ErrorMessage = "Seçili arşiv çıktısı doğrulanamadı; geçici çıktı yayımlanmadı.";
            record.CompletedAt = DateTimeOffset.UtcNow;
            _jobs.UpdateRecovery(record);
        }
    }

    private static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Bağlantılı çıktı yolu kabul edilmez.");
    }

    private void ValidateFrozenPlan(ArchiveSelectedPlan plan)
    {
        if (Hash(JsonSerializer.Serialize(plan.SearchRequest)) != plan.Frozen.QueryFingerprint) throw new InvalidDataException("Dondurulmuş arama isteği değişti.");
        if (SourceMetadataFingerprint(plan.Frozen.Items.Select(x => x.PhysicalItemId), _catalog.GetRegisteredManifests()) != plan.SourceMetadataFingerprint) throw new InvalidDataException("Kaynak arşiv metadata/yeterlilik kaydı değişti.");
    }

    private static string SourceMetadataFingerprint(IEnumerable<string> physicalIds, IReadOnlyDictionary<string, ArchiveManifest> manifests)
    {
        var rows = physicalIds.OrderBy(x => x, StringComparer.Ordinal).Select(id =>
        {
            string[] parts = id.Split(':', 2); var manifest = manifests[parts[0]]; var item = manifest.Items.Single(x => x.ItemId == parts[1]);
            return new { id, manifest.CompanyId, manifest.ProjectId, manifest.QualificationFingerprint, manifest.DateFilterBlocked, manifest.QualificationIsPartial, manifest.QualificationWarnings, item.OriginalFolder, item.OriginalMimeDateUtc, item.SourceInternalDateUtc, item.MessageIdHeader, item.EnvelopeFrom, item.IsBodyTruncated };
        });
        return Hash(JsonSerializer.Serialize(rows));
    }

    private static string MappingKey(string archiveId, string originalFolder) => archiveId + ":" + originalFolder;

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static string FileHash(string path) => BitigMail.Engine.Recovery.DamagedStoreResultValidator.HashFile(path);
}
