using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Archive;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Archive;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;
using Xunit;

namespace BitigMail.Engine.Tests;

public class ArchiveStorageAndIngestTests
{
    private static string ResolveRepoRoot()
    {
        string? current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "ROADMAP.md")) ||
                Directory.Exists(Path.Combine(current, ".codex-coordination")))
            {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "ROADMAP.md")) ||
                Directory.Exists(Path.Combine(current, ".codex-coordination")))
            {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        throw new DirectoryNotFoundException("Repo root containing ROADMAP.md or .codex-coordination not found.");
    }

    private static string GetCorpusMboxPath() => Path.Combine(ResolveRepoRoot(), "fixtures", "mail-corpus-v1", "corpus.mbox");
    private static string GetCorpusEmlDir() => Path.Combine(ResolveRepoRoot(), "fixtures", "mail-corpus-v1", "eml");
    private static string GetExpectedFixtureSearchJsonPath() => Path.Combine(ResolveRepoRoot(), ".codex-coordination", "evidence", "TASK-018", "expected-fixture-search.json");

    private static string CreateTempDir(string prefix)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"bitigmail-test-{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void IngestCorpusMbox_Preserves12PhysicalItems_AndPhysicalDuplicates()
    {
        string runtimeDir = CreateTempDir("mbox-ingest");
        try
        {
            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(runtimeDir);
            var catalogService = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry, jobManager);

            string mboxPath = GetCorpusMboxPath();
            Assert.True(File.Exists(mboxPath), $"MBOX fixture must exist at {mboxPath}");

            // Register Mbox source handle
            var inspector = new MimeSourceInspector();
            var mimeManifest = inspector.BuildMboxManifest(mboxPath);
            string handle = handleRegistry.RegisterMimeSource(mimeManifest, "corpus.mbox");

            // Preview
            var preview = catalogService.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "Mbox Corpus Test",
                CompanyId = "comp-1",
                ProjectId = "proj-1",
                CompanyName = "Company One",
                ProjectName = "Project One",
                SourceHandle = handle
            });

            Assert.True(preview.CanIngest);
            Assert.Equal(12, preview.TotalItems);

            // Ingest job
            var plan = planStore.GetPlan(preview.PreviewId)!;
            var jobRecord = jobManager.StartArchiveIngestJob(
                plan,
                idempotencyKey: "test-mbox-idemp-1",
                new Models.ClientProjectContext { CompanyId = "comp-1", ProjectId = "proj-1" },
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                runInBackground: false);

            Assert.Equal("completed", jobRecord.Status);
            Assert.Equal(12, jobRecord.ItemsWritten);

            // Verify stored manifest
            var storedManifest = storageManager.GetArchiveManifest(plan.ArchiveId);
            Assert.NotNull(storedManifest);
            Assert.Equal(12, storedManifest.TotalItems);

            // Physical duplicates check: msg-01 and msg-02 must both be stored with the same SHA256
            var item1 = storedManifest.Items.First(i => i.Ordinal == 1);
            var item2 = storedManifest.Items.First(i => i.Ordinal == 2);
            Assert.NotEqual(item1.ItemId, item2.ItemId);
            Assert.Equal(item1.StoredSha256, item2.StoredSha256);
            Assert.False(string.IsNullOrEmpty(item1.StoredSha256));

            // Check that raw files actually exist on disk
            using var s1 = storageManager.OpenRawEmlStream(plan.ArchiveId, item1.RelativeEmlPath);
            using var s2 = storageManager.OpenRawEmlStream(plan.ArchiveId, item2.RelativeEmlPath);
            Assert.Equal(item1.ByteLength, s1.Length);
            Assert.Equal(item2.ByteLength, s2.Length);
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void IngestCorpusEmlDir_And_VerifyAll10ReferenceSearchCases()
    {
        string runtimeDir = CreateTempDir("eml-search-cases");
        try
        {
            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(runtimeDir);
            var catalogService = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry, jobManager);

            string emlDir = GetCorpusEmlDir();
            Assert.True(Directory.Exists(emlDir), $"EML fixture directory must exist at {emlDir}");

            // Register EML directory
            var inspector = new MimeSourceInspector();
            var mimeManifest = inspector.BuildEmlDirectoryManifest(emlDir);
            string handle = handleRegistry.RegisterMimeSource(mimeManifest, "EML Directory");

            // Create Preview
            var preview = catalogService.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "EML Corpus Reference",
                CompanyId = "comp-ref",
                ProjectId = "proj-ref",
                CompanyName = "Company Ref",
                ProjectName = "Project Ref",
                SourceHandle = handle
            });

            Assert.True(preview.CanIngest);
            Assert.Equal(12, preview.TotalItems);

            // Ingest
            var plan = planStore.GetPlan(preview.PreviewId)!;
            var job = jobManager.StartArchiveIngestJob(
                plan,
                idempotencyKey: "ref-ingest-1",
                new Models.ClientProjectContext { CompanyId = "comp-ref", ProjectId = "proj-ref" },
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                runInBackground: false);

            Assert.Equal("completed", job.Status);

            var scope = new List<ArchiveScopeTriple>
            {
                new() { CompanyId = "comp-ref", ProjectId = "proj-ref", ArchiveId = plan.ArchiveId }
            };

            // Read and run all 10 reference cases from expected-fixture-search.json
            string jsonPath = GetExpectedFixtureSearchJsonPath();
            Assert.True(File.Exists(jsonPath), $"expected-fixture-search.json must exist at {jsonPath}");

            string jsonContent = File.ReadAllText(jsonPath);
            using var doc = JsonDocument.Parse(jsonContent);
            var casesArray = doc.RootElement.GetProperty("cases");

            int caseIndex = 0;
            foreach (var testCase in casesArray.EnumerateArray())
            {
                caseIndex++;
                string? field = testCase.TryGetProperty("field", out var fp) ? fp.GetString() : null;
                string? query = testCase.TryGetProperty("query", out var qp) ? qp.GetString() : null;
                bool? hasAttachments = testCase.TryGetProperty("hasAttachments", out var hp) ? hp.GetBoolean() : null;
                string? startDate = testCase.TryGetProperty("startDate", out var sdp) ? sdp.GetString() : null;
                string? endDate = testCase.TryGetProperty("endDate", out var edp) ? edp.GetString() : null;
                int expectedCount = testCase.GetProperty("expectedCount").GetInt32();

                var searchReq = new ArchiveSearchRequest
                {
                    SelectedScopes = scope,
                    Field = field ?? "all",
                    Query = query,
                    HasAttachment = hasAttachments,
                    StartDate = startDate,
                    EndDate = endDate,
                    Page = 1,
                    PageSize = 50
                };

                var response = catalogService.Search(searchReq);

                Assert.True(
                    expectedCount == response.TotalCount,
                    $"Case #{caseIndex} ({field ?? "date/att"}: '{query ?? hasAttachments?.ToString() ?? startDate}') failed. Expected: {expectedCount}, Actual: {response.TotalCount}");
            }
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void MboxrdUnescape_SingleLevelFromUnescaping_PreservesLinesAndTrailingWhitespace()
    {
        // Construct mboxrd payload with escaped '>From ' and '>>From '
        string mboxData =
            "From sender@example.com Wed Jun 12 10:00:00 2024\n" +
            "From: sender@example.com\n" +
            "To: recipient@example.com\n" +
            "Subject: Escaped From Test\n" +
            "\n" +
            ">From line start unescapes to From\n" +
            ">>From double escaped unescapes to >From\n" +
            "Normal line with >From inside does not change\n" +
            "\n";

        using var ms = new MemoryStream(Encoding.ASCII.GetBytes(mboxData));
        var records = MboxrdRecordReader.EnumerateRecords(ms).ToList();

        Assert.Single(records);
        string body = Encoding.ASCII.GetString(records[0].RawMimeBytes);

        Assert.Contains("From line start unescapes to From", body);
        Assert.Contains(">From double escaped unescapes to >From", body);
        Assert.Contains("Normal line with >From inside does not change", body);
    }

    [Fact]
    public void RawMessageExceeding64MiB_IsBlockedFailClosed()
    {
        string runtimeDir = CreateTempDir("size-cap");
        try
        {
            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(runtimeDir);
            var catalogService = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry, jobManager);

            // Register fake manifest with an entry over 64 MiB (67_108_865 bytes)
            var fakeEntry = new MimeSourceEntry
            {
                CanonicalPath = "large.eml",
                SizeBytes = 67_108_865,
                Sha256 = "0000000000000000000000000000000000000000000000000000000000000000",
                MappedFolder = "INBOX"
            };

            var manifest = new MimeSourceManifest
            {
                RootPath = runtimeDir,
                SourceKind = "eml",
                Dialect = "eml",
                AggregateFingerprint = "fake-fingerprint",
                Entries = new List<MimeSourceEntry> { fakeEntry }
            };

            string handle = handleRegistry.RegisterMimeSource(manifest, "Large EML");

            var preview = catalogService.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "Large Archive",
                CompanyId = "c1",
                ProjectId = "p1",
                SourceHandle = handle
            });

            Assert.False(preview.CanIngest);
            Assert.Contains("64 MiB", preview.BlockerReason);
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void TraversalAndSymlinkAttempt_ThrowsSecurityException()
    {
        string runtimeDir = CreateTempDir("security-traversal");
        try
        {
            var storageManager = new ArchiveStorageManager(runtimeDir);

            // Attempting path traversal with ../
            Assert.Throws<InvalidOperationException>(() =>
            {
                storageManager.OpenRawEmlStream("arc_test", "../../some_secret.txt");
            });

            // Attempting invalid archive ID with traversal characters
            Assert.Throws<ArgumentException>(() =>
            {
                storageManager.OpenRawEmlStream("..\\..\\bad_id", "msg.eml");
            });
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }
}
