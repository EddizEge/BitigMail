using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Aspose.Email.Mapi;
using Aspose.Email.Storage.Pst;
using BitigMail.Engine.Models;

namespace BitigMail.Engine.Storage;

public static class OstSelectionEngine
{
    public const string DefaultTimeZone = "Europe/Istanbul (UTC+03:00)";
    public const string DefaultDatePolicy = "SubmissionDateThenDeliveryDate_UtcPlus3_Inclusive";

    public static string ComputeFolderId(string sourceSha256, string rawEntryId)
    {
        using var sha = SHA256.Create();
        byte[] bytes = Encoding.UTF8.GetBytes(sourceSha256.ToLowerInvariant() + ":" + rawEntryId);
        return "fld_" + System.Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant()[..16];
    }

    public static string ComputeSelectionContentHash(bool isFiltered, ConversionSelectionFilter? filter)
    {
        if (!isFiltered || filter == null)
        {
            return "unfiltered";
        }

        var sortedFolderIds = (filter.FolderIds ?? new List<string>())
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        string canonicalString = string.Join("|", new[]
        {
            "filtered",
            string.Join(",", sortedFolderIds),
            filter.StartDate ?? "",
            filter.EndDate ?? "",
            filter.DatePolicy ?? DefaultDatePolicy,
            filter.TimeZone ?? DefaultTimeZone
        });

        using var sha = SHA256.Create();
        byte[] bytes = Encoding.UTF8.GetBytes(canonicalString);
        return System.Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
    }

    public static void ParseDateBoundaries(
        string? startDate,
        string? endDate,
        out DateTime? startUtc,
        out DateTime? endExclusiveUtc)
    {
        startUtc = null;
        endExclusiveUtc = null;

        DateTime? parsedStart = null;
        DateTime? parsedEnd = null;

        if (!string.IsNullOrWhiteSpace(startDate))
        {
            if (!DateTime.TryParseExact(startDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var s))
            {
                throw new ArgumentException($"Geçersiz başlangıç tarihi formatı ('{startDate}'). YYYY-MM-DD bekleniyor.", nameof(startDate));
            }
            parsedStart = s.Date;
            // In Türkiye (UTC+03:00), 00:00:00 local is 21:00:00 previous day UTC
            startUtc = DateTime.SpecifyKind(parsedStart.Value.AddHours(-3), DateTimeKind.Utc);
        }

        if (!string.IsNullOrWhiteSpace(endDate))
        {
            if (!DateTime.TryParseExact(endDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var e))
            {
                throw new ArgumentException($"Geçersiz bitiş tarihi formatı ('{endDate}'). YYYY-MM-DD bekleniyor.", nameof(endDate));
            }
            parsedEnd = e.Date;
            // In Türkiye (UTC+03:00), inclusive day ends at next day 00:00:00 local, which is 21:00:00 current day UTC
            endExclusiveUtc = DateTime.SpecifyKind(parsedEnd.Value.AddDays(1).AddHours(-3), DateTimeKind.Utc);
        }

        if (parsedStart.HasValue && parsedEnd.HasValue && parsedStart.Value > parsedEnd.Value)
        {
            throw new ArgumentException($"Başlangıç tarihi ({startDate}) bitiş tarihinden ({endDate}) sonra olamaz.", nameof(startDate));
        }
    }

    public static DateTime NormalizeToUtc(DateTime date) => date.Kind switch
    {
        DateTimeKind.Utc => date,
        DateTimeKind.Local => date.ToUniversalTime(),
        DateTimeKind.Unspecified or _ => DateTime.SpecifyKind(date, DateTimeKind.Utc)
    };

    public static DateTime? ExtractMessageDate(MapiMessage msg)
    {
        DateTime date = DateTime.MinValue;
        if (msg.ClientSubmitTime != DateTime.MinValue && msg.ClientSubmitTime.Year > 1601)
        {
            date = msg.ClientSubmitTime;
        }
        else if (msg.DeliveryTime != DateTime.MinValue && msg.DeliveryTime.Year > 1601)
        {
            date = msg.DeliveryTime;
        }

        if (date == DateTime.MinValue || date.Year <= 1601)
        {
            return null;
        }

        return NormalizeToUtc(date);
    }

    public static PreflightCheckResult InspectPreflightAuthoritatively(
        PersonalStorage storage,
        string sourceSha256 = "",
        long sizeBytes = 0,
        CancellationToken cancellationToken = default)
    {
        var analyzer = new OstAnalyzer();
        var analysis = analyzer.AnalyzeStorageCore(
            storage,
            fileName: "source",
            sizeBytes: sizeBytes,
            sha256: sourceSha256,
            cancellationToken: cancellationToken);
        return analysis.Preflight;
    }

    public static (SelectionPreviewResult Preview, RegisteredSelection Registered) EvaluateSelection(
        PersonalStorage ost,
        string sourceHandle,
        string sourceSha256,
        List<string>? folderIds,
        string? startDate,
        string? endDate,
        PreflightCheckResult? preflight,
        CancellationToken cancellationToken = default)
    {
        // 1. Enumerate all folders to establish authoritative folder identities
        var allFolders = new List<(string FolderId, string FolderPath, string DisplayName, FolderInfo Folder)>();
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Root folder
        CollectFoldersRecursive(
            ost,
            ost.RootFolder,
            parentPath: "",
            isRoot: true,
            sourceSha256,
            allFolders,
            usedPaths,
            cancellationToken);

        var allFolderMap = allFolders.ToDictionary(f => f.FolderId, StringComparer.Ordinal);

        // 2. Validate folder selection
        HashSet<string> selectedFolderIds;
        if (folderIds == null)
        {
            // Default: All folders selected
            selectedFolderIds = new HashSet<string>(allFolderMap.Keys, StringComparer.Ordinal);
        }
        else if (folderIds.Count == 0)
        {
            // Explicit empty selection: exactly zero folders selected
            selectedFolderIds = new HashSet<string>(StringComparer.Ordinal);
        }
        else
        {
            var unknownIds = folderIds.Where(id => !allFolderMap.ContainsKey(id)).ToList();
            if (unknownIds.Count > 0)
            {
                throw new ArgumentException($"Bilinmeyen klasör kimliği belirtildi: {string.Join(", ", unknownIds)}");
            }
            selectedFolderIds = new HashSet<string>(folderIds, StringComparer.Ordinal);
        }

        // 3. Parse date bounds
        ParseDateBoundaries(startDate, endDate, out var startUtc, out var endExclusiveUtc);
        bool isDateFilterActive = startUtc.HasValue || endExclusiveUtc.HasValue;

        var selectedFolderSnapshots = allFolders
            .Where(f => selectedFolderIds.Contains(f.FolderId))
            .Select(f => new SelectedFolderSnapshot
            {
                FolderId = f.FolderId,
                FolderPath = f.FolderPath,
                DisplayName = f.DisplayName
            })
            .OrderBy(f => f.FolderPath, StringComparer.Ordinal)
            .ToList();

        var canonicalFilter = new ConversionSelectionFilter
        {
            FolderIds = selectedFolderIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            SelectedFolders = selectedFolderSnapshots,
            StartDate = !string.IsNullOrWhiteSpace(startDate) ? startDate.Trim() : null,
            EndDate = !string.IsNullOrWhiteSpace(endDate) ? endDate.Trim() : null,
            TimeZone = DefaultTimeZone,
            DatePolicy = DefaultDatePolicy
        };

        // 4. Iterate physical items across all folders
        int totalSourceMessages = 0;
        int selectedMessagesCount = 0;
        int selectedAttachmentsCount = 0;
        int missingDateExcludedCount = 0;
        var selectedMessageKeys = new HashSet<string>(StringComparer.Ordinal);
        var folderBreakdown = new List<FolderSelectionSummary>();

        foreach (var (fId, fPath, fDisplay, fInfo) in allFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<MessageInfo> enumerated;
            try
            {
                enumerated = fInfo.EnumerateMessages();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"'{fPath}' klasöründeki iletiler taranamadı: {ex.Message}", ex);
            }

            var msgList = enumerated != null ? enumerated.ToList() : new List<MessageInfo>();
            int folderTotal = msgList.Count;
            int folderSelected = 0;
            bool isFolderSelected = selectedFolderIds.Contains(fId);

            totalSourceMessages += folderTotal;

            foreach (var mi in msgList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string physicalKey = $"m_{fId}_{mi.EntryIdString}";

                if (!isFolderSelected)
                {
                    // Excluded by folder
                    continue;
                }

                // Folder is selected: inspect message date
                using var msg = ost.ExtractMessage(mi);
                if (msg == null)
                {
                    throw new InvalidOperationException($"'{fPath}' klasöründeki {mi.EntryIdString} öğesi okunamadı.");
                }

                DateTime? msgDate = ExtractMessageDate(msg);
                int attCount = msg.Attachments?.Count ?? 0;

                if (!msgDate.HasValue)
                {
                    if (isDateFilterActive)
                    {
                        // Excluded due to missing date when date filter is active
                        missingDateExcludedCount++;
                    }
                    else
                    {
                        // Included without date filter
                        folderSelected++;
                        selectedAttachmentsCount += attCount;
                        selectedMessageKeys.Add(physicalKey);
                    }
                }
                else
                {
                    bool inRange = (!startUtc.HasValue || msgDate.Value >= startUtc.Value) &&
                                   (!endExclusiveUtc.HasValue || msgDate.Value < endExclusiveUtc.Value);

                    if (inRange)
                    {
                        folderSelected++;
                        selectedAttachmentsCount += attCount;
                        selectedMessageKeys.Add(physicalKey);
                    }
                }
            }

            selectedMessagesCount += folderSelected;

            folderBreakdown.Add(new FolderSelectionSummary
            {
                FolderId = fId,
                FolderPath = fPath,
                DisplayName = fDisplay,
                TotalItems = folderTotal,
                SelectedItems = folderSelected,
                IsSelected = isFolderSelected
            });
        }

        int excludedMessagesCount = totalSourceMessages - selectedMessagesCount;

        // 5. Evaluate CanConvert & BlockerReason across whole source
        bool hasWholeSourceBlocker = preflight != null && (!preflight.CanConvert || preflight.HasTrialBlocker || (preflight.Blockers != null && preflight.Blockers.Count > 0));
        string? wholeSourceBlockerReason = null;
        if (hasWholeSourceBlocker && preflight != null)
        {
            if (preflight.HasTrialBlocker && !string.IsNullOrWhiteSpace(preflight.TrialBlockerReason))
            {
                wholeSourceBlockerReason = preflight.TrialBlockerReason;
            }
            else if (preflight.Blockers != null && preflight.Blockers.Count > 0)
            {
                wholeSourceBlockerReason = string.Join("; ", preflight.Blockers);
            }
            else
            {
                wholeSourceBlockerReason = "Kaynak dosyada ön kontrol engeli bulunmaktadır.";
            }
        }

        bool canConvert = true;
        string? blockerReason = null;

        if (hasWholeSourceBlocker)
        {
            canConvert = false;
            blockerReason = $"[ÖN KONTROL ENGELİ] {wholeSourceBlockerReason}";
        }
        else if (selectedMessagesCount == 0)
        {
            canConvert = false;
            blockerReason = "Seçilen filtre kriterlerine uyan ileti bulunamadı. Dönüştürme başlatılamaz.";
        }

        string selectionId = "sel_" + Guid.NewGuid().ToString("N")[..16];
        string selectionContentHash = ComputeSelectionContentHash(true, canonicalFilter);

        var registered = new RegisteredSelection
        {
            SelectionId = selectionId,
            SourceHandle = sourceHandle,
            SourceSha256 = sourceSha256,
            Filters = canonicalFilter,
            SelectedFolderIds = selectedFolderIds,
            SelectedMessageKeys = selectedMessageKeys,
            TotalSourceMessages = totalSourceMessages,
            SelectedMessagesCount = selectedMessagesCount,
            ExcludedMessagesCount = excludedMessagesCount,
            SelectedAttachmentsCount = selectedAttachmentsCount,
            MissingDateExcludedCount = missingDateExcludedCount,
            CanConvert = canConvert,
            HasTrialBlocker = preflight?.HasTrialBlocker ?? false,
            TrialBlockerReason = preflight?.TrialBlockerReason,
            HasPreflightBlocker = hasWholeSourceBlocker,
            PreflightBlockerReason = blockerReason,
            SelectionContentHash = selectionContentHash,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var preview = new SelectionPreviewResult
        {
            SelectionId = selectionId,
            SourceSha256 = sourceSha256,
            Filters = canonicalFilter,
            TotalSourceMessages = totalSourceMessages,
            SelectedMessagesCount = selectedMessagesCount,
            ExcludedMessagesCount = excludedMessagesCount,
            SelectedAttachmentsCount = selectedAttachmentsCount,
            MissingDateExcludedCount = missingDateExcludedCount,
            TotalFoldersCount = allFolders.Count,
            SelectedFoldersCount = selectedFolderIds.Count,
            CanConvert = canConvert,
            BlockerReason = blockerReason,
            FolderBreakdown = folderBreakdown
        };

        return (preview, registered);
    }

    private static void CollectFoldersRecursive(
        PersonalStorage ost,
        FolderInfo folder,
        string parentPath,
        bool isRoot,
        string sourceSha256,
        List<(string FolderId, string FolderPath, string DisplayName, FolderInfo Folder)> collected,
        HashSet<string> usedPaths,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string folderName = isRoot ? "[Kök Klasör]" : (folder.DisplayName ?? "Adsız Klasör");
        string basePath = string.IsNullOrEmpty(parentPath) ? folderName : $"{parentPath}/{folderName}";
        string uniquePath = basePath;
        int disambiguator = 1;
        while (usedPaths.Contains(uniquePath))
        {
            uniquePath = $"{basePath} ({disambiguator++})";
        }
        usedPaths.Add(uniquePath);

        string rawEntryId = folder.EntryId != null
            ? System.Convert.ToHexString(folder.EntryId)
            : (folder.EntryIdString ?? uniquePath);
        string folderId = ComputeFolderId(sourceSha256, rawEntryId);

        int msgCount = 0;
        try { msgCount = folder.ContentCount; } catch { }

        if (!isRoot || msgCount > 0)
        {
            collected.Add((folderId, uniquePath, folderName, folder));
        }

        FolderInfoCollection? subFolders;
        try
        {
            subFolders = folder.GetSubFolders();
        }
        catch
        {
            subFolders = null;
        }

        if (subFolders != null)
        {
            foreach (FolderInfo sub in subFolders)
            {
                CollectFoldersRecursive(ost, sub, isRoot ? "" : uniquePath, false, sourceSha256, collected, usedPaths, cancellationToken);
            }
        }
    }
}
