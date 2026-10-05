using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Bridge;
using BitigMail.Engine.Imap;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Bridge.Transfer;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.Jobs;
using MailKit;
using MimeKit;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class BridgeJournalAndResumeTests
{
    private static ClientProjectContext Scope() => new() { CompanyId = "company-1", ProjectId = "project-1" };

    [Fact]
    public async Task ImportResume_AppendIntent_OneMatch_ReconcilesToVerified_ZeroAdditionalAppends()
    {
        string dir = BridgeTestHelpers.CreateTestDir("resume-one-match");
        var (accountStore, _, tgtAccId, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var fakeClient = new BridgeFakeTransferClient();
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        string emlDir = Path.Combine(dir, "eml");
        Directory.CreateDirectory(emlDir);
        var path = BridgeTestHelpers.CreateEmlFile(emlDir, "item1.eml", "Item 1", "Fri, 05 Jan 2024 10:00:00 +0300");

        var manifest = new MimeSourceInspector().BuildEmlFilesManifest(new[] { path }, "INBOX");
        string srcHandle = handleRegistry.RegisterMimeSource(manifest, "eml");

        var previewService = new BridgeImportPreviewService(handleRegistry, accountStore, clientFactory, journal, resolver);
        var preview = await previewService.CreatePreviewAsync(new BridgeImportPreviewRequest
        {
            CompanyId = "company-1",
            ProjectId = "project-1",
            SourceHandle = srcHandle,
            TargetAccountId = tgtAccId,
            SelectedFolders = new List<string> { "INBOX" }
        });

        var plan = journal.GetImportPlan(preview.PreviewId)!;
        string jobId = "job-resume-test";

        // Initialize journal and simulate item in AppendIntent
        var journalState = journal.InitializeImportJournal(jobId, plan);
        var entry = journalState.Entries[plan.Items[0].ItemId];
        string keyword = "bitigmail_testtoken1234567890abcdef";
        entry.Status = BridgeItemStatus.AppendIntent;
        entry.BitigMailKeyword = keyword;
        entry.TargetUidValidity = 12345u;
        entry.ExpectedSha256 = plan.Items[0].CanonicalSha256;
        journal.UpdateImportEntry(jobId, entry);

        // Target server already has this message with matching keyword and identical bytes
        byte[] canonicalBytes = File.ReadAllBytes(path);
        string sha = Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();
        fakeClient.TargetMessages.Add((
            "INBOX",
            500u,
            canonicalBytes,
            sha,
            MessageFlags.None,
            new List<string> { keyword },
            plan.Items[0].PlannedInternalDateUtc));

        var worker = new BridgeImportWorker(
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
            JobKind = "bridge-import",
            PlanId = plan.PlanId,
            ClientContext = Scope(),
            Status = "interrupted"
        };

        await worker.ExecuteAsync(jobRecord, plan);

        Assert.Equal("completed", jobRecord.Status);
        // Reconciled to Verified:
        var updatedEntry = journal.GetImportJournal(jobId)!.Entries[plan.Items[0].ItemId];
        Assert.Equal(BridgeItemStatus.Verified, updatedEntry.Status);
        Assert.Equal(500u, updatedEntry.TargetUid);
        // ZERO new appends!
        Assert.Empty(fakeClient.AppendedMessages);
    }

    [Fact]
    public async Task ImportResume_AppendIntent_ZeroMatches_MarksNeedsAttention_NeverReAppends()
    {
        string dir = BridgeTestHelpers.CreateTestDir("resume-zero-matches");
        var (accountStore, _, tgtAccId, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var fakeClient = new BridgeFakeTransferClient();
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        string emlDir = Path.Combine(dir, "eml");
        Directory.CreateDirectory(emlDir);
        var path = BridgeTestHelpers.CreateEmlFile(emlDir, "item1.eml", "Item 1", "Fri, 05 Jan 2024 10:00:00 +0300");

        var manifest = new MimeSourceInspector().BuildEmlFilesManifest(new[] { path }, "INBOX");
        string srcHandle = handleRegistry.RegisterMimeSource(manifest, "eml");

        var previewService = new BridgeImportPreviewService(handleRegistry, accountStore, clientFactory, journal, resolver);
        var preview = await previewService.CreatePreviewAsync(new BridgeImportPreviewRequest
        {
            CompanyId = "company-1",
            ProjectId = "project-1",
            SourceHandle = srcHandle,
            TargetAccountId = tgtAccId,
            SelectedFolders = new List<string> { "INBOX" }
        });

        var plan = journal.GetImportPlan(preview.PreviewId)!;
        string jobId = "job-zero-matches";

        var journalState = journal.InitializeImportJournal(jobId, plan);
        var entry = journalState.Entries[plan.Items[0].ItemId];
        entry.Status = BridgeItemStatus.AppendIntent;
        entry.BitigMailKeyword = "bitigmail_zerotoken123";
        entry.TargetUidValidity = 12345u;
        entry.ExpectedSha256 = plan.Items[0].CanonicalSha256;
        journal.UpdateImportEntry(jobId, entry);

        // Target server has 0 matching messages (empty target)
        var worker = new BridgeImportWorker(
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
            JobKind = "bridge-import",
            PlanId = plan.PlanId,
            ClientContext = Scope(),
            Status = "interrupted"
        };

        await worker.ExecuteAsync(jobRecord, plan);

        Assert.Equal("failed", jobRecord.Status);
        Assert.Contains("otomatik yeniden ekleme yapılmadı", jobRecord.ErrorMessage);
        var updatedEntry = journal.GetImportJournal(jobId)!.Entries[plan.Items[0].ItemId];
        Assert.Equal(BridgeItemStatus.NeedsAttention, updatedEntry.Status);
        // Zero additional appends!
        Assert.Empty(fakeClient.AppendedMessages);
    }

    [Fact]
    public async Task ImportResume_AppendIntent_MultipleMatches_MarksNeedsAttention()
    {
        string dir = BridgeTestHelpers.CreateTestDir("resume-multi-matches");
        var (accountStore, _, tgtAccId, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var fakeClient = new BridgeFakeTransferClient();
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        string emlDir = Path.Combine(dir, "eml");
        Directory.CreateDirectory(emlDir);
        var path = BridgeTestHelpers.CreateEmlFile(emlDir, "item1.eml", "Item 1", "Fri, 05 Jan 2024 10:00:00 +0300");

        var manifest = new MimeSourceInspector().BuildEmlFilesManifest(new[] { path }, "INBOX");
        string srcHandle = handleRegistry.RegisterMimeSource(manifest, "eml");

        var previewService = new BridgeImportPreviewService(handleRegistry, accountStore, clientFactory, journal, resolver);
        var preview = await previewService.CreatePreviewAsync(new BridgeImportPreviewRequest
        {
            CompanyId = "company-1",
            ProjectId = "project-1",
            SourceHandle = srcHandle,
            TargetAccountId = tgtAccId,
            SelectedFolders = new List<string> { "INBOX" }
        });

        var plan = journal.GetImportPlan(preview.PreviewId)!;
        string jobId = "job-multi-matches";

        var journalState = journal.InitializeImportJournal(jobId, plan);
        var entry = journalState.Entries[plan.Items[0].ItemId];
        string keyword = "bitigmail_multitoken";
        entry.Status = BridgeItemStatus.AppendIntent;
        entry.BitigMailKeyword = keyword;
        entry.TargetUidValidity = 12345u;
        entry.ExpectedSha256 = plan.Items[0].CanonicalSha256;
        journal.UpdateImportEntry(jobId, entry);

        // Target server has 2 matching messages with the same keyword
        byte[] canonicalBytes = File.ReadAllBytes(path);
        string sha = Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();
        fakeClient.TargetMessages.Add(("INBOX", 501u, canonicalBytes, sha, MessageFlags.None, new List<string> { keyword }, plan.Items[0].PlannedInternalDateUtc));
        fakeClient.TargetMessages.Add(("INBOX", 502u, canonicalBytes, sha, MessageFlags.None, new List<string> { keyword }, plan.Items[0].PlannedInternalDateUtc));

        var worker = new BridgeImportWorker(
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
            JobKind = "bridge-import",
            PlanId = plan.PlanId,
            ClientContext = Scope(),
            Status = "interrupted"
        };

        await worker.ExecuteAsync(jobRecord, plan);

        Assert.Equal("failed", jobRecord.Status);
        Assert.Contains("birden çok ileti tespit edildi", jobRecord.ErrorMessage);
        var updatedEntry = journal.GetImportJournal(jobId)!.Entries[plan.Items[0].ItemId];
        Assert.Equal(BridgeItemStatus.NeedsAttention, updatedEntry.Status);
        Assert.Empty(fakeClient.AppendedMessages);
    }

    [Fact]
    public async Task ExportResume_OutputFileDrift_FailsVerification()
    {
        string dir = BridgeTestHelpers.CreateTestDir("export-drift");
        var (accountStore, srcAccId, _, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var fakeClient = new BridgeFakeTransferClient();
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        string outputRoot = Path.Combine(dir, "export-output");
        Directory.CreateDirectory(outputRoot);
        string dirHandle = handleRegistry.RegisterOutputDir(outputRoot);

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("S", "s@example.test"));
        msg.To.Add(new MailboxAddress("R", "r@example.test"));
        msg.Subject = "Drift Test";
        msg.Body = new TextPart("plain") { Text = "Body\r\n" };
        byte[] bytes = ImapSerializationAssumptions.Serialize(msg);
        fakeClient.AddSourceMessage("INBOX", 1, bytes, msg, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None);

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

        var plan = journal.GetExportPlan(preview.PreviewId)!;
        string jobId = "job-drift-test";
        string jobOutputDir = Path.Combine(outputRoot, $"bridge-export-{jobId}");
        Directory.CreateDirectory(jobOutputDir);

        var journalState = journal.InitializeExportJournal(jobId, plan, jobOutputDir);
        var entry = journalState.Entries[plan.Items[0].ItemId];
        entry.Status = BridgeItemStatus.Verified;
        entry.VerifiedSha256 = plan.Items[0].SourceSha256;
        journal.UpdateExportEntry(jobId, entry);

        // Write DRIFTED (tampered) file to output path
        string finalPath = Path.Combine(jobOutputDir, entry.RelativeOutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        await File.WriteAllBytesAsync(finalPath, Encoding.UTF8.GetBytes("Drifted different content!"));

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
            ClientContext = Scope(),
            Status = "interrupted"
        };

        await worker.ExecuteAsync(jobRecord, plan);

        Assert.Contains("diskte değiştirilmiş", jobRecord.ErrorMessage);
    }

    [Fact]
    public async Task JobManager_Idempotency_SameKeySamePlan_ReturnsSameJob()
    {
        string dir = BridgeTestHelpers.CreateTestDir("idemp-same");
        var (accountStore, _, tgtAccId, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var jobManager = new JobManager(dir);
        var fakeClient = new BridgeFakeTransferClient();
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        string emlDir = Path.Combine(dir, "eml");
        Directory.CreateDirectory(emlDir);
        var path = BridgeTestHelpers.CreateEmlFile(emlDir, "msg.eml", "S", "Fri, 05 Jan 2024 10:00:00 +0300");
        var manifest = new MimeSourceInspector().BuildEmlFilesManifest(new[] { path }, "INBOX");
        string srcHandle = handleRegistry.RegisterMimeSource(manifest, "eml");

        var previewService = new BridgeImportPreviewService(handleRegistry, accountStore, clientFactory, journal, resolver);
        var preview = await previewService.CreatePreviewAsync(new BridgeImportPreviewRequest
        {
            CompanyId = "company-1",
            ProjectId = "project-1",
            SourceHandle = srcHandle,
            TargetAccountId = tgtAccId,
            SelectedFolders = new List<string> { "INBOX" }
        });
        var plan = journal.GetImportPlan(preview.PreviewId)!;

        var job1 = jobManager.StartBridgeImportJob(
            plan,
            "idemp-key-A",
            Scope(),
            handleRegistry,
            accountStore,
            clientFactory,
            journal,
            resolver);

        // Calling again with same key and same plan returns same job instance
        var job2 = jobManager.StartBridgeImportJob(
            plan,
            "idemp-key-A",
            Scope(),
            handleRegistry,
            accountStore,
            clientFactory,
            journal,
            resolver);

        Assert.Equal(job1.JobId, job2.JobId);
    }

    [Fact]
    public async Task JobManager_Idempotency_SameKeyDifferentPlan_Throws409Conflict()
    {
        string dir = BridgeTestHelpers.CreateTestDir("idemp-conflict");
        var (accountStore, _, tgtAccId, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var jobManager = new JobManager(dir);
        var fakeClient = new BridgeFakeTransferClient();
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        string emlDir = Path.Combine(dir, "eml");
        Directory.CreateDirectory(emlDir);
        var path1 = BridgeTestHelpers.CreateEmlFile(emlDir, "msg1.eml", "S1", "Fri, 05 Jan 2024 10:00:00 +0300");
        var path2 = BridgeTestHelpers.CreateEmlFile(emlDir, "msg2.eml", "S2", "Sat, 06 Jan 2024 10:00:00 +0300");

        var manifest1 = new MimeSourceInspector().BuildEmlFilesManifest(new[] { path1 }, "INBOX");
        var manifest2 = new MimeSourceInspector().BuildEmlFilesManifest(new[] { path2 }, "INBOX");
        string srcHandle1 = handleRegistry.RegisterMimeSource(manifest1, "eml1");
        string srcHandle2 = handleRegistry.RegisterMimeSource(manifest2, "eml2");

        var previewService = new BridgeImportPreviewService(handleRegistry, accountStore, clientFactory, journal, resolver);
        var prev1 = await previewService.CreatePreviewAsync(new BridgeImportPreviewRequest
        {
            CompanyId = "company-1",
            ProjectId = "project-1",
            SourceHandle = srcHandle1,
            TargetAccountId = tgtAccId,
            SelectedFolders = new List<string> { "INBOX" }
        });
        var prev2 = await previewService.CreatePreviewAsync(new BridgeImportPreviewRequest
        {
            CompanyId = "company-1",
            ProjectId = "project-1",
            SourceHandle = srcHandle2,
            TargetAccountId = tgtAccId,
            SelectedFolders = new List<string> { "INBOX" }
        });

        var plan1 = journal.GetImportPlan(prev1.PreviewId)!;
        var plan2 = journal.GetImportPlan(prev2.PreviewId)!;

        jobManager.StartBridgeImportJob(
            plan1,
            "conflict-key",
            Scope(),
            handleRegistry,
            accountStore,
            clientFactory,
            journal,
            resolver);

        // Same idempotency key with different plan throws InvalidOperationException (typed 409)
        var ex = Assert.Throws<InvalidOperationException>(() =>
            jobManager.StartBridgeImportJob(
                plan2,
                "conflict-key",
                Scope(),
                handleRegistry,
                accountStore,
                clientFactory,
                journal,
                resolver));

        Assert.Contains("daha önce farklı bir köprü içe aktarım planı", ex.Message);
    }

    [Fact]
    public async Task JobManager_Idempotency_SamePlanDifferentKey_DedupesToExistingJob()
    {
        string dir = BridgeTestHelpers.CreateTestDir("idemp-dedupe");
        var (accountStore, _, tgtAccId, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var jobManager = new JobManager(dir);
        var fakeClient = new BridgeFakeTransferClient();
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        string emlDir = Path.Combine(dir, "eml");
        Directory.CreateDirectory(emlDir);
        var path = BridgeTestHelpers.CreateEmlFile(emlDir, "msg.eml", "S", "Fri, 05 Jan 2024 10:00:00 +0300");
        var manifest = new MimeSourceInspector().BuildEmlFilesManifest(new[] { path }, "INBOX");
        string srcHandle = handleRegistry.RegisterMimeSource(manifest, "eml");

        var previewService = new BridgeImportPreviewService(handleRegistry, accountStore, clientFactory, journal, resolver);
        var prev = await previewService.CreatePreviewAsync(new BridgeImportPreviewRequest
        {
            CompanyId = "company-1",
            ProjectId = "project-1",
            SourceHandle = srcHandle,
            TargetAccountId = tgtAccId,
            SelectedFolders = new List<string> { "INBOX" }
        });
        var plan = journal.GetImportPlan(prev.PreviewId)!;

        var job1 = jobManager.StartBridgeImportJob(
            plan,
            "first-key",
            Scope(),
            handleRegistry,
            accountStore,
            clientFactory,
            journal,
            resolver);

        // Different key but identical plan -> returns existing job (no duplicate job created)
        var job2 = jobManager.StartBridgeImportJob(
            plan,
            "second-different-key",
            Scope(),
            handleRegistry,
            accountStore,
            clientFactory,
            journal,
            resolver);

        Assert.Equal(job1.JobId, job2.JobId);
    }
}
