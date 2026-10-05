using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aspose.Email.Mapi;
using Aspose.Email.Storage.Pst;
using BitigMail.Engine.Models;

namespace BitigMail.Engine.Storage;

public class OstToPstConverter
{
    private readonly IDiskCapacityProbe _capacityProbe;

    public OstToPstConverter(IDiskCapacityProbe? capacityProbe = null)
    {
        _capacityProbe = capacityProbe ?? new WindowsDiskCapacityProbe();
    }
    private static readonly string[] SystemKeywords = new[]
    {
        "NON_IPM_SUBTREE", "EFORMS REGISTRY", "Kuruluş Formları", "Ortak Görünümler",
        "Bulucu", "Kısayollar", "Görünümler", "Eşitleme Sorunları", "Yerel Hatalar",
        "~MAPISP", "Drizzle", "Paylaşılan Veri", "Konuşma Eylemi Ayarları",
        "Hızlı Adım Ayarları", "RSS Akışları"
    };

    internal Action<string, List<SourceItemSnapshot>>? BeforeVerificationHook { get; set; }

    public Task<ConversionReport> ConvertAsync(
        string sourceOstPath,
        string targetPstPath,
        string jobId,
        ClientProjectContext clientContext,
        string? expectedSourceSha256 = null,
        IProgress<ConversionJobProgress>? progress = null,
        CancellationToken cancellationToken = default,
        RegisteredSelection? selection = null)
    {
        return Task.Run(() => Convert(sourceOstPath, targetPstPath, jobId, clientContext, expectedSourceSha256, progress, cancellationToken, selection), cancellationToken);
    }

    public ConversionReport Convert(
        string sourceOstPath,
        string targetPstPath,
        string jobId,
        ClientProjectContext clientContext,
        string? expectedSourceSha256 = null,
        IProgress<ConversionJobProgress>? progress = null,
        CancellationToken cancellationToken = default,
        RegisteredSelection? selection = null)
    {
        if (string.IsNullOrWhiteSpace(sourceOstPath) || !File.Exists(sourceOstPath))
        {
            throw new FileNotFoundException($"Kaynak OST dosyası bulunamadı: {sourceOstPath}", sourceOstPath);
        }

        if (string.IsNullOrWhiteSpace(targetPstPath))
        {
            throw new ArgumentException("Hedef PST dosya yolu belirtilmelidir.", nameof(targetPstPath));
        }

        string fullSourcePath = Path.GetFullPath(sourceOstPath);
        string fullTargetPath = Path.GetFullPath(targetPstPath);

        if (string.Equals(fullSourcePath, fullTargetPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Kaynak ve hedef dosya aynı olamaz.");
        }

        // Safeguard 1: Refuse if target PST already exists
        if (File.Exists(fullTargetPath))
        {
            throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Hedef PST dosyası zaten mevcut: '{fullTargetPath}'. Üzerine yazma kesinlikle reddedildi.");
        }

        if (selection != null)
        {
            if (!selection.CanConvert || selection.HasTrialBlocker || selection.HasPreflightBlocker)
            {
                string reason = selection.PreflightBlockerReason ?? selection.TrialBlockerReason ?? selection.BlockerReason ?? "Kaynak dosyada ön kontrol engeli bulunmaktadır. Dönüştürme başlatılamaz.";
                if (!reason.Contains("[ÖN KONTROL ENGELİ]"))
                {
                    reason = $"[ÖN KONTROL ENGELİ] {reason}";
                }
                throw new InvalidOperationException(reason);
            }

            if (selection.SelectedMessagesCount == 0)
            {
                throw new InvalidOperationException("Seçilen filtre kriterlerine uyan ileti bulunmadığından dönüştürme başlatılamaz.");
            }
        }

        // Open source with FileShare.Read and HOLD IT OPEN for the whole conversion.
        // This eliminates the hash-before-lock race condition and blocks external writers.
        using var ostStream = new FileStream(fullSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Safeguard 2: Authoritative format validation on the open stream
        OutlookStorageInspector.ValidateOstAuthoritatively(ostStream, fullSourcePath);

        // Safeguard 3: Source SHA-256 computed on the locked stream before conversion
        ostStream.Position = 0;
        using var shaBefore = SHA256.Create();
        string sourceShaBefore = System.Convert.ToHexString(shaBefore.ComputeHash(ostStream)).ToLowerInvariant();
        long sourceSizeBytes = ostStream.Length;
        ostStream.Position = 0;

        int capacityItems = selection?.SelectedMessagesCount ?? 0;
        long requiredBytes = DiskCapacityPlanning.EstimatePst(sourceSizeBytes, capacityItems);
        string? capacityBlocker = DiskCapacityPlanning.CapacityBlocker(requiredBytes, _capacityProbe.Probe(fullTargetPath));
        if (capacityBlocker != null)
            throw new InvalidOperationException(capacityBlocker + " Çıktı oluşturulmadı.");

        if (!string.IsNullOrEmpty(expectedSourceSha256) &&
            !string.Equals(sourceShaBefore, expectedSourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] Kaynak OST dosyası analizden sonra değiştirilmiş! Analiz hash'i: {expectedSourceSha256}, Güncel hash: {sourceShaBefore}. Yeniden analiz yapılmalıdır.");
        }

        if (selection != null)
        {
            if (!string.Equals(selection.SourceSha256, sourceShaBefore, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] Seçim SHA hash'i ({selection.SourceSha256}) geçerli kaynak OST hash'i ({sourceShaBefore}) ile uyuşmuyor.");
            }

            if (!selection.CanConvert || selection.HasTrialBlocker || selection.HasPreflightBlocker)
            {
                string reason = selection.PreflightBlockerReason ?? selection.TrialBlockerReason ?? selection.BlockerReason ?? "Kaynak dosyada ön kontrol engeli bulunmaktadır. Dönüştürme başlatılamaz.";
                if (!reason.Contains("[ÖN KONTROL ENGELİ]"))
                {
                    reason = $"[ÖN KONTROL ENGELİ] {reason}";
                }
                throw new InvalidOperationException(reason);
            }

            if (selection.SelectedMessagesCount == 0)
            {
                throw new InvalidOperationException("Seçilen filtre kriterlerine uyan ileti bulunmadığından dönüştürme başlatılamaz.");
            }
        }

        // Staging partial output in the same directory
        string targetDirectory = Path.GetDirectoryName(fullTargetPath) ?? Directory.GetCurrentDirectory();
        if (!Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        string partialPath = Path.Combine(targetDirectory, $"{Path.GetFileName(fullTargetPath)}.{jobId}.partial");
        if (File.Exists(partialPath))
        {
            File.Delete(partialPath);
        }

        var jobProgress = new ConversionJobProgress
        {
            JobId = jobId,
            Status = "converting",
            Stage = "Dönüştürülüyor",
            CreatedAt = DateTimeOffset.UtcNow,
            StartedAt = DateTimeOffset.UtcNow
        };
        progress?.Report(jobProgress);

        var stopwatch = Stopwatch.StartNew();
        int totalRead = 0;
        int totalWritten = 0;
        int failedItems = 0;
        int totalFoldersProcessed = 0;

        var sourceItemSnapshots = new List<SourceItemSnapshot>();
        var errors = new List<string>();
        var warnings = new List<string>();

        try
        {
            using (var ost = PersonalStorage.FromStream(new NonClosingStream(ostStream)))
            using (var pst = PersonalStorage.Create(partialPath, FileFormatVersion.Unicode))
            {
                string rootRawId = ost.RootFolder.EntryId != null
                    ? System.Convert.ToHexString(ost.RootFolder.EntryId)
                    : (ost.RootFolder.EntryIdString ?? "[Kök Klasör]");
                string rootFolderId = OstSelectionEngine.ComputeFolderId(sourceShaBefore, rootRawId);

                // 1. Process physical items in the root folder itself (root items cannot disappear)
                ProcessFolderItems(
                    ost,
                    ost.RootFolder,
                    pst.RootFolder,
                    folderPath: "[Kök Klasör]",
                    rootFolderId,
                    selection,
                    ref totalRead,
                    ref totalWritten,
                    ref failedItems,
                    sourceItemSnapshots,
                    cancellationToken);

                // 2. Process all subfolders recursively
                ProcessSubFoldersRecursive(
                    ost,
                    ost.RootFolder,
                    pst.RootFolder,
                    parentPath: "",
                    sourceShaBefore,
                    selection,
                    ref totalRead,
                    ref totalWritten,
                    ref failedItems,
                    ref totalFoldersProcessed,
                    sourceItemSnapshots,
                    errors,
                    warnings,
                    jobProgress,
                    progress,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            if (File.Exists(partialPath))
            {
                try { File.Delete(partialPath); } catch { }
            }
            throw new InvalidOperationException($"Dönüştürme sırasında hata oluştu: {ex.Message}", ex);
        }

        stopwatch.Stop();

        // Safeguard 4: Source SHA-256 recomputed before closing lock
        ostStream.Position = 0;
        using var shaAfter = SHA256.Create();
        string sourceShaAfter = System.Convert.ToHexString(shaAfter.ComputeHash(ostStream)).ToLowerInvariant();
        bool sourceHashMatch = string.Equals(sourceShaBefore, sourceShaAfter, StringComparison.OrdinalIgnoreCase);

        if (!sourceHashMatch)
        {
            if (File.Exists(partialPath))
            {
                try { File.Delete(partialPath); } catch { }
            }
            throw new InvalidOperationException("KRİTİK GÜVENLİK HATASI: Dönüştürme sırasında kaynak OST dosyası değişti!");
        }

        // Verification Stage
        jobProgress.Status = "verifying";
        jobProgress.Stage = "Doğrulanıyor";
        jobProgress.ItemsRead = totalRead;
        jobProgress.ItemsWritten = totalWritten;
        jobProgress.FailedItems = failedItems;
        progress?.Report(jobProgress);

        BeforeVerificationHook?.Invoke(partialPath, sourceItemSnapshots);
        var verificationInfo = VerifyReopenedPst(partialPath, sourceItemSnapshots);

        if (selection != null)
        {
            if (totalWritten != selection.SelectedMessagesCount)
            {
                verificationInfo.VerificationSuccess = false;
                verificationInfo.VerificationNotes.Add($"Seçilen ileti sayısı ({selection.SelectedMessagesCount}) ile yazılan ileti sayısı ({totalWritten}) uyuşmuyor.");
            }
            if (verificationInfo.TotalAttachmentsVerified != selection.SelectedAttachmentsCount)
            {
                verificationInfo.VerificationSuccess = false;
                verificationInfo.VerificationNotes.Add($"Seçilen ek sayısı ({selection.SelectedAttachmentsCount}) ile doğrulanan ek sayısı ({verificationInfo.TotalAttachmentsVerified}) uyuşmuyor.");
            }
        }

        bool conversionSuccess = sourceHashMatch &&
                                 failedItems == 0 &&
                                 (selection == null ? totalWritten == totalRead : totalWritten == selection.SelectedMessagesCount) &&
                                 verificationInfo.VerificationSuccess;

        string outputPstSha256 = string.Empty;
        long outputPstSize = 0;

        if (conversionSuccess)
        {
            // Safeguard 5: Atomic move to destination ONLY if verification accepted
            if (File.Exists(fullTargetPath))
            {
                throw new InvalidOperationException($"[RACE GÜVENLİK ENGELİ] Hedef dosya dönüştürme sürerken dışarıdan oluşturuldu: '{fullTargetPath}'. Hedef dosya korunarak işlem durduruldu.");
            }

            File.Move(partialPath, fullTargetPath, overwrite: false);
            outputPstSha256 = ComputeFileSha256(fullTargetPath);
            outputPstSize = new FileInfo(fullTargetPath).Length;
        }
        else
        {
            // Failure: DO NOT publish final PST! Partial file remains for diagnosis.
            errors.AddRange(verificationInfo.VerificationNotes);
            if (File.Exists(partialPath))
            {
                outputPstSha256 = ComputeFileSha256(partialPath);
                outputPstSize = new FileInfo(partialPath).Length;
            }
        }

        var report = new ConversionReport
        {
            JobId = jobId,
            ClientContext = clientContext,
            SourceFileName = Path.GetFileName(fullSourcePath),
            SourceSizeBytes = sourceSizeBytes,
            SourceSha256Before = sourceShaBefore,
            SourceSha256After = sourceShaAfter,
            SourceHashMatch = sourceHashMatch,
            OutputPstFileName = conversionSuccess ? Path.GetFileName(fullTargetPath) : Path.GetFileName(partialPath),
            OutputPath = conversionSuccess ? fullTargetPath : partialPath,
            OutputPstSizeBytes = outputPstSize,
            OutputPstSha256 = outputPstSha256,
            ConversionSuccess = conversionSuccess,
            ItemsRead = totalRead,
            ItemsWritten = totalWritten,
            FailedItems = failedItems,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            TotalFoldersProcessed = totalFoldersProcessed,
            FidelityStatus = (verificationInfo.VerificationNotes.Count > 0) ? "DIFFERENCES" : "PASS",
            OverallStatus = conversionSuccess ? "SUCCESS" : "FAILED",
            ReopenedPstVerification = verificationInfo,
            IsFiltered = selection != null,
            SelectionFilter = selection?.Filters,
            TotalSourceMessages = selection != null ? selection.TotalSourceMessages : totalRead,
            SelectedMessagesCount = selection != null ? selection.SelectedMessagesCount : totalRead,
            ExcludedMessagesCount = selection != null ? selection.ExcludedMessagesCount : 0,
            MissingDateExcludedCount = selection != null ? selection.MissingDateExcludedCount : 0,
            SelectedAttachmentsCount = selection != null ? selection.SelectedAttachmentsCount : verificationInfo.TotalAttachmentsVerified,
            SelectionId = selection?.SelectionId,
            Errors = errors,
            Warnings = warnings
        };

        return report;
    }

    internal void ProcessFolderItems(
        PersonalStorage ost,
        FolderInfo ostFolder,
        FolderInfo destFolder,
        string folderPath,
        ref int totalRead,
        ref int totalWritten,
        ref int failedItems,
        List<SourceItemSnapshot> sourceItemSnapshots,
        CancellationToken cancellationToken)
    {
        ProcessFolderItems(
            ost,
            ostFolder,
            destFolder,
            folderPath,
            folderId: "",
            selection: null,
            ref totalRead,
            ref totalWritten,
            ref failedItems,
            sourceItemSnapshots,
            cancellationToken);
    }

    internal void ProcessFolderItems(
        PersonalStorage ost,
        FolderInfo ostFolder,
        FolderInfo destFolder,
        string folderPath,
        string folderId,
        RegisteredSelection? selection,
        ref int totalRead,
        ref int totalWritten,
        ref int failedItems,
        List<SourceItemSnapshot> sourceItemSnapshots,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Enumerate messages - NO empty-list fallback on error
        IEnumerable<MessageInfo> enumerated;
        try
        {
            enumerated = ostFolder.EnumerateMessages();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"'{folderPath}' klasöründeki iletiler listelenemedi: {ex.Message}", ex);
        }

        var msgInfos = enumerated != null ? enumerated.ToList() : new List<MessageInfo>();

        // Independent 50-item evaluation limit enforcement
        if (msgInfos.Count > 50)
        {
            throw new InvalidOperationException($"[GÜVENLİK ENGELİ] '{folderPath}' klasöründe {msgInfos.Count} ileti var. Değerlendirme lisansı klasör başına en fazla 50 öğeye izin verir. Sessiz kırpma kesinlikle reddedildi.");
        }

        foreach (var msgInfo in msgInfos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalRead++;

            // If selection is provided, check if this physical message was selected
            if (selection != null)
            {
                string physicalKey = $"m_{folderId}_{msgInfo.EntryIdString}";
                if (!selection.SelectedMessageKeys.Contains(physicalKey))
                {
                    // Message excluded by selection filter
                    continue;
                }
            }

            using (MapiMessage? msg = ost.ExtractMessage(msgInfo))
            {
                if (msg == null)
                {
                    failedItems++;
                    throw new InvalidOperationException($"[BÜTÜNLÜK HATASI] '{folderPath}' klasöründeki {msgInfo.EntryIdString} öğesi okunamadı.");
                }

                // Take snapshot for reopen verification
                var snapshot = BuildItemSnapshot(msg, msgInfo, folderPath);
                sourceItemSnapshots.Add(snapshot);

                // Add to destination PST
                destFolder.AddMessage(msg);
                totalWritten++;
            }
        }
    }

    internal void ProcessSubFoldersRecursive(
        PersonalStorage ost,
        FolderInfo ostFolder,
        FolderInfo destFolder,
        string parentPath,
        ref int totalRead,
        ref int totalWritten,
        ref int failedItems,
        ref int totalFoldersProcessed,
        List<SourceItemSnapshot> sourceItemSnapshots,
        List<string> errors,
        List<string> warnings,
        ConversionJobProgress jobProgress,
        IProgress<ConversionJobProgress>? progress,
        CancellationToken cancellationToken)
    {
        ProcessSubFoldersRecursive(
            ost,
            ostFolder,
            destFolder,
            parentPath,
            sourceSha: "",
            selection: null,
            ref totalRead,
            ref totalWritten,
            ref failedItems,
            ref totalFoldersProcessed,
            sourceItemSnapshots,
            errors,
            warnings,
            jobProgress,
            progress,
            cancellationToken);
    }

    internal void ProcessSubFoldersRecursive(
        PersonalStorage ost,
        FolderInfo ostFolder,
        FolderInfo pstParentFolder,
        string parentPath,
        string sourceSha,
        RegisteredSelection? selection,
        ref int totalRead,
        ref int totalWritten,
        ref int failedItems,
        ref int totalFoldersProcessed,
        List<SourceItemSnapshot> sourceItemSnapshots,
        List<string> errors,
        List<string> warnings,
        ConversionJobProgress jobProgress,
        IProgress<ConversionJobProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        FolderInfoCollection? subFolders;
        try
        {
            subFolders = ostFolder.GetSubFolders();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"'{ostFolder.DisplayName}' alt klasörleri alınamadı: {ex.Message}", ex);
        }

        if (subFolders == null) return;

        foreach (FolderInfo subFolder in subFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string folderName = subFolder.DisplayName ?? "Adsız Klasör";
            string currentPath = string.IsNullOrEmpty(parentPath) ? folderName : $"{parentPath}/{folderName}";
            totalFoldersProcessed++;

            jobProgress.CurrentFolder = currentPath;
            progress?.Report(jobProgress);

            string rawEntryId = subFolder.EntryId != null
                ? System.Convert.ToHexString(subFolder.EntryId)
                : (subFolder.EntryIdString ?? currentPath);
            string subFolderId = OstSelectionEngine.ComputeFolderId(sourceSha, rawEntryId);

            FolderInfo destSubFolder;
            try
            {
                destSubFolder = pstParentFolder.AddSubFolder(folderName);
            }
            catch
            {
                destSubFolder = pstParentFolder.GetSubFolder(folderName);
            }

            // Process messages in this subfolder
            ProcessFolderItems(
                ost,
                subFolder,
                destSubFolder,
                currentPath,
                subFolderId,
                selection,
                ref totalRead,
                ref totalWritten,
                ref failedItems,
                sourceItemSnapshots,
                cancellationToken);

            jobProgress.ItemsRead = totalRead;
            jobProgress.ItemsWritten = totalWritten;
            jobProgress.FailedItems = failedItems;
            progress?.Report(jobProgress);

            // Recurse into children
            ProcessSubFoldersRecursive(
                ost,
                subFolder,
                destSubFolder,
                currentPath,
                sourceSha,
                selection,
                ref totalRead,
                ref totalWritten,
                ref failedItems,
                ref totalFoldersProcessed,
                sourceItemSnapshots,
                errors,
                warnings,
                jobProgress,
                progress,
                cancellationToken);
        }
    }

    internal static SourceItemSnapshot BuildItemSnapshot(MapiMessage msg, MessageInfo msgInfo, string currentPath)
    {
        DateTime? date = OstSelectionEngine.ExtractMessageDate(msg);
        var snapshot = new SourceItemSnapshot
        {
            EntryId = msgInfo.EntryIdString ?? string.Empty,
            FolderPath = currentPath,
            Subject = msg.Subject ?? string.Empty,
            Sender = msg.SenderEmailAddress ?? msg.SenderName ?? string.Empty,
            DisplayTo = msg.DisplayTo ?? string.Empty,
            MessageId = (msg.InternetMessageId ?? string.Empty).Trim(),
            DateUtc = date.HasValue ? date.Value.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty,
            NormalizedBodySha256 = ComputeNormalizedBodySha256(msg.Body),
            BodyLength = msg.Body?.Length ?? 0
        };

        if (msg.Attachments != null)
        {
            foreach (MapiAttachment att in msg.Attachments)
            {
                string attName = att.LongFileName ?? att.FileName ?? att.DisplayName ?? string.Empty;
                long attSize = att.BinaryData?.Length ?? 0;
                string attSha = string.Empty;
                if (att.BinaryData != null)
                {
                    using var sha = SHA256.Create();
                    attSha = System.Convert.ToHexString(sha.ComputeHash(att.BinaryData)).ToLowerInvariant();
                }

                string? cid = AttachmentHelper.GetAttachmentContentId(att);
                snapshot.Attachments.Add(new AttachmentSnapshot
                {
                    FileName = attName,
                    SizeBytes = attSize,
                    Sha256 = attSha,
                    ContentId = cid,
                    IsInline = att.IsInline
                });
            }
        }

        return snapshot;
    }

    internal static SourceItemSnapshot CaptureSourceSnapshot(MapiMessage msg, string folderPath, string folderId, string entryId)
    {
        DateTime? date = OstSelectionEngine.ExtractMessageDate(msg);
        var snapshot = new SourceItemSnapshot
        {
            EntryId = entryId,
            FolderPath = folderPath,
            Subject = msg.Subject ?? string.Empty,
            Sender = msg.SenderEmailAddress ?? msg.SenderName ?? string.Empty,
            DisplayTo = msg.DisplayTo ?? string.Empty,
            MessageId = (msg.InternetMessageId ?? string.Empty).Trim(),
            DateUtc = date.HasValue ? date.Value.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty,
            NormalizedBodySha256 = ComputeNormalizedBodySha256(msg.Body),
            BodyLength = msg.Body?.Length ?? 0
        };

        if (msg.Attachments != null)
        {
            foreach (MapiAttachment att in msg.Attachments)
            {
                string attName = att.LongFileName ?? att.FileName ?? att.DisplayName ?? string.Empty;
                long attSize = att.BinaryData?.Length ?? 0;
                string attSha = string.Empty;
                if (att.BinaryData != null)
                {
                    using var sha = SHA256.Create();
                    attSha = System.Convert.ToHexString(sha.ComputeHash(att.BinaryData)).ToLowerInvariant();
                }

                string? cid = AttachmentHelper.GetAttachmentContentId(att);
                snapshot.Attachments.Add(new AttachmentSnapshot
                {
                    FileName = attName,
                    SizeBytes = attSize,
                    Sha256 = attSha,
                    ContentId = cid,
                    IsInline = att.IsInline
                });
            }
        }

        return snapshot;
    }

    internal ReopenedPstVerificationInfo VerifyReopenedPst(string partialPstPath, List<SourceItemSnapshot> sourceSnapshots)
    {
        var info = new ReopenedPstVerificationInfo();

        using (var stream = new FileStream(partialPstPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var pst = PersonalStorage.FromStream(stream))
        {
            var reopenedItems = new List<SourceItemSnapshot>();
            int activeFolders = 0;
            int emptyFolders = 0;
            int systemFolders = 0;

            // Traverse root folder items and all subfolders
            TraversePstForVerification(
                pst,
                pst.RootFolder,
                folderPath: "[Kök Klasör]",
                isRoot: true,
                reopenedItems,
                ref activeFolders,
                ref emptyFolders,
                ref systemFolders);

            info.TotalFoldersFound = activeFolders + emptyFolders + systemFolders;
            info.ActiveFoldersFound = activeFolders;
            info.EmptyFoldersFound = emptyFolders;
            info.SystemFoldersFound = systemFolders;
            info.TotalPhysicalItemsFound = reopenedItems.Count;
            info.ItemCountMatch = (reopenedItems.Count == sourceSnapshots.Count);

            if (!info.ItemCountMatch)
            {
                info.VerificationSuccess = false;
                info.VerificationNotes.Add($"Toplam öğe sayısı uyuşmazlığı: Kaynakta {sourceSnapshots.Count}, PST'de {reopenedItems.Count}.");
            }

            // Consuming physical multiset matching (never FirstOrDefault by folder/subject)
            var pool = new List<SourceItemSnapshot>(reopenedItems);
            int attVerified = 0;
            int cidVerified = 0;
            bool verificationPassed = info.ItemCountMatch;

            foreach (var src in sourceSnapshots)
            {
                // Find candidate from pool via prioritized consuming matching:
                SourceItemSnapshot? match = null;

                // 1. If Message-ID is non-empty, try exact Message-ID + Subject + DateUtc
                if (!string.IsNullOrEmpty(src.MessageId))
                {
                    match = pool.FirstOrDefault(p =>
                        string.Equals(p.FolderPath, src.FolderPath, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.MessageId, src.MessageId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.Subject, src.Subject, StringComparison.Ordinal) &&
                        string.Equals(p.DateUtc, src.DateUtc, StringComparison.Ordinal));

                    // 1b. Match by Message-ID + Subject
                    match ??= pool.FirstOrDefault(p =>
                        string.Equals(p.FolderPath, src.FolderPath, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.MessageId, src.MessageId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.Subject, src.Subject, StringComparison.Ordinal));

                    // 1c. Match by Message-ID
                    match ??= pool.FirstOrDefault(p =>
                        string.Equals(p.FolderPath, src.FolderPath, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.MessageId, src.MessageId, StringComparison.OrdinalIgnoreCase));
                }

                // 2. Exact match by Folder + Subject + DateUtc + Sender (critical for blank or duplicate MessageId)
                if (match == null)
                {
                    match = pool.FirstOrDefault(p =>
                        string.Equals(p.FolderPath, src.FolderPath, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.Subject, src.Subject, StringComparison.Ordinal) &&
                        string.Equals(p.DateUtc, src.DateUtc, StringComparison.Ordinal) &&
                        string.Equals(p.Sender, src.Sender, StringComparison.OrdinalIgnoreCase));
                }

                // 3. Match by Folder + Subject + DateUtc
                if (match == null)
                {
                    match = pool.FirstOrDefault(p =>
                        string.Equals(p.FolderPath, src.FolderPath, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.Subject, src.Subject, StringComparison.Ordinal) &&
                        string.Equals(p.DateUtc, src.DateUtc, StringComparison.Ordinal));
                }

                // 4. Fallback match by Folder + Subject
                if (match == null)
                {
                    match = pool.FirstOrDefault(p =>
                        string.Equals(p.FolderPath, src.FolderPath, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.Subject, src.Subject, StringComparison.Ordinal));
                }

                if (match == null)
                {
                    verificationPassed = false;
                    info.VerificationNotes.Add($"Eşleşen PST iletisi bulunamadı: '{src.Subject}' ({src.FolderPath})");
                    continue;
                }

                // CONSUME matched item from pool
                pool.Remove(match);

                // Compare basic fields
                if (!string.Equals(src.Subject, match.Subject, StringComparison.Ordinal))
                {
                    verificationPassed = false;
                    info.VerificationNotes.Add($"Konu uyuşmazlığı: Kaynak '{src.Subject}' != PST '{match.Subject}'");
                }
                if (!string.IsNullOrEmpty(src.MessageId) && !string.Equals(src.MessageId, match.MessageId, StringComparison.OrdinalIgnoreCase))
                {
                    verificationPassed = false;
                    info.VerificationNotes.Add($"Message-ID uyuşmazlığı: '{src.MessageId}' != '{match.MessageId}'");
                }
                if (!string.Equals(src.Sender, match.Sender, StringComparison.OrdinalIgnoreCase))
                {
                    verificationPassed = false;
                    info.VerificationNotes.Add($"Gönderen uyuşmazlığı: '{src.Sender}' != '{match.Sender}'");
                }

                // Compare newline-normalized body SHA-256
                if (!string.Equals(src.NormalizedBodySha256, match.NormalizedBodySha256, StringComparison.OrdinalIgnoreCase))
                {
                    verificationPassed = false;
                    info.VerificationNotes.Add($"İleti gövdesi hash uyuşmazlığı: '{src.Subject}'");
                }

                // Compare attachments via consuming multiset matching
                var attPool = new List<AttachmentSnapshot>(match.Attachments);
                if (src.Attachments.Count != match.Attachments.Count)
                {
                    verificationPassed = false;
                    info.VerificationNotes.Add($"Ek sayısı uyuşmazlığı ('{src.Subject}'): Kaynak {src.Attachments.Count}, PST {match.Attachments.Count}.");
                }

                foreach (var srcAtt in src.Attachments)
                {
                    var attMatch = attPool.FirstOrDefault(a =>
                        string.Equals(a.Sha256, srcAtt.Sha256, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(a.FileName, srcAtt.FileName, StringComparison.OrdinalIgnoreCase));

                    attMatch ??= attPool.FirstOrDefault(a => string.Equals(a.Sha256, srcAtt.Sha256, StringComparison.OrdinalIgnoreCase));

                    if (attMatch == null)
                    {
                        verificationPassed = false;
                        info.VerificationNotes.Add($"Ek hash uyuşmuyor veya eksik: '{srcAtt.FileName}' ({src.Subject})");
                        continue;
                    }

                    // Consume matched attachment
                    attPool.Remove(attMatch);
                    attVerified++;

                    if (srcAtt.SizeBytes != attMatch.SizeBytes)
                    {
                        verificationPassed = false;
                        info.VerificationNotes.Add($"Ek boyutu uyuşmazlığı: '{srcAtt.FileName}' (Kaynak {srcAtt.SizeBytes}, PST {attMatch.SizeBytes})");
                    }

                    // Validate Content-ID
                    if (!string.IsNullOrEmpty(srcAtt.ContentId))
                    {
                        string cleanSrcCid = srcAtt.ContentId.Trim('<', '>', ' ', '\t');
                        string cleanMatchCid = (attMatch.ContentId ?? "").Trim('<', '>', ' ', '\t');
                        if (string.Equals(cleanSrcCid, cleanMatchCid, StringComparison.OrdinalIgnoreCase))
                        {
                            cidVerified++;
                        }
                        else
                        {
                            verificationPassed = false;
                            info.VerificationNotes.Add($"Ek Content-ID uyuşmazlığı: '{srcAtt.FileName}' (Kaynak: {srcAtt.ContentId}, PST: {attMatch.ContentId})");
                        }
                    }
                }

                if (attPool.Count > 0)
                {
                    verificationPassed = false;
                    info.VerificationNotes.Add($"PST içinde beklenmeyen fazladan ekler var: '{src.Subject}' ({attPool.Count} adet)");
                }
            }

            if (pool.Count > 0)
            {
                verificationPassed = false;
                info.VerificationNotes.Add($"PST içinde kaynakta bulunmayan fazladan iletiler var: {pool.Count} adet.");
            }

            info.TotalAttachmentsVerified = attVerified;
            info.TotalCidVerified = cidVerified;
            info.VerificationSuccess = verificationPassed && (info.VerificationNotes.Count == 0);
        }

        return info;
    }

    private void TraversePstForVerification(
        PersonalStorage pst,
        FolderInfo folder,
        string folderPath,
        bool isRoot,
        List<SourceItemSnapshot> items,
        ref int activeFolders,
        ref int emptyFolders,
        ref int systemFolders)
    {
        // Enumerate messages in this folder
        IEnumerable<MessageInfo> msgInfos;
        try
        {
            msgInfos = folder.EnumerateMessages() ?? Array.Empty<MessageInfo>();
        }
        catch
        {
            msgInfos = Array.Empty<MessageInfo>();
        }

        int count = 0;
        foreach (var mi in msgInfos)
        {
            count++;
            try
            {
                using var msg = pst.ExtractMessage(mi);
                if (msg != null)
                {
                    items.Add(BuildItemSnapshot(msg, mi, folderPath));
                }
            }
            catch
            {
                // Extraction error will cause multiset discrepancy
            }
        }

        if (!isRoot || count > 0)
        {
            bool isSystem = IsSystemFolder(folder.DisplayName ?? "", folderPath);
            if (count > 0) activeFolders++;
            else if (isSystem) systemFolders++;
            else emptyFolders++;
        }

        // Traverse subfolders
        FolderInfoCollection? subs = null;
        try
        {
            subs = folder.GetSubFolders();
        }
        catch { }

        if (subs == null) return;

        foreach (FolderInfo sf in subs)
        {
            string childName = sf.DisplayName ?? "Adsız Klasör";
            string childPath = (isRoot && folderPath == "[Kök Klasör]") ? childName : $"{folderPath}/{childName}";
            TraversePstForVerification(pst, sf, childPath, isRoot: false, items, ref activeFolders, ref emptyFolders, ref systemFolders);
        }
    }

    internal static string ComputeNormalizedBodySha256(string? body)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;
        string normalized = body.Replace("\r\n", "\n").Replace("\r", "\n");
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        return System.Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static string ComputeFileSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] hash = sha.ComputeHash(stream);
        return System.Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsSystemFolder(string displayName, string path)
    {
        foreach (var kw in SystemKeywords)
        {
            if (displayName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                path.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    internal class SourceItemSnapshot
    {
        public string EntryId { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Sender { get; set; } = string.Empty;
        public string DisplayTo { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public string DateUtc { get; set; } = string.Empty;
        public string NormalizedBodySha256 { get; set; } = string.Empty;
        public int BodyLength { get; set; }
        public List<AttachmentSnapshot> Attachments { get; set; } = new();
    }

    internal class AttachmentSnapshot
    {
        public string FileName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; } = string.Empty;
        public string? ContentId { get; set; }
        public bool IsInline { get; set; }
    }
}
