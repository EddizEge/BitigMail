using System;
using System.Collections.Generic;
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
using BitigMail.Engine.Storage;
using BitigMail.LocalHost;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BitigMail.Engine.Tests;

public class PstSplitWorkflowTests
{
    private static string ResolvePath(string relativePath)
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, relativePath);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, relativePath);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        throw new FileNotFoundException($"Approved fixture not found: {relativePath}");
    }

    private static string ResolveApprovedPstFixturePath()
    {
        return ResolvePath(Path.Combine("lab", "ost-spike", "output", "genuine-full-converted-04.pst"));
    }

    private static string ResolveApprovedOstFixturePath()
    {
        return ResolvePath(Path.Combine("lab", "ost-spike", "input", "bitigmail-lab-full.ost"));
    }

    [Fact]
    public void PstSplitter_GenuinePst_YearSplit_OracleMatchesExpectedCounts()
    {
        string pstFixture = ResolveApprovedPstFixturePath();
        string tempOutParent = Path.Combine(Path.GetTempPath(), $"bm-pst-year-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempOutParent);

        try
        {
            var formatInfo = OutlookStorageInspector.ValidateSplitSourceAuthoritatively(pstFixture);
            Assert.True(formatInfo.IsValidOutlookStorage);
            Assert.True(formatInfo.IsPstSignature);
            Assert.False(formatInfo.IsOstSignature);
            Assert.Contains(".pst", formatInfo.FormatName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Pst", formatInfo.AuthoritativeRuntimeFormat);

            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(pstFixture);
            Assert.NotNull(analysis);
            Assert.True(analysis.FormatInfo.IsPstSignature);
            Assert.False(analysis.FormatInfo.IsOstSignature);
            Assert.Contains(".pst", analysis.FormatInfo.FormatName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Pst", analysis.FormatInfo.AuthoritativeRuntimeFormat);

            var options = new SplitOptions { Mode = SplitOptions.ModeYear };
            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_test_year",
                SourceHandle = "src_fixture",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var splitter = new PstSplitter();
            string jobId = "job-test-pst-year";
            var context = new ClientProjectContext { CompanyId = "c1", ProjectId = "p1" };

            var report = splitter.Split(
                pstFixture,
                tempOutParent,
                jobId,
                context,
                plan,
                progress: null,
                cancellationToken: CancellationToken.None,
                selection: null);

            Assert.True(report.ConversionSuccess);
            Assert.NotNull(report.Parts);
            // Genuine oracle: 2022=1, 2023=2, 2024=8, 2025=1, 2026=1
            Assert.Equal(5, report.Parts.Count);

            Assert.All(report.Parts, p => Assert.NotNull(p.GroupKey));
            var partsByYear = report.Parts.ToDictionary(p => p.GroupKey!, p => p);
            Assert.Contains("2022", partsByYear);
            Assert.Contains("2023", partsByYear);
            Assert.Contains("2024", partsByYear);
            Assert.Contains("2025", partsByYear);
            Assert.Contains("2026", partsByYear);

            Assert.Equal(1, partsByYear["2022"].ItemsWritten);
            Assert.Equal(2, partsByYear["2023"].ItemsWritten);
            Assert.Equal(8, partsByYear["2024"].ItemsWritten);
            Assert.Equal(1, partsByYear["2025"].ItemsWritten);
            Assert.Equal(1, partsByYear["2026"].ItemsWritten);

            Assert.Equal(13, report.ItemsWritten);
            int totalAttachments = report.Parts.Sum(p => p.TotalAttachmentsVerified);
            Assert.Equal(4, totalAttachments);

            foreach (var part in report.Parts)
            {
                Assert.NotNull(part.ReopenedPstVerification);
                Assert.True(part.ReopenedPstVerification.VerificationSuccess);
                Assert.Equal(part.ItemsWritten, part.ReopenedPstVerification.TotalPhysicalItemsFound);
            }

            // Verify final bundle directory and manifest
            string finalDir = Path.Combine(tempOutParent, $"arsiv-{jobId}");
            Assert.True(Directory.Exists(finalDir));
            string manifestPath = Path.Combine(finalDir, "arsiv-manifest.json");
            Assert.True(File.Exists(manifestPath));

            string manifestJson = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<SplitManifest>(manifestJson, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            Assert.NotNull(manifest);
            Assert.Equal(5, manifest.Parts.Count);
            Assert.Equal(13, manifest.TotalMessagesWritten);
        }
        finally
        {
            if (Directory.Exists(tempOutParent))
            {
                try { Directory.Delete(tempOutParent, true); } catch { }
            }
        }
    }

    [Fact]
    public void PstSplitter_GenuineOst_YearSplit_SucceedsWithIdenticalCounts()
    {
        string ostFixture = ResolveApprovedOstFixturePath();
        string tempOutParent = Path.Combine(Path.GetTempPath(), $"bm-ost-year-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempOutParent);

        try
        {
            var formatInfo = OutlookStorageInspector.ValidateSplitSourceAuthoritatively(ostFixture);
            Assert.True(formatInfo.IsValidOutlookStorage);
            Assert.True(formatInfo.IsOstSignature);
            Assert.False(formatInfo.IsPstSignature);
            Assert.Contains(".ost", formatInfo.FormatName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Ost", formatInfo.AuthoritativeRuntimeFormat);

            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(ostFixture);
            Assert.NotNull(analysis);
            Assert.True(analysis.FormatInfo.IsOstSignature);
            Assert.False(analysis.FormatInfo.IsPstSignature);
            Assert.Contains(".ost", analysis.FormatInfo.FormatName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Ost", analysis.FormatInfo.AuthoritativeRuntimeFormat);

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_test_ost_year",
                SourceHandle = "src_fixture_ost",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var splitter = new PstSplitter();
            string jobId = "job-test-ost-year";
            var context = new ClientProjectContext { CompanyId = "c2", ProjectId = "p2" };

            var report = splitter.Split(
                ostFixture,
                tempOutParent,
                jobId,
                context,
                plan,
                progress: null,
                cancellationToken: CancellationToken.None,
                selection: null);

            Assert.True(report.ConversionSuccess);
            Assert.Equal(5, report.Parts.Count);
            Assert.Equal(13, report.ItemsWritten);
        }
        finally
        {
            if (Directory.Exists(tempOutParent))
            {
                try { Directory.Delete(tempOutParent, true); } catch { }
            }
        }
    }

    [Fact]
    public void PstSplitter_TurkeyUtcPlus3_YearBoundary_MessageAt2130UtcDec31_AssignedTo2024()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-boundary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string sourcePst = Path.Combine(tempDir, "boundary.pst");
        string outParent = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outParent);

        try
        {
            using (var pst = PersonalStorage.Create(sourcePst, FileFormatVersion.Unicode))
            {
                var folder = pst.RootFolder.AddSubFolder("TestFolder");

                // Message 1: 2023-12-31 20:59:00 UTC -> 23:59:00 in Turkey (UTC+3) -> 2023
                using var msg2023 = new MapiMessage("a@test.com", "b@test.com", "Subject 2023", "Body");
                msg2023.ClientSubmitTime = new DateTime(2023, 12, 31, 20, 59, 0, DateTimeKind.Utc);
                msg2023.DeliveryTime = msg2023.ClientSubmitTime;
                folder.AddMessage(msg2023);

                // Message 2: 2023-12-31 21:30:00 UTC -> 00:30:00 2024-01-01 in Turkey (UTC+3) -> 2024
                using var msg2024 = new MapiMessage("c@test.com", "d@test.com", "Subject 2024 Boundary", "Body");
                msg2024.ClientSubmitTime = new DateTime(2023, 12, 31, 21, 30, 0, DateTimeKind.Utc);
                msg2024.DeliveryTime = msg2024.ClientSubmitTime;
                folder.AddMessage(msg2024);
            }

            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(sourcePst);

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_boundary",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var splitter = new PstSplitter();
            var report = splitter.Split(sourcePst, outParent, "job-boundary", new ClientProjectContext(), plan);

            Assert.True(report.ConversionSuccess);
            Assert.Equal(2, report.Parts.Count);

            var part2023 = report.Parts.FirstOrDefault(p => p.GroupKey == "2023");
            var part2024 = report.Parts.FirstOrDefault(p => p.GroupKey == "2024");

            Assert.NotNull(part2023);
            Assert.NotNull(part2024);
            Assert.Equal(1, part2023.ItemsWritten);
            Assert.Equal(1, part2024.ItemsWritten);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void PstSplitter_Tarihsiz_UndatedMessagesGroupedIntoTarihsizPart()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-tarihsiz-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string sourcePst = Path.Combine(tempDir, "tarihsiz.pst");
        string outParent = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outParent);

        try
        {
            using (var pst = PersonalStorage.Create(sourcePst, FileFormatVersion.Unicode))
            {
                var folder = pst.RootFolder.AddSubFolder("Notlar");

                using var msg1 = new MapiMessage("a@test.com", "b@test.com", "Dated", "Body");
                msg1.ClientSubmitTime = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
                msg1.DeliveryTime = msg1.ClientSubmitTime;
                folder.AddMessage(msg1);

                using var msg2 = new MapiMessage("a@test.com", "b@test.com", "Undated", "Body");
                msg2.ClientSubmitTime = DateTime.MinValue;
                msg2.DeliveryTime = DateTime.MinValue;
                msg2.RemoveProperty(MapiPropertyTag.PR_CLIENT_SUBMIT_TIME);
                msg2.RemoveProperty(MapiPropertyTag.PR_MESSAGE_DELIVERY_TIME);
                folder.AddMessage(msg2);
            }

            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(sourcePst);

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_tarihsiz",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var splitter = new PstSplitter();
            var report = splitter.Split(sourcePst, outParent, "job-tarihsiz", new ClientProjectContext(), plan);

            Assert.True(report.ConversionSuccess);
            Assert.Equal(2, report.Parts.Count);

            var partDated = report.Parts.FirstOrDefault(p => p.GroupKey == "2025");
            var partUndated = report.Parts.FirstOrDefault(p => p.GroupKey == "Tarihsiz");

            Assert.NotNull(partDated);
            Assert.NotNull(partUndated);
            Assert.Equal("arsiv-tarihsiz.pst", partUndated.PartFileName);
            Assert.Equal(1, partUndated.ItemsWritten);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void PstSplitter_EmptyYears_OmittedFromParts()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-emptyyears-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string sourcePst = Path.Combine(tempDir, "emptyyears.pst");
        string outParent = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outParent);

        try
        {
            using (var pst = PersonalStorage.Create(sourcePst, FileFormatVersion.Unicode))
            {
                var folder = pst.RootFolder.AddSubFolder("Klasor");

                using var msg1 = new MapiMessage("a@test.com", "b@test.com", "2022", "Body");
                msg1.ClientSubmitTime = new DateTime(2022, 1, 1, 12, 0, 0, DateTimeKind.Utc);
                msg1.DeliveryTime = msg1.ClientSubmitTime;
                folder.AddMessage(msg1);

                using var msg2 = new MapiMessage("a@test.com", "b@test.com", "2025", "Body");
                msg2.ClientSubmitTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
                msg2.DeliveryTime = msg2.ClientSubmitTime;
                folder.AddMessage(msg2);
            }

            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(sourcePst);

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_emptyyears",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var splitter = new PstSplitter();
            var report = splitter.Split(sourcePst, outParent, "job-emptyyears", new ClientProjectContext(), plan);

            Assert.True(report.ConversionSuccess);
            Assert.Equal(2, report.Parts.Count);
            // No 2023 or 2024 part created
            Assert.DoesNotContain(report.Parts, p => p.GroupKey == "2023" || p.GroupKey == "2024");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void PstSplitter_PhysicalMultiplicity_PreservesSameMessageId()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-multiplicity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string sourcePst = Path.Combine(tempDir, "multiplicity.pst");
        string outParent = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outParent);

        try
        {
            using (var pst = PersonalStorage.Create(sourcePst, FileFormatVersion.Unicode))
            {
                var f1 = pst.RootFolder.AddSubFolder("F1");
                var f2 = pst.RootFolder.AddSubFolder("F2");

                using var msg1 = new MapiMessage("sender@test.com", "rcv@test.com", "Duplicate ID Message", "Body 1");
                msg1.SetMessageFlags(MapiMessageFlags.MSGFLAG_READ);
                msg1.DeliveryTime = new DateTime(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc);
                f1.AddMessage(msg1);

                using var msg2 = new MapiMessage("sender@test.com", "rcv@test.com", "Duplicate ID Message", "Body 2");
                msg2.SetMessageFlags(MapiMessageFlags.MSGFLAG_READ);
                msg2.DeliveryTime = new DateTime(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc);
                f2.AddMessage(msg2);
            }

            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(sourcePst);

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_dup",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var splitter = new PstSplitter();
            var report = splitter.Split(sourcePst, outParent, "job-dup", new ClientProjectContext(), plan);

            Assert.True(report.ConversionSuccess);
            Assert.Single(report.Parts);
            Assert.Equal(2, report.Parts[0].ItemsWritten);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void PstSplitter_SizeMode_MultipleParts_EveryPartRespectsHardCap()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-size-mult-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string outParent = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outParent);

        try
        {
            var (fixturePath, manifestPath, manifest) = SplitFixtureBuilder.BuildNineMessageFixture(tempDir);
            Assert.True(File.Exists(fixturePath));
            Assert.True(File.Exists(manifestPath));

            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(fixturePath);

            const long hardCapBytes = 1_000_000; // 1 MB hard cap
            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_size_mult",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeSize,
                SizeCapBytes = hardCapBytes,
                CanSplit = true
            };

            var splitter = new PstSplitter();
            string jobId = "job-size-mult";
            var report = splitter.Split(fixturePath, outParent, jobId, new ClientProjectContext(), plan);

            Assert.True(report.ConversionSuccess);
            Assert.True(report.Parts.Count > 1, $"Expected multiple parts for 9 x 256KiB messages with 1MB cap, got {report.Parts.Count}");

            int totalWritten = 0;
            foreach (var part in report.Parts)
            {
                var fi = new FileInfo(part.PartFullPath);
                Assert.True(fi.Exists);
                Assert.True(fi.Length <= hardCapBytes, $"Part '{part.PartFileName}' exceeded hard cap {hardCapBytes} with length {fi.Length}");
                Assert.Equal(fi.Length, part.PartSizeBytes);
                totalWritten += part.ItemsWritten;
            }

            Assert.Equal(9, totalWritten);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void PstSplitter_SizeMode_SingleOversizeMessage_FailsWholeJobBeforePublication()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-oversize-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string outParent = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outParent);

        try
        {
            string oversizePst = SplitFixtureBuilder.BuildSingleMessageOversizeFixture(tempDir, attachmentSizeBytes: 1_200_000);
            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(oversizePst);

            const long hardCapBytes = 1_000_000; // 1 MB cap is smaller than the single message (~1.2 MB)
            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_oversize",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeSize,
                SizeCapBytes = hardCapBytes,
                CanSplit = true
            };

            var splitter = new PstSplitter();
            string jobId = "job-oversize";

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                splitter.Split(oversizePst, outParent, jobId, new ClientProjectContext(), plan);
            });

            Assert.Contains("Tek bir ileti", ex.Message);

            // Final bundle directory must NOT exist
            string finalDir = Path.Combine(outParent, $"arsiv-{jobId}");
            Assert.False(Directory.Exists(finalDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void SplitOptions_Validation_InvalidCapsAndModes_Rejected()
    {
        Assert.False(SplitOptions.Validate("year", null).Error != null);
        Assert.True(SplitOptions.Validate("year", null).IsValid);

        var minRes = SplitOptions.Validate("size", 999_999);
        Assert.False(minRes.IsValid);
        Assert.NotNull(minRes.Error);
        Assert.True(minRes.Error.Contains("en az 1.000.000") || (minRes.Error.Contains("1.000.000") && minRes.Error.Contains("en az")));

        var maxRes = SplitOptions.Validate("size", 100_000_000_001);
        Assert.False(maxRes.IsValid);
        Assert.NotNull(maxRes.Error);
        Assert.Contains("en fazla", maxRes.Error);

        var unknownRes = SplitOptions.Validate("unknown", null);
        Assert.False(unknownRes.IsValid);
        Assert.NotNull(unknownRes.Error);
        Assert.True(unknownRes.Error.Contains("Bilinmeyen bölme modu") || unknownRes.Error.Contains("Geçersiz"));

        Assert.True(SplitOptions.Validate("size", 1_000_000).IsValid);
        Assert.True(SplitOptions.Validate("size", 2_048_000_000).IsValid);
    }

    [Fact]
    public void PstSplitter_LatePartVerificationFailure_PreventsEntireBundlePublication()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-late-fault-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string outParent = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outParent);

        try
        {
            var (fixturePath, _, _) = SplitFixtureBuilder.BuildNineMessageFixture(tempDir);
            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(fixturePath);

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_late_fault",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var splitter = new PstSplitter();
            string jobId = "job-late-fault";

            // Injected fault on part verification
            splitter.BeforePartVerificationHook = (partPath, snapshots) =>
            {
                if (partPath.Contains("2024"))
                {
                    throw new InvalidDataException("Simulated verification fault on 2024 part");
                }
            };

            var ex = Assert.Throws<InvalidDataException>(() =>
            {
                splitter.Split(fixturePath, outParent, jobId, new ClientProjectContext(), plan);
            });

            Assert.Contains("Simulated verification fault", ex.Message);

            // Final bundle directory must NOT exist
            string finalDir = Path.Combine(outParent, $"arsiv-{jobId}");
            Assert.False(Directory.Exists(finalDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void PstSplitter_ExistingOutputCollision_LeavesSentinelUntouched()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-collision-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string outParent = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outParent);

        try
        {
            var (fixturePath, _, _) = SplitFixtureBuilder.BuildNineMessageFixture(tempDir);
            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(fixturePath);

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_collision",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var splitter = new PstSplitter();
            string jobId = "job-collision";

            // Pre-create final directory with sentinel file
            string finalDir = Path.Combine(outParent, $"arsiv-{jobId}");
            Directory.CreateDirectory(finalDir);
            string sentinelPath = Path.Combine(finalDir, "sentinel.txt");
            File.WriteAllText(sentinelPath, "SAFE_GUARD_CONTENT");

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                splitter.Split(fixturePath, outParent, jobId, new ClientProjectContext(), plan);
            });

            Assert.Contains("[ÇAKIŞMA ENGELİ]", ex.Message);
            Assert.True(File.Exists(sentinelPath));
            Assert.Equal("SAFE_GUARD_CONTENT", File.ReadAllText(sentinelPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void PstSplitter_SourceMutation_RejectedFailClosed()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-src-mut-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string sourceCopy = Path.Combine(tempDir, "copy.pst");
        string outParent = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outParent);

        try
        {
            string genuinePst = ResolveApprovedPstFixturePath();
            File.Copy(genuinePst, sourceCopy, overwrite: true);

            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(sourceCopy);

            // Mutate source file AFTER analysis
            using (var fs = new FileStream(sourceCopy, FileMode.Open, FileAccess.ReadWrite))
            {
                fs.Seek(512, SeekOrigin.Begin);
                byte b = (byte)fs.ReadByte();
                fs.Seek(512, SeekOrigin.Begin);
                fs.WriteByte((byte)(b ^ 0xFF));
            }

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_mut",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var splitter = new PstSplitter();
            string jobId = "job-mut";

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                splitter.Split(sourceCopy, outParent, jobId, new ClientProjectContext(), plan);
            });

            Assert.Contains("Kaynak dosyada analizden sonra değişiklik tespit edildi", ex.Message);

            // Final bundle directory must NOT exist
            string finalDir = Path.Combine(outParent, $"arsiv-{jobId}");
            Assert.False(Directory.Exists(finalDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void SplitJob_Idempotency_SameBasenameInDifferentDirectories_RejectsConflict()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-samebase-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string dirA = Path.Combine(tempDir, "dirA");
        string dirB = Path.Combine(tempDir, "dirB");
        string outDir = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        Directory.CreateDirectory(outDir);

        string srcA = Path.Combine(dirA, "archive.pst");
        string srcB = Path.Combine(dirB, "archive.pst");

        File.WriteAllText(srcA, "content A");
        File.WriteAllText(srcB, "content B");

        try
        {
            var jm = new JobManager(tempDir);
            var planA = new RegisteredSplitPlan
            {
                PlanId = "plan_A",
                SourceSha256 = "sha_A",
                SplitMode = "year",
                CanSplit = true
            };
            var planB = new RegisteredSplitPlan
            {
                PlanId = "plan_B",
                SourceSha256 = "sha_B",
                SplitMode = "year",
                CanSplit = true
            };

            string idempKey = "same_base_key";
            var ctx = new ClientProjectContext { CompanyId = "c1", ProjectId = "p1" };

            var job1 = jm.StartSplitJob(srcA, outDir, idempKey, ctx, planA, selection: null);
            Assert.NotNull(job1);
            Assert.True(SpinWait.SpinUntil(() => jm.ActiveRunningJobId == null, TimeSpan.FromSeconds(10)));

            // Using same idempotency key for different full path (even with same basename archive.pst) MUST throw conflict
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                jm.StartSplitJob(srcB, outDir, idempKey, ctx, planB, selection: null);
            });

            Assert.Contains("daha önce farklı parametrelerle", ex.Message);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void SplitJob_Idempotency_RestartSensitive_ChangedParameters_RejectsConflict()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-restart-split-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string src = Path.Combine(tempDir, "data.pst");
        string outA = Path.Combine(tempDir, "outA");
        string outB = Path.Combine(tempDir, "outB");
        Directory.CreateDirectory(outA);
        Directory.CreateDirectory(outB);
        File.WriteAllText(src, "test data");

        try
        {
            string idempKey = "restart_split_key";
            var ctx1 = new ClientProjectContext { CompanyId = "c1", ProjectId = "p1" };
            var ctx2 = new ClientProjectContext { CompanyId = "c2", ProjectId = "p1" };

            var planYear = new RegisteredSplitPlan
            {
                PlanId = "plan_year",
                SourceSha256 = "hash1",
                SplitMode = "year",
                CanSplit = true
            };

            var planSize1 = new RegisteredSplitPlan
            {
                PlanId = "plan_size1",
                SourceSha256 = "hash1",
                SplitMode = "size",
                SizeCapBytes = 1_000_000,
                CanSplit = true
            };

            var planSize2 = new RegisteredSplitPlan
            {
                PlanId = "plan_size2",
                SourceSha256 = "hash1",
                SplitMode = "size",
                SizeCapBytes = 2_000_000,
                CanSplit = true
            };

            // Process 1: start year split
            var jm1 = new JobManager(tempDir);
            var job1 = jm1.StartSplitJob(src, outA, idempKey, ctx1, planYear, selection: null);
            Assert.NotNull(job1);
            // The previous process must finish writing before the simulated restart opens its store.
            Assert.True(SpinWait.SpinUntil(() => jm1.ActiveRunningJobId == null, TimeSpan.FromSeconds(10)));

            // Process 2 (simulated restart):
            // 1. Changed mode (year -> size) must conflict
            var jm2 = new JobManager(tempDir);
            var ex1 = Assert.Throws<InvalidOperationException>(() =>
            {
                jm2.StartSplitJob(src, outA, idempKey, ctx1, planSize1, selection: null);
            });
            Assert.Contains("daha önce farklı parametrelerle", ex1.Message);

            // 2. Changed target parent directory (outA -> outB) must conflict
            var ex2 = Assert.Throws<InvalidOperationException>(() =>
            {
                jm2.StartSplitJob(src, outB, idempKey, ctx1, planYear, selection: null);
            });
            Assert.Contains("daha önce farklı parametrelerle", ex2.Message);

            // 3. Changed context (company c1 -> c2) must conflict
            var ex3 = Assert.Throws<InvalidOperationException>(() =>
            {
                jm2.StartSplitJob(src, outA, idempKey, ctx2, planYear, selection: null);
            });
            Assert.Contains("daha önce farklı parametrelerle", ex3.Message);

            // 4. Exact match after restart returns the same job record
            var exactJob = jm2.StartSplitJob(src, outA, idempKey, ctx1, planYear, selection: null);
            Assert.NotNull(exactJob);
            Assert.Equal(job1.JobId, exactJob.JobId);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void SplitJob_Idempotency_LegacyJobWithoutFingerprint_RefusesBasenameMatchAndRejects()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-legacy-nofp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string jobsDir = Path.Combine(tempDir, "jobs");
        Directory.CreateDirectory(jobsDir);

        string src = Path.Combine(tempDir, "legacy.pst");
        string outDir = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(src, "legacy data");

        try
        {
            string idempKey = "legacy_key_without_fp";
            string jobId = "job-legacy-01";

            // Create a legacy record without RequestFingerprint on disk
            var legacyRecord = new LocalJobRecord
            {
                JobId = jobId,
                JobKind = "split",
                SplitMode = "year",
                IdempotencyKey = idempKey,
                SourceFileName = Path.GetFileName(src),
                Status = "completed",
                RequestFingerprint = null // Simulating legacy persisted job
            };

            string json = JsonSerializer.Serialize(legacyRecord, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            File.WriteAllText(Path.Combine(jobsDir, $"{jobId}.json"), json);

            // Startup JobManager: recovers the legacy job
            var jm = new JobManager(tempDir);
            var recovered = jm.GetJob(jobId);
            Assert.NotNull(recovered);
            Assert.Null(recovered.RequestFingerprint);

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_new",
                SourceSha256 = "hash",
                SplitMode = "year",
                CanSplit = true
            };

            // Trying to use that key without fingerprint must NOT match by basename; it must throw conflict
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                jm.StartSplitJob(src, outDir, idempKey, new ClientProjectContext(), plan);
            });

            Assert.Contains("parmak iziyle doğrulanamamaktadır", ex.Message);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void FileHandleRegistry_TypedHandleSegregation_EnforcedStrictly()
    {
        var registry = new FileHandleRegistry();

        string tempFile = Path.GetTempFileName();
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string srcHandle = registry.RegisterSource(tempFile);
            string tgtHandle = registry.RegisterTarget(tempFile);
            string dirHandle = registry.RegisterOutputDir(tempDir);

            Assert.StartsWith("src_", srcHandle);
            Assert.StartsWith("tgt_", tgtHandle);
            Assert.StartsWith("dir_", dirHandle);

            // Output dir handle cannot be resolved as source or target
            Assert.Null(registry.GetSourcePath(dirHandle));
            Assert.Null(registry.GetTargetPath(dirHandle));

            // Source and target handles cannot be resolved as output dir
            Assert.Null(registry.GetOutputDirPath(srcHandle));
            Assert.Null(registry.GetOutputDirPath(tgtHandle));

            // Matching types resolve correctly
            Assert.NotNull(registry.GetSourcePath(srcHandle));
            Assert.NotNull(registry.GetTargetPath(tgtHandle));
            Assert.NotNull(registry.GetOutputDirPath(dirHandle));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void LegacyRoute_RejectsPst_StrictOstOnlyMaintained()
    {
        string pstFixture = ResolveApprovedPstFixturePath();

        var analyzer = new OstAnalyzer();
        // Legacy Analyze strictly rejects PST
        Assert.Throws<InvalidDataException>(() =>
        {
            analyzer.Analyze(pstFixture);
        });

        var converter = new OstToPstConverter();
        string tempOut = Path.Combine(Path.GetTempPath(), $"out-{Guid.NewGuid():N}.pst");

        // Legacy Convert strictly rejects PST
        Assert.Throws<InvalidDataException>(() =>
        {
            converter.Convert(pstFixture, tempOut, "job-legacy-test", new ClientProjectContext(), "sha");
        });
    }

    [Fact]
    public void PstSplitter_ExistingPartialDirectoryOrFile_SentinelPreserved_FailsClosed()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-partial-sentinel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string outParent = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outParent);

        try
        {
            string genuinePst = ResolveApprovedPstFixturePath();
            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(genuinePst);

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_partial_sentinel",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var splitter = new PstSplitter();
            string jobId1 = "job-partial-dir";

            // Scenario 1: Pre-existing partial directory with sentinel file
            string partialDir = Path.Combine(outParent, $"arsiv-{jobId1}.partial");
            Directory.CreateDirectory(partialDir);
            string sentinelFilePath = Path.Combine(partialDir, "sentinel.txt");
            File.WriteAllText(sentinelFilePath, "PRESERVE_STAGING_CONTENT");

            var ex1 = Assert.Throws<InvalidOperationException>(() =>
            {
                splitter.Split(genuinePst, outParent, jobId1, new ClientProjectContext(), plan);
            });

            Assert.Contains("[ÇAKIŞMA ENGELİ]", ex1.Message);
            Assert.True(Directory.Exists(partialDir));
            Assert.True(File.Exists(sentinelFilePath));
            Assert.Equal("PRESERVE_STAGING_CONTENT", File.ReadAllText(sentinelFilePath));

            // Scenario 2: Pre-existing partial as a file
            string jobId2 = "job-partial-file";
            string partialFile = Path.Combine(outParent, $"arsiv-{jobId2}.partial");
            File.WriteAllText(partialFile, "PRESERVE_FILE_CONTENT");

            var ex2 = Assert.Throws<InvalidOperationException>(() =>
            {
                splitter.Split(genuinePst, outParent, jobId2, new ClientProjectContext(), plan);
            });

            Assert.Contains("[ÇAKIŞMA ENGELİ]", ex2.Message);
            Assert.True(File.Exists(partialFile));
            Assert.Equal("PRESERVE_FILE_CONTENT", File.ReadAllText(partialFile));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void PstSplitter_ExistingFinalFileOrDirectory_SentinelPreserved_FailsClosed()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-final-sentinel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string outParent = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outParent);

        try
        {
            string genuinePst = ResolveApprovedPstFixturePath();
            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(genuinePst);

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_final_sentinel",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var splitter = new PstSplitter();
            string jobId = "job-final-file";

            // Pre-existing final destination as a file with sentinel content
            string finalPath = Path.Combine(outParent, $"arsiv-{jobId}");
            File.WriteAllText(finalPath, "PRESERVE_FINAL_FILE");

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                splitter.Split(genuinePst, outParent, jobId, new ClientProjectContext(), plan);
            });

            Assert.Contains("[ÇAKIŞMA ENGELİ]", ex.Message);
            Assert.True(File.Exists(finalPath));
            Assert.Equal("PRESERVE_FINAL_FILE", File.ReadAllText(finalPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void PstSplitter_UnsafeJobIdOrPathTraversal_Rejected()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-traversal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string outParent = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outParent);

        try
        {
            string genuinePst = ResolveApprovedPstFixturePath();
            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(genuinePst);

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_traversal",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var splitter = new PstSplitter();
            var ctx = new ClientProjectContext();

            string[] unsafeJobIds = new[]
            {
                "../escape",
                "sub/path",
                "sub\\path",
                "job..name",
                "c:drive",
                "   "
            };

            foreach (var badId in unsafeJobIds)
            {
                Assert.Throws<ArgumentException>(() =>
                {
                    splitter.Split(genuinePst, outParent, badId, ctx, plan);
                });
            }

            // Assert no files or directories were created inside outParent
            Assert.Empty(Directory.GetFileSystemEntries(outParent));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void PstSplitter_LaterPartTamperAfterVerificationOrHook_BlocksEntireBundle()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-tamper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string outParent = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outParent);

        try
        {
            string genuinePst = ResolveApprovedPstFixturePath();
            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(genuinePst);

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_tamper",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var splitter = new PstSplitter();
            string jobId = "job-tamper";

            // BeforePublicationHook intentionally corrupts one of the candidate PSTs
            splitter.BeforePublicationHook = (stagingDir) =>
            {
                var pstFiles = Directory.GetFiles(stagingDir, "*.pst");
                Assert.NotEmpty(pstFiles);
                string victimPst = pstFiles.First();

                // Tamper with bytes inside the PST file
                using var fs = new FileStream(victimPst, FileMode.Open, FileAccess.ReadWrite);
                fs.Seek(0, SeekOrigin.Begin);
                fs.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
            };

            Assert.ThrowsAny<Exception>(() =>
            {
                splitter.Split(genuinePst, outParent, jobId, new ClientProjectContext(), plan);
            });

            // The final bundle directory MUST NOT exist - publication was completely blocked
            string finalDir = Path.Combine(outParent, $"arsiv-{jobId}");
            Assert.False(Directory.Exists(finalDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void PstSplitter_ExtraOrMissingCandidateFileInStaging_BlocksPublish()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-extra-missing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string outParent = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outParent);

        try
        {
            string genuinePst = ResolveApprovedPstFixturePath();
            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(genuinePst);

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_file_set",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            // Scenario 1: Extra candidate PST in staging directory
            var splitter1 = new PstSplitter();
            string jobId1 = "job-extra-file";

            splitter1.BeforePublicationHook = (stagingDir) =>
            {
                string extraPst = Path.Combine(stagingDir, "arsiv-extra-candidate.pst");
                File.WriteAllText(extraPst, "EXTRA_UNEXPECTED_DATA");
            };

            var ex1 = Assert.Throws<InvalidOperationException>(() =>
            {
                splitter1.Split(genuinePst, outParent, jobId1, new ClientProjectContext(), plan);
            });

            Assert.Contains("beklenmeyen", ex1.Message);
            Assert.False(Directory.Exists(Path.Combine(outParent, $"arsiv-{jobId1}")));

            // Scenario 2: Missing candidate PST in staging directory
            var splitter2 = new PstSplitter();
            string jobId2 = "job-missing-file";

            splitter2.BeforePublicationHook = (stagingDir) =>
            {
                var pstFiles = Directory.GetFiles(stagingDir, "*.pst");
                Assert.NotEmpty(pstFiles);
                File.Delete(pstFiles.First());
            };

            Assert.ThrowsAny<Exception>(() =>
            {
                splitter2.Split(genuinePst, outParent, jobId2, new ClientProjectContext(), plan);
            });

            Assert.False(Directory.Exists(Path.Combine(outParent, $"arsiv-{jobId2}")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void PstSplitter_ManifestAndReport_PartFullPathPointsToFinalBundleAndExistsAfterRename()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-manifest-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string outParent = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outParent);

        try
        {
            string genuinePst = ResolveApprovedPstFixturePath();
            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(genuinePst);

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_manifest_path",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var splitter = new PstSplitter();
            string jobId = "job-manifest-path";

            var report = splitter.Split(genuinePst, outParent, jobId, new ClientProjectContext(), plan);

            Assert.True(report.ConversionSuccess);
            string finalDir = Path.Combine(outParent, $"arsiv-{jobId}");
            Assert.True(Directory.Exists(finalDir));

            // Verify report part paths
            Assert.NotEmpty(report.Parts);
            foreach (var part in report.Parts)
            {
                Assert.StartsWith(finalDir, part.PartFullPath, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(".partial", part.PartFullPath, StringComparison.OrdinalIgnoreCase);
                Assert.True(File.Exists(part.PartFullPath));
            }

            // Verify manifest part paths
            string manifestPath = Path.Combine(finalDir, "arsiv-manifest.json");
            Assert.True(File.Exists(manifestPath));
            string json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<SplitManifest>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            Assert.NotNull(manifest);
            Assert.Equal(report.Parts.Count, manifest.Parts.Count);

            foreach (var part in manifest.Parts)
            {
                Assert.StartsWith(finalDir, part.PartFullPath, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(".partial", part.PartFullPath, StringComparison.OrdinalIgnoreCase);
                Assert.True(File.Exists(part.PartFullPath));
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void PstSplitter_CapAndHashArePostHookFinalValues()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-cap-hash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string outParent = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outParent);

        try
        {
            string genuinePst = ResolveApprovedPstFixturePath();
            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(genuinePst);

            var planYear = new RegisteredSplitPlan
            {
                PlanId = "plan_year_values",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var splitter = new PstSplitter();
            string jobId = "job-final-values";

            var report = splitter.Split(genuinePst, outParent, jobId, new ClientProjectContext(), planYear);

            Assert.True(report.ConversionSuccess);
            string finalDir = Path.Combine(outParent, $"arsiv-{jobId}");

            // Verify every part's size and SHA in report and manifest match exact disk values
            string manifestPath = Path.Combine(finalDir, "arsiv-manifest.json");
            var manifest = JsonSerializer.Deserialize<SplitManifest>(File.ReadAllText(manifestPath), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            Assert.NotNull(manifest);

            foreach (var part in report.Parts)
            {
                var fi = new FileInfo(part.PartFullPath);
                Assert.Equal(fi.Length, part.PartSizeBytes);

                using var sha = SHA256.Create();
                using var fs = File.OpenRead(part.PartFullPath);
                string diskSha = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
                Assert.Equal(diskSha, part.PartSha256);
            }

            foreach (var part in manifest.Parts)
            {
                var fi = new FileInfo(part.PartFullPath);
                Assert.Equal(fi.Length, part.PartSizeBytes);

                using var sha = SHA256.Create();
                using var fs = File.OpenRead(part.PartFullPath);
                string diskSha = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
                Assert.Equal(diskSha, part.PartSha256);
            }

            // Scenario 2: Size mode exceeding cap during post-hook measurement fails closed
            var (fixturePath, _, _) = SplitFixtureBuilder.BuildNineMessageFixture(tempDir);
            var analysis9 = analyzer.AnalyzeSplitSource(fixturePath);

            long tinyCap = 1_000_000L;
            var planSize = new RegisteredSplitPlan
            {
                PlanId = "plan_size_cap",
                SourceSha256 = analysis9.SourceSha256,
                SplitMode = SplitOptions.ModeSize,
                SizeCapBytes = tinyCap,
                CanSplit = true
            };

            var splitterSize = new PstSplitter();
            string jobSizeId = "job-size-cap-hook";

            // In hook, append padding bytes to a part to exceed tinyCap
            splitterSize.BeforePublicationHook = (stagingDir) =>
            {
                var pstFiles = Directory.GetFiles(stagingDir, "*.pst");
                Assert.NotEmpty(pstFiles);
                string victim = pstFiles.First();
                // Append 2 MB of dummy data to exceed 1 MB cap
                using var fs = new FileStream(victim, FileMode.Append, FileAccess.Write);
                byte[] padding = new byte[2_000_000];
                fs.Write(padding, 0, padding.Length);
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                splitterSize.Split(fixturePath, outParent, jobSizeId, new ClientProjectContext(), planSize);
            });

            Assert.Contains("[BOYUT SINIRI AŞILDI]", ex.Message);
            Assert.False(Directory.Exists(Path.Combine(outParent, $"arsiv-{jobSizeId}")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void PstSplitter_BeforePublicationHook_MutatesBodyOrAttachment_PreservingItemCount_FailsClosed()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-tamper-item-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string outParent = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outParent);

        try
        {
            var (fixturePath, _, _) = SplitFixtureBuilder.BuildNineMessageFixture(tempDir);
            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(fixturePath);

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_tamper_item",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var splitter = new PstSplitter();
            string jobId = "job-tamper-item";

            splitter.BeforePublicationHook = (stagingDir) =>
            {
                var pstFiles = Directory.GetFiles(stagingDir, "*.pst");
                Assert.NotEmpty(pstFiles);
                string victimPst = pstFiles.First();

                // Deterministically rebuild/modify the candidate into a temporary PST outside staging,
                // mutating one message body while preserving the exact same physical message count and folder structure.
                string tempRebuiltPst = Path.Combine(tempDir, $"temp_rebuilt_{Guid.NewGuid():N}.pst");
                bool mutated = false;
                int originalCount = 0;
                int rebuiltCount = 0;

                using (var srcPst = PersonalStorage.FromFile(victimPst))
                using (var dstPst = PersonalStorage.Create(tempRebuiltPst, FileFormatVersion.Unicode))
                {
                    FolderInfo ResolveChildFolder(FolderInfo parent, string name)
                    {
                        if (!string.IsNullOrEmpty(parent.DisplayName) &&
                            string.Equals(parent.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                        {
                            return parent;
                        }

                        try
                        {
                            var existing = parent.GetSubFolder(name);
                            if (existing != null)
                            {
                                return existing;
                            }
                        }
                        catch { }

                        try
                        {
                            var subs = parent.GetSubFolders();
                            if (subs != null)
                            {
                                foreach (var s in subs)
                                {
                                    if (string.Equals(s.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        return s;
                                    }
                                }
                            }
                        }
                        catch { }

                        try
                        {
                            return parent.AddSubFolder(name);
                        }
                        catch
                        {
                            try
                            {
                                var fallback = parent.GetSubFolder(name);
                                if (fallback != null) return fallback;
                            }
                            catch { }

                            var subs = parent.GetSubFolders();
                            if (subs != null)
                            {
                                foreach (var s in subs)
                                {
                                    if (string.Equals(s.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        return s;
                                    }
                                }
                            }

                            throw;
                        }
                    }

                    if (srcPst.RootFolder != null && dstPst.RootFolder != null)
                    {
                        // Copy physical root messages directly into target.RootFolder
                        foreach (var mi in srcPst.RootFolder.GetContents())
                        {
                            originalCount++;
                            using var msg = srcPst.ExtractMessage(mi);
                            if (!mutated)
                            {
                                msg.Body = (msg.Body ?? "") + " [TAMPERED_POST_VERIFICATION_MUTATION]";
                                mutated = true;
                            }
                            dstPst.RootFolder.AddMessage(msg);
                            rebuiltCount++;
                        }

                        // Recursively create/copy only source root's child folders beneath target.RootFolder
                        void CopyChildFolders(FolderInfo srcParent, FolderInfo dstParent)
                        {
                            foreach (var srcSub in srcParent.GetSubFolders())
                            {
                                string folderName = string.IsNullOrWhiteSpace(srcSub.DisplayName) ? "SubFolder" : srcSub.DisplayName;
                                FolderInfo dstSub = ResolveChildFolder(dstParent, folderName);

                                foreach (var mi in srcSub.GetContents())
                                {
                                    originalCount++;
                                    using var msg = srcPst.ExtractMessage(mi);
                                    if (!mutated)
                                    {
                                        msg.Body = (msg.Body ?? "") + " [TAMPERED_POST_VERIFICATION_MUTATION]";
                                        mutated = true;
                                    }
                                    dstSub.AddMessage(msg);
                                    rebuiltCount++;
                                }

                                CopyChildFolders(srcSub, dstSub);
                            }
                        }

                        CopyChildFolders(srcPst.RootFolder, dstPst.RootFolder);
                    }
                }

                Assert.True(mutated, "En az bir ileti değiştirilmiş olmalıdır.");
                Assert.True(originalCount > 0, "Kaynak PST boş olmamalıdır.");
                Assert.Equal(originalCount, rebuiltCount);

                File.Move(tempRebuiltPst, victimPst, overwrite: true);

                // Verify that PersonalStorage can still open the file and that item count is preserved
                using (var checkPst = PersonalStorage.FromFile(victimPst))
                {
                    int count = 0;
                    void CountItems(FolderInfo f)
                    {
                        count += f.ContentCount;
                        foreach (var s in f.GetSubFolders()) CountItems(s);
                    }
                    if (checkPst.RootFolder != null) CountItems(checkPst.RootFolder);
                    Assert.Equal(originalCount, count);
                    Assert.True(count > 0, "Item count in victim PST must be preserved and positive");
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                splitter.Split(fixturePath, outParent, jobId, new ClientProjectContext(), plan);
            });

            Assert.Contains("[BÜTÜNLÜK HATASI]", ex.Message);

            // Final bundle directory MUST NOT exist
            string finalDir = Path.Combine(outParent, $"arsiv-{jobId}");
            Assert.False(Directory.Exists(finalDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void PstSplitter_UnexpectedSubdirectoryInStaging_BlocksPublication()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-subdir-fail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string outParent = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outParent);

        try
        {
            string genuinePst = ResolveApprovedPstFixturePath();
            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(genuinePst);

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_unexpected_subdir",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var splitter = new PstSplitter();
            string jobId = "job-unexpected-subdir";

            // In hook, inject an unexpected subdirectory inside stagingDir
            splitter.BeforePublicationHook = (stagingDir) =>
            {
                string rogueSubdir = Path.Combine(stagingDir, "unauthorized-subfolder");
                Directory.CreateDirectory(rogueSubdir);
                File.WriteAllText(Path.Combine(rogueSubdir, "payload.tmp"), "rogue content");
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                splitter.Split(genuinePst, outParent, jobId, new ClientProjectContext(), plan);
            });

            Assert.Contains("beklenmeyen", ex.Message);

            // Final bundle directory MUST NOT exist
            string finalDir = Path.Combine(outParent, $"arsiv-{jobId}");
            Assert.False(Directory.Exists(finalDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }


    [Fact]
    public void SplitJob_InitialPersistenceFailure_RollsBackAllStateAndKeyNotPoisoned()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-init-fail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string src = Path.Combine(tempDir, "archive.pst");
        string outDir = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(src, "dummy source content");

        try
        {
            var jm = new JobManager(tempDir);
            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_init_fail",
                SourceSha256 = "hash123",
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            string idempKey = "key_init_fail";
            var ctx = new ClientProjectContext { CompanyId = "c1", ProjectId = "p1" };

            // Inject fault: SaveJobRecord throws on first attempt
            jm.OnBeforeSaveJobRecord = (rec) => throw new IOException("Disk write failure during initial job save");

            var ex = Assert.Throws<IOException>(() =>
            {
                jm.StartSplitJob(src, outDir, idempKey, ctx, plan, selection: null);
            });
            Assert.Contains("Disk write failure", ex.Message);

            // Verify in-memory state is completely rolled back
            Assert.Empty(jm.GetAllJobs());
            Assert.Null(jm.ActiveRunningJobId);

            // Clear fault: now starting with the exact same idempotency key succeeds
            jm.OnBeforeSaveJobRecord = null;
            var successJob = jm.StartSplitJob(src, outDir, idempKey, ctx, plan, selection: null);
            Assert.NotNull(successJob);
            Assert.Single(jm.GetAllJobs());
            Assert.True(SpinWait.SpinUntil(() => jm.ActiveRunningJobId == null, TimeSpan.FromSeconds(10)));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void SplitJob_PostPublicationReportPersistenceFailure_PreservesBundleAndKnownDiagnostics()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-post-pub-fail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string outDir = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outDir);

        try
        {
            string genuinePst = ResolveApprovedPstFixturePath();
            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(genuinePst);

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_post_pub",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var jm = new JobManager(tempDir);
            string idempKey = "key_post_pub";
            var ctx = new ClientProjectContext { CompanyId = "c1", ProjectId = "p1" };

            // Start the job without fault, disabling background task to test ExecuteSplitJob synchronously
            var jobRecord = jm.StartSplitJob(genuinePst, outDir, idempKey, ctx, plan, selection: null, runInBackground: false);
            string jobId = jobRecord.JobId;

            // Inject fault on report persistence AFTER publication
            jm.OnBeforeSaveReport = (rep) => throw new IOException("Simulated disk error saving report");

            // Execute split job synchronously with fault active
            jm.ExecuteSplitJob(jobId, genuinePst, outDir, ctx, plan, selection: null);

            // 1. Final bundle directory MUST be preserved on disk! (Never erased)
            string finalDir = Path.Combine(outDir, $"arsiv-{jobId}");
            Assert.True(Directory.Exists(finalDir));

            // Manifest and parts must exist
            string manifestPath = Path.Combine(finalDir, "arsiv-manifest.json");
            Assert.True(File.Exists(manifestPath));
            var pstFiles = Directory.GetFiles(finalDir, "*.pst");
            Assert.Equal(5, pstFiles.Length);

            // 2. In-memory job must be in failed/interrupted state with error message, but retaining final bundle path, parts, and counts
            var jobInMem = jm.GetJob(jobId);
            Assert.NotNull(jobInMem);
            Assert.Equal("failed", jobInMem.Status);
            Assert.Equal("Hata", jobInMem.Stage);
            Assert.Contains("Simulated disk error saving report", jobInMem.ErrorMessage);
            Assert.Equal(finalDir, jobInMem.OutputPath);
            Assert.Equal(finalDir, jobInMem.OutputDirectoryPath);
            Assert.NotNull(jobInMem.Parts);
            Assert.Equal(5, jobInMem.Parts.Count);
            Assert.Equal(13, jobInMem.ItemsWritten);

            // 3. In-memory report is populated before save attempt
            var repInMem = jm.GetReport(jobId);
            Assert.NotNull(repInMem);
            Assert.True(repInMem.ConversionSuccess);
            Assert.Equal(finalDir, repInMem.OutputPath);

            // 4. Active slot is released
            Assert.Null(jm.ActiveRunningJobId);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void SplitJob_SecondaryCatchPersistenceFailure_ReleasesActiveSlotInFinally()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bm-sec-fail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string outDir = Path.Combine(tempDir, "out");
        Directory.CreateDirectory(outDir);

        try
        {
            string genuinePst = ResolveApprovedPstFixturePath();
            var analyzer = new OstAnalyzer();
            var analysis = analyzer.AnalyzeSplitSource(genuinePst);

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan_sec_fail",
                SourceSha256 = analysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var jm = new JobManager(tempDir);
            string idempKey = "key_sec_fail";
            var ctx = new ClientProjectContext { CompanyId = "c1", ProjectId = "p1" };

            var jobRecord = jm.StartSplitJob(genuinePst, outDir, idempKey, ctx, plan, selection: null, runInBackground: false);
            string jobId = jobRecord.JobId;

            // Inject fault: SaveReport fails (primary), and then SaveJobRecord in catch fails (secondary)
            jm.OnBeforeSaveReport = (rep) => throw new IOException("Primary report save failure");
            jm.OnBeforeSaveJobRecord = (rec) =>
            {
                if (rec.Status == "failed")
                {
                    throw new IOException("Secondary job record save failure in catch block");
                }
            };

            // Execute split job synchronously
            jm.ExecuteSplitJob(jobId, genuinePst, outDir, ctx, plan, selection: null);

            // Even though secondary persistence in catch threw, finally must release active running slot!
            Assert.Null(jm.ActiveRunningJobId);

            // Able to start a new job immediately without active running concurrency block
            jm.OnBeforeSaveReport = null;
            jm.OnBeforeSaveJobRecord = null;
            var nextJob = jm.StartSplitJob(genuinePst, outDir, "new_subsequent_key", ctx, plan, selection: null);
            Assert.NotNull(nextJob);
            Assert.True(SpinWait.SpinUntil(() => jm.ActiveRunningJobId == null, TimeSpan.FromSeconds(10)));
            Assert.Equal("completed", jm.GetJob(nextJob.JobId)!.Status);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
