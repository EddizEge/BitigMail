using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

namespace BitigMail.LocalHost.Bridge.Transfer;

/// <summary>
/// Generates immutable, server-side persisted preview and plan for IMAP -> File export.
/// Inspects source folders in read-only EXAMINE mode, rejects \Deleted items,
/// enforces UTC+03:00 inclusive date filtering, freezes exact SHA-256,
/// and maps server folder names to safe, collision-resistant output paths.
/// </summary>
public class BridgeExportPreviewService
{
    private readonly FileHandleRegistry _handleRegistry;
    private readonly ImapAccountStore _accountStore;
    private readonly IImapTransferClientFactory _clientFactory;
    private readonly BridgeTransferJournal _journal;
    private readonly IImapCredentialResolver _credentialResolver;
    private readonly IDiskCapacityProbe _capacityProbe;
    private readonly TransientResourceOwnershipRegistry? _owners;

    public BridgeExportPreviewService(
        FileHandleRegistry handleRegistry,
        ImapAccountStore accountStore,
        IImapTransferClientFactory clientFactory,
        BridgeTransferJournal journal,
        IImapCredentialResolver credentialResolver,
        IDiskCapacityProbe? capacityProbe = null,
        TransientResourceOwnershipRegistry? owners = null)
    {
        _handleRegistry = handleRegistry ?? throw new ArgumentNullException(nameof(handleRegistry));
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _credentialResolver = credentialResolver ?? throw new ArgumentNullException(nameof(credentialResolver));
        _capacityProbe = capacityProbe ?? new WindowsDiskCapacityProbe();
        _owners = owners;
    }

    public virtual async Task<BridgeExportPreviewResponse> CreatePreviewAsync(
        BridgeExportPreviewRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ImapConnectionPolicy.ValidateScope(request.CompanyId, request.ProjectId);
        foreach (var label in new[] { request.CompanyName, request.ProjectName })
        {
            if (label != null && (label.Length > 200 || label.Any(char.IsControl)))
                throw new ArgumentException("Müşteri veya proje adı geçersiz.");
        }

        ImapAccountStore.ValidateAccountId(request.SourceAccountId);

        if (string.IsNullOrWhiteSpace(request.TargetDirHandle) || !request.TargetDirHandle.StartsWith("dir_"))
        {
            throw new ArgumentException("Geçersiz hedef klasör tanıtıcısı (targetDirHandle). Yalnızca dir_* tanıtıcıları kabul edilir.");
        }

        string? targetDir = _handleRegistry.GetOutputDirPath(request.TargetDirHandle);
        if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
        {
            throw new ArgumentException("Hedef klasör tanıtıcısı bulunamadı veya klasör mevcut değil.");
        }

        string formatNormalized = request.TargetFormat?.Trim().ToLowerInvariant() ?? string.Empty;
        if (formatNormalized is not ("eml-tree" or "mboxrd" or "eml" or "mbox"))
        {
            throw new ArgumentException($"Desteklenmeyen hedef biçim: '{request.TargetFormat}'. İzin verilen biçimler: 'eml-tree', 'mboxrd'.");
        }
        string targetFormat = formatNormalized is "mboxrd" or "mbox" ? "mboxrd" : "eml-tree";

        // "Boş seçimi tümü sayma." - fail-closed on empty selection
        if (request.SelectedFolders == null || request.SelectedFolders.Count == 0)
        {
            throw new ArgumentException("En az bir kaynak klasör seçilmelidir. Boş klasör seçimi geçerli değildir.");
        }

        foreach (var folder in request.SelectedFolders)
        {
            ImapConnectionPolicy.ValidateFolderPath(folder);
        }

        var advanced = request.AdvancedFilter is null ? null : AdvancedMailFilter.Compile(request.AdvancedFilter);

        // Retrieve source account credentials
        await using var sourceResolved = await _credentialResolver.ResolveCredentialAsync(
            request.SourceAccountId, request.CompanyId, request.ProjectId, ct);
        var sourceRecord = sourceResolved.Account;

        // Connect to source IMAP server strictly in read-only EXAMINE mode
        using var client = _clientFactory.CreateClient();
        await client.ConnectAndAuthenticateAsync(
            sourceRecord.Host,
            sourceRecord.Port,
            sourceRecord.TlsMode,
            sourceResolved.Credential,
            ct);

        // Parse date filter: UTC+03:00 inclusive day bounds
        OstSelectionEngine.ParseDateBoundaries(request.StartDate, request.EndDate, out var startUtc, out var endExclusiveUtc);
        bool hasDateFilter = startUtc.HasValue || endExclusiveUtc.HasValue;

        string previewId = "bprev_" + Guid.NewGuid().ToString("N");
        DateTimeOffset createdAtUtc = DateTimeOffset.UtcNow;

        var plan = new BridgeExportPlan
        {
            AdvancedFilterCanonicalJson = advanced?.CanonicalJson,
            AdvancedFilterFingerprint = advanced?.Fingerprint,
            PlanId = previewId,
            PreviewId = previewId,
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            CompanyName = request.CompanyName,
            ProjectName = request.ProjectName,
            SourceAccountId = request.SourceAccountId,
            SourceAccountVersion = sourceRecord.Version,
            SourceDisplayName = sourceRecord.DisplayName,
            TargetDirHandle = request.TargetDirHandle,
            TargetDirectoryPath = targetDir,
            TargetFormat = targetFormat,
            TargetDisplayName = Path.GetFileName(targetDir),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            CreatedAtUtc = createdAtUtc
        };

        bool canTransfer = true;
        string? blockerReason = null;

        int totalSourceItems = 0;
        int eligibleItemsCount = 0;
        int excludedCount = 0;
        int missingDateExcludedCount = 0;
        int deletedExcludedCount = 0;
        int itemOrdinal = 0;
        int folderIndex = 0;
        long eligibleRawBytes = 0;

        var folderSummaries = new List<BridgeFolderSummary>();

        foreach (var folderPath in request.SelectedFolders)
        {
            ct.ThrowIfCancellationRequested();
            folderIndex++;
            string folderKey = $"fld_{folderIndex:D3}";

            plan.FolderMappings.Add(new BridgeFolderMapping
            {
                FolderKey = folderKey,
                OriginalFolder = folderPath
            });

            uint uidValidity = await client.GetFolderUidValidityAsync(folderPath, ct);
            var headerSummaries = await client.FetchHeaderSummariesAsync(folderPath, ct);

            int folderTotal = headerSummaries.Count;
            int folderEligible = 0;
            int folderExcluded = 0;
            int folderMissingDate = 0;
            int folderDeleted = 0;

            totalSourceItems += folderTotal;

            foreach (var header in headerSummaries)
            {
                ct.ThrowIfCancellationRequested();

                // Reject Deleted items in v1 fail-closed
                if (header.HasDeletedFlag)
                {
                    deletedExcludedCount++;
                    folderDeleted++;
                    excludedCount++;
                    folderExcluded++;
                    canTransfer = false;
                    blockerReason = $"[ÖN KONTROL ENGELİ] '{folderPath}' klasöründe silindi (\\Deleted) olarak işaretlenmiş ileti (UID: {header.SourceUid}) bulunmaktadır. Silinmiş iletiler dışa aktarılamaz.";
                    continue;
                }

                // Date filter check
                if (hasDateFilter)
                {
                    if (!header.OriginalMimeDateUtc.HasValue)
                    {
                        missingDateExcludedCount++;
                        folderMissingDate++;
                        excludedCount++;
                        folderExcluded++;
                        continue;
                    }

                    if (startUtc.HasValue && header.OriginalMimeDateUtc.Value < startUtc.Value)
                    {
                        excludedCount++;
                        folderExcluded++;
                        continue;
                    }

                    if (endExclusiveUtc.HasValue && header.OriginalMimeDateUtc.Value >= endExclusiveUtc.Value)
                    {
                        excludedCount++;
                        folderExcluded++;
                        continue;
                    }
                }

                // Eligible item: fetch full single message to compute exact frozen raw SHA-256
                var fullMsg = await client.FetchSingleSourceMessageAsync(folderPath, header.SourceUid, ct);
                if (fullMsg == null)
                {
                    canTransfer = false;
                    blockerReason = $"[VERİ BÜTÜNLÜĞÜ ENGELİ] '{folderPath}' klasöründeki ileti (UID: {header.SourceUid}) sunucudan okunamadı.";
                    break;
                }

                using var fetchedMessage = fullMsg.Message;
                if (advanced is not null)
                {
                    var match = MimeAdvancedFilterAdapter.Evaluate(advanced, fetchedMessage, fullMsg.RawBytes.LongLength);
                    if (match != MailFilterMatch.Match)
                    {
                        if (match == MailFilterMatch.UnknownMetadata) plan.AdvancedFilterUnknownCount++;
                        excludedCount++; folderExcluded++; continue;
                    }
                }

                itemOrdinal++;
                eligibleItemsCount++;
                folderEligible++;
                eligibleRawBytes = checked(eligibleRawBytes + fullMsg.RawBytes.LongLength);

                string itemId = $"exp_{itemOrdinal:D6}_{header.SourceUid}";
                string relativePath = targetFormat == "mboxrd"
                    ? $"{folderKey}.mbox"
                    : $"{folderKey}/msg_{header.SourceUid:D8}.eml";

                plan.Items.Add(new BridgeExportPlannedItem
                {
                    ItemId = itemId,
                    SourceFolder = folderPath,
                    SourceUid = header.SourceUid,
                    SourceUidValidity = uidValidity,
                    SourceSha256 = fullMsg.RawSha256,
                    OriginalMimeDateUtc = fullMsg.OriginalMimeDateUtc,
                    InternalDateUtc = fullMsg.InternalDateUtc,
                    Flags = fullMsg.Flags.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList(),
                    Keywords = fullMsg.Keywords.ToList(),
                    FolderKey = folderKey,
                    RelativeOutputPath = relativePath
                });
            }

            folderSummaries.Add(new BridgeFolderSummary
            {
                SourceFolder = folderPath,
                TargetFolder = folderKey,
                TotalItems = folderTotal,
                EligibleItems = folderEligible,
                ExcludedCount = folderExcluded,
                MissingDateExcludedCount = folderMissingDate,
                DeletedExcludedCount = folderDeleted
            });
        }

        if (eligibleItemsCount == 0 && canTransfer)
        {
            canTransfer = false;
            blockerReason = "Seçilen kriterlere uygun hiçbir ileti bulunamadı.";
        }

        long? estimatedRequiredBytes = null;
        long? availableFreeBytes = null;
        try
        {
            estimatedRequiredBytes = BridgeExportCapacityEstimator.Estimate(targetFormat, eligibleRawBytes, eligibleItemsCount);
            var capacity = _capacityProbe.Probe(targetDir);
            availableFreeBytes = capacity.IsAvailable ? capacity.AvailableBytes : null;
            if (canTransfer && (!capacity.IsAvailable || !capacity.AvailableBytes.HasValue))
            {
                canTransfer = false;
                blockerReason = $"[DİSK KAPASİTESİ ENGELİ] Hedef klasörün kullanılabilir disk alanı doğrulanamadı. {capacity.Error}".Trim();
            }
            else if (canTransfer && availableFreeBytes!.Value < estimatedRequiredBytes.Value)
            {
                canTransfer = false;
                blockerReason = $"[DİSK KAPASİTESİ ENGELİ] Tahmini gereken alan {estimatedRequiredBytes.Value} bayt, kullanılabilir alan {availableFreeBytes.Value} bayt. Dışa aktarım başlatılamaz.";
            }
        }
        catch (OverflowException)
        {
            if (canTransfer)
            {
                canTransfer = false;
                blockerReason = "[DİSK KAPASİTESİ ENGELİ] Tahmini gereken alan güvenli sayı aralığını aşıyor. Dışa aktarım başlatılamaz.";
            }
        }

        plan.CanTransfer = canTransfer;
        plan.BlockerReason = blockerReason;
        plan.EstimatedRequiredBytes = estimatedRequiredBytes;

        var response = new BridgeExportPreviewResponse
        {
            PreviewId = previewId,
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            SourceAccountId = request.SourceAccountId,
            SourceAccountVersion = sourceRecord.Version,
            TargetDirHandle = request.TargetDirHandle,
            TargetFormat = targetFormat,
            TotalSourceItems = totalSourceItems,
            EligibleItemsCount = eligibleItemsCount,
            ExcludedCount = excludedCount,
            AdvancedFilterFingerprint = plan.AdvancedFilterFingerprint,
            AdvancedFilterUnknownCount = plan.AdvancedFilterUnknownCount,
            MissingDateExcludedCount = missingDateExcludedCount,
            DeletedExcludedCount = deletedExcludedCount,
            Folders = folderSummaries,
            CreatedAtUtc = createdAtUtc,
            CanTransfer = canTransfer,
            BlockerReason = blockerReason,
            EstimatedRequiredBytes = estimatedRequiredBytes,
            AvailableFreeBytes = availableFreeBytes
        };

        plan.Preview = response;

        // Persist immutable export plan
        _journal.SaveExportPlan(plan);
        _owners?.Bind(plan.PreviewId);

        return response;
    }

    public virtual BridgeExportPreviewResponse? GetPreviewResponse(string previewId, string companyId, string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previewId);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        var plan = _journal.GetExportPlan(previewId);
        if (plan == null) return null;

        if (!string.Equals(plan.CompanyId, companyId, StringComparison.Ordinal) ||
            !string.Equals(plan.ProjectId, projectId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Müşteri veya proje kapsamı plan ile uyuşmuyor.");
        }

        return plan.Preview;
    }
}
