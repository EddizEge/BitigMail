using BitigMail.Engine.Planning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BitigMail.Engine.Models;
using MimeKit;

namespace BitigMail.Engine.Storage;

public static class MimeSelectionEngine
{
    public static (SelectionPreviewResult Preview, RegisteredSelection Registered) EvaluateSelection(
        MimeSourceManifest manifest,
        string sourceHandle,
        List<string>? requestedFolderIds,
        string? startDate,
        string? endDate,
        PreflightCheckResult preflight,
        CancellationToken cancellationToken,
        MailFilterDefinition? advancedFilter = null,
        IReadOnlyList<FolderMappingRule>? folderMappings = null,
        DuplicatePolicy duplicatePolicy = DuplicatePolicy.PreservePhysical)
    {
        OstSelectionEngine.ParseDateBoundaries(startDate, endDate, out DateTime? startUtc, out DateTime? endExclusiveUtc);
        bool hasDateFilter = startUtc.HasValue || endExclusiveUtc.HasValue;
        var qualification = new NormalizedSourceQualificationReader().Read(manifest, cancellationToken);
        var compiledAdvanced = advancedFilter is null ? null : AdvancedMailFilter.Compile(advancedFilter,
            new HashSet<string>(["subject","body","sender","recipient","attachmentName","hasAttachment","size","date"],StringComparer.Ordinal),
            qualification?.DateFilterBlocked == true);

        var filter = new ConversionSelectionFilter
        {
            FolderIds = requestedFolderIds?.Distinct().ToList() ?? new List<string>(),
            StartDate = startDate?.Trim(),
            EndDate = endDate?.Trim(),
            DatePolicy = "OriginalMimeDate_ExplicitZone_UtcPlus3_Inclusive",
            TimeZone = OstSelectionEngine.DefaultTimeZone
        };

        bool hasFolderFilter = filter.FolderIds.Count > 0;
        bool hasExecutionPolicy = folderMappings?.Count > 0 || duplicatePolicy != DuplicatePolicy.PreservePhysical;
        bool isFiltered = hasFolderFilter || hasDateFilter || compiledAdvanced is not null || hasExecutionPolicy;

        // Build folderId to folderName mapping
        var folderIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var folderGrouped = manifest.Entries
            .GroupBy(e => e.MappedFolder, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var folderName in folderGrouped.Keys)
        {
            string fId = MimeSourceInspector.ComputeFolderId(manifest.AggregateFingerprint, folderName);
            folderIdMap[fId] = folderName;
        }

        IEnumerable<string> snapshotIds = hasFolderFilter ? filter.FolderIds : folderIdMap.Keys;
        filter.SelectedFolders = snapshotIds.Where(folderIdMap.ContainsKey).Select(id => new SelectedFolderSnapshot
        {
            FolderId = id,
            FolderPath = folderIdMap[id],
            DisplayName = folderIdMap[id]
        }).ToList();

        HashSet<string>? targetFolders = null;
        if (hasFolderFilter)
        {
            targetFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fId in filter.FolderIds!)
            {
                if (folderIdMap.TryGetValue(fId, out var fName))
                {
                    targetFolders.Add(fName);
                }
            }
        }

        var selectedKeys = new HashSet<string>(StringComparer.Ordinal);
        var folderBreakdownMap = new Dictionary<string, (int Total, int Selected)>(StringComparer.OrdinalIgnoreCase);
        foreach (var fName in folderGrouped.Keys)
        {
            folderBreakdownMap[fName] = (folderGrouped[fName].Count, 0);
        }

        int totalMessages = manifest.Entries.Count;
        int selectedCount = 0;
        int excludedCount = 0;
        int missingDateExcludedCount = 0;
        int selectedAttachmentsCount = 0;
        var attachmentsByOrdinal = new Dictionary<string, int>(StringComparer.Ordinal);
        int advancedUnknownCount = 0;

        bool AdvancedMatches(MimeMessage mime,long rawSize)
        {
            if(compiledAdvanced is null)return true;
            var names=mime.Attachments.Select(x=>x.ContentDisposition?.FileName??x.ContentType.Name??string.Empty).ToArray();
            string fullBody=mime.TextBody??(mime.HtmlBody is null?string.Empty:BitigMail.Engine.Archive.ArchiveMimeParser.ExtractTextFromHtml(mime.HtmlBody));
            var result=compiledAdvanced.Evaluate(new(mime.Subject??string.Empty,fullBody,
                mime.From.Mailboxes.Select(x=>x.Address).ToArray(),mime.To.Mailboxes.Concat(mime.Cc.Mailboxes).Concat(mime.Bcc.Mailboxes).Select(x=>x.Address).ToArray(),
                MimeFidelityPolicy.OriginalDate(mime) is DateTime d?new DateTimeOffset(DateTime.SpecifyKind(d,DateTimeKind.Utc)):null,
                rawSize,names.Length>0,names));
            if(result==MailFilterMatch.UnknownMetadata){advancedUnknownCount++;return false;}
            return result==MailFilterMatch.Match;
        }

        // Extract dates and attachments for filtering
        if (manifest.SourceKind == "mbox")
        {
            using var fs = new FileStream(manifest.RootPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            int idx = 0;
            foreach (var record in MboxrdRecordReader.EnumerateRecords(fs))
            {
                cancellationToken.ThrowIfCancellationRequested();
                idx++;
                var entry = manifest.Entries[idx - 1];
                string folderName = entry.MappedFolder;

                bool folderMatches = targetFolders == null || targetFolders.Contains(folderName);
                if (!folderMatches)
                {
                    excludedCount++;
                    continue;
                }

                using var ms = new MemoryStream(record.RawMimeBytes);
                var mime = MimeMessage.Load(ms);
                if(!AdvancedMatches(mime,record.RawMimeBytes.LongLength)){excludedCount++;continue;}

                DateTime? msgDate = MimeFidelityPolicy.OriginalDate(mime);

                if (hasDateFilter)
                {
                    if (!msgDate.HasValue)
                    {
                        missingDateExcludedCount++;
                        excludedCount++;
                        continue;
                    }

                    if (startUtc.HasValue && msgDate.Value < startUtc.Value)
                    {
                        excludedCount++;
                        continue;
                    }

                    if (endExclusiveUtc.HasValue && msgDate.Value >= endExclusiveUtc.Value)
                    {
                        excludedCount++;
                        continue;
                    }
                }

                selectedCount++;
                selectedKeys.Add(entry.PhysicalOrdinal.ToString());
                int attCount = MimeAttachmentInventory.CountTopLevelAttachments(mime);
                selectedAttachmentsCount += attCount;
                attachmentsByOrdinal[entry.PhysicalOrdinal.ToString()] = attCount;

                var current = folderBreakdownMap[folderName];
                folderBreakdownMap[folderName] = (current.Total, current.Selected + 1);
            }
        }
        else
        {
            for (int i = 0; i < manifest.Entries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = manifest.Entries[i];
                string folderName = entry.MappedFolder;

                bool folderMatches = targetFolders == null || targetFolders.Contains(folderName);
                if (!folderMatches)
                {
                    excludedCount++;
                    continue;
                }

                using var fs = new FileStream(entry.CanonicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var mime = MimeMessage.Load(fs);
                if(!AdvancedMatches(mime,entry.SizeBytes)){excludedCount++;continue;}

                DateTime? msgDate = MimeFidelityPolicy.OriginalDate(mime);

                if (hasDateFilter)
                {
                    if (!msgDate.HasValue)
                    {
                        missingDateExcludedCount++;
                        excludedCount++;
                        continue;
                    }

                    if (startUtc.HasValue && msgDate.Value < startUtc.Value)
                    {
                        excludedCount++;
                        continue;
                    }

                    if (endExclusiveUtc.HasValue && msgDate.Value >= endExclusiveUtc.Value)
                    {
                        excludedCount++;
                        continue;
                    }
                }

                selectedCount++;
                selectedKeys.Add(entry.PhysicalOrdinal.ToString());
                int attCount = MimeAttachmentInventory.CountTopLevelAttachments(mime);
                selectedAttachmentsCount += attCount;
                attachmentsByOrdinal[entry.PhysicalOrdinal.ToString()] = attCount;

                var current = folderBreakdownMap[folderName];
                folderBreakdownMap[folderName] = (current.Total, current.Selected + 1);
            }
        }

        MimeExecutionPolicySnapshot? executionPolicy = null;
        if (hasExecutionPolicy)
        {
            executionPolicy = MimeExecutionPolicy.Freeze(manifest, selectedKeys, folderMappings ?? [], duplicatePolicy);
            selectedKeys = executionPolicy.IncludedIds.ToHashSet(StringComparer.Ordinal);
            selectedCount = selectedKeys.Count;
            excludedCount = totalMessages - selectedCount;
            selectedAttachmentsCount = selectedKeys.Sum(id => attachmentsByOrdinal[id]);
            var selectedFolderCounts = manifest.Entries.Where(e => selectedKeys.Contains(e.PhysicalOrdinal.ToString()))
                .GroupBy(e => e.MappedFolder, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (string folder in folderBreakdownMap.Keys.ToArray())
                folderBreakdownMap[folder] = (folderBreakdownMap[folder].Total, selectedFolderCounts.GetValueOrDefault(folder));
        }

        var folderBreakdown = new List<FolderSelectionSummary>();
        foreach (var (folderName, (tot, sel)) in folderBreakdownMap)
        {
            string fId = MimeSourceInspector.ComputeFolderId(manifest.AggregateFingerprint, folderName);
            folderBreakdown.Add(new FolderSelectionSummary
            {
                FolderId = fId,
                FolderPath = folderName,
                DisplayName = folderName,
                TotalItems = tot,
                SelectedItems = sel,
                IsSelected = sel > 0
            });
        }

        bool hasFolderExceedingTrial = false;
        string? trialFolderReason = null;
        foreach (var (folderName, entries) in folderGrouped)
        {
            if (entries.Count > MimeSourceInspector.MaxItemsPerFolderTrialLimit)
            {
                hasFolderExceedingTrial = true;
                trialFolderReason = $"[ÖN KONTROL ENGELİ] Değerlendirme lisansı klasör başına en fazla {MimeSourceInspector.MaxItemsPerFolderTrialLimit} öğe desteklemektedir. '{folderName}' klasöründe {entries.Count} öğe bulunmaktadır. Dönüştürme engellendi.";
                break;
            }
        }

        bool hasTrialBlocker = preflight.HasTrialBlocker || hasFolderExceedingTrial;
        bool canConvert = preflight.CanConvert && !hasFolderExceedingTrial;
        string? blockerReason = trialFolderReason ?? preflight.TrialBlockerReason;
        if (hasDateFilter && qualification?.DateFilterBlocked == true)
        {
            canConvert = false;
            blockerReason = "[TARİH FİLTRESİ ENGELİ] Kaynak dönüşümünün özgün tarih sadakati doğrulanmadı; tarih filtresi kullanılamaz.";
        }

        if (canConvert && selectedCount == 0)
        {
            canConvert = false;
            blockerReason = "Seçim kriterlerinize uyan hiçbir ileti bulunamadı. Sıfır iletili dönüştürme başlatılamaz.";
        }

        string selectionId = "msel_" + Guid.NewGuid().ToString("N");
        string legacyHash = OstSelectionEngine.ComputeSelectionContentHash(isFiltered, filter);
        string advancedIdentity = string.Join("\n", selectedKeys.OrderBy(x=>x,StringComparer.Ordinal));
        string selectionContentHash = compiledAdvanced is null ? legacyHash : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{legacyHash}\n{compiledAdvanced.Fingerprint}\n{advancedIdentity}"))).ToLowerInvariant();

        if (executionPolicy is not null) selectionContentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(selectionContentHash + "\n" + executionPolicy.Fingerprint))).ToLowerInvariant();

        var preview = new SelectionPreviewResult
        {
            FolderMappingFingerprint = executionPolicy?.MappingFingerprint,
            DuplicatePolicy = duplicatePolicy.ToString(),
            SkippedDuplicateItemIds = executionPolicy?.SkippedIds.ToList() ?? new(),
            ExecutionPolicyFingerprint = executionPolicy?.Fingerprint,
            SelectionId = selectionId,
            SourceSha256 = manifest.AggregateFingerprint,
            Filters = filter,
            TotalSourceMessages = totalMessages,
            SelectedMessagesCount = selectedCount,
            ExcludedMessagesCount = excludedCount,
            SelectedAttachmentsCount = selectedAttachmentsCount,
            MissingDateExcludedCount = missingDateExcludedCount,
            TotalFoldersCount = folderGrouped.Count,
            SelectedFoldersCount = folderBreakdown.Count(f => f.SelectedItems > 0),
            CanConvert = canConvert,
            BlockerReason = blockerReason,
            FolderBreakdown = folderBreakdown
            ,DateFilterBlocked = qualification?.DateFilterBlocked ?? false
            ,QualificationWarnings = qualification?.Warnings.ToList() ?? new()
            ,SdkQualification = AsposeSdkStartupService.CurrentStatus.Qualification
            ,AdvancedFilterFingerprint = compiledAdvanced?.Fingerprint
            ,AdvancedFilterUnknownCount = advancedUnknownCount
        };

        var registered = new RegisteredSelection
        {
            ExecutionPolicy = executionPolicy,
            SelectionId = selectionId,
            SourceHandle = sourceHandle,
            SourceSha256 = manifest.AggregateFingerprint,
            Filters = filter,
            SelectedFolderIds = new HashSet<string>(hasFolderFilter ? filter.FolderIds : folderIdMap.Keys, StringComparer.Ordinal),
            SelectedMessageKeys = selectedKeys,
            TotalSourceMessages = totalMessages,
            SelectedMessagesCount = selectedCount,
            ExcludedMessagesCount = excludedCount,
            SelectedAttachmentsCount = selectedAttachmentsCount,
            MissingDateExcludedCount = missingDateExcludedCount,
            CanConvert = canConvert,
            HasTrialBlocker = hasTrialBlocker,
            TrialBlockerReason = blockerReason,
            SelectionContentHash = selectionContentHash,
            CreatedAt = DateTimeOffset.UtcNow
            ,QualificationFingerprint = qualification?.Fingerprint
            ,DateFilterBlocked = qualification?.DateFilterBlocked ?? false
            ,QualificationIsPartial = qualification?.IsPartial ?? false
            ,QualificationWarnings = qualification?.Warnings.ToList() ?? new()
            ,SdkQualification = AsposeSdkStartupService.CurrentStatus.Qualification
            ,AdvancedFilterCanonicalJson = compiledAdvanced?.CanonicalJson
            ,AdvancedFilterFingerprint = compiledAdvanced?.Fingerprint
            ,AdvancedFilterUnknownCount = advancedUnknownCount
        };

        return (preview, registered);
    }
}
