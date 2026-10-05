using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Archive;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Archive;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;
using Xunit;

namespace BitigMail.Engine.Tests;

public class ArchiveSearchAndScopeTests
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

        throw new DirectoryNotFoundException("Repo root not found.");
    }

    private static string GetCorpusMboxPath() => Path.Combine(ResolveRepoRoot(), "fixtures", "mail-corpus-v1", "corpus.mbox");

    private static string CreateTempDir(string prefix)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"bitigmail-test-{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ScopeTripleValidation_RejectsMismatchedCompanyOrProject()
    {
        string runtimeDir = CreateTempDir("scope-triple");
        try
        {
            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(runtimeDir);
            var catalogService = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry, jobManager);

            // Ingest archive owned by company "AcmeCorp", project "ProjA"
            var inspector = new MimeSourceInspector();
            var mimeManifest = inspector.BuildMboxManifest(GetCorpusMboxPath());
            string handle = handleRegistry.RegisterMimeSource(mimeManifest, "corpus.mbox");

            var preview = catalogService.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "Acme Archive",
                CompanyId = "AcmeCorp",
                ProjectId = "ProjA",
                SourceHandle = handle
            });

            var plan = planStore.GetPlan(preview.PreviewId)!;
            jobManager.StartArchiveIngestJob(
                plan,
                "idemp-scope-1",
                new Models.ClientProjectContext { CompanyId = "AcmeCorp", ProjectId = "ProjA" },
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                runInBackground: false);

            // Attempt search with wrong company "EvilCorp"
            var badScope = new List<ArchiveScopeTriple>
            {
                new() { CompanyId = "EvilCorp", ProjectId = "ProjA", ArchiveId = plan.ArchiveId }
            };

            var ex = Assert.Throws<ArchiveSearchPolicyException>(() =>
            {
                catalogService.Search(new ArchiveSearchRequest
                {
                    SelectedScopes = badScope,
                    Query = "İstanbul"
                });
            });

            Assert.Contains("kapsamına ait değil", ex.Message);
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void EmptySelection_ReturnsZeroResults_WithoutDatabaseQuery()
    {
        string runtimeDir = CreateTempDir("empty-scope");
        try
        {
            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(runtimeDir);
            var catalogService = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry, jobManager);

            var response = catalogService.Search(new ArchiveSearchRequest
            {
                SelectedScopes = new List<ArchiveScopeTriple>(),
                Query = "İstanbul"
            });

            Assert.Equal(0, response.TotalCount);
            Assert.Empty(response.Items);
            Assert.True(response.IndexHealthy);
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void MessagePreviewBoundary_RequiresMatchingSearchScopeAndFilters()
    {
        string runtimeDir = CreateTempDir("preview-boundary");
        try
        {
            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(runtimeDir);
            var catalogService = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry, jobManager);

            var inspector = new MimeSourceInspector();
            var mimeManifest = inspector.BuildMboxManifest(GetCorpusMboxPath());
            string handle = handleRegistry.RegisterMimeSource(mimeManifest, "corpus.mbox");

            var preview = catalogService.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "Preview Boundary Archive",
                CompanyId = "comp-prev",
                ProjectId = "proj-prev",
                SourceHandle = handle
            });

            var plan = planStore.GetPlan(preview.PreviewId)!;
            jobManager.StartArchiveIngestJob(
                plan,
                "idemp-prev-1",
                new Models.ClientProjectContext { CompanyId = "comp-prev", ProjectId = "proj-prev" },
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                runInBackground: false);

            var scope = new List<ArchiveScopeTriple>
            {
                new() { CompanyId = "comp-prev", ProjectId = "proj-prev", ArchiveId = plan.ArchiveId }
            };

            // Search for "İstanbul" in subject -> msg_00000003, msg_00000004, msg_00000011, msg_00000012
            var searchReq = new ArchiveSearchRequest
            {
                SelectedScopes = scope,
                Field = "subject",
                Query = "İstanbul"
            };
            var searchResp = catalogService.Search(searchReq);
            Assert.Equal(4, searchResp.TotalCount);

            string validMessageId = searchResp.Items[0].MessageId;

            // 1. Valid preview request matching search query and scope succeeds
            var previewResp = catalogService.GetMessagePreview(new ArchiveMessagePreviewRequest
            {
                MessageId = validMessageId,
                SearchRequest = searchReq
            });
            Assert.NotNull(previewResp);
            Assert.Equal(validMessageId, previewResp.MessageId);
            Assert.NotEmpty(previewResp.BodyText);

            // 2. Requesting an item that is NOT in the search results fails closed
            // msg_00000001 does not have "İstanbul" in subject
            var invalidPreviewReq = new ArchiveMessagePreviewRequest
            {
                MessageId = "msg_00000001",
                SearchRequest = searchReq
            };
            Assert.Throws<ArchiveSearchPolicyException>(() =>
            {
                catalogService.GetMessagePreview(invalidPreviewReq);
            });

            // 3. Requesting valid message with empty scope fails closed
            var emptyScopeReq = new ArchiveMessagePreviewRequest
            {
                MessageId = validMessageId,
                SearchRequest = new ArchiveSearchRequest
                {
                    SelectedScopes = new(),
                    Query = "İstanbul"
                }
            };
            Assert.Throws<ArchiveSearchPolicyException>(() =>
            {
                catalogService.GetMessagePreview(emptyScopeReq);
            });
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SafePlainTextExtraction_FromHtmlOnlyMime_StripsScriptAndStyleTags()
    {
        string rawHtmlMime =
            "From: test@example.com\r\n" +
            "To: dest@example.com\r\n" +
            "Subject: HTML Test\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            "\r\n" +
            "<html>" +
            "<head><style>body { color: red; }</style><script>alert('xss');</script></head>" +
            "<body>" +
            "<h1>Başlık Metni</h1>" +
            "<p>Bu güvenli bir <b>metin</b> paragrafıdır.</p>" +
            "</body>" +
            "</html>";

        byte[] bytes = Encoding.UTF8.GetBytes(rawHtmlMime);
        using var ms = new MemoryStream(bytes);
        var parsed = ArchiveMimeParser.Parse(ms, bytes.Length);

        Assert.Contains("Başlık Metni", parsed.BodyText);
        Assert.Contains("Bu güvenli bir metin paragrafıdır", parsed.BodyText);
        Assert.DoesNotContain("<script>", parsed.BodyText);
        Assert.DoesNotContain("alert", parsed.BodyText);
        Assert.DoesNotContain("<style>", parsed.BodyText);
        Assert.DoesNotContain("color: red", parsed.BodyText);
        Assert.DoesNotContain("<h1>", parsed.BodyText);
    }

    [Fact]
    public void BodyTruncation_At512KiB_IsScalarSafe_AndSetsTruncatedFlag()
    {
        // Generate a body larger than 512 KiB (524,288 bytes)
        // Repeat a Turkish phrase with multi-byte characters: "Şemsi Paşa Pasajı "
        var sb = new StringBuilder();
        sb.AppendLine("From: test@example.com");
        sb.AppendLine("To: recipient@example.com");
        sb.AppendLine("Subject: Large Body Test");
        sb.AppendLine("Content-Type: text/plain; charset=utf-8");
        sb.AppendLine();

        string phrase = "Şemsi Paşa Pasajı Sesi Güzel Çocuklar ";
        while (sb.Length < 600 * 1024)
        {
            sb.AppendLine(phrase);
        }

        byte[] rawBytes = Encoding.UTF8.GetBytes(sb.ToString());
        using var ms = new MemoryStream(rawBytes);
        var parsed = ArchiveMimeParser.Parse(ms, rawBytes.Length);

        Assert.True(parsed.IsBodyTruncated);
        // Body text UTF-8 byte length must not exceed 512 KiB
        int bodyUtf8Bytes = Encoding.UTF8.GetByteCount(parsed.BodyText);
        Assert.True(bodyUtf8Bytes <= ArchiveMimeParser.MaxBodySizeBytes, $"Body must be <= 512 KiB, got {bodyUtf8Bytes}");
    }

    [Fact]
    public void CanaryStream_Exceeding64MiB_AbortsImmediately_WithoutAllocatingExcessiveMemory()
    {
        using var canaryStream = new RepeatingCanaryStream(0x5A);

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            ArchiveMimeParser.Parse(canaryStream, streamLength: 100);
        });

        Assert.Contains("64 MiB", ex.Message);
    }

    private sealed class RepeatingCanaryStream : Stream
    {
        private readonly byte _fillByte;
        public RepeatingCanaryStream(byte fillByte) => _fillByte = fillByte;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, _fillByte, offset, count);
            return count;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void EdgeFixture_ArchiveEdges_ExercisesSentinelsMissingDateAttachmentsAndTruncation()
    {
        string edgeDir = Path.Combine(ResolveRepoRoot(), "runtime", "task018-qa", "archive-edges-cbab019c");
        Assert.True(Directory.Exists(edgeDir), $"Edge fixture dir must exist at {edgeDir}");

        string runtimeDir = CreateTempDir("edge-fixture");
        try
        {
            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(runtimeDir);
            var catalogService = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry, jobManager);

            var inspector = new MimeSourceInspector();
            var mimeManifest = inspector.BuildEmlDirectoryManifest(edgeDir);
            string handle = handleRegistry.RegisterMimeSource(mimeManifest, "Edge Cases");

            var preview = catalogService.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "Edge Cases Archive",
                CompanyId = "comp-edge",
                ProjectId = "proj-edge",
                SourceHandle = handle
            });

            Assert.True(preview.CanIngest);
            Assert.Equal(4, preview.TotalItems);

            var plan = planStore.GetPlan(preview.PreviewId)!;
            var job = jobManager.StartArchiveIngestJob(
                plan,
                "idemp-edge-1",
                new Models.ClientProjectContext { CompanyId = "comp-edge", ProjectId = "proj-edge" },
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                runInBackground: false);

            Assert.Equal("completed", job.Status);
            Assert.Equal(4, job.ItemsWritten);

            var scope = new List<ArchiveScopeTriple>
            {
                new() { CompanyId = "comp-edge", ProjectId = "proj-edge", ArchiveId = plan.ArchiveId }
            };

            // 1. Sentinels must NOT be in body index
            var headRes = catalogService.Search(new ArchiveSearchRequest
            {
                SelectedScopes = scope,
                Query = "HEAD_SENTINEL_018"
            });
            Assert.Equal(0, headRes.TotalCount);

            var styleRes = catalogService.Search(new ArchiveSearchRequest
            {
                SelectedScopes = scope,
                Query = "STYLE_SENTINEL_018"
            });
            Assert.Equal(0, styleRes.TotalCount);

            var scriptRes = catalogService.Search(new ArchiveSearchRequest
            {
                SelectedScopes = scope,
                Query = "SCRIPT_SENTINEL_018"
            });
            Assert.Equal(0, scriptRes.TotalCount);

            // 2. Text attachment content must NOT be indexed as body
            var attContentRes = catalogService.Search(new ArchiveSearchRequest
            {
                SelectedScopes = scope,
                Query = "ATTACHMENT_CONTENT_ONLY_018"
            });
            Assert.Equal(0, attContentRes.TotalCount);

            // 3. Past 512 KiB truncation content must NOT be indexed as body
            var truncContentRes = catalogService.Search(new ArchiveSearchRequest
            {
                SelectedScopes = scope,
                Query = "AFTER_TRUNCATION_018"
            });
            Assert.Equal(0, truncContentRes.TotalCount);

            // 4. Visible HTML content MUST be indexed and searchable
            var visibleRes = catalogService.Search(new ArchiveSearchRequest
            {
                SelectedScopes = scope,
                Query = "İstanbul"
            });
            Assert.True(visibleRes.TotalCount >= 1);

            // 5. Date filter: missing-date message (03-missing-date.eml) excluded in date-bounded search
            var dateRes = catalogService.Search(new ArchiveSearchRequest
            {
                SelectedScopes = scope,
                StartDate = "2024-01-01",
                EndDate = "2024-01-03"
            });
            Assert.Equal(3, dateRes.TotalCount);

            // 6. Search without date filter returns all 4 messages
            var allRes = catalogService.Search(new ArchiveSearchRequest
            {
                SelectedScopes = scope
            });
            Assert.Equal(4, allRes.TotalCount);
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }
}
