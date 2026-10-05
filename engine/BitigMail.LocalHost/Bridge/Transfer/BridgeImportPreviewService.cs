using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Bridge;
using BitigMail.Engine.Imap;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.Imap.Transfer;
using BitigMail.LocalHost.Security;
using MimeKit;

namespace BitigMail.LocalHost.Bridge.Transfer;

/// <summary>
/// Generates immutable, server-side persisted preview and plan for File -> IMAP import.
/// Operates with strict canonical byte policy, MimeKit byte equality before APPEND,
/// UTC+03:00 inclusive date filtering, and target folder keyword capability verification.
/// </summary>
public class BridgeImportPreviewService
{
    private readonly FileHandleRegistry _handleRegistry;
    private readonly ImapAccountStore _accountStore;
    private readonly IImapTransferClientFactory _clientFactory;
    private readonly BridgeTransferJournal _journal;
    private readonly IImapCredentialResolver _credentialResolver;
    private readonly TransientResourceOwnershipRegistry? _owners;
    private readonly MimeSourceInspector _inspector = new();

    public BridgeImportPreviewService(
        FileHandleRegistry handleRegistry,
        ImapAccountStore accountStore,
        IImapTransferClientFactory clientFactory,
        BridgeTransferJournal journal,
        IImapCredentialResolver credentialResolver,
        TransientResourceOwnershipRegistry? owners = null)
    {
        _handleRegistry = handleRegistry ?? throw new ArgumentNullException(nameof(handleRegistry));
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _credentialResolver = credentialResolver ?? throw new ArgumentNullException(nameof(credentialResolver));
        _owners = owners;
    }

    public virtual async Task<BridgeImportPreviewResponse> CreatePreviewAsync(
        BridgeImportPreviewRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ImapConnectionPolicy.ValidateScope(request.CompanyId, request.ProjectId);
        foreach (var label in new[] { request.CompanyName, request.ProjectName })
        {
            if (label != null && (label.Length > 200 || label.Any(char.IsControl)))
                throw new ArgumentException("Müşteri veya proje adı geçersiz.");
        }

        if (string.IsNullOrWhiteSpace(request.SourceHandle) || !request.SourceHandle.StartsWith("msrc_"))
        {
            throw new ArgumentException("Geçersiz kaynak tanıtıcısı (sourceHandle). Yalnızca msrc_* tanıtıcıları kabul edilir.");
        }

        ImapAccountStore.ValidateAccountId(request.TargetAccountId);

        var mimeEntry = _handleRegistry.GetMimeSourceEntry(request.SourceHandle);
        if (mimeEntry == null || mimeEntry.Manifest == null)
        {
            throw new ArgumentException("Kaynak dosya tanıtıcısı bulunamadı veya süresi dolmuş. Lütfen dosyayı tekrar seçin.");
        }

        var manifest = mimeEntry.Manifest;

        // Revalidate source manifest integrity before processing
        _inspector.RevalidateManifest(manifest);
        var qualification = new NormalizedSourceQualificationReader().Read(manifest, ct);
        var advanced = request.AdvancedFilter is null ? null : AdvancedMailFilter.Compile(request.AdvancedFilter, dateFidelityUnresolved: qualification?.DateFilterBlocked == true);

        if (string.Equals(manifest.SourceKind, "mbox", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(manifest.Dialect, "mboxrd", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(manifest.RootPath))
            {
                BridgeMboxrdValidator.ValidateStrictMboxrd(manifest.RootPath);
            }
        }

        // "Boş seçimi tümü sayma." - fail-closed on empty selection
        if (request.SelectedFolders == null || request.SelectedFolders.Count == 0)
        {
            throw new ArgumentException("En az bir kaynak klasör seçilmelidir. Boş klasör seçimi geçerli değildir.");
        }

        var selectedFolderSet = new HashSet<string>(request.SelectedFolders, StringComparer.OrdinalIgnoreCase);

        // Retrieve target account and credentials
        await using var targetResolved = await _credentialResolver.ResolveCredentialAsync(
            request.TargetAccountId, request.CompanyId, request.ProjectId, ct);
        var targetRecord = targetResolved.Account;

        // Connect to target IMAP server and verify keyword capabilities
        using var targetClient = _clientFactory.CreateClient();
        await targetClient.ConnectAndAuthenticateAsync(
            targetRecord.Host,
            targetRecord.Port,
            targetRecord.TlsMode,
            targetResolved.Credential,
            ct);

        // Parse date filter: UTC+03:00 inclusive day bounds
        OstSelectionEngine.ParseDateBoundaries(request.StartDate, request.EndDate, out var startUtc, out var endExclusiveUtc);
        bool hasDateFilter = startUtc.HasValue || endExclusiveUtc.HasValue;

        string previewId = "bprev_" + Guid.NewGuid().ToString("N");
        DateTimeOffset createdAtUtc = DateTimeOffset.UtcNow;

        var plan = new BridgeImportPlan
        {
            AdvancedFilterCanonicalJson = advanced?.CanonicalJson,
            AdvancedFilterFingerprint = advanced?.Fingerprint,
            PlanId = previewId,
            PreviewId = previewId,
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            CompanyName = request.CompanyName,
            ProjectName = request.ProjectName,
            SourceHandle = request.SourceHandle,
            SourceFingerprint = manifest.AggregateFingerprint,
            SourceKind = manifest.SourceKind,
            SourceRootPath = manifest.RootPath,
            SourceDisplayName = mimeEntry.DisplayPath,
            TargetAccountId = request.TargetAccountId,
            TargetAccountVersion = targetRecord.Version,
            TargetDisplayName = targetRecord.DisplayName,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            CreatedAtUtc = createdAtUtc,
            QualificationFingerprint = qualification?.Fingerprint,
            DateFilterBlocked = qualification?.DateFilterBlocked ?? false,
            QualificationIsPartial = qualification?.IsPartial ?? false,
            QualificationWarnings = qualification?.Warnings.ToList() ?? new(),
            QualificationSourcePaths = manifest.Entries.Select(x => x.CanonicalPath).ToList()
        };

        bool canTransfer = true;
        string? blockerReason = null;
        if (hasDateFilter && plan.DateFilterBlocked)
        {
            canTransfer = false;
            blockerReason = "[TARİH FİLTRESİ ENGELİ] Kaynak dönüşümünün özgün tarih sadakati doğrulanmadı; tarih filtresi kullanılamaz.";
        }

        // Group manifest entries by MappedFolder
        var folderGrouped = manifest.Entries
            .GroupBy(e => e.MappedFolder, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Check target folder keyword capability for each selected folder
        var folderSummaries = new List<BridgeFolderSummary>();
        var targetFolderMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceFolder in selectedFolderSet)
        {
            if (!folderGrouped.ContainsKey(sourceFolder))
            {
                throw new ArgumentException($"Seçilen '{sourceFolder}' klasörü kaynak arşivinde bulunamadı.");
            }

            string targetFolder = sourceFolder;
            if (request.TargetFolderMappings != null &&
                request.TargetFolderMappings.TryGetValue(sourceFolder, out string? customTarget) &&
                !string.IsNullOrWhiteSpace(customTarget))
            {
                targetFolder = customTarget.Trim();
            }

            ImapConnectionPolicy.ValidateFolderPath(targetFolder);
            targetFolderMap[sourceFolder] = targetFolder;

            bool supportsKeywords = await targetClient.SupportsUserKeywordsAsync(targetFolder, ct);
            if (!supportsKeywords)
            {
                canTransfer = false;
                blockerReason = $"[ÖN KONTROL ENGELİ] Hedef klasör '{targetFolder}' kalıcı özel anahtar kelime (PERMANENTFLAGS \\*) desteğine sahip değildir. Güvenli aktarım yürütülemez.";
            }
        }

        int totalSourceItems = manifest.Entries.Count;
        int eligibleItemsCount = 0;
        int excludedCount = 0;
        int missingDateExcludedCount = 0;
        int itemOrdinal = 0;

        foreach (var (folderName, entries) in folderGrouped)
        {
            bool isFolderSelected = selectedFolderSet.Contains(folderName);
            int folderTotal = entries.Count;
            int folderEligible = 0;
            int folderExcluded = 0;
            int folderMissingDate = 0;

            string targetFolder = targetFolderMap.TryGetValue(folderName, out var tf) ? tf : folderName;

            if (manifest.SourceKind == "mbox")
            {
                using var fs = new FileStream(manifest.RootPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                int recordIdx = 0;
                foreach (var record in MboxrdRecordReader.EnumerateRecords(fs))
                {
                    ct.ThrowIfCancellationRequested();
                    recordIdx++;
                    var entry = entries[recordIdx - 1];

                    if (!isFolderSelected)
                    {
                        excludedCount++;
                        folderExcluded++;
                        continue;
                    }

                    itemOrdinal++;
                    byte[] rawBytes = record.RawMimeBytes;
                    ProcessMimeBytes(
                        rawBytes,
                        advanced,
                        entry.RelativePath,
                        entry.CanonicalPath,
                        entry.PhysicalOrdinal,
                        entry.MappedFolder,
                        targetFolder,
                        hasDateFilter,
                        startUtc,
                        endExclusiveUtc,
                        createdAtUtc,
                        plan,
                        ref eligibleItemsCount,
                        ref excludedCount,
                        ref missingDateExcludedCount,
                        ref folderEligible,
                        ref folderExcluded,
                        ref folderMissingDate,
                        ref canTransfer,
                        ref blockerReason);
                }
            }
            else
            {
                foreach (var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!isFolderSelected)
                    {
                        excludedCount++;
                        folderExcluded++;
                        continue;
                    }

                    itemOrdinal++;
                    byte[] rawBytes = File.ReadAllBytes(entry.CanonicalPath);
                    ProcessMimeBytes(
                        rawBytes,
                        advanced,
                        entry.RelativePath,
                        entry.CanonicalPath,
                        entry.PhysicalOrdinal,
                        entry.MappedFolder,
                        targetFolder,
                        hasDateFilter,
                        startUtc,
                        endExclusiveUtc,
                        createdAtUtc,
                        plan,
                        ref eligibleItemsCount,
                        ref excludedCount,
                        ref missingDateExcludedCount,
                        ref folderEligible,
                        ref folderExcluded,
                        ref folderMissingDate,
                        ref canTransfer,
                        ref blockerReason);
                }
            }

            folderSummaries.Add(new BridgeFolderSummary
            {
                SourceFolder = folderName,
                TargetFolder = targetFolder,
                TotalItems = folderTotal,
                EligibleItems = folderEligible,
                ExcludedCount = folderExcluded,
                MissingDateExcludedCount = folderMissingDate
            });
        }

        if (eligibleItemsCount == 0 && canTransfer)
        {
            canTransfer = false;
            blockerReason = "Seçilen kriterlere uygun hiçbir ileti bulunamadı.";
        }

        plan.CanTransfer = canTransfer;
        plan.BlockerReason = blockerReason;

        var response = new BridgeImportPreviewResponse
        {
            PreviewId = previewId,
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            SourceHandle = request.SourceHandle,
            SourceFingerprint = manifest.AggregateFingerprint,
            SourceKind = manifest.SourceKind,
            TargetAccountId = request.TargetAccountId,
            TargetAccountVersion = targetRecord.Version,
            TotalSourceItems = totalSourceItems,
            EligibleItemsCount = eligibleItemsCount,
            ExcludedCount = excludedCount,
            AdvancedFilterFingerprint = plan.AdvancedFilterFingerprint,
            AdvancedFilterUnknownCount = plan.AdvancedFilterUnknownCount,
            MissingDateExcludedCount = missingDateExcludedCount,
            Folders = folderSummaries,
            CreatedAtUtc = createdAtUtc,
            CanTransfer = canTransfer,
            BlockerReason = blockerReason,
            DateFilterBlocked = plan.DateFilterBlocked,
            QualificationWarnings = plan.QualificationWarnings
        };

        plan.Preview = response;

        // Persist immutable plan to durable storage
        _journal.SaveImportPlan(plan);
        _owners?.Bind(plan.PreviewId);

        return response;
    }

    private static void ProcessMimeBytes(
        byte[] rawBytes,
        CompiledMailFilter? advanced,
        string relativePath,
        string canonicalPath,
        int physicalOrdinal,
        string sourceMappedFolder,
        string targetFolder,
        bool hasDateFilter,
        DateTime? startUtc,
        DateTime? endExclusiveUtc,
        DateTimeOffset createdAtUtc,
        BridgeImportPlan plan,
        ref int eligibleItemsCount,
        ref int excludedCount,
        ref int missingDateExcludedCount,
        ref int folderEligible,
        ref int folderExcluded,
        ref int folderMissingDate,
        ref bool canTransfer,
        ref string? blockerReason)
    {
        // 1. Strict byte canonicalization
        BridgeCanonicalMime canonical;
        try
        {
            canonical = BridgeMimeBytePolicy.CanonicalizeForImap(rawBytes);
        }
        catch (InvalidDataException ex)
        {
            canTransfer = false;
            blockerReason = $"[VERİ BÜTÜNLÜĞÜ ENGELİ] '{relativePath}' iletisi aktarılamaz: {ex.Message}";
            return;
        }

        // 2. Parse MIME to extract Date and verify serialization byte equality
        using var ms = new MemoryStream(canonical.Bytes);
        using var message = MimeMessage.Load(ms);

        if (advanced is not null)
        {
            var match = MimeAdvancedFilterAdapter.Evaluate(advanced, message, rawBytes.LongLength);
            if (match != MailFilterMatch.Match)
            {
                if (match == MailFilterMatch.UnknownMetadata) plan.AdvancedFilterUnknownCount++;
                excludedCount++; folderExcluded++; return;
            }
        }

        byte[] reserialized = ImapSerializationAssumptions.Serialize(message);
        if (!reserialized.SequenceEqual(canonical.Bytes))
        {
            canTransfer = false;
            blockerReason = $"[VERİ BÜTÜNLÜĞÜ ENGELİ] '{relativePath}' MIME yeniden serileştirme çıktısı kanonik baytlar ile birebir eşleşmiyor. Yazma engellendi.";
            return;
        }

        DateTimeOffset? originalMimeDate = null;
        var dateHeader = message.Headers[HeaderId.Date];
        if (MimeSourceInspector.HasExplicitTimeZone(dateHeader) &&
            MimeKit.Utils.DateUtils.TryParse(dateHeader, out var parsedDto))
        {
            originalMimeDate = parsedDto.ToUniversalTime();
        }

        // 3. Date filtering
        DateTimeOffset plannedInternalDate;
        if (hasDateFilter)
        {
            if (!originalMimeDate.HasValue)
            {
                missingDateExcludedCount++;
                excludedCount++;
                folderExcluded++;
                folderMissingDate++;
                return;
            }

            if (startUtc.HasValue && originalMimeDate.Value < startUtc.Value)
            {
                excludedCount++;
                folderExcluded++;
                return;
            }

            if (endExclusiveUtc.HasValue && originalMimeDate.Value >= endExclusiveUtc.Value)
            {
                excludedCount++;
                folderExcluded++;
                return;
            }

            plannedInternalDate = originalMimeDate.Value;
        }
        else
        {
            plannedInternalDate = originalMimeDate ?? createdAtUtc;
        }

        eligibleItemsCount++;
        folderEligible++;

        string itemId = "imp_" + physicalOrdinal.ToString("D6") + "_" + canonical.OriginalSha256[..8];

        plan.Items.Add(new BridgeImportPlannedItem
        {
            ItemId = itemId,
            SourceRelativePath = relativePath,
            SourceCanonicalPath = canonicalPath,
            PhysicalOrdinal = physicalOrdinal,
            SourceMappedFolder = sourceMappedFolder,
            TargetFolder = targetFolder,
            SourceSha256 = canonical.OriginalSha256,
            CanonicalSha256 = canonical.CanonicalSha256,
            ConvertedLfToCrLf = canonical.ConvertedLfToCrLf,
            AddedTerminalNewline = canonical.AddedTerminalNewline,
            OriginalMimeDateUtc = originalMimeDate,
            PlannedInternalDateUtc = plannedInternalDate,
            PlannedFlags = new List<string>(),
            PlannedKeywords = new List<string>()
        });
    }

    public virtual BridgeImportPreviewResponse? GetPreviewResponse(string previewId, string companyId, string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previewId);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        var plan = _journal.GetImportPlan(previewId);
        if (plan == null) return null;

        if (!string.Equals(plan.CompanyId, companyId, StringComparison.Ordinal) ||
            !string.Equals(plan.ProjectId, projectId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Müşteri veya proje kapsamı plan ile uyuşmuyor.");
        }

        return plan.Preview;
    }
}
