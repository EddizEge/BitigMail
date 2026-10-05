using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Aspose.Email.Mapi;
using Aspose.Email.Storage.Pst;
using BitigMail.Engine.Models;

namespace BitigMail.Engine.Storage;

public class OstAnalyzer
{
    private readonly IDiskCapacityProbe _capacityProbe;

    public OstAnalyzer(IDiskCapacityProbe? capacityProbe = null)
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

    public Task<OstAnalysisResult> AnalyzeAsync(string ostFilePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Analyze(ostFilePath, cancellationToken), cancellationToken);
    }

    public OstAnalysisResult Analyze(string ostFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ostFilePath))
        {
            throw new FileNotFoundException($"OST dosyası bulunamadı: {ostFilePath}", ostFilePath);
        }

        // Single read-only lock (FileShare.Read) for format validation + hash + full inventory
        using var stream = new FileStream(ostFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // 1. Authoritative format validation under lock
        var formatInfo = OutlookStorageInspector.ValidateOstAuthoritatively(stream, ostFilePath);

        // 2. File size & SHA-256 computed on the locked stream
        stream.Position = 0;
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(stream);
        string sha256 = System.Convert.ToHexString(hash).ToLowerInvariant();
        long sizeBytes = stream.Length;
        stream.Position = 0;

        // 3. Open PersonalStorage under the same active lock
        using var ost = PersonalStorage.FromStream(new NonClosingStream(stream));
        return AnalyzeStorageCore(ost, Path.GetFileName(ostFilePath), sizeBytes, sha256, formatInfo, null, cancellationToken);
    }

    public Task<OstAnalysisResult> AnalyzeSplitSourceAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => AnalyzeSplitSource(filePath, cancellationToken), cancellationToken);
    }

    public OstAnalysisResult AnalyzeSplitSource(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Kaynak dosya bulunamadı: {filePath}", filePath);
        }

        // Single read-only lock (FileShare.Read) for split format validation (PST or OST) + hash + full inventory
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // 1. Authoritative split format validation under lock (header and SDK format must agree)
        var formatInfo = OutlookStorageInspector.ValidateSplitSourceAuthoritatively(stream, filePath);

        // 2. File size & SHA-256 computed on the locked stream
        stream.Position = 0;
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(stream);
        string sha256 = System.Convert.ToHexString(hash).ToLowerInvariant();
        long sizeBytes = stream.Length;
        stream.Position = 0;

        // 3. Open PersonalStorage under the same active lock
        using var storage = PersonalStorage.FromStream(new NonClosingStream(stream));
        return AnalyzeStorageCore(storage, Path.GetFileName(filePath), sizeBytes, sha256, formatInfo, null, cancellationToken);
    }

    internal OstAnalysisResult AnalyzeStorageCore(
        PersonalStorage ost,
        string fileName,
        long sizeBytes,
        string sha256,
        OutlookStorageFormatInfo? formatInfo = null,
        string? diskCheckPath = null,
        CancellationToken cancellationToken = default)
    {
        var folders = new List<FolderSummary>();
        var sampleMessages = new List<SampleMessageSummary>();
        var blockers = new List<string>();
        var warnings = new List<string>();
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int totalPhysicalItems = 0;
        int totalAttachments = 0;
        bool hasTrialFolderBlocker = false;
        string? trialBlockerReason = null;

        var rootFolder = ost.RootFolder;

        // Inspect root items (root items cannot be omitted)
        ProcessFolderForAnalysis(
            ost,
            rootFolder,
            folderName: "[Kök Klasör]",
            parentPath: "",
            isRoot: true,
            sha256,
            folders,
            sampleMessages,
            blockers,
            warnings,
            usedPaths,
            ref totalPhysicalItems,
            ref totalAttachments,
            ref hasTrialFolderBlocker,
            ref trialBlockerReason,
            cancellationToken);

        // Recursively inspect all subfolders
        EnumerateSubFoldersRecursive(
            ost,
            rootFolder,
            parentPath: "",
            sha256,
            folders,
            sampleMessages,
            blockers,
            warnings,
            usedPaths,
            ref totalPhysicalItems,
            ref totalAttachments,
            ref hasTrialFolderBlocker,
            ref trialBlockerReason,
            cancellationToken);

        // 4. Categorize folders
        int activeCount = 0;
        int emptyCount = 0;
        int systemCount = 0;

        foreach (var f in folders)
        {
            if (f.Category == "Active") activeCount++;
            else if (f.Category == "System") systemCount++;
            else emptyCount++;
        }

        if (totalPhysicalItems == 0)
        {
            warnings.Add("OST dosyasında dönüştürülecek posta öğesi bulunamadı (Tüm klasörler boş).");
        }

        // Estimate PST size and check disk space
        DiskCapacityResult? capacity = !string.IsNullOrEmpty(diskCheckPath) ? _capacityProbe.Probe(diskCheckPath) : null;
        long availableDisk = capacity?.AvailableBytes ?? 0;

        var preflight = EvaluatePreflight(folders, blockers, warnings, sizeBytes, availableDisk);
        preflight.DiskCapacityAvailable = capacity?.IsAvailable;
        preflight.AvailableDiskBytes = capacity?.IsAvailable == true ? capacity.AvailableBytes : null;
        preflight.DiskCapacityError = capacity?.IsAvailable == false ? capacity.Error : null;
        preflight.EstimatedRequiredBytes = DiskCapacityPlanning.EstimatePst(sizeBytes, totalPhysicalItems);
        preflight.EstimateBasis = "Tüm değişmez kaynak boyutu; tahmini PST genişlemesi ve geçici/son çıktı birlikteliği";

        return new OstAnalysisResult
        {
            SourceFileName = fileName,
            SourceSizeBytes = sizeBytes,
            SourceSha256 = sha256,
            FormatInfo = formatInfo ?? new OutlookStorageFormatInfo { IsValidOutlookStorage = true, IsOstSignature = true, FormatName = "Outlook Storage", AuthoritativeRuntimeValidation = "PASS" },
            TotalFolders = folders.Count,
            ActiveFoldersCount = activeCount,
            EmptyFoldersCount = emptyCount,
            SystemFoldersCount = systemCount,
            TotalItems = totalPhysicalItems,
            PhysicalTotalItems = totalPhysicalItems,
            TotalAttachments = totalAttachments,
            Folders = folders,
            SampleMessages = sampleMessages,
            Preflight = preflight
        };
    }

    public static PreflightCheckResult EvaluatePreflight(
        IEnumerable<FolderSummary> folders,
        IEnumerable<string>? existingBlockers = null,
        IEnumerable<string>? existingWarnings = null,
        long sizeBytes = 0,
        long availableDisk = 0)
    {
        var blockers = new List<string>(existingBlockers ?? Enumerable.Empty<string>());
        var warnings = new List<string>(existingWarnings ?? Enumerable.Empty<string>());
        bool hasTrialBlocker = false;
        string? trialBlockerReason = null;

        foreach (var f in folders)
        {
            if (f.ItemCount > 50)
            {
                hasTrialBlocker = true;
                trialBlockerReason = $"Deneme Sürümü Sınırı: '{f.DisplayName}' klasöründe {f.ItemCount} öğe var. Değerlendirme lisansı klasör başına en fazla 50 öğeye izin verir. Sessiz kırpma yapılmaz; dönüştürme engellenir.";
                blockers.Add(trialBlockerReason);
            }
        }

        long estimatedPstSize = (sizeBytes > 0) ? (long)(sizeBytes * 1.1) + (10 * 1024 * 1024) : 0;
        if (availableDisk > 0 && estimatedPstSize > 0 && availableDisk < estimatedPstSize * 2)
        {
            warnings.Add($"Hedef sürücüde boş disk alanı düşük: {availableDisk / (1024 * 1024)} MB kullanılabilir. Tahmini gereken alan: {estimatedPstSize / (1024 * 1024)} MB.");
        }

        return new PreflightCheckResult
        {
            CanConvert = blockers.Count == 0,
            HasTrialBlocker = hasTrialBlocker,
            TrialBlockerReason = trialBlockerReason,
            Blockers = blockers,
            Warnings = warnings,
            EstimatedPstSizeBytes = estimatedPstSize,
            AvailableDiskSizeBytes = availableDisk
        };
    }

    internal void EnumerateSubFoldersRecursive(
        PersonalStorage ost,
        FolderInfo parentFolder,
        string parentPath,
        string sha256,
        List<FolderSummary> folders,
        List<SampleMessageSummary> sampleMessages,
        List<string> blockers,
        List<string> warnings,
        HashSet<string> usedPaths,
        ref int totalPhysicalItems,
        ref int totalAttachments,
        ref bool hasTrialFolderBlocker,
        ref string? trialBlockerReason,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        FolderInfoCollection? subFolders;
        try
        {
            subFolders = parentFolder.GetSubFolders();
        }
        catch (Exception ex)
        {
            blockers.Add($"Alt klasörler listelenemedi ('{parentPath}'): {ex.Message}");
            return;
        }

        if (subFolders == null) return;

        foreach (FolderInfo subFolder in subFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string folderName = subFolder.DisplayName ?? "Adsız Klasör";
            string currentPath = string.IsNullOrEmpty(parentPath) ? folderName : $"{parentPath}/{folderName}";

            ProcessFolderForAnalysis(
                ost,
                subFolder,
                folderName,
                parentPath,
                isRoot: false,
                sha256,
                folders,
                sampleMessages,
                blockers,
                warnings,
                usedPaths,
                ref totalPhysicalItems,
                ref totalAttachments,
                ref hasTrialFolderBlocker,
                ref trialBlockerReason,
                cancellationToken);

            // Recurse into children
            EnumerateSubFoldersRecursive(
                ost,
                subFolder,
                currentPath,
                sha256,
                folders,
                sampleMessages,
                blockers,
                warnings,
                usedPaths,
                ref totalPhysicalItems,
                ref totalAttachments,
                ref hasTrialFolderBlocker,
                ref trialBlockerReason,
                cancellationToken);
        }
    }

    internal void ProcessFolderForAnalysis(
        PersonalStorage ost,
        FolderInfo folder,
        string folderName,
        string parentPath,
        bool isRoot,
        string sha256,
        List<FolderSummary> folders,
        List<SampleMessageSummary> sampleMessages,
        List<string> blockers,
        List<string> warnings,
        HashSet<string> usedPaths,
        ref int totalPhysicalItems,
        ref int totalAttachments,
        ref bool hasTrialFolderBlocker,
        ref string? trialBlockerReason,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Ensure unique path across entire store
        string basePath = string.IsNullOrEmpty(parentPath) ? folderName : $"{parentPath}/{folderName}";
        string uniquePath = basePath;
        int disambiguator = 1;
        while (usedPaths.Contains(uniquePath))
        {
            uniquePath = $"{basePath} ({disambiguator++})";
        }
        usedPaths.Add(uniquePath);

        int contentCount = 0;
        try { contentCount = folder.ContentCount; } catch { }

        int subCount = 0;
        try { subCount = folder.GetSubFolders()?.Count ?? 0; } catch { }

        // Enumerate messages physically - DO NOT swallow errors
        List<MessageInfo> msgInfos;
        try
        {
            var enumerated = folder.EnumerateMessages();
            msgInfos = enumerated != null ? enumerated.ToList() : new List<MessageInfo>();
        }
        catch (Exception ex)
        {
            blockers.Add($"Klasör iletileri numaralandırılamadı: '{uniquePath}' - {ex.Message}");
            msgInfos = new List<MessageInfo>();
        }

        int physicalCount = msgInfos.Count;

        // Include folder in summary if not root, or if root contains physical items
        if (!isRoot || physicalCount > 0)
        {
            bool isSystem = IsSystemFolder(folderName, uniquePath);
            string category = physicalCount > 0 ? "Active" : (isSystem ? "System" : "Empty");
            bool isIpm = uniquePath.Contains("IPM_SUBTREE", StringComparison.OrdinalIgnoreCase);

            string rawEntryId = folder.EntryId != null
                ? System.Convert.ToHexString(folder.EntryId)
                : (folder.EntryIdString ?? uniquePath);
            string folderId = OstSelectionEngine.ComputeFolderId(sha256, rawEntryId);

            folders.Add(new FolderSummary
            {
                FolderId = folderId,
                FolderPath = uniquePath,
                DisplayName = folderName,
                ItemCount = physicalCount,
                SubFolderCount = subCount,
                Category = category,
                IsIpmFolder = isIpm
            });

            // Strict 50-item evaluation limit rule
            if (physicalCount > 50)
            {
                hasTrialFolderBlocker = true;
                trialBlockerReason = $"Deneme Sürümü Sınırı: '{uniquePath}' klasöründe {physicalCount} öğe var. Değerlendirme lisansı klasör başına en fazla 50 öğeye izin verir. Sessiz kırpma yapılmaz; dönüştürme engellenir.";
                blockers.Add(trialBlockerReason);
            }
        }

        totalPhysicalItems += physicalCount;

        // Reconcile ContentCount with physical enumerated count
        if (contentCount > 0 && contentCount != physicalCount)
        {
            warnings.Add($"'{uniquePath}' klasöründe meta veri sayısı ({contentCount}) ile fiziksel ileti sayısı ({physicalCount}) farklı.");
        }

        // Count attachments across ALL items, not only the first 10 samples
        foreach (var msgInfo in msgInfos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var msg = ost.ExtractMessage(msgInfo);
                if (msg == null)
                {
                    blockers.Add($"Öğe çıkarılamadı (boş): '{uniquePath}' ({msgInfo.EntryIdString})");
                    continue;
                }

                int attCount = msg.Attachments?.Count ?? 0;
                totalAttachments += attCount;

                if (sampleMessages.Count < 10)
                {
                    DateTime? date = OstSelectionEngine.ExtractMessageDate(msg);
                    sampleMessages.Add(new SampleMessageSummary
                    {
                        EntryId = msgInfo.EntryIdString ?? string.Empty,
                        FolderPath = uniquePath,
                        Subject = msg.Subject ?? "(Konu yok)",
                        Sender = msg.SenderEmailAddress ?? msg.SenderName ?? "(Gönderen bilinmiyor)",
                        DisplayTo = msg.DisplayTo ?? "",
                        DateUtc = date.HasValue ? date.Value.ToString("yyyy-MM-dd HH:mm:ss") : "Bilinmiyor",
                        HasAttachments = attCount > 0,
                        AttachmentCount = attCount
                    });
                }
            }
            catch (Exception ex)
            {
                blockers.Add($"Öğe okunamadı / bozuk: '{uniquePath}' ({msgInfo.EntryIdString}) - {ex.Message}");
            }
        }
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

}
