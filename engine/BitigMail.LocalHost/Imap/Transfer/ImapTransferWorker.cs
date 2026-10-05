using BitigMail.Engine.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Imap;
using BitigMail.Engine.Imap.Transfer;
using BitigMail.Engine.Models;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Security;
using MailKit;

namespace BitigMail.LocalHost.Imap.Transfer;

/// <summary>
/// Worker executing IMAP transfer with strict atomic journal transitions,
/// target keyword reconciliation, and fail-closed integrity checks.
/// </summary>
public sealed class ImapTransferWorker
{
    private readonly ImapAccountStore _accountStore;
    private readonly IImapTransferClientFactory _clientFactory;
    private readonly ImapTransferJournal _journal;
    private readonly IImapCredentialResolver? _credentialResolver;
    private readonly Action<LocalJobRecord> _saveJobRecord;
    private readonly Action<ConversionReport> _saveReport;

    public ImapTransferWorker(
        ImapAccountStore accountStore,
        IImapTransferClientFactory clientFactory,
        ImapTransferJournal journal,
        Action<LocalJobRecord> saveJobRecord,
        Action<ConversionReport> saveReport,
        IImapCredentialResolver? credentialResolver = null)
    {
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _saveJobRecord = saveJobRecord ?? throw new ArgumentNullException(nameof(saveJobRecord));
        _saveReport = saveReport ?? throw new ArgumentNullException(nameof(saveReport));
        _credentialResolver = credentialResolver;
    }

    public async Task ExecuteAsync(
        LocalJobRecord jobRecord,
        ImapTransferPlan plan,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(jobRecord);
        ArgumentNullException.ThrowIfNull(plan);

        CompiledMailFilter? advanced;
        try { advanced = ImapAdvancedFilterPolicy.ValidateFrozen(plan); }
        catch (InvalidDataException) { FailJob(jobRecord, "Kalıcı gelişmiş filtre doğrulanamadı."); return; }
        jobRecord.AdvancedFilterCanonicalJson = plan.AdvancedFilterCanonicalJson;
        jobRecord.AdvancedFilterFingerprint = plan.AdvancedFilterFingerprint;
        jobRecord.AdvancedFilterUnknownCount = plan.AdvancedFilterUnknownCount;

        bool wasStarted = jobRecord.StartedAt != null || jobRecord.Status == "interrupted";

        jobRecord.Status = "converting";
        jobRecord.Stage = "Aktarım Başlatılıyor";
        jobRecord.ProgressPhase = "transferring";
        jobRecord.PhaseCompleted = null;
        jobRecord.PhaseTotal = null;
        jobRecord.StartedAt = DateTimeOffset.UtcNow;
        _saveJobRecord(jobRecord);

        // An initial authentication interruption must still have a durable, empty journal to resume.
        var initialJournal = _journal.GetJournal(jobRecord.JobId);
        if (initialJournal == null)
        {
            if (wasStarted)
            {
                FailJob(jobRecord, "Aktarım günlüğü bulunamadı veya hasarlı. Kesintiye uğrayan iş devam ettirilemez.");
                return;
            }
            _journal.InitializeJournal(jobRecord.JobId, plan);
        }
        else if (!string.Equals(initialJournal.PlanId, plan.PlanId, StringComparison.Ordinal))
        {
            FailJob(jobRecord, "Aktarım günlüğündeki plan bilgisi geçerli plan ile uyuşmuyor.");
            return;
        }

        ResolvedImapCredential? sourceResolved = null;
        ResolvedImapCredential? targetResolved = null;
        ImapAccountRecord sourceRecord;
        ImapAccountRecord targetRecord;
        ImapConnectionCredential sourceCredential;
        ImapConnectionCredential targetCredential;

        try
        {
            if (_credentialResolver != null)
            {
                sourceResolved = await _credentialResolver.ResolveCredentialAsync(
                    plan.SourceAccountId, plan.CompanyId, plan.ProjectId, ct);
                targetResolved = await _credentialResolver.ResolveCredentialAsync(
                    plan.TargetAccountId, plan.CompanyId, plan.ProjectId, ct);

                sourceRecord = sourceResolved.Account;
                targetRecord = targetResolved.Account;
                sourceCredential = sourceResolved.Credential;
                targetCredential = targetResolved.Credential;
            }
            else
            {
                var (sRec, sPwd) = _accountStore.GetInternalAccountWithPassword(
                    plan.SourceAccountId, plan.CompanyId, plan.ProjectId);
                var (tRec, tPwd) = _accountStore.GetInternalAccountWithPassword(
                    plan.TargetAccountId, plan.CompanyId, plan.ProjectId);

                sourceRecord = sRec;
                targetRecord = tRec;
                sourceCredential = new ImapPasswordCredential(sRec.Username, sPwd, sRec.AllowUnencryptedConnection);
                targetCredential = new ImapPasswordCredential(tRec.Username, tPwd, tRec.AllowUnencryptedConnection);
            }
        }
        catch (ReauthorizationRequiredException ex)
        {
            jobRecord.Status = "interrupted";
            jobRecord.Stage = "Yeniden Yetkilendirme Gerekli";
            jobRecord.ErrorMessage = $"[reauthorization_required] Hesabın Microsoft oturum süresi doldu ({ex.AccountId}). Yeniden bağlandıktan sonra aktarıma devam edebilirsiniz.";
            _saveJobRecord(jobRecord);
            return;
        }

        if (sourceRecord.Version != plan.SourceAccountVersion)
        {
            FailJob(jobRecord, "Kaynak hesap sürümü önizleme sonrasında değişti. Bütünlüğü korumak için aktarım engellendi.");
            return;
        }

        if (targetRecord.Version != plan.TargetAccountVersion)
        {
            FailJob(jobRecord, "Hedef hesap sürümü önizleme sonrasında değişti. Bütünlüğü korumak için aktarım engellendi.");
            return;
        }

        using var sourceClient = _clientFactory.CreateClient();
        using var targetClient = _clientFactory.CreateClient();

        try
        {
            await sourceClient.ConnectAndAuthenticateAsync(
                sourceRecord.Host, sourceRecord.Port, sourceRecord.TlsMode, sourceCredential, ct);
            await targetClient.ConnectAndAuthenticateAsync(
                targetRecord.Host, targetRecord.Port, targetRecord.TlsMode, targetCredential, ct);
        }
        catch (ReauthorizationRequiredException ex)
        {
            jobRecord.Status = "interrupted";
            jobRecord.Stage = "Yeniden Yetkilendirme Gerekli";
            jobRecord.ErrorMessage = $"[reauthorization_required] Hesabın Microsoft oturum süresi doldu ({ex.AccountId}). Yeniden bağlandıktan sonra aktarıma devam edebilirsiniz.";
            _saveJobRecord(jobRecord);
            return;
        }

        try
        {
            // Only brand-new start InitializeJournal; missing/corrupt journal on resume fails closed.
            var journalState = _journal.GetJournal(jobRecord.JobId);
            if (journalState == null)
            {
                if (wasStarted)
                {
                    FailJob(jobRecord, "Aktarım günlüğü bulunamadı veya hasarlı. Kesintiye uğrayan iş devam ettirilemez.");
                    return;
                }
                journalState = _journal.InitializeJournal(jobRecord.JobId, plan);
            }
            else if (!string.Equals(journalState.PlanId, plan.PlanId, StringComparison.Ordinal))
            {
                FailJob(jobRecord, "Aktarım günlüğündeki plan bilgisi geçerli plan ile uyuşmuyor. Güvenlik gereği aktarım durduruldu.");
                return;
            }

            // 2. Validate target folders support permanent user-defined keywords (case-sensitive except INBOX)
            var distinctTargetFolders = plan.Items.Select(i => i.TargetFolder).Distinct(ImapFolderPathComparer.Instance).ToList();
            foreach (var tf in distinctTargetFolders)
            {
                bool supportsKeywords = await targetClient.SupportsUserKeywordsAsync(tf, ct);
                if (!supportsKeywords)
                {
                    FailJob(jobRecord, $"Hedef klasör '{tf}' kalıcı özel anahtar kelime (PERMANENTFLAGS \\*) desteğine sahip değildir. Güvenli aktarım yürütülemez.");
                    return;
                }
            }

            // 3. Pre-flight source re-check: verify UIDVALIDITY and item hashes (case-sensitive except INBOX)
            var distinctSourceFolders = plan.Items.Select(i => i.SourceFolder).Distinct(ImapFolderPathComparer.Instance).ToList();
            foreach (var sf in distinctSourceFolders)
            {
                uint currentValidity = await sourceClient.GetFolderUidValidityAsync(sf, ct);
                uint expectedValidity = plan.Items.First(i => ImapFolderPathComparer.Instance.Equals(i.SourceFolder, sf)).SourceUidValidity;
                if (currentValidity != expectedValidity)
                {
                    FailJob(jobRecord, $"Kaynak klasör '{sf}' UIDVALIDITY değeri değişti (beklenen: {expectedValidity}, geçerli: {currentValidity}). Aktarım engellendi.");
                    return;
                }

            }

            // Verify each planned item matches current source state before any write
            foreach (var item in plan.Items)
            {
                var currentMsg = await sourceClient.FetchSingleSourceMessageAsync(item.SourceFolder, item.SourceUid, ct);
                if (currentMsg == null)
                {
                    FailJob(jobRecord, $"Kaynak ileti '{item.SourceFolder}' klasöründe bulunamadı (UID: {item.SourceUid}). Kaynakta değişiklik tespit edildi.");
                    return;
                }

                using var preflightMessage = currentMsg.Message;

                if (advanced is not null && MimeAdvancedFilterAdapter.Evaluate(advanced, preflightMessage, currentMsg.RawBytes.LongLength) != MailFilterMatch.Match)
                {
                    FailJob(jobRecord, "Kaynak ileti donmuş gelişmiş filtreye artık uymuyor.");
                    return;
                }

                if (!SourceMatches(item, currentMsg))
                {
                    FailJob(jobRecord, $"Kaynak ileti içeriği ('{item.SourceFolder}', UID: {item.SourceUid}) önizleme sonrasında değişti.");
                    return;
                }
            }

            int processedCount = 0;
            int verifiedCount = 0;
            int failedCount = 0;
            int needsAttentionCount = 0;

            // 4. Process each item through durable journal state machine
            foreach (var item in plan.Items.OrderBy(i => journalState.Entries[i.ItemId].Status == ImapTransferItemStatus.Planned ? 1 : 0))
            {
                ct.ThrowIfCancellationRequested();

                if (!journalState.Entries.TryGetValue(item.ItemId, out var entry))
                {
                    entry = new ImapTransferJournalEntry
                    {
                        ItemId = item.ItemId,
                        Status = ImapTransferItemStatus.Planned,
                        SourceFolder = item.SourceFolder,
                        SourceUid = item.SourceUid,
                        SourceUidValidity = item.SourceUidValidity,
                        TargetFolder = item.TargetFolder,
                        ExpectedSha256 = item.SourceSha256
                    };
                    journalState.Entries[item.ItemId] = entry;
                }

                jobRecord.CurrentFolder = item.SourceFolder;
                jobRecord.Stage = $"Aktarılıyor: {item.SourceFolder} (UID: {item.SourceUid})";
                _saveJobRecord(jobRecord);

                if (entry.Status is ImapTransferItemStatus.NeedsAttention or ImapTransferItemStatus.Failed)
                {
                    FailJob(jobRecord, "Önceki aktarım kaydı inceleme gerektiriyor. Yeni ileti eklenmedi.");
                    return;
                }

                var expectedFlags = MessageFlags.None;
                if (item.Flags.Contains("\\Seen", StringComparer.OrdinalIgnoreCase)) expectedFlags |= MessageFlags.Seen;
                if (item.Flags.Contains("\\Answered", StringComparer.OrdinalIgnoreCase)) expectedFlags |= MessageFlags.Answered;
                if (item.Flags.Contains("\\Flagged", StringComparer.OrdinalIgnoreCase)) expectedFlags |= MessageFlags.Flagged;
                if (item.Flags.Contains("\\Draft", StringComparer.OrdinalIgnoreCase)) expectedFlags |= MessageFlags.Draft;

                // Case 1: Already Verified
                if (entry.Status == ImapTransferItemStatus.Verified)
                {
                    // Verified requires target UIDVALIDITY, exact bytes/hash/date/flags/keywords/token
                    if (!entry.TargetUid.HasValue)
                    {
                        entry.Status = ImapTransferItemStatus.NeedsAttention;
                        entry.ErrorMessage = "Doğrulanmış öğe hedef UID bilgisi içermiyor.";
                        _journal.UpdateEntry(jobRecord.JobId, entry);
                        needsAttentionCount++;
                        FailJob(jobRecord, entry.ErrorMessage);
                        return;
                    }

                    uint currentTargetValidity = await targetClient.GetFolderUidValidityAsync(item.TargetFolder, ct);
                    if (!entry.TargetUidValidity.HasValue || currentTargetValidity != entry.TargetUidValidity.Value)
                    {
                        entry.Status = ImapTransferItemStatus.NeedsAttention;
                        entry.ErrorMessage = $"Hedef klasör UIDVALIDITY bilgisi eksik veya değişti (beklenen: {entry.TargetUidValidity}, geçerli: {currentTargetValidity}).";
                        _journal.UpdateEntry(jobRecord.JobId, entry);
                        needsAttentionCount++;
                        FailJob(jobRecord, entry.ErrorMessage);
                        return;
                    }

                    var verifyResult = await targetClient.FetchAndVerifyAsync(item.TargetFolder, entry.TargetUid.Value, ct);

                    bool bytesMatch = verifyResult.Exists && string.Equals(verifyResult.RawSha256, item.SourceSha256, StringComparison.OrdinalIgnoreCase);
                    bool dateMatch = verifyResult.InternalDateUtc.HasValue && verifyResult.InternalDateUtc.Value == item.InternalDateUtc;
                    bool flagsMatch = (verifyResult.Flags & ~MessageFlags.Recent) == expectedFlags;
                    bool keywordsMatch = item.Keywords.All(k => verifyResult.Keywords.Contains(k, StringComparer.OrdinalIgnoreCase));
                    var matches = string.IsNullOrEmpty(entry.BitigMailKeyword) ? Array.Empty<uint>() : await targetClient.SearchByKeywordAsync(item.TargetFolder, entry.BitigMailKeyword, ct);
                    bool tokenMatch = !string.IsNullOrEmpty(entry.BitigMailKeyword) && verifyResult.Keywords.Contains(entry.BitigMailKeyword, StringComparer.OrdinalIgnoreCase) && matches.Count == 1 && matches[0] == entry.TargetUid.Value;

                    if (!bytesMatch || !dateMatch || !flagsMatch || !keywordsMatch || !tokenMatch)
                    {
                        entry.Status = ImapTransferItemStatus.NeedsAttention;
                        entry.ErrorMessage = $"Daha önce doğrulanmış hedef ileti (UID: {entry.TargetUid}) hedefte değiştirilmiş veya silinmiş.";
                        _journal.UpdateEntry(jobRecord.JobId, entry);
                        needsAttentionCount++;
                        FailJob(jobRecord, entry.ErrorMessage);
                        return;
                    }

                    verifiedCount++;
                    processedCount++;
                    UpdateJobProgress(jobRecord, verifiedCount, failedCount, plan.Items.Count);
                    continue;
                }

                // Case 2: AppendIntent -> resume reconciliation
                if (entry.Status == ImapTransferItemStatus.AppendIntent)
                {
                    if (string.IsNullOrEmpty(entry.BitigMailKeyword))
                    {
                        entry.Status = ImapTransferItemStatus.NeedsAttention;
                        entry.ErrorMessage = "AppendIntent kaydı işlem anahtar kelimesi (BitigMailKeyword) içermiyor.";
                        _journal.UpdateEntry(jobRecord.JobId, entry);
                        needsAttentionCount++;
                        FailJob(jobRecord, entry.ErrorMessage);
                        return;
                    }

                    uint currentTargetValidity = await targetClient.GetFolderUidValidityAsync(item.TargetFolder, ct);
                    if (!entry.TargetUidValidity.HasValue || currentTargetValidity != entry.TargetUidValidity.Value)
                    {
                        entry.Status = ImapTransferItemStatus.NeedsAttention;
                        entry.ErrorMessage = $"Hedef klasör UIDVALIDITY bilgisi eksik veya değişti (beklenen: {entry.TargetUidValidity}, geçerli: {currentTargetValidity}).";
                        _journal.UpdateEntry(jobRecord.JobId, entry);
                        needsAttentionCount++;
                        FailJob(jobRecord, entry.ErrorMessage);
                        return;
                    }

                    var matchingUids = await targetClient.SearchByKeywordAsync(item.TargetFolder, entry.BitigMailKeyword, ct);

                    if (matchingUids.Count == 1)
                    {
                        uint foundUid = matchingUids[0];
                        var targetData = await targetClient.FetchAndVerifyAsync(item.TargetFolder, foundUid, ct);

                        bool bytesMatch = targetData.Exists && string.Equals(targetData.RawSha256, item.SourceSha256, StringComparison.OrdinalIgnoreCase);
                        bool dateMatch = targetData.InternalDateUtc.HasValue && targetData.InternalDateUtc.Value == item.InternalDateUtc;
                        bool flagsMatch = (targetData.Flags & ~MessageFlags.Recent) == expectedFlags;
                        bool keywordsMatch = item.Keywords.All(k => targetData.Keywords.Contains(k, StringComparer.OrdinalIgnoreCase));
                        bool tokenMatch = targetData.Keywords.Contains(entry.BitigMailKeyword, StringComparer.OrdinalIgnoreCase);

                        if (bytesMatch && dateMatch && flagsMatch && keywordsMatch && tokenMatch)
                        {
                            entry.Status = ImapTransferItemStatus.Verified;
                            entry.TargetUid = foundUid;
                            entry.VerifiedSha256 = targetData.RawSha256;
                            entry.TargetInternalDateUtc = targetData.InternalDateUtc;
                            entry.TargetFlags = new List<string> { targetData.Flags.ToString() };
                            entry.ErrorMessage = null;
                            _journal.UpdateEntry(jobRecord.JobId, entry);

                            verifiedCount++;
                            processedCount++;
                            UpdateJobProgress(jobRecord, verifiedCount, failedCount, plan.Items.Count);
                            continue;
                        }
                        else
                        {
                            entry.Status = ImapTransferItemStatus.NeedsAttention;
                            entry.ErrorMessage = "Hedefte bulunan anahtar kelimeli ileti kaynak ile bayt veya meta veri eşitliği sağlamıyor.";
                            _journal.UpdateEntry(jobRecord.JobId, entry);
                            needsAttentionCount++;
                            FailJob(jobRecord, entry.ErrorMessage);
                            return;
                        }
                    }
                    else if (matchingUids.Count == 0)
                    {
                        // Ambiguous: Zero token stops whole job; never continue to later items
                        entry.Status = ImapTransferItemStatus.NeedsAttention;
                        entry.ErrorMessage = "AppendIntent kaydı için hedefte anahtar kelime bulunamadı. Yinelenen ileti riskini önlemek için otomatik yeniden ekleme yapılmadı.";
                        _journal.UpdateEntry(jobRecord.JobId, entry);
                        needsAttentionCount++;
                        FailJob(jobRecord, entry.ErrorMessage);
                        return;
                    }
                    else
                    {
                        // Multiple tokens stops whole job; never continue to later items
                        entry.Status = ImapTransferItemStatus.NeedsAttention;
                        entry.ErrorMessage = $"Hedef klasörde aynı anahtar kelimeye sahip birden çok ileti tespit edildi ({matchingUids.Count} adet).";
                        _journal.UpdateEntry(jobRecord.JobId, entry);
                        needsAttentionCount++;
                        FailJob(jobRecord, entry.ErrorMessage);
                        return;
                    }
                }

                // Case 3: Planned -> Normal transfer sequence
                if (entry.Status == ImapTransferItemStatus.Planned)
                {
                    // Final recheck full selected identity
                    var sourceMsg = await sourceClient.FetchSingleSourceMessageAsync(item.SourceFolder, item.SourceUid, ct);
                    if (sourceMsg == null)
                    {
                        FailJob(jobRecord, "Kaynak ileti klasörde bulunamadı. Güvenlik gereği aktarım durduruldu.");
                        return;
                    }

                    using var transferMessage = sourceMsg.Message;

                    if (sourceMsg.SourceUidValidity != item.SourceUidValidity)
                    {
                        FailJob(jobRecord, "Kaynak klasör UIDVALIDITY değeri değişti. Güvenlik gereği aktarım durduruldu.");
                        return;
                    }

                    // Before each APPEND current source raw serialization with DOS+HiddenHeaders.Clear+EnsureNewLine=true
                    byte[] serialized = ImapSerializationAssumptions.Serialize(sourceMsg.Message);
                    string serializedSha256 = Convert.ToHexString(SHA256.HashData(serialized)).ToLowerInvariant();

                    // Frozen exact bytes/hash/date/flags/keywords recheck
                    bool rawBytesMatch = serialized.SequenceEqual(sourceMsg.RawBytes);
                    bool hashMatch = string.Equals(serializedSha256, item.SourceSha256, StringComparison.OrdinalIgnoreCase) &&
                                     string.Equals(sourceMsg.RawSha256, item.SourceSha256, StringComparison.OrdinalIgnoreCase);
                    bool dateMatch = sourceMsg.InternalDateUtc == item.InternalDateUtc &&
                                     sourceMsg.OriginalMimeDateUtc == item.OriginalMimeDateUtc;
                    bool flagsMatch = (sourceMsg.Flags & ~MessageFlags.Recent) == expectedFlags;
                    bool keywordsMatch = sourceMsg.Keywords.Count == item.Keywords.Count &&
                                         item.Keywords.All(k => sourceMsg.Keywords.Contains(k, StringComparer.OrdinalIgnoreCase));

                    if (!rawBytesMatch || !hashMatch || !dateMatch || !flagsMatch || !keywordsMatch)
                    {
                        FailJob(jobRecord, "Kaynak ileti içeriği veya meta verileri önizleme sonrasında değişti. Güvenlik gereği aktarım durduruldu.");
                        return;
                    }

                    // Generate random 128-bit per-item BitigMail keyword
                    byte[] tokenBytes = RandomNumberGenerator.GetBytes(16);
                    string keyword = "bitigmail_" + Convert.ToHexString(tokenBytes).ToLowerInvariant();

                    uint targetValidity = await targetClient.GetFolderUidValidityAsync(item.TargetFolder, ct);

                    entry.Status = ImapTransferItemStatus.AppendIntent;
                    entry.BitigMailKeyword = keyword;
                    entry.TargetUidValidity = targetValidity;
                    entry.ExpectedSha256 = item.SourceSha256;

                    // Atomic write to durable journal BEFORE network write
                    try
                    {
                        _journal.UpdateEntry(jobRecord.JobId, entry);
                    }
                    catch (Exception)
                    {
                        FailJob(jobRecord, "Duran günlüğe AppendIntent yazılamadı. Güvenlik gereği aktarım durduruldu.");
                        return;
                    }

                    // Prepare flags
                    var flags = expectedFlags;

                    var keywords = new HashSet<string>(item.Keywords, StringComparer.OrdinalIgnoreCase)
                    {
                        keyword
                    };

                    // Public MailKit APPEND
                    uint appendedUid;
                    try
                    {
                        appendedUid = await targetClient.AppendMessageAsync(
                            item.TargetFolder,
                            sourceMsg.Message,
                            flags,
                            keywords,
                            item.InternalDateUtc,
                            ct);
                    }
                    catch (Exception)
                    {
                        entry.ErrorMessage = "Hedef IMAP sunucusuna ileti ekleme (APPEND) işlemi başarısız oldu.";
                        _journal.UpdateEntry(jobRecord.JobId, entry);
                        failedCount++;
                        jobRecord.Status = "interrupted";
                        jobRecord.Stage = "Bağlantı kesildi; devam etmeden önce hedef uzlaştırılacak";
                        jobRecord.ErrorMessage = entry.ErrorMessage;
                        _saveJobRecord(jobRecord);
                        return;
                    }

                    // Independent FETCH from target and keyword SEARCH verification
                    var targetVerify = await targetClient.FetchAndVerifyAsync(item.TargetFolder, appendedUid, ct);
                    var searchMatches = await targetClient.SearchByKeywordAsync(item.TargetFolder, keyword, ct);

                    uint currentPostAppendValidity = await targetClient.GetFolderUidValidityAsync(item.TargetFolder, ct);

                    bool postAppendShaMatch = targetVerify.Exists && string.Equals(targetVerify.RawSha256, item.SourceSha256, StringComparison.OrdinalIgnoreCase);
                    bool postAppendKeywordMatch = searchMatches.Count == 1 && searchMatches[0] == appendedUid;
                    bool postAppendValidityMatch = currentPostAppendValidity == targetValidity;
                    bool postAppendDateMatch = targetVerify.InternalDateUtc.HasValue && targetVerify.InternalDateUtc.Value == item.InternalDateUtc;
                    bool postAppendFlagsMatch = (targetVerify.Flags & ~MessageFlags.Recent) == flags;
                    bool postAppendKeywordsMatch = item.Keywords.All(k => targetVerify.Keywords.Contains(k, StringComparer.OrdinalIgnoreCase)) &&
                                                   targetVerify.Keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase);

                    if (!postAppendShaMatch || !postAppendKeywordMatch || !postAppendValidityMatch || !postAppendDateMatch || !postAppendFlagsMatch || !postAppendKeywordsMatch)
                    {
                        entry.Status = ImapTransferItemStatus.Failed;
                        entry.TargetUid = appendedUid;
                        entry.VerifiedSha256 = targetVerify.RawSha256;
                        entry.ErrorMessage = "Hedef doğrulama başarısız oldu (meta veri veya içerik uyumsuzluğu).";
                        _journal.UpdateEntry(jobRecord.JobId, entry);
                        failedCount++;
                        FailJob(jobRecord, entry.ErrorMessage);
                        return;
                    }

                    // Transition to Verified
                    entry.Status = ImapTransferItemStatus.Verified;
                    entry.TargetUid = appendedUid;
                    entry.VerifiedSha256 = targetVerify.RawSha256;
                    entry.TargetInternalDateUtc = targetVerify.InternalDateUtc;
                    entry.TargetFlags = new List<string> { targetVerify.Flags.ToString() };
                    entry.ErrorMessage = null;

                    // Atomic write to durable journal AFTER verification
                    try
                    {
                        _journal.UpdateEntry(jobRecord.JobId, entry);
                    }
                    catch (Exception)
                    {
                        // Persistence error: keep target UID evidence in entry and fail closed
                        FailJob(jobRecord, $"Doğrulama sonrası günlüğe yazılamadı. Hedef UID ({appendedUid}) korundu.");
                        return;
                    }

                    verifiedCount++;
                    processedCount++;
                    UpdateJobProgress(jobRecord, verifiedCount, failedCount, plan.Items.Count);
                }
            }

            // 5. Post-transfer source re-verification (case-sensitive except INBOX)
            foreach (var sf in distinctSourceFolders)
            {
                uint postValidity = await sourceClient.GetFolderUidValidityAsync(sf, ct);
                uint expectedValidity = plan.Items.First(i => ImapFolderPathComparer.Instance.Equals(i.SourceFolder, sf)).SourceUidValidity;
                if (postValidity != expectedValidity)
                {
                    FailJob(jobRecord, $"Aktarım sonrası kaynak klasör '{sf}' UIDVALIDITY değeri değişti.");
                    return;
                }
                foreach (var item in plan.Items.Where(i => ImapFolderPathComparer.Instance.Equals(i.SourceFolder, sf)))
                {
                    var message = await sourceClient.FetchSingleSourceMessageAsync(sf, item.SourceUid, ct);
                    if (message == null)
                    {
                        FailJob(jobRecord, "Aktarım sonrasında kaynak ileti içeriği veya özellikleri değişti.");
                        return;
                    }
                    using var finalSourceMessage = message.Message;
                    if (!SourceMatches(item, message))
                    {
                        FailJob(jobRecord, "Aktarım sonrasında kaynak ileti içeriği veya özellikleri değişti.");
                        return;
                    }
                }
            }

            jobRecord.Status = "verifying";
            jobRecord.Stage = "Son doğrulama yapılıyor";
            jobRecord.ProgressPhase = "verifying";
            jobRecord.PhaseCompleted = 0;
            jobRecord.PhaseTotal = plan.Items.Count;
            _saveJobRecord(jobRecord);
            var phaseClock = System.Diagnostics.Stopwatch.StartNew();
            // Recheck every destination at completion, including messages written earlier in this job.
            foreach (var item in plan.Items)
            {
                var entry = journalState.Entries[item.ItemId];
                if (entry.Status != ImapTransferItemStatus.Verified || !entry.TargetUid.HasValue || !entry.TargetUidValidity.HasValue || string.IsNullOrEmpty(entry.BitigMailKeyword))
                { FailJob(jobRecord, "Aktarımın tüm hedef kayıtları doğrulanamadı."); return; }
                var verification = await targetClient.VerifyTargetItemAsync(item.TargetFolder, entry.TargetUid.Value, entry.BitigMailKeyword, ct);
                var value = verification.Message;
                var tokens = verification.MatchingKeywordUids;
                if (verification.UidValidity != entry.TargetUidValidity.Value || !value.Exists || value.RawSha256 != item.SourceSha256 ||
                    value.InternalDateUtc != item.InternalDateUtc || (value.Flags & ~MessageFlags.Recent) != PlannedFlags(item) ||
                    !item.Keywords.All(k => value.Keywords.Contains(k, StringComparer.OrdinalIgnoreCase)) ||
                    !value.Keywords.Contains(entry.BitigMailKeyword, StringComparer.OrdinalIgnoreCase) || tokens.Count != 1 || tokens[0] != entry.TargetUid.Value)
                { FailJob(jobRecord, "Aktarım sonrasında hedef ileti içeriği veya özellikleri değişti."); return; }
                jobRecord.PhaseCompleted++;
                if (phaseClock.ElapsedMilliseconds >= 750) { _saveJobRecord(jobRecord); phaseClock.Restart(); }
            }
            _saveJobRecord(jobRecord);

            // 6. Build durable ConversionReport with ImapTransfer details
            var auditItems = new List<ImapTransferItemAuditRecord>();
            foreach (var item in plan.Items)
            {
                if (journalState.Entries.TryGetValue(item.ItemId, out var entry))
                {
                    auditItems.Add(new ImapTransferItemAuditRecord
                    {
                        ItemId = item.ItemId,
                        SourceFolder = item.SourceFolder,
                        SourceUid = item.SourceUid,
                        TargetFolder = item.TargetFolder,
                        TargetUid = entry.TargetUid,
                        BitigMailKeyword = entry.BitigMailKeyword ?? string.Empty,
                        Status = entry.Status.ToString(),
                        SourceSha256 = item.SourceSha256,
                        VerifiedSha256 = entry.VerifiedSha256,
                        OriginalMimeDateUtc = item.OriginalMimeDateUtc,
                        InternalDateUtc = item.InternalDateUtc,
                        ErrorMessage = entry.ErrorMessage
                    });
                }
            }

            var reportDetail = new ImapTransferReportDetail
            {
                AdvancedFilterCanonicalJson = plan.AdvancedFilterCanonicalJson,
                AdvancedFilterFingerprint = plan.AdvancedFilterFingerprint,
                AdvancedFilterUnknownCount = plan.AdvancedFilterUnknownCount,
                JobId = jobRecord.JobId,
                PlanId = plan.PlanId,
                SourceAccountId = plan.SourceAccountId,
                SourceAccountVersion = plan.SourceAccountVersion,
                TargetAccountId = plan.TargetAccountId,
                TargetAccountVersion = plan.TargetAccountVersion,
                StartDate = plan.StartDate,
                EndDate = plan.EndDate,
                Folders = plan.Preview?.Folders ?? new(),
                TotalPlanned = plan.Items.Count,
                TotalVerified = verifiedCount,
                TotalFailed = failedCount,
                TotalNeedsAttention = needsAttentionCount,
                Items = auditItems
            };

            bool isFullSuccess = (verifiedCount == plan.Items.Count) && (failedCount == 0) && (needsAttentionCount == 0);

            var report = new ConversionReport
            {
                JobId = jobRecord.JobId,
                JobKind = "imap-transfer",
                EvidenceLabel = "IMAP_SERVER_TRANSFER (MailKit APPEND/FETCH Verification)",
                ClientContext = jobRecord.ClientContext,
                SourceFileName = plan.SourceDisplayName ?? plan.SourceAccountId,
                OutputPstFileName = plan.TargetDisplayName ?? plan.TargetAccountId,
                ConversionSuccess = isFullSuccess,
                OverallStatus = isFullSuccess ? "SUCCESS" : (needsAttentionCount > 0 ? "NEEDS_ATTENTION" : "FAILED"),
                ItemsRead = plan.Items.Count,
                ItemsWritten = verifiedCount,
                FailedItems = failedCount,
                TotalSourceMessages = plan.Preview?.TotalSourceItems ?? plan.Items.Count,
                ExcludedMessagesCount = plan.Preview?.ExcludedCount ?? 0,
                MissingDateExcludedCount = plan.Preview?.MissingDateExcludedCount ?? 0,
                IsFiltered = advanced is not null || !string.IsNullOrEmpty(plan.StartDate) || !string.IsNullOrEmpty(plan.EndDate),
                SelectedMessagesCount = plan.Items.Count,
                ImapTransfer = reportDetail,
                ReopenedPstVerification = new ReopenedPstVerificationInfo
                {
                    VerificationSuccess = false,
                    VerifiedWith = "N/A (IMAP Transfer: MailKit APPEND/FETCH exact byte SHA-256)",
                    VerificationNotes = new List<string> { "PST verification is not applicable for IMAP transfer jobs." }
                }
            };

            // Reports are durable and saved BEFORE marking job as complete/failed
            try
            {
                if (sourceResolved?.CommitAsync != null) await sourceResolved.CommitAsync();
                if (targetResolved?.CommitAsync != null) await targetResolved.CommitAsync();
                _saveReport(report);

                jobRecord.ImapTransfer = reportDetail;
                jobRecord.ItemsRead = plan.Items.Count;
                jobRecord.ItemsWritten = verifiedCount;
                jobRecord.FailedItems = failedCount;
                jobRecord.TotalItems = plan.Items.Count;
                jobRecord.PercentComplete = 100;
                jobRecord.CompletedAt = DateTimeOffset.UtcNow;

                if (isFullSuccess)
                {
                    jobRecord.Status = "completed";
                    jobRecord.Stage = "Aktarım Başarıyla Tamamlandı";
                }
                else if (needsAttentionCount > 0)
                {
                    jobRecord.Status = "interrupted";
                    jobRecord.Stage = "İnceleme Gerektiriyor (NeedsAttention)";
                    jobRecord.ErrorMessage = $"Aktarım sırasında {needsAttentionCount} ileti inceleme gerektiren duruma geldi.";
                }
                else
                {
                    jobRecord.Status = "failed";
                    jobRecord.Stage = "Aktarım Hatalarla Tamamlandı";
                    jobRecord.ErrorMessage = $"{failedCount} ileti aktarılamadı.";
                }

                _saveJobRecord(jobRecord);
            }
            catch (Exception)
            {
                // Terminal persistence failure: never publishes Completed, preserves target evidence and publishes non-complete safe state
                jobRecord.Status = "failed";
                jobRecord.Stage = "Hata";
                jobRecord.ErrorMessage = "Dayanıklı rapor veya nihai iş kaydı kaydedilemedi. Hedef veriler korundu.";
                jobRecord.CompletedAt = DateTimeOffset.UtcNow;
                try
                {
                    _saveJobRecord(jobRecord);
                }
                catch { }
                throw;
            }
        }
        finally
        {
            await sourceClient.DisconnectAsync(ct);
            await targetClient.DisconnectAsync(ct);
        }
    }

    private void UpdateJobProgress(LocalJobRecord job, int verified, int failed, int total)
    {
        job.ItemsWritten = verified;
        job.FailedItems = failed;
        job.TotalItems = total;
        job.PercentComplete = total > 0 ? (int)((long)(verified + failed) * 100 / total) : 0;
        _saveJobRecord(job);
    }

    private void FailJob(LocalJobRecord job, string error)
    {
        job.Status = "failed";
        job.Stage = "Hata";
        job.ErrorMessage = error;
        job.CompletedAt = DateTimeOffset.UtcNow;
        try { _saveJobRecord(job); } catch { }
    }

    private static MessageFlags PlannedFlags(ImapTransferPlannedItem item)
    {
        MessageFlags value = MessageFlags.None;
        foreach (var flag in item.Flags)
            value |= flag.ToLowerInvariant() switch { "\\seen" => MessageFlags.Seen, "\\answered" => MessageFlags.Answered,
                "\\flagged" => MessageFlags.Flagged, "\\draft" => MessageFlags.Draft, _ => MessageFlags.None };
        return value;
    }

    private static bool SourceMatches(ImapTransferPlannedItem item, ImapSourceMessageSummary source) =>
        source.SourceUidValidity == item.SourceUidValidity && source.RawSha256 == item.SourceSha256 &&
        source.InternalDateUtc == item.InternalDateUtc && source.OriginalMimeDateUtc == item.OriginalMimeDateUtc &&
        (source.Flags & ~MessageFlags.Recent) == PlannedFlags(item) && source.Keywords.Count == item.Keywords.Count &&
        item.Keywords.All(k => source.Keywords.Contains(k, StringComparer.OrdinalIgnoreCase)) &&
        ImapSerializationAssumptions.Serialize(source.Message).SequenceEqual(source.RawBytes);
}
