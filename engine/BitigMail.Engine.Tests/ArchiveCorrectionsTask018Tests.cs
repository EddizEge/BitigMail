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
using BitigMail.Engine.Bridge;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost;
using BitigMail.LocalHost.Archive;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BitigMail.Engine.Tests;

public class ArchiveCorrectionsTask018Tests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static string CreateTempDir(string prefix)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"bitigmail-task018-{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void RegisterCompletedBridgeExportJob(
        string runtimeDir,
        string jobId,
        string outputPath,
        string companyId,
        string projectId,
        string targetFormat = "mboxrd")
    {
        string jobsDir = Path.Combine(runtimeDir, "jobs");
        Directory.CreateDirectory(jobsDir);
        var record = new LocalJobRecord
        {
            JobId = jobId,
            JobKind = "bridge-export",
            Status = "completed",
            Stage = "Tamamlandı",
            Dialect = targetFormat,
            OutputPath = outputPath,
            ClientContext = new ClientProjectContext
            {
                CompanyId = companyId,
                CompanyName = companyId,
                ProjectId = projectId,
                ProjectName = projectId
            },
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAt = DateTimeOffset.UtcNow,
            ItemsRead = 3,
            ItemsWritten = 3,
            PercentComplete = 100
        };
        string json = JsonSerializer.Serialize(record, JsonOpts);
        File.WriteAllText(Path.Combine(jobsDir, $"{jobId}.json"), json);
    }

    private static string CreateEmlFile(string dir, string fileName, string subject, string body, DateTimeOffset? date = null)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        var sb = new StringBuilder();
        sb.AppendLine("From: sender@example.com");
        sb.AppendLine("To: recipient@example.com");
        sb.AppendLine($"Subject: {subject}");
        if (date.HasValue)
        {
            sb.AppendLine($"Date: {date.Value:R}");
        }
        sb.AppendLine("MIME-Version: 1.0");
        sb.AppendLine("Content-Type: text/plain; charset=utf-8");
        sb.AppendLine();
        sb.AppendLine(body);

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    /// <summary>
    /// Requirement 1:
    /// Search across two selected archives produces unique public archiveId:itemId identities.
    /// Preview of second archive opens exact second owner/item and cross-owner/scope fails.
    /// Reindex preserves public IDs.
    /// </summary>
    [Fact]
    public async Task Search_AcrossTwoSelectedArchives_ProducesUniquePublicIds_AndPreviewBindsExactOwner_PreservedAcrossReindex()
    {
        string runtimeDir = CreateTempDir("r1-multi-archive");
        try
        {
            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(runtimeDir);
            var catalogService = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry, jobManager);
            var inspector = new MimeSourceInspector();

            // Archive Alpha (Company comp-a, Project proj-a)
            string emlDirA = Path.Combine(runtimeDir, "sources", "alpha");
            CreateEmlFile(emlDirA, "01.eml", "Project Progress Alpha", "Body content Alpha 1");
            CreateEmlFile(emlDirA, "02.eml", "Internal Note Alpha", "Body content Alpha 2");

            var manifestA = inspector.BuildEmlDirectoryManifest(emlDirA);
            string handleA = handleRegistry.RegisterMimeSource(manifestA, "eml-dir");
            var previewA = catalogService.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "Archive Alpha",
                CompanyId = "comp-a",
                ProjectId = "proj-a",
                SourceHandle = handleA
            });
            var planA = planStore.GetPlan(previewA.PreviewId)!;
            jobManager.StartArchiveIngestJob(
                planA,
                "idemp-r1-alpha",
                new ClientProjectContext { CompanyId = "comp-a", ProjectId = "proj-a" },
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                runInBackground: false);

            // Archive Beta (Company comp-b, Project proj-b)
            string emlDirB = Path.Combine(runtimeDir, "sources", "beta");
            CreateEmlFile(emlDirB, "01.eml", "Project Progress Beta", "Body content Beta 1");
            CreateEmlFile(emlDirB, "02.eml", "Internal Note Beta", "Body content Beta 2");

            var manifestB = inspector.BuildEmlDirectoryManifest(emlDirB);
            string handleB = handleRegistry.RegisterMimeSource(manifestB, "eml-dir");
            var previewB = catalogService.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "Archive Beta",
                CompanyId = "comp-b",
                ProjectId = "proj-b",
                SourceHandle = handleB
            });
            var planB = planStore.GetPlan(previewB.PreviewId)!;
            jobManager.StartArchiveIngestJob(
                planB,
                "idemp-r1-beta",
                new ClientProjectContext { CompanyId = "comp-b", ProjectId = "proj-b" },
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                runInBackground: false);

            string arcAId = planA.ArchiveId;
            string arcBId = planB.ArchiveId;

            // Search across both selected scopes
            var multiScopeSearchReq = new ArchiveSearchRequest
            {
                SelectedScopes = new List<ArchiveScopeTriple>
                {
                    new() { CompanyId = "comp-a", ProjectId = "proj-a", ArchiveId = arcAId },
                    new() { CompanyId = "comp-b", ProjectId = "proj-b", ArchiveId = arcBId }
                },
                Query = "Project Progress"
            };

            var searchResp = catalogService.Search(multiScopeSearchReq);
            Assert.Equal(2, searchResp.TotalCount);

            var itemA = searchResp.Items.FirstOrDefault(i => i.ArchiveId == arcAId);
            var itemB = searchResp.Items.FirstOrDefault(i => i.ArchiveId == arcBId);

            Assert.NotNull(itemA);
            Assert.NotNull(itemB);

            // 1. Assert public stable globally unique MessageId wire format is exactly archiveId:itemId
            Assert.Equal($"{arcAId}:msg_00000001", itemA.MessageId);
            Assert.Equal($"{arcBId}:msg_00000001", itemB.MessageId);
            Assert.NotEqual(itemA.MessageId, itemB.MessageId);
            Assert.StartsWith(arcAId + ":", itemA.MessageId);
            Assert.StartsWith(arcBId + ":", itemB.MessageId);

            // 2. Preview of second archive message opens exact second owner/item
            var previewRespB = catalogService.GetMessagePreview(new ArchiveMessagePreviewRequest
            {
                MessageId = itemB.MessageId,
                SearchRequest = multiScopeSearchReq
            });

            Assert.NotNull(previewRespB);
            Assert.Equal(arcBId, previewRespB.ArchiveId);
            Assert.Equal(itemB.MessageId, previewRespB.MessageId);
            Assert.Equal("Project Progress Beta", previewRespB.Subject);
            Assert.Contains("Beta 1", previewRespB.BodyText);

            // 3. Cross-owner/scope fails: preview for itemB under search request with only Archive Alpha
            var singleScopeReqA = new ArchiveSearchRequest
            {
                SelectedScopes = new List<ArchiveScopeTriple>
                {
                    new() { CompanyId = "comp-a", ProjectId = "proj-a", ArchiveId = arcAId }
                },
                Query = "Project Progress"
            };

            Assert.Throws<ArchiveSearchPolicyException>(() =>
            {
                catalogService.GetMessagePreview(new ArchiveMessagePreviewRequest
                {
                    MessageId = itemB.MessageId,
                    SearchRequest = singleScopeReqA
                });
            });

            // Endpoint returns 404 Not Found for cross-scope preview attempt
            var crossEndpointRes = LocalEngineApiEndpointsArchive.HandleMessagePreview(new ArchiveMessagePreviewRequest
            {
                MessageId = itemB.MessageId,
                SearchRequest = singleScopeReqA
            }, catalogService);
            var crossStatus = Assert.IsAssignableFrom<IStatusCodeHttpResult>(crossEndpointRes);
            Assert.Equal(404, crossStatus.StatusCode);

            // 4. Reindex preserves public MessageId format and identity
            await searchIndex.RebuildDatabaseAsync(storageManager, CancellationToken.None);

            var searchAfterReindex = catalogService.Search(multiScopeSearchReq);
            Assert.Equal(2, searchAfterReindex.TotalCount);

            var itemAReindexed = searchAfterReindex.Items.First(i => i.ArchiveId == arcAId);
            var itemBReindexed = searchAfterReindex.Items.First(i => i.ArchiveId == arcBId);

            Assert.Equal(itemA.MessageId, itemAReindexed.MessageId);
            Assert.Equal(itemB.MessageId, itemBReindexed.MessageId);

            var previewAfterReindex = catalogService.GetMessagePreview(new ArchiveMessagePreviewRequest
            {
                MessageId = itemB.MessageId,
                SearchRequest = multiScopeSearchReq
            });
            Assert.Equal(arcBId, previewAfterReindex.ArchiveId);
            Assert.Equal(itemB.MessageId, previewAfterReindex.MessageId);
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Requirement 2:
    /// Omitted field defaults All, but explicitly null/unknown/invalid wire field maps controlled 400.
    /// Tests closed DTO mapping and endpoint behavior.
    /// </summary>
    [Fact]
    public void SearchFieldMapping_ClosedValidation_OmittedDefaultsAll_ExplicitNullOrUnknownReturnsControlled400()
    {
        string runtimeDir = CreateTempDir("r2-field-mapping");
        try
        {
            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(runtimeDir);
            var catalogService = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry, jobManager);

            // 1. Omitted field in JSON -> defaults to ArchiveSearchField.All
            string jsonOmitted = """{"query":"test","selectedScopes":[]}""";
            var reqOmitted = JsonSerializer.Deserialize<ArchiveSearchRequest>(jsonOmitted, JsonOpts)!;
            Assert.Equal(ArchiveSearchField.All, reqOmitted.ResolveField());

            // 2. Explicit null field in JSON -> throws ArgumentException
            string jsonNull = """{"query":"test","field":null,"selectedScopes":[]}""";
            var reqNull = JsonSerializer.Deserialize<ArchiveSearchRequest>(jsonNull, JsonOpts)!;
            Assert.Null(reqNull.Field);
            var exNull = Assert.Throws<ArgumentException>(() => reqNull.ResolveField());
            Assert.Contains("null", exNull.Message);

            // 3. Explicit unknown field in JSON -> throws ArgumentException
            string jsonUnknown = """{"query":"test","field":"unknown","selectedScopes":[]}""";
            var reqUnknown = JsonSerializer.Deserialize<ArchiveSearchRequest>(jsonUnknown, JsonOpts)!;
            var exUnknown = Assert.Throws<ArgumentException>(() => reqUnknown.ResolveField());
            Assert.Contains("unknown", exUnknown.Message);

            // 4. Explicit invalid field in JSON -> throws ArgumentException
            string jsonInvalid = """{"query":"test","field":"sql_injection;--","selectedScopes":[]}""";
            var reqInvalid = JsonSerializer.Deserialize<ArchiveSearchRequest>(jsonInvalid, JsonOpts)!;
            var exInvalid = Assert.Throws<ArgumentException>(() => reqInvalid.ResolveField());
            Assert.Contains("Geçersiz", exInvalid.Message);

            // 5. Valid field variants accepted cleanly
            string[] validFields = ["all", "subject", "sender", "recipient", "recipients", "body", "attachment", "attachments", "attachmentname"];
            foreach (var f in validFields)
            {
                var reqValid = JsonSerializer.Deserialize<ArchiveSearchRequest>($$"""{"query":"test","field":"{{f}}","selectedScopes":[]}""", JsonOpts)!;
                Assert.NotNull(reqValid.ResolveField().ToString());
            }

            // 6. Endpoint testing: HandleSearch maps invalid fields to HTTP 400 Bad Request
            var resNull = LocalEngineApiEndpointsArchive.HandleSearch(reqNull, catalogService);
            Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(resNull).StatusCode);

            var resUnknown = LocalEngineApiEndpointsArchive.HandleSearch(reqUnknown, catalogService);
            Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(resUnknown).StatusCode);

            var resInvalid = LocalEngineApiEndpointsArchive.HandleSearch(reqInvalid, catalogService);
            Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(resInvalid).StatusCode);

            // Omitted field succeeds through endpoint (returns 200 OK with 0 items for empty scope)
            var resOmitted = LocalEngineApiEndpointsArchive.HandleSearch(reqOmitted, catalogService);
            Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(resOmitted).StatusCode);

            // Message preview endpoint also rejects invalid search request field with HTTP 400
            var prevReqUnknown = new ArchiveMessagePreviewRequest
            {
                MessageId = "arc_test:msg_00000001",
                SearchRequest = reqUnknown
            };
            var prevResUnknown = LocalEngineApiEndpointsArchive.HandleMessagePreview(prevReqUnknown, catalogService);
            Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(prevResUnknown).StatusCode);
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Requirement 3:
    /// Mutating managed raw EML after index/manifest triggers controlled 409 Conflict integrity error without returning body.
    /// Restores fixture bytes in finally.
    /// </summary>
    [Fact]
    public async Task MessagePreview_ManagedRawMutation_ReturnsControlled409Conflict_WithoutReturningBody_RestoresFixture()
    {
        string runtimeDir = CreateTempDir("r3-mutation-guard");
        try
        {
            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(runtimeDir);
            var catalogService = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry, jobManager);
            var inspector = new MimeSourceInspector();

            string emlDir = Path.Combine(runtimeDir, "sources", "orig");
            CreateEmlFile(emlDir, "msg1.eml", "Integrity Test Subject", "Original untouched body content.");

            var manifestMime = inspector.BuildEmlDirectoryManifest(emlDir);
            string handle = handleRegistry.RegisterMimeSource(manifestMime, "eml-dir");
            var preview = catalogService.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "Integrity Archive",
                CompanyId = "comp-integrity",
                ProjectId = "proj-integrity",
                SourceHandle = handle
            });

            var plan = planStore.GetPlan(preview.PreviewId)!;
            jobManager.StartArchiveIngestJob(
                plan,
                "idemp-r3",
                new ClientProjectContext { CompanyId = "comp-integrity", ProjectId = "proj-integrity" },
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                runInBackground: false);

            var searchReq = new ArchiveSearchRequest
            {
                SelectedScopes = new List<ArchiveScopeTriple>
                {
                    new() { CompanyId = "comp-integrity", ProjectId = "proj-integrity", ArchiveId = plan.ArchiveId }
                },
                Query = "Integrity"
            };

            var searchResp = catalogService.Search(searchReq);
            Assert.Equal(1, searchResp.TotalCount);
            string messageId = searchResp.Items[0].MessageId;

            var previewReq = new ArchiveMessagePreviewRequest
            {
                MessageId = messageId,
                SearchRequest = searchReq
            };

            // Before mutation: preview succeeds
            var validPreview = catalogService.GetMessagePreview(previewReq);
            Assert.NotNull(validPreview);
            Assert.Contains("Original untouched body content.", validPreview.BodyText);

            // Locate managed raw EML file on disk
            var managedManifest = storageManager.GetArchiveManifest(plan.ArchiveId)!;
            var manifestItem = managedManifest.Items[0];
            string managedEmlPath = Path.Combine(runtimeDir, "archives", plan.ArchiveId, manifestItem.RelativeEmlPath);
            Assert.True(File.Exists(managedEmlPath));

            byte[] originalBytes = await File.ReadAllBytesAsync(managedEmlPath);

            try
            {
                // A. Test mutation by appending bytes (length mismatch + SHA mismatch)
                await File.AppendAllTextAsync(managedEmlPath, "\r\nTAMPERED_BYTE_MUTATION_TASK018");

                // 1. Service layer must throw ArchiveIntegrityException
                var exLength = Assert.Throws<ArchiveIntegrityException>(() => catalogService.GetMessagePreview(previewReq));
                Assert.Contains("bayt uzunluğu", exLength.Message);

                // 2. Endpoint layer must return HTTP 409 Conflict with NO message body
                var endpointResLength = LocalEngineApiEndpointsArchive.HandleMessagePreview(previewReq, catalogService);
                var statusResLength = Assert.IsAssignableFrom<IStatusCodeHttpResult>(endpointResLength);
                Assert.Equal(409, statusResLength.StatusCode);

                string jsonLength = JsonSerializer.Serialize(endpointResLength);
                Assert.DoesNotContain("BodyText", jsonLength, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("TAMPERED_BYTE_MUTATION_TASK018", jsonLength, StringComparison.OrdinalIgnoreCase);

                // B. Test same-length byte mutation (exact length preserved, SHA-256 corrupted)
                byte[] sameLengthTampered = (byte[])originalBytes.Clone();
                sameLengthTampered[sameLengthTampered.Length / 2] ^= 0xAA;
                await File.WriteAllBytesAsync(managedEmlPath, sameLengthTampered);

                // 1. Service layer must throw ArchiveIntegrityException on SHA-256 mismatch
                var exSha = Assert.Throws<ArchiveIntegrityException>(() => catalogService.GetMessagePreview(previewReq));
                Assert.Contains("SHA-256 hash uyuşmazlığı", exSha.Message);

                // 2. Endpoint layer must return HTTP 409 Conflict with NO message body
                var endpointResSha = LocalEngineApiEndpointsArchive.HandleMessagePreview(previewReq, catalogService);
                var statusResSha = Assert.IsAssignableFrom<IStatusCodeHttpResult>(endpointResSha);
                Assert.Equal(409, statusResSha.StatusCode);

                string jsonSha = JsonSerializer.Serialize(endpointResSha);
                Assert.DoesNotContain("BodyText", jsonSha, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                // Restore fixture bytes
                await File.WriteAllBytesAsync(managedEmlPath, originalBytes);
            }

            // Confirm that restoring fixture allows preview to succeed again
            var restoredPreview = catalogService.GetMessagePreview(previewReq);
            Assert.NotNull(restoredPreview);
            Assert.Equal(manifestItem.StoredSha256, restoredPreview.Sha256);
            Assert.Contains("Original untouched body content.", restoredPreview.BodyText);
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Requirement 4:
    /// Completed bridge mboxrd export with repeated relativeOutputPath ingests distinct per-record
    /// physical items and validates each record hash, not the whole container hash.
    /// </summary>
    [Fact]
    public void CompletedBridgeExport_MboxrdWithRepeatedContainerPath_IngestsDistinctRecords_ValidatingPerRecordHashes()
    {
        string runtimeDir = CreateTempDir("r4-mboxrd-export");
        try
        {
            string exportDir = Path.Combine(runtimeDir, "bridge-export-output");
            Directory.CreateDirectory(exportDir);

            // Construct an mboxrd container file containing 3 distinct messages in INBOX.mbox
            string mboxContainerPath = Path.Combine(exportDir, "INBOX.mbox");
            var recordsToEmit = new[]
            {
                ("Invoice 101", "Body 1 for Invoice 101"),
                ("Invoice 102", "Body 2 for Invoice 102"),
                ("Invoice 103", "Body 3 for Invoice 103")
            };

            var mboxSb = new StringBuilder();
            foreach (var (subj, body) in recordsToEmit)
            {
                mboxSb.AppendLine("From sender@example.com Mon Sep 14 00:00:00 2026");
                mboxSb.AppendLine("From: sender@example.com");
                mboxSb.AppendLine("To: recipient@example.com");
                mboxSb.AppendLine($"Subject: {subj}");
                mboxSb.AppendLine("MIME-Version: 1.0");
                mboxSb.AppendLine("Content-Type: text/plain; charset=utf-8");
                mboxSb.AppendLine();
                mboxSb.AppendLine(body);
                mboxSb.AppendLine();
            }
            File.WriteAllText(mboxContainerPath, mboxSb.ToString(), new UTF8Encoding(false));

            // Parse container once to retrieve exact per-record MIME bytes and per-record SHA-256
            List<MboxrdRecord> parsedRecords;
            using (var fs = new FileStream(mboxContainerPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                parsedRecords = MboxrdRecordReader.EnumerateRecords(fs, ArchiveMimeParser.MaxRawMessageSizeBytes).ToList();
            }
            Assert.Equal(3, parsedRecords.Count);

            var recordHashes = parsedRecords.Select(r =>
                Convert.ToHexString(SHA256.HashData(r.RawMimeBytes)).ToLowerInvariant()).ToList();

            // Note: Whole container file hash is completely distinct from each record's hash
            string containerFileSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(mboxContainerPath))).ToLowerInvariant();
            foreach (var rh in recordHashes)
            {
                Assert.NotEqual(containerFileSha, rh);
            }

            // Create bridge export manifest.json with repeated relativeOutputPath = "INBOX.mbox"
            var manifestItems = new List<BridgeExportManifestItem>();
            for (int i = 0; i < parsedRecords.Count; i++)
            {
                manifestItems.Add(new BridgeExportManifestItem
                {
                    ItemId = $"msg_{i + 1:D8}",
                    OriginalFolder = "INBOX",
                    SourceUid = (uint)(100 + i),
                    SourceUidValidity = 1,
                    SourceSha256 = recordHashes[i],
                    OriginalLength = parsedRecords[i].RawMimeBytes.Length,
                    StoredSha256 = recordHashes[i], // Exact per-record SHA-256
                    RelativeOutputPath = "INBOX.mbox", // Legitimate repeated container path
                    InternalDateUtc = DateTimeOffset.UtcNow.AddMinutes(-i * 10)
                });
            }

            var exportManifest = new BridgeExportManifest
            {
                JobId = "job-bridge-export-018",
                PlanId = "plan-bridge-018",
                CompanyId = "comp-bridge",
                ProjectId = "proj-bridge",
                SourceAccountId = "acc-001",
                SourceAccountVersion = 1,
                TargetFormat = "mboxrd",
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Folders = new List<BridgeExportManifestFolder>
                {
                    new()
                    {
                        FolderKey = "INBOX",
                        OriginalFolder = "INBOX",
                        OutputTarget = "INBOX.mbox",
                        MessageCount = 3
                    }
                },
                Items = manifestItems
            };

            string manifestJson = JsonSerializer.Serialize(exportManifest, JsonOpts);
            File.WriteAllText(Path.Combine(exportDir, "manifest.json"), manifestJson);

            // Register completed export job in JobManager
            RegisterCompletedBridgeExportJob(runtimeDir, "job-bridge-export-018", exportDir, "comp-bridge", "proj-bridge", "mboxrd");

            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(runtimeDir);
            var catalogService = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry, jobManager);

            // Create ingest preview from completed bridge job
            var previewResp = catalogService.CreatePreview(new ArchiveIngestPreviewRequest
            {
                SourceJobId = "job-bridge-export-018",
                ArchiveName = "Bridge Mboxrd Archive",
                CompanyId = "comp-bridge",
                ProjectId = "proj-bridge"
            });

            Assert.True(previewResp.CanIngest);
            Assert.Equal(1, previewResp.TotalFiles); // 1 distinct container file
            Assert.Equal(3, previewResp.TotalItems); // 3 physical records

            var plan = planStore.GetPlan(previewResp.PreviewId)!;
            var jobRecord = jobManager.StartArchiveIngestJob(
                plan,
                "idemp-bridge-ingest",
                new ClientProjectContext { CompanyId = "comp-bridge", ProjectId = "proj-bridge" },
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                runInBackground: false);

            Assert.Equal("completed", jobRecord.Status);
            Assert.Equal(3, jobRecord.ItemsWritten);

            // Managed manifest must contain 3 distinct physical EML items
            var managedManifest = storageManager.GetArchiveManifest(plan.ArchiveId)!;
            Assert.Equal(3, managedManifest.TotalItems);

            var relativePaths = managedManifest.Items.Select(i => i.RelativeEmlPath).Distinct().ToList();
            Assert.Equal(3, relativePaths.Count);

            // Check that each stored physical item has the expected per-record hash
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(recordHashes[i], managedManifest.Items[i].StoredSha256);
                string itemDiskPath = Path.Combine(runtimeDir, "archives", plan.ArchiveId, managedManifest.Items[i].RelativeEmlPath);
                Assert.True(File.Exists(itemDiskPath));
                string actualStoredSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(itemDiskPath))).ToLowerInvariant();
                Assert.Equal(recordHashes[i], actualStoredSha);
            }

            // Search finds all 3 records individually
            var searchResp = catalogService.Search(new ArchiveSearchRequest
            {
                SelectedScopes = new List<ArchiveScopeTriple>
                {
                    new() { CompanyId = "comp-bridge", ProjectId = "proj-bridge", ArchiveId = plan.ArchiveId }
                },
                Query = "Invoice"
            });
            Assert.Equal(3, searchResp.TotalCount);
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Requirement 5:
    /// SourceInternalDateUtc remains separate from MIME Date from bridge sidecar through
    /// managed manifest, SQLite index, search results, preview response, and stable after reindex.
    /// </summary>
    [Fact]
    public async Task SourceInternalDateUtc_PreservedSeparatelyFromMimeDate_FromSidecarToManifestIndexSearchPreviewAndReindex()
    {
        string runtimeDir = CreateTempDir("r5-internal-date");
        try
        {
            string exportDir = Path.Combine(runtimeDir, "bridge-date-export");
            Directory.CreateDirectory(exportDir);

            // EML has MIME Date: 2020-05-10T12:00:00Z
            var mimeDate = new DateTimeOffset(2020, 5, 10, 12, 0, 0, TimeSpan.Zero);
            // Bridge sidecar has InternalDateUtc: 2026-09-14T03:15:00Z
            var internalDate = new DateTimeOffset(2026, 9, 14, 3, 15, 0, TimeSpan.Zero);

            string emlPath = Path.Combine(exportDir, "msg01.eml");
            var sb = new StringBuilder();
            sb.AppendLine("From: sender@example.com");
            sb.AppendLine("To: recipient@example.com");
            sb.AppendLine("Subject: Dual Date Subject");
            sb.AppendLine($"Date: {mimeDate:R}");
            sb.AppendLine("MIME-Version: 1.0");
            sb.AppendLine("Content-Type: text/plain; charset=utf-8");
            sb.AppendLine();
            sb.AppendLine("Message body testing separate internal date preservation.");
            File.WriteAllText(emlPath, sb.ToString(), new UTF8Encoding(false));

            byte[] emlBytes = File.ReadAllBytes(emlPath);
            string emlSha256 = Convert.ToHexString(SHA256.HashData(emlBytes)).ToLowerInvariant();

            var exportManifest = new BridgeExportManifest
            {
                JobId = "job-bridge-date-018",
                PlanId = "plan-bridge-date-018",
                CompanyId = "comp-date",
                ProjectId = "proj-date",
                SourceAccountId = "acc-date",
                SourceAccountVersion = 1,
                TargetFormat = "eml-tree",
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Folders = new List<BridgeExportManifestFolder>
                {
                    new()
                    {
                        FolderKey = "INBOX",
                        OriginalFolder = "INBOX",
                        OutputTarget = "INBOX",
                        MessageCount = 1
                    }
                },
                Items = new List<BridgeExportManifestItem>
                {
                    new()
                    {
                        ItemId = "msg_00000001",
                        OriginalFolder = "INBOX",
                        SourceUid = 1001,
                        SourceUidValidity = 1,
                        SourceSha256 = emlSha256,
                        OriginalLength = emlBytes.Length,
                        StoredSha256 = emlSha256,
                        RelativeOutputPath = "msg01.eml",
                        InternalDateUtc = internalDate // Distinct from MIME Date
                    }
                }
            };

            File.WriteAllText(Path.Combine(exportDir, "manifest.json"), JsonSerializer.Serialize(exportManifest, JsonOpts));
            RegisterCompletedBridgeExportJob(runtimeDir, "job-bridge-date-018", exportDir, "comp-date", "proj-date", "eml-tree");

            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(runtimeDir);
            var catalogService = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry, jobManager);

            // 1. Ingest Preview Plan carries SourceInternalDateUtc
            var previewResp = catalogService.CreatePreview(new ArchiveIngestPreviewRequest
            {
                SourceJobId = "job-bridge-date-018",
                ArchiveName = "Dual Date Archive",
                CompanyId = "comp-date",
                ProjectId = "proj-date"
            });

            var plan = planStore.GetPlan(previewResp.PreviewId)!;
            Assert.Equal(internalDate, plan.Items[0].SourceInternalDateUtc);

            var job = jobManager.StartArchiveIngestJob(
                plan,
                "idemp-date-1",
                new ClientProjectContext { CompanyId = "comp-date", ProjectId = "proj-date" },
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                runInBackground: false);

            Assert.Equal("completed", job.Status);

            // 2. Managed Manifest carries SourceInternalDateUtc
            var managedManifest = storageManager.GetArchiveManifest(plan.ArchiveId)!;
            Assert.Equal(internalDate, managedManifest.Items[0].SourceInternalDateUtc);

            // 3. Search Results carry both OriginalMimeDateUtc and SourceInternalDateUtc separately
            var searchReq = new ArchiveSearchRequest
            {
                SelectedScopes = new List<ArchiveScopeTriple>
                {
                    new() { CompanyId = "comp-date", ProjectId = "proj-date", ArchiveId = plan.ArchiveId }
                },
                Query = "Dual Date"
            };

            var searchResp = catalogService.Search(searchReq);
            Assert.Equal(1, searchResp.TotalCount);

            var searchItem = searchResp.Items[0];
            Assert.Equal(mimeDate, searchItem.OriginalMimeDateUtc);
            Assert.Equal(internalDate, searchItem.SourceInternalDateUtc);
            Assert.NotEqual(searchItem.OriginalMimeDateUtc, searchItem.SourceInternalDateUtc);

            // 4. Message Preview Response carries both DateUtc and SourceInternalDateUtc separately
            var previewResult = catalogService.GetMessagePreview(new ArchiveMessagePreviewRequest
            {
                MessageId = searchItem.MessageId,
                SearchRequest = searchReq
            });

            Assert.Equal(mimeDate, previewResult.DateUtc);
            Assert.Equal(internalDate, previewResult.SourceInternalDateUtc);
            Assert.NotEqual(previewResult.DateUtc, previewResult.SourceInternalDateUtc);

            // 5. Stability across Reindex
            await searchIndex.RebuildDatabaseAsync(storageManager, CancellationToken.None);

            var searchAfterReindex = catalogService.Search(searchReq);
            Assert.Equal(1, searchAfterReindex.TotalCount);

            var itemAfterReindex = searchAfterReindex.Items[0];
            Assert.Equal(searchItem.MessageId, itemAfterReindex.MessageId); // Public ID preserved!
            Assert.Equal(mimeDate, itemAfterReindex.OriginalMimeDateUtc);
            Assert.Equal(internalDate, itemAfterReindex.SourceInternalDateUtc);

            var previewAfterReindex = catalogService.GetMessagePreview(new ArchiveMessagePreviewRequest
            {
                MessageId = searchItem.MessageId,
                SearchRequest = searchReq
            });
            Assert.Equal(searchItem.MessageId, previewAfterReindex.MessageId);
            Assert.Equal(internalDate, previewAfterReindex.SourceInternalDateUtc);
            Assert.Equal(mimeDate, previewAfterReindex.DateUtc);
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Requirement 6:
    /// Fresh empty registry restart resume:
    /// Partial EML ingest interrupted after 2/4 files.
    /// New process restart with fresh empty FileHandleRegistry safely rehydrates source from durable plan,
    /// reuses verified staged files without duplicates/overwrite, copies remaining items, and completes.
    /// </summary>
    [Fact]
    public async Task Resume_AfterProcessRestart_WithFreshEmptyRegistry_Succeeds_AndReusesPartialStaging_Eml()
    {
        string runtimeDir = CreateTempDir("r6-eml-resume");
        try
        {
            string emlDir = Path.Combine(runtimeDir, "sources", "eml");
            string f1 = CreateEmlFile(emlDir, "01.eml", "Subject 1", "Body 1");
            string f2 = CreateEmlFile(emlDir, "02.eml", "Subject 2", "Body 2");
            string f3 = CreateEmlFile(emlDir, "03.eml", "Subject 3", "Body 3");
            string f4 = CreateEmlFile(emlDir, "04.eml", "Subject 4", "Body 4");

            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry1 = new FileHandleRegistry();
            var jobManager1 = new JobManager(runtimeDir);
            var catalogService1 = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry1, jobManager1);

            var inspector = new MimeSourceInspector();
            var manifest = inspector.BuildEmlDirectoryManifest(emlDir);
            string handle = handleRegistry1.RegisterMimeSource(manifest, "eml-dir");

            var preview = catalogService1.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "Restart EML Archive",
                CompanyId = "comp-crash",
                ProjectId = "proj-crash",
                SourceHandle = handle
            });

            var plan = planStore.GetPlan(preview.PreviewId)!;
            string stagingDir = storageManager.EnsureStagingDirectory(plan.ArchiveId);

            // Pre-stage first 2 files into staging (simulating mid-copy crash after 2 items)
            using (var fs1 = new FileStream(f1, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await storageManager.StoreMimeItemAsync(
                    stagingDir, 1, plan.Items[0].ItemId, plan.Items[0].MappedFolder,
                    fs1, plan.Items[0].SourceSha256, null, CancellationToken.None);
            }
            using (var fs2 = new FileStream(f2, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await storageManager.StoreMimeItemAsync(
                    stagingDir, 2, plan.Items[1].ItemId, plan.Items[1].MappedFolder,
                    fs2, plan.Items[1].SourceSha256, null, CancellationToken.None);
            }

            string staged1 = Path.Combine(stagingDir, "messages", "000001.eml");
            string staged2 = Path.Combine(stagingDir, "messages", "000002.eml");
            Assert.True(File.Exists(staged1));
            Assert.True(File.Exists(staged2));
            var staged1BytesBefore = File.ReadAllBytes(staged1);
            var staged2BytesBefore = File.ReadAllBytes(staged2);

            // Register interrupted job record
            string jobId = "job-crash-eml-01";
            var record = new LocalJobRecord
            {
                JobId = jobId,
                JobKind = "archive-ingest",
                PlanId = plan.PlanId,
                ArchiveId = plan.ArchiveId,
                ArchiveName = plan.ArchiveName,
                Status = "interrupted",
                Stage = "Kesintiye Uğradı",
                ItemsRead = 2,
                ItemsWritten = 2,
                TotalItems = 4,
                TotalSourceMessages = 4,
                SelectedMessagesCount = 4,
                ClientContext = new ClientProjectContext
                {
                    CompanyId = "comp-crash",
                    CompanyName = "Crash Co",
                    ProjectId = "proj-crash",
                    ProjectName = "Crash Proj"
                },
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            };
            File.WriteAllText(Path.Combine(runtimeDir, "jobs", $"{jobId}.json"), JsonSerializer.Serialize(record, JsonOpts));

            // SIMULATE RESTART: Fresh process with empty FileHandleRegistry and brand new JobManager
            var handleRegistry2 = new FileHandleRegistry();
            Assert.Null(handleRegistry2.GetMimeSourceEntry(handle)); // Empty registry!

            var jobManager2 = new JobManager(runtimeDir);
            var clientContext = new ClientProjectContext { CompanyId = "comp-crash", ProjectId = "proj-crash" };

            var resumedJob = jobManager2.ResumeArchiveIngestJob(
                jobId,
                clientContext,
                storageManager,
                searchIndex,
                planStore,
                handleRegistry2,
                runInBackground: false);

            Assert.Equal("completed", resumedJob.Status);
            Assert.Equal(4, resumedJob.ItemsWritten);
            Assert.Equal(4, resumedJob.ItemsRead);

            // Verify that first 2 staged files were reused and not corrupted/tampered
            var managedManifest = storageManager.GetArchiveManifest(plan.ArchiveId);
            Assert.NotNull(managedManifest);
            Assert.Equal(4, managedManifest.TotalItems);

            string managedItem1 = Path.Combine(runtimeDir, "archives", plan.ArchiveId, "messages", "000001.eml");
            string managedItem2 = Path.Combine(runtimeDir, "archives", plan.ArchiveId, "messages", "000002.eml");
            Assert.Equal(staged1BytesBefore, File.ReadAllBytes(managedItem1));
            Assert.Equal(staged2BytesBefore, File.ReadAllBytes(managedItem2));

            // Search finds all 4 messages
            var searchResp = catalogService1.Search(new ArchiveSearchRequest
            {
                SelectedScopes = new List<ArchiveScopeTriple>
                {
                    new() { CompanyId = "comp-crash", ProjectId = "proj-crash", ArchiveId = plan.ArchiveId }
                },
                Query = "Subject"
            });
            Assert.Equal(4, searchResp.TotalCount);
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Requirement 7:
    /// Fresh empty registry restart resume for MBOX bounded record source:
    /// MBOX ingest interrupted after 2/4 records.
    /// Restart with empty registry rehydrates from plan, validates each bounded record hash,
    /// reuses verified staged files without duplicates, and completes remaining records.
    /// </summary>
    [Fact]
    public async Task Resume_AfterProcessRestart_WithFreshEmptyRegistry_Succeeds_AndReusesPartialStaging_Mbox()
    {
        string runtimeDir = CreateTempDir("r7-mbox-resume");
        try
        {
            string mboxDir = Path.Combine(runtimeDir, "sources");
            Directory.CreateDirectory(mboxDir);
            string mboxPath = Path.Combine(mboxDir, "inbox.mbox");

            var sb = new StringBuilder();
            for (int i = 1; i <= 4; i++)
            {
                sb.AppendLine("From sender@example.com Mon Sep 14 00:00:00 2026");
                sb.AppendLine("From: sender@example.com");
                sb.AppendLine("To: recipient@example.com");
                sb.AppendLine($"Subject: Mbox Order #{100 + i}");
                sb.AppendLine("MIME-Version: 1.0");
                sb.AppendLine("Content-Type: text/plain; charset=utf-8");
                sb.AppendLine();
                sb.AppendLine($"Body text for order {100 + i}");
                sb.AppendLine();
            }
            File.WriteAllText(mboxPath, sb.ToString(), new UTF8Encoding(false));

            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry1 = new FileHandleRegistry();
            var jobManager1 = new JobManager(runtimeDir);
            var catalogService1 = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry1, jobManager1);

            var inspector = new MimeSourceInspector();
            var mboxManifest = inspector.BuildMboxManifest(mboxPath);
            string handle = handleRegistry1.RegisterMimeSource(mboxManifest, "inbox.mbox");

            var preview = catalogService1.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "MBOX Restart Archive",
                CompanyId = "comp-mbox",
                ProjectId = "proj-mbox",
                SourceHandle = handle
            });

            var plan = planStore.GetPlan(preview.PreviewId)!;
            string stagingDir = storageManager.EnsureStagingDirectory(plan.ArchiveId);

            // Pre-stage first 2 records into staging
            List<MboxrdRecord> records;
            using (var fs = new FileStream(mboxPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                records = MboxrdRecordReader.EnumerateRecords(fs, ArchiveMimeParser.MaxRawMessageSizeBytes).ToList();
            }
            Assert.Equal(4, records.Count);

            using (var ms1 = new MemoryStream(records[0].RawMimeBytes, writable: false))
            {
                await storageManager.StoreMimeItemAsync(
                    stagingDir, 1, plan.Items[0].ItemId, plan.Items[0].MappedFolder,
                    ms1, plan.Items[0].SourceSha256, records[0].EnvelopeFrom, CancellationToken.None);
            }
            using (var ms2 = new MemoryStream(records[1].RawMimeBytes, writable: false))
            {
                await storageManager.StoreMimeItemAsync(
                    stagingDir, 2, plan.Items[1].ItemId, plan.Items[1].MappedFolder,
                    ms2, plan.Items[1].SourceSha256, records[1].EnvelopeFrom, CancellationToken.None);
            }

            string staged1 = Path.Combine(stagingDir, "messages", "000001.eml");
            string staged2 = Path.Combine(stagingDir, "messages", "000002.eml");
            Assert.True(File.Exists(staged1));
            Assert.True(File.Exists(staged2));
            var staged1Bytes = File.ReadAllBytes(staged1);

            string jobId = "job-crash-mbox-02";
            var record = new LocalJobRecord
            {
                JobId = jobId,
                JobKind = "archive-ingest",
                PlanId = plan.PlanId,
                ArchiveId = plan.ArchiveId,
                ArchiveName = plan.ArchiveName,
                Status = "interrupted",
                Stage = "Kesintiye Uğradı",
                ItemsRead = 2,
                ItemsWritten = 2,
                TotalItems = 4,
                TotalSourceMessages = 4,
                SelectedMessagesCount = 4,
                ClientContext = new ClientProjectContext { CompanyId = "comp-mbox", ProjectId = "proj-mbox" },
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            };
            File.WriteAllText(Path.Combine(runtimeDir, "jobs", $"{jobId}.json"), JsonSerializer.Serialize(record, JsonOpts));

            // SIMULATE RESTART with empty registry
            var handleRegistry2 = new FileHandleRegistry();
            var jobManager2 = new JobManager(runtimeDir);
            var clientContext = new ClientProjectContext { CompanyId = "comp-mbox", ProjectId = "proj-mbox" };

            var resumedJob = jobManager2.ResumeArchiveIngestJob(
                jobId,
                clientContext,
                storageManager,
                searchIndex,
                planStore,
                handleRegistry2,
                runInBackground: false);

            Assert.Equal("completed", resumedJob.Status);
            Assert.Equal(4, resumedJob.ItemsWritten);

            var managedManifest = storageManager.GetArchiveManifest(plan.ArchiveId);
            Assert.NotNull(managedManifest);
            Assert.Equal(4, managedManifest.TotalItems);

            string managed1 = Path.Combine(runtimeDir, "archives", plan.ArchiveId, "messages", "000001.eml");
            Assert.Equal(staged1Bytes, File.ReadAllBytes(managed1));

            var searchResp = catalogService1.Search(new ArchiveSearchRequest
            {
                SelectedScopes = new List<ArchiveScopeTriple>
                {
                    new() { CompanyId = "comp-mbox", ProjectId = "proj-mbox", ArchiveId = plan.ArchiveId }
                },
                Query = "Order"
            });
            Assert.Equal(4, searchResp.TotalCount);
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Requirement 8:
    /// Source mutation before start/resume:
    /// Physical source file tampered with before resume starts.
    /// Pre-flight validation against frozen plan fails closed BEFORE any staging mutation.
    /// Verified partial staging files are preserved (not erased).
    /// </summary>
    [Fact]
    public async Task Resume_SourceMutationBeforeStart_FailsClosed_AndPreservesPartialStaging()
    {
        string runtimeDir = CreateTempDir("r8-mutation-before-start");
        try
        {
            string emlDir = Path.Combine(runtimeDir, "sources", "eml");
            string f1 = CreateEmlFile(emlDir, "01.eml", "Report 1", "Body 1");
            string f2 = CreateEmlFile(emlDir, "02.eml", "Report 2", "Body 2");
            string f3 = CreateEmlFile(emlDir, "03.eml", "Report 3", "Body 3");

            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry1 = new FileHandleRegistry();
            var jobManager1 = new JobManager(runtimeDir);
            var catalogService1 = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry1, jobManager1);

            var inspector = new MimeSourceInspector();
            var manifest = inspector.BuildEmlDirectoryManifest(emlDir);
            string handle = handleRegistry1.RegisterMimeSource(manifest, "eml-dir");

            var preview = catalogService1.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "Tamper Archive",
                CompanyId = "comp-tamper",
                ProjectId = "proj-tamper",
                SourceHandle = handle
            });

            var plan = planStore.GetPlan(preview.PreviewId)!;
            string stagingDir = storageManager.EnsureStagingDirectory(plan.ArchiveId);

            // Pre-stage first file
            using (var fs1 = new FileStream(f1, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await storageManager.StoreMimeItemAsync(
                    stagingDir, 1, plan.Items[0].ItemId, plan.Items[0].MappedFolder,
                    fs1, plan.Items[0].SourceSha256, null, CancellationToken.None);
            }

            string staged1 = Path.Combine(stagingDir, "messages", "000001.eml");
            Assert.True(File.Exists(staged1));
            byte[] staged1OriginalBytes = File.ReadAllBytes(staged1);

            // MUTATE source file 2 on disk BEFORE resume
            File.AppendAllText(f2, "\r\nTAMPERED_MUTATION_BEFORE_START");

            string jobId = "job-tamper-01";
            var record = new LocalJobRecord
            {
                JobId = jobId,
                JobKind = "archive-ingest",
                PlanId = plan.PlanId,
                ArchiveId = plan.ArchiveId,
                ArchiveName = plan.ArchiveName,
                Status = "interrupted",
                Stage = "Kesintiye Uğradı",
                ItemsRead = 1,
                ItemsWritten = 1,
                TotalItems = 3,
                TotalSourceMessages = 3,
                SelectedMessagesCount = 3,
                ClientContext = new ClientProjectContext { CompanyId = "comp-tamper", ProjectId = "proj-tamper" },
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            };
            File.WriteAllText(Path.Combine(runtimeDir, "jobs", $"{jobId}.json"), JsonSerializer.Serialize(record, JsonOpts));

            var handleRegistry2 = new FileHandleRegistry();
            var jobManager2 = new JobManager(runtimeDir);
            var clientContext = new ClientProjectContext { CompanyId = "comp-tamper", ProjectId = "proj-tamper" };

            // Resume should fail closed
            Assert.Throws<InvalidOperationException>(() =>
            {
                jobManager2.ResumeArchiveIngestJob(
                    jobId,
                    clientContext,
                    storageManager,
                    searchIndex,
                    planStore,
                    handleRegistry2,
                    runInBackground: false);
            });

            var failedJob = jobManager2.GetJob(jobId)!;
            Assert.Equal("failed", failedJob.Status);
            Assert.Contains("BÜTÜNLÜK ENGELİ", failedJob.ErrorMessage);

            // Partial staging MUST BE PRESERVED on disk
            Assert.True(File.Exists(staged1), "Partial staging file 000001.eml must be preserved");
            Assert.Equal(staged1OriginalBytes, File.ReadAllBytes(staged1));

            // Archive MUST NOT be published
            Assert.Null(storageManager.GetArchiveManifest(plan.ArchiveId));
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Requirement 9:
    /// Late source mutation before publication:
    /// Physical source file tampered with immediately before publication.
    /// Second validation immediately before publication detects mutation and fails closed.
    /// Publication is aborted; staging is preserved for quarantine/diagnosis.
    /// </summary>
    [Fact]
    public void Ingest_LateSourceMutationBeforePublication_FailsClosed_AndPreservesPartialStaging_WithoutPublishingArchive()
    {
        string runtimeDir = CreateTempDir("r9-late-mutation");
        try
        {
            string emlDir = Path.Combine(runtimeDir, "sources", "eml");
            string f1 = CreateEmlFile(emlDir, "01.eml", "Notice 1", "Body 1");
            string f2 = CreateEmlFile(emlDir, "02.eml", "Notice 2", "Body 2");
            string f3 = CreateEmlFile(emlDir, "03.eml", "Notice 3", "Body 3");

            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(runtimeDir);
            var catalogService = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry, jobManager);

            var inspector = new MimeSourceInspector();
            var manifest = inspector.BuildEmlDirectoryManifest(emlDir);
            string handle = handleRegistry.RegisterMimeSource(manifest, "eml-dir");

            var preview = catalogService.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "Late Mutation Archive",
                CompanyId = "comp-late",
                ProjectId = "proj-late",
                SourceHandle = handle
            });

            var plan = planStore.GetPlan(preview.PreviewId)!;

            // Hook into JobManager: when all 3 items are staged into staging, tamper with source f3 before final validation/publish
            jobManager.OnBeforeSaveJobRecord = (rec) =>
            {
                if (rec.ItemsWritten == 3 && rec.Status == "converting")
                {
                    File.AppendAllText(f3, "\r\nLATE_TAMPER_AFTER_STAGING_BEFORE_PUBLISH");
                }
            };

            var clientContext = new ClientProjectContext { CompanyId = "comp-late", ProjectId = "proj-late" };

            Assert.Throws<InvalidOperationException>(() =>
            {
                jobManager.StartArchiveIngestJob(
                    plan,
                    "idemp-late-tamper",
                    clientContext,
                    storageManager,
                    searchIndex,
                    planStore,
                    handleRegistry,
                    runInBackground: false);
            });

            // Archive MUST NOT be published to managed storage
            Assert.Null(storageManager.GetArchiveManifest(plan.ArchiveId));

            // Staging files MUST BE PRESERVED in .staging for quarantine
            string stagingMessagesDir = Path.Combine(runtimeDir, "archives", ".staging", plan.ArchiveId, "messages");
            Assert.True(Directory.Exists(stagingMessagesDir));
            Assert.True(File.Exists(Path.Combine(stagingMessagesDir, "000001.eml")));
            Assert.True(File.Exists(Path.Combine(stagingMessagesDir, "000002.eml")));
            Assert.True(File.Exists(Path.Combine(stagingMessagesDir, "000003.eml")));
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Requirement 10:
    /// Missing source files:
    /// Entire source folder removed before resume.
    /// Fails closed with controlled error; partial staging files remain preserved on disk.
    /// </summary>
    [Fact]
    public async Task Resume_MissingSource_FailsClosed_AndPreservesPartialStaging()
    {
        string runtimeDir = CreateTempDir("r10-missing-source");
        try
        {
            string emlDir = Path.Combine(runtimeDir, "sources", "eml");
            string f1 = CreateEmlFile(emlDir, "01.eml", "Audit 1", "Body 1");
            string f2 = CreateEmlFile(emlDir, "02.eml", "Audit 2", "Body 2");

            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry1 = new FileHandleRegistry();
            var jobManager1 = new JobManager(runtimeDir);
            var catalogService1 = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry1, jobManager1);

            var inspector = new MimeSourceInspector();
            var manifest = inspector.BuildEmlDirectoryManifest(emlDir);
            string handle = handleRegistry1.RegisterMimeSource(manifest, "eml-dir");

            var preview = catalogService1.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "Missing Source Archive",
                CompanyId = "comp-missing",
                ProjectId = "proj-missing",
                SourceHandle = handle
            });

            var plan = planStore.GetPlan(preview.PreviewId)!;
            string stagingDir = storageManager.EnsureStagingDirectory(plan.ArchiveId);

            // Pre-stage first file
            using (var fs1 = new FileStream(f1, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await storageManager.StoreMimeItemAsync(
                    stagingDir, 1, plan.Items[0].ItemId, plan.Items[0].MappedFolder,
                    fs1, plan.Items[0].SourceSha256, null, CancellationToken.None);
            }

            string staged1 = Path.Combine(stagingDir, "messages", "000001.eml");
            Assert.True(File.Exists(staged1));

            // DELETE entire source folder
            Directory.Delete(emlDir, recursive: true);
            Assert.False(Directory.Exists(emlDir));

            string jobId = "job-missing-01";
            var record = new LocalJobRecord
            {
                JobId = jobId,
                JobKind = "archive-ingest",
                PlanId = plan.PlanId,
                ArchiveId = plan.ArchiveId,
                ArchiveName = plan.ArchiveName,
                Status = "interrupted",
                Stage = "Kesintiye Uğradı",
                ItemsRead = 1,
                ItemsWritten = 1,
                TotalItems = 2,
                TotalSourceMessages = 2,
                SelectedMessagesCount = 2,
                ClientContext = new ClientProjectContext { CompanyId = "comp-missing", ProjectId = "proj-missing" },
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            };
            File.WriteAllText(Path.Combine(runtimeDir, "jobs", $"{jobId}.json"), JsonSerializer.Serialize(record, JsonOpts));

            var handleRegistry2 = new FileHandleRegistry();
            var jobManager2 = new JobManager(runtimeDir);
            var clientContext = new ClientProjectContext { CompanyId = "comp-missing", ProjectId = "proj-missing" };

            Assert.ThrowsAny<Exception>(() =>
            {
                jobManager2.ResumeArchiveIngestJob(
                    jobId,
                    clientContext,
                    storageManager,
                    searchIndex,
                    planStore,
                    handleRegistry2,
                    runInBackground: false);
            });

            var failedJob = jobManager2.GetJob(jobId)!;
            Assert.Equal("failed", failedJob.Status);

            // Assert partial staging file 000001.eml is STILL on disk (not wiped)
            Assert.True(File.Exists(staged1));
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Requirement 11:
    /// Completed published archive reindex remains source-independent:
    /// After an archive is published, the original source folder is deleted entirely.
    /// Reindex succeeds and search continues to work cleanly because reindexing
    /// reads exclusively from managed storage.
    /// </summary>
    [Fact]
    public void CompletedPublishedArchive_Reindex_RemainsSourceIndependent()
    {
        string runtimeDir = CreateTempDir("r11-source-independent-reindex");
        try
        {
            string emlDir = Path.Combine(runtimeDir, "sources", "eml");
            CreateEmlFile(emlDir, "01.eml", "Independent Note 1", "Body 1");
            CreateEmlFile(emlDir, "02.eml", "Independent Note 2", "Body 2");

            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(runtimeDir);
            var catalogService = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry, jobManager);

            var inspector = new MimeSourceInspector();
            var manifest = inspector.BuildEmlDirectoryManifest(emlDir);
            string handle = handleRegistry.RegisterMimeSource(manifest, "eml-dir");

            var preview = catalogService.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "Source Independent Archive",
                CompanyId = "comp-indep",
                ProjectId = "proj-indep",
                SourceHandle = handle
            });

            var plan = planStore.GetPlan(preview.PreviewId)!;
            var clientContext = new ClientProjectContext { CompanyId = "comp-indep", ProjectId = "proj-indep" };

            var job = jobManager.StartArchiveIngestJob(
                plan,
                "idemp-indep",
                clientContext,
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                runInBackground: false);

            Assert.Equal("completed", job.Status);

            // DELETE original source completely!
            Directory.Delete(emlDir, recursive: true);
            Assert.False(Directory.Exists(emlDir));

            // Reindex job runs without source
            var reindexJob = jobManager.StartArchiveReindexJob(
                plan.ArchiveId,
                "idemp-reindex-indep",
                clientContext,
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                runInBackground: false);

            Assert.Equal("completed", reindexJob.Status);
            Assert.Equal(2, reindexJob.ItemsWritten);

            // Search works cleanly
            var searchResp = catalogService.Search(new ArchiveSearchRequest
            {
                SelectedScopes = new List<ArchiveScopeTriple>
                {
                    new() { CompanyId = "comp-indep", ProjectId = "proj-indep", ArchiveId = plan.ArchiveId }
                },
                Query = "Independent Note"
            });
            Assert.Equal(2, searchResp.TotalCount);
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }
}
