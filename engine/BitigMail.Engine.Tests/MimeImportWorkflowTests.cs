using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
using BitigMail.TestingHost;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BitigMail.Engine.Tests;

public class MimeImportWorkflowTests
{
    private static string ResolvePath(string relativePath)
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, relativePath);
            if (File.Exists(candidate) || Directory.Exists(candidate)) return Path.GetFullPath(candidate);
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, relativePath);
            if (File.Exists(candidate) || Directory.Exists(candidate)) return Path.GetFullPath(candidate);
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        throw new FileNotFoundException($"Approved fixture not found: {relativePath}");
    }

    private static string GetCorpusEmlDir() => ResolvePath(Path.Combine("fixtures", "mail-corpus-v1", "eml"));
    private static string GetCorpusMboxPath() => ResolvePath(Path.Combine("fixtures", "mail-corpus-v1", "corpus.mbox"));
    private static string GetEscapesMboxPath() => ResolvePath(Path.Combine("fixtures", "mime-import-v1", "escape-edges", "escapes.mbox"));

    private static string CreateIsolatedTempDir(string prefix)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"bitigmail-qa-{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static object? ExtractResultValue(IResult result)
    {
        if (result == null) return null;
        var prop = result.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        if (prop != null) return prop.GetValue(result);
        return result;
    }

    [Fact]
    public async Task TASK013_WireContract_IntegrationAndSerialization_OutputsWirePath()
    {
        string tempDir = CreateIsolatedTempDir("wire-contract");
        try
        {
            var registry = new FileHandleRegistry();
            var jobManager = new JobManager(tempDir);
            var inspector = new MimeSourceInspector();

            string emlDir = GetCorpusEmlDir();
            var files = Directory.GetFiles(emlDir, "*.eml", SearchOption.TopDirectoryOnly).OrderBy(f => f).ToList();
            var manifest = inspector.BuildEmlFilesManifest(files, "Corpus");

            // 1. Picker handle registration
            string sourceHandle = registry.RegisterMimeSource(manifest, "Corpus EML (12 dosya)");
            Assert.StartsWith("msrc_", sourceHandle);

            // 2. Analyze endpoint
            var analyzeResult = await LocalEngineApiEndpoints.HandleMimeAnalyze(registry, new AnalyzeRequest(sourceHandle));
            object? analyzeValue = ExtractResultValue(analyzeResult);
            Assert.NotNull(analyzeValue);

            // 3. Selection preview endpoint
            var previewResult = LocalEngineApiEndpoints.HandleMimePreview(registry, new SelectionPreviewRequest(sourceHandle));
            object? previewValue = ExtractResultValue(previewResult);
            Assert.NotNull(previewValue);

            var entry = registry.GetMimeSourceEntry(sourceHandle);
            Assert.NotNull(entry?.AnalysisResult);

            // 4. Register target and Start job endpoint
            string targetPstPath = Path.Combine(tempDir, "wire-target.pst");
            string targetHandle = registry.RegisterTarget(targetPstPath);

            var startReq = new StartJobRequest(
                sourceHandle,
                targetHandle,
                IdempotencyKey: "wire-test-idemp-" + Guid.NewGuid().ToString("N"),
                ClientContext: new ClientProjectContext
                {
                    CompanyId = "cmp-test",
                    CompanyName = "Test Şirket",
                    ProjectId = "prj-test",
                    ProjectName = "MIME Proje"
                }
            );

            var startResult = LocalEngineApiEndpoints.HandleMimeStart(registry, jobManager, startReq);
            object? startValue = ExtractResultValue(startResult);
            Assert.NotNull(startValue);

            // Extract Job ID
            string? jobId = null;
            if (startValue != null)
            {
                var idProp = startValue.GetType().GetProperty("jobId") ?? startValue.GetType().GetProperty("JobId");
                jobId = idProp?.GetValue(startValue)?.ToString();
            }
            Assert.False(string.IsNullOrEmpty(jobId));

            // Wait for completion (synchronously or polled)
            LocalJobRecord? completedJob = null;
            for (int i = 0; i < 60; i++)
            {
                completedJob = jobManager.GetJob(jobId!);
                if (completedJob != null && (completedJob.Status == "completed" || completedJob.Status == "failed"))
                {
                    break;
                }
                await Task.Delay(250);
            }

            Assert.NotNull(completedJob);
            Assert.Equal("completed", completedJob.Status);

            // 5. Job report endpoint
            var report = jobManager.GetReport(jobId!);
            Assert.NotNull(report);
            Assert.True(report.ConversionSuccess);
            Assert.NotNull(report.MimeImport);
            Assert.Equal("DIFFERENCES", report.MimeImport.OverallQualification);

            // 6. Bundle all actual responses and serialize wire JSON
            var wireBundle = new
            {
                EndpointContractVersion = "TASK-013-BACKEND-WIRE-V1",
                GeneratedAtUtc = DateTime.UtcNow.ToString("o"),
                AnalyzeResponse = analyzeValue,
                PreviewResponse = previewValue,
                StartJobResponse = startValue,
                JobStatusResponse = completedJob,
                ReportResponse = report
            };

            string wireJsonPath = Path.Combine(Path.GetTempPath(), $"task013-backend-wire-{Guid.NewGuid():N}.json");
            string serializedJson = JsonSerializer.Serialize(wireBundle, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(wireJsonPath, serializedJson, Encoding.UTF8);

            // Authoritative output requirement
            Console.WriteLine($"TASK013_BACKEND_WIRE={wireJsonPath}");
            Trace.WriteLine($"TASK013_BACKEND_WIRE={wireJsonPath}");

            Assert.True(File.Exists(wireJsonPath));
            Assert.True(new FileInfo(wireJsonPath).Length > 200);
            Assert.Contains("OverallQualification", serializedJson);
            Assert.Contains("DIFFERENCES", serializedJson);
            Assert.Contains("SourceSetFingerprint", serializedJson);
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
    public void RealCorpus12Messages_EmlFiles_PreservesAll12MessagesAttachmentsAndCid()
    {
        string tempDir = CreateIsolatedTempDir("corpus-eml");
        try
        {
            var inspector = new MimeSourceInspector();
            string emlDir = GetCorpusEmlDir();
            var files = Directory.GetFiles(emlDir, "*.eml", SearchOption.TopDirectoryOnly).OrderBy(f => f).ToList();
            var manifest = inspector.BuildEmlFilesManifest(files, "Corpus");

            var analysis = inspector.Analyze(manifest);
            Assert.True(analysis.Preflight.CanConvert);
            Assert.False(analysis.Preflight.HasTrialBlocker);
            Assert.Equal(12, manifest.Entries.Count);

            string targetPst = Path.Combine(tempDir, "eml-corpus-out.pst");
            var converter = new MimeToPstConverter();
            var report = converter.Convert(
                manifest,
                targetPst,
                "job-test-eml",
                new ClientProjectContext { CompanyName = "Test", ProjectName = "Corpus" });

            Assert.True(report.ConversionSuccess);
            Assert.Equal(12, report.ItemsWritten);
            Assert.Equal(12, report.TotalItems);
            Assert.NotNull(report.MimeImport);
            Assert.Equal(12, report.MimeImport.ImportedMessagesCount);
            Assert.Equal(4, report.MimeImport.AttachmentsWrittenCount);
            Assert.Equal(1, report.MimeImport.InlineCidCount);
            Assert.Equal("DIFFERENCES", report.MimeImport.OverallQualification);

            // Re-open PST authoritatively
            using var pst = PersonalStorage.FromFile(targetPst);
            var corpusFolder = pst.RootFolder.GetSubFolder("Corpus");
            Assert.NotNull(corpusFolder);
            Assert.Equal(12, corpusFolder.ContentCount);

            int totalAtts = 0;
            foreach (var msgInfo in corpusFolder.EnumerateMessages())
            {
                using var mapi = pst.ExtractMessage(msgInfo);
                totalAtts += mapi.Attachments.Count;
            }
            Assert.Equal(4, totalAtts);
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
    public void RealCorpus12Messages_MboxFile_PreservesAll12MessagesAttachmentsAndCid()
    {
        string tempDir = CreateIsolatedTempDir("corpus-mbox");
        try
        {
            var inspector = new MimeSourceInspector();
            string mboxPath = GetCorpusMboxPath();
            var manifest = inspector.BuildMboxManifest(mboxPath, "corpus");

            var analysis = inspector.Analyze(manifest);
            Assert.True(analysis.Preflight.CanConvert);
            Assert.False(analysis.Preflight.HasTrialBlocker);
            Assert.Equal(12, manifest.Entries.Count);

            string targetPst = Path.Combine(tempDir, "mbox-corpus-out.pst");
            var converter = new MimeToPstConverter();
            var report = converter.Convert(
                manifest,
                targetPst,
                "job-test-mbox",
                new ClientProjectContext { CompanyName = "Test", ProjectName = "MboxCorpus" });

            Assert.True(report.ConversionSuccess);
            Assert.Equal(12, report.ItemsWritten);
            Assert.NotNull(report.MimeImport);
            Assert.Equal(12, report.MimeImport.ImportedMessagesCount);
            Assert.Equal(4, report.MimeImport.AttachmentsWrittenCount);
            Assert.Equal(1, report.MimeImport.InlineCidCount);
            Assert.Equal("DIFFERENCES", report.MimeImport.OverallQualification);

            // Verify narrow LF restoration for msg-09/10 was reported
            Assert.True(report.MimeImport.RestoredTrailingLfCount >= 2);

            // Re-open PST
            using var pst = PersonalStorage.FromFile(targetPst);
            var folder = pst.RootFolder.GetSubFolder("corpus");
            Assert.NotNull(folder);
            Assert.Equal(12, folder.ContentCount);
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
    public void FolderAndDateFiltering_ExactOwnFolder_InclusiveUtc3_DuplicatesPreserved()
    {
        string tempDir = CreateIsolatedTempDir("filtering");
        try
        {
            var inspector = new MimeSourceInspector();
            string emlDir = GetCorpusEmlDir();
            var files = Directory.GetFiles(emlDir, "*.eml", SearchOption.TopDirectoryOnly).OrderBy(f => f).ToList();
            var manifest = inspector.BuildEmlFilesManifest(files, "Corpus");

            // Filter Istanbul dates: 2024-01-01 to 2024-02-16 (from expected-mime.json: matches msg-03, msg-04, msg-11 -> 3 items)
            var (preview, registered) = MimeSelectionEngine.EvaluateSelection(
                manifest,
                "msrc_dummy",
                requestedFolderIds: null,
                startDate: "2024-01-01",
                endDate: "2024-02-16",
                preflight: new PreflightCheckResult { CanConvert = true },
                cancellationToken: CancellationToken.None);

            Assert.Equal(3, preview.SelectedCount);
            Assert.Equal(9, preview.ExcludedCount);
            Assert.Equal(12, preview.TotalCount);

            string targetPst = Path.Combine(tempDir, "filtered-date.pst");
            var converter = new MimeToPstConverter();
            var report = converter.Convert(
                manifest,
                targetPst,
                "job-test-date-filter",
                new ClientProjectContext(),
                selection: registered);

            Assert.True(report.ConversionSuccess);
            Assert.Equal(3, report.ItemsWritten);

            // Verify duplicate retention: msg-01.eml and msg-02.eml have identical content/Message-ID.
            // When converting both, both must be written as 2 distinct items.
            var pairFiles = files.Where(f => f.EndsWith("msg-01.eml") || f.EndsWith("msg-02.eml")).ToList();
            var pairManifest = inspector.BuildEmlFilesManifest(pairFiles, "Pair");
            string pairPst = Path.Combine(tempDir, "pair.pst");
            var pairReport = converter.Convert(
                pairManifest,
                pairPst,
                "job-test-pair",
                new ClientProjectContext());

            Assert.True(pairReport.ConversionSuccess);
            Assert.Equal(2, pairReport.ItemsWritten);

            using var pst = PersonalStorage.FromFile(pairPst);
            var pairFolder = pst.RootFolder.GetSubFolder("Pair");
            Assert.NotNull(pairFolder);
            Assert.Equal(2, pairFolder.ContentCount);
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
    public void EscapeEdges_MboxrdQuotingUnescapedBeforeMimeDecoding()
    {
        string tempDir = CreateIsolatedTempDir("escapes");
        try
        {
            var inspector = new MimeSourceInspector();
            string escapesPath = GetEscapesMboxPath();
            var manifest = inspector.BuildMboxManifest(escapesPath, "escapes");

            Assert.Equal(3, manifest.Entries.Count);

            string targetPst = Path.Combine(tempDir, "escapes.pst");
            var converter = new MimeToPstConverter();
            var report = converter.Convert(
                manifest,
                targetPst,
                "job-escapes",
                new ClientProjectContext());

            Assert.True(report.ConversionSuccess);
            Assert.Equal(3, report.ItemsWritten);

            using var pst = PersonalStorage.FromFile(targetPst);
            var folder = pst.RootFolder.GetSubFolder("escapes");
            Assert.NotNull(folder);
            Assert.Equal(3, folder.ContentCount);
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
    public void MalformedEmptyAndMissingDate_BoundaryCases()
    {
        string tempDir = CreateIsolatedTempDir("boundary-cases");
        try
        {
            // 1. Missing date boundary
            string missingDateDir = Path.Combine(tempDir, "missing-date");
            Directory.CreateDirectory(missingDateDir);
            string noDateEml = Path.Combine(missingDateDir, "no-date.eml");
            File.WriteAllText(noDateEml,
                "From: sender@test.local\r\n" +
                "To: receiver@test.local\r\n" +
                "Subject: Tarihsiz Mesaj\r\n" +
                "\r\n" +
                "Bu iletide Date basligi yoktur.\r\n");

            var inspector = new MimeSourceInspector();
            var manifest = inspector.BuildEmlDirectoryManifest(missingDateDir);

            // Without date filter: imported
            var converter = new MimeToPstConverter();
            string outPst1 = Path.Combine(tempDir, "no-date.pst");
            var report1 = converter.Convert(manifest, outPst1, "job-no-date", new ClientProjectContext());
            Assert.True(report1.ConversionSuccess);
            Assert.Equal(1, report1.ItemsWritten);

            // With date filter: missing date is excluded
            var (preview, registered) = MimeSelectionEngine.EvaluateSelection(
                manifest,
                "msrc_test",
                null,
                startDate: "2024-01-01",
                endDate: "2024-12-31",
                preflight: new PreflightCheckResult { CanConvert = true },
                cancellationToken: CancellationToken.None);

            Assert.Equal(0, preview.SelectedCount);
            Assert.Equal(1, preview.ExcludedCount);
            Assert.Equal(1, preview.MissingDateExcludedCount);

            // 2. Empty EML file: fails closed
            string emptyDir = Path.Combine(tempDir, "empty-eml");
            Directory.CreateDirectory(emptyDir);
            string emptyEml = Path.Combine(emptyDir, "empty.eml");
            File.WriteAllBytes(emptyEml, Array.Empty<byte>());

            var emptyManifest = inspector.BuildEmlDirectoryManifest(emptyDir);
            string outPst2 = Path.Combine(tempDir, "empty.pst");
            Assert.Throws<InvalidOperationException>(() =>
                converter.Convert(emptyManifest, outPst2, "job-empty", new ClientProjectContext()));

            // 3. Malformed MIME file: corrupt stream fails closed
            string malformedDir = Path.Combine(tempDir, "malformed-eml");
            Directory.CreateDirectory(malformedDir);
            string malformedEml = Path.Combine(malformedDir, "malformed.eml");
            File.WriteAllText(malformedEml, "CORRUPT RAW BYTES \0\0\xFF\xFE NOT RFC822");

            var malformedManifest = inspector.BuildEmlDirectoryManifest(malformedDir);
            string outPst3 = Path.Combine(tempDir, "malformed.pst");
            Assert.Throws<InvalidOperationException>(() =>
                converter.Convert(malformedManifest, outPst3, "job-malformed", new ClientProjectContext()));
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
    public void TrialLimit_Over50ItemsPerFolder_BlocksConversion()
    {
        string tempDir = CreateIsolatedTempDir("trial-limit");
        try
        {
            string oversizeDir = Path.Combine(tempDir, "oversize");
            Directory.CreateDirectory(oversizeDir);

            for (int i = 1; i <= 52; i++)
            {
                File.WriteAllText(Path.Combine(oversizeDir, $"msg-{i:D3}.eml"),
                    $"From: s{i}@test.local\r\nTo: r{i}@test.local\r\nSubject: Test {i}\r\nDate: Mon, 10 Jan 2022 10:00:00 +0300\r\n\r\nBody {i}");
            }

            var inspector = new MimeSourceInspector();
            var manifest = inspector.BuildEmlDirectoryManifest(oversizeDir);

            var analysis = inspector.Analyze(manifest);
            Assert.False(analysis.Preflight.CanConvert);
            Assert.True(analysis.Preflight.HasTrialBlocker);
            Assert.Contains("50", analysis.Preflight.TrialBlockerReason);

            var converter = new MimeToPstConverter();
            string outPst = Path.Combine(tempDir, "oversize.pst");

            // Attempting to convert throws preflight blocker
            var ex = Assert.Throws<InvalidOperationException>(() =>
                converter.Convert(manifest, outPst, "job-oversize", new ClientProjectContext()));
            Assert.Contains("[ÖN KONTROL ENGELİ]", ex.Message);
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
    public void VendorMarkerTextInUserBody_PreservedIntact()
    {
        string tempDir = CreateIsolatedTempDir("marker-text");
        try
        {
            string emlDir = Path.Combine(tempDir, "user-marker");
            Directory.CreateDirectory(emlDir);

            string userContent =
                "Kullanici tarafindan yazilmis ilk satir.\r\n" +
                "Evaluation Only. Created with Aspose.Email. for .NET. Copyright 2002-2024 Aspose Pty Ltd.\r\n" +
                "Kullanici tarafindan yazilmis son satir.\r\n";

            string emlPath = Path.Combine(emlDir, "marker-in-body.eml");
            File.WriteAllText(emlPath,
                "From: user@test.local\r\n" +
                "To: user@test.local\r\n" +
                "Subject: Marker Test\r\n" +
                "Date: Mon, 15 Jan 2024 10:00:00 +0300\r\n" +
                "MIME-Version: 1.0\r\n" +
                "Content-Type: text/plain; charset=utf-8\r\n" +
                "\r\n" +
                userContent);

            var inspector = new MimeSourceInspector();
            var manifest = inspector.BuildEmlDirectoryManifest(emlDir);

            string targetPst = Path.Combine(tempDir, "marker.pst");
            var converter = new MimeToPstConverter();
            var report = converter.Convert(manifest, targetPst, "job-marker", new ClientProjectContext());

            Assert.True(report.ConversionSuccess);

            using var pst = PersonalStorage.FromFile(targetPst);
            var folder = pst.RootFolder.GetSubFolder("user-marker");
            Assert.NotNull(folder);

            var msgInfo = folder.EnumerateMessages().First();
            using var mapi = pst.ExtractMessage(msgInfo);

            string body = mapi.Body;
            Assert.Contains("Kullanici tarafindan yazilmis ilk satir.", body);
            Assert.Contains("Evaluation Only. Created with Aspose.Email.", body);
            Assert.Contains("Kullanici tarafindan yazilmis son satir.", body);
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
    public void SourceMutationAndReparsePoints_RejectedFailClosed()
    {
        string tempDir = CreateIsolatedTempDir("mutation");
        try
        {
            string emlDir = Path.Combine(tempDir, "eml");
            Directory.CreateDirectory(emlDir);
            string file1 = Path.Combine(emlDir, "msg1.eml");
            File.WriteAllText(file1, "From: a@t.l\r\nTo: b@t.l\r\nSubject: S1\r\nDate: Mon, 10 Jan 2022 10:00:00 +0300\r\n\r\nB1");

            var inspector = new MimeSourceInspector();
            var manifest = inspector.BuildEmlDirectoryManifest(emlDir);

            // Mutate file content
            File.WriteAllText(file1, "From: a@t.l\r\nTo: b@t.l\r\nSubject: MUTATED\r\nDate: Mon, 10 Jan 2022 10:00:00 +0300\r\n\r\nB1");

            var converter = new MimeToPstConverter();
            string outPst = Path.Combine(tempDir, "mutated.pst");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                converter.Convert(manifest, outPst, "job-mut", new ClientProjectContext()));
            Assert.Contains("[BÜTÜNLÜK ENGELİ]", ex.Message);
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
    public void TargetCollision_RefusesExistingFile()
    {
        string tempDir = CreateIsolatedTempDir("collision");
        try
        {
            string emlDir = Path.Combine(tempDir, "eml");
            Directory.CreateDirectory(emlDir);
            string file1 = Path.Combine(emlDir, "msg1.eml");
            File.WriteAllText(file1, "From: a@t.l\r\nTo: b@t.l\r\nSubject: S1\r\nDate: Mon, 10 Jan 2022 10:00:00 +0300\r\n\r\nB1");

            var inspector = new MimeSourceInspector();
            var manifest = inspector.BuildEmlDirectoryManifest(emlDir);

            string targetPst = Path.Combine(tempDir, "existing.pst");
            File.WriteAllText(targetPst, "EXISTING SENTINEL FILE");

            var converter = new MimeToPstConverter();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                converter.Convert(manifest, targetPst, "job-col", new ClientProjectContext()));

            Assert.Contains("[GÜVENLİK ENGELİ]", ex.Message);
            Assert.Equal("EXISTING SENTINEL FILE", File.ReadAllText(targetPst));
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
    public async Task SharedActiveJobSlot_PreventsConcurrentJobsAcrossKinds()
    {
        string tempDir = CreateIsolatedTempDir("shared-slot");
        try
        {
            var jobManager = new JobManager(tempDir);
            var inspector = new MimeSourceInspector();

            string emlDir = GetCorpusEmlDir();
            var files = Directory.GetFiles(emlDir, "*.eml", SearchOption.TopDirectoryOnly).OrderBy(f => f).ToList();
            var manifest = inspector.BuildEmlFilesManifest(files, "Corpus");

            string target1 = Path.Combine(tempDir, "target1.pst");
            string target2 = Path.Combine(tempDir, "target2.pst");

            // Start background MIME job
            var job1 = jobManager.StartMimeJob(
                manifest,
                target1,
                idempotencyKey: "key-1",
                clientContext: new ClientProjectContext(),
                selection: null,
                runInBackground: true);

            Assert.NotNull(job1);

            // Attempting to start another job immediately throws active job conflict
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                jobManager.StartMimeJob(
                    manifest,
                    target2,
                    idempotencyKey: "key-2",
                    clientContext: new ClientProjectContext(),
                    selection: null,
                    runInBackground: false);
            });
            Assert.Contains("devam eden etkin bir", ex.Message);

            // Wait for job1 to complete
            for (int i = 0; i < 60; i++)
            {
                var j = jobManager.GetJob(job1.JobId);
                if (j != null && (j.Status == "completed" || j.Status == "failed"))
                {
                    break;
                }
                await Task.Delay(200);
            }

            // After completion, slot is free
            var completedJob = jobManager.GetJob(job1.JobId);
            Assert.Equal("completed", completedJob?.Status);
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
    public void ContextAwareIdempotency_SameReturnsSameJob_DifferentConflicts_RestartInterrupted()
    {
        string tempDir = CreateIsolatedTempDir("idempotency");
        try
        {
            var jobManager = new JobManager(tempDir);
            var inspector = new MimeSourceInspector();

            string emlDir = GetCorpusEmlDir();
            var files = Directory.GetFiles(emlDir, "*.eml", SearchOption.TopDirectoryOnly).Take(2).ToList();
            var manifest = inspector.BuildEmlFilesManifest(files, "Corpus");

            string target1 = Path.Combine(tempDir, "target1.pst");
            var ctx1 = new ClientProjectContext { CompanyId = "c1", ProjectId = "p1", CompanyName = "C1", ProjectName = "P1" };
            string key = "idemp-key-shared";

            // Run first job synchronously
            var job1 = jobManager.StartMimeJob(manifest, target1, key, ctx1, selection: null, runInBackground: false);
            Assert.NotNull(job1);

            // Exact same request with same key returns existing job
            var job2 = jobManager.StartMimeJob(manifest, target1, key, ctx1, selection: null, runInBackground: false);
            Assert.Equal(job1.JobId, job2.JobId);

            // Conflicting target with same key throws InvalidOperationException
            string target2 = Path.Combine(tempDir, "target2.pst");
            Assert.Throws<InvalidOperationException>(() =>
                jobManager.StartMimeJob(manifest, target2, key, ctx1, selection: null, runInBackground: false));

            // Restart test: create an in-progress record in runtime directory
            string jobsDir = Path.Combine(tempDir, "jobs");
            Directory.CreateDirectory(jobsDir);
            string inProgressJobId = "job-mock-inprogress";
            var inProgressRecord = new LocalJobRecord
            {
                JobId = inProgressJobId,
                JobKind = "mime-import",
                Status = "converting",
                Stage = "İçe Aktarılıyor",
                CreatedAt = DateTimeOffset.UtcNow
            };
            File.WriteAllText(Path.Combine(jobsDir, $"{inProgressJobId}.json"), JsonSerializer.Serialize(inProgressRecord, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            // New JobManager instance recovers it as interrupted
            var newJobManager = new JobManager(tempDir);
            var recoveredJob = newJobManager.GetJob(inProgressJobId);
            Assert.NotNull(recoveredJob);
            Assert.Equal("interrupted", recoveredJob.Status);
            Assert.Equal("Kesintiye Uğradı", recoveredJob.Stage);
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
    public void ReportPersistenceAndReload_BackwardCompatibleWithOldReports()
    {
        // 1. Report with MimeImport detail
        var report = new ConversionReport
        {
            JobId = "job-report-test",
            ConversionSuccess = true,
            SourceSetFingerprint = "fingerprint_12345",
            SourceKind = "eml-files",
            Dialect = "rfc822",
            IgnoredNonEmlFilesCount = 0,
            MimeImport = new MimeImportReportDetail
            {
                SourceKind = "eml-files",
                Dialect = "rfc822",
                TotalSourceItems = 12,
                ImportedMessagesCount = 12,
                AttachmentsWrittenCount = 4,
                InlineCidCount = 1,
                RestoredTrailingLfCount = 2,
                OverallQualification = "DIFFERENCES",
                Differences = new List<MimeMessageDifferenceSummary>
                {
                    new()
                    {
                        Ordinal = 1,
                        MessageId = "<msg1@test.local>",
                        SubjectDifference = "Aspose Evaluation Suffix",
                        ObservedSubject = "Subject [Evaluation Suffix]"
                    }
                }
            }
        };

        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        var deserialized = JsonSerializer.Deserialize<ConversionReport>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("fingerprint_12345", deserialized.SourceSetFingerprint);
        Assert.NotNull(deserialized.MimeImport);
        Assert.Equal(12, deserialized.MimeImport.ImportedMessagesCount);
        Assert.Equal(4, deserialized.MimeImport.AttachmentsWrittenCount);
        Assert.Equal("DIFFERENCES", deserialized.MimeImport.OverallQualification);

        // 2. Legacy OST report without MimeImport detail
        string legacyJson = @"{
            ""JobId"": ""job-legacy-01"",
            ""ConversionSuccess"": true,
            ""TotalItems"": 15,
            ""ItemsWritten"": 15,
            ""SourcePath"": ""input.ost"",
            ""OutputPath"": ""output.pst""
        }";

        var legacyReport = JsonSerializer.Deserialize<ConversionReport>(legacyJson);
        Assert.NotNull(legacyReport);
        Assert.True(legacyReport.ConversionSuccess);
        Assert.Null(legacyReport.MimeImport);
        Assert.Null(legacyReport.SourceSetFingerprint);
    }

    [Fact]
    public void CompletedJobId_CanBeRoutedIntoTestingHostSplitFixture()
    {
        string tempDir = CreateIsolatedTempDir("split-route");
        try
        {
            var jobManager = new JobManager(tempDir);
            var inspector = new MimeSourceInspector();

            string emlDir = GetCorpusEmlDir();
            var files = Directory.GetFiles(emlDir, "*.eml", SearchOption.TopDirectoryOnly).OrderBy(f => f).ToList();
            var manifest = inspector.BuildEmlFilesManifest(files, "Corpus");

            string targetPst = Path.Combine(tempDir, "imported.pst");
            var job = jobManager.StartMimeJob(
                manifest,
                targetPst,
                idempotencyKey: "split-route-idemp",
                clientContext: new ClientProjectContext(),
                selection: null,
                runInBackground: false);

            Assert.NotNull(job);
            Assert.Equal("completed", job.Status);
            Assert.True(File.Exists(targetPst));

            // Use server-resolved job route in TestingFilePickerService
            var handleRegistry = new FileHandleRegistry();
            var picker = new TestingFilePickerService(handleRegistry, jobManager);

            // Try job:... prefix
            bool success = picker.TrySetSplitSourceFixture($"job:{job.JobId}", out string error);
            Assert.True(success, error);
            Assert.Equal(string.Empty, error);
            Assert.Equal(targetPst, picker.ActiveSplitFixturePath);

            // Execute PstSplitter on the newly created PST from MIME import
            var splitter = new PstSplitter();
            var splitDir = Path.Combine(tempDir, "split-out");
            Directory.CreateDirectory(splitDir);

            var analyzer = new OstAnalyzer();
            var splitAnalysis = analyzer.AnalyzeSplitSource(targetPst);
            Assert.NotNull(splitAnalysis);

            var plan = new RegisteredSplitPlan
            {
                PlanId = "plan-split-mime",
                SourceSha256 = splitAnalysis.SourceSha256,
                SplitMode = SplitOptions.ModeYear,
                CanSplit = true
            };

            var splitReport = splitter.Split(targetPst, splitDir, "job-split-after-mime", new ClientProjectContext(), plan);
            Assert.True(splitReport.ConversionSuccess);
            Assert.True(splitReport.Parts.Count > 0);
            Assert.Equal(12, splitReport.ItemsWritten);
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
    public void PartialSentinel_PreservedIntact_NeverDeletedOrOverwritten()
    {
        string tempDir = CreateIsolatedTempDir("partial-sentinel");
        try
        {
            string emlDir = Path.Combine(tempDir, "eml");
            Directory.CreateDirectory(emlDir);
            string emlPath = Path.Combine(emlDir, "test.eml");
            File.WriteAllText(emlPath,
                "From: a@test.local\r\n" +
                "To: b@test.local\r\n" +
                "Subject: Sentinel Test\r\n" +
                "Date: Mon, 15 Jan 2024 10:00:00 +0300\r\n" +
                "MIME-Version: 1.0\r\n" +
                "Content-Type: text/plain; charset=utf-8\r\n\r\n" +
                "Sentinel Body Content\r\n");

            var inspector = new MimeSourceInspector();
            var manifest = inspector.BuildEmlDirectoryManifest(emlDir);

            string targetPst = Path.Combine(tempDir, "output.pst");
            string externalPartialSentinel = targetPst + ".partial";
            const string sentinelPayload = "EXISTING_EXTERNAL_OR_USER_PARTIAL_FILE_CONTENT";
            File.WriteAllText(externalPartialSentinel, sentinelPayload);

            var converter = new MimeToPstConverter();
            string jobId = "job-sentinel-check";
            var report = converter.Convert(manifest, targetPst, jobId, new ClientProjectContext());

            Assert.True(report.ConversionSuccess);
            Assert.True(File.Exists(targetPst));

            // Must preserve pre-existing target+'.partial' completely untouched
            Assert.True(File.Exists(externalPartialSentinel));
            Assert.Equal(sentinelPayload, File.ReadAllText(externalPartialSentinel));

            // Task-owned partial must have been published (moved) and thus no longer exists
            string taskOwnedPartial = $"{targetPst}.{jobId}.partial";
            Assert.False(File.Exists(taskOwnedPartial));
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
    public void PreExistingTaskOwnedPartial_ThrowsCollisionRefusal_AndPreservesFile()
    {
        string tempDir = CreateIsolatedTempDir("partial-collision");
        try
        {
            string emlDir = Path.Combine(tempDir, "eml");
            Directory.CreateDirectory(emlDir);
            string emlPath = Path.Combine(emlDir, "test.eml");
            File.WriteAllText(emlPath,
                "From: a@test.local\r\n" +
                "To: b@test.local\r\n" +
                "Subject: Collision Test\r\n" +
                "Date: Mon, 15 Jan 2024 10:00:00 +0300\r\n" +
                "MIME-Version: 1.0\r\n" +
                "Content-Type: text/plain; charset=utf-8\r\n\r\n" +
                "Collision Body Content\r\n");

            var inspector = new MimeSourceInspector();
            var manifest = inspector.BuildEmlDirectoryManifest(emlDir);

            string targetPst = Path.Combine(tempDir, "output.pst");
            string jobId = "job-collision-check";
            string taskOwnedPartial = $"{targetPst}.{jobId}.partial";
            const string existingPartialContent = "PRE_EXISTING_RUNNING_JOB_PARTIAL";
            File.WriteAllText(taskOwnedPartial, existingPartialContent);

            var converter = new MimeToPstConverter();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                converter.Convert(manifest, targetPst, jobId, new ClientProjectContext()));

            Assert.Contains("[ÇAKIŞMA ENGELİ]", ex.Message);

            // The pre-existing file must NOT have been deleted by clean-up logic
            Assert.True(File.Exists(taskOwnedPartial));
            Assert.Equal(existingPartialContent, File.ReadAllText(taskOwnedPartial));
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
    public void TerminalLfInPlainBody_PreservedOrRestoredDeterministically()
    {
        string tempDir = CreateIsolatedTempDir("terminal-lf");
        try
        {
            string emlDir = Path.Combine(tempDir, "eml");
            Directory.CreateDirectory(emlDir);
            string emlPath = Path.Combine(emlDir, "msg-trailing-lf.eml");
            string originalPlainWithLf = "Line 1\r\nLine 2\r\nLine 3\r\n";
            File.WriteAllText(emlPath,
                "From: a@test.local\r\n" +
                "To: b@test.local\r\n" +
                "Subject: Trailing LF Test\r\n" +
                "Date: Mon, 15 Jan 2024 10:00:00 +0300\r\n" +
                "MIME-Version: 1.0\r\n" +
                "Content-Type: text/plain; charset=utf-8\r\n\r\n" +
                originalPlainWithLf);

            var inspector = new MimeSourceInspector();
            var manifest = inspector.BuildEmlDirectoryManifest(emlDir);

            string targetPst = Path.Combine(tempDir, "terminal-lf.pst");
            var converter = new MimeToPstConverter();
            var report = converter.Convert(manifest, targetPst, "job-terminal-lf", new ClientProjectContext());

            Assert.True(report.ConversionSuccess);

            using var pst = PersonalStorage.FromFile(targetPst);
            var folder = pst.RootFolder.GetSubFolder("eml");
            Assert.NotNull(folder);
            var msgInfo = folder.EnumerateMessages().First();
            using var mapi = pst.ExtractMessage(msgInfo);

            string body = mapi.Body.Replace("\r\n", "\n").Replace("\r", "\n");
            // Deterministically preserved trailing LF
            Assert.EndsWith("\n", body);
            Assert.Contains("Line 1\nLine 2\nLine 3\n", body);
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
    public void MarkerLikeUserContentInPlainAndHtml_PreservedVerbatim()
    {
        string tempDir = CreateIsolatedTempDir("marker-plain-html");
        try
        {
            string emlDir = Path.Combine(tempDir, "eml");
            Directory.CreateDirectory(emlDir);
            string emlPath = Path.Combine(emlDir, "marker-dual.eml");

            string boundary = "bnd_marker_12345";
            string userPlain =
                "Header Line.\r\n" +
                "Evaluation Only. Created with Aspose.Email. for .NET. Copyright 2002-2024 Aspose Pty Ltd.\r\n" +
                "Footer Line.\r\n";
            string userHtml =
                "<html><body><p>Evaluation Only. Created with Aspose.Email.</p><p>Important User Content</p></body></html>";

            string mimeContent =
                $"From: user@test.local\r\n" +
                $"To: client@test.local\r\n" +
                $"Subject: Dual Marker Test\r\n" +
                $"Date: Tue, 16 Jan 2024 11:00:00 +0300\r\n" +
                $"MIME-Version: 1.0\r\n" +
                $"Content-Type: multipart/alternative; boundary=\"{boundary}\"\r\n\r\n" +
                $"--{boundary}\r\n" +
                $"Content-Type: text/plain; charset=utf-8\r\n\r\n" +
                $"{userPlain}" +
                $"--{boundary}\r\n" +
                $"Content-Type: text/html; charset=utf-8\r\n\r\n" +
                $"{userHtml}\r\n" +
                $"--{boundary}--\r\n";

            File.WriteAllText(emlPath, mimeContent);

            var inspector = new MimeSourceInspector();
            var manifest = inspector.BuildEmlDirectoryManifest(emlDir);

            string targetPst = Path.Combine(tempDir, "marker-dual.pst");
            var converter = new MimeToPstConverter();
            var report = converter.Convert(manifest, targetPst, "job-marker-dual", new ClientProjectContext());

            Assert.True(report.ConversionSuccess);

            using var pst = PersonalStorage.FromFile(targetPst);
            var folder = pst.RootFolder.GetSubFolder("eml");
            Assert.NotNull(folder);
            var msgInfo = folder.EnumerateMessages().First();
            using var mapi = pst.ExtractMessage(msgInfo);

            // Plain body contains user text verbatim
            Assert.Contains("Header Line.", mapi.Body);
            Assert.Contains("Evaluation Only. Created with Aspose.Email.", mapi.Body);
            Assert.Contains("Footer Line.", mapi.Body);

            // HTML body contains user text without corruption or stripping
            Assert.Contains("Important User Content", mapi.BodyHtml);
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
    public void HtmlOnlyMime_ReportsAddedRepresentation_AndNoManufacturedPlain()
    {
        string tempDir = CreateIsolatedTempDir("html-only");
        try
        {
            string emlDir = Path.Combine(tempDir, "eml");
            Directory.CreateDirectory(emlDir);
            string emlPath = Path.Combine(emlDir, "html-only.eml");

            string htmlContent = "<p>Yalnizca <b>HTML</b> iceren ileti govdesi.</p>";
            string mimeContent =
                "From: htmlonly@test.local\r\n" +
                "To: user@test.local\r\n" +
                "Subject: HTML Only Test\r\n" +
                "Date: Mon, 15 Jan 2024 10:00:00 +0300\r\n" +
                "MIME-Version: 1.0\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n\r\n" +
                htmlContent + "\r\n";

            File.WriteAllText(emlPath, mimeContent, Encoding.UTF8);

            var inspector = new MimeSourceInspector();
            var manifest = inspector.BuildEmlDirectoryManifest(emlDir);

            string targetPst = Path.Combine(tempDir, "html-only.pst");
            var converter = new MimeToPstConverter();
            var report = converter.Convert(manifest, targetPst, "job-html-only", new ClientProjectContext());

            Assert.True(report.ConversionSuccess);
            Assert.Equal(1, report.ItemsWritten);
            Assert.NotNull(report.MimeImport);
            Assert.Equal("DIFFERENCES", report.MimeImport.OverallQualification);

            var diff = Assert.Single(report.MimeImport.MessageDifferences);
            Assert.True(diff.HasHtml);
            Assert.False(diff.HasPlain);
            // Must not manufacture a fake empty source plain
            Assert.Null(diff.PlainBodySha256);
            Assert.NotEqual("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", diff.PlainBodySha256);
            // Any SDK-generated plain is reported as an added representation
            Assert.True(diff.AddedPlainRepresentation);
            Assert.Equal("text/plain", diff.AddedRepresentation);
            Assert.Equal(1, report.MimeImport.AddedPlainRepresentationCount);

            // Reopened PST check
            using var pst = PersonalStorage.FromFile(targetPst);
            var folder = pst.RootFolder.GetSubFolder("eml");
            Assert.NotNull(folder);
            var msgInfo = folder.EnumerateMessages().First();
            using var mapi = pst.ExtractMessage(msgInfo);
            Assert.Contains("HTML", mapi.BodyHtml);
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
    public void DatePolicy_InvalidAndZonelessDates_RetainedWithoutFilter_AndReopenedDateNotSilentlyToday()
    {
        string tempDir = CreateIsolatedTempDir("date-policy");
        try
        {
            string emlDir = Path.Combine(tempDir, "eml");
            Directory.CreateDirectory(emlDir);

            // 1. Missing Date header completely
            string noDateEml = Path.Combine(emlDir, "msg-no-date.eml");
            File.WriteAllText(noDateEml,
                "From: a@test.local\r\n" +
                "To: b@test.local\r\n" +
                "Subject: No Date Msg\r\n" +
                "Content-Type: text/plain; charset=utf-8\r\n\r\n" +
                "Iletide Date alani yok.\r\n", Encoding.UTF8);

            // 2. Invalid Date header
            string invalidDateEml = Path.Combine(emlDir, "msg-invalid-date.eml");
            File.WriteAllText(invalidDateEml,
                "From: a@test.local\r\n" +
                "To: b@test.local\r\n" +
                "Subject: Invalid Date Msg\r\n" +
                "Date: INVALID_DATE_STRING_NOT_RFC\r\n" +
                "Content-Type: text/plain; charset=utf-8\r\n\r\n" +
                "Iletide gecersiz Date alani var.\r\n", Encoding.UTF8);

            // 3. Zoneless Date header (no timezone offset)
            string zonelessDateEml = Path.Combine(emlDir, "msg-zoneless-date.eml");
            File.WriteAllText(zonelessDateEml,
                "From: a@test.local\r\n" +
                "To: b@test.local\r\n" +
                "Subject: Zoneless Date Msg\r\n" +
                "Date: 15 Jan 2024 10:00:00\r\n" +
                "Content-Type: text/plain; charset=utf-8\r\n\r\n" +
                "Iletide saat dilimsiz Date alani var.\r\n", Encoding.UTF8);

            var inspector = new MimeSourceInspector();
            var manifest = inspector.BuildEmlDirectoryManifest(emlDir);
            Assert.Equal(3, manifest.Entries.Count);

            // Evaluation with Date Filter (2024-01-01 to 2024-01-31):
            // missing and invalid dates must be excluded; zoneless date has no authoritative UTC instant and must also be excluded.
            var (preview, registered) = MimeSelectionEngine.EvaluateSelection(
                manifest,
                "msrc_date_test",
                null,
                startDate: "2024-01-01",
                endDate: "2024-01-31",
                preflight: new PreflightCheckResult { CanConvert = true },
                cancellationToken: CancellationToken.None);

            Assert.Equal(0, preview.SelectedCount);
            Assert.Equal(3, preview.ExcludedCount);
            Assert.Equal(3, preview.MissingDateExcludedCount);

            // Without Date Filter: all 3 items must be retained and converted
            string targetPst = Path.Combine(tempDir, "dates.pst");
            var converter = new MimeToPstConverter();
            var report = converter.Convert(manifest, targetPst, "job-dates", new ClientProjectContext());

            Assert.True(report.ConversionSuccess);
            Assert.Equal(3, report.ItemsWritten);

            // Reopened PST check: missing/invalid date must NOT silently be today's date!
            using var pst = PersonalStorage.FromFile(targetPst);
            var folder = pst.RootFolder.GetSubFolder("eml");
            Assert.NotNull(folder);
            var messages = folder.EnumerateMessages().ToList();
            Assert.Equal(3, messages.Count);

            DateTime todayUtc = DateTime.UtcNow;

            foreach (var msgInfo in messages)
            {
                using var mapi = pst.ExtractMessage(msgInfo);
                string subj = mapi.Subject ?? string.Empty;
                DateTime? extractedDate = OstSelectionEngine.ExtractMessageDate(mapi);

                if (subj.Contains("No Date Msg") || subj.Contains("Invalid Date Msg") || subj.Contains("Zoneless Date Msg"))
                {
                    // Must NOT have extracted a date (must be null)
                    Assert.Null(extractedDate);

                    // ClientSubmitTime must NOT silently be today's date
                    if (mapi.ClientSubmitTime != DateTime.MinValue && mapi.ClientSubmitTime.Year > 1601)
                    {
                        Assert.True(Math.Abs((mapi.ClientSubmitTime - todayUtc).TotalDays) > 1,
                            $"Reopened submit date must not silently be today: {mapi.ClientSubmitTime}");
                    }
                    if (mapi.DeliveryTime != DateTime.MinValue && mapi.DeliveryTime.Year > 1601)
                    {
                        Assert.True(Math.Abs((mapi.DeliveryTime - todayUtc).TotalDays) > 1,
                            $"Reopened delivery date must not silently be today: {mapi.DeliveryTime}");
                    }
                }

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
    public void MalformedBase64AndMissingMultipartClosingBoundary_BlocksWholeOperation()
    {
        string tempDir = CreateIsolatedTempDir("malformed-boundaries");
        try
        {
            var inspector = new MimeSourceInspector();
            var converter = new MimeToPstConverter();

            // Case A: Valid MIME header structure, but malformed base64 payload
            string base64Dir = Path.Combine(tempDir, "bad-base64");
            Directory.CreateDirectory(base64Dir);
            string badBase64Eml = Path.Combine(base64Dir, "bad-base64.eml");
            File.WriteAllText(badBase64Eml,
                "From: a@test.local\r\n" +
                "To: b@test.local\r\n" +
                "Subject: Bad Base64 Test\r\n" +
                "Date: Mon, 15 Jan 2024 10:00:00 +0300\r\n" +
                "MIME-Version: 1.0\r\n" +
                "Content-Type: text/plain; charset=utf-8\r\n" +
                "Content-Transfer-Encoding: base64\r\n\r\n" +
                "THIS_IS_NOT_VALID_BASE64_CONTENT!@#$%^&*()====\r\n", Encoding.UTF8);

            var base64Manifest = inspector.BuildEmlDirectoryManifest(base64Dir);
            string targetPstA = Path.Combine(tempDir, "bad-base64.pst");

            var exA = Assert.Throws<InvalidOperationException>(() =>
                converter.Convert(base64Manifest, targetPstA, "job-bad-base64", new ClientProjectContext()));
            Assert.Contains("[BÜTÜNLÜK ENGELİ]", exA.Message);
            Assert.False(File.Exists(targetPstA));

            // Case B: Valid multipart MIME structure, but missing/malformed closing boundary
            string boundaryDir = Path.Combine(tempDir, "missing-boundary");
            Directory.CreateDirectory(boundaryDir);
            string missingBoundaryEml = Path.Combine(boundaryDir, "missing-boundary.eml");
            const string boundary = "TEST_BOUNDARY_SENTINEL_XYZ";
            File.WriteAllText(missingBoundaryEml,
                "From: a@test.local\r\n" +
                "To: b@test.local\r\n" +
                "Subject: Missing Boundary Test\r\n" +
                "Date: Mon, 15 Jan 2024 10:00:00 +0300\r\n" +
                "MIME-Version: 1.0\r\n" +
                $"Content-Type: multipart/mixed; boundary=\"{boundary}\"\r\n\r\n" +
                $"--{boundary}\r\n" +
                "Content-Type: text/plain; charset=utf-8\r\n\r\n" +
                "Part 1 content.\r\n", Encoding.UTF8);

            var boundaryManifest = inspector.BuildEmlDirectoryManifest(boundaryDir);
            string targetPstB = Path.Combine(tempDir, "missing-boundary.pst");

            var exB = Assert.Throws<InvalidOperationException>(() =>
                converter.Convert(boundaryManifest, targetPstB, "job-missing-boundary", new ClientProjectContext()));
            Assert.Contains("[BÜTÜNLÜK ENGELİ]", exB.Message);
            Assert.False(File.Exists(targetPstB));
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
    public void MalformedItem_BlocksWholeSource_EvenWhenExcludedByFilter()
    {
        string tempDir = CreateIsolatedTempDir("excluded-malformed");
        try
        {
            string emlDir = Path.Combine(tempDir, "eml");
            Directory.CreateDirectory(emlDir);

            // Item 1: Valid message dated in 2024
            string validEml = Path.Combine(emlDir, "msg-2024-valid.eml");
            File.WriteAllText(validEml,
                "From: valid@test.local\r\n" +
                "To: receiver@test.local\r\n" +
                "Subject: Valid 2024 Message\r\n" +
                "Date: Fri, 10 May 2024 14:00:00 +0300\r\n" +
                "MIME-Version: 1.0\r\n" +
                "Content-Type: text/plain; charset=utf-8\r\n\r\n" +
                "Valid body content.\r\n", Encoding.UTF8);

            // Item 2: Malformed message dated in 2020 (malformed base64 / corrupt)
            string malformedEml = Path.Combine(emlDir, "msg-2020-malformed.eml");
            File.WriteAllText(malformedEml,
                "From: malformed@test.local\r\n" +
                "To: receiver@test.local\r\n" +
                "Subject: Malformed 2020 Message\r\n" +
                "Date: Wed, 15 Jan 2020 10:00:00 +0300\r\n" +
                "MIME-Version: 1.0\r\n" +
                "Content-Type: text/plain; charset=utf-8\r\n" +
                "Content-Transfer-Encoding: base64\r\n\r\n" +
                "CORRUPT_BASE64_NOT_VALID_!@#$%\r\n", Encoding.UTF8);

            var inspector = new MimeSourceInspector();
            var manifest = inspector.BuildEmlDirectoryManifest(emlDir);
            Assert.Equal(2, manifest.Entries.Count);

            // Selection filter: only year 2024 (2024-01-01 to 2024-12-31)
            // Under this filter, the 2020 malformed message is EXCLUDED.
            var (preview, registered) = MimeSelectionEngine.EvaluateSelection(
                manifest,
                "msrc_filter_test",
                null,
                startDate: "2024-01-01",
                endDate: "2024-12-31",
                preflight: new PreflightCheckResult { CanConvert = true },
                cancellationToken: CancellationToken.None);

            Assert.Equal(1, preview.SelectedCount);
            Assert.Equal(1, preview.ExcludedCount);

            // Attempting to convert must block the entire source despite the malformed item being excluded!
            string targetPst = Path.Combine(tempDir, "blocked.pst");
            var converter = new MimeToPstConverter();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                converter.Convert(manifest, targetPst, "job-excluded-malformed", new ClientProjectContext(), selection: registered));

            Assert.Contains("[BÜTÜNLÜK ENGELİ]", ex.Message);
            Assert.False(File.Exists(targetPst));
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
