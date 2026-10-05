using BitigMail.Engine.Planning;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aspose.Email;
using Aspose.Email.Mapi;
using Aspose.Email.Storage.Pst;
using BitigMail.Engine.Models;
using MimeKit;

namespace BitigMail.Engine.Storage;

public class MimeToPstConverter
{
    private readonly MimeSourceInspector _inspector = new();
    private readonly IDiskCapacityProbe _capacityProbe;

    public MimeToPstConverter(IDiskCapacityProbe? capacityProbe = null)
    {
        _capacityProbe = capacityProbe ?? new WindowsDiskCapacityProbe();
    }

    public Task<ConversionReport> ConvertAsync(
        MimeSourceManifest manifest,
        string targetPstPath,
        string jobId,
        ClientProjectContext clientContext,
        IProgress<ConversionJobProgress>? progress = null,
        CancellationToken cancellationToken = default,
        RegisteredSelection? selection = null)
    {
        return Task.Run(() => Convert(manifest, targetPstPath, jobId, clientContext, progress, cancellationToken, selection), cancellationToken);
    }

    public ConversionReport Convert(
        MimeSourceManifest manifest,
        string targetPstPath,
        string jobId,
        ClientProjectContext clientContext,
        IProgress<ConversionJobProgress>? progress = null,
        CancellationToken cancellationToken = default,
        RegisteredSelection? selection = null)
    {
        var stopwatch = Stopwatch.StartNew();

        if (manifest == null || manifest.Entries.Count == 0)
        {
            throw new ArgumentException("Dönüştürülecek kaynak manifestosu boş olamaz.", nameof(manifest));
        }

        if (string.IsNullOrWhiteSpace(targetPstPath))
        {
            throw new ArgumentException("Hedef PST dosya yolu belirtilmelidir.", nameof(targetPstPath));
        }

        string fullTargetPath = Path.GetFullPath(targetPstPath);

        // Safeguard 0: Enforce >50-per-output-folder trial limit over entire source before PST creation
        foreach (var group in manifest.Entries.GroupBy(e => e.MappedFolder, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > MimeSourceInspector.MaxItemsPerFolderTrialLimit)
            {
                throw new InvalidOperationException($"[ÖN KONTROL ENGELİ] Değerlendirme lisansı klasör başına en fazla {MimeSourceInspector.MaxItemsPerFolderTrialLimit} öğe desteklemektedir. '{group.Key}' klasöründe {group.Count()} öğe bulunmaktadır. Dönüştürme engellendi.");
            }
        }

        // Safeguard 1: Refuse if target PST already exists
        if (File.Exists(fullTargetPath))
        {
            throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Hedef PST dosyası zaten mevcut: '{fullTargetPath}'. Üzerine yazma kesinlikle reddedildi.");
        }

        // Safeguard 2: Selection validation
        if (selection != null)
        {
            if (!selection.CanConvert || selection.HasTrialBlocker || selection.HasPreflightBlocker)
            {
                string reason = selection.PreflightBlockerReason ?? selection.TrialBlockerReason ?? selection.BlockerReason ?? "Kaynakta ön kontrol engeli bulunmaktadır. Dönüştürme başlatılamaz.";
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

            if (!string.Equals(selection.SourceSha256, manifest.AggregateFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] Seçim parmak izi ({selection.SourceSha256}) kaynak manifestosu ({manifest.AggregateFingerprint}) ile uyuşmuyor.");
            }
        }

        var outputFolders = selection?.ExecutionPolicy is null ? null : MimeExecutionPolicy.Validate(manifest, selection);
        var frozenAdvanced = selection is null ? null : FrozenMailFilter.Validate(selection.AdvancedFilterCanonicalJson, selection.AdvancedFilterFingerprint, selection.AdvancedFilterUnknownCount, selection.DateFilterBlocked);
        void ValidateAdvancedSelection(byte[] bytes)
        {
            if (frozenAdvanced is null) return;
            using var stream = new MemoryStream(bytes, false);
            using var message = MimeMessage.Load(stream, cancellationToken);
            if (MimeAdvancedFilterAdapter.Evaluate(frozenAdvanced, message, bytes.LongLength) != MailFilterMatch.Match)
                throw new InvalidDataException("Seçili ileti donmuş gelişmiş filtreye uymuyor.");
        }

        // Safeguard 3: Revalidate source files on disk before consuming
        _inspector.RevalidateManifest(manifest);

        long sourceBytes = 0;
        foreach (var entry in manifest.Entries)
        {
            if (entry.SizeBytes < 0) throw new InvalidOperationException("[DİSK KAPASİTESİ ENGELİ] Kaynak boyutu geçersiz.");
            sourceBytes = checked(sourceBytes + entry.SizeBytes);
        }
        int capacityItems = selection?.SelectedMessagesCount ?? manifest.Entries.Count;
        long requiredBytes = DiskCapacityPlanning.EstimatePst(sourceBytes, capacityItems);
        string? capacityBlocker = DiskCapacityPlanning.CapacityBlocker(requiredBytes, _capacityProbe.Probe(fullTargetPath));
        if (capacityBlocker != null)
            throw new InvalidOperationException(capacityBlocker + " Çıktı oluşturulmadı.");

        // Step 4: Calibrate pinned SDK
        var calibration = MimeVendorCalibrator.GetCalibration();

        // Step 5: Prepare task-owned .partial output
        string targetDir = Path.GetDirectoryName(fullTargetPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(targetDir);

        // Publication Safety 1: Unique task-owned partial path; never delete existing target+'.partial' or any unknown file
        string partialPath = $"{fullTargetPath}.{jobId}.partial";

        if (File.Exists(partialPath) || Directory.Exists(partialPath))
        {
            throw new InvalidOperationException($"[ÇAKIŞMA ENGELİ] Geçici hazırlık dosyası zaten mevcut: '{partialPath}'. Üzerine yazma kesinlikle reddedildi.");
        }

        bool partialCreatedByThisInvocation = false;

        int itemsRead = 0;
        int itemsWritten = 0;
        int restoredTrailingLfCount = 0;
        int generatedCidCount = 0;
        int checkedOriginalCidCount = 0;
        int checkedAttachmentsCount = 0;
        var messageDifferences = new List<MimeMessageDifferenceSummary>();
        var expectedSnapshots = new List<ExpectedMessageSnapshot>();
        var initialSystemFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folderCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Keep the exclusive CreateNew handle through PST writing: no reservation/delete race.
            using (var outputStream = new FileStream(partialPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var pst = PersonalStorage.Create(outputStream, FileFormatVersion.Unicode, true))
            {
                partialCreatedByThisInvocation = true;
                void CaptureEmptyInitialFolders(FolderInfo folder, string parent)
                {
                    if (folder.EnumerateMessages().Any()) throw new InvalidOperationException("Yeni PST beklenmedik ileti içeriyor.");
                    foreach (FolderInfo child in folder.GetSubFolders())
                    {
                        string path = parent.Length == 0 ? child.DisplayName : parent + "/" + child.DisplayName;
                        initialSystemFolders.Add(path);
                        CaptureEmptyInitialFolders(child, path);
                    }
                }
                CaptureEmptyInitialFolders(pst.RootFolder, "");
                if (manifest.SourceKind == "mbox")
                {
                    using var fs = new FileStream(manifest.RootPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var sha = SHA256.Create();
                    int mboxOrdinal = 0;
                    foreach (var record in MboxrdRecordReader.EnumerateRecords(fs))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        mboxOrdinal++;
                        itemsRead++;

                        if (mboxOrdinal > manifest.Entries.Count)
                        {
                            throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] MBOX kayıt sayısı manifestodaki sayıdan ({manifest.Entries.Count}) fazla.");
                        }

                        var entry = manifest.Entries[mboxOrdinal - 1];

                        // Close mutation race: hash exact parsed bytes and compare with frozen manifest entry
                        string parsedSha = System.Convert.ToHexString(sha.ComputeHash(record.RawMimeBytes)).ToLowerInvariant();
                        if (!string.Equals(parsedSha, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] MBOX {entry.PhysicalOrdinal}. kayıt hash uyuşmazlığı ({parsedSha} != {entry.Sha256}).");
                        }

                        // Validate entire source including excluded messages: malformed item blocks the whole source!
                        ValidateMessageIntegrity(record.RawMimeBytes, entry);

                        if (selection != null && !selection.SelectedMessageKeys.Contains(entry.PhysicalOrdinal.ToString()))
                        {
                            continue;
                        }

                        ValidateAdvancedSelection(record.RawMimeBytes);
                        ProcessSingleMessage(
                            pst,
                            record.RawMimeBytes,
                            MimeExecutionPolicy.ForOutput(entry, outputFolders),
                            calibration,
                            ref itemsWritten,
                            ref restoredTrailingLfCount,
                            messageDifferences,
                            expectedSnapshots,
                            folderCounts);

                        progress?.Report(new ConversionJobProgress
                        {
                            JobId = jobId,
                            ItemsRead = itemsRead,
                            ItemsWritten = itemsWritten,
                            CurrentFolder = entry.MappedFolder,
                            PercentComplete = (int)((itemsRead / (double)manifest.Entries.Count) * 70)
                        });
                    }

                    if (mboxOrdinal != manifest.Entries.Count)
                    {
                        throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] MBOX kayıt sayısı manifestodaki sayıyla uyuşmuyor (beklenen: {manifest.Entries.Count}, okunan: {mboxOrdinal}).");
                    }
                }
                else
                {
                    using var sha = SHA256.Create();
                    for (int i = 0; i < manifest.Entries.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entry = manifest.Entries[i];

                        // Shared read lock prevents concurrent mutation while reading
                        byte[] rawBytes;
                        using (var eFs = new FileStream(entry.CanonicalPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            using var ms = new MemoryStream();
                            eFs.CopyTo(ms);
                            rawBytes = ms.ToArray();
                        }

                        itemsRead++;

                        // Close mutation race: hash exact read bytes and compare with frozen manifest entry
                        string readSha = System.Convert.ToHexString(sha.ComputeHash(rawBytes)).ToLowerInvariant();
                        if (!string.Equals(readSha, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] Tüketilen EML dosyası dondurulmuş manifesto hash'i ile uyuşmuyor ({entry.RelativePath}).");
                        }

                        // Validate entire source including excluded messages: malformed item blocks the whole source!
                        ValidateMessageIntegrity(rawBytes, entry);

                        if (selection != null && !selection.SelectedMessageKeys.Contains(entry.PhysicalOrdinal.ToString()))
                        {
                            continue;
                        }

                        ValidateAdvancedSelection(rawBytes);
                        ProcessSingleMessage(
                            pst,
                            rawBytes,
                            MimeExecutionPolicy.ForOutput(entry, outputFolders),
                            calibration,
                            ref itemsWritten,
                            ref restoredTrailingLfCount,
                            messageDifferences,
                            expectedSnapshots,
                            folderCounts);

                        progress?.Report(new ConversionJobProgress
                        {
                            JobId = jobId,
                            ItemsRead = itemsRead,
                            ItemsWritten = itemsWritten,
                            CurrentFolder = entry.MappedFolder,
                            PercentComplete = (int)((itemsRead / (double)manifest.Entries.Count) * 70)
                        });
                    }
                }
            } // PST closed here

            // Step 6: Closed/Reopened PST Verification
            progress?.Report(new ConversionJobProgress
            {
                JobId = jobId,
                ItemsRead = itemsRead,
                ItemsWritten = itemsWritten,
                Stage = "Doğrulanıyor",
                PercentComplete = 85
            });

            var reopenInfo = VerifyReopenedPst(
                partialPath,
                expectedSnapshots,
                initialSystemFolders,
                ref checkedAttachmentsCount,
                ref checkedOriginalCidCount,
                ref generatedCidCount);

            // Step 7: Revalidate source manifest one more time before publishing
            _inspector.RevalidateManifest(manifest);

            // Step 8: Final publication - target must still not exist
            if (File.Exists(fullTargetPath))
            {
                throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Taşıma öncesinde hedef PST dosyasının mevcut olduğu tespit edildi: '{fullTargetPath}'.");
            }

            File.Move(partialPath, fullTargetPath);

            // Step 9: Compute final output hash
            string outputSha256;
            long outputSize;
            using (var outFs = new FileStream(fullTargetPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                outputSize = outFs.Length;
                using var sha = SHA256.Create();
                outputSha256 = System.Convert.ToHexString(sha.ComputeHash(outFs)).ToLowerInvariant();
            }

            stopwatch.Stop();

            var observedModifications = new List<string>
            {
                $"Konu alanına değerlendirme soneki eklendi: '{calibration.SubjectSuffix.Trim()}'",
                "Düz metin gövdeye değerlendirme bildirimi öneki eklendi (MAPI KnownPropertyList.Body üzerinden bit düzeyinde korundu).",
                "HTML gövdeye değerlendirme bildirimi eklendi (özgün HTML korunarak SDK sarmalayıcısı muhafaza edildi)."
            };

            if (restoredTrailingLfCount > 0)
            {
                observedModifications.Add($"SDK yüklemesinde kaybolan son LF karakteri {restoredTrailingLfCount} iletide özgün kaynaktan geri yüklendi.");
            }

            if (generatedCidCount > 0)
            {
                observedModifications.Add($"SDK tarafından {generatedCidCount} ek için üretilen Content-ID üstverisi kaydedildi.");
            }

            int addedPlainCount = messageDifferences.Count(d => d.AddedPlainRepresentation);
            if (addedPlainCount > 0)
            {
                observedModifications.Add($"SDK tarafından {addedPlainCount} adet HTML-only iletiye düz metin temsili üretildi ve eklendi.");
            }

            var report = new ConversionReport
            {
                JobId = jobId,
                JobKind = "mime-import",
                EvidenceLabel = "GENUINE_MIME_IMPORT (Raw Stream Adapter Reopen Verification)",
                ClientContext = clientContext,
                SourceFileName = manifest.SourceKind == "mbox" ? Path.GetFileName(manifest.RootPath) : $"{manifest.TotalFiles} EML Dosyası",
                SourceSizeBytes = manifest.Entries.Sum(e => e.SizeBytes),
                SourceSha256Before = manifest.AggregateFingerprint,
                SourceSha256After = manifest.AggregateFingerprint,
                SourceSetFingerprint = manifest.AggregateFingerprint,
                SourceHashMatch = true,
                SourceKind = manifest.SourceKind,
                Dialect = manifest.Dialect,
                IgnoredNonEmlFilesCount = manifest.IgnoredNonEmlFilesCount,
                OutputPstFileName = Path.GetFileName(fullTargetPath),
                OutputPath = fullTargetPath,
                OutputPstSizeBytes = outputSize,
                OutputPstSha256 = outputSha256,
                ConversionSuccess = true,
                ItemsRead = itemsRead,
                ItemsWritten = itemsWritten,
                FailedItems = 0,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                TotalFoldersProcessed = folderCounts.Count,
                FidelityStatus = "DIFFERENCES",
                OverallStatus = "SUCCESS",
                IsFiltered = selection != null && selection.SelectionContentHash != "unfiltered",
                SelectionFilter = selection?.Filters,
                SelectionId = selection?.SelectionId,
                TotalSourceMessages = manifest.Entries.Count,
                TotalItems = manifest.Entries.Count,
                SelectedMessagesCount = itemsWritten,
                ExcludedMessagesCount = itemsRead - itemsWritten,
                MissingDateExcludedCount = selection?.MissingDateExcludedCount ?? 0,
                SelectedAttachmentsCount = checkedAttachmentsCount,
                TrialDifferences = new TrialDifferencesInfo
                {
                    HasObservedTrialModifications = true,
                    ObservedModificationsInThisRun = observedModifications,
                    ObservedSummary = $"Değerlendirme modu: {observedModifications.Count} adet kontrollü SDK eklemesi tespit edildi ve raporlandı."
                },
                ReopenedPstVerification = reopenInfo,
                MimeImport = new MimeImportReportDetail
                {
                    FolderMappingFingerprint = selection?.ExecutionPolicy?.MappingFingerprint,
                    DuplicatePolicy = selection?.ExecutionPolicy?.DuplicatePolicy.ToString() ?? "PreservePhysical",
                    SkippedDuplicateItemIds = selection?.ExecutionPolicy?.SkippedIds.ToList() ?? new(),
                    ExecutionPolicyFingerprint = selection?.ExecutionPolicy?.Fingerprint,
                    SourceKind = manifest.SourceKind,
                    Dialect = manifest.Dialect,
                    SourceSetFingerprint = manifest.AggregateFingerprint,
                    TotalSourceEntries = manifest.Entries.Count,
                    TotalSourceItems = manifest.Entries.Count,
                    ImportedMessagesCount = itemsWritten,
                    IgnoredNonEmlFilesCount = manifest.IgnoredNonEmlFilesCount,
                    FolderCounts = folderCounts,
                    CalibratedSubjectSuffix = calibration.SubjectSuffix,
                    CalibratedPlainPrefix = calibration.PlainPrefix,
                    CalibratedMultipartPlainPrefix = calibration.MultipartPlainPrefix,
                    CalibratedHtmlNotice = calibration.HtmlNotice,
                    CalibratedHtmlPrefix = calibration.HtmlPrefix,
                    CalibratedHtmlSuffix = calibration.HtmlSuffix,
                    RestoredTrailingLfCount = restoredTrailingLfCount,
                    GeneratedCidCount = generatedCidCount,
                    AddedPlainRepresentationCount = addedPlainCount,
                    CheckedOriginalCidCount = checkedOriginalCidCount,
                    InlineCidCount = checkedOriginalCidCount,
                    CheckedAttachmentsCount = checkedAttachmentsCount,
                    AttachmentsWrittenCount = checkedAttachmentsCount,
                    OverallQualification = "DIFFERENCES",
                    ObservedDifferences = observedModifications,
                    MessageDifferences = messageDifferences,
                    Differences = messageDifferences
                }
            };

            return report;
        }
        catch
        {
            if (partialCreatedByThisInvocation && File.Exists(partialPath))
            {
                try { File.Delete(partialPath); } catch { }
            }
            throw;
        }
    }

    private static void ProcessSingleMessage(
        PersonalStorage pst,
        byte[] rawBytes,
        MimeSourceEntry entry,
        MimeVendorCalibration calibration,
        ref int itemsWritten,
        ref int restoredTrailingLfCount,
        List<MimeMessageDifferenceSummary> messageDifferences,
        List<ExpectedMessageSnapshot> expectedSnapshots,
        Dictionary<string, int> folderCounts)
    {
        // 1. Independent MimeKit parse on raw bytes
        using var mimeMs = new MemoryStream(rawBytes);
        var mime = MimeMessage.Load(mimeMs);

        string? originalPlain = null;
        string? originalHtml = null;
        var originalAttachments = new List<ExpectedAttachmentSnapshot>();

        foreach (var entity in mime.BodyParts)
        {
            if (entity is MimePart mimePart)
            {
                string? fileName = mimePart.FileName ?? mimePart.ContentType?.Name;
                string? disp = mimePart.ContentDisposition?.Disposition?.ToLowerInvariant();
                string cid = (mimePart.ContentId ?? string.Empty).Trim('<', '>');
                bool isAttachment = !string.IsNullOrEmpty(fileName) || disp == "attachment" || !string.IsNullOrEmpty(cid);

                if (isAttachment)
                {
                    using var attMs = new MemoryStream();
                    mimePart.Content?.DecodeTo(attMs);
                    byte[] payload = attMs.ToArray();
                    string attSha = ComputeSha256Bytes(payload);
                    originalAttachments.Add(new ExpectedAttachmentSnapshot
                    {
                        Name = fileName ?? string.Empty,
                        Bytes = payload.Length,
                        Sha256 = attSha,
                        Cid = cid
                    });
                }
                else if (mimePart is TextPart textPart)
                {
                    string mediaType = textPart.ContentType?.MimeType?.ToLowerInvariant() ?? string.Empty;
                    if (mediaType == "text/plain" && originalPlain == null)
                    {
                        originalPlain = MimeVendorCalibration.Normalize(textPart.Text);
                    }
                    else if (mediaType == "text/html" && originalHtml == null)
                    {
                        originalHtml = MimeVendorCalibration.Normalize(textPart.Text);
                    }
                }
            }
        }

        // Fallback for bodies if not caught in BodyParts iteration
        if (originalPlain == null && !string.IsNullOrEmpty(mime.TextBody))
        {
            originalPlain = MimeVendorCalibration.Normalize(mime.TextBody);
        }
        if (originalHtml == null && !string.IsNullOrEmpty(mime.HtmlBody))
        {
            originalHtml = MimeVendorCalibration.Normalize(mime.HtmlBody);
        }

        // 2. Aspose load on SAME raw bytes
        using var sdkMs = new MemoryStream(rawBytes);
        using var mail = MailMessage.Load(sdkMs, new EmlLoadOptions());

        string origSubject = mime.Subject ?? string.Empty;
        string expectedSubject = mail.Subject ?? string.Empty;
        if (expectedSubject != origSubject && expectedSubject != origSubject + calibration.SubjectSuffix)
            throw new InvalidOperationException("Bilinmeyen SDK konu değişikliği.");
        bool hasPlain = originalPlain != null;
        bool hasHtml = originalHtml != null;
        string normPlain = originalPlain ?? string.Empty;
        string observedPlain = MimeVendorCalibration.Normalize(mail.Body);
        bool lfOnly = false;
        string markedPlain = hasPlain ? MimeFidelityPolicy.QualifyPlain(normPlain, observedPlain, calibration, out lfOnly) : observedPlain;
        bool exact = !lfOnly;
        if (lfOnly) restoredTrailingLfCount++;
        bool htmlLfRestored = false;
        string? markedHtml = hasHtml ? MimeFidelityPolicy.QualifyHtml(originalHtml!, MimeVendorCalibration.Normalize(mail.HtmlBody), calibration, out htmlLfRestored) : null;
        using var mapi = MapiMessage.FromMailMessage(mail, MapiConversionOptions.UnicodeFormat);
        if (markedHtml != null)
        {
            mapi.SetProperty(KnownPropertyList.TagHtml, Encoding.UTF8.GetBytes(markedHtml));
            mapi.SetProperty(KnownPropertyList.InternetCodepage, 65001);
        }
        // Write the expected value directly; MailMessage.Body may derive from HTML.
        mapi.SetProperty(KnownPropertyList.Body, markedPlain);
        string writtenPlain = MimeVendorCalibration.Normalize(mapi.Body);
        string normMapiHtml = MimeVendorCalibration.Normalize(mapi.BodyHtml);
        if (writtenPlain != markedPlain || (markedHtml != null && normMapiHtml != markedHtml))
            throw new InvalidOperationException($"MAPI gövde farkı: plain={writtenPlain == markedPlain}, html={normMapiHtml == markedHtml}, htmlLength={normMapiHtml.Length}/{markedHtml?.Length}, plainLength={writtenPlain.Length}/{markedPlain.Length}.");
        bool isPlainRepresentationAdded = !hasPlain && writtenPlain.Length > 0;
        string addedPlainRepresentation = isPlainRepresentationAdded ? "text/plain" : string.Empty;

        DateTime? expectedDate = MimeFidelityPolicy.OriginalDate(mime);
        mapi.ClientSubmitTime = expectedDate ?? DateTime.MinValue;
        mapi.DeliveryTime = expectedDate ?? DateTime.MinValue;
        if (OstSelectionEngine.ExtractMessageDate(mapi) != expectedDate)
            throw new InvalidOperationException("MAPI kaynak tarihini değiştirdi.");
        var metadataAdditions = new List<string>();
        var senders = mime.From.Mailboxes.ToArray();
        if (senders.Length > 1) throw new InvalidOperationException("Birden çok From adresi bu sürümde doğrulanamıyor.");
        if (senders.Length == 1) QualifyAddress(senders[0], mapi.SenderName, mapi.SenderEmailAddress, "from", metadataAdditions);
        QualifyRecipients(mime.To.Mailboxes, mapi, MapiRecipientType.MAPI_TO, "to", metadataAdditions);
        QualifyRecipients(mime.Cc.Mailboxes, mapi, MapiRecipientType.MAPI_CC, "cc", metadataAdditions);
        QualifyRecipients(mime.Bcc.Mailboxes, mapi, MapiRecipientType.MAPI_BCC, "bcc", metadataAdditions);
        string expectedSender = SenderSignature(mapi);
        string expectedTo = RecipientSignature(mapi, MapiRecipientType.MAPI_TO);
        string expectedCc = RecipientSignature(mapi, MapiRecipientType.MAPI_CC);
        string expectedBcc = RecipientSignature(mapi, MapiRecipientType.MAPI_BCC);
        string expectedMsgId = MessageIdValue(mapi.InternetMessageId);
        string sourceId = MessageIdValue(mime.MessageId);
        if (sourceId.Length > 0 && expectedMsgId != sourceId) throw new InvalidOperationException("MAPI özgün Message-ID değerini değiştirdi.");
        if (sourceId.Length == 0 && expectedMsgId.Length > 0) metadataAdditions.Add("generated-message-id");
        if (mapi.Subject != expectedSubject) throw new InvalidOperationException("MAPI konu alanını değiştirdi.");

        // 9. Add to target folder
        var folder = GetOrCreateFolder(pst, entry.MappedFolder);
        folder.AddMessage(mapi);
        itemsWritten++;

        folderCounts[entry.MappedFolder] = folderCounts.GetValueOrDefault(entry.MappedFolder, 0) + 1;

        // 10. Compute body hashes (do NOT retain strings in snapshot)
        string? plainHash = hasPlain ? ComputeSha256(normPlain) : null;
        string? htmlHash = originalHtml != null ? ComputeSha256(originalHtml) : null;
        string markedPlainHash = ComputeSha256(writtenPlain);
        string? mapiHtmlHash = normMapiHtml.Length > 0 ? ComputeSha256(normMapiHtml) : null;

        messageDifferences.Add(new MimeMessageDifferenceSummary
        {
            Ordinal = entry.PhysicalOrdinal,
            MessageKey = entry.PhysicalOrdinal.ToString(),
            MessageId = expectedMsgId,
            Subject = origSubject,
            ObservedSubject = expectedSubject,
            SubjectDifference = expectedSubject[origSubject.Length..],
            MetadataAdditions = metadataAdditions,
            RestoredHtmlTrailingLf = htmlLfRestored,
            AddedHtmlRepresentation = !hasHtml && normMapiHtml.Length > 0,
            MappedFolder = entry.MappedFolder,
            HasHtml = hasHtml,
            HasPlain = hasPlain,
            AddedPlainRepresentation = isPlainRepresentationAdded,
            AddedRepresentation = addedPlainRepresentation,
            InitialRawLoadExactWithVendorPrefix = exact,
            RestoredTrailingLf = lfOnly,
            AttachmentCount = originalAttachments.Count,
            OriginalCidCount = originalAttachments.Count(a => !string.IsNullOrEmpty(a.Cid)),
            GeneratedCidCount = 0,
            PlainBodySha256 = plainHash,
            HtmlBodySha256 = htmlHash
        });

        expectedSnapshots.Add(new ExpectedMessageSnapshot
        {
            Ordinal = entry.PhysicalOrdinal,
            MessageKey = entry.PhysicalOrdinal.ToString(),
            MappedFolder = entry.MappedFolder,
            Subject = expectedSubject,
            Sender = expectedSender,
            DisplayTo = expectedTo,
            DisplayCc = expectedCc,
            DisplayBcc = expectedBcc,
            MessageId = expectedMsgId,
            ExpectedDateUtc = expectedDate,
            HasPlain = true,
            PlainLength = writtenPlain.Length,
            PlainSha256 = markedPlainHash,
            AddedPlainRepresentation = isPlainRepresentationAdded,
            HasHtml = normMapiHtml.Length > 0,
            HtmlLength = normMapiHtml.Length,
            HtmlSha256 = mapiHtmlHash,
            Attachments = originalAttachments
        });
    }

    private static ReopenedPstVerificationInfo VerifyReopenedPst(
        string pstPath,
        List<ExpectedMessageSnapshot> expectedSnapshots,
        HashSet<string> initialSystemFolders,
        ref int checkedAttachmentsCount,
        ref int checkedOriginalCidCount,
        ref int generatedCidCount)
    {
        var notes = new List<string>();
        using var reopened = PersonalStorage.FromFile(pstPath);

        // Step 1: Recursive physical folder and item walk from RootFolder
        var pstItems = new List<(string FolderPath, MessageInfo MsgInfo)>();
        var allFolderPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int emptyFoldersCount = 0;

        void WalkPstFolders(FolderInfo folder, string currentPath)
        {
            if (currentPath.Length > 0) allFolderPaths.Add(currentPath);
            var messages = folder.EnumerateMessages().ToList();
            if (messages.Count > 0)
            {
                allFolderPaths.Add(currentPath);
                foreach (var msgInfo in messages)
                {
                    pstItems.Add((currentPath, msgInfo));
                }
            }
            else if (!string.IsNullOrEmpty(currentPath))
            {
                emptyFoldersCount++;
            }

            var subFolders = folder.GetSubFolders();
            if (subFolders != null)
            {
                foreach (FolderInfo sub in subFolders)
                {
                    string subName = sub.DisplayName ?? "Adsız Klasör";
                    string subPath = string.IsNullOrEmpty(currentPath) ? subName : $"{currentPath}/{subName}";
                    WalkPstFolders(sub, subPath);
                }
            }
        }

        if (reopened.RootFolder != null)
        {
            WalkPstFolders(reopened.RootFolder, string.Empty);
        }

        // Step 2: Detect extra items across the entire PST
        if (pstItems.Count != expectedSnapshots.Count)
        {
            throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] PST içindeki toplam ileti sayısı uyuşmuyor (beklenen: {expectedSnapshots.Count}, bulunan: {pstItems.Count}).");
        }

        // Step 3: Detect extra folders containing messages
        var expectedFolders = expectedSnapshots
            .Select(s => s.MappedFolder.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var leaf in expectedFolders.ToArray())
        {
            string ancestor = leaf;
            while (ancestor.Contains('/')) { ancestor = ancestor[..ancestor.LastIndexOf('/')]; expectedFolders.Add(ancestor); }
        }
        expectedFolders.UnionWith(initialSystemFolders);
        if (!allFolderPaths.SetEquals(expectedFolders)) throw new InvalidOperationException($"PST klasör kümesi uyuşmuyor. Beklenen=[{string.Join("|", expectedFolders)}], bulunan=[{string.Join("|", allFolderPaths)}]");
        foreach (var actualFolder in allFolderPaths)
        {
            if (!expectedFolders.Contains(actualFolder))
            {
                throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] PST içinde beklenmeyen klasörde ileti tespit edildi: '{actualFolder}'.");
            }
        }

        // Step 4: Consuming multiset pool match against expectedSnapshots
        var remainingExpected = new List<ExpectedMessageSnapshot>(expectedSnapshots);

        foreach (var (folderPath, msgInfo) in pstItems)
        {
            using var actual = reopened.ExtractMessage(msgInfo);
            string actualSubject = actual.Subject ?? string.Empty;
            string actualSender = SenderSignature(actual);
            string actualTo = RecipientSignature(actual, MapiRecipientType.MAPI_TO);
            string actualCc = RecipientSignature(actual, MapiRecipientType.MAPI_CC);
            string actualBcc = RecipientSignature(actual, MapiRecipientType.MAPI_BCC);
            string actualMsgId = MessageIdValue(actual.InternetMessageId);
            DateTime? actualDate = OstSelectionEngine.ExtractMessageDate(actual);

            string normActualPlain = MimeVendorCalibration.Normalize(actual.Body);
            int actualPlainLen = normActualPlain.Length;
            string actualPlainSha = ComputeSha256(normActualPlain);

            bool actualHasHtml = !string.IsNullOrEmpty(actual.BodyHtml);
            string normActualHtml = MimeVendorCalibration.Normalize(actual.BodyHtml);
            int actualHtmlLen = actualHasHtml ? normActualHtml.Length : 0;
            string? actualHtmlSha = actualHasHtml ? ComputeSha256(normActualHtml) : null;

            var actualAttachments = new List<(string Name, long Bytes, string Sha256, string Cid)>();
            if (actual.Attachments != null)
            {
                foreach (MapiAttachment att in actual.Attachments)
                {
                    string attName = !string.IsNullOrEmpty(att.LongFileName)
                        ? att.LongFileName
                        : (!string.IsNullOrEmpty(att.FileName) ? att.FileName : (att.DisplayName ?? "attachment"));
                    byte[] data = att.BinaryData ?? Array.Empty<byte>();
                    string attSha = ComputeSha256Bytes(data);
                    string cid = AttachmentHelper.GetAttachmentContentId(att) ?? string.Empty;
                    actualAttachments.Add((attName, data.Length, attSha, cid));
                }
            }

            List<string> Mismatches(ExpectedMessageSnapshot exp)
            {
                var fields = new List<string>();
                if (!string.Equals(exp.MappedFolder.Replace('\\', '/'), folderPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)) fields.Add("folder");
                if (exp.Subject != actualSubject) fields.Add("subject");
                if (exp.Sender != actualSender) fields.Add("from");
                if (exp.DisplayTo != actualTo) fields.Add("to");
                if (exp.DisplayCc != actualCc) fields.Add("cc");
                if (exp.DisplayBcc != actualBcc) fields.Add("bcc");
                if (exp.MessageId != actualMsgId) fields.Add("message-id");
                if (exp.ExpectedDateUtc != actualDate) fields.Add("date");
                if (exp.PlainLength != actualPlainLen || exp.PlainSha256 != actualPlainSha) fields.Add("plain-body");
                if (exp.HasHtml != actualHasHtml || exp.HtmlLength != actualHtmlLen || exp.HtmlSha256 != actualHtmlSha) fields.Add("html-body");
                var expectedAtts = exp.Attachments.Select(a => (a.Name, a.Bytes, a.Sha256, a.Cid)).OrderBy(a => a.Name, StringComparer.Ordinal).ThenBy(a => a.Sha256, StringComparer.Ordinal).ThenBy(a => a.Cid, StringComparer.Ordinal);
                var actualAtts = actualAttachments.OrderBy(a => a.Name, StringComparer.Ordinal).ThenBy(a => a.Sha256, StringComparer.Ordinal).ThenBy(a => a.Cid, StringComparer.Ordinal);
                if (!expectedAtts.SequenceEqual(actualAtts)) fields.Add("attachments-name-size-hash-cid");
                return fields;
            }
            int matchIdx = remainingExpected.FindIndex(exp => Mismatches(exp).Count == 0);
            if (matchIdx < 0)
            {
                var closest = remainingExpected.Select(exp => Mismatches(exp)).OrderBy(f => f.Count).FirstOrDefault();
                throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] PST geri okuma uyuşmazlığı: {string.Join(", ", closest ?? new List<string> { "extra-item" })}; klasör '{folderPath}'.");
            }

            var matched = remainingExpected[matchIdx];
            remainingExpected.RemoveAt(matchIdx);

            checkedAttachmentsCount += matched.Attachments.Count;
            foreach (var att in matched.Attachments)
            {
                if (!string.IsNullOrEmpty(att.Cid))
                {
                    checkedOriginalCidCount++;
                }
            }

            for (int a = 0; a < actualAttachments.Count; a++)
            {
                var actAtt = actualAttachments[a];
                var expAtt = matched.Attachments[a];
                if (string.IsNullOrEmpty(expAtt.Cid) && !string.IsNullOrEmpty(actAtt.Cid))
                {
                    generatedCidCount++;
                }
            }
        }

        if (remainingExpected.Count > 0)
        {
            throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] {remainingExpected.Count} adet beklenen ileti PST içinde bulunamadı.");
        }

        notes.Add($"Tüm {pstItems.Count} ileti ve {checkedAttachmentsCount} ek fiziksel multiset taraması ve ikili hash'leriyle başarıyla doğrulandı.");
        if (checkedOriginalCidCount > 0)
        {
            notes.Add($"{checkedOriginalCidCount} özgün inline Content-ID tam uyumla doğrulandı.");
        }

        var activePaths = pstItems.Select(item => item.FolderPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int emptySystemFolders = initialSystemFolders.Count(path => !activePaths.Contains(path));
        notes.Add($"Yeni PST'nin {emptySystemFolders} boş sistem klasörü kaynak iletisi sayılmaz.");
        return new ReopenedPstVerificationInfo
        {
            VerifiedWith = "Aspose.Email 24.8 (Strictly SAME_SDK_ONLY)",
            VerificationSuccess = true,
            TotalFoldersFound = allFolderPaths.Count,
            ActiveFoldersFound = activePaths.Count,
            EmptyFoldersFound = emptyFoldersCount - emptySystemFolders,
            SystemFoldersFound = emptySystemFolders,
            TotalPhysicalItemsFound = pstItems.Count,
            ItemCountMatch = true,
            TotalAttachmentsVerified = checkedAttachmentsCount,
            TotalCidVerified = checkedOriginalCidCount,
            VerificationNotes = notes
        };
    }

    private static string MessageIdValue(string? value) => (value ?? string.Empty).Trim().Trim('<', '>');
    private static string SenderSignature(MapiMessage m) => System.Text.Json.JsonSerializer.Serialize(new[] { m.SenderName ?? "", m.SenderEmailAddress ?? "" });
    private static string RecipientSignature(MapiMessage m, MapiRecipientType type) => System.Text.Json.JsonSerializer.Serialize(m.Recipients.Cast<MapiRecipient>().Where(r => r.RecipientType == type).Select(r => new[] { r.DisplayName ?? "", r.EmailAddress ?? "" }).ToArray());
    private static void QualifyAddress(MailboxAddress source, string? name, string? address, string field, List<string> additions)
    {
        if (source.Address != (address ?? "")) throw new InvalidOperationException("SDK özgün adresi değiştirdi: " + field);
        if (source.Name == (name ?? "")) return;
        if (string.IsNullOrEmpty(source.Name) && name == source.Address) { additions.Add("generated-display-name:" + field); return; }
        throw new InvalidOperationException("SDK özgün görünen adı değiştirdi: " + field);
    }
    private static void QualifyRecipients(IEnumerable<MailboxAddress> source, MapiMessage message, MapiRecipientType type, string field, List<string> additions)
    {
        var originals = source.ToArray();
        var actual = message.Recipients.Cast<MapiRecipient>().Where(r => r.RecipientType == type).ToArray();
        if (originals.Length != actual.Length) throw new InvalidOperationException("SDK alıcı sayısını değiştirdi: " + field);
        for (int i = 0; i < originals.Length; i++) QualifyAddress(originals[i], actual[i].DisplayName, actual[i].EmailAddress, field, additions);
    }

    private static string ComputeSha256(string text)
    {
        using var sha = SHA256.Create();
        return System.Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string ComputeSha256Bytes(byte[] data)
    {
        using var sha = SHA256.Create();
        return System.Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
    }

    private static void ValidateMessageIntegrity(byte[] rawBytes, MimeSourceEntry entry)
    {
        if (rawBytes == null || rawBytes.Length == 0)
        {
            throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] Kaynak ileti dosyası boş ({entry.RelativePath}).");
        }

        MimeMessage mime;
        try
        {
            using var ms = new MemoryStream(rawBytes);
            mime = MimeMessage.Load(ms);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] Bozuk MIME iletisi okunamadı ({entry.RelativePath}): {ex.Message}", ex);
        }

        if (mime.Headers.Count == 0)
        {
            throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] MIME üstbilgileri bulunamadı ({entry.RelativePath}).");
        }

        string rawAscii = Encoding.ASCII.GetString(rawBytes);
        ValidateMultipartBoundaries(mime.Body, rawAscii, entry.RelativePath);

        foreach (var entity in mime.BodyParts)
        {
            if (entity is MimePart part)
            {
                bool isBase64 = part.ContentTransferEncoding == ContentEncoding.Base64 ||
                                string.Equals(part.Headers[HeaderId.ContentTransferEncoding], "base64", StringComparison.OrdinalIgnoreCase);

                if (isBase64 && part.Content != null)
                {
                    using var rawPartMs = new MemoryStream();
                    part.Content.WriteTo(rawPartMs);
                    string rawBase64 = Encoding.ASCII.GetString(rawPartMs.ToArray());
                    string cleaned = rawBase64.Replace("\r", "").Replace("\n", "").Replace(" ", "").Replace("\t", "");
                    if (cleaned.Length > 0)
                    {
                        try
                        {
                            System.Convert.FromBase64String(cleaned);
                        }
                        catch (FormatException ex)
                        {
                            throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] Hatalı base64 kodlaması tespit edildi ({entry.RelativePath}): {ex.Message}", ex);
                        }
                    }
                }
            }
        }

        try
        {
            using var sdkMs = new MemoryStream(rawBytes);
            using var mail = MailMessage.Load(sdkMs, new EmlLoadOptions());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] Aspose SDK iletisini yükleyemedi ({entry.RelativePath}): {ex.Message}", ex);
        }
    }

    private static void ValidateMultipartBoundaries(MimeEntity? entity, string rawText, string relativePath)
    {
        if (entity is Multipart multipart)
        {
            if (string.IsNullOrWhiteSpace(multipart.Boundary))
            {
                throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] Multipart boundary tanımlanmamış ({relativePath}).");
            }

            string closingBoundary = $"--{multipart.Boundary}--";
            if (!rawText.Contains(closingBoundary, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"[BÜTÜNLÜK ENGELİ] Multipart kapanış sınırı eksik veya hatalı: '{closingBoundary}' ({relativePath}).");
            }

            foreach (var sub in multipart)
            {
                ValidateMultipartBoundaries(sub, rawText, relativePath);
            }
        }
    }

    private static FolderInfo GetOrCreateFolder(PersonalStorage pst, string folderPath)
    {
        var parts = folderPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var current = pst.RootFolder;
        foreach (var part in parts)
        {
            var existing = current.GetSubFolder(part);
            current = existing ?? current.AddSubFolder(part);
        }
        return current;
    }

    private static FolderInfo? FindFolder(PersonalStorage pst, string folderPath)
    {
        var parts = folderPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var current = pst.RootFolder;
        foreach (var part in parts)
        {
            current = current.GetSubFolder(part);
            if (current == null) return null;
        }
        return current;
    }

    private sealed class ExpectedAttachmentSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public long Bytes { get; set; }
        public string Sha256 { get; set; } = string.Empty;
        public string Cid { get; set; } = string.Empty;
    }

    private sealed class ExpectedMessageSnapshot
    {
        public int Ordinal { get; set; }
        public string MessageKey { get; set; } = string.Empty;
        public string MappedFolder { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Sender { get; set; } = string.Empty;
        public string DisplayTo { get; set; } = string.Empty;
        public string DisplayCc { get; set; } = string.Empty;
        public string DisplayBcc { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public DateTime? ExpectedDateUtc { get; set; }
        public bool HasPlain { get; set; }
        public int PlainLength { get; set; }
        public string? PlainSha256 { get; set; }
        public bool AddedPlainRepresentation { get; set; }
        public bool HasHtml { get; set; }
        public int HtmlLength { get; set; }
        public string? HtmlSha256 { get; set; }
        public List<ExpectedAttachmentSnapshot> Attachments { get; set; } = new();
    }
}
