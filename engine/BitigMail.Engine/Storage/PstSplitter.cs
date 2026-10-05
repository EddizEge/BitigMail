using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aspose.Email.Mapi;
using Aspose.Email.Storage.Pst;
using BitigMail.Engine.Models;

namespace BitigMail.Engine.Storage;

public class PstSplitter
{
    private readonly OstToPstConverter _converter;
    private readonly IDiskCapacityProbe _capacityProbe;

    public PstSplitter(IDiskCapacityProbe? capacityProbe = null)
    {
        _capacityProbe = capacityProbe ?? new WindowsDiskCapacityProbe();
        _converter = new OstToPstConverter(_capacityProbe);
    }

    internal Action<string, List<OstToPstConverter.SourceItemSnapshot>>? BeforePartVerificationHook { get; set; }
    internal Action<string>? BeforePublicationHook { get; set; }

    public Task<ConversionReport> SplitAsync(
        string sourcePath,
        string outputParentDir,
        string jobId,
        ClientProjectContext clientContext,
        string expectedSourceSha256,
        RegisteredSelection selection,
        SplitOptions options,
        IProgress<ConversionJobProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Split(
            sourcePath,
            outputParentDir,
            jobId,
            clientContext,
            expectedSourceSha256,
            selection,
            options,
            progress,
            cancellationToken), cancellationToken);
    }

    public ConversionReport Split(
        string sourcePath,
        string outputParentDir,
        string jobId,
        ClientProjectContext clientContext,
        RegisteredSplitPlan splitPlan,
        IProgress<ConversionJobProgress>? progress = null,
        CancellationToken cancellationToken = default,
        RegisteredSelection? selection = null)
    {
        if (splitPlan == null)
        {
            throw new ArgumentNullException(nameof(splitPlan), "Bölümleme planı belirtilmelidir.");
        }

        if (!splitPlan.CanSplit)
        {
            throw new InvalidOperationException(splitPlan.BlockerReason ?? "Bölme planı geçersizdir.");
        }

        if (string.IsNullOrWhiteSpace(splitPlan.SourceSha256))
        {
            throw new InvalidOperationException("Bölümleme planında kaynak SHA-256 parmak izi (SourceSha256) eksik veya boş. Geçersiz plan reddedildi.");
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Kaynak dosya bulunamadı: {sourcePath}", sourcePath);
        }

        string expectedSha = splitPlan.SourceSha256;

        // Under outer read lock compute actual SHA and compare expected plan/source hash
        // BEFORE opening PersonalStorage/SDK traversal; mismatch rejects deterministically before output work.
        using (var preCheckStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            using var sha = SHA256.Create();
            string actualSha = Convert.ToHexString(sha.ComputeHash(preCheckStream)).ToLowerInvariant();
            if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] Kaynak dosyada analizden sonra değişiklik tespit edildi! Analiz hash'i: {expectedSha}, Güncel hash: {actualSha}. Yeniden analiz yapılmalıdır.");
            }
        }

        RegisteredSelection effectiveSelection;

        if (selection != null)
        {
            effectiveSelection = selection;
        }
        else if (splitPlan.Selection != null)
        {
            effectiveSelection = splitPlan.Selection;
        }
        else
        {
            using var preCheckStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var formatInfo = OutlookStorageInspector.ValidateSplitSourceAuthoritatively(preCheckStream, sourcePath);
            preCheckStream.Position = 0;
            using var preCheckStorage = PersonalStorage.FromStream(new NonClosingStream(preCheckStream));
            preCheckStream.Position = 0;
            using var sha = SHA256.Create();
            string actualSha = Convert.ToHexString(sha.ComputeHash(preCheckStream)).ToLowerInvariant();

            var preflight = OstSelectionEngine.InspectPreflightAuthoritatively(preCheckStorage, actualSha, preCheckStream.Length, cancellationToken);
            var (_, regSel) = OstSelectionEngine.EvaluateSelection(
                preCheckStorage,
                splitPlan.SourceHandle ?? "src_auto",
                actualSha,
                folderIds: null,
                startDate: null,
                endDate: null,
                preflight: preflight,
                cancellationToken: cancellationToken);
            effectiveSelection = regSel;
        }

        var options = new SplitOptions
        {
            SplitMode = splitPlan.SplitMode ?? SplitOptions.ModeYear,
            SizeCapBytes = splitPlan.SizeCapBytes
        };

        return Split(
            sourcePath,
            outputParentDir,
            jobId,
            clientContext,
            expectedSha,
            effectiveSelection,
            options,
            progress,
            cancellationToken);
    }

    public ConversionReport Split(
        string sourcePath,
        string outputParentDir,
        string jobId,
        ClientProjectContext clientContext,
        string expectedSourceSha256,
        RegisteredSelection selection,
        SplitOptions options,
        IProgress<ConversionJobProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Kaynak dosya bulunamadı: {sourcePath}", sourcePath);
        }

        if (!Directory.Exists(outputParentDir))
        {
            throw new DirectoryNotFoundException($"Çıktı üst dizini bulunamadı: {outputParentDir}");
        }

        // Validate safe single-path-segment jobId (no '/', '\', '..', ':')
        if (string.IsNullOrWhiteSpace(jobId) ||
            jobId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            jobId.Contains('/') || jobId.Contains('\\') || jobId.Contains("..") || jobId.Contains(':'))
        {
            throw new ArgumentException("Geçersiz jobId: Tek bir yol segmenti olmalı, üst dizin veya yol ayırıcı içeremez.", nameof(jobId));
        }

        // Validate canonical parent containment
        string canonicalParent = Path.GetFullPath(outputParentDir);
        string partialDir = Path.Combine(canonicalParent, $"arsiv-{jobId}.partial");
        string finalDir = Path.Combine(canonicalParent, $"arsiv-{jobId}");

        string? partialDirParent = Path.GetDirectoryName(Path.GetFullPath(partialDir));
        string? finalDirParent = Path.GetDirectoryName(Path.GetFullPath(finalDir));
        if (!string.Equals(partialDirParent, canonicalParent, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(finalDirParent, canonicalParent, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Hedef ve geçici dizinler çıktı üst klasörünün doğrudan alt elemanı olmalıdır.");
        }

        // Publication Safety 1: NEVER recursively delete an existing arsiv-{jobId}.partial.
        // If staging path exists as file or directory, fail closed and preserve it.
        if (Directory.Exists(partialDir) || File.Exists(partialDir))
        {
            throw new InvalidOperationException($"[ÇAKIŞMA ENGELİ] Geçici hazırlık dizini/dosyası zaten mevcut: '{partialDir}'. Veri kaybını önlemek için işlem başlatılamaz.");
        }

        // If final path exists as file or directory, fail closed; never File.Delete/overwrite.
        if (Directory.Exists(finalDir) || File.Exists(finalDir))
        {
            throw new InvalidOperationException($"[ÇAKIŞMA ENGELİ] Hedef arşiv dizini zaten mevcut: '{finalDir}'. Üzerine yazma kesinlikle reddedildi.");
        }

        SplitOptions.Validate(options);

        if (selection == null)
        {
            throw new ArgumentNullException(nameof(selection), "Seçim bilgisi zorunludur.");
        }

        if (!selection.CanConvert || selection.HasTrialBlocker || selection.HasPreflightBlocker)
        {
            string reason = selection.PreflightBlockerReason ?? selection.TrialBlockerReason ?? selection.BlockerReason ?? "Kaynak dosyada ön kontrol engeli bulunmaktadır. Bölümleme başlatılamaz.";
            if (!reason.Contains("[ÖN KONTROL ENGELİ]"))
            {
                reason = $"[ÖN KONTROL ENGELİ] {reason}";
            }
            throw new InvalidOperationException(reason);
        }

        if (selection.SelectedMessagesCount == 0)
        {
            throw new InvalidOperationException("Seçilen filtre kriterlerine uyan hiçbir ileti bulunamadığından bölümleme başlatılamaz.");
        }

        var stopwatch = Stopwatch.StartNew();

        // Hold ONE outer read lock (FileShare.Read) across entire validation, splitting, and rehash
        using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Safeguard 1: Source SHA-256 computed on the locked stream before SDK parsing
        sourceStream.Position = 0;
        using var shaBefore = SHA256.Create();
        string sourceShaBefore = Convert.ToHexString(shaBefore.ComputeHash(sourceStream)).ToLowerInvariant();
        long sourceSizeBytes = sourceStream.Length;
        sourceStream.Position = 0;

        if (!string.IsNullOrEmpty(expectedSourceSha256) &&
            !string.Equals(sourceShaBefore, expectedSourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] Kaynak dosyada analizden sonra değişiklik tespit edildi! Analiz hash'i: {expectedSourceSha256}, Güncel hash: {sourceShaBefore}. Yeniden analiz yapılmalıdır.");
        }

        if (!string.Equals(selection.SourceSha256, sourceShaBefore, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] Seçim SHA hash'i ({selection.SourceSha256}) geçerli kaynak dosya hash'i ({sourceShaBefore}) ile uyuşmuyor.");
        }

        // Safeguard 2: Authoritative format validation on the locked stream for matching source
        var formatInfo = OutlookStorageInspector.ValidateSplitSourceAuthoritatively(sourceStream, sourcePath);
        sourceStream.Position = 0;

        // Open PersonalStorage on the locked stream
        using var sourceStorage = PersonalStorage.FromStream(new NonClosingStream(sourceStream));

        // Inventory and collect PhysicalMessageRef objects for all selected messages
        var selectedRefs = CollectSelectedMessageRefs(sourceStorage, sourceShaBefore, selection, cancellationToken);
        if (selectedRefs.Count != selection.SelectedMessagesCount)
        {
            throw new InvalidOperationException($"Kaynak taranmasında seçilen ileti sayısı ({selection.SelectedMessagesCount}) ile toplanan fiziksel ileti sayısı ({selectedRefs.Count}) uyuşmuyor.");
        }

        int plannedParts = options.SplitMode.Equals(SplitOptions.ModeYear, StringComparison.OrdinalIgnoreCase)
            ? Math.Max(1, selectedRefs.Select(item => item.Year).Distinct().Count())
            : DiskCapacityPlanning.EstimateSizeSplitParts(sourceSizeBytes, options.SizeCapBytes!.Value);
        long requiredBytes = DiskCapacityPlanning.EstimateSplit(
            sourceSizeBytes, selection.SelectedMessagesCount, plannedParts);
        string? capacityBlocker = DiskCapacityPlanning.CapacityBlocker(requiredBytes, _capacityProbe.Probe(canonicalParent));
        if (capacityBlocker != null)
            throw new InvalidOperationException(capacityBlocker + " Bölüm çıktısı oluşturulmadı.");

        Directory.CreateDirectory(partialDir);

        var partsList = new List<SplitPartReport>();
        var writtenMessageKeys = new HashSet<string>(StringComparer.Ordinal);

        var jobProgress = new ConversionJobProgress
        {
            JobId = jobId,
            Status = "converting",
            Stage = "Bölümleniyor",
            TotalItems = selection.SelectedMessagesCount
        };

        string mode = options.SplitMode.Trim().ToLowerInvariant();

        if (mode == SplitOptions.ModeYear)
        {
            // Group by stored date normalized to Türkiye UTC+03:00 year
            var yearGroups = selectedRefs
                .GroupBy(m => m.Year)
                .OrderBy(g => g.Key == -1 ? int.MaxValue : g.Key)
                .ToList();

            int currentPartIdx = 0;
            foreach (var grp in yearGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                currentPartIdx++;

                string groupKey = grp.Key == -1 ? "Tarihsiz" : grp.Key.ToString();
                string partFileName = $"arsiv-{groupKey.ToLowerInvariant()}.pst";
                string partPath = Path.Combine(partialDir, partFileName);

                jobProgress.CurrentFolder = $"Yıl: {groupKey} ({grp.Count()} ileti)";
                progress?.Report(jobProgress);

                var (fileSize, fileSha, snapshots) = WritePstPart(sourceStorage, partPath, grp.ToList(), cancellationToken);

                BeforePartVerificationHook?.Invoke(partPath, snapshots);

                var verificationInfo = _converter.VerifyReopenedPst(partPath, snapshots);
                if (!verificationInfo.VerificationSuccess)
                {
                    throw new InvalidOperationException($"[DOĞRULAMA HATASI] '{partFileName}' parçası doğrulamadan geçemedi: {string.Join("; ", verificationInfo.VerificationNotes)}");
                }

                // Immediately AFTER the first successful full VerifyReopenedPst/fault-hook, measure and store verified baseline actual closed bytes and SHA-256
                long baselineSize;
                string baselineSha;
                using (var partStream = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    baselineSize = partStream.Length;
                    using var sha = SHA256.Create();
                    baselineSha = Convert.ToHexString(sha.ComputeHash(partStream)).ToLowerInvariant();
                }

                foreach (var msgRef in grp)
                {
                    if (!writtenMessageKeys.Add(msgRef.PhysicalKey))
                    {
                        throw new InvalidOperationException($"KRİTİK BÜTÜNLÜK HATASI: '{msgRef.PhysicalKey}' iletisi birden fazla parçaya yazıldı!");
                    }
                }

                // PartFullPath points to canonical finalDir, never .partial
                partsList.Add(new SplitPartReport
                {
                    PartFileName = partFileName,
                    PartFullPath = Path.Combine(finalDir, partFileName),
                    PartSizeBytes = baselineSize,
                    PartSha256 = baselineSha,
                    ItemsWritten = grp.Count(),
                    TotalAttachmentsVerified = verificationInfo.TotalAttachmentsVerified,
                    TotalCidVerified = verificationInfo.TotalCidVerified,
                    GroupKey = groupKey,
                    ReopenedPstVerification = verificationInfo
                });

                jobProgress.ItemsWritten = writtenMessageKeys.Count;
                progress?.Report(jobProgress);
            }
        }
        else if (mode == SplitOptions.ModeSize)
        {
            long sizeCapBytes = options.SizeCapBytes!.Value;
            int partCounter = 1;

            PartitionAndBuildBySize(
                sourceStorage,
                partialDir,
                finalDir,
                selectedRefs,
                sizeCapBytes,
                partsList,
                writtenMessageKeys,
                ref partCounter,
                jobProgress,
                progress,
                cancellationToken);
        }

        // Safeguard 3: Aggregate disjoint physical coverage check
        if (writtenMessageKeys.Count != selection.SelectedMessagesCount ||
            !writtenMessageKeys.SetEquals(selection.SelectedMessageKeys))
        {
            throw new InvalidOperationException($"KRİTİK BÜTÜNLÜK HATASI: Parçalara yazılan toplam ileti kümesi ({writtenMessageKeys.Count}) ile seçilen ileti kümesi ({selection.SelectedMessagesCount}) birebir uyuşmuyor!");
        }

        int totalItemsWritten = partsList.Sum(p => p.ItemsWritten);
        int totalAttachmentsVerified = partsList.Sum(p => p.TotalAttachmentsVerified);
        int totalCidVerified = partsList.Sum(p => p.TotalCidVerified);

        if (totalItemsWritten != selection.SelectedMessagesCount)
        {
            throw new InvalidOperationException($"Toplam yazılan ileti sayısı ({totalItemsWritten}) seçilen ileti sayısı ({selection.SelectedMessagesCount}) ile uyuşmuyor.");
        }

        if (totalAttachmentsVerified != selection.SelectedAttachmentsCount)
        {
            throw new InvalidOperationException($"Toplam doğrulanan ek sayısı ({totalAttachmentsVerified}) seçilen ek sayısı ({selection.SelectedAttachmentsCount}) ile uyuşmuyor.");
        }

        // Publication Safety 3: Injected publication hook invoked BEFORE post-hook measurements
        BeforePublicationHook?.Invoke(partialDir);

        // Verification & Measurement Step 1: Verify exact staging entries (files and directories) match finalized PST candidates
        var stagingEntriesBeforeManifest = Directory.GetFileSystemEntries(partialDir)
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var expectedPstFiles = partsList
            .Select(p => p.PartFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!stagingEntriesBeforeManifest.SetEquals(expectedPstFiles))
        {
            throw new InvalidOperationException("Artık veya beklenmeyen geçici dosya veya alt dizinler tespit edildi. Yayınlama durduruldu.");
        }

        // Verification & Measurement Step 2: Recompute each candidate's bytes/SHA AFTER hook, compare against verified baseline, enforce cap, re-verify candidate PSTs
        foreach (var part in partsList)
        {
            string stagingPartPath = Path.Combine(partialDir, part.PartFileName);
            if (!File.Exists(stagingPartPath))
            {
                throw new FileNotFoundException($"Beklenen arşiv parçası bulunamadı: {stagingPartPath}", stagingPartPath);
            }

            long measuredSize;
            string measuredSha;
            using (var partStream = new FileStream(stagingPartPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                measuredSize = partStream.Length;
                using var sha = SHA256.Create();
                measuredSha = Convert.ToHexString(sha.ComputeHash(partStream)).ToLowerInvariant();
            }

            // 1. Enforce cap on measured post-hook size
            if (mode == SplitOptions.ModeSize && options.SizeCapBytes.HasValue && measuredSize > options.SizeCapBytes.Value)
            {
                throw new InvalidOperationException($"[BOYUT SINIRI AŞILDI] '{part.PartFileName}' dosya boyutu ({measuredSize:N0} bayt) belirlenen sınırı ({options.SizeCapBytes.Value:N0} bayt) aştı!");
            }

            // 2. Compare against verified baseline (stored immediately after VerifyReopenedPst)
            // If either size or SHA differs from verified baseline, fail closed.
            // Do NOT overwrite the baseline with mutated measurements and accept count-only equality!
            if (measuredSize != part.PartSizeBytes)
            {
                throw new InvalidOperationException($"[BÜTÜNLÜK HATASI] '{part.PartFileName}' parçasının dosya boyutu doğrulama sonrasında değişti! Doğrulanan boyut: {part.PartSizeBytes:N0} bayt, Güncel boyut: {measuredSize:N0} bayt.");
            }

            if (!string.Equals(measuredSha, part.PartSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"[BÜTÜNLÜK HATASI] '{part.PartFileName}' parçasının SHA-256 hash değeri doğrulama sonrasında değişti! Doğrulanan hash: {part.PartSha256}, Güncel hash: {measuredSha}.");
            }

            // Re-open candidate PST to re-verify integrity and message count
            using (var candidatePst = PersonalStorage.FromFile(stagingPartPath))
            {
                int verifiedMessageCount = 0;
                void TraverseCount(FolderInfo f)
                {
                    verifiedMessageCount += f.ContentCount;
                    foreach (var sub in f.GetSubFolders())
                    {
                        TraverseCount(sub);
                    }
                }
                if (candidatePst.RootFolder != null)
                {
                    TraverseCount(candidatePst.RootFolder);
                }

                if (verifiedMessageCount != part.ItemsWritten)
                {
                    throw new InvalidOperationException($"[DOĞRULAMA HATASI] '{part.PartFileName}' parçasında beklenen ileti sayısı ({part.ItemsWritten}) ile dosyadaki ileti sayısı ({verifiedMessageCount}) uyuşmuyor!");
                }
            }

            // Canonical final bundle path in manifest & report
            part.PartFullPath = Path.Combine(finalDir, part.PartFileName);
        }

        // Verification & Measurement Step 3: Re-check source locked stream hash
        sourceStream.Position = 0;
        using var shaAfter = SHA256.Create();
        string sourceShaAfter = Convert.ToHexString(shaAfter.ComputeHash(sourceStream)).ToLowerInvariant();
        bool sourceHashMatch = string.Equals(sourceShaBefore, sourceShaAfter, StringComparison.OrdinalIgnoreCase);

        if (!sourceHashMatch)
        {
            throw new InvalidOperationException("KRİTİK GÜVENLİK HATASI: Bölümleme sırasında kaynak dosya değişti!");
        }

        // Publication Safety 2: Manifest describes post-rename final bundle.
        // PartFullPath in manifest and report must point to canonical finalDir, never .partial
        var manifest = new SplitManifest
        {
            JobId = jobId,
            SourceFileName = Path.GetFileName(sourcePath),
            SourceSha256 = sourceShaBefore,
            SplitMode = mode,
            SizeCapBytes = options.SizeCapBytes,
            TotalParts = partsList.Count,
            TotalMessagesWritten = totalItemsWritten,
            TotalAttachmentsVerified = totalAttachmentsVerified,
            TotalCidVerified = totalCidVerified,
            CompletedAt = DateTimeOffset.UtcNow,
            ClientContext = clientContext,
            Parts = partsList
        };

        string manifestPath = Path.Combine(partialDir, "arsiv-manifest.json");
        string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(manifestPath, manifestJson, Encoding.UTF8);

        // Verification & Measurement Step 4: Verify staging directory contains ONLY finalized PSTs and arsiv-manifest.json
        // Using GetFileSystemEntries to ensure no unexpected files OR subdirectories exist.
        var finalStagingEntries = Directory.GetFileSystemEntries(partialDir)
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allowedEntries = expectedPstFiles
            .Concat(new[] { "arsiv-manifest.json" })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!finalStagingEntries.SetEquals(allowedEntries))
        {
            throw new InvalidOperationException("Geçici hazırlık dizininde beklenmeyen artık dosya veya alt dizinler tespit edildi. Yayınlama durduruldu.");
        }

        // Safeguard 6: Atomic publication (rename staging directory to final directory)
        if (Directory.Exists(finalDir) || File.Exists(finalDir))
        {
            throw new InvalidOperationException($"[ÇAKIŞMA ENGELİ] Hedef arşiv dizini zaten mevcut: '{finalDir}'. Üzerine yazma kesinlikle reddedildi.");
        }

        Directory.Move(partialDir, finalDir);

        stopwatch.Stop();

        return new ConversionReport
        {
            JobId = jobId,
            EvidenceLabel = "PST_OST_SPLIT (Local-Engine Reopen Verification)",
            ClientContext = clientContext,
            SourceFileName = Path.GetFileName(sourcePath),
            SourceSizeBytes = sourceSizeBytes,
            SourceSha256Before = sourceShaBefore,
            SourceSha256After = sourceShaAfter,
            SourceHashMatch = sourceHashMatch,
            OutputPstFileName = Path.GetFileName(finalDir),
            OutputPath = finalDir,
            OutputPstSizeBytes = partsList.Sum(p => p.PartSizeBytes),
            OutputPstSha256 = partsList.FirstOrDefault()?.PartSha256 ?? string.Empty,
            ConversionSuccess = true,
            ItemsRead = selection.TotalSourceMessages,
            ItemsWritten = totalItemsWritten,
            FailedItems = 0,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            TotalFoldersProcessed = selection.Filters.SelectedFolders?.Count ?? 1,
            FidelityStatus = "PASS",
            OverallStatus = "SUCCESS",
            ReopenedPstVerification = new ReopenedPstVerificationInfo
            {
                VerificationSuccess = partsList.Count > 0 && partsList.All(p => p.ReopenedPstVerification.VerificationSuccess),
                ItemCountMatch = totalItemsWritten == selection.SelectedMessagesCount
                    && partsList.All(p => p.ReopenedPstVerification.ItemCountMatch),
                TotalPhysicalItemsFound = partsList.Sum(p => p.ReopenedPstVerification.TotalPhysicalItemsFound),
                TotalAttachmentsVerified = totalAttachmentsVerified,
                TotalCidVerified = totalCidVerified,
                TotalFoldersFound = partsList.Sum(p => p.ReopenedPstVerification.TotalFoldersFound),
                ActiveFoldersFound = partsList.Sum(p => p.ReopenedPstVerification.ActiveFoldersFound),
                EmptyFoldersFound = partsList.Sum(p => p.ReopenedPstVerification.EmptyFoldersFound),
                SystemFoldersFound = partsList.Sum(p => p.ReopenedPstVerification.SystemFoldersFound),
                VerificationNotes = new List<string>
                {
                    "Summary aggregates the reopened verification of every output part. Folder counts are summed across parts, not unique archive folder paths.",
                    "SAME_SDK_ONLY verification; unmeasured fields remain UNKNOWN."
                }
            },
            IsFiltered = true,
            SelectionFilter = selection.Filters,
            TotalSourceMessages = selection.TotalSourceMessages,
            SelectedMessagesCount = selection.SelectedMessagesCount,
            ExcludedMessagesCount = selection.ExcludedMessagesCount,
            MissingDateExcludedCount = selection.MissingDateExcludedCount,
            SelectedAttachmentsCount = selection.SelectedAttachmentsCount,
            SelectionId = selection.SelectionId,
            JobKind = "split",
            SplitMode = mode,
            SplitSizeCapBytes = options.SizeCapBytes,
            OutputDirectoryPath = finalDir,
            Parts = partsList
        };
    }

    private void PartitionAndBuildBySize(
        PersonalStorage sourceStorage,
        string partialDir,
        string finalDir,
        List<PhysicalMessageRef> candidateList,
        long sizeCapBytes,
        List<SplitPartReport> partsList,
        HashSet<string> writtenMessageKeys,
        ref int partCounter,
        ConversionJobProgress jobProgress,
        IProgress<ConversionJobProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (candidateList.Count == 0) return;

        string candidateFileName = $"candidate-{Guid.NewGuid():N}.pst";
        string candidatePath = Path.Combine(partialDir, candidateFileName);

        jobProgress.CurrentFolder = $"Boyut Denemesi: {candidateList.Count} ileti (Hedef sınır: {sizeCapBytes:N0} B)";
        progress?.Report(jobProgress);

        var (fileSize, fileSha, snapshots) = WritePstPart(sourceStorage, candidatePath, candidateList, cancellationToken);

        if (fileSize <= sizeCapBytes)
        {
            // Fits within hard cap: finalize part
            string partFileName = $"arsiv-{partCounter:D3}.pst";
            string finalizedPath = Path.Combine(partialDir, partFileName);

            if (File.Exists(finalizedPath)) File.Delete(finalizedPath);
            File.Move(candidatePath, finalizedPath);
            partCounter++;

            BeforePartVerificationHook?.Invoke(finalizedPath, snapshots);

            var verificationInfo = _converter.VerifyReopenedPst(finalizedPath, snapshots);
            if (!verificationInfo.VerificationSuccess)
            {
                throw new InvalidOperationException($"[DOĞRULAMA HATASI] '{partFileName}' parçası doğrulamadan geçemedi: {string.Join("; ", verificationInfo.VerificationNotes)}");
            }

            // Immediately AFTER the first successful full VerifyReopenedPst/fault-hook, measure and store verified baseline actual closed bytes and SHA-256
            long baselineSize;
            string baselineSha;
            using (var partStream = new FileStream(finalizedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                baselineSize = partStream.Length;
                using var sha = SHA256.Create();
                baselineSha = Convert.ToHexString(sha.ComputeHash(partStream)).ToLowerInvariant();
            }

            foreach (var msgRef in candidateList)
            {
                if (!writtenMessageKeys.Add(msgRef.PhysicalKey))
                {
                    throw new InvalidOperationException($"KRİTİK BÜTÜNLÜK HATASI: '{msgRef.PhysicalKey}' iletisi birden fazla parçaya yazıldı!");
                }
            }

            // Canonical final bundle path in manifest & report
            partsList.Add(new SplitPartReport
            {
                PartFileName = partFileName,
                PartFullPath = Path.Combine(finalDir, partFileName),
                PartSizeBytes = baselineSize,
                PartSha256 = baselineSha,
                ItemsWritten = candidateList.Count,
                TotalAttachmentsVerified = verificationInfo.TotalAttachmentsVerified,
                TotalCidVerified = verificationInfo.TotalCidVerified,
                GroupKey = $"{partCounter - 1:D3}",
                ReopenedPstVerification = verificationInfo
            });

            jobProgress.ItemsWritten = writtenMessageKeys.Count;
            progress?.Report(jobProgress);
        }
        else
        {
            // Oversized: discard candidate file
            if (File.Exists(candidatePath))
            {
                try { File.Delete(candidatePath); } catch { }
            }

            if (candidateList.Count == 1)
            {
                throw new InvalidOperationException($"[BOYUT ENGELİ] Tek bir ileti ve ekleri ({fileSize:N0} bayt) belirlenen boyut sınırını ({sizeCapBytes:N0} bayt) aşıyor. Bölümleme durduruldu. Lütfen en az {fileSize:N0} baytlık bir boyut sınırı belirleyin.");
            }

            // Bounded recursive bisection
            int mid = candidateList.Count / 2;
            var left = candidateList.GetRange(0, mid);
            var right = candidateList.GetRange(mid, candidateList.Count - mid);

            PartitionAndBuildBySize(
                sourceStorage,
                partialDir,
                finalDir,
                left,
                sizeCapBytes,
                partsList,
                writtenMessageKeys,
                ref partCounter,
                jobProgress,
                progress,
                cancellationToken);

            PartitionAndBuildBySize(
                sourceStorage,
                partialDir,
                finalDir,
                right,
                sizeCapBytes,
                partsList,
                writtenMessageKeys,
                ref partCounter,
                jobProgress,
                progress,
                cancellationToken);
        }
    }

    private static (long FileSize, string Sha256, List<OstToPstConverter.SourceItemSnapshot> Snapshots) WritePstPart(
        PersonalStorage sourceStorage,
        string targetPstPath,
        IReadOnlyCollection<PhysicalMessageRef> messages,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<OstToPstConverter.SourceItemSnapshot>();
        var folderCache = new Dictionary<string, FolderInfo>(StringComparer.OrdinalIgnoreCase);

        using (var destPst = PersonalStorage.Create(targetPstPath, FileFormatVersion.Unicode))
        {
            foreach (var msgRef in messages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                FolderInfo destFolder = GetOrCreateFolderPath(destPst, msgRef.FolderPath, folderCache);

                using var msg = sourceStorage.ExtractMessage(msgRef.MsgInfo);
                if (msg == null)
                {
                    throw new InvalidOperationException($"İleti okunamadı: {msgRef.PhysicalKey}");
                }

                var snapshot = OstToPstConverter.CaptureSourceSnapshot(
                    msg,
                    msgRef.FolderPath,
                    msgRef.FolderId,
                    msgRef.MsgInfo.EntryIdString ?? string.Empty);

                snapshots.Add(snapshot);

                destFolder.AddMessage(msg);
            }
        }

        long size;
        string sha;
        using (var fs = new FileStream(targetPstPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            size = fs.Length;
            using var sha256 = SHA256.Create();
            sha = Convert.ToHexString(sha256.ComputeHash(fs)).ToLowerInvariant();
        }

        return (size, sha, snapshots);
    }

    private static FolderInfo GetOrCreateFolderPath(
        PersonalStorage pst,
        string folderPath,
        Dictionary<string, FolderInfo> cache)
    {
        if (cache.TryGetValue(folderPath, out var cached))
        {
            return cached;
        }

        string normalized = folderPath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(normalized) || normalized == "[Kök Klasör]")
        {
            cache[folderPath] = pst.RootFolder;
            return pst.RootFolder;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        FolderInfo current = pst.RootFolder;

        foreach (var seg in segments)
        {
            if (seg == "[Kök Klasör]") continue;

            FolderInfo? next = null;
            try
            {
                next = current.GetSubFolder(seg);
            }
            catch { }

            if (next == null)
            {
                try
                {
                    next = current.AddSubFolder(seg);
                }
                catch
                {
                    next = current.GetSubFolder(seg);
                }
            }

            current = next;
        }

        cache[folderPath] = current;
        return current;
    }

    private static List<PhysicalMessageRef> CollectSelectedMessageRefs(
        PersonalStorage storage,
        string sourceSha,
        RegisteredSelection selection,
        CancellationToken cancellationToken)
    {
        var result = new List<PhysicalMessageRef>();
        var allFolders = new List<(string FolderId, string FolderPath, FolderInfo Folder)>();
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CollectFolderHierarchies(storage.RootFolder, "", isRoot: true, sourceSha, allFolders, usedPaths, cancellationToken);

        foreach (var (fId, fPath, fInfo) in allFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<MessageInfo> msgs;
            try
            {
                msgs = fInfo.EnumerateMessages() ?? Array.Empty<MessageInfo>();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"'{fPath}' klasöründeki iletiler taranamadı: {ex.Message}", ex);
            }

            foreach (var mi in msgs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string physicalKey = $"m_{fId}_{mi.EntryIdString}";

                if (selection.SelectedMessageKeys.Contains(physicalKey))
                {
                    using var msg = storage.ExtractMessage(mi);
                    if (msg == null)
                    {
                        throw new InvalidOperationException($"'{fPath}' klasöründeki {mi.EntryIdString} öğesi okunamadı.");
                    }

                    DateTime? storedDate = OstSelectionEngine.ExtractMessageDate(msg);
                    int year = -1;
                    if (storedDate.HasValue)
                    {
                        // In Türkiye (UTC+03:00) policy: add 3 hours to determine calendar year
                        year = storedDate.Value.AddHours(3).Year;
                    }

                    result.Add(new PhysicalMessageRef
                    {
                        PhysicalKey = physicalKey,
                        FolderId = fId,
                        FolderPath = fPath,
                        EntryIdString = mi.EntryIdString ?? string.Empty,
                        MsgInfo = mi,
                        Folder = fInfo,
                        StoredDateUtc = storedDate,
                        Year = year,
                        AttachmentCount = msg.Attachments?.Count ?? 0
                    });
                }
            }
        }

        return result;
    }

    private static void CollectFolderHierarchies(
        FolderInfo folder,
        string parentPath,
        bool isRoot,
        string sourceSha,
        List<(string FolderId, string FolderPath, FolderInfo Folder)> list,
        HashSet<string> usedPaths,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string folderName = isRoot ? "[Kök Klasör]" : (folder.DisplayName ?? "Adsız Klasör");
        string currentPath = isRoot ? folderName : (string.IsNullOrEmpty(parentPath) ? folderName : $"{parentPath}/{folderName}");

        string rawEntryId = folder.EntryId != null
            ? Convert.ToHexString(folder.EntryId)
            : (folder.EntryIdString ?? currentPath);
        string folderId = OstSelectionEngine.ComputeFolderId(sourceSha, rawEntryId);

        list.Add((folderId, currentPath, folder));

        FolderInfoCollection? subs = null;
        try
        {
            subs = folder.GetSubFolders();
        }
        catch { }

        if (subs == null) return;

        foreach (FolderInfo sub in subs)
        {
            // The recursive call appends its own name once; pass only its parent's path.
            CollectFolderHierarchies(sub, isRoot ? string.Empty : currentPath,
                isRoot: false, sourceSha, list, usedPaths, cancellationToken);
        }
    }

    public static List<SplitYearGroupPreview> CalculateYearGroups(
        PersonalStorage storage,
        string sourceSha,
        RegisteredSelection selection,
        CancellationToken cancellationToken = default)
    {
        var refs = CollectSelectedMessageRefs(storage, sourceSha, selection, cancellationToken);
        var groups = refs
            .GroupBy(m => m.Year)
            .OrderBy(g => g.Key == -1 ? int.MaxValue : g.Key)
            .Select(g =>
            {
                string yearStr = g.Key == -1 ? "Tarihsiz" : g.Key.ToString();
                return new SplitYearGroupPreview
                {
                    Year = yearStr,
                    MessageCount = g.Count(),
                    AttachmentCount = g.Sum(m => m.AttachmentCount),
                    TargetFileName = $"arsiv-{yearStr.ToLowerInvariant()}.pst"
                };
            })
            .ToList();

        return groups;
    }

    public static List<YearGroupSummary> PlanYearGroups(
        PersonalStorage storage,
        string sourceSha,
        RegisteredSelection selection,
        CancellationToken cancellationToken = default)
    {
        return CalculateYearGroups(storage, sourceSha, selection, cancellationToken)
            .Select(g => new YearGroupSummary
            {
                Year = g.Year,
                MessageCount = g.MessageCount,
                AttachmentCount = g.AttachmentCount,
                TargetFileName = g.TargetFileName
            })
            .ToList();
    }

    internal class PhysicalMessageRef
    {
        public string PhysicalKey { get; set; } = string.Empty;
        public string FolderId { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string EntryIdString { get; set; } = string.Empty;
        public MessageInfo MsgInfo { get; set; } = null!;
        public FolderInfo Folder { get; set; } = null!;
        public DateTime? StoredDateUtc { get; set; }
        public int Year { get; set; } // Türkiye UTC+03:00 year, or -1 for Tarihsiz
        public int AttachmentCount { get; set; }
    }
}
