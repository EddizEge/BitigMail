using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Bridge;
using BitigMail.Engine.Imap.Transfer;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.Imap.Transfer;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Security;

namespace BitigMail.LocalHost.Bridge.Transfer;

/// <summary>
/// Worker executing IMAP -> File export with read-only EXAMINE mode,
/// unique new job output directory, safe collision-resistant paths,
/// atomic temp+flush+no-overwrite publishing, per-folder staged MBOXRD writing,
/// and complete sidecar manifest generation.
/// </summary>
public sealed class BridgeExportWorker
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static async Task<(string Hash, long Length)> HashFileAsync(string path, CancellationToken ct)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        long length = stream.Length;
        byte[] hash = await SHA256.HashDataAsync(stream, ct);
        return (Convert.ToHexString(hash).ToLowerInvariant(), length);
    }
    private readonly FileHandleRegistry _handleRegistry;
    private readonly ImapAccountStore _accountStore;
    private readonly IImapTransferClientFactory _clientFactory;
    private readonly BridgeTransferJournal _journal;
    private readonly Action<LocalJobRecord> _saveJobRecord;
    private readonly Action<ConversionReport> _saveReport;
    private readonly IImapCredentialResolver? _credentialResolver;
    private readonly IDiskCapacityProbe _capacityProbe;

    public BridgeExportWorker(
        FileHandleRegistry handleRegistry,
        ImapAccountStore accountStore,
        IImapTransferClientFactory clientFactory,
        BridgeTransferJournal journal,
        Action<LocalJobRecord> saveJobRecord,
        Action<ConversionReport> saveReport,
        IImapCredentialResolver? credentialResolver = null,
        IDiskCapacityProbe? capacityProbe = null)
    {
        _handleRegistry = handleRegistry ?? throw new ArgumentNullException(nameof(handleRegistry));
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _saveJobRecord = saveJobRecord ?? throw new ArgumentNullException(nameof(saveJobRecord));
        _saveReport = saveReport ?? throw new ArgumentNullException(nameof(saveReport));
        _credentialResolver = credentialResolver;
        _capacityProbe = capacityProbe ?? new WindowsDiskCapacityProbe();
    }

    public async Task ExecuteAsync(
        LocalJobRecord jobRecord,
        BridgeExportPlan plan,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(jobRecord);
        ArgumentNullException.ThrowIfNull(plan);

        CompiledMailFilter? advanced;
        try { advanced = FrozenMailFilter.Validate(plan.AdvancedFilterCanonicalJson, plan.AdvancedFilterFingerprint, plan.AdvancedFilterUnknownCount); }
        catch (InvalidDataException) { FailJob(jobRecord, "Kalıcı gelişmiş filtre doğrulanamadı."); return; }
        jobRecord.AdvancedFilterCanonicalJson = plan.AdvancedFilterCanonicalJson;
        jobRecord.AdvancedFilterFingerprint = plan.AdvancedFilterFingerprint;
        jobRecord.AdvancedFilterUnknownCount = plan.AdvancedFilterUnknownCount;

        bool wasStarted = jobRecord.StartedAt != null || jobRecord.Status == "interrupted";

        jobRecord.Status = "converting";
        jobRecord.Stage = "Dışa Aktarım Başlatılıyor";
        jobRecord.ProgressPhase = "transferring";
        jobRecord.PhaseCompleted = null;
        jobRecord.PhaseTotal = null;
        jobRecord.StartedAt = DateTimeOffset.UtcNow;

        string? parentDir = _handleRegistry.GetOutputDirPath(plan.TargetDirHandle) ?? plan.TargetDirectoryPath;
        if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
        {
            FailJob(jobRecord, "Hedef üst klasör bulunamadı veya erişilemez durumda.");
            return;
        }

        var parentInfo = new DirectoryInfo(parentDir);
        if (parentInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            FailJob(jobRecord, "Hedef üst klasör reparse point / symlink içeriyor. Güvenlik gereği işlem durduruldu.");
            return;
        }

        if (plan.EstimatedRequiredBytes.HasValue)
        {
            long required = plan.EstimatedRequiredBytes.Value;
            if (required < 0)
            {
                FailJob(jobRecord, "[DİSK KAPASİTESİ ENGELİ] Plandaki tahmini gereken alan geçersiz. Hiçbir çıktı oluşturulmadı.");
                return;
            }

            var capacity = _capacityProbe.Probe(parentDir);
            if (!capacity.IsAvailable || !capacity.AvailableBytes.HasValue)
            {
                FailJob(jobRecord, $"[DİSK KAPASİTESİ ENGELİ] Çalıştırma öncesinde kullanılabilir disk alanı doğrulanamadı. Hiçbir çıktı oluşturulmadı. {capacity.Error}".Trim());
                return;
            }
            if (capacity.AvailableBytes.Value < required)
            {
                FailJob(jobRecord, $"[DİSK KAPASİTESİ ENGELİ] Tahmini gereken alan {required} bayt, kullanılabilir alan {capacity.AvailableBytes.Value} bayt. Hiçbir çıktı oluşturulmadı; mevcut veriler silinmedi.");
                return;
            }
        }
        else
        {
            jobRecord.Stage = "Eski plan: disk kapasitesi tahmini bulunmuyor";
            _saveJobRecord(jobRecord);
        }

        // Unique new job output directory below parent directory
        string jobOutputDir = Path.Combine(parentDir, $"bridge-export-{jobRecord.JobId}");

        // Load or initialize durable journal
        var journalState = _journal.GetExportJournal(jobRecord.JobId);
        if (journalState == null)
        {
            if (wasStarted)
            {
                FailJob(jobRecord, "Aktarım günlüğü bulunamadı veya hasarlı. Kesintiye uğrayan iş devam ettirilemez.");
                return;
            }
            Directory.CreateDirectory(jobOutputDir);
            jobRecord.OutputPath = jobOutputDir;
            jobRecord.OutputDirectoryPath = jobOutputDir;
            _saveJobRecord(jobRecord);
            journalState = _journal.InitializeExportJournal(jobRecord.JobId, plan, jobOutputDir);
        }
        else if (!string.Equals(journalState.PlanId, plan.PlanId, StringComparison.Ordinal))
        {
            FailJob(jobRecord, "Aktarım günlüğündeki plan bilgisi geçerli plan ile uyuşmuyor.");
            return;
        }
        else
        {
            jobOutputDir = journalState.JobOutputDir;
            string canonicalParent = Path.GetFullPath(parentDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string expectedJobDir = Path.GetFullPath(Path.Combine(canonicalParent, $"bridge-export-{jobRecord.JobId}")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string actualJobDir = Path.GetFullPath(jobOutputDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Export JobOutputDir must exactly equal canonical Path.Combine(plan.TargetDirectoryPath, "bridge-export-" + jobId),
            // not merely be under parent; sibling directory tamper rejected.
            if (!string.Equals(actualJobDir, expectedJobDir, StringComparison.OrdinalIgnoreCase))
            {
                FailJob(jobRecord, "Aktarım günlüğündeki çıktı dizini beklenen aktarım dizini ile uyuşmuyor (dizin tahrifatı engeli / güvenlik ihlali).");
                return;
            }

            var dirInfo = new DirectoryInfo(actualJobDir);
            if (dirInfo.Exists && dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                FailJob(jobRecord, "Çıktı dizini reparse point / symlink içeriyor. Güvenlik gereği işlem durduruldu.");
                return;
            }
            if (!Directory.Exists(jobOutputDir))
            {
                Directory.CreateDirectory(jobOutputDir);
            }
            jobRecord.OutputPath = jobOutputDir;
            jobRecord.OutputDirectoryPath = jobOutputDir;
            _saveJobRecord(jobRecord);
        }

        // Resolve source credentials and connect in read-only mode
        ResolvedImapCredential? sourceResolved = null;
        ImapAccountRecord sourceRecord;
        ImapConnectionCredential sourceCredential;

        try
        {
            if (_credentialResolver != null)
            {
                sourceResolved = await _credentialResolver.ResolveCredentialAsync(
                    plan.SourceAccountId, plan.CompanyId, plan.ProjectId, ct);
                sourceRecord = sourceResolved.Account;
                sourceCredential = sourceResolved.Credential;
            }
            else
            {
                var (sRec, sPwd) = _accountStore.GetInternalAccountWithPassword(
                    plan.SourceAccountId, plan.CompanyId, plan.ProjectId);
                sourceRecord = sRec;
                sourceCredential = new ImapPasswordCredential(sRec.Username, sPwd, sRec.AllowUnencryptedConnection);
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

        if (sourceRecord.Version != plan.SourceAccountVersion)
        {
            FailJob(jobRecord, "Kaynak hesap sürümü önizleme sonrasında değişti. Bütünlüğü korumak için aktarım engellendi.");
            return;
        }

        using var sourceClient = _clientFactory.CreateClient();
        try
        {
            await sourceClient.ConnectAndAuthenticateAsync(
                sourceRecord.Host, sourceRecord.Port, sourceRecord.TlsMode, sourceCredential, ct);
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
            // Verify UIDVALIDITY on all source folders before any export
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

            int verifiedCount = 0;
            int failedCount = 0;
            int needsAttentionCount = 0;

            if (plan.TargetFormat == "mboxrd")
            {
                // MBOXRD Export: stage raw items first, then construct per-folder temp mbox via root writer
                var itemsByFolder = plan.Items.GroupBy(i => i.FolderKey, StringComparer.Ordinal).ToList();

                foreach (var folderGroup in itemsByFolder)
                {
                    ct.ThrowIfCancellationRequested();
                    string folderKey = folderGroup.Key;
                    string stagingDir = Path.Combine(jobOutputDir, ".staging", folderKey);
                    Directory.CreateDirectory(stagingDir);

                    string finalMboxPath = Path.Combine(jobOutputDir, $"{folderKey}.mbox");
                    bool folderAllVerified = folderGroup.All(i => journalState.Entries[i.ItemId].Status == BridgeItemStatus.Verified);

                    if (folderAllVerified && File.Exists(finalMboxPath))
                    {
                        verifiedCount += folderGroup.Count();
                        UpdateJobProgress(jobRecord, verifiedCount, failedCount, plan.Items.Count);
                        continue;
                    }

                    // Stage raw items
                    var stagedEntries = new List<(BridgeExportPlannedItem Item, string StagedPath)>();

                    foreach (var item in folderGroup)
                    {
                        ct.ThrowIfCancellationRequested();
                        jobRecord.CurrentFolder = item.SourceFolder;
                        jobRecord.Stage = $"Aşama 1: İleti indiriliyor ({item.SourceFolder}, UID: {item.SourceUid})";
                        _saveJobRecord(jobRecord);

                        var entry = journalState.Entries[item.ItemId];
                        string stagedPath = Path.Combine(stagingDir, $"msg_{item.SourceUid:D8}.raw");

                        if (File.Exists(stagedPath))
                        {
                            var (stagedHash, _) = await HashFileAsync(stagedPath, ct);
                            if (!string.Equals(stagedHash, item.SourceSha256, StringComparison.OrdinalIgnoreCase))
                            {
                                FailJob(jobRecord, $"Hazırlık dosyasının (staging) hash değeri uyuşmuyor: {stagedPath}");
                                return;
                            }
                        }
                        else
                        {
                            var msgSummary = await sourceClient.FetchSingleSourceMessageAsync(item.SourceFolder, item.SourceUid, ct);
                            if (msgSummary == null)
                            {
                                FailJob(jobRecord, $"Kaynak ileti bulunamadı ({item.SourceFolder}, UID: {item.SourceUid}).");
                                return;
                            }

                            using var fetchedMessage = msgSummary.Message;
                    if (advanced is not null && MimeAdvancedFilterAdapter.Evaluate(advanced, fetchedMessage, msgSummary.RawBytes.LongLength) != MailFilterMatch.Match)
                    { FailJob(jobRecord, "Kaynak ileti donmuş gelişmiş filtreye uymuyor."); return; }

                            if (!string.Equals(msgSummary.RawSha256, item.SourceSha256, StringComparison.OrdinalIgnoreCase))
                            {
                                FailJob(jobRecord, $"Kaynak ileti içeriği ({item.SourceFolder}, UID: {item.SourceUid}) önizleme sonrasında değişti.");
                                return;
                            }

                            // Atomic write to staging file
                            string tempStaged = stagedPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                            try
                            {
                                using (var fs = new FileStream(tempStaged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                                {
                                    await fs.WriteAsync(msgSummary.RawBytes, ct);
                                    fs.Flush(true);
                                }
                                File.Move(tempStaged, stagedPath, overwrite: false);
                            }
                            finally
                            {
                                try { File.Delete(tempStaged); } catch { }
                            }

                            entry.Status = BridgeItemStatus.Staged;
                            _journal.UpdateExportEntry(jobRecord.JobId, entry);
                        }

                        stagedEntries.Add((item, stagedPath));
                    }

                    // Construct per-folder MBOX using root writer: BridgeMimeBytePolicy.WriteMboxrdRecord
                    jobRecord.Stage = $"Aşama 2: MBOX oluşturuluyor ({folderKey}.mbox)";
                    _saveJobRecord(jobRecord);

                    string tempMboxPath = finalMboxPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        using (var mboxFs = new FileStream(tempMboxPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            foreach (var (item, stagedPath) in stagedEntries)
                            {
                                ct.ThrowIfCancellationRequested();

                                if (!File.Exists(stagedPath))
                                {
                                    FailJob(jobRecord, $"Hazırlık dosyası (staging) bulunamadı: {stagedPath}");
                                    return;
                                }

                                byte[] stagedBytes = await File.ReadAllBytesAsync(stagedPath, ct);
                                using (var sha = SHA256.Create())
                                {
                                    string stagedHash = Convert.ToHexString(sha.ComputeHash(stagedBytes)).ToLowerInvariant();
                                    if (!string.Equals(stagedHash, item.SourceSha256, StringComparison.OrdinalIgnoreCase))
                                    {
                                        FailJob(jobRecord, $"Hazırlık dosyasının (staging) hash değeri montaj öncesinde uyuşmuyor: {stagedPath}");
                                        return;
                                    }
                                }

                                DateTimeOffset envelopeDate = item.OriginalMimeDateUtc ?? item.InternalDateUtc;
                                var recordInfo = BridgeMimeBytePolicy.WriteMboxrdRecord(mboxFs, stagedBytes, envelopeDate);

                                var entry = journalState.Entries[item.ItemId];
                                entry.Status = BridgeItemStatus.Verified;
                                entry.VerifiedSha256 = recordInfo.StoredSha256;
                                entry.OutputLength = recordInfo.OriginalLength;
                                entry.AddedTerminalNewline = recordInfo.AddedTerminalNewline;
                                entry.ErrorMessage = null;
                            }
                            mboxFs.Flush(true);
                        }

                        File.Move(tempMboxPath, finalMboxPath, overwrite: false);

                        // Durably update all entries for this folder in journal
                        foreach (var (item, _) in stagedEntries)
                        {
                            var entry = journalState.Entries[item.ItemId];
                            _journal.UpdateExportEntry(jobRecord.JobId, entry);
                        }

                        // Clean up staging directory
                        try { Directory.Delete(stagingDir, true); } catch { }
                        try
                        {
                            string parentStaging = Path.Combine(jobOutputDir, ".staging");
                            if (Directory.Exists(parentStaging) && !Directory.EnumerateFileSystemEntries(parentStaging).Any())
                            {
                                Directory.Delete(parentStaging);
                            }
                        }
                        catch { }

                        verifiedCount += stagedEntries.Count;
                        UpdateJobProgress(jobRecord, verifiedCount, failedCount, plan.Items.Count);
                    }
                    finally
                    {
                        try { File.Delete(tempMboxPath); } catch { }
                    }
                }
            }
            else
            {
                // EML-tree Export: atomic per-item CreateNew temp + Flush(true) + move
                foreach (var item in plan.Items)
                {
                    ct.ThrowIfCancellationRequested();

                    var entry = journalState.Entries[item.ItemId];
                    string finalPath = Path.Combine(jobOutputDir, item.RelativeOutputPath);

                    jobRecord.CurrentFolder = item.SourceFolder;
                    jobRecord.Stage = $"Dışa aktarılıyor: {item.SourceFolder} (UID: {item.SourceUid})";
                    _saveJobRecord(jobRecord);

                    if (entry.Status == BridgeItemStatus.Verified)
                    {
                        if (!File.Exists(finalPath))
                        {
                            entry.Status = BridgeItemStatus.NeedsAttention;
                            entry.ErrorMessage = "Daha önce doğrulanmış EML dosyası diskte bulunamadı.";
                            _journal.UpdateExportEntry(jobRecord.JobId, entry);
                            needsAttentionCount++;
                            FailJob(jobRecord, entry.ErrorMessage);
                            return;
                        }

                        var (existingHash, _) = await HashFileAsync(finalPath, ct);
                        if (!string.Equals(existingHash, item.SourceSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            entry.Status = BridgeItemStatus.NeedsAttention;
                            entry.ErrorMessage = "Doğrulanmış EML dosyasının içeriği diskte değiştirilmiş.";
                            _journal.UpdateExportEntry(jobRecord.JobId, entry);
                            needsAttentionCount++;
                            FailJob(jobRecord, entry.ErrorMessage);
                            return;
                        }

                        verifiedCount++;
                        UpdateJobProgress(jobRecord, verifiedCount, failedCount, plan.Items.Count);
                        continue;
                    }

                    // Check if target file already exists
                    if (File.Exists(finalPath))
                    {
                        var (existingHash, existingLength) = await HashFileAsync(finalPath, ct);
                        if (string.Equals(existingHash, item.SourceSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            entry.Status = BridgeItemStatus.Verified;
                            entry.VerifiedSha256 = existingHash;
                            entry.OutputLength = existingLength;
                            _journal.UpdateExportEntry(jobRecord.JobId, entry);
                            verifiedCount++;
                            UpdateJobProgress(jobRecord, verifiedCount, failedCount, plan.Items.Count);
                            continue;
                        }
                        else
                        {
                            FailJob(jobRecord, $"Hedef dosya mevcut ve içeriği kaynak ile uyuşmuyor: {finalPath}");
                            return;
                        }
                    }

                    // Fetch single message from IMAP
                    var msg = await sourceClient.FetchSingleSourceMessageAsync(item.SourceFolder, item.SourceUid, ct);
                    if (msg == null)
                    {
                        FailJob(jobRecord, $"Kaynak ileti sunucuda bulunamadı: {item.SourceFolder} (UID: {item.SourceUid})");
                        return;
                    }


                    using var fetchedMessage = msg.Message;
                    if (advanced is not null && MimeAdvancedFilterAdapter.Evaluate(advanced, fetchedMessage, msg.RawBytes.LongLength) != MailFilterMatch.Match)
                    { FailJob(jobRecord, "Kaynak ileti donmuş gelişmiş filtreye uymuyor."); return; }

                    if (!string.Equals(msg.RawSha256, item.SourceSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        FailJob(jobRecord, $"Kaynak ileti içeriği değişti: {item.SourceFolder} (UID: {item.SourceUid})");
                        return;
                    }

                    string folderDir = Path.GetDirectoryName(finalPath)!;
                    Directory.CreateDirectory(folderDir);

                    // Atomic write: CreateNew temp + Flush(true) + no-overwrite move
                    string tempPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            await fs.WriteAsync(msg.RawBytes, ct);
                            fs.Flush(true);
                        }
                        File.Move(tempPath, finalPath, overwrite: false);
                    }
                    finally
                    {
                        try { File.Delete(tempPath); } catch { }
                    }

                    entry.Status = BridgeItemStatus.Verified;
                    entry.VerifiedSha256 = msg.RawSha256;
                    entry.OutputLength = msg.RawBytes.Length;
                    entry.ErrorMessage = null;
                    _journal.UpdateExportEntry(jobRecord.JobId, entry);

                    verifiedCount++;
                    UpdateJobProgress(jobRecord, verifiedCount, failedCount, plan.Items.Count);
                }
            }

            jobRecord.Status = "verifying";
            jobRecord.ProgressPhase = "verifying";
            jobRecord.PhaseCompleted = null;
            jobRecord.PhaseTotal = null;
            jobRecord.Stage = "Kaynak ve çıktı kayıtları son kez doğrulanıyor";
            _saveJobRecord(jobRecord);
            // Final source revalidation
            foreach (var sf in distinctSourceFolders)
            {
                uint postValidity = await sourceClient.GetFolderUidValidityAsync(sf, ct);
                uint expectedValidity = plan.Items.First(i => ImapFolderPathComparer.Instance.Equals(i.SourceFolder, sf)).SourceUidValidity;
                if (postValidity != expectedValidity)
                {
                    FailJob(jobRecord, $"Dışa aktarım sonrasında kaynak klasör '{sf}' UIDVALIDITY değeri değişti.");
                    return;
                }
            }

            // Final destination revalidation: ensure every planned output exists and hash matches
            foreach (var item in plan.Items)
            {
                var entry = journalState.Entries[item.ItemId];
                if (entry.Status != BridgeItemStatus.Verified)
                {
                    FailJob(jobRecord, "Dışa aktarımın tüm kayıtları doğrulanamadı.");
                    return;
                }
            }

            // Build and persist sidecar manifest BEFORE job completes
            var manifestFolders = plan.FolderMappings.Select(fm =>
            {
                string outputTarget = plan.TargetFormat == "mboxrd"
                    ? $"{fm.FolderKey}.mbox"
                    : fm.FolderKey;
                int count = plan.Items.Count(i => i.FolderKey == fm.FolderKey);
                return new BridgeExportManifestFolder
                {
                    FolderKey = fm.FolderKey,
                    OriginalFolder = fm.OriginalFolder,
                    OutputTarget = outputTarget,
                    MessageCount = count
                };
            }).ToList();

            var manifestItems = plan.Items.Select(i =>
            {
                var entry = journalState.Entries[i.ItemId];
                return new BridgeExportManifestItem
                {
                    ItemId = i.ItemId,
                    OriginalFolder = i.SourceFolder,
                    SourceUid = i.SourceUid,
                    SourceUidValidity = i.SourceUidValidity,
                    SourceSha256 = i.SourceSha256,
                    OriginalLength = entry.OutputLength ?? 0,
                    StoredSha256 = entry.VerifiedSha256 ?? i.SourceSha256,
                    AddedTerminalNewline = entry.AddedTerminalNewline,
                    RelativeOutputPath = i.RelativeOutputPath,
                    InternalDateUtc = i.InternalDateUtc,
                    OriginalMimeDateUtc = i.OriginalMimeDateUtc,
                    Flags = i.Flags,
                    Keywords = i.Keywords
                };
            }).ToList();

            var sidecarManifest = new BridgeExportManifest
            {
                JobId = jobRecord.JobId,
                PlanId = plan.PlanId,
                CompanyId = plan.CompanyId,
                ProjectId = plan.ProjectId,
                CompanyName = plan.CompanyName,
                ProjectName = plan.ProjectName,
                SourceAccountId = plan.SourceAccountId,
                SourceAccountVersion = plan.SourceAccountVersion,
                TargetFormat = plan.TargetFormat,
                CreatedAtUtc = plan.CreatedAtUtc,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Folders = manifestFolders,
                Items = manifestItems
            };

            string manifestPath = Path.Combine(jobOutputDir, "manifest.json");
            string tmpManifest = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var fs = new FileStream(tmpManifest, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(fs, sidecarManifest, JsonOptions, ct);
                    fs.Flush(true);
                }
                File.Move(tmpManifest, manifestPath, overwrite: false);
            }
            finally
            {
                try { File.Delete(tmpManifest); } catch { }
            }

            // Build audit report detail
            var auditItems = plan.Items.Select(i =>
            {
                var entry = journalState.Entries[i.ItemId];
                return new BridgeAuditItem
                {
                    ItemId = i.ItemId,
                    Status = entry.Status.ToString(),
                    SourceIdentity = $"{i.SourceFolder} (UID: {i.SourceUid})",
                    TargetIdentity = i.RelativeOutputPath,
                    SourceSha256 = i.SourceSha256,
                    VerifiedSha256 = entry.VerifiedSha256,
                    OriginalMimeDateUtc = i.OriginalMimeDateUtc,
                    InternalDateUtc = i.InternalDateUtc,
                    AddedTerminalNewline = entry.AddedTerminalNewline,
                    ErrorMessage = entry.ErrorMessage
                };
            }).ToList();

            var reportDetail = new BridgeTransferReportDetail
            {
                AdvancedFilterCanonicalJson = plan.AdvancedFilterCanonicalJson,
                AdvancedFilterFingerprint = plan.AdvancedFilterFingerprint,
                AdvancedFilterUnknownCount = plan.AdvancedFilterUnknownCount,
                JobId = jobRecord.JobId,
                PlanId = plan.PlanId,
                Direction = "export",
                SourceIdentifier = plan.SourceDisplayName ?? plan.SourceAccountId,
                TargetIdentifier = plan.TargetDisplayName ?? plan.TargetDirHandle,
                TargetFormat = plan.TargetFormat,
                OutputPath = jobOutputDir,
                StartDate = plan.StartDate,
                EndDate = plan.EndDate,
                Folders = plan.Preview?.Folders ?? new(),
                TotalPlanned = plan.Items.Count,
                TotalVerified = verifiedCount,
                TotalFailed = failedCount,
                TotalNeedsAttention = needsAttentionCount,
                Items = auditItems,
                SidecarManifestPath = manifestPath
            };

            bool isFullSuccess = (verifiedCount == plan.Items.Count) && (failedCount == 0) && (needsAttentionCount == 0);

            var report = new ConversionReport
            {
                JobId = jobRecord.JobId,
                JobKind = "bridge-export",
                EvidenceLabel = $"FILE_ACCOUNT_BRIDGE_EXPORT ({plan.TargetFormat.ToUpperInvariant()} Verification)",
                ClientContext = jobRecord.ClientContext,
                SourceFileName = plan.SourceDisplayName ?? plan.SourceAccountId,
                OutputPath = jobOutputDir,
                OutputDirectoryPath = jobOutputDir,
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
                    VerifiedWith = $"N/A (File/Account Bridge: {plan.TargetFormat})",
                    VerificationNotes = new List<string> { "PST verification is not applicable for bridge jobs." }
                },
                TrialDifferences = new TrialDifferencesInfo
                {
                    HasObservedTrialModifications = false,
                    ObservedSummary = "N/A (EML/MBOX Bridge does not use Aspose or evaluation limits)."
                }
            };

            if (sourceResolved?.CommitAsync != null) await sourceResolved.CommitAsync();

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
                jobRecord.Stage = "Dışa Aktarım Başarıyla Tamamlandı";
            }
            else if (needsAttentionCount > 0)
            {
                jobRecord.Status = "interrupted";
                jobRecord.Stage = "İnceleme Gerektiriyor (NeedsAttention)";
                jobRecord.ErrorMessage = $"Dışa aktarım sırasında {needsAttentionCount} kayıt inceleme gerektiren duruma geldi.";
            }
            else
            {
                jobRecord.Status = "failed";
                jobRecord.Stage = "Dışa Aktarım Başarısız Oldu";
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
            FailJob(jobRecord, $"Dışa aktarım yürütülürken hata oluştu: {ex.Message}");
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
