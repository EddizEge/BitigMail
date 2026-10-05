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
using BitigMail.Engine.Storage;
using BitigMail.LocalHost;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Xunit;

namespace BitigMail.Engine.Tests;

public class FilteredOstWorkflowTests
{
    private static string ResolveApprovedFixturePath()
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, "lab", "ost-spike", "input", "bitigmail-lab-full.ost");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, "lab", "ost-spike", "input", "bitigmail-lab-full.ost");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        throw new FileNotFoundException("Approved test fixture 'lab/ost-spike/input/bitigmail-lab-full.ost' was not found.");
    }

    [Fact]
    public void FolderId_IsStableAndBoundToSourceSha_DifferentShaProducesDifferentFolderId()
    {
        string rawEntryId = "0000000012345678";
        string shaA = "b0801758a2e61d4ce6e86799701a81a7a60c38401f73b13c993d94c03a2ee57a";
        string shaB = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";

        string idA1 = OstSelectionEngine.ComputeFolderId(shaA, rawEntryId);
        string idA2 = OstSelectionEngine.ComputeFolderId(shaA, rawEntryId);
        string idB = OstSelectionEngine.ComputeFolderId(shaB, rawEntryId);

        Assert.StartsWith("fld_", idA1);
        Assert.Equal(idA1, idA2);
        Assert.NotEqual(idA1, idB);
    }

    [Fact]
    public void DateBoundaries_InclusiveTurkeyUtcPlus3_CalculatesCorrectInterval()
    {
        // For a single day: 2026-05-10
        // Turkey UTC+03:00 day starts at 2026-05-09 21:00:00 UTC
        // Next day starts at 2026-05-10 21:00:00 UTC
        OstSelectionEngine.ParseDateBoundaries("2026-05-10", "2026-05-10", out DateTime? startUtc, out DateTime? endExclusiveUtc);

        Assert.NotNull(startUtc);
        Assert.NotNull(endExclusiveUtc);
        Assert.Equal(DateTimeKind.Utc, startUtc.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, endExclusiveUtc.Value.Kind);

        Assert.Equal(new DateTime(2026, 5, 9, 21, 0, 0, DateTimeKind.Utc), startUtc.Value);
        Assert.Equal(new DateTime(2026, 5, 10, 21, 0, 0, DateTimeKind.Utc), endExclusiveUtc.Value);
    }

    [Fact]
    public void DateBoundaries_ReversedDates_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
        {
            OstSelectionEngine.ParseDateBoundaries("2026-05-20", "2026-05-10", out _, out _);
        });

        Assert.Contains("sonra olamaz", ex.Message);
    }

    [Fact]
    public void DateBoundaries_InvalidDateFormat_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            OstSelectionEngine.ParseDateBoundaries("10-05-2026", null, out _, out _);
        });

        Assert.Throws<ArgumentException>(() =>
        {
            OstSelectionEngine.ParseDateBoundaries(null, "invalid-date", out _, out _);
        });
    }

    [Fact]
    public void SelectionEvaluation_MissingDates_ExcludedWhenDateFilterActive_IncludedWhenInactive()
    {
        string tempPst = Path.Combine(Path.GetTempPath(), $"pst-dates-{Guid.NewGuid():N}.pst");
        try
        {
            using (var pst = PersonalStorage.Create(tempPst, FileFormatVersion.Unicode))
            {
                var folder = pst.RootFolder.AddSubFolder("DateFolder");

                // Message 1: With delivery & submit date in range (2026-06-15)
                var msg1 = new MapiMessage("sender@test.com", "dest@test.com", "Görüşme", "İçerik");
                msg1.ClientSubmitTime = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
                msg1.DeliveryTime = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
                folder.AddMessage(msg1);

                // Message 2: With submit & delivery date out of range (2025-01-01)
                var msg2 = new MapiMessage("sender@test.com", "dest@test.com", "Eski", "İçerik");
                msg2.ClientSubmitTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
                msg2.DeliveryTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
                folder.AddMessage(msg2);

                // Message 3: Missing date (MinValue / cleared properties)
                var msg3 = new MapiMessage("sender@test.com", "dest@test.com", "Tarihsiz", "İçerik");
                msg3.ClientSubmitTime = DateTime.MinValue;
                msg3.DeliveryTime = DateTime.MinValue;
                msg3.RemoveProperty(MapiPropertyTag.PR_CLIENT_SUBMIT_TIME);
                msg3.RemoveProperty(MapiPropertyTag.PR_MESSAGE_DELIVERY_TIME);
                folder.AddMessage(msg3);
            }

            using var fs = new FileStream(tempPst, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var readPst = PersonalStorage.FromStream(fs);
            var preflight = new PreflightCheckResult { CanConvert = true };

            // 1. Without date filter: all 3 included, missingDateExcluded == 0
            var (previewNoDate, _) = OstSelectionEngine.EvaluateSelection(
                readPst, "src_test", "dummyhash", null, null, null, preflight);

            Assert.Equal(3, previewNoDate.TotalSourceMessages);
            Assert.Equal(3, previewNoDate.SelectedMessagesCount);
            Assert.Equal(0, previewNoDate.MissingDateExcludedCount);

            // 2. With date filter (2026-06-01 to 2026-06-30): msg1 included, msg2 excluded by range, msg3 excluded by missing date
            var (previewWithDate, _) = OstSelectionEngine.EvaluateSelection(
                readPst, "src_test", "dummyhash", null, "2026-06-01", "2026-06-30", preflight);

            Assert.Equal(3, previewWithDate.TotalSourceMessages);
            Assert.Equal(1, previewWithDate.SelectedMessagesCount);
            Assert.Equal(2, previewWithDate.ExcludedMessagesCount);
            Assert.Equal(1, previewWithDate.MissingDateExcludedCount);
            Assert.True(previewWithDate.CanConvert);
        }
        finally
        {
            if (File.Exists(tempPst)) File.Delete(tempPst);
        }
    }

    [Fact]
    public void SelectionEvaluation_ZeroMatches_ProducesValidPreviewWithCanConvertFalse()
    {
        string tempPst = Path.Combine(Path.GetTempPath(), $"pst-zero-{Guid.NewGuid():N}.pst");
        try
        {
            using (var pst = PersonalStorage.Create(tempPst, FileFormatVersion.Unicode))
            {
                var folder = pst.RootFolder.AddSubFolder("TestFolder");
                var msg = new MapiMessage("sender@test.com", "dest@test.com", "Konu", "Metin");
                msg.DeliveryTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                folder.AddMessage(msg);
            }

            using var fs = new FileStream(tempPst, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var readPst = PersonalStorage.FromStream(fs);
            var preflight = new PreflightCheckResult { CanConvert = true };

            // Date filter for 2030 (no matching messages)
            var (preview, registered) = OstSelectionEngine.EvaluateSelection(
                readPst, "src_test", "dummyhash", null, "2030-01-01", "2030-01-31", preflight);

            Assert.Equal(1, preview.TotalSourceMessages);
            Assert.Equal(0, preview.SelectedMessagesCount);
            Assert.Equal(1, preview.ExcludedMessagesCount);
            Assert.False(preview.CanConvert);
            Assert.NotNull(preview.BlockerReason);
            Assert.NotNull(preview.BlockReason);
            Assert.Equal(preview.BlockerReason, preview.BlockReason);
            Assert.Equal(0, registered.SelectedMessagesCount);
        }
        finally
        {
            if (File.Exists(tempPst)) File.Delete(tempPst);
        }
    }

    [Fact]
    public void SelectionEvaluation_EmptyFolderSelection_MeansZeroSelected()
    {
        string tempPst = Path.Combine(Path.GetTempPath(), $"pst-empty-fld-{Guid.NewGuid():N}.pst");
        try
        {
            using (var pst = PersonalStorage.Create(tempPst, FileFormatVersion.Unicode))
            {
                var folder = pst.RootFolder.AddSubFolder("TestFolder");
                var msg = new MapiMessage("sender@test.com", "dest@test.com", "Konu", "Metin");
                folder.AddMessage(msg);
            }

            using var fs = new FileStream(tempPst, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var readPst = PersonalStorage.FromStream(fs);
            var preflight = new PreflightCheckResult { CanConvert = true };

            // Explicit empty list of folder IDs means ZERO folders selected
            var (preview, _) = OstSelectionEngine.EvaluateSelection(
                readPst, "src_test", "dummyhash", new List<string>(), null, null, preflight);

            Assert.Equal(1, preview.TotalSourceMessages);
            Assert.Equal(0, preview.SelectedMessagesCount);
            Assert.Equal(1, preview.ExcludedMessagesCount);
            Assert.False(preview.CanConvert);
        }
        finally
        {
            if (File.Exists(tempPst)) File.Delete(tempPst);
        }
    }

    [Fact]
    public void SelectionEvaluation_UnknownFolderId_ThrowsArgumentException()
    {
        string tempPst = Path.Combine(Path.GetTempPath(), $"pst-unknown-fld-{Guid.NewGuid():N}.pst");
        try
        {
            using (var pst = PersonalStorage.Create(tempPst, FileFormatVersion.Unicode))
            {
                pst.RootFolder.AddSubFolder("RealFolder");
            }

            using var fs = new FileStream(tempPst, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var readPst = PersonalStorage.FromStream(fs);
            var preflight = new PreflightCheckResult { CanConvert = true };

            var ex = Assert.Throws<ArgumentException>(() =>
            {
                OstSelectionEngine.EvaluateSelection(
                    readPst, "src_test", "dummyhash", new List<string> { "fld_nonexistent123" }, null, null, preflight);
            });

            Assert.Contains("fld_nonexistent123", ex.Message);
        }
        finally
        {
            if (File.Exists(tempPst)) File.Delete(tempPst);
        }
    }

    [Fact]
    public void SelectionEvaluation_WholeSourceTrialBlocker_RejectsEvenIfSubsetIsSmall()
    {
        string tempPst = Path.Combine(Path.GetTempPath(), $"pst-blocker-{Guid.NewGuid():N}.pst");
        string tempTarget = Path.Combine(Path.GetTempPath(), $"pst-blocker-tgt-{Guid.NewGuid():N}.pst");
        try
        {
            using (var pst = PersonalStorage.Create(tempPst, FileFormatVersion.Unicode))
            {
                pst.RootFolder.AddSubFolder("SmallFolder");
            }

            using var fs = new FileStream(tempPst, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var readPst = PersonalStorage.FromStream(fs);
            var preflight = new PreflightCheckResult
            {
                CanConvert = false,
                HasTrialBlocker = true,
                TrialBlockerReason = "Bir klasör 50 sınırını aşıyor."
            };

            // EvaluateSelection preserves authoritative preview counts while setting CanConvert=false
            var (preview, registered) = OstSelectionEngine.EvaluateSelection(
                readPst, "src_test", "dummyhash", null, null, null, preflight);

            Assert.False(preview.CanConvert);
            Assert.NotNull(preview.BlockerReason);
            Assert.Contains("ÖN KONTROL ENGELİ", preview.BlockerReason);
            Assert.Contains("50 sınırını aşıyor", preview.BlockerReason);

            Assert.False(registered.CanConvert);
            Assert.True(registered.HasTrialBlocker);
            Assert.True(registered.HasPreflightBlocker);

            // JobManager.StartJob must fail closed
            var jobManager = new JobManager();
            var clientContext = new ClientProjectContext { CompanyId = "c1", ProjectId = "p1" };
            var exJob = Assert.Throws<InvalidOperationException>(() =>
            {
                jobManager.StartJob(tempPst, tempTarget, "key-blocker", clientContext, "dummyhash", 0, registered);
            });
            Assert.Contains("ÖN KONTROL ENGELİ", exJob.Message);

            // OstToPstConverter must fail closed
            var converter = new OstToPstConverter();
            var exConv = Assert.Throws<InvalidOperationException>(() =>
            {
                converter.Convert(tempPst, tempTarget, "job-blocker", clientContext, "dummyhash", selection: registered);
            });
            Assert.Contains("ÖN KONTROL ENGELİ", exConv.Message);
            Assert.False(File.Exists(tempTarget));
        }
        finally
        {
            if (File.Exists(tempPst)) File.Delete(tempPst);
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }
    }

    [Fact]
    public void SelectionEvaluation_NonTrialWholeSourceBlocker_RejectsJobStartAndConverter()
    {
        string fixturePath = ResolveApprovedFixturePath();
        string tempSource = Path.Combine(Path.GetTempPath(), $"ost-nontrial-{Guid.NewGuid():N}.ost");
        string tempTarget = Path.Combine(Path.GetTempPath(), $"pst-nontrial-tgt-{Guid.NewGuid():N}.pst");
        try
        {
            File.Copy(fixturePath, tempSource, overwrite: true);

            using var fs = new FileStream(tempSource, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var readOst = PersonalStorage.FromStream(fs);

            // Compute real SHA-256 for binding
            fs.Position = 0;
            using var sha = SHA256.Create();
            string realSha256 = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
            fs.Position = 0;

            // Preflight with non-trial whole-source blocker (e.g. damaged folder enumeration failure)
            var preflight = new PreflightCheckResult
            {
                CanConvert = false,
                HasTrialBlocker = false,
                Blockers = new List<string> { "Alt klasör okunamadı: CRC hatası" }
            };

            // Authoritative preview preserves counts for healthy subset while failing CanConvert
            var (preview, registered) = OstSelectionEngine.EvaluateSelection(
                readOst, "src_genuine_nontrial", realSha256, null, null, null, preflight);

            Assert.False(preview.CanConvert);
            Assert.Equal(13, preview.TotalSourceMessages);
            Assert.Equal(13, preview.SelectedMessagesCount);
            Assert.NotNull(preview.BlockerReason);
            Assert.Contains("ÖN KONTROL ENGELİ", preview.BlockerReason);
            Assert.Contains("CRC hatası", preview.BlockerReason);

            Assert.False(registered.CanConvert);
            Assert.True(registered.HasPreflightBlocker);
            Assert.Equal(realSha256, registered.SourceSha256);

            // JobManager.StartJob must fail closed
            var jobManager = new JobManager();
            var clientContext = new ClientProjectContext { CompanyId = "c1", ProjectId = "p1" };
            var exJob = Assert.Throws<InvalidOperationException>(() =>
            {
                jobManager.StartJob(tempSource, tempTarget, "key-nontrial", clientContext, realSha256, 13, registered);
            });
            Assert.Contains("ÖN KONTROL ENGELİ", exJob.Message);
            Assert.Contains("CRC hatası", exJob.Message);

            // OstToPstConverter must fail closed at selection defense (after valid OST signature & SHA checks)
            var converter = new OstToPstConverter();
            var exConv = Assert.Throws<InvalidOperationException>(() =>
            {
                converter.Convert(tempSource, tempTarget, "job-nontrial", clientContext, realSha256, selection: registered);
            });
            Assert.Contains("ÖN KONTROL ENGELİ", exConv.Message);
            Assert.Contains("CRC hatası", exConv.Message);

            // Prove no target PST publication
            Assert.False(File.Exists(tempTarget));
        }
        finally
        {
            if (File.Exists(tempSource)) File.Delete(tempSource);
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }
    }

    [Fact]
    public void FilteredConversion_GenuineOstFixture_SelectSubset_ConvertsAndVerifiesExactly()
    {
        string fixturePath = ResolveApprovedFixturePath();
        string tempTarget = Path.Combine(Path.GetTempPath(), $"pst-filtered-fixture-{Guid.NewGuid():N}.pst");

        try
        {
            // 1. Analyze genuine fixture
            var analyzer = new OstAnalyzer();
            var analysis = analyzer.Analyze(fixturePath);

            Assert.Equal(13, analysis.TotalItems);
            Assert.False(analysis.Preflight.HasTrialBlocker);

            // Find "Istanbul" folder
            var istanbulFolder = analysis.Folders.FirstOrDefault(f =>
                f.DisplayName.Equals("İstanbul", StringComparison.OrdinalIgnoreCase) ||
                f.DisplayName.Equals("Istanbul", StringComparison.OrdinalIgnoreCase) ||
                f.FolderPath.EndsWith("stanbul", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(istanbulFolder);
            Assert.Equal(4, istanbulFolder.ItemCount);

            // 2. Evaluate selection for Istanbul folder only
            using var fs = new FileStream(fixturePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var ost = PersonalStorage.FromStream(fs);

            var (preview, registered) = OstSelectionEngine.EvaluateSelection(
                ost,
                "src_genuine",
                analysis.SourceSha256,
                new List<string> { istanbulFolder.FolderId },
                null,
                null,
                analysis.Preflight);

            Assert.Equal(13, preview.TotalSourceMessages);
            Assert.Equal(4, preview.SelectedMessagesCount);
            Assert.Equal(9, preview.ExcludedMessagesCount);
            Assert.True(preview.CanConvert);

            // 3. Execute conversion with this registered selection
            var converter = new OstToPstConverter();
            var clientContext = new ClientProjectContext
            {
                CompanyId = "c-1",
                CompanyName = "Test Şirketi",
                ProjectId = "p-1",
                ProjectName = "Filtre Testi"
            };

            var report = converter.Convert(
                fixturePath,
                tempTarget,
                "job-test-filter-1",
                clientContext,
                analysis.SourceSha256,
                progress: null,
                cancellationToken: CancellationToken.None,
                selection: registered);

            // 4. Assert conversion report and verification results
            Assert.True(report.ConversionSuccess);
            Assert.True(report.IsFiltered);
            Assert.Equal(13, report.TotalSourceMessages);
            Assert.Equal(4, report.SelectedMessagesCount);
            Assert.Equal(9, report.ExcludedMessagesCount);
            Assert.Equal(4, report.ItemsWritten);
            Assert.Equal(0, report.FailedItems);
            Assert.True(File.Exists(tempTarget));

            // Assert SelectedFolders snapshot persisted in report
            Assert.NotNull(report.SelectionFilter);
            Assert.NotEmpty(report.SelectionFilter.SelectedFolders);
            Assert.Equal(istanbulFolder.FolderId, report.SelectionFilter.SelectedFolders[0].FolderId);
            Assert.Equal(istanbulFolder.DisplayName, report.SelectionFilter.SelectedFolders[0].DisplayName);

            // Assert reopened verification found exactly 4 items in PST
            Assert.NotNull(report.ReopenedPstVerification);
            Assert.True(report.ReopenedPstVerification.VerificationSuccess);
            Assert.Equal(4, report.ReopenedPstVerification.TotalPhysicalItemsFound);
            Assert.True(report.ReopenedPstVerification.ItemCountMatch);
        }
        finally
        {
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }
    }

    [Fact]
    public void SelectionSensitiveIdempotency_SameKeyDifferentSelection_ThrowsConflict()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-idemp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string tempSource = Path.GetTempFileName();
        string tempTarget = Path.Combine(tempDir, "out.pst");

        try
        {
            var jobManager = new JobManager(tempDir);
            var clientContext = new ClientProjectContext { CompanyId = "c1", ProjectId = "p1" };

            var filterA = new ConversionSelectionFilter
            {
                FolderIds = new List<string> { "fld_1" },
                StartDate = "2026-01-01"
            };
            var selA = new RegisteredSelection
            {
                SelectionId = "sel_A",
                SourceHandle = "src_1",
                SourceSha256 = "hash1",
                Filters = filterA,
                SelectionContentHash = OstSelectionEngine.ComputeSelectionContentHash(true, filterA),
                TotalSourceMessages = 10,
                SelectedMessagesCount = 5
            };

            var filterB = new ConversionSelectionFilter
            {
                FolderIds = new List<string> { "fld_2" },
                StartDate = "2026-02-01"
            };
            var selB = new RegisteredSelection
            {
                SelectionId = "sel_B",
                SourceHandle = "src_1",
                SourceSha256 = "hash1",
                Filters = filterB,
                SelectionContentHash = OstSelectionEngine.ComputeSelectionContentHash(true, filterB),
                TotalSourceMessages = 10,
                SelectedMessagesCount = 3
            };

            string idempKey = "key_filter_test";

            // First start with selA creates job
            var job1 = jobManager.StartJob(tempSource, tempTarget, idempKey, clientContext, "hash1", 10, selA);
            Assert.NotNull(job1);
            // The invalid fixture fails asynchronously; wait before reusing or deleting its files.
            Assert.True(SpinWait.SpinUntil(() => jobManager.ActiveRunningJobId == null, TimeSpan.FromSeconds(10)));

            // Same start with identical selA returns the same job
            var jobSame = jobManager.StartJob(tempSource, tempTarget, idempKey, clientContext, "hash1", 10, selA);
            Assert.Equal(job1.JobId, jobSame.JobId);

            // Starting with same key but differing selection selB throws conflict
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                jobManager.StartJob(tempSource, tempTarget, idempKey, clientContext, "hash1", 10, selB);
            });

            Assert.Contains("farklı parametrelerle", ex.Message);
        }
        finally
        {
            if (File.Exists(tempSource)) File.Delete(tempSource);
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void SelectionSensitiveIdempotency_PersistedAcrossRestart()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-restart-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string tempSource = Path.GetTempFileName();
        string tempTarget = Path.Combine(tempDir, "out.pst");

        try
        {
            var filterA = new ConversionSelectionFilter
            {
                FolderIds = new List<string> { "fld_1" },
                StartDate = "2026-01-01"
            };
            var selA = new RegisteredSelection
            {
                SelectionId = "sel_A",
                SourceHandle = "src_1",
                SourceSha256 = "hash1",
                Filters = filterA,
                SelectionContentHash = OstSelectionEngine.ComputeSelectionContentHash(true, filterA),
                TotalSourceMessages = 10,
                SelectedMessagesCount = 5
            };

            string idempKey = "restart_key_1";
            var clientContext = new ClientProjectContext { CompanyId = "c1", ProjectId = "p1" };

            // Process 1: start job and persist
            var jm1 = new JobManager(tempDir);
            var job1 = jm1.StartJob(tempSource, tempTarget, idempKey, clientContext, "hash1", 10, selA);
            Assert.NotNull(job1);
            // A restart begins only after the old writer has stopped, not while both managers write one store.
            Assert.True(SpinWait.SpinUntil(() => jm1.ActiveRunningJobId == null, TimeSpan.FromSeconds(10)));

            // Process 2 (simulated restart): instantiate new JobManager pointing to same directory
            var jm2 = new JobManager(tempDir);

            // Re-requesting with identical selection matches
            var jobMatched = jm2.StartJob(tempSource, tempTarget, idempKey, clientContext, "hash1", 10, selA);
            Assert.Equal(job1.JobId, jobMatched.JobId);

            // Re-requesting with different selection fails even after restart
            var filterB = new ConversionSelectionFilter { StartDate = "2026-05-01" };
            var selB = new RegisteredSelection
            {
                SelectionId = "sel_B",
                SourceHandle = "src_1",
                SourceSha256 = "hash1",
                Filters = filterB,
                SelectionContentHash = OstSelectionEngine.ComputeSelectionContentHash(true, filterB),
                TotalSourceMessages = 10,
                SelectedMessagesCount = 2
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                jm2.StartJob(tempSource, tempTarget, idempKey, clientContext, "hash1", 10, selB);
            });

            Assert.Contains("farklı parametrelerle", ex.Message);
        }
        finally
        {
            if (File.Exists(tempSource)) File.Delete(tempSource);
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void NormalizeToUtc_UtcValuesStayUnchanged_LocalUsesToUniversalTime_UnspecifiedUsesSpecifyKindUtc()
    {
        // 1. Utc values stay unchanged
        var utcDate = new DateTime(2026, 5, 10, 14, 0, 0, DateTimeKind.Utc);
        var normUtc = OstSelectionEngine.NormalizeToUtc(utcDate);
        Assert.Equal(DateTimeKind.Utc, normUtc.Kind);
        Assert.Equal(utcDate.Ticks, normUtc.Ticks);

        // 2. Unspecified values use DateTime.SpecifyKind(value, DateTimeKind.Utc)
        var unspecDate = new DateTime(2026, 5, 10, 14, 0, 0, DateTimeKind.Unspecified);
        var normUnspec = OstSelectionEngine.NormalizeToUtc(unspecDate);
        Assert.Equal(DateTimeKind.Utc, normUnspec.Kind);
        Assert.Equal(unspecDate.Ticks, normUnspec.Ticks);

        // 3. Local values use ToUniversalTime()
        var local2330 = new DateTime(2026, 5, 10, 23, 30, 0, DateTimeKind.Local);
        var normLocal = OstSelectionEngine.NormalizeToUtc(local2330);
        Assert.Equal(DateTimeKind.Utc, normLocal.Kind);
        Assert.Equal(local2330.ToUniversalTime(), normLocal);

        // Calendar boundary test under local timezone assumption:
        // Converting a 23:30 local time to UTC shifts by the local UTC offset.
        TimeSpan localOffset = TimeZoneInfo.Local.GetUtcOffset(local2330);
        DateTime expectedUtc = DateTime.SpecifyKind(local2330 - localOffset, DateTimeKind.Utc);
        Assert.Equal(expectedUtc, normLocal);
    }

    [Fact]
    public void ExtractMessageDate_ProductionMapiMessage_ClientSubmitTimeAndDeliveryTime_NormalizeProperly()
    {
        // 1. ClientSubmitTime with Local kind uses ToUniversalTime()
        var msgLocal = new MapiMessage("from@test.com", "to@test.com", "Test", "Body");
        var localTime = new DateTime(2026, 5, 10, 23, 30, 0, DateTimeKind.Local);
        msgLocal.ClientSubmitTime = localTime;
        var extractedLocal = OstSelectionEngine.ExtractMessageDate(msgLocal);
        Assert.NotNull(extractedLocal);
        Assert.Equal(DateTimeKind.Utc, extractedLocal.Value.Kind);
        Assert.Equal(localTime.ToUniversalTime(), extractedLocal.Value);

        // 2. ClientSubmitTime MinValue/cleared falls back to DeliveryTime with Unspecified kind treated as UTC
        var msgUnspec = new MapiMessage("from@test.com", "to@test.com", "Test", "Body");
        msgUnspec.ClientSubmitTime = DateTime.MinValue;
        msgUnspec.RemoveProperty(MapiPropertyTag.PR_CLIENT_SUBMIT_TIME);
        var unspecTime = new DateTime(2026, 5, 10, 23, 30, 0, DateTimeKind.Unspecified);
        msgUnspec.DeliveryTime = unspecTime;
        var extractedUnspec = OstSelectionEngine.ExtractMessageDate(msgUnspec);
        Assert.NotNull(extractedUnspec);
        Assert.Equal(DateTimeKind.Utc, extractedUnspec.Value.Kind);
        Assert.Equal(new DateTime(2026, 5, 10, 23, 30, 0, DateTimeKind.Utc), extractedUnspec.Value);

        // 3. Both MinValue/cleared returns null
        var msgEmpty = new MapiMessage("from@test.com", "to@test.com", "Test", "Body");
        msgEmpty.ClientSubmitTime = DateTime.MinValue;
        msgEmpty.DeliveryTime = DateTime.MinValue;
        msgEmpty.RemoveProperty(MapiPropertyTag.PR_CLIENT_SUBMIT_TIME);
        msgEmpty.RemoveProperty(MapiPropertyTag.PR_MESSAGE_DELIVERY_TIME);
        var extractedEmpty = OstSelectionEngine.ExtractMessageDate(msgEmpty);
        Assert.Null(extractedEmpty);
    }

    [Fact]
    public void DateFilter_TurkeyUtcPlus3_CalendarBoundary2330_EvaluatesInclusiveDayCorrectly()
    {
        string tempPst = Path.Combine(Path.GetTempPath(), $"pst-boundary-{Guid.NewGuid():N}.pst");
        try
        {
            using (var pst = PersonalStorage.Create(tempPst, FileFormatVersion.Unicode))
            {
                var folder = pst.RootFolder.AddSubFolder("BoundaryFolder");

                // In Turkey (UTC+03:00), the day 2026-05-10 spans UTC [2026-05-09 21:00:00, 2026-05-10 21:00:00).
                // Message 1: 2026-05-10 20:30:00 UTC (which is 23:30 local in Turkey on 2026-05-10) -> INCLUDED in 2026-05-10
                var msg1 = new MapiMessage("from@test.com", "to@test.com", "Boundary Included (23:30 TR)", "Body");
                msg1.ClientSubmitTime = new DateTime(2026, 5, 10, 20, 30, 0, DateTimeKind.Utc);
                folder.AddMessage(msg1);

                // Message 2: 2026-05-10 21:30:00 UTC (which is 00:30 local in Turkey on 2026-05-11) -> EXCLUDED from 2026-05-10
                var msg2 = new MapiMessage("from@test.com", "to@test.com", "Boundary Excluded Next Day (00:30 TR)", "Body");
                msg2.ClientSubmitTime = new DateTime(2026, 5, 10, 21, 30, 0, DateTimeKind.Utc);
                folder.AddMessage(msg2);

                // Message 3: 2026-05-09 20:30:00 UTC (which is 23:30 local in Turkey on 2026-05-09) -> EXCLUDED from 2026-05-10
                var msg3 = new MapiMessage("from@test.com", "to@test.com", "Boundary Excluded Prev Day (23:30 TR)", "Body");
                msg3.ClientSubmitTime = new DateTime(2026, 5, 9, 20, 30, 0, DateTimeKind.Utc);
                folder.AddMessage(msg3);
            }

            using var fs = new FileStream(tempPst, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var readPst = PersonalStorage.FromStream(fs);
            var preflight = new PreflightCheckResult { CanConvert = true };

            // Evaluate selection for single inclusive day 2026-05-10
            var (preview, registered) = OstSelectionEngine.EvaluateSelection(
                readPst, "src_test", "dummyhash", null, "2026-05-10", "2026-05-10", preflight);

            Assert.Equal(3, preview.TotalSourceMessages);
            Assert.Equal(1, preview.SelectedMessagesCount);
            Assert.Equal(2, preview.ExcludedMessagesCount);
            Assert.Equal(0, preview.MissingDateExcludedCount);
            Assert.True(preview.CanConvert);
            Assert.Equal(1, registered.SelectedMessagesCount);
        }
        finally
        {
            if (File.Exists(tempPst)) File.Delete(tempPst);
        }
    }

    [Fact]
    public void SelectionFilter_PersistsSelectedFoldersSnapshot_OpaqueIdsRemainSoleAuthority()
    {
        // 1. Verify content hash invariance to SelectedFolders snapshots (opaque IDs remain sole authority)
        var filterA = new ConversionSelectionFilter
        {
            FolderIds = new List<string> { "fld_1", "fld_2" },
            SelectedFolders = new List<SelectedFolderSnapshot>
            {
                new() { FolderId = "fld_1", FolderPath = "Gelen", DisplayName = "Gelen Kutusu" },
                new() { FolderId = "fld_2", FolderPath = "Arsiv", DisplayName = "Arşiv" }
            }
        };

        var filterB = new ConversionSelectionFilter
        {
            FolderIds = new List<string> { "fld_1", "fld_2" },
            SelectedFolders = new List<SelectedFolderSnapshot>
            {
                new() { FolderId = "fld_1", FolderPath = "Completely/Different/Label", DisplayName = "Hacked Name" }
            }
        };

        string hashA = OstSelectionEngine.ComputeSelectionContentHash(true, filterA);
        string hashB = OstSelectionEngine.ComputeSelectionContentHash(true, filterB);
        Assert.Equal(hashA, hashB);

        // 2. Verify persistence and restore across JobManager restart
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-fld-snapshot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string tempSource = Path.GetTempFileName();
        string tempTarget = Path.Combine(tempDir, "out.pst");

        try
        {
            var jm1 = new JobManager(tempDir);
            var clientContext = new ClientProjectContext { CompanyId = "c1", ProjectId = "p1" };

            var sel = new RegisteredSelection
            {
                SelectionId = "sel_snap_1",
                SourceHandle = "src_1",
                SourceSha256 = "hash1",
                Filters = filterA,
                SelectionContentHash = hashA,
                TotalSourceMessages = 10,
                SelectedMessagesCount = 5
            };

            var job1 = jm1.StartJob(tempSource, tempTarget, "key_snap_1", clientContext, "hash1", 10, sel);
            Assert.True(SpinWait.SpinUntil(() => jm1.ActiveRunningJobId == null, TimeSpan.FromSeconds(10)));
            Assert.NotNull(job1.SelectionFilter);
            Assert.Equal(2, job1.SelectionFilter.SelectedFolders.Count);
            Assert.Equal("Gelen Kutusu", job1.SelectionFilter.SelectedFolders[0].DisplayName);

            // Simulated restart
            var jm2 = new JobManager(tempDir);
            var reloadedJob = jm2.GetJob(job1.JobId);
            Assert.NotNull(reloadedJob);
            Assert.NotNull(reloadedJob.SelectionFilter);
            Assert.Equal(2, reloadedJob.SelectionFilter.SelectedFolders.Count);
            Assert.Equal("fld_1", reloadedJob.SelectionFilter.SelectedFolders[0].FolderId);
            Assert.Equal("Gelen Kutusu", reloadedJob.SelectionFilter.SelectedFolders[0].DisplayName);
            Assert.Equal("Gelen", reloadedJob.SelectionFilter.SelectedFolders[0].FolderPath);
            Assert.Equal("fld_2", reloadedJob.SelectionFilter.SelectedFolders[1].FolderId);
            Assert.Equal("Arşiv", reloadedJob.SelectionFilter.SelectedFolders[1].DisplayName);
        }
        finally
        {
            if (File.Exists(tempSource)) File.Delete(tempSource);
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void SelectionPreviewEndpoint_MutatedOstWithoutSizeChange_RejectedFailClosed_NoSelectionRegistered()
    {
        string fixturePath = ResolveApprovedFixturePath();
        string tempSource = Path.Combine(Path.GetTempPath(), $"mutated-preview-{Guid.NewGuid():N}.ost");
        string tempTarget = Path.Combine(Path.GetTempPath(), $"target-preview-{Guid.NewGuid():N}.pst");

        try
        {
            // 1. Copy real valid OST fixture
            File.Copy(fixturePath, tempSource, overwrite: true);
            long originalSize = new FileInfo(tempSource).Length;

            // 2. Perform production analysis on the unmodified OST
            var analyzer = new OstAnalyzer();
            var analysis = analyzer.Analyze(tempSource);
            string originalSha256 = analysis.SourceSha256;
            Assert.False(string.IsNullOrEmpty(originalSha256));

            // 3. Register source in production FileHandleRegistry with opaque handle and attach analysis
            var handleRegistry = new FileHandleRegistry();
            string sourceHandle = handleRegistry.RegisterSource(tempSource);
            Assert.StartsWith("src_", sourceHandle);
            handleRegistry.AttachAnalysis(sourceHandle, analysis);

            var entry = handleRegistry.GetSourceEntry(sourceHandle);
            Assert.NotNull(entry);
            Assert.Equal(originalSha256, entry.BoundSha256);
            Assert.Equal(originalSize, entry.SizeBytes);
            Assert.Equal(0, handleRegistry.ActiveSelectionsCount);

            // 4. Mutate data portion (offset 1024, keeping 64-byte header intact) WITHOUT changing file size
            using (var stream = new FileStream(tempSource, FileMode.Open, FileAccess.ReadWrite))
            {
                stream.Seek(1024, SeekOrigin.Begin);
                int b = stream.ReadByte();
                stream.Seek(1024, SeekOrigin.Begin);
                stream.WriteByte((byte)(b ^ 0xFF));
            }

            // Verify file size did NOT change (size-only checks would falsely pass)
            long postMutationSize = new FileInfo(tempSource).Length;
            Assert.Equal(originalSize, postMutationSize);

            // Verify SHA-256 actually changed
            string mutatedSha256;
            using (var stream = File.OpenRead(tempSource))
            using (var sha = SHA256.Create())
            {
                mutatedSha256 = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
            }
            Assert.NotEqual(originalSha256, mutatedSha256);

            // 5. Invoke production endpoint handler HandleSelectionPreview
            var request = new SelectionPreviewRequest(sourceHandle, null, null, null);
            var result = LocalEngineApiEndpoints.HandleSelectionPreview(handleRegistry, request);

            // 6. Assert preview is rejected with 400 Bad Request
            Assert.NotNull(result);
            var statusResult = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(result);
            Assert.Equal(400, statusResult.StatusCode);
            var valueResult = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IValueHttpResult>(result);
            using var doc = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(valueResult.Value));
            string? errorMessage = doc.RootElement.GetProperty("error").GetString();
            Assert.NotNull(errorMessage);
            Assert.Contains("BÜTÜNLÜK ENGELİ", errorMessage);
            Assert.Contains("değişiklik tespit edildi", errorMessage);

            // 7. Assert NO RegisteredSelection was created in the registry
            Assert.Equal(0, handleRegistry.ActiveSelectionsCount);

            // 8. Assert converter rehash protection also rejects if started directly
            var converter = new OstToPstConverter();
            var clientContext = new ClientProjectContext { CompanyId = "c1", ProjectId = "p1" };
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                converter.Convert(tempSource, tempTarget, "job-mutated-guard", clientContext, expectedSourceSha256: originalSha256);
            });
            Assert.Contains("BÜTÜNLÜK ENGELİ", ex.Message);
        }
        finally
        {
            if (File.Exists(tempSource)) File.Delete(tempSource);
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }
    }

    [Fact]
    public async Task SelectionPreviewEndpoint_ProtectedUnderSecurityMiddleware_ValidatesHostOriginTokenAndHandles()
    {
        var config = new SecurityConfig { ExpectedHost = "127.0.0.1:6174" };
        var sessionManager = new SessionManager();
        string validToken = sessionManager.CreateSession();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(sessionManager);
        var serviceProvider = services.BuildServiceProvider();

        var handleRegistry = new FileHandleRegistry();

        // Middleware pipeline with preview endpoint as downstream handler
        var middleware = new LocalSecurityMiddleware(async ctx =>
        {
            if (ctx.Request.Path == "/api/source/selection/preview")
            {
                var badResult = LocalEngineApiEndpoints.HandleSelectionPreview(
                    handleRegistry,
                    new SelectionPreviewRequest("", null, null, null));
                if (badResult is IStatusCodeHttpResult statusResult)
                {
                    ctx.Response.StatusCode = statusResult.StatusCode ?? StatusCodes.Status400BadRequest;
                }
                else
                {
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
                await Task.CompletedTask;
            }
            else
            {
                ctx.Response.StatusCode = StatusCodes.Status200OK;
            }
        }, config);

        // 1. Rejected if Host is foreign or invalid -> 400 Bad Request
        var invalidHostCtx = new DefaultHttpContext { RequestServices = serviceProvider };
        invalidHostCtx.Request.Method = "POST";
        invalidHostCtx.Request.Path = "/api/source/selection/preview";
        invalidHostCtx.Request.Headers["Host"] = "localhost:6174";
        invalidHostCtx.Request.Headers["Origin"] = "http://127.0.0.1:5173";
        invalidHostCtx.Request.Headers["Content-Type"] = "application/json";
        invalidHostCtx.Request.Headers[SecurityConfig.SessionHeaderName] = validToken;
        await middleware.InvokeAsync(invalidHostCtx);
        Assert.Equal(StatusCodes.Status400BadRequest, invalidHostCtx.Response.StatusCode);

        // 2. Rejected if Origin is foreign -> 403 Forbidden
        var invalidOriginCtx = new DefaultHttpContext { RequestServices = serviceProvider };
        invalidOriginCtx.Request.Method = "POST";
        invalidOriginCtx.Request.Path = "/api/source/selection/preview";
        invalidOriginCtx.Request.Headers["Host"] = "127.0.0.1:6174";
        invalidOriginCtx.Request.Headers["Origin"] = "http://evil.com";
        invalidOriginCtx.Request.Headers["Content-Type"] = "application/json";
        invalidOriginCtx.Request.Headers[SecurityConfig.SessionHeaderName] = validToken;
        await middleware.InvokeAsync(invalidOriginCtx);
        Assert.Equal(StatusCodes.Status403Forbidden, invalidOriginCtx.Response.StatusCode);

        // 3. Rejected if Session token is missing -> 401 Unauthorized
        var missingTokenCtx = new DefaultHttpContext { RequestServices = serviceProvider };
        missingTokenCtx.Request.Method = "POST";
        missingTokenCtx.Request.Path = "/api/source/selection/preview";
        missingTokenCtx.Request.Headers["Host"] = "127.0.0.1:6174";
        missingTokenCtx.Request.Headers["Origin"] = "http://127.0.0.1:5173";
        missingTokenCtx.Request.Headers["Content-Type"] = "application/json";
        await middleware.InvokeAsync(missingTokenCtx);
        Assert.Equal(StatusCodes.Status401Unauthorized, missingTokenCtx.Response.StatusCode);

        // 4. Valid Host, Origin, and Token passes through middleware to endpoint
        var validReqCtx = new DefaultHttpContext { RequestServices = serviceProvider };
        validReqCtx.Request.Method = "POST";
        validReqCtx.Request.Path = "/api/source/selection/preview";
        validReqCtx.Request.Headers["Host"] = "127.0.0.1:6174";
        validReqCtx.Request.Headers["Origin"] = "http://127.0.0.1:5173";
        validReqCtx.Request.Headers["Content-Type"] = "application/json";
        validReqCtx.Request.Headers[SecurityConfig.SessionHeaderName] = validToken;
        await middleware.InvokeAsync(validReqCtx);
        Assert.Equal(StatusCodes.Status400BadRequest, validReqCtx.Response.StatusCode);
    }
}
