using BitigMail.Engine.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Imap;
using BitigMail.Engine.Imap.Transfer;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Security;

namespace BitigMail.LocalHost.Imap.Transfer;

/// <summary>
/// Service responsible for generating server-side immutable IMAP transfer previews and plans.
/// Operates strictly in read-only EXAMINE mode, evaluates date filters against original MIME Date
/// with explicit timezones (UTC+03 inclusive), and verifies MailKit serialization byte equality.
/// </summary>
public sealed class ImapTransferPreviewService
{
    private readonly ImapAccountStore _accountStore;
    private readonly IImapTransferClientFactory _clientFactory;
    private readonly ImapTransferJournal _journal;
    private readonly IImapCredentialResolver _credentialResolver;
    private readonly TransientResourceOwnershipRegistry? _owners;

    public ImapTransferPreviewService(
        ImapAccountStore accountStore,
        IImapTransferClientFactory clientFactory,
        ImapTransferJournal journal,
        IImapCredentialResolver credentialResolver,
        TransientResourceOwnershipRegistry? owners = null)
    {
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _credentialResolver = credentialResolver ?? throw new ArgumentNullException(nameof(credentialResolver));
        _owners = owners;
    }

    public async Task<ImapTransferPreviewResponse> CreatePreviewAsync(
        ImapTransferPreviewRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ImapConnectionPolicy.ValidateScope(request.CompanyId, request.ProjectId);
        foreach (var label in new[] { request.CompanyName, request.ProjectName })
            if (label != null && (label.Length > 200 || label.Any(char.IsControl))) throw new ArgumentException("Müşteri veya proje adı geçersiz.");
        ImapAccountStore.ValidateAccountId(request.SourceAccountId);
        ImapAccountStore.ValidateAccountId(request.TargetAccountId);

        if (string.Equals(request.SourceAccountId, request.TargetAccountId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Kaynak ve hedef hesap aynı olamaz.");
        }

        if (request.SelectedFolders == null || request.SelectedFolders.Count == 0)
        {
            throw new ArgumentException("En az bir klasör seçilmelidir. Boş klasör listesi geçersizdir.");
        }

        // Validate folder paths
        foreach (var mapping in request.SelectedFolders)
        {
            if (string.IsNullOrWhiteSpace(mapping.SourceFolderPath) || string.IsNullOrWhiteSpace(mapping.TargetFolderPath))
            {
                throw new ArgumentException("Klasör eşlemesinde kaynak ve hedef klasör adları boş olamaz.");
            }
            ImapConnectionPolicy.ValidateFolderPath(mapping.SourceFolderPath);
            ImapConnectionPolicy.ValidateFolderPath(mapping.TargetFolderPath);
        }

        var advanced = request.AdvancedFilter is null ? null : AdvancedMailFilter.Compile(request.AdvancedFilter);

        // Retrieve immutable scoped credentials for source and target accounts
        await using var sourceResolved = await _credentialResolver.ResolveCredentialAsync(
            request.SourceAccountId, request.CompanyId, request.ProjectId, ct);
        await using var targetResolved = await _credentialResolver.ResolveCredentialAsync(
            request.TargetAccountId, request.CompanyId, request.ProjectId, ct);

        var sourceRecord = sourceResolved.Account;
        var targetRecord = targetResolved.Account;

        // Parse UTC+03:00 inclusive day bounds
        OstSelectionEngine.ParseDateBoundaries(request.StartDate, request.EndDate, out var startUtc, out var endExclusiveUtc);
        bool hasDateFilter = startUtc.HasValue || endExclusiveUtc.HasValue;

        string previewId = "iprev_" + Guid.NewGuid().ToString("N");
        var plan = new ImapTransferPlan
        {
            AdvancedFilterCanonicalJson = advanced?.CanonicalJson,
            AdvancedFilterFingerprint = advanced?.Fingerprint,
            PlanId = previewId,
            PreviewId = previewId,
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            CompanyName = request.CompanyName,
            ProjectName = request.ProjectName,
            SourceDisplayName = sourceRecord.DisplayName,
            TargetDisplayName = targetRecord.DisplayName,
            SourceAccountId = request.SourceAccountId,
            SourceAccountVersion = sourceRecord.Version,
            TargetAccountId = request.TargetAccountId,
            TargetAccountVersion = targetRecord.Version,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        int totalSourceItems = 0;
        int eligibleItemsCount = 0;
        int excludedCount = 0;
        int missingDateExcludedCount = 0;
        int deletedExcludedCount = 0;
        bool canTransfer = true;
        string? blockerReason = null;

        var folderSummaries = new List<ImapTransferFolderSummary>();

        using var client = _clientFactory.CreateClient();
        using var targetClient = _clientFactory.CreateClient();
        await client.ConnectAndAuthenticateAsync(
            sourceRecord.Host,
            sourceRecord.Port,
            sourceRecord.TlsMode,
            sourceResolved.Credential,
            ct);
        await targetClient.ConnectAndAuthenticateAsync(
            targetRecord.Host,
            targetRecord.Port,
            targetRecord.TlsMode,
            targetResolved.Credential,
            ct);

        try
        {
            int itemOrdinal = 0;

            foreach (var mapping in request.SelectedFolders)
            {
                ct.ThrowIfCancellationRequested();

                bool supportsKeywords = await targetClient.SupportsUserKeywordsAsync(mapping.TargetFolderPath, ct);
                if (!supportsKeywords)
                {
                    canTransfer = false;
                    blockerReason = $"[ÖN KONTROL ENGELİ] Hedef klasör '{mapping.TargetFolderPath}' kalıcı özel anahtar kelime desteğine sahip değildir. Güvenli aktarım yürütülemez.";
                }

                var messages = await client.FetchHeaderSummariesAsync(mapping.SourceFolderPath, ct);
                uint validity = await client.GetFolderUidValidityAsync(mapping.SourceFolderPath, ct);

                int folderTotal = messages.Count;
                int folderEligible = 0;
                int folderExcluded = 0;

                totalSourceItems += folderTotal;

                foreach (var header in messages)
                {
                    itemOrdinal++;

                    // Check for Deleted flag: Deleted items block the transfer fail-closed
                    if (header.Flags.HasFlag(MailKit.MessageFlags.Deleted))
                    {
                        deletedExcludedCount++;
                        excludedCount++;
                        folderExcluded++;
                        canTransfer = false;
                        blockerReason = $"[ÖN KONTROL ENGELİ] '{mapping.SourceFolderPath}' klasöründe silindi (\\Deleted) olarak işaretlenmiş ileti (UID: {header.SourceUid}) bulunmaktadır. Veri kaybını ve yanlış silinmeyi önlemek için silinmiş iletiler aktarılamaz.";
                        continue;
                    }

                    // Check date filtering: strictly original MIME Date with explicit timezone
                    if (hasDateFilter)
                    {
                        if (!header.OriginalMimeDateUtc.HasValue)
                        {
                            missingDateExcludedCount++;
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

                    var msg = await client.FetchSingleSourceMessageAsync(mapping.SourceFolderPath, header.SourceUid, ct);
                    if (msg == null)
                    {
                        canTransfer = false;
                        blockerReason = $"[BÜTÜNLÜK ENGELİ] '{mapping.SourceFolderPath}' klasöründeki ileti (UID: {header.SourceUid}) okunamadı.";
                        break;
                    }
                    using var fetchedMessage = msg.Message;

                    if (advanced is not null)
                    {
                        var match = MimeAdvancedFilterAdapter.Evaluate(advanced, fetchedMessage, msg.RawBytes.LongLength);
                        if (match != MailFilterMatch.Match)
                        {
                            if (match == MailFilterMatch.UnknownMetadata) plan.AdvancedFilterUnknownCount++;
                            excludedCount++; folderExcluded++; continue;
                        }
                    }

                    // Gate A Serialization byte equality check: compare raw bytes with MailKit format options output
                    byte[] serialized = ImapSerializationAssumptions.Serialize(msg.Message);
                    if (!msg.RawBytes.SequenceEqual(serialized))
                    {
                        canTransfer = false;
                        blockerReason = $"[BÜTÜNLÜK ENGELİ] '{mapping.SourceFolderPath}' klasöründeki ileti (UID: {msg.SourceUid}) MailKit yeniden yazımında kaynak baytları ile birebir uyuşmuyor. Bayt eşitliği sağlanamadığından aktarım engellendi.";
                    }

                    folderEligible++;
                    eligibleItemsCount++;

                    // Check unsupported flags
                    var unsupportedFlags = msg.Flags & ~(MailKit.MessageFlags.Seen | MailKit.MessageFlags.Answered | MailKit.MessageFlags.Flagged | MailKit.MessageFlags.Draft | MailKit.MessageFlags.Recent | MailKit.MessageFlags.Deleted);
                    if (unsupportedFlags != 0)
                    {
                        canTransfer = false;
                        blockerReason = $"[ÖN KONTROL ENGELİ] '{mapping.SourceFolderPath}' klasöründeki ileti (UID: {msg.SourceUid}) desteklenmeyen bayraklar ({unsupportedFlags}) içermektedir.";
                    }

                    // Convert flags to strings, strictly excluding Recent
                    var flagsList = new List<string>();
                    if (msg.Flags.HasFlag(MailKit.MessageFlags.Seen)) flagsList.Add("\\Seen");
                    if (msg.Flags.HasFlag(MailKit.MessageFlags.Answered)) flagsList.Add("\\Answered");
                    if (msg.Flags.HasFlag(MailKit.MessageFlags.Flagged)) flagsList.Add("\\Flagged");
                    if (msg.Flags.HasFlag(MailKit.MessageFlags.Draft)) flagsList.Add("\\Draft");

                    var plannedItem = new ImapTransferPlannedItem
                    {
                        ItemId = $"item-{itemOrdinal:D6}",
                        SourceFolder = mapping.SourceFolderPath,
                        SourceDelimiter = "/",
                        SourceUid = msg.SourceUid,
                        SourceUidValidity = validity,
                        SourceSha256 = msg.RawSha256,
                        OriginalMimeDateUtc = msg.OriginalMimeDateUtc,
                        InternalDateUtc = msg.InternalDateUtc,
                        Flags = flagsList,
                        Keywords = msg.Keywords,
                        TargetFolder = mapping.TargetFolderPath
                    };

                    plan.Items.Add(plannedItem);
                }

                folderSummaries.Add(new ImapTransferFolderSummary
                {
                    SourceFolder = mapping.SourceFolderPath,
                    TargetFolder = mapping.TargetFolderPath,
                    SourceUidValidity = validity,
                    TotalItems = folderTotal,
                    EligibleItems = folderEligible,
                    ExcludedCount = folderExcluded
                });
            }

            if (canTransfer && eligibleItemsCount == 0)
            {
                canTransfer = false;
                blockerReason = "Seçim kriterlerinize veya tarih filtresine uyan hiçbir ileti bulunamadı. Sıfır iletili aktarım başlatılamaz.";
            }

            plan.CanTransfer = canTransfer;
            plan.BlockerReason = blockerReason;

            var response = new ImapTransferPreviewResponse
            {
                PreviewId = previewId,
                CompanyId = request.CompanyId,
                ProjectId = request.ProjectId,
                SourceAccountId = request.SourceAccountId,
                SourceAccountVersion = sourceRecord.Version,
                TargetAccountId = request.TargetAccountId,
                TargetAccountVersion = targetRecord.Version,
                TotalSourceItems = totalSourceItems,
                EligibleItemsCount = eligibleItemsCount,
                ExcludedCount = excludedCount,
                AdvancedFilterFingerprint = plan.AdvancedFilterFingerprint,
                AdvancedFilterUnknownCount = plan.AdvancedFilterUnknownCount,
                MissingDateExcludedCount = missingDateExcludedCount,
                DeletedExcludedCount = deletedExcludedCount,
                Folders = folderSummaries,
                CreatedAtUtc = plan.CreatedAtUtc,
                CanTransfer = canTransfer,
                BlockerReason = blockerReason
            };
            plan.Preview = response;
            if (sourceResolved.CommitAsync != null) await sourceResolved.CommitAsync();
            if (targetResolved.CommitAsync != null) await targetResolved.CommitAsync();
            _journal.SavePlan(plan);
            _owners?.Bind(plan.PreviewId);
            return response;
        }
        finally
        {
            await client.DisconnectAsync(ct);
            await targetClient.DisconnectAsync(ct);
        }
    }

    public ImapTransferPreviewResponse? GetPreviewResponse(string previewId, string companyId, string projectId)
    {
        var plan = _journal.GetPlan(previewId);
        if (plan == null) return null;

        if (!string.Equals(plan.CompanyId, companyId, StringComparison.Ordinal) ||
            !string.Equals(plan.ProjectId, projectId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Müşteri veya proje kapsamı bu önizleme ile uyuşmuyor.");
        }

        if (plan.Preview != null) return plan.Preview;
        var folderGrouped = plan.Items.GroupBy(i => (i.SourceFolder, i.TargetFolder)).ToList();
        var summaries = folderGrouped.Select(g => new ImapTransferFolderSummary
        {
            SourceFolder = g.Key.SourceFolder,
            TargetFolder = g.Key.TargetFolder,
            SourceUidValidity = g.First().SourceUidValidity,
            TotalItems = g.Count(),
            EligibleItems = g.Count(),
            ExcludedCount = 0
        }).ToList();

        return new ImapTransferPreviewResponse
        {
            PreviewId = plan.PreviewId,
            CompanyId = plan.CompanyId,
            ProjectId = plan.ProjectId,
            SourceAccountId = plan.SourceAccountId,
            SourceAccountVersion = plan.SourceAccountVersion,
            TargetAccountId = plan.TargetAccountId,
            TargetAccountVersion = plan.TargetAccountVersion,
            TotalSourceItems = plan.Items.Count,
            EligibleItemsCount = plan.Items.Count,
            ExcludedCount = 0,
            AdvancedFilterFingerprint = plan.AdvancedFilterFingerprint,
            AdvancedFilterUnknownCount = plan.AdvancedFilterUnknownCount,
            MissingDateExcludedCount = 0,
            DeletedExcludedCount = 0,
            Folders = summaries,
            CreatedAtUtc = plan.CreatedAtUtc,
            CanTransfer = plan.CanTransfer,
            BlockerReason = plan.BlockerReason
        };
    }
}
