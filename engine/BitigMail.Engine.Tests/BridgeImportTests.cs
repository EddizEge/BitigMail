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

public sealed class BridgeImportTests
{
    private static ClientProjectContext Scope() => new() { CompanyId = "company-1", ProjectId = "project-1" };

    [Fact]
    public async Task Import_EmlTree_12Items_Duplicates_Filter3to1_MimeKitEquality_AndExecution()
    {
        string dir = BridgeTestHelpers.CreateTestDir("import-eml-12");
        var (accountStore, srcAccId, tgtAccId, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var jobManager = new JobManager(dir);
        var fakeClient = new BridgeFakeTransferClient();
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        // 1. Create EML Tree with 12 physical items across folders (including Turkish folder names)
        string emlTreeRoot = Path.Combine(dir, "eml-source");
        string inboxDir = Path.Combine(emlTreeRoot, "INBOX");
        string arsivDir = Path.Combine(emlTreeRoot, "Arşiv");
        string sentDir = Path.Combine(emlTreeRoot, "Sent");
        Directory.CreateDirectory(inboxDir);
        Directory.CreateDirectory(arsivDir);
        Directory.CreateDirectory(sentDir);

        // INBOX: 6 items (3 in date range 2024-01-05..2024-01-07, 1 outside 2024-02-15 => filter 3/1, 1 duplicate content, 1 same Message-ID)
        var msg1Path = BridgeTestHelpers.CreateEmlFile(inboxDir, "msg01.eml", "Subject 1", "Fri, 05 Jan 2024 10:00:00 +0300", "<id-1@example.test>", "Body 1\r\n");
        var msg2Path = BridgeTestHelpers.CreateEmlFile(inboxDir, "msg02.eml", "Subject 2", "Sat, 06 Jan 2024 12:00:00 +0300", "<id-2@example.test>", "Body 2\r\n");
        var msg3Path = BridgeTestHelpers.CreateEmlFile(inboxDir, "msg03.eml", "Subject 3", "Sun, 07 Jan 2024 14:00:00 +0300", "<id-3@example.test>", "Body 3\r\n");
        var msg4Path = BridgeTestHelpers.CreateEmlFile(inboxDir, "msg04.eml", "Subject 4 Outside", "Thu, 15 Feb 2024 10:00:00 +0300", "<id-4@example.test>", "Body 4\r\n");
        // msg05: duplicate raw bytes of msg01
        var msg5Path = Path.Combine(inboxDir, "msg05.eml");
        File.Copy(msg1Path, msg5Path);
        // msg06: same Message-ID as msg02 but distinct body
        var msg6Path = BridgeTestHelpers.CreateEmlFile(inboxDir, "msg06.eml", "Subject 2 Distinct Body", "Sun, 07 Jan 2024 16:00:00 +0300", "<id-2@example.test>", "Body 2 Distinct Content\r\n");

        // Arşiv: 3 items
        BridgeTestHelpers.CreateEmlFile(arsivDir, "msg07.eml", "Subject 7", "Mon, 08 Jan 2024 10:00:00 +0300", "<id-7@example.test>", "Body 7\r\n");
        BridgeTestHelpers.CreateEmlFile(arsivDir, "msg08.eml", "Subject 8", "Tue, 09 Jan 2024 10:00:00 +0300", "<id-8@example.test>", "Body 8\r\n");
        BridgeTestHelpers.CreateEmlFile(arsivDir, "msg09.eml", "Subject 9", "Wed, 10 Jan 2024 10:00:00 +0300", "<id-9@example.test>", "Body 9\r\n");

        // Sent: 3 items (msg12 has NO date header -> date fallback test)
        BridgeTestHelpers.CreateEmlFile(sentDir, "msg10.eml", "Subject 10", "Thu, 11 Jan 2024 10:00:00 +0300", "<id-10@example.test>", "Body 10\r\n");
        BridgeTestHelpers.CreateEmlFile(sentDir, "msg11.eml", "Subject 11", "Fri, 12 Jan 2024 10:00:00 +0300", "<id-11@example.test>", "Body 11\r\n");
        BridgeTestHelpers.CreateEmlFile(sentDir, "msg12.eml", "Subject 12 No Date", null, "<id-12@example.test>", "Body 12\r\n");

        // Verify total 12 physical files
        var inspector = new MimeSourceInspector();
        var manifest = inspector.BuildEmlDirectoryManifest(emlTreeRoot);
        Assert.Equal(12, manifest.Entries.Count);

        string srcHandle = handleRegistry.RegisterMimeSource(manifest, "eml-source-display");

        // 2. Preview with filter on INBOX: 2024-01-05 to 2024-01-07
        var previewService = new BridgeImportPreviewService(handleRegistry, accountStore, clientFactory, journal, resolver);
        var previewRequest = new BridgeImportPreviewRequest
        {
            CompanyId = "company-1",
            ProjectId = "project-1",
            SourceHandle = srcHandle,
            TargetAccountId = tgtAccId,
            SelectedFolders = new List<string> { "INBOX" },
            StartDate = "2024-01-05",
            EndDate = "2024-01-07"
        };

        var preview = await previewService.CreatePreviewAsync(previewRequest);
        Assert.True(preview.CanTransfer);
        Assert.Equal(12, preview.TotalSourceItems);
        // In INBOX: msg01, msg02, msg03, msg05 (copy of 1), msg06 are within Jan 5..Jan 7; msg04 (Feb 15) is excluded.
        // Filter excludes msg04 plus non-selected folders (6 items)
        Assert.Equal(5, preview.EligibleItemsCount);
        Assert.Equal(7, preview.ExcludedCount);

        // 3. Verify MimeKit serialization equals BridgeMimeBytePolicy canonical bytes
        var plan = journal.GetImportPlan(preview.PreviewId);
        Assert.NotNull(plan);
        Assert.Equal(5, plan.Items.Count);

        foreach (var item in plan.Items)
        {
            byte[] rawBytes = await File.ReadAllBytesAsync(item.SourceCanonicalPath);
            var canonical = BridgeMimeBytePolicy.CanonicalizeForImap(rawBytes);
            using var ms = new MemoryStream(canonical.Bytes);
            var msg = MimeMessage.Load(ms);
            byte[] serialized = ImapSerializationAssumptions.Serialize(msg);
            Assert.Equal(canonical.Bytes, serialized);
            Assert.Equal(canonical.CanonicalSha256, item.CanonicalSha256);
        }

        // Distinct own-baseline canonical hashes: msg01 and msg02 have distinct hashes
        var item1 = plan.Items.First(i => i.SourceRelativePath.Contains("msg01.eml"));
        var item2 = plan.Items.First(i => i.SourceRelativePath.Contains("msg02.eml"));
        Assert.NotEqual(item1.CanonicalSha256, item2.CanonicalSha256);

        // Duplicate item retention: msg01 and msg05 have same canonical hash but distinct item IDs
        var item5 = plan.Items.First(i => i.SourceRelativePath.Contains("msg05.eml"));
        Assert.NotEqual(item1.ItemId, item5.ItemId);
        Assert.Equal(item1.CanonicalSha256, item5.CanonicalSha256);

        // 4. Start and execute job via JobManager
        var jobRecord = jobManager.StartBridgeImportJob(
            plan,
            "idemp-eml-1",
            Scope(),
            handleRegistry,
            accountStore,
            clientFactory,
            journal,
            resolver);

        Assert.NotNull(jobRecord);
        await BridgeTestHelpers.WaitForJobCompletionAsync(jobManager, jobRecord.JobId);

        var finishedJob = jobManager.GetJob(jobRecord.JobId);
        Assert.NotNull(finishedJob);
        Assert.Equal("completed", finishedJob.Status);
        Assert.NotNull(finishedJob.BridgeTransfer);
        Assert.Equal("import", finishedJob.BridgeTransfer.Direction);
        Assert.Equal(5, finishedJob.BridgeTransfer.TotalVerified);
        Assert.Equal(0, finishedJob.BridgeTransfer.TotalFailed);
        Assert.Equal(5, fakeClient.TargetMessages.Count);

        // Verify independent keywords stamped per item
        var keywords = fakeClient.TargetMessages.SelectMany(m => m.Keywords).ToList();
        Assert.Equal(5, keywords.Count);
        Assert.Equal(5, keywords.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(keywords, k => Assert.StartsWith("bitigmail_", k));
    }

    [Fact]
    public async Task Import_Mboxrd_12Items_FromEscaping_Filter3to1_AndExecution()
    {
        string dir = BridgeTestHelpers.CreateTestDir("import-mbox-12");
        var (accountStore, srcAccId, tgtAccId, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var jobManager = new JobManager(dir);
        var fakeClient = new BridgeFakeTransferClient();
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        // 1. Create MBOXRD file with 12 physical records using BridgeMimeBytePolicy.WriteMboxrdRecord
        string mboxPath = Path.Combine(dir, "corpus.mbox");
        using (var fs = new FileStream(mboxPath, FileMode.CreateNew, FileAccess.ReadWrite))
        {
            for (int i = 1; i <= 12; i++)
            {
                string dateStr = i switch
                {
                    1 => "Fri, 05 Jan 2024 10:00:00 +0300",
                    2 => "Sat, 06 Jan 2024 11:00:00 +0300",
                    3 => "Sun, 07 Jan 2024 12:00:00 +0300",
                    4 => "Thu, 15 Feb 2024 10:00:00 +0300", // Outside filter (filter 3/1: 3 in range, 1 outside)
                    _ => $"Mon, {i:D2} Jan 2024 10:00:00 +0300"
                };

                // Add body with From escaping edge cases: 'From ', '>From '
                string body = $"Line 1\r\nFrom escaped line in body {i}\r\n>From double escaped\r\nTail line\r\n";
                var msg = new MimeMessage();
                msg.From.Add(new MailboxAddress($"Sender {i}", $"sender{i}@example.test"));
                msg.To.Add(new MailboxAddress($"Recipient {i}", $"recipient{i}@example.test"));
                msg.Subject = $"Mbox Record {i}";
                msg.Headers[HeaderId.Date] = dateStr;
                msg.MessageId = $"<mbox-item-{i}@example.test>";
                msg.Body = new TextPart("plain") { Text = body };

                byte[] raw = ImapSerializationAssumptions.Serialize(msg);
                BridgeMimeBytePolicy.WriteMboxrdRecord(fs, raw, DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
            }
        }

        // Verify MboxrdRecordReader enumerates exactly 12 records
        using (var fs = File.OpenRead(mboxPath))
        {
            var records = MboxrdRecordReader.EnumerateRecords(fs).ToList();
            Assert.Equal(12, records.Count);
        }

        var inspector = new MimeSourceInspector();
        var manifest = inspector.BuildMboxManifest(mboxPath, "MBOX");
        Assert.Equal(12, manifest.Entries.Count);

        string srcHandle = handleRegistry.RegisterMimeSource(manifest, "mbox-display");

        // 2. Preview with filter: 2024-01-05 .. 2024-01-07 (Rec 1, 2, 3 in range, Rec 4 out of range)
        var previewService = new BridgeImportPreviewService(handleRegistry, accountStore, clientFactory, journal, resolver);
        var previewRequest = new BridgeImportPreviewRequest
        {
            CompanyId = "company-1",
            ProjectId = "project-1",
            SourceHandle = srcHandle,
            TargetAccountId = tgtAccId,
            SelectedFolders = new List<string> { "MBOX" },
            StartDate = "2024-01-05",
            EndDate = "2024-01-07"
        };

        var preview = await previewService.CreatePreviewAsync(previewRequest);
        Assert.True(preview.CanTransfer);
        Assert.Equal(12, preview.TotalSourceItems);
        // Rec 1 (Jan 5), Rec 2 (Jan 6), Rec 3 (Jan 7), Rec 5 (Jan 5), Rec 6 (Jan 6), Rec 7 (Jan 7) in range; others outside
        Assert.Equal(6, preview.EligibleItemsCount);
        Assert.Equal(6, preview.ExcludedCount);

        var plan = journal.GetImportPlan(preview.PreviewId);
        Assert.NotNull(plan);
        Assert.Equal(6, plan.Items.Count);

        // MimeKit equality check for each Mbox item
        foreach (var item in plan.Items)
        {
            using var fs = File.OpenRead(mboxPath);
            var rec = MboxrdRecordReader.EnumerateRecords(fs).First(r => r.Ordinal == item.PhysicalOrdinal);
            var canonical = BridgeMimeBytePolicy.CanonicalizeForImap(rec.RawMimeBytes);
            using var ms = new MemoryStream(canonical.Bytes);
            var msg = MimeMessage.Load(ms);
            byte[] serialized = ImapSerializationAssumptions.Serialize(msg);
            Assert.Equal(canonical.Bytes, serialized);
            Assert.Equal(canonical.CanonicalSha256, item.CanonicalSha256);
        }

        // 3. Execute transfer
        var jobRecord = jobManager.StartBridgeImportJob(
            plan,
            "idemp-mbox-1",
            Scope(),
            handleRegistry,
            accountStore,
            clientFactory,
            journal,
            resolver);

        await BridgeTestHelpers.WaitForJobCompletionAsync(jobManager, jobRecord.JobId);

        var finished = jobManager.GetJob(jobRecord.JobId);
        Assert.NotNull(finished);
        Assert.Equal("completed", finished.Status);
        Assert.NotNull(finished.BridgeTransfer);
        Assert.Equal(6, finished.BridgeTransfer.TotalVerified);
        Assert.Equal(0, finished.BridgeTransfer.TotalFailed);
        Assert.Equal(6, fakeClient.TargetMessages.Count);
    }

    [Fact]
    public async Task Import_DateFallback_UsesCreatedAtUtc_WhenNoDateHeader()
    {
        string dir = BridgeTestHelpers.CreateTestDir("import-date-fallback");
        var (accountStore, _, tgtAccId, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var fakeClient = new BridgeFakeTransferClient();
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        string emlDir = Path.Combine(dir, "eml");
        Directory.CreateDirectory(emlDir);
        var path = BridgeTestHelpers.CreateEmlFile(emlDir, "nodate.eml", "No Date Message", null, "<nodate@example.test>", "Body");

        var manifest = new MimeSourceInspector().BuildEmlFilesManifest(new[] { path }, "INBOX");
        string srcHandle = handleRegistry.RegisterMimeSource(manifest, "eml-nodate");

        var previewService = new BridgeImportPreviewService(handleRegistry, accountStore, clientFactory, journal, resolver);
        var preview = await previewService.CreatePreviewAsync(new BridgeImportPreviewRequest
        {
            CompanyId = "company-1",
            ProjectId = "project-1",
            SourceHandle = srcHandle,
            TargetAccountId = tgtAccId,
            SelectedFolders = new List<string> { "INBOX" }
        });

        Assert.True(preview.CanTransfer);
        Assert.Equal(1, preview.EligibleItemsCount);
        Assert.Equal(0, preview.MissingDateExcludedCount);

        var plan = journal.GetImportPlan(preview.PreviewId)!;
        Assert.Single(plan.Items);
        Assert.Null(plan.Items[0].OriginalMimeDateUtc);
        Assert.Equal(plan.CreatedAtUtc, plan.Items[0].PlannedInternalDateUtc);
    }

    [Fact]
    public async Task Import_TargetMissingUserKeywords_BlocksTransfer()
    {
        string dir = BridgeTestHelpers.CreateTestDir("import-no-keywords");
        var (accountStore, _, tgtAccId, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var fakeClient = new BridgeFakeTransferClient { SupportsKeywords = false };
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        string emlDir = Path.Combine(dir, "eml");
        Directory.CreateDirectory(emlDir);
        var path = BridgeTestHelpers.CreateEmlFile(emlDir, "msg.eml", "Subject", "Fri, 05 Jan 2024 10:00:00 +0300");

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

        Assert.False(preview.CanTransfer);
        Assert.Contains("kalıcı özel anahtar kelime", preview.BlockerReason);
    }

    [Fact]
    public async Task Import_HandleFreeRestart_SucceedsWithoutRegistryState()
    {
        string dir = BridgeTestHelpers.CreateTestDir("import-handle-free");
        var (accountStore, _, tgtAccId, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var initialRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var jobManager = new JobManager(dir);
        var fakeClient = new BridgeFakeTransferClient();
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        string emlDir = Path.Combine(dir, "eml");
        Directory.CreateDirectory(emlDir);
        var path = BridgeTestHelpers.CreateEmlFile(emlDir, "restart.eml", "Restart Test", "Fri, 05 Jan 2024 10:00:00 +0300");

        var manifest = new MimeSourceInspector().BuildEmlFilesManifest(new[] { path }, "INBOX");
        string srcHandle = initialRegistry.RegisterMimeSource(manifest, "eml");

        var previewService = new BridgeImportPreviewService(initialRegistry, accountStore, clientFactory, journal, resolver);
        var preview = await previewService.CreatePreviewAsync(new BridgeImportPreviewRequest
        {
            CompanyId = "company-1",
            ProjectId = "project-1",
            SourceHandle = srcHandle,
            TargetAccountId = tgtAccId,
            SelectedFolders = new List<string> { "INBOX" }
        });

        var plan = journal.GetImportPlan(preview.PreviewId)!;

        // Discard initialRegistry completely (simulate process restart where handle registry is empty)
        var emptyRegistry = new FileHandleRegistry();
        Assert.Null(emptyRegistry.GetMimeSourceEntry(plan.SourceHandle));

        // Start job with emptyRegistry
        var job = jobManager.StartBridgeImportJob(
            plan,
            "restart-key-1",
            Scope(),
            emptyRegistry,
            accountStore,
            clientFactory,
            journal,
            resolver);

        await BridgeTestHelpers.WaitForJobCompletionAsync(jobManager, job.JobId);

        var finished = jobManager.GetJob(job.JobId);
        Assert.NotNull(finished);
        Assert.Equal("completed", finished.Status);
        Assert.Equal(1, finished.BridgeTransfer?.TotalVerified);
    }

    [Fact]
    public async Task Import_SourceFileTampered_FailsJobImmediately()
    {
        string dir = BridgeTestHelpers.CreateTestDir("import-tamper");
        var (accountStore, _, tgtAccId, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handleRegistry = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var jobManager = new JobManager(dir);
        var fakeClient = new BridgeFakeTransferClient();
        var clientFactory = new BridgeFakeClientFactory(fakeClient);

        string emlDir = Path.Combine(dir, "eml");
        Directory.CreateDirectory(emlDir);
        var path = BridgeTestHelpers.CreateEmlFile(emlDir, "msg.eml", "Subject", "Fri, 05 Jan 2024 10:00:00 +0300");

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

        // Tamper with source file on disk
        await File.AppendAllTextAsync(path, "\r\nTAMPERED CONTENT\r\n");

        var job = jobManager.StartBridgeImportJob(
            plan,
            "tamper-key",
            Scope(),
            handleRegistry,
            accountStore,
            clientFactory,
            journal,
            resolver);

        await BridgeTestHelpers.WaitForJobCompletionAsync(jobManager, job.JobId);

        var finished = jobManager.GetJob(job.JobId);
        Assert.NotNull(finished);
        Assert.Contains("Kaynak arşiv doğrulanamadı", finished.ErrorMessage);
        Assert.Empty(fakeClient.AppendedMessages);
    }
}
