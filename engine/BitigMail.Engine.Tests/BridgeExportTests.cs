using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Bridge;
using BitigMail.Engine.Imap;
using BitigMail.Engine.Imap.Transfer;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Bridge.Transfer;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.Imap.Transfer;
using BitigMail.LocalHost.Jobs;
using MailKit;
using MimeKit;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class BridgeExportTests
{
    private static ClientProjectContext Scope() => new() { CompanyId = "company-1", ProjectId = "project-1" };

    [Fact]
    public async Task Export_DeletedFlag_FailsPreflight_FailClosed()
    {
        string dir = BridgeTestHelpers.CreateTestDir("export-deleted-block");
        var (accountStore, srcAccId, _, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var fakeClient = new BridgeFakeTransferClient();
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        string outputRoot = Path.Combine(dir, "export-output");
        Directory.CreateDirectory(outputRoot);
        string dirHandle = handleRegistry.RegisterOutputDir(outputRoot);

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("Sender", "sender@example.test"));
        msg.To.Add(new MailboxAddress("Recipient", "rec@example.test"));
        msg.Subject = "Deleted Msg";
        msg.Body = new TextPart("plain") { Text = "Body\r\n" };
        byte[] rawBytes = ImapSerializationAssumptions.Serialize(msg);

        fakeClient.AddSourceMessage(
            "INBOX",
            1,
            rawBytes,
            msg,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            MessageFlags.Deleted);

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

        Assert.False(preview.CanTransfer);
        Assert.Equal(1, preview.DeletedExcludedCount);
        Assert.Contains("silindi (\\Deleted)", preview.BlockerReason);
    }

    [Fact]
    public async Task Export_EmlTree_12Items_SafeFolderNames_AndGeneratesSidecarManifest()
    {
        string dir = BridgeTestHelpers.CreateTestDir("export-eml-tree");
        var (accountStore, srcAccId, _, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var jobManager = new JobManager(dir);
        var fakeClient = new BridgeFakeTransferClient();
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        // Register output directory
        string outputRoot = Path.Combine(dir, "export-output");
        Directory.CreateDirectory(outputRoot);
        string dirHandle = handleRegistry.RegisterOutputDir(outputRoot);

        // Setup 12 source messages in fakeClient across INBOX and Turkish folder "Gelen Kutusu"
        for (uint i = 1; i <= 12; i++)
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress($"Sender {i}", $"sender{i}@example.test"));
            msg.To.Add(new MailboxAddress($"Recipient {i}", $"rec{i}@example.test"));
            msg.Subject = $"Export Msg {i}";
            msg.MessageId = $"<msg-{i}@example.test>";
            msg.Body = new TextPart("plain") { Text = $"Body content for export item {i}\r\n" };
            byte[] rawBytes = ImapSerializationAssumptions.Serialize(msg);

            var flags = (i % 2 == 0) ? MessageFlags.Seen : MessageFlags.None;

            fakeClient.AddSourceMessage(
                "INBOX",
                i,
                rawBytes,
                msg,
                DateTimeOffset.Parse($"2024-01-{i:D2}T10:00:00+03:00"),
                DateTimeOffset.Parse($"2024-01-{i:D2}T07:00:00Z"),
                flags);
        }

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
        Assert.Equal(12, preview.TotalSourceItems);
        Assert.Equal(0, preview.DeletedExcludedCount);
        Assert.Equal(12, preview.EligibleItemsCount);

        var plan = journal.GetExportPlan(preview.PreviewId)!;
        Assert.Equal(12, plan.Items.Count);

        // Safe fixed folder keys only (e.g. fld_001), safe relative output paths
        Assert.All(plan.Items, item =>
        {
            Assert.StartsWith("fld_", item.FolderKey);
            Assert.Matches(@"^fld_\d{3}/msg_\d{8}\.eml$", item.RelativeOutputPath.Replace('\\', '/'));
        });

        // Execute export job
        var job = jobManager.StartBridgeExportJob(
            plan,
            "idemp-export-1",
            Scope(),
            handleRegistry,
            accountStore,
            clientFactory,
            journal,
            resolver);

        await BridgeTestHelpers.WaitForJobCompletionAsync(jobManager, job.JobId);

        var finished = jobManager.GetJob(job.JobId);
        Assert.NotNull(finished);
        Assert.Equal("completed", finished.Status);
        Assert.Equal(12, finished.BridgeTransfer?.TotalVerified);

        string jobOutputDir = finished.OutputPath!;
        Assert.True(Directory.Exists(jobOutputDir));

        // Verify sidecar manifest.json
        string manifestPath = Path.Combine(jobOutputDir, "manifest.json");
        Assert.True(File.Exists(manifestPath));
        string manifestJson = await File.ReadAllTextAsync(manifestPath);

        // Verify no secret leak in manifest
        Assert.DoesNotContain("source-password", manifestJson);
        Assert.DoesNotContain("target-password", manifestJson);

        using var doc = JsonDocument.Parse(manifestJson);
        var root = doc.RootElement;
        Assert.Equal("1.0", root.GetProperty("manifestVersion").GetString());
        Assert.Equal("eml-tree", root.GetProperty("targetFormat").GetString());
        Assert.Equal(12, root.GetProperty("items").GetArrayLength());

        // Verify exported .eml files exist and match source SHA-256
        foreach (var item in plan.Items)
        {
            string filePath = Path.Combine(jobOutputDir, item.RelativeOutputPath);
            Assert.True(File.Exists(filePath));
            byte[] exportedBytes = await File.ReadAllBytesAsync(filePath);
            string exportedSha = Convert.ToHexString(SHA256.HashData(exportedBytes)).ToLowerInvariant();
            Assert.Equal(item.SourceSha256, exportedSha);
        }
    }

    [Fact]
    public async Task Export_Mboxrd_FromEscaping_AndAccurateRoundtrip()
    {
        string dir = BridgeTestHelpers.CreateTestDir("export-mboxrd");
        var (accountStore, srcAccId, _, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var jobManager = new JobManager(dir);
        var fakeClient = new BridgeFakeTransferClient();
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        string outputRoot = Path.Combine(dir, "export-output");
        Directory.CreateDirectory(outputRoot);
        string dirHandle = handleRegistry.RegisterOutputDir(outputRoot);

        // Create message with From line in body
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("Sender", "sender@example.test"));
        msg.To.Add(new MailboxAddress("Recipient", "rec@example.test"));
        msg.Subject = "From escaping test";
        msg.Body = new TextPart("plain") { Text = "Line 1\r\nFrom line in body\r\n>From double line\r\nEnd line\r\n" };
        byte[] rawBytes = ImapSerializationAssumptions.Serialize(msg);

        fakeClient.AddSourceMessage(
            "INBOX",
            1,
            rawBytes,
            msg,
            DateTimeOffset.Parse("2024-01-01T10:00:00+03:00"),
            DateTimeOffset.Parse("2024-01-01T07:00:00Z"),
            MessageFlags.None);

        var previewService = new BridgeExportPreviewService(handleRegistry, accountStore, clientFactory, journal, resolver);
        var preview = await previewService.CreatePreviewAsync(new BridgeExportPreviewRequest
        {
            CompanyId = "company-1",
            ProjectId = "project-1",
            SourceAccountId = srcAccId,
            TargetDirHandle = dirHandle,
            TargetFormat = "mboxrd",
            SelectedFolders = new List<string> { "INBOX" }
        });

        var plan = journal.GetExportPlan(preview.PreviewId)!;
        var job = jobManager.StartBridgeExportJob(
            plan,
            "export-mbox-key",
            Scope(),
            handleRegistry,
            accountStore,
            clientFactory,
            journal,
            resolver);

        await BridgeTestHelpers.WaitForJobCompletionAsync(jobManager, job.JobId);

        var finished = jobManager.GetJob(job.JobId)!;
        Assert.Equal("completed", finished.Status);

        string mboxFile = Path.Combine(finished.OutputPath!, "fld_001.mbox");
        Assert.True(File.Exists(mboxFile));

        // Roundtrip check with MboxrdRecordReader
        using var fs = File.OpenRead(mboxFile);
        var records = MboxrdRecordReader.EnumerateRecords(fs).ToList();
        Assert.Single(records);
        Assert.Equal(rawBytes, records[0].RawMimeBytes);
    }

    [Fact]
    public async Task Export_TamperedJobOutputDir_OutsideParent_RejectedFailClosed()
    {
        string dir = BridgeTestHelpers.CreateTestDir("export-escape");
        var (accountStore, srcAccId, _, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var jobManager = new JobManager(dir);
        var fakeClient = new BridgeFakeTransferClient();
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        string outputRoot = Path.Combine(dir, "export-output");
        Directory.CreateDirectory(outputRoot);
        string dirHandle = handleRegistry.RegisterOutputDir(outputRoot);

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("S", "s@example.test"));
        msg.To.Add(new MailboxAddress("R", "r@example.test"));
        msg.Subject = "Test";
        msg.Body = new TextPart("plain") { Text = "Body\r\n" };
        fakeClient.AddSourceMessage("INBOX", 1, ImapSerializationAssumptions.Serialize(msg), msg, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None);

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

        // Initialize journal with tampered JobOutputDir pointing outside outputRoot
        string tamperedEscapeDir = Path.GetFullPath(Path.Combine(dir, "escaped-outside-target"));
        journal.InitializeExportJournal("tampered-job", plan, tamperedEscapeDir);

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
            JobId = "tampered-job",
            JobKind = "bridge-export",
            PlanId = plan.PlanId,
            ClientContext = Scope()
        };

        await worker.ExecuteAsync(jobRecord, plan);

        Assert.Equal("failed", jobRecord.Status);
        Assert.Contains("güvenlik ihlali", jobRecord.ErrorMessage);
    }

    [Fact]
    public async Task Export_HandleFreeRestart_SucceedsUsingPlanTargetDirectoryPath()
    {
        string dir = BridgeTestHelpers.CreateTestDir("export-handle-free");
        var (accountStore, srcAccId, _, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var initialRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var jobManager = new JobManager(dir);
        var fakeClient = new BridgeFakeTransferClient();
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        string outputRoot = Path.Combine(dir, "export-output");
        Directory.CreateDirectory(outputRoot);
        string dirHandle = initialRegistry.RegisterOutputDir(outputRoot);

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("S", "s@example.test"));
        msg.To.Add(new MailboxAddress("R", "r@example.test"));
        msg.Subject = "Test";
        msg.Body = new TextPart("plain") { Text = "Body\r\n" };
        fakeClient.AddSourceMessage("INBOX", 1, ImapSerializationAssumptions.Serialize(msg), msg, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None);

        var previewService = new BridgeExportPreviewService(initialRegistry, accountStore, clientFactory, journal, resolver);
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
        Assert.NotNull(plan.TargetDirectoryPath);

        // Empty registry simulating restart
        var emptyRegistry = new FileHandleRegistry();

        var job = jobManager.StartBridgeExportJob(
            plan,
            "export-restart-key",
            Scope(),
            emptyRegistry,
            accountStore,
            clientFactory,
            journal,
            resolver);

        await BridgeTestHelpers.WaitForJobCompletionAsync(jobManager, job.JobId);

        var finished = jobManager.GetJob(job.JobId)!;
        Assert.Equal("completed", finished.Status);
        Assert.Equal(1, finished.BridgeTransfer?.TotalVerified);
    }

    [Fact]
    public async Task Export_Mboxrd_NormalExactBytes_AndMimeEndings_MetadataPreserved()
    {
        string dir = BridgeTestHelpers.CreateTestDir("export-mbox-endings");
        var (accountStore, srcAccId, _, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var fakeClient = new BridgeFakeTransferClient();
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        string outputRoot = Path.Combine(dir, "export-output");
        Directory.CreateDirectory(outputRoot);
        string dirHandle = handleRegistry.RegisterOutputDir(outputRoot);

        // Msg 1: Standard CRLF ending
        var msg1 = new MimeMessage();
        msg1.From.Add(new MailboxAddress("Alice", "alice@example.test"));
        msg1.To.Add(new MailboxAddress("Bob", "bob@example.test"));
        msg1.Subject = "With standard newline";
        msg1.Body = new TextPart("plain") { Text = "Line 1\r\nLine 2\r\n" };
        byte[] raw1 = ImapSerializationAssumptions.Serialize(msg1);

        // Msg 2: No terminal newline
        byte[] raw2 = Encoding.UTF8.GetBytes("From: Carol <carol@example.test>\r\nTo: Dave <dave@example.test>\r\nSubject: No terminal newline\r\nDate: Tue, 02 Jan 2024 10:00:00 +0300\r\n\r\nLine without trailing newline");
        var mime2 = MimeMessage.Load(new MemoryStream(raw2));

        var date1 = DateTimeOffset.Parse("2024-01-01T10:00:00+03:00");
        var date2 = DateTimeOffset.Parse("2024-01-02T10:00:00+03:00");

        fakeClient.AddSourceMessage("INBOX", 1, raw1, msg1, date1, date1, MessageFlags.Seen);
        fakeClient.AddSourceMessage("INBOX", 2, raw2, mime2, date2, date2, MessageFlags.Flagged);

        var previewService = new BridgeExportPreviewService(handleRegistry, accountStore, clientFactory, journal, resolver);
        var preview = await previewService.CreatePreviewAsync(new BridgeExportPreviewRequest
        {
            CompanyId = "company-1",
            ProjectId = "project-1",
            SourceAccountId = srcAccId,
            TargetDirHandle = dirHandle,
            TargetFormat = "mboxrd",
            SelectedFolders = new List<string> { "INBOX" }
        });

        var plan = journal.GetExportPlan(preview.PreviewId)!;
        Assert.Equal(2, plan.Items.Count);

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
            JobId = "mbox-endings-job",
            JobKind = "bridge-export",
            PlanId = plan.PlanId,
            ClientContext = Scope()
        };

        await worker.ExecuteAsync(jobRecord, plan);

        Assert.Equal("completed", jobRecord.Status);
        Assert.Equal(2, jobRecord.ItemsWritten);

        string mboxPath = Path.Combine(jobRecord.OutputPath!, "fld_001.mbox");
        Assert.True(File.Exists(mboxPath));

        // Read records using MboxrdRecordReader
        using (var fs = File.OpenRead(mboxPath))
        {
            var records = MboxrdRecordReader.EnumerateRecords(fs).ToList();
            Assert.Equal(2, records.Count);

            // Record 1: Exact bytes preserved (had terminal CRLF)
            Assert.Equal(raw1, records[0].RawMimeBytes);
            // Record 2: Original bytes preserved with terminal newline added per MBOXRD spec
            Assert.Equal(raw2, records[1].RawMimeBytes[..raw2.Length]);
            Assert.Equal((byte)'\r', records[1].RawMimeBytes[^2]);
            Assert.Equal((byte)'\n', records[1].RawMimeBytes[^1]);
        }

        // Sidecar manifest verification
        string manifestPath = Path.Combine(jobRecord.OutputPath!, "manifest.json");
        Assert.True(File.Exists(manifestPath));
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        {
            var items = doc.RootElement.GetProperty("items");
            Assert.Equal(2, items.GetArrayLength());

            var item1 = items[0];
            Assert.Equal(1u, item1.GetProperty("sourceUid").GetUInt32());
            Assert.False(item1.GetProperty("addedTerminalNewline").GetBoolean());
            Assert.Equal(raw1.Length, item1.GetProperty("originalLength").GetInt32());

            var item2 = items[1];
            Assert.Equal(2u, item2.GetProperty("sourceUid").GetUInt32());
            Assert.True(item2.GetProperty("addedTerminalNewline").GetBoolean());
            Assert.Equal(raw2.Length, item2.GetProperty("originalLength").GetInt32());
        }

        // Verify staging directory is cleaned up
        string stagingDir = Path.Combine(jobRecord.OutputPath!, ".staging");
        Assert.False(Directory.Exists(stagingDir));
    }

    [Fact]
    public async Task Export_Mboxrd_CorruptStagedContent_RejectedBeforePublication()
    {
        string dir = BridgeTestHelpers.CreateTestDir("export-mbox-corrupt");
        var (accountStore, srcAccId, _, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);

        string outputRoot = Path.Combine(dir, "export-output");
        Directory.CreateDirectory(outputRoot);
        string dirHandle = handleRegistry.RegisterOutputDir(outputRoot);

        var msg1 = new MimeMessage();
        msg1.From.Add(new MailboxAddress("Sender 1", "s1@example.test"));
        msg1.To.Add(new MailboxAddress("Recipient 1", "r1@example.test"));
        msg1.Subject = "Msg 1";
        msg1.Body = new TextPart("plain") { Text = "Body 1\r\n" };
        byte[] raw1 = ImapSerializationAssumptions.Serialize(msg1);

        var msg2 = new MimeMessage();
        msg2.From.Add(new MailboxAddress("Sender 2", "s2@example.test"));
        msg2.To.Add(new MailboxAddress("Recipient 2", "r2@example.test"));
        msg2.Subject = "Msg 2";
        msg2.Body = new TextPart("plain") { Text = "Body 2\r\n" };
        byte[] raw2 = ImapSerializationAssumptions.Serialize(msg2);

        var interceptingClient = new InterceptingFakeTransferClient();
        interceptingClient.AddSourceMessage("INBOX", 1, raw1, msg1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None);
        interceptingClient.AddSourceMessage("INBOX", 2, raw2, msg2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None);

        var clientFactory = new BridgeFakeClientFactory(interceptingClient);

        var previewService = new BridgeExportPreviewService(handleRegistry, accountStore, clientFactory, journal, resolver);
        var preview = await previewService.CreatePreviewAsync(new BridgeExportPreviewRequest
        {
            CompanyId = "company-1",
            ProjectId = "project-1",
            SourceAccountId = srcAccId,
            TargetDirHandle = dirHandle,
            TargetFormat = "mboxrd",
            SelectedFolders = new List<string> { "INBOX" }
        });

        var plan = journal.GetExportPlan(preview.PreviewId)!;

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
            JobId = "mbox-corrupt-job",
            JobKind = "bridge-export",
            PlanId = plan.PlanId,
            ClientContext = Scope()
        };

        string jobOutputDir = Path.Combine(outputRoot, $"bridge-export-{jobRecord.JobId}");

        // Intercept during export execution: when fetching item 2, tamper with item 1's staged file
        interceptingClient.OnFetchMessage = async (folder, uid) =>
        {
            if (uid == 2)
            {
                string stagedMsg1 = Path.Combine(jobOutputDir, ".staging", "fld_001", "msg_00000001.raw");
                if (File.Exists(stagedMsg1))
                {
                    await File.WriteAllBytesAsync(stagedMsg1, Encoding.UTF8.GetBytes("CORRUPTED STAGED CONTENT"));
                }
            }
        };

        await worker.ExecuteAsync(jobRecord, plan);

        Assert.Equal("failed", jobRecord.Status);
        Assert.Contains("montaj öncesinde uyuşmuyor", jobRecord.ErrorMessage);

        // The final MBOX file must NOT exist (rejected before final publication)
        string finalMboxPath = Path.Combine(jobOutputDir, "fld_001.mbox");
        Assert.False(File.Exists(finalMboxPath));

        // Journal entry must not be Verified
        var journalState = journal.GetExportJournal(jobRecord.JobId)!;
        Assert.NotEqual(BridgeItemStatus.Verified, journalState.Entries[plan.Items[0].ItemId].Status);
    }

    [Fact]
    public async Task Export_Mboxrd_CorruptExistingStagedContent_FailsAtStage1()
    {
        string dir = BridgeTestHelpers.CreateTestDir("export-mbox-staged-corrupt");
        var (accountStore, srcAccId, _, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var fakeClient = new BridgeFakeTransferClient();
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        string outputRoot = Path.Combine(dir, "export-output");
        Directory.CreateDirectory(outputRoot);
        string dirHandle = handleRegistry.RegisterOutputDir(outputRoot);

        var msg1 = new MimeMessage();
        msg1.From.Add(new MailboxAddress("Sender", "s@example.test"));
        msg1.To.Add(new MailboxAddress("Recipient", "r@example.test"));
        msg1.Subject = "Msg 1";
        msg1.Body = new TextPart("plain") { Text = "Body 1\r\n" };
        byte[] raw1 = ImapSerializationAssumptions.Serialize(msg1);
        fakeClient.AddSourceMessage("INBOX", 1, raw1, msg1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None);

        var previewService = new BridgeExportPreviewService(handleRegistry, accountStore, clientFactory, journal, resolver);
        var preview = await previewService.CreatePreviewAsync(new BridgeExportPreviewRequest
        {
            CompanyId = "company-1",
            ProjectId = "project-1",
            SourceAccountId = srcAccId,
            TargetDirHandle = dirHandle,
            TargetFormat = "mboxrd",
            SelectedFolders = new List<string> { "INBOX" }
        });

        var plan = journal.GetExportPlan(preview.PreviewId)!;

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
            JobId = "mbox-stage1-corrupt",
            JobKind = "bridge-export",
            PlanId = plan.PlanId,
            ClientContext = Scope()
        };

        string jobOutputDir = Path.Combine(outputRoot, $"bridge-export-{jobRecord.JobId}");
        string stagingDir = Path.Combine(jobOutputDir, ".staging", "fld_001");
        Directory.CreateDirectory(stagingDir);
        await File.WriteAllBytesAsync(Path.Combine(stagingDir, "msg_00000001.raw"), Encoding.UTF8.GetBytes("BAD STAGED CONTENT"));

        await worker.ExecuteAsync(jobRecord, plan);

        Assert.Equal("failed", jobRecord.Status);
        Assert.Contains("Hazırlık dosyasının (staging) hash değeri uyuşmuyor", jobRecord.ErrorMessage);
        Assert.False(File.Exists(Path.Combine(jobOutputDir, "fld_001.mbox")));
    }

    [Fact]
    public async Task Export_Mboxrd_CancellationAndFailure_SameJobRecovery_Succeeds()
    {
        string dir = BridgeTestHelpers.CreateTestDir("export-mbox-resume");
        var (accountStore, srcAccId, _, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);

        string outputRoot = Path.Combine(dir, "export-output");
        Directory.CreateDirectory(outputRoot);
        string dirHandle = handleRegistry.RegisterOutputDir(outputRoot);

        var msg1 = new MimeMessage();
        msg1.From.Add(new MailboxAddress("Sender 1", "s1@example.test"));
        msg1.To.Add(new MailboxAddress("Recipient 1", "r1@example.test"));
        msg1.Subject = "Msg 1";
        msg1.Body = new TextPart("plain") { Text = "Body 1\r\n" };
        byte[] raw1 = ImapSerializationAssumptions.Serialize(msg1);

        var msg2 = new MimeMessage();
        msg2.From.Add(new MailboxAddress("Sender 2", "s2@example.test"));
        msg2.To.Add(new MailboxAddress("Recipient 2", "r2@example.test"));
        msg2.Subject = "Msg 2";
        msg2.Body = new TextPart("plain") { Text = "Body 2\r\n" };
        byte[] raw2 = ImapSerializationAssumptions.Serialize(msg2);

        var cancelClient = new InterceptingFakeTransferClient();
        cancelClient.AddSourceMessage("INBOX", 1, raw1, msg1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None);
        cancelClient.AddSourceMessage("INBOX", 2, raw2, msg2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None);

        var clientFactory = new BridgeFakeClientFactory(cancelClient);

        var previewService = new BridgeExportPreviewService(handleRegistry, accountStore, clientFactory, journal, resolver);
        var preview = await previewService.CreatePreviewAsync(new BridgeExportPreviewRequest
        {
            CompanyId = "company-1",
            ProjectId = "project-1",
            SourceAccountId = srcAccId,
            TargetDirHandle = dirHandle,
            TargetFormat = "mboxrd",
            SelectedFolders = new List<string> { "INBOX" }
        });

        var plan = journal.GetExportPlan(preview.PreviewId)!;

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
            JobId = "mbox-resume-job",
            JobKind = "bridge-export",
            PlanId = plan.PlanId,
            ClientContext = Scope()
        };

        using var cts = new CancellationTokenSource();
        cancelClient.OnFetchMessage = (folder, uid) =>
        {
            if (uid == 2)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        };

        // First run: canceled when fetching item 2
        await worker.ExecuteAsync(jobRecord, plan, cts.Token);

        Assert.Equal("interrupted", jobRecord.Status);
        string jobOutputDir = jobRecord.OutputPath!;
        string finalMbox = Path.Combine(jobOutputDir, "fld_001.mbox");
        Assert.False(File.Exists(finalMbox));

        // msg 1 was staged and is preserved
        string stagedMsg1 = Path.Combine(jobOutputDir, ".staging", "fld_001", "msg_00000001.raw");
        Assert.True(File.Exists(stagedMsg1));

        // Second run: resume the exact same job without cancellation
        cancelClient.OnFetchMessage = null; // do not cancel
        await worker.ExecuteAsync(jobRecord, plan, CancellationToken.None);

        Assert.Equal("completed", jobRecord.Status);
        Assert.Equal(2, jobRecord.ItemsWritten);
        Assert.True(File.Exists(finalMbox));

        // Staging directory cleaned up after successful completion
        Assert.False(Directory.Exists(Path.Combine(jobOutputDir, ".staging")));

        // Verify sidecar manifest
        string manifestPath = Path.Combine(jobOutputDir, "manifest.json");
        Assert.True(File.Exists(manifestPath));
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        Assert.Equal(2, doc.RootElement.GetProperty("items").GetArrayLength());
    }
}

file sealed class InterceptingFakeTransferClient : BridgeFakeTransferClient, IImapTransferClient
{
    public Func<string, uint, Task>? OnFetchMessage { get; set; }

    async Task<ImapSourceMessageSummary?> IImapTransferClient.FetchSingleSourceMessageAsync(string folderPath, uint uid, CancellationToken ct)
    {
        if (OnFetchMessage != null)
        {
            await OnFetchMessage(folderPath, uid);
        }
        return await base.FetchSingleSourceMessageAsync(folderPath, uid, ct);
    }
}
