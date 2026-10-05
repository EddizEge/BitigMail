using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Bridge;
using BitigMail.Engine.Imap;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost;
using BitigMail.LocalHost.Bridge.Transfer;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.Imap.Transfer;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Security;
using MailKit;
using Microsoft.AspNetCore.Http;
using MimeKit;
using Xunit;

namespace BitigMail.Engine.Tests;

public class BridgeSecurityAndScaleTests
{
    private const string SecretSentinel = "SECRET_SENTINEL_TOKEN_XYZ_999";
    private const string SecretPath = @"C:\sensitive\passwords.txt";

    private static readonly JsonSerializerOptions UnescapedJsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string ExtractResponseJson(IResult result)
    {
        if (result is IValueHttpResult valueResult)
        {
            return JsonSerializer.Serialize(valueResult.Value, UnescapedJsonOptions);
        }
        return JsonSerializer.Serialize(result, UnescapedJsonOptions);
    }

    private static int? ExtractStatusCode(IResult result)
    {
        if (result is IStatusCodeHttpResult statusResult)
        {
            return statusResult.StatusCode;
        }
        return null;
    }

    private class ThrowingImportPreviewService : BridgeImportPreviewService
    {
        private readonly Exception _exceptionToThrow;

        public ThrowingImportPreviewService(
            FileHandleRegistry handleRegistry,
            ImapAccountStore accountStore,
            IImapTransferClientFactory clientFactory,
            BridgeTransferJournal journal,
            IImapCredentialResolver credentialResolver,
            Exception exceptionToThrow)
            : base(handleRegistry, accountStore, clientFactory, journal, credentialResolver)
        {
            _exceptionToThrow = exceptionToThrow;
        }

        public override Task<BridgeImportPreviewResponse> CreatePreviewAsync(BridgeImportPreviewRequest request, CancellationToken ct = default)
        {
            throw _exceptionToThrow;
        }
    }

    private class ThrowingExportPreviewService : BridgeExportPreviewService
    {
        private readonly Exception _exceptionToThrow;

        public ThrowingExportPreviewService(
            FileHandleRegistry handleRegistry,
            ImapAccountStore accountStore,
            IImapTransferClientFactory clientFactory,
            BridgeTransferJournal journal,
            IImapCredentialResolver credentialResolver,
            Exception exceptionToThrow)
            : base(handleRegistry, accountStore, clientFactory, journal, credentialResolver)
        {
            _exceptionToThrow = exceptionToThrow;
        }

        public override Task<BridgeExportPreviewResponse> CreatePreviewAsync(BridgeExportPreviewRequest request, CancellationToken ct = default)
        {
            throw _exceptionToThrow;
        }
    }

    private class ThrowingJobManager : JobManager
    {
        private readonly Exception _exceptionToThrow;

        public ThrowingJobManager(string runtimeDir, Exception exceptionToThrow) : base(runtimeDir)
        {
            _exceptionToThrow = exceptionToThrow;
        }

        public override LocalJobRecord StartBridgeImportJob(
            BridgeImportPlan plan,
            string? idempotencyKey,
            ClientProjectContext clientContext,
            FileHandleRegistry handleRegistry,
            ImapAccountStore accountStore,
            IImapTransferClientFactory clientFactory,
            BridgeTransferJournal transferJournal,
            IImapCredentialResolver? credentialResolver = null,
            bool enqueueIfBusy = false)
        {
            throw _exceptionToThrow;
        }

        public override LocalJobRecord ResumeBridgeImportJob(
            string jobId,
            ClientProjectContext clientContext,
            FileHandleRegistry handleRegistry,
            ImapAccountStore accountStore,
            IImapTransferClientFactory clientFactory,
            BridgeTransferJournal transferJournal,
            IImapCredentialResolver? credentialResolver = null,
            bool enqueueIfBusy = false)
        {
            throw _exceptionToThrow;
        }

        public override LocalJobRecord StartBridgeExportJob(
            BridgeExportPlan plan,
            string? idempotencyKey,
            ClientProjectContext clientContext,
            FileHandleRegistry handleRegistry,
            ImapAccountStore accountStore,
            IImapTransferClientFactory clientFactory,
            BridgeTransferJournal transferJournal,
            IImapCredentialResolver? credentialResolver = null,
            bool enqueueIfBusy = false)
        {
            throw _exceptionToThrow;
        }

        public override LocalJobRecord ResumeBridgeExportJob(
            string jobId,
            ClientProjectContext clientContext,
            FileHandleRegistry handleRegistry,
            ImapAccountStore accountStore,
            IImapTransferClientFactory clientFactory,
            BridgeTransferJournal transferJournal,
            IImapCredentialResolver? credentialResolver = null,
            bool enqueueIfBusy = false)
        {
            throw _exceptionToThrow;
        }
    }

    // -----------------------------------------------------------------
    // 1. Secret & Path Leak Prevention Tests
    // -----------------------------------------------------------------

    [Fact]
    public async Task ImportPreview_ExceptionWithSecretAndPath_NeverLeaksInResponse()
    {
        string testDir = BridgeTestHelpers.CreateTestDir("import-preview-leak");
        try
        {
            var (accountStore, _, targetId, resolver) = BridgeTestHelpers.SetupTestAccounts(testDir);
            var clientFactory = new BridgeFakeClientFactory(new BridgeFakeTransferClient());
            var journal = new BridgeTransferJournal(testDir);
            var handleRegistry = new FileHandleRegistry();

            var injectedException = new Exception($"Internal crash while accessing {SecretPath} with credential {SecretSentinel}!");
            var service = new ThrowingImportPreviewService(handleRegistry, accountStore, clientFactory, journal, resolver, injectedException);

            var req = new BridgeImportPreviewRequest
            {
                CompanyId = "company-1",
                ProjectId = "project-1",
                SourceHandle = "src_dummy",
                TargetAccountId = targetId,
                SelectedFolders = new List<string> { "INBOX" }
            };

            var result = await LocalEngineApiEndpointsBridgeTransfer.HandleBridgeImportPreviewAsync(service, req, CancellationToken.None);

            Assert.Equal(400, ExtractStatusCode(result));
            string json = ExtractResponseJson(result);
            Assert.DoesNotContain(SecretSentinel, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(SecretPath, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("passwords.txt", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(LocalEngineApiEndpointsBridgeTransfer.DefaultImportPreviewError, json);
        }
        finally
        {
            try { Directory.Delete(testDir, true); } catch { }
        }
    }

    [Fact]
    public void ImportStart_ExceptionWithSecretAndPath_NeverLeaksInResponse()
    {
        string testDir = BridgeTestHelpers.CreateTestDir("import-start-leak");
        try
        {
            var (accountStore, _, targetId, resolver) = BridgeTestHelpers.SetupTestAccounts(testDir);
            var clientFactory = new BridgeFakeClientFactory(new BridgeFakeTransferClient());
            var journal = new BridgeTransferJournal(testDir);
            var handleRegistry = new FileHandleRegistry();

            string planId = $"plan_imp_{Guid.NewGuid():N}";
            var plan = new BridgeImportPlan
            {
                PlanId = planId,
                PreviewId = planId,
                CompanyId = "company-1",
                ProjectId = "project-1",
                SourceHandle = "src_dummy",
                SourceFingerprint = "dummy_fingerprint",
                SourceKind = "eml-tree",
                TargetAccountId = targetId,
                TargetAccountVersion = 1,
                CanTransfer = true,
                Items = new List<BridgeImportPlannedItem>
                {
                    new()
                    {
                        ItemId = "item_1",
                        SourceRelativePath = "msg1.eml",
                        SourceCanonicalPath = Path.Combine(testDir, "msg1.eml"),
                        PhysicalOrdinal = 0,
                        SourceMappedFolder = "INBOX",
                        TargetFolder = "INBOX",
                        SourceSha256 = "dummy_source_sha",
                        CanonicalSha256 = "dummy_canonical_sha",
                        PlannedInternalDateUtc = DateTimeOffset.UtcNow
                    }
                }
            };
            journal.SaveImportPlan(plan);

            var injectedException = new InvalidOperationException($"[ÖN KONTROL ENGELİ] Unsafe leak at {SecretPath} with key {SecretSentinel}");
            var jobManager = new ThrowingJobManager(testDir, injectedException);

            var req = new BridgeStartRequest
            {
                PreviewId = planId,
                IdempotencyKey = $"idemp_{Guid.NewGuid():N}",
                CompanyId = "company-1",
                ProjectId = "project-1"
            };

            var result = LocalEngineApiEndpointsBridgeTransfer.HandleStartBridgeImport(
                jobManager, journal, handleRegistry, accountStore, clientFactory, resolver, req);

            Assert.True(ExtractStatusCode(result) is 400 or 409);
            string json = ExtractResponseJson(result);
            Assert.DoesNotContain(SecretSentinel, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(SecretPath, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("passwords.txt", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(LocalEngineApiEndpointsBridgeTransfer.DefaultImportStartError, json);
        }
        finally
        {
            try { Directory.Delete(testDir, true); } catch { }
        }
    }

    [Fact]
    public void ImportResume_ExceptionWithSecretAndPath_NeverLeaksInResponse()
    {
        string testDir = BridgeTestHelpers.CreateTestDir("import-resume-leak");
        try
        {
            var (accountStore, _, _, resolver) = BridgeTestHelpers.SetupTestAccounts(testDir);
            var clientFactory = new BridgeFakeClientFactory(new BridgeFakeTransferClient());
            var journal = new BridgeTransferJournal(testDir);
            var handleRegistry = new FileHandleRegistry();

            var injectedException = new KeyNotFoundException($"Key missing at {SecretPath} using token {SecretSentinel}");
            var jobManager = new ThrowingJobManager(testDir, injectedException);

            var req = new BridgeResumeRequest
            {
                CompanyId = "company-1",
                ProjectId = "project-1"
            };

            var result = LocalEngineApiEndpointsBridgeTransfer.HandleResumeBridgeImport(
                jobManager, journal, handleRegistry, accountStore, clientFactory, resolver, "job_test_123", req, "company-1", "project-1");

            Assert.True(ExtractStatusCode(result) is 400 or 404 or 409);
            string json = ExtractResponseJson(result);
            Assert.DoesNotContain(SecretSentinel, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(SecretPath, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("passwords.txt", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(testDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ExportPreview_ExceptionWithSecretAndPath_NeverLeaksInResponse()
    {
        string testDir = BridgeTestHelpers.CreateTestDir("export-preview-leak");
        try
        {
            var (accountStore, sourceId, _, resolver) = BridgeTestHelpers.SetupTestAccounts(testDir);
            var clientFactory = new BridgeFakeClientFactory(new BridgeFakeTransferClient());
            var journal = new BridgeTransferJournal(testDir);
            var handleRegistry = new FileHandleRegistry();

            var injectedException = new Exception($"Fatal export probe error: path={SecretPath}, secret={SecretSentinel}");
            var service = new ThrowingExportPreviewService(handleRegistry, accountStore, clientFactory, journal, resolver, injectedException);

            var req = new BridgeExportPreviewRequest
            {
                CompanyId = "company-1",
                ProjectId = "project-1",
                SourceAccountId = sourceId,
                TargetDirHandle = "dir_dummy",
                TargetFormat = "eml-tree",
                SelectedFolders = new List<string> { "INBOX" }
            };

            var result = await LocalEngineApiEndpointsBridgeTransfer.HandleBridgeExportPreviewAsync(service, req, CancellationToken.None);

            Assert.Equal(400, ExtractStatusCode(result));
            string json = ExtractResponseJson(result);
            Assert.DoesNotContain(SecretSentinel, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(SecretPath, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("passwords.txt", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(LocalEngineApiEndpointsBridgeTransfer.DefaultExportPreviewError, json);
        }
        finally
        {
            try { Directory.Delete(testDir, true); } catch { }
        }
    }

    [Fact]
    public void ExportStart_ExceptionWithSecretAndPath_NeverLeaksInResponse()
    {
        string testDir = BridgeTestHelpers.CreateTestDir("export-start-leak");
        try
        {
            var (accountStore, sourceId, _, resolver) = BridgeTestHelpers.SetupTestAccounts(testDir);
            var clientFactory = new BridgeFakeClientFactory(new BridgeFakeTransferClient());
            var journal = new BridgeTransferJournal(testDir);
            var handleRegistry = new FileHandleRegistry();

            string planId = $"plan_exp_{Guid.NewGuid():N}";
            var plan = new BridgeExportPlan
            {
                PlanId = planId,
                PreviewId = planId,
                CompanyId = "company-1",
                ProjectId = "project-1",
                SourceAccountId = sourceId,
                SourceAccountVersion = 1,
                TargetDirHandle = "dir_dummy",
                TargetDirectoryPath = Path.Combine(testDir, "export-out"),
                TargetFormat = "eml-tree",
                CanTransfer = true,
                FolderMappings = new List<BridgeFolderMapping>
                {
                    new() { FolderKey = "fld_001", OriginalFolder = "INBOX" }
                },
                Items = new List<BridgeExportPlannedItem>
                {
                    new()
                    {
                        ItemId = "exp_1",
                        SourceFolder = "INBOX",
                        FolderKey = "fld_001",
                        SourceUid = 101,
                        SourceUidValidity = 12345u,
                        SourceSha256 = "dummy_sha",
                        RelativeOutputPath = "INBOX/msg_00000001.eml",
                        InternalDateUtc = DateTimeOffset.UtcNow
                    }
                }
            };
            journal.SaveExportPlan(plan);

            var injectedException = new InvalidOperationException($"IO failure at {SecretPath} containing {SecretSentinel}");
            var jobManager = new ThrowingJobManager(testDir, injectedException);

            var req = new BridgeStartRequest
            {
                PreviewId = planId,
                IdempotencyKey = $"idemp_{Guid.NewGuid():N}",
                CompanyId = "company-1",
                ProjectId = "project-1"
            };

            var result = LocalEngineApiEndpointsBridgeTransfer.HandleStartBridgeExport(
                jobManager, journal, handleRegistry, accountStore, clientFactory, resolver, req);

            Assert.True(ExtractStatusCode(result) is 400 or 409);
            string json = ExtractResponseJson(result);
            Assert.DoesNotContain(SecretSentinel, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(SecretPath, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("passwords.txt", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(LocalEngineApiEndpointsBridgeTransfer.DefaultExportStartError, json);
        }
        finally
        {
            try { Directory.Delete(testDir, true); } catch { }
        }
    }

    [Fact]
    public void ExportResume_ExceptionWithSecretAndPath_NeverLeaksInResponse()
    {
        string testDir = BridgeTestHelpers.CreateTestDir("export-resume-leak");
        try
        {
            var (accountStore, _, _, resolver) = BridgeTestHelpers.SetupTestAccounts(testDir);
            var clientFactory = new BridgeFakeClientFactory(new BridgeFakeTransferClient());
            var journal = new BridgeTransferJournal(testDir);
            var handleRegistry = new FileHandleRegistry();

            var injectedException = new InvalidOperationException($"State corruption reading {SecretPath} with key {SecretSentinel}");
            var jobManager = new ThrowingJobManager(testDir, injectedException);

            var req = new BridgeResumeRequest
            {
                CompanyId = "company-1",
                ProjectId = "project-1"
            };

            var result = LocalEngineApiEndpointsBridgeTransfer.HandleResumeBridgeExport(
                jobManager, journal, handleRegistry, accountStore, clientFactory, resolver, "job_test_456", req, "company-1", "project-1");

            Assert.True(ExtractStatusCode(result) is 400 or 409);
            string json = ExtractResponseJson(result);
            Assert.DoesNotContain(SecretSentinel, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(SecretPath, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("passwords.txt", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(LocalEngineApiEndpointsBridgeTransfer.DefaultExportResumeError, json);
        }
        finally
        {
            try { Directory.Delete(testDir, true); } catch { }
        }
    }

    // -----------------------------------------------------------------
    // 2. Corrupt Plan Retrieval Safe Catch Tests (400/409, Never 500)
    // -----------------------------------------------------------------

    [Fact]
    public void StartBridgeImport_CorruptPlanRetrieval_ReturnsSafeConflictOrBadRequest_Not500()
    {
        string testDir = BridgeTestHelpers.CreateTestDir("corrupt-plan-import");
        try
        {
            var (accountStore, _, _, resolver) = BridgeTestHelpers.SetupTestAccounts(testDir);
            var clientFactory = new BridgeFakeClientFactory(new BridgeFakeTransferClient());
            var journal = new BridgeTransferJournal(testDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(testDir);

            string planId = $"plan_corrupt_{Guid.NewGuid():N}";
            string planFile = Path.Combine(journal.ImportPlansDirectory, $"{planId}.json");
            File.WriteAllText(planFile, "{ corrupt json content not valid }");

            var req = new BridgeStartRequest
            {
                PreviewId = planId,
                IdempotencyKey = "idemp_1",
                CompanyId = "company-1",
                ProjectId = "project-1"
            };

            var result = LocalEngineApiEndpointsBridgeTransfer.HandleStartBridgeImport(
                jobManager, journal, handleRegistry, accountStore, clientFactory, resolver, req);

            int? statusCode = ExtractStatusCode(result);
            Assert.True(statusCode is 400 or 409, $"Expected safe 400 or 409, got {statusCode}");
            string json = ExtractResponseJson(result);
            Assert.Contains(LocalEngineApiEndpointsBridgeTransfer.DefaultImportStartError, json);
        }
        finally
        {
            try { Directory.Delete(testDir, true); } catch { }
        }
    }

    [Fact]
    public void StartBridgeExport_CorruptPlanRetrieval_ReturnsSafeConflictOrBadRequest_Not500()
    {
        string testDir = BridgeTestHelpers.CreateTestDir("corrupt-plan-export");
        try
        {
            var (accountStore, _, _, resolver) = BridgeTestHelpers.SetupTestAccounts(testDir);
            var clientFactory = new BridgeFakeClientFactory(new BridgeFakeTransferClient());
            var journal = new BridgeTransferJournal(testDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(testDir);

            string planId = $"plan_corrupt_{Guid.NewGuid():N}";
            string planFile = Path.Combine(journal.ExportPlansDirectory, $"{planId}.json");
            File.WriteAllText(planFile, "{ corrupt export json not valid }");

            var req = new BridgeStartRequest
            {
                PreviewId = planId,
                IdempotencyKey = "idemp_2",
                CompanyId = "company-1",
                ProjectId = "project-1"
            };

            var result = LocalEngineApiEndpointsBridgeTransfer.HandleStartBridgeExport(
                jobManager, journal, handleRegistry, accountStore, clientFactory, resolver, req);

            int? statusCode = ExtractStatusCode(result);
            Assert.True(statusCode is 400 or 409, $"Expected safe 400 or 409, got {statusCode}");
            string json = ExtractResponseJson(result);
            Assert.Contains(LocalEngineApiEndpointsBridgeTransfer.DefaultExportStartError, json);
        }
        finally
        {
            try { Directory.Delete(testDir, true); } catch { }
        }
    }

    // -----------------------------------------------------------------
    // 3. Controlled Domain Message Preservation
    // -----------------------------------------------------------------

    [Fact]
    public void ControlledDomainMessages_WhenSafe_ArePreservedInResponses()
    {
        Assert.True(LocalEngineApiEndpointsBridgeTransfer.IsSafeControlledDomainMessage("En az bir kaynak dosya seçilmelidir."));
        Assert.True(LocalEngineApiEndpointsBridgeTransfer.IsSafeControlledDomainMessage("Hedef IMAP hesabı zorunludur."));
        Assert.True(LocalEngineApiEndpointsBridgeTransfer.IsSafeControlledDomainMessage("[ÖN KONTROL ENGELİ] Hedef klasör salt-okunur durumdadır."));
        Assert.True(LocalEngineApiEndpointsBridgeTransfer.IsSafeControlledDomainMessage("[GÜVENLİK ENGELİ] Yetkisiz erişim girişimi."));

        Assert.False(LocalEngineApiEndpointsBridgeTransfer.IsSafeControlledDomainMessage("[ÖN KONTROL ENGELİ] Error at C:\\secret\\file.txt"));
        Assert.False(LocalEngineApiEndpointsBridgeTransfer.IsSafeControlledDomainMessage("En az bir kaynak dosya: token=SECRET_VAL"));
        Assert.False(LocalEngineApiEndpointsBridgeTransfer.IsSafeControlledDomainMessage("Unexpected NullReferenceException at System.IO.File"));
    }

    // -----------------------------------------------------------------
    // 4. Import Resume Without Registry: Revalidates Entire Frozen Set
    // -----------------------------------------------------------------

    [Fact]
    public async Task ImportResume_WithoutRegistry_RevalidatesEntireFrozenSourceSet_ChangedSecondItemBlocksWithZeroAppends()
    {
        string dir = BridgeTestHelpers.CreateTestDir("resume-reval-frozen");
        try
        {
            var (accountStore1, _, tgtAccId, resolver1) = BridgeTestHelpers.SetupTestAccounts(dir);
            var handleRegistry1 = new FileHandleRegistry();
            var journal1 = new BridgeTransferJournal(dir);
            var fakeClient1 = new BridgeFakeTransferClient();
            var clientFactory1 = new BridgeFakeClientFactory(fakeClient1);

            // 1. Create EML directory with 2 physical items
            string emlDir = Path.Combine(dir, "eml-source", "INBOX");
            Directory.CreateDirectory(emlDir);
            BridgeTestHelpers.CreateEmlFile(emlDir, "msg01.eml", "Subject 1", "Fri, 05 Jan 2024 10:00:00 +0300", "<id-1@example.test>", "Body 1\r\n");
            string msg2Path = BridgeTestHelpers.CreateEmlFile(emlDir, "msg02.eml", "Subject 2", "Sat, 06 Jan 2024 12:00:00 +0300", "<id-2@example.test>", "Body 2\r\n");

            var inspector = new MimeSourceInspector();
            var manifest = inspector.BuildEmlDirectoryManifest(Path.Combine(dir, "eml-source"));
            Assert.Equal(2, manifest.Entries.Count);

            string srcHandle = handleRegistry1.RegisterMimeSource(manifest, "eml-display");

            var previewService1 = new BridgeImportPreviewService(handleRegistry1, accountStore1, clientFactory1, journal1, resolver1);
            var preview = await previewService1.CreatePreviewAsync(new BridgeImportPreviewRequest
            {
                CompanyId = "company-1",
                ProjectId = "project-1",
                SourceHandle = srcHandle,
                TargetAccountId = tgtAccId,
                SelectedFolders = new List<string> { "INBOX" }
            });

            Assert.True(preview.CanTransfer);
            Assert.Equal(2, preview.TotalSourceItems);

            var plan = journal1.GetImportPlan(preview.PreviewId)!;
            Assert.Equal(2, plan.Items.Count);

            // 2. Start initial job with simulated failure/interruption
            var jobManager1 = new JobManager(dir);
            fakeClient1.CustomAppend = (_, _, _, _, _) => throw new IOException("Simulated network crash during first append");

            var clientContext = new ClientProjectContext { CompanyId = "company-1", ProjectId = "project-1" };
            var startedJob = jobManager1.StartBridgeImportJob(
                plan,
                "idemp_test_resume",
                clientContext,
                handleRegistry1,
                accountStore1,
                clientFactory1,
                journal1,
                resolver1);

            await BridgeTestHelpers.WaitForJobCompletionAsync(jobManager1, startedJob.JobId);
            var failedJob = jobManager1.GetJob(startedJob.JobId)!;
            Assert.True(failedJob.Status is "failed" or "interrupted");

            // 3. Mutate the SECOND item on disk
            File.WriteAllText(msg2Path, "MUTATED_CONTENT_AFTER_FREEZE\r\n");

            // 4. Simulate process restart with BRAND NEW objects and EMPTY handle registry
            var handleRegistry2 = new FileHandleRegistry(); // Empty: no MimeSource registered!
            var jobManager2 = new JobManager(dir); // Re-reads job from disk
            var (accountStore2, _, _, resolver2) = BridgeTestHelpers.SetupTestAccounts(dir);
            var fakeClient2 = new BridgeFakeTransferClient();
            var clientFactory2 = new BridgeFakeClientFactory(fakeClient2);

            // 5. Resume job
            var resumedJob = jobManager2.ResumeBridgeImportJob(
                startedJob.JobId,
                clientContext,
                handleRegistry2,
                accountStore2,
                clientFactory2,
                journal1,
                resolver2);

            await BridgeTestHelpers.WaitForJobCompletionAsync(jobManager2, resumedJob.JobId);
            var finalJob = jobManager2.GetJob(resumedJob.JobId)!;

            // 6. Assert failure due to integrity mismatch on the second item BEFORE first append
            Assert.Equal("failed", finalJob.Status);
            Assert.Contains("BÜTÜNLÜK ENGELİ", finalJob.ErrorMessage);
            Assert.Empty(fakeClient2.AppendedMessages); // ZERO appends because pre-flight revalidation stopped it
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // -----------------------------------------------------------------
    // 5. Export Resume: Reject Sibling JobOutputDir Tampering
    // -----------------------------------------------------------------

    [Fact]
    public async Task ExportResume_TamperedSiblingJobOutputDir_RejectedFailClosed()
    {
        string dir = BridgeTestHelpers.CreateTestDir("export-sibling-tamper");
        try
        {
            var (accountStore, srcAccId, _, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
            var handleRegistry = new FileHandleRegistry();
            var journal = new BridgeTransferJournal(dir);
            var fakeClient = new BridgeFakeTransferClient();
            var clientFactory = new BridgeFakeClientFactory(fakeClient);

            string outputRoot = Path.Combine(dir, "export-output");
            Directory.CreateDirectory(outputRoot);
            string dirHandle = handleRegistry.RegisterOutputDir(outputRoot);

            // Add 1 source message
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress("Sender", "sender@example.test"));
            msg.To.Add(new MailboxAddress("Recipient", "rec@example.test"));
            msg.Subject = "Export Msg 1";
            msg.Body = new TextPart("plain") { Text = "Body\r\n" };
            byte[] rawBytes = ImapSerializationAssumptions.Serialize(msg);
            fakeClient.AddSourceMessage("INBOX", 1, rawBytes, msg, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None);

            var previewService = new BridgeExportPreviewService(handleRegistry, accountStore, clientFactory, journal, resolver);
            var preview = await previewService.CreatePreviewAsync(new BridgeExportPreviewRequest
            {
                CompanyId = "company-1",
                ProjectId = "project-1",
                SourceAccountId = srcAccId,
                TargetDirHandle = dirHandle,
                TargetFormat = "eml-tree",
                SelectedFolders = new List<string> { "INBOX" }
            });

            Assert.True(preview.CanTransfer);
            var plan = journal.GetExportPlan(preview.PreviewId)!;

            string jobId = $"job_exp_{Guid.NewGuid():N}";
            string siblingTamperedDir = Path.Combine(outputRoot, "sibling-tampered-dir");
            Directory.CreateDirectory(siblingTamperedDir);

            // Initialize journal state with tampered sibling directory
            journal.InitializeExportJournal(jobId, plan, siblingTamperedDir);

            var worker = new BridgeExportWorker(
                handleRegistry,
                accountStore,
                clientFactory,
                journal,
                _ => { },
                _ => { },
                resolver);

            var jobRecord = new LocalJobRecord
            {
                JobId = jobId,
                JobKind = "bridge-export",
                PlanId = plan.PlanId,
                ClientContext = new ClientProjectContext { CompanyId = "company-1", ProjectId = "project-1" },
                Status = "interrupted"
            };

            await worker.ExecuteAsync(jobRecord, plan);

            Assert.Equal("failed", jobRecord.Status);
            Assert.Contains("dizin tahrifatı engeli", jobRecord.ErrorMessage);
            Assert.Empty(Directory.GetFiles(siblingTamperedDir));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // -----------------------------------------------------------------
    // 6. Vendor-Free >50 Source Acceptance Test
    // -----------------------------------------------------------------

    [Fact]
    public async Task VendorFree_Over50SourceAcceptance_Imports72ItemsWithoutTrialLimits()
    {
        string? current = AppContext.BaseDirectory;
        string? fixtureJsonPath = null;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, ".codex-coordination", "evidence", "TASK-017", "over50-fixture.json");
            if (File.Exists(candidate))
            {
                fixtureJsonPath = candidate;
                break;
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        if (fixtureJsonPath == null)
        {
            current = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(current))
            {
                string candidate = Path.Combine(current, ".codex-coordination", "evidence", "TASK-017", "over50-fixture.json");
                if (File.Exists(candidate))
                {
                    fixtureJsonPath = candidate;
                    break;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
        }

        Assert.True(fixtureJsonPath != null && File.Exists(fixtureJsonPath), "over50-fixture.json must exist in evidence");

        string jsonContent = File.ReadAllText(fixtureJsonPath);
        using var doc = JsonDocument.Parse(jsonContent);
        var root = doc.RootElement;
        int expectedCount = root.GetProperty("count").GetInt32();
        Assert.Equal(72, expectedCount);
        Assert.True(expectedCount > 50, "Acceptance requires strictly > 50 physical items");

        string configuredDir = root.GetProperty("directory").GetString()!;
        string fixtureDir = configuredDir;
        if (!Directory.Exists(fixtureDir))
        {
            string repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fixtureJsonPath)!, "..", "..", ".."));
            fixtureDir = Path.Combine(repoRoot, "runtime", "task017-qa", "over50-1cd78704");
        }
        Assert.True(Directory.Exists(fixtureDir), $"Fixture directory must exist at {fixtureDir}");

        // Inspect manifest from disk (read-only verification)
        var entries = Directory.GetFiles(fixtureDir, "*.eml");
        Assert.Equal(72, entries.Length);

        var inspector = new MimeSourceInspector();
        var manifest = inspector.BuildEmlDirectoryManifest(fixtureDir);
        Assert.Equal(72, manifest.Entries.Count);

        string testDir = BridgeTestHelpers.CreateTestDir("over50-acceptance");
        try
        {
            var (accountStore, _, targetId, resolver) = BridgeTestHelpers.SetupTestAccounts(testDir);
            var fakeClient = new BridgeFakeTransferClient { SupportsKeywords = true };
            var clientFactory = new BridgeFakeClientFactory(fakeClient);
            var journal = new BridgeTransferJournal(testDir);
            var handleRegistry = new FileHandleRegistry();

            string sourceHandle = handleRegistry.RegisterMimeSource(manifest, "synthetic-over50");

            var previewService = new BridgeImportPreviewService(handleRegistry, accountStore, clientFactory, journal, resolver);

            var previewReq = new BridgeImportPreviewRequest
            {
                CompanyId = "company-1",
                ProjectId = "project-1",
                SourceHandle = sourceHandle,
                TargetAccountId = targetId,
                SelectedFolders = manifest.Entries.Select(e => e.MappedFolder).Distinct().ToList()
            };

            var previewResponse = await previewService.CreatePreviewAsync(previewReq);

            Assert.NotNull(previewResponse);
            Assert.True(previewResponse.CanTransfer);
            Assert.Null(previewResponse.BlockerReason);
            Assert.Equal(72, previewResponse.TotalSourceItems);
            Assert.Equal(72, previewResponse.EligibleItemsCount);
            Assert.Equal(0, previewResponse.ExcludedCount);

            var savedPlan = journal.GetImportPlan(previewResponse.PreviewId);
            Assert.NotNull(savedPlan);
            Assert.Equal(72, savedPlan.Items.Count);
            Assert.True(savedPlan.CanTransfer);
        }
        finally
        {
            try { Directory.Delete(testDir, true); } catch { }
        }
    }

    // -----------------------------------------------------------------
    // 7. Vendor-Free Source Descriptor & TestingHost Fixture Tests
    // -----------------------------------------------------------------

    [Fact]
    public void BridgeSourceDescribe_ReturnsAccurateDescriptor_FromHandleRegistryManifest_WithoutAsposeOrTrialGuard()
    {
        var handleRegistry = new FileHandleRegistry();
        var manifest = new MimeSourceManifest
        {
            SourceKind = "eml-tree",
            Dialect = "rfc822",
            RootPath = @"C:\dummy\eml-tree",
            Entries = new List<MimeSourceEntry>
            {
                new() { CanonicalPath = @"C:\dummy\eml-tree\INBOX\msg1.eml", MappedFolder = "INBOX", SizeBytes = 100 },
                new() { CanonicalPath = @"C:\dummy\eml-tree\INBOX\msg2.eml", MappedFolder = "INBOX", SizeBytes = 150 },
                new() { CanonicalPath = @"C:\dummy\eml-tree\Arşiv\msg3.eml", MappedFolder = "Arşiv", SizeBytes = 200 }
            },
            TotalFiles = 3,
            IgnoredNonEmlFilesCount = 1
        };

        string handle = handleRegistry.RegisterMimeSource(manifest, "Test Eml Tree");

        var result = LocalEngineApiEndpointsBridgeTransfer.HandleBridgeSourceDescribe(
            handleRegistry, new BridgeSourceDescribeRequest { SourceHandle = handle });

        Assert.Equal(200, ExtractStatusCode(result));
        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var descriptor = Assert.IsType<BridgeSourceDescriptorResponse>(valueResult.Value);

        Assert.Equal(handle, descriptor.SourceHandle);
        Assert.Equal("eml-tree", descriptor.SourceKind);
        Assert.Equal("rfc822", descriptor.Dialect);
        Assert.Equal("Test Eml Tree", descriptor.DisplayPath);
        Assert.Equal(3, descriptor.TotalItems);
        Assert.Equal(450, descriptor.TotalSizeBytes);
        Assert.Equal(1, descriptor.IgnoredNonEmlFilesCount);
        Assert.Equal(2, descriptor.Folders.Count);

        var inboxFolder = descriptor.Folders.First(f => f.FolderName == "INBOX");
        Assert.Equal(2, inboxFolder.ItemCount);
        Assert.Equal(250, inboxFolder.TotalSizeBytes);

        var archiveFolder = descriptor.Folders.First(f => f.FolderName == "Arşiv");
        Assert.Equal(1, archiveFolder.ItemCount);
        Assert.Equal(200, archiveFolder.TotalSizeBytes);
    }

    [Fact]
    public void BridgeSourceDescribe_RejectsInvalidOrMissingHandle_FailClosed()
    {
        var handleRegistry = new FileHandleRegistry();

        // Null request
        var res1 = LocalEngineApiEndpointsBridgeTransfer.HandleBridgeSourceDescribe(handleRegistry, null);
        Assert.Equal(400, ExtractStatusCode(res1));

        // Invalid non-opaque handle
        var res2 = LocalEngineApiEndpointsBridgeTransfer.HandleBridgeSourceDescribe(
            handleRegistry, new BridgeSourceDescribeRequest { SourceHandle = @"C:\Windows\System32" });
        Assert.Equal(400, ExtractStatusCode(res2));

        // Missing handle
        var res3 = LocalEngineApiEndpointsBridgeTransfer.HandleBridgeSourceDescribe(
            handleRegistry, new BridgeSourceDescribeRequest { SourceHandle = "msrc_missing_001" });
        Assert.Equal(404, ExtractStatusCode(res3));
    }

    [Fact]
    public void BridgeMboxrdValidator_ValidMboxrd_PassesCleanly()
    {
        string validMbox =
            "From sender@test.local Mon Jan 01 00:00:00 2026\r\n" +
            "Subject: Test 1\r\n" +
            "From: sender@test.local\r\n" +
            "To: recipient@test.local\r\n" +
            "\r\n" +
            "Hello World\r\n" +
            ">From escaped line\r\n" +
            "\r\n" +
            "From sender2@test.local Mon Jan 01 00:01:00 2026\r\n" +
            "Subject: Test 2\r\n" +
            "From: sender2@test.local\r\n" +
            "\r\n" +
            "Body 2\r\n";

        using var ms = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(validMbox));
        BridgeMboxrdValidator.ValidateStrictMboxrd(ms, "valid-test");
    }

    [Fact]
    public void BridgeMboxrdValidator_RejectsLeadingBytesBeforeFirstFromEnvelope()
    {
        // Leading whitespace / bytes before "From "
        string leadingBytes =
            "  \r\nFrom sender@test.local Mon Jan 01 00:00:00 2026\r\n" +
            "Subject: Test\r\n" +
            "\r\n" +
            "Body\r\n";

        using var ms1 = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(leadingBytes));
        var ex1 = Assert.Throws<InvalidOperationException>(() =>
            BridgeMboxrdValidator.ValidateStrictMboxrd(ms1, "leading-bytes"));
        Assert.Contains("From ", ex1.Message);

        // UTF-8 BOM before "From "
        byte[] bom = [0xEF, 0xBB, 0xBF];
        byte[] body = System.Text.Encoding.ASCII.GetBytes("From sender@test.local Mon Jan 01 00:00:00 2026\r\nSubject: Test\r\n\r\nBody\r\n");
        byte[] combined = [.. bom, .. body];
        using var ms2 = new MemoryStream(combined);
        Assert.Throws<InvalidOperationException>(() =>
            BridgeMboxrdValidator.ValidateStrictMboxrd(ms2, "bom-bytes"));
    }

    [Fact]
    public void BridgeMboxrdValidator_RejectsEmptyOrMalformedRecord()
    {
        // Consecutive From lines with no headers
        string emptyRecord =
            "From sender1@test.local Mon Jan 01 00:00:00 2026\r\n" +
            "From sender2@test.local Mon Jan 01 00:01:00 2026\r\n" +
            "Subject: Test\r\n" +
            "\r\n" +
            "Body\r\n";

        using var ms = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(emptyRecord));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BridgeMboxrdValidator.ValidateStrictMboxrd(ms, "empty-record"));
        Assert.Contains("Boş veya hatalı", ex.Message);
    }

    [Fact]
    public void BridgeMboxrdValidator_RejectsAmbiguousDelimiterOrMalformedHeader()
    {
        // Header without colon or blank delimiter before EOF
        string malformedHeader =
            "From sender@test.local Mon Jan 01 00:00:00 2026\r\n" +
            "NotAValidHeaderLineWithoutColon\r\n" +
            "\r\n" +
            "Body\r\n";

        using var ms1 = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(malformedHeader));
        var ex1 = Assert.Throws<InvalidOperationException>(() =>
            BridgeMboxrdValidator.ValidateStrictMboxrd(ms1, "bad-header"));
        Assert.Contains("Belirsiz veya bozuk başlık satırı", ex1.Message);

        // No blank delimiter separating headers and body before EOF
        string noBlankDelimiter =
            "From sender@test.local Mon Jan 01 00:00:00 2026\r\n" +
            "Subject: Test\r\n";

        using var ms2 = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(noBlankDelimiter));
        var ex2 = Assert.Throws<InvalidOperationException>(() =>
            BridgeMboxrdValidator.ValidateStrictMboxrd(ms2, "no-delimiter"));
        Assert.Contains("Son kayıt tamamlanmamış veya bozuk", ex2.Message);
    }

    [Fact]
    public void BridgeSourceDescribe_StrictMboxrdValidation_RejectsMalformedMbox()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "task017_mbox_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string badMboxFile = Path.Combine(tempDir, "bad.mbox");
            File.WriteAllText(badMboxFile, "GARBAGE HEADER\r\nFrom sender@test.local Mon Jan 01 00:00:00 2026\r\nSubject: Test\r\n\r\nBody\r\n");

            var handleRegistry = new FileHandleRegistry();
            var manifest = new MimeSourceManifest
            {
                SourceKind = "mbox",
                Dialect = "mboxrd",
                RootPath = badMboxFile,
                Entries = new List<MimeSourceEntry>
                {
                    new()
                    {
                        CanonicalPath = badMboxFile,
                        RelativePath = "bad.mbox#record-1",
                        MappedFolder = "INBOX",
                        SizeBytes = 100,
                        Sha256 = "dummy_sha",
                        PhysicalOrdinal = 1
                    }
                }
            };
            string handle = handleRegistry.RegisterMimeSource(manifest, "Bad Mbox");

            var res = LocalEngineApiEndpointsBridgeTransfer.HandleBridgeSourceDescribe(
                handleRegistry, new BridgeSourceDescribeRequest { SourceHandle = handle });

            Assert.Equal(400, ExtractStatusCode(res));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
