using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Bridge;
using BitigMail.Engine.Imap;
using BitigMail.Engine.Imap.Transfer;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.Imap.Transfer;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Security;
using MailKit;
using MimeKit;

namespace BitigMail.LocalHost.Bridge.Transfer;

/// <summary>
/// Worker executing File -> IMAP import with durable journal transitions,
/// per-item keyword stamping, independent post-append verification,
/// TASK-014 ambiguous resume rules, and fail-closed integrity checks.
/// </summary>
public sealed class BridgeImportWorker
{
    private readonly FileHandleRegistry _handleRegistry;
    private readonly ImapAccountStore _accountStore;
    private readonly IImapTransferClientFactory _clientFactory;
    private readonly BridgeTransferJournal _journal;
    private readonly Action<LocalJobRecord> _saveJobRecord;
    private readonly Action<ConversionReport> _saveReport;
    private readonly IImapCredentialResolver? _credentialResolver;
    private readonly MimeSourceInspector _inspector = new();

    public BridgeImportWorker(
        FileHandleRegistry handleRegistry,
        ImapAccountStore accountStore,
        IImapTransferClientFactory clientFactory,
        BridgeTransferJournal journal,
        Action<LocalJobRecord> saveJobRecord,
        Action<ConversionReport> saveReport,
        IImapCredentialResolver? credentialResolver = null)
    {
        _handleRegistry = handleRegistry ?? throw new ArgumentNullException(nameof(handleRegistry));
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _saveJobRecord = saveJobRecord ?? throw new ArgumentNullException(nameof(saveJobRecord));
        _saveReport = saveReport ?? throw new ArgumentNullException(nameof(saveReport));
        _credentialResolver = credentialResolver;
    }

    public async Task ExecuteAsync(
        LocalJobRecord jobRecord,
        BridgeImportPlan plan,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(jobRecord);
        ArgumentNullException.ThrowIfNull(plan);

        CompiledMailFilter? advanced;
        try { advanced = FrozenMailFilter.Validate(plan.AdvancedFilterCanonicalJson, plan.AdvancedFilterFingerprint, plan.AdvancedFilterUnknownCount, plan.DateFilterBlocked); }
        catch (InvalidDataException) { FailJob(jobRecord, "Kalıcı gelişmiş filtre doğrulanamadı."); return; }
        jobRecord.AdvancedFilterCanonicalJson = plan.AdvancedFilterCanonicalJson;
        jobRecord.AdvancedFilterFingerprint = plan.AdvancedFilterFingerprint;
        jobRecord.AdvancedFilterUnknownCount = plan.AdvancedFilterUnknownCount;

        bool wasStarted = jobRecord.StartedAt != null || jobRecord.Status == "interrupted";

        jobRecord.Status = "converting";
        jobRecord.Stage = "İçe Aktarım Başlatılıyor";
        jobRecord.ProgressPhase = "transferring";
        jobRecord.PhaseCompleted = null;
        jobRecord.PhaseTotal = null;
        jobRecord.StartedAt = DateTimeOffset.UtcNow;
        _saveJobRecord(jobRecord);

        // Load or initialize durable journal
        var journalState = _journal.GetImportJournal(jobRecord.JobId);
        if (journalState == null)
        {
            if (wasStarted)
            {
                FailJob(jobRecord, "Aktarım günlüğü bulunamadı veya hasarlı. Kesintiye uğrayan iş devam ettirilemez.");
                return;
            }
            journalState = _journal.InitializeImportJournal(jobRecord.JobId, plan);
        }
        else if (!string.Equals(journalState.PlanId, plan.PlanId, StringComparison.Ordinal))
        {
            FailJob(jobRecord, "Aktarım günlüğündeki plan bilgisi geçerli plan ile uyuşmuyor.");
            return;
        }

        // Revalidate frozen normalization qualification on start and resume, even if the picker handle expired.
        if (plan.QualificationFingerprint is not null)
        {
            try
            {
                var frozenQualification = new NormalizedSourceQualificationReader().ReadManifestSource(plan.SourceKind, plan.SourceRootPath ?? throw new InvalidDataException("Qualification kaynak yolu eksik."), plan.QualificationSourcePaths, ct);
                if (!string.Equals(frozenQualification?.Fingerprint, plan.QualificationFingerprint, StringComparison.Ordinal) ||
                    frozenQualification?.DateFilterBlocked != plan.DateFilterBlocked || frozenQualification?.IsPartial != plan.QualificationIsPartial)
                { FailJob(jobRecord, "[BÜTÜNLÜK ENGELİ] Kaynak qualification bilgisi önizlemeden sonra değişti."); return; }
            }
            catch (Exception ex) { FailJob(jobRecord, $"Kaynak qualification doğrulanamadı: {ex.Message}"); return; }
        }

        // Revalidate source manifest integrity
        var mimeEntry = _handleRegistry.GetMimeSourceEntry(plan.SourceHandle);
        if (mimeEntry != null && mimeEntry.Manifest != null)
        {
            try
            {
                _inspector.RevalidateManifest(mimeEntry.Manifest);
                if (!string.Equals(mimeEntry.Manifest.AggregateFingerprint, plan.SourceFingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    FailJob(jobRecord, "[BÜTÜNLÜK ENGELİ] Kaynak arşiv parmak izi dondurulmuş plan ile uyuşmuyor.");
                    return;
                }
                var qualification = new NormalizedSourceQualificationReader().Read(mimeEntry.Manifest, ct);
                if (!string.Equals(qualification?.Fingerprint, plan.QualificationFingerprint, StringComparison.Ordinal) ||
                    (qualification?.DateFilterBlocked ?? false) != plan.DateFilterBlocked ||
                    (qualification?.IsPartial ?? false) != plan.QualificationIsPartial)
                {
                    FailJob(jobRecord, "[BÜTÜNLÜK ENGELİ] Kaynak qualification bilgisi dondurulmuş plan ile uyuşmuyor.");
                    return;
                }
            }
            catch (Exception ex)
            {
                FailJob(jobRecord, $"Kaynak arşiv doğrulanamadı: {ex.Message}");
                return;
            }
        }
        else
        {
            // Handle-free restart: frozen plan contains canonical source paths + record/item fingerprints.
            // Must revalidate the ENTIRE frozen source set and consume SourceFingerprint BEFORE first APPEND.
            if (plan.Items.Count == 0)
            {
                FailJob(jobRecord, "Dondurulmuş aktarım planında öğe bulunamadı.");
                return;
            }

            try
            {
                if (plan.SourceKind == "mbox")
                {
                    string mboxPath = !string.IsNullOrEmpty(plan.SourceRootPath) && File.Exists(plan.SourceRootPath)
                        ? plan.SourceRootPath
                        : (plan.Items.FirstOrDefault()?.SourceCanonicalPath ?? string.Empty);

                    if (!File.Exists(mboxPath))
                    {
                        FailJob(jobRecord, $"[BÜTÜNLÜK ENGELİ] Kaynak MBOX dosyası artık mevcut değil: {mboxPath}");
                        return;
                    }

                    var mboxManifest = _inspector.BuildMboxManifest(mboxPath);
                    if (!string.Equals(mboxManifest.AggregateFingerprint, plan.SourceFingerprint, StringComparison.OrdinalIgnoreCase))
                    {
                        FailJob(jobRecord, "[BÜTÜNLÜK ENGELİ] Kaynak MBOX parmak izi dondurulmuş plan ile uyuşmuyor.");
                        return;
                    }

                    foreach (var item in plan.Items)
                    {
                        if (item.PhysicalOrdinal < 0 || item.PhysicalOrdinal >= mboxManifest.Entries.Count)
                        {
                            FailJob(jobRecord, $"[BÜTÜNLÜK ENGELİ] MBOX kayıt indeksi geçersiz: {item.PhysicalOrdinal}");
                            return;
                        }
                        var mboxEntry = mboxManifest.Entries[item.PhysicalOrdinal];
                        if (!string.Equals(mboxEntry.Sha256, item.SourceSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            FailJob(jobRecord, $"[BÜTÜNLÜK ENGELİ] MBOX {item.PhysicalOrdinal + 1}. kayıt içeriğinde değişiklik tespit edildi.");
                            return;
                        }
                    }
                }
                else
                {
                    // Revalidate every single item in the entire frozen source set and rebuild manifest entries
                    var entries = new List<MimeSourceEntry>();
                    using var sha = SHA256.Create();
                    foreach (var item in plan.Items)
                    {
                        if (!File.Exists(item.SourceCanonicalPath))
                        {
                            FailJob(jobRecord, $"[BÜTÜNLÜK ENGELİ] Kaynak dosya silinmiş veya erişilemez: {item.SourceCanonicalPath}");
                            return;
                        }

                        var fi = new FileInfo(item.SourceCanonicalPath);
                        if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        {
                            FailJob(jobRecord, $"[GÜVENLİK ENGELİ] Kaynak dosya reparse point içeriyor: {item.SourceCanonicalPath}");
                            return;
                        }

                        byte[] fileBytes = await File.ReadAllBytesAsync(item.SourceCanonicalPath, ct);
                        string currentSha = Convert.ToHexString(sha.ComputeHash(fileBytes)).ToLowerInvariant();
                        if (!string.Equals(currentSha, item.SourceSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            FailJob(jobRecord, $"[BÜTÜNLÜK ENGELİ] Kaynak dosya içeriği değişmiş (hash uyuşmazlığı): {item.SourceCanonicalPath}");
                            return;
                        }

                        entries.Add(new MimeSourceEntry
                        {
                            CanonicalPath = item.SourceCanonicalPath,
                            RelativePath = item.SourceRelativePath,
                            MappedFolder = item.SourceMappedFolder,
                            SizeBytes = fi.Length,
                            Sha256 = currentSha,
                            PhysicalOrdinal = item.PhysicalOrdinal
                        });
                    }

                    if (plan.SourceKind == "eml-tree" && !string.IsNullOrEmpty(plan.SourceRootPath) && Directory.Exists(plan.SourceRootPath))
                    {
                        var dirManifest = _inspector.BuildEmlDirectoryManifest(plan.SourceRootPath);
                        if (!string.Equals(dirManifest.AggregateFingerprint, plan.SourceFingerprint, StringComparison.OrdinalIgnoreCase))
                        {
                            FailJob(jobRecord, "[BÜTÜNLÜK ENGELİ] Kaynak arşiv parmak izi dondurulmuş plan ile uyuşmuyor.");
                            return;
                        }
                    }
                    else
                    {
                        var reconstructedManifest = new MimeSourceManifest
                        {
                            SourceKind = plan.SourceKind,
                            Dialect = "rfc822",
                            RootPath = plan.SourceRootPath ?? string.Empty,
                            Entries = entries,
                            TotalFiles = entries.Count,
                            IgnoredNonEmlFilesCount = 0
                        };
                        string recomputed = MimeSourceInspector.ComputeManifestFingerprint(reconstructedManifest);
                        if (!string.Equals(recomputed, plan.SourceFingerprint, StringComparison.OrdinalIgnoreCase))
                        {
                            FailJob(jobRecord, "[BÜTÜNLÜK ENGELİ] Kaynak dosya parmak izi dondurulmuş plan ile uyuşmuyor.");
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FailJob(jobRecord, $"Kaynak arşiv doğrulanamadı: {ex.Message}");
                return;
            }
        }

        // Resolve target credentials and connect
        ResolvedImapCredential? targetResolved = null;
        ImapAccountRecord targetRecord;
        ImapConnectionCredential targetCredential;

        try
        {
            if (_credentialResolver != null)
            {
                targetResolved = await _credentialResolver.ResolveCredentialAsync(
                    plan.TargetAccountId, plan.CompanyId, plan.ProjectId, ct);
                targetRecord = targetResolved.Account;
                targetCredential = targetResolved.Credential;
            }
            else
            {
                var (tRec, tPwd) = _accountStore.GetInternalAccountWithPassword(
                    plan.TargetAccountId, plan.CompanyId, plan.ProjectId);
                targetRecord = tRec;
                targetCredential = new ImapPasswordCredential(tRec.Username, tPwd, tRec.AllowUnencryptedConnection);
            }
        }
        catch (ReauthorizationRequiredException ex)
        {
            jobRecord.Status = "interrupted";
            jobRecord.Stage = "Yeniden Yetkilendirme Gerekli";
            jobRecord.ErrorMessage = $"[reauthorization_required] Hesabın Microsoft oturum süresi doldu ({ex.AccountId}).";
            _saveJobRecord(jobRecord);
            return;
        }

        if (targetRecord.Version != plan.TargetAccountVersion)
        {
            FailJob(jobRecord, "Hedef hesap sürümü önizleme sonrasında değişti. Bütünlüğü korumak için aktarım engellendi.");
            return;
        }

        using var targetClient = _clientFactory.CreateClient();
        try
        {
            await targetClient.ConnectAndAuthenticateAsync(
                targetRecord.Host, targetRecord.Port, targetRecord.TlsMode, targetCredential, ct);
        }
        catch (ReauthorizationRequiredException ex)
        {
            jobRecord.Status = "interrupted";
            jobRecord.Stage = "Yeniden Yetkilendirme Gerekli";
            jobRecord.ErrorMessage = $"[reauthorization_required] Hesabın Microsoft oturum süresi doldu ({ex.AccountId}).";
            _saveJobRecord(jobRecord);
            return;
        }

        try
        {
            // Verify target folders support permanent user-defined keywords
            var distinctTargetFolders = plan.Items.Select(i => i.TargetFolder).Distinct(ImapFolderPathComparer.Instance).ToList();
            foreach (var tf in distinctTargetFolders)
            {
                bool supportsKeywords = await targetClient.SupportsUserKeywordsAsync(tf, ct);
                if (!supportsKeywords)
                {
                    FailJob(jobRecord, $"Hedef klasör '{tf}' kalıcı özel anahtar kelime (PERMANENTFLAGS \\*) desteğine sahip değildir.");
                    return;
                }
            }

            int processedCount = 0;
            int verifiedCount = 0;
            int failedCount = 0;
            int needsAttentionCount = 0;

            // Process items
            foreach (var item in plan.Items)
            {
                ct.ThrowIfCancellationRequested();

                if (!journalState.Entries.TryGetValue(item.ItemId, out var entry))
                {
                    entry = new BridgeImportJournalEntry
                    {
                        ItemId = item.ItemId,
                        Status = BridgeItemStatus.Planned,
                        SourceRelativePath = item.SourceRelativePath,
                        PhysicalOrdinal = item.PhysicalOrdinal,
                        TargetFolder = item.TargetFolder,
                        ExpectedSha256 = item.CanonicalSha256
                    };
                    journalState.Entries[item.ItemId] = entry;
                }

                jobRecord.CurrentFolder = item.TargetFolder;
                jobRecord.Stage = $"Aktarılıyor: {item.TargetFolder} ({item.SourceRelativePath})";
                _saveJobRecord(jobRecord);

                if (entry.Status is BridgeItemStatus.NeedsAttention or BridgeItemStatus.Failed)
                {
                    FailJob(jobRecord, "Önceki aktarım kaydı inceleme gerektiriyor (NeedsAttention/Failed). Yeni ileti eklenmedi.");
                    return;
                }

                // Case 1: Already Verified
                if (entry.Status == BridgeItemStatus.Verified)
                {
                    if (!entry.TargetUid.HasValue)
                    {
                        entry.Status = BridgeItemStatus.NeedsAttention;
                        entry.ErrorMessage = "Doğrulanmış öğe hedef UID bilgisi içermiyor.";
                        _journal.UpdateImportEntry(jobRecord.JobId, entry);
                        needsAttentionCount++;
                        FailJob(jobRecord, entry.ErrorMessage);
                        return;
                    }

                    uint currentTargetValidity = await targetClient.GetFolderUidValidityAsync(item.TargetFolder, ct);
                    if (!entry.TargetUidValidity.HasValue || currentTargetValidity != entry.TargetUidValidity.Value)
                    {
                        entry.Status = BridgeItemStatus.NeedsAttention;
                        entry.ErrorMessage = $"Hedef klasör UIDVALIDITY bilgisi eksik veya değişti (beklenen: {entry.TargetUidValidity}, geçerli: {currentTargetValidity}).";
                        _journal.UpdateImportEntry(jobRecord.JobId, entry);
                        needsAttentionCount++;
                        FailJob(jobRecord, entry.ErrorMessage);
                        return;
                    }

                    var verifyResult = await targetClient.FetchAndVerifyAsync(item.TargetFolder, entry.TargetUid.Value, ct);
                    bool bytesMatch = verifyResult.Exists && string.Equals(verifyResult.RawSha256, item.CanonicalSha256, StringComparison.OrdinalIgnoreCase);
                    bool dateMatch = verifyResult.InternalDateUtc.HasValue && verifyResult.InternalDateUtc.Value == item.PlannedInternalDateUtc;
                    bool flagsMatch = (verifyResult.Flags & ~MessageFlags.Recent) == MessageFlags.None;
                    var matches = string.IsNullOrEmpty(entry.BitigMailKeyword) ? Array.Empty<uint>() : await targetClient.SearchByKeywordAsync(item.TargetFolder, entry.BitigMailKeyword, ct);
                    bool tokenMatch = !string.IsNullOrEmpty(entry.BitigMailKeyword) && verifyResult.Keywords.Contains(entry.BitigMailKeyword, StringComparer.OrdinalIgnoreCase) && matches.Count == 1 && matches[0] == entry.TargetUid.Value;

                    if (!bytesMatch || !dateMatch || !flagsMatch || !tokenMatch)
                    {
                        entry.Status = BridgeItemStatus.NeedsAttention;
                        entry.ErrorMessage = $"Daha önce doğrulanmış hedef ileti (UID: {entry.TargetUid}) hedefte değiştirilmiş veya silinmiş.";
                        _journal.UpdateImportEntry(jobRecord.JobId, entry);
                        needsAttentionCount++;
                        FailJob(jobRecord, entry.ErrorMessage);
                        return;
                    }

                    verifiedCount++;
                    processedCount++;
                    UpdateJobProgress(jobRecord, verifiedCount, failedCount, plan.Items.Count);
                    continue;
                }

                // Case 2: AppendIntent -> resume reconciliation (TASK-014 rules)
                if (entry.Status == BridgeItemStatus.AppendIntent)
                {
                    if (string.IsNullOrEmpty(entry.BitigMailKeyword))
                    {
                        entry.Status = BridgeItemStatus.NeedsAttention;
                        entry.ErrorMessage = "AppendIntent kaydı işlem anahtar kelimesi (BitigMailKeyword) içermiyor.";
                        _journal.UpdateImportEntry(jobRecord.JobId, entry);
                        needsAttentionCount++;
                        FailJob(jobRecord, entry.ErrorMessage);
                        return;
                    }

                    uint currentTargetValidity = await targetClient.GetFolderUidValidityAsync(item.TargetFolder, ct);
                    if (!entry.TargetUidValidity.HasValue || currentTargetValidity != entry.TargetUidValidity.Value)
                    {
                        entry.Status = BridgeItemStatus.NeedsAttention;
                        entry.ErrorMessage = $"Hedef klasör UIDVALIDITY bilgisi eksik veya değişti (beklenen: {entry.TargetUidValidity}, geçerli: {currentTargetValidity}).";
                        _journal.UpdateImportEntry(jobRecord.JobId, entry);
                        needsAttentionCount++;
                        FailJob(jobRecord, entry.ErrorMessage);
                        return;
                    }

                    var matchingUids = await targetClient.SearchByKeywordAsync(item.TargetFolder, entry.BitigMailKeyword, ct);

                    if (matchingUids.Count == 1)
                    {
                        uint foundUid = matchingUids[0];
                        var targetData = await targetClient.FetchAndVerifyAsync(item.TargetFolder, foundUid, ct);

                        bool bytesMatch = targetData.Exists && string.Equals(targetData.RawSha256, item.CanonicalSha256, StringComparison.OrdinalIgnoreCase);
                        bool dateMatch = targetData.InternalDateUtc.HasValue && targetData.InternalDateUtc.Value == item.PlannedInternalDateUtc;
                        bool flagsMatch = (targetData.Flags & ~MessageFlags.Recent) == MessageFlags.None;
                        bool tokenMatch = targetData.Keywords.Contains(entry.BitigMailKeyword, StringComparer.OrdinalIgnoreCase);

                        if (bytesMatch && dateMatch && flagsMatch && tokenMatch)
                        {
                            entry.Status = BridgeItemStatus.Verified;
                            entry.TargetUid = foundUid;
                            entry.VerifiedSha256 = targetData.RawSha256;
                            entry.TargetInternalDateUtc = targetData.InternalDateUtc;
                            entry.TargetFlags = new List<string> { targetData.Flags.ToString() };
                            entry.ErrorMessage = null;
                            _journal.UpdateImportEntry(jobRecord.JobId, entry);

                            verifiedCount++;
                            processedCount++;
                            UpdateJobProgress(jobRecord, verifiedCount, failedCount, plan.Items.Count);
                            continue;
                        }
                        else
                        {
                            entry.Status = BridgeItemStatus.NeedsAttention;
                            entry.ErrorMessage = "Hedefte bulunan anahtar kelimeli ileti kaynak ile bayt veya meta veri eşitliği sağlamıyor.";
                            _journal.UpdateImportEntry(jobRecord.JobId, entry);
                            needsAttentionCount++;
                            FailJob(jobRecord, entry.ErrorMessage);
                            return;
                        }
                    }
                    else if (matchingUids.Count == 0)
                    {
                        // Ambiguous zero: stops whole job, never re-APPEND
                        entry.Status = BridgeItemStatus.NeedsAttention;
                        entry.ErrorMessage = "AppendIntent kaydı için hedefte anahtar kelime bulunamadı. Yinelenen ileti riskini önlemek için otomatik yeniden ekleme yapılmadı.";
                        _journal.UpdateImportEntry(jobRecord.JobId, entry);
                        needsAttentionCount++;
                        FailJob(jobRecord, entry.ErrorMessage);
                        return;
                    }
                    else
                    {
                        // Multiple matches: stops whole job
                        entry.Status = BridgeItemStatus.NeedsAttention;
                        entry.ErrorMessage = $"Hedef klasörde aynı anahtar kelimeye sahip birden çok ileti tespit edildi ({matchingUids.Count} adet).";
                        _journal.UpdateImportEntry(jobRecord.JobId, entry);
                        needsAttentionCount++;
                        FailJob(jobRecord, entry.ErrorMessage);
                        return;
                    }
                }

                // Case 3: Planned -> Normal transfer sequence
                if (entry.Status == BridgeItemStatus.Planned)
                {
                    // Revalidate single item from source
                    byte[] sourceRawBytes;
                    if (plan.SourceKind == "mbox")
                    {
                        string mboxPath = !string.IsNullOrEmpty(plan.SourceRootPath) && File.Exists(plan.SourceRootPath)
                            ? plan.SourceRootPath
                            : (mimeEntry?.Manifest?.RootPath ?? item.SourceCanonicalPath);
                        if (!File.Exists(mboxPath))
                        {
                            FailJob(jobRecord, $"Kaynak MBOX dosyası bulunamadı: {mboxPath}");
                            return;
                        }
                        using var fs = new FileStream(mboxPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        var record = MboxrdRecordReader.EnumerateRecords(fs).FirstOrDefault(r => r.Ordinal == item.PhysicalOrdinal);
                        if (record == null)
                        {
                            FailJob(jobRecord, $"Kaynak MBOX kaydı bulunamadı (Ordinal: {item.PhysicalOrdinal}).");
                            return;
                        }
                        sourceRawBytes = record.RawMimeBytes;
                    }
                    else
                    {
                        if (!File.Exists(item.SourceCanonicalPath))
                        {
                            FailJob(jobRecord, $"Kaynak EML dosyası bulunamadı: {item.SourceCanonicalPath}");
                            return;
                        }
                        sourceRawBytes = await File.ReadAllBytesAsync(item.SourceCanonicalPath, ct);
                    }

                    using var sha = SHA256.Create();
                    string currentRawSha256 = Convert.ToHexString(sha.ComputeHash(sourceRawBytes)).ToLowerInvariant();
                    if (!string.Equals(currentRawSha256, item.SourceSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        FailJob(jobRecord, $"Kaynak ileti içeriği ('{item.SourceRelativePath}') önizleme sonrasında değişti. Güvenlik gereği aktarım durduruldu.");
                        return;
                    }

                    // Strict byte canonicalization
                    var canonical = BridgeMimeBytePolicy.CanonicalizeForImap(sourceRawBytes);
                    if (!string.Equals(canonical.CanonicalSha256, item.CanonicalSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        FailJob(jobRecord, "Kanonik hash değeri dondurulmuş plan ile uyuşmuyor.");
                        return;
                    }

                    using var mimeStream = new MemoryStream(canonical.Bytes);
                    using var mimeMsg = MimeMessage.Load(mimeStream);
                    if (advanced is not null && MimeAdvancedFilterAdapter.Evaluate(advanced, mimeMsg, sourceRawBytes.LongLength) != MailFilterMatch.Match)
                    { FailJob(jobRecord, "Kaynak ileti donmuş gelişmiş filtreye uymuyor."); return; }

                    byte[] serialized = ImapSerializationAssumptions.Serialize(mimeMsg);
                    if (!serialized.SequenceEqual(canonical.Bytes))
                    {
                        FailJob(jobRecord, $"MIME yeniden serileştirme baytları kanonik baytlar ile uyuşmuyor: {item.SourceRelativePath}");
                        return;
                    }

                    // Generate random 128-bit per-item BitigMail keyword
                    byte[] tokenBytes = RandomNumberGenerator.GetBytes(16);
                    string keyword = "bitigmail_" + Convert.ToHexString(tokenBytes).ToLowerInvariant();

                    uint targetValidity = await targetClient.GetFolderUidValidityAsync(item.TargetFolder, ct);

                    entry.Status = BridgeItemStatus.AppendIntent;
                    entry.BitigMailKeyword = keyword;
                    entry.TargetUidValidity = targetValidity;
                    entry.ExpectedSha256 = canonical.CanonicalSha256;

                    // Atomic durable flush BEFORE network write
                    try
                    {
                        _journal.UpdateImportEntry(jobRecord.JobId, entry);
                    }
                    catch (Exception)
                    {
                        FailJob(jobRecord, "Duran günlüğe AppendIntent yazılamadı. Güvenlik gereği aktarım durduruldu.");
                        return;
                    }

                    // Execute APPEND with exact flags and keyword
                    var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { keyword };
                    uint appendedUid;
                    try
                    {
                        appendedUid = await targetClient.AppendMessageAsync(
                            item.TargetFolder,
                            mimeMsg,
                            MessageFlags.None,
                            keywords,
                            item.PlannedInternalDateUtc,
                            ct);
                    }
                    catch (Exception ex)
                    {
                        entry.ErrorMessage = $"Hedef IMAP sunucusuna ileti ekleme (APPEND) işlemi başarısız oldu: {ex.Message}";
                        _journal.UpdateImportEntry(jobRecord.JobId, entry);
                        failedCount++;
                        jobRecord.Status = "interrupted";
                        jobRecord.Stage = "Bağlantı kesildi; devam etmeden önce hedef uzlaştırılacak";
                        jobRecord.ErrorMessage = entry.ErrorMessage;
                        _saveJobRecord(jobRecord);
                        return;
                    }

                    // Independent post-append verification
                    var targetVerify = await targetClient.FetchAndVerifyAsync(item.TargetFolder, appendedUid, ct);
                    var searchMatches = await targetClient.SearchByKeywordAsync(item.TargetFolder, keyword, ct);
                    uint postAppendValidity = await targetClient.GetFolderUidValidityAsync(item.TargetFolder, ct);

                    bool postShaMatch = targetVerify.Exists && string.Equals(targetVerify.RawSha256, canonical.CanonicalSha256, StringComparison.OrdinalIgnoreCase);
                    bool postKeywordMatch = searchMatches.Count == 1 && searchMatches[0] == appendedUid;
                    bool postValidityMatch = postAppendValidity == targetValidity;
                    bool postDateMatch = targetVerify.InternalDateUtc.HasValue && targetVerify.InternalDateUtc.Value == item.PlannedInternalDateUtc;
                    bool postFlagsMatch = (targetVerify.Flags & ~MessageFlags.Recent) == MessageFlags.None;
                    bool postTokenMatch = targetVerify.Keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase);

                    if (!postShaMatch || !postKeywordMatch || !postValidityMatch || !postDateMatch || !postFlagsMatch || !postTokenMatch)
                    {
                        entry.Status = BridgeItemStatus.Failed;
                        entry.TargetUid = appendedUid;
                        entry.VerifiedSha256 = targetVerify.RawSha256;
                        entry.ErrorMessage = "Hedef doğrulama başarısız oldu (meta veri veya içerik uyumsuzluğu).";
                        _journal.UpdateImportEntry(jobRecord.JobId, entry);
                        failedCount++;
                        FailJob(jobRecord, entry.ErrorMessage);
                        return;
                    }

                    // Transition to Verified
                    entry.Status = BridgeItemStatus.Verified;
                    entry.TargetUid = appendedUid;
                    entry.VerifiedSha256 = targetVerify.RawSha256;
                    entry.TargetInternalDateUtc = targetVerify.InternalDateUtc;
                    entry.TargetFlags = new List<string> { targetVerify.Flags.ToString() };
                    entry.ErrorMessage = null;

                    try
                    {
                        _journal.UpdateImportEntry(jobRecord.JobId, entry);
                    }
                    catch (Exception)
                    {
                        FailJob(jobRecord, $"Doğrulama sonrası günlüğe yazılamadı. Hedef UID ({appendedUid}) korundu.");
                        return;
                    }

                    verifiedCount++;
                    processedCount++;
                    UpdateJobProgress(jobRecord, verifiedCount, failedCount, plan.Items.Count);
                }
            }

            // Final revalidation of all source entries
            if (mimeEntry?.Manifest != null)
            {
                _inspector.RevalidateManifest(mimeEntry.Manifest);
                if (plan.QualificationFingerprint is not null)
                {
                    var finalQualification = new NormalizedSourceQualificationReader().Read(mimeEntry.Manifest, ct);
                    if (!string.Equals(finalQualification?.Fingerprint, plan.QualificationFingerprint, StringComparison.Ordinal))
                    { FailJob(jobRecord, "[BÜTÜNLÜK ENGELİ] Kaynak qualification bilgisi son doğrulamada değişti."); return; }
                }
            }
            else
            {
                foreach (var item in plan.Items)
                {
                    string path = plan.SourceKind == "mbox"
                        ? (plan.SourceRootPath ?? item.SourceCanonicalPath)
                        : item.SourceCanonicalPath;

                    if (!File.Exists(path))
                    {
                        FailJob(jobRecord, $"Nihai doğrulamada kaynak dosya bulunamadı: {path}");
                        return;
                    }
                }
            }

            // Progress is separate from transferred items; integrity checks below are unchanged.
            jobRecord.Status = "verifying";
            jobRecord.ProgressPhase = "verifying";
            jobRecord.PhaseCompleted = 0;
            jobRecord.PhaseTotal = plan.Items.Count;
            jobRecord.Stage = "Son doğrulama yapılıyor";
            _saveJobRecord(jobRecord);
            var phaseClock = System.Diagnostics.Stopwatch.StartNew();
            // Recheck every destination at completion
            foreach (var item in plan.Items)
            {
                var entry = journalState.Entries[item.ItemId];
                if (entry.Status != BridgeItemStatus.Verified || !entry.TargetUid.HasValue || !entry.TargetUidValidity.HasValue || string.IsNullOrEmpty(entry.BitigMailKeyword))
                {
                    FailJob(jobRecord, "Aktarımın tüm hedef kayıtları doğrulanamadı.");
                    return;
                }
                var verification = await targetClient.VerifyTargetItemAsync(item.TargetFolder, entry.TargetUid.Value, entry.BitigMailKeyword, ct);
                var value = verification.Message;
                var tokens = verification.MatchingKeywordUids;
                if (verification.UidValidity != entry.TargetUidValidity.Value || !value.Exists || value.RawSha256 != item.CanonicalSha256 ||
                    value.InternalDateUtc != item.PlannedInternalDateUtc || (value.Flags & ~MessageFlags.Recent) != MessageFlags.None ||
                    !value.Keywords.Contains(entry.BitigMailKeyword, StringComparer.OrdinalIgnoreCase) || tokens.Count != 1 || tokens[0] != entry.TargetUid.Value)
                {
                    FailJob(jobRecord, "Aktarım sonrasında hedef ileti içeriği veya özellikleri değişti.");
                    return;
                }
                jobRecord.PhaseCompleted++;
                if (phaseClock.ElapsedMilliseconds >= 750)
                {
                    _saveJobRecord(jobRecord);
                    phaseClock.Restart();
                }
            }

            _saveJobRecord(jobRecord);
            // Build audit report detail
            var auditItems = new List<BridgeAuditItem>();
            foreach (var item in plan.Items)
            {
                if (journalState.Entries.TryGetValue(item.ItemId, out var entry))
                {
                    auditItems.Add(new BridgeAuditItem
                    {
                        ItemId = item.ItemId,
                        Status = entry.Status.ToString(),
                        SourceIdentity = item.SourceRelativePath,
                        TargetIdentity = $"{item.TargetFolder} (UID: {entry.TargetUid})",
                        SourceSha256 = item.SourceSha256,
                        VerifiedSha256 = entry.VerifiedSha256,
                        OriginalMimeDateUtc = item.OriginalMimeDateUtc,
                        InternalDateUtc = entry.TargetInternalDateUtc ?? item.PlannedInternalDateUtc,
                        BitigMailKeyword = entry.BitigMailKeyword,
                        ConvertedLfToCrLf = item.ConvertedLfToCrLf,
                        AddedTerminalNewline = item.AddedTerminalNewline,
                        ErrorMessage = entry.ErrorMessage
                    });
                }
            }

            var reportDetail = new BridgeTransferReportDetail
            {
                AdvancedFilterCanonicalJson = plan.AdvancedFilterCanonicalJson,
                AdvancedFilterFingerprint = plan.AdvancedFilterFingerprint,
                AdvancedFilterUnknownCount = plan.AdvancedFilterUnknownCount,
                JobId = jobRecord.JobId,
                PlanId = plan.PlanId,
                Direction = "import",
                SourceIdentifier = plan.SourceDisplayName ?? plan.SourceHandle,
                TargetIdentifier = plan.TargetDisplayName ?? plan.TargetAccountId,
                StartDate = plan.StartDate,
                EndDate = plan.EndDate,
                Folders = plan.Preview?.Folders ?? new(),
                TotalPlanned = plan.Items.Count,
                TotalVerified = verifiedCount,
                TotalFailed = failedCount,
                TotalNeedsAttention = needsAttentionCount,
                Items = auditItems
                ,QualificationWarnings = plan.QualificationWarnings
            };

            bool isFullSuccess = (verifiedCount == plan.Items.Count) && (failedCount == 0) && (needsAttentionCount == 0);

            var report = new ConversionReport
            {
                JobId = jobRecord.JobId,
                JobKind = "bridge-import",
                EvidenceLabel = "FILE_ACCOUNT_BRIDGE_IMPORT (MailKit APPEND/FETCH Verification)",
                ClientContext = jobRecord.ClientContext,
                SourceFileName = plan.SourceDisplayName ?? plan.SourceHandle,
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
                BridgeTransfer = reportDetail,
                ReopenedPstVerification = new ReopenedPstVerificationInfo
                {
                    VerificationSuccess = false,
                    VerifiedWith = "N/A (File/Account Bridge: MailKit APPEND/FETCH exact byte SHA-256)",
                    VerificationNotes = new List<string> { "PST verification is not applicable for bridge jobs." }
                },
                TrialDifferences = new TrialDifferencesInfo
                {
                    HasObservedTrialModifications = false,
                    ObservedSummary = "N/A (EML/MBOX Bridge does not use Aspose or evaluation limits)."
                }
            };

            if (targetResolved?.CommitAsync != null) await targetResolved.CommitAsync();

            // Save report BEFORE marking job as completed
            _saveReport(report);

            jobRecord.BridgeTransfer = reportDetail;
            jobRecord.ItemsRead = plan.Items.Count;
            jobRecord.ItemsWritten = verifiedCount;
            jobRecord.FailedItems = failedCount;
            jobRecord.TotalItems = plan.Items.Count;
            jobRecord.PercentComplete = 100;
            jobRecord.CompletedAt = DateTimeOffset.UtcNow;

            if (isFullSuccess)
            {
                jobRecord.Status = "completed";
                jobRecord.Stage = "İçe Aktarım Başarıyla Tamamlandı";
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
                jobRecord.Stage = "Aktarım Başarısız Oldu";
            }

            _saveJobRecord(jobRecord);
        }
        catch (OperationCanceledException)
        {
            jobRecord.Status = "interrupted";
            jobRecord.Stage = "İptal Edildi";
            jobRecord.ErrorMessage = "Kullanıcı tarafından iptal edildi. Kaynak ve kısmi aktarım korundu.";
            _saveJobRecord(jobRecord);
        }
        catch (Exception ex)
        {
            FailJob(jobRecord, $"Aktarım yürütülürken hata oluştu: {ex.Message}");
        }
    }

    private void FailJob(LocalJobRecord jobRecord, string message)
    {
        jobRecord.Status = "failed";
        jobRecord.Stage = "Hata";
        jobRecord.ErrorMessage = message;
        jobRecord.CompletedAt = DateTimeOffset.UtcNow;
        _saveJobRecord(jobRecord);
    }

    private void UpdateJobProgress(LocalJobRecord jobRecord, int verified, int failed, int total)
    {
        jobRecord.ItemsWritten = verified;
        jobRecord.FailedItems = failed;
        jobRecord.TotalItems = total;
        jobRecord.PercentComplete = total > 0 ? (int)((double)(verified + failed) / total * 100) : 0;
        _saveJobRecord(jobRecord);
    }
}
