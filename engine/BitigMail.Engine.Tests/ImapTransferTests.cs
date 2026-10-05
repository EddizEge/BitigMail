using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Imap;
using BitigMail.Engine.Imap.Transfer;
using BitigMail.Engine.Models;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.Imap.Transfer;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Security;
using MailKit;
using MimeKit;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed partial class ImapTransferTests
{
    private static string CreateTestDir()
    {
        string path = Path.Combine(Path.GetTempPath(), "bitigmail-transfer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static (ImapAccountStore store, string sourceId, string targetId) SetupTestAccounts(string dir)
    {
        var protector = new WindowsImapCredentialProtector();
        var policy = new ImapConnectionPolicy(allowTask014Loopback: true);
        var store = new ImapAccountStore(Path.Combine(dir, "accounts"), protector, policy);

        var src = store.CreateAccount(new CreateImapAccountRequest
        {
            CompanyId = "comp_test",
            ProjectId = "proj_test",
            DisplayName = "Source Account",
            Email = "src@example.test",
            Host = "imap.example.test",
            Port = 993,
            TlsMode = "ssl",
            Username = "src_user",
            Password = "source-secret-password-123"
        });

        var tgt = store.CreateAccount(new CreateImapAccountRequest
        {
            CompanyId = "comp_test",
            ProjectId = "proj_test",
            DisplayName = "Target Account",
            Email = "tgt@example.test",
            Host = "imap.example.test",
            Port = 993,
            TlsMode = "ssl",
            Username = "tgt_user",
            Password = "target-secret-password-456"
        });

        return (store, src.AccountId, tgt.AccountId);
    }

    private static MimeMessage CreateMime(string subject, string? dateHeader = "Mon, 01 Jan 2024 10:00:00 +0300", string? messageId = null)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("Sender", "sender@example.test"));
        msg.To.Add(new MailboxAddress("Recipient", "recipient@example.test"));
        msg.Subject = subject;
        if (!string.IsNullOrEmpty(messageId))
        {
            msg.MessageId = messageId;
        }
        if (dateHeader != null)
        {
            msg.Headers[HeaderId.Date] = dateHeader;
        }
        else
        {
            msg.Headers.Remove(HeaderId.Date);
        }
        msg.Body = new TextPart("plain") { Text = $"Body for {subject}\r\n" };
        return msg;
    }

    [Fact]
    public async Task FrozenPreview_CapturesExactItems_ExcludesSecrets()
    {
        string dir = CreateTestDir();
        var (store, srcId, tgtId) = SetupTestAccounts(dir);
        var journal = new ImapTransferJournal(dir);
        var fakeClient = new FakeImapTransferClient();

        var mime1 = CreateMime("Message 1");
        byte[] rawBytes1 = ImapSerializationAssumptions.Serialize(mime1);
        fakeClient.AddSourceMessage("INBOX", 1, rawBytes1, mime1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.Seen, null);

        var previewService = new ImapTransferPreviewService(store, new FakeTransferClientFactory(fakeClient), journal, new ImapCredentialResolver(store, new WindowsImapCredentialProtector(), new MsalMicrosoftAuthProvider()));

        var request = new ImapTransferPreviewRequest
        {
            CompanyId = "comp_test",
            ProjectId = "proj_test",
            SourceAccountId = srcId,
            TargetAccountId = tgtId,
            SelectedFolders = new List<ImapFolderMappingRequest>
            {
                new() { SourceFolderPath = "INBOX", TargetFolderPath = "Migrated_INBOX" }
            }
        };

        var preview = await previewService.CreatePreviewAsync(request);

        Assert.NotNull(preview);
        Assert.True(preview.CanTransfer);
        Assert.Equal(1, preview.EligibleItemsCount);
        Assert.Equal(1, preview.TotalSourceItems);
        Assert.Equal(0, preview.ExcludedCount);

        // Verify no secret leak
        string json = JsonSerializer.Serialize(preview);
        Assert.DoesNotContain("source-secret-password", json);
        Assert.DoesNotContain("target-secret-password", json);
        Assert.DoesNotContain("src_user", json);
        Assert.DoesNotContain("imap.example.test", json);

        var plan = journal.GetPlan(preview.PreviewId);
        Assert.NotNull(plan);
        Assert.Single(plan.Items);
        Assert.Equal(1u, plan.Items[0].SourceUid);
        Assert.Equal("INBOX", plan.Items[0].SourceFolder);
        Assert.Equal("Migrated_INBOX", plan.Items[0].TargetFolder);
    }

    [Fact]
    public async Task DateEdgeAndMissingTimezone_ExcludesAndCountsMissing()
    {
        string dir = CreateTestDir();
        var (store, srcId, tgtId) = SetupTestAccounts(dir);
        var journal = new ImapTransferJournal(dir);
        var fakeClient = new FakeImapTransferClient();

        // Msg 1: Eligible within 2024-01-01 .. 2024-01-15 UTC+03
        var mime1 = CreateMime("Msg 1", "Tue, 02 Jan 2024 12:00:00 +0300");
        fakeClient.AddSourceMessage("INBOX", 1, ImapSerializationAssumptions.Serialize(mime1), mime1, DateTimeOffset.Parse("2024-01-02T09:00:00Z"), DateTimeOffset.UtcNow, MessageFlags.None, null);

        // Msg 2: Missing timezone in Date header -> must be excluded and counted
        var mime2 = CreateMime("Msg 2", "Tue, 02 Jan 2024 12:00:00"); // No timezone
        fakeClient.AddSourceMessage("INBOX", 2, ImapSerializationAssumptions.Serialize(mime2), mime2, null, DateTimeOffset.UtcNow, MessageFlags.None, null);

        // Msg 3: Missing Date header altogether -> must be excluded and counted
        var mime3 = CreateMime("Msg 3", null);
        fakeClient.AddSourceMessage("INBOX", 3, ImapSerializationAssumptions.Serialize(mime3), mime3, null, DateTimeOffset.UtcNow, MessageFlags.None, null);

        // Msg 4: Outside date range (2024-02-01) -> excluded, but not missing timezone
        var mime4 = CreateMime("Msg 4", "Thu, 01 Feb 2024 12:00:00 +0300");
        fakeClient.AddSourceMessage("INBOX", 4, ImapSerializationAssumptions.Serialize(mime4), mime4, DateTimeOffset.Parse("2024-02-01T09:00:00Z"), DateTimeOffset.UtcNow, MessageFlags.None, null);

        var previewService = new ImapTransferPreviewService(store, new FakeTransferClientFactory(fakeClient), journal, new ImapCredentialResolver(store, new WindowsImapCredentialProtector(), new MsalMicrosoftAuthProvider()));

        var preview = await previewService.CreatePreviewAsync(new ImapTransferPreviewRequest
        {
            CompanyId = "comp_test",
            ProjectId = "proj_test",
            SourceAccountId = srcId,
            TargetAccountId = tgtId,
            StartDate = "2024-01-01",
            EndDate = "2024-01-15",
            SelectedFolders = new List<ImapFolderMappingRequest>
            {
                new() { SourceFolderPath = "INBOX", TargetFolderPath = "Migrated_INBOX" }
            }
        });

        Assert.Equal(4, preview.TotalSourceItems);
        Assert.Equal(1, preview.EligibleItemsCount);
        Assert.Equal(3, preview.ExcludedCount);
        Assert.Equal(2, preview.MissingDateExcludedCount);
        Assert.True(preview.CanTransfer);
    }

    [Fact]
    public async Task PhysicalDuplicatesAndSharedMessageId_TreatedAsDistinctItems()
    {
        string dir = CreateTestDir();
        var (store, srcId, tgtId) = SetupTestAccounts(dir);
        var journal = new ImapTransferJournal(dir);
        var fakeClient = new FakeImapTransferClient();

        // Two physical messages with IDENTICAL Message-ID
        string sharedMsgId = "<shared-123@example.test>";
        var mime1 = CreateMime("Shared ID Msg 1", messageId: sharedMsgId);
        var mime2 = CreateMime("Shared ID Msg 2", messageId: sharedMsgId);

        fakeClient.AddSourceMessage("INBOX", 101, ImapSerializationAssumptions.Serialize(mime1), mime1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None, null);
        fakeClient.AddSourceMessage("INBOX", 102, ImapSerializationAssumptions.Serialize(mime2), mime2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None, null);

        var previewService = new ImapTransferPreviewService(store, new FakeTransferClientFactory(fakeClient), journal, new ImapCredentialResolver(store, new WindowsImapCredentialProtector(), new MsalMicrosoftAuthProvider()));

        var preview = await previewService.CreatePreviewAsync(new ImapTransferPreviewRequest
        {
            CompanyId = "comp_test",
            ProjectId = "proj_test",
            SourceAccountId = srcId,
            TargetAccountId = tgtId,
            SelectedFolders = new List<ImapFolderMappingRequest>
            {
                new() { SourceFolderPath = "INBOX", TargetFolderPath = "Migrated_INBOX" }
            }
        });

        Assert.Equal(2, preview.EligibleItemsCount);
        var plan = journal.GetPlan(preview.PreviewId)!;
        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(101u, plan.Items[0].SourceUid);
        Assert.Equal(102u, plan.Items[1].SourceUid);
    }

    [Fact]
    public async Task AccountVersionOrSourceHashMismatch_BlocksBeforeWrites()
    {
        string dir = CreateTestDir();
        var (store, srcId, tgtId) = SetupTestAccounts(dir);
        var journal = new ImapTransferJournal(dir);
        var fakeClient = new FakeImapTransferClient();

        var mime = CreateMime("Msg");
        fakeClient.AddSourceMessage("INBOX", 1, ImapSerializationAssumptions.Serialize(mime), mime, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None, null);

        var previewService = new ImapTransferPreviewService(store, new FakeTransferClientFactory(fakeClient), journal, new ImapCredentialResolver(store, new WindowsImapCredentialProtector(), new MsalMicrosoftAuthProvider()));
        var preview = await previewService.CreatePreviewAsync(new ImapTransferPreviewRequest
        {
            CompanyId = "comp_test",
            ProjectId = "proj_test",
            SourceAccountId = srcId,
            TargetAccountId = tgtId,
            SelectedFolders = new List<ImapFolderMappingRequest> { new() { SourceFolderPath = "INBOX", TargetFolderPath = "Migrated" } }
        });

        var plan = journal.GetPlan(preview.PreviewId)!;

        // Mutate source account version
        store.UpdateAccount(srcId, new UpdateImapAccountRequest
        {
            CompanyId = "comp_test",
            ProjectId = "proj_test",
            DisplayName = "Updated Source",
            ExpectedVersion = 1
        });

        var jobRecord = new LocalJobRecord { JobId = "job-version-test", JobKind = "imap-transfer" };
        var worker = new ImapTransferWorker(
            store,
            new FakeTransferClientFactory(fakeClient),
            journal,
            _ => { },
            _ => { });

        await worker.ExecuteAsync(jobRecord, plan);

        Assert.Equal("failed", jobRecord.Status);
        Assert.Contains("Kaynak hesap sürümü", jobRecord.ErrorMessage);
        Assert.Empty(fakeClient.TargetAppendedMessages);
    }

    [Fact]
    public async Task IdempotencyConflict_SameKeyDifferentPlan_ThrowsConflict()
    {
        string dir = CreateTestDir();
        var (store, srcId, tgtId) = SetupTestAccounts(dir);
        var journal = new ImapTransferJournal(dir);
        var jobManager = new JobManager(dir);
        var fakeClient = new FakeImapTransferClient();

        var mime = CreateMime("Msg");
        fakeClient.AddSourceMessage("INBOX", 1, ImapSerializationAssumptions.Serialize(mime), mime, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None, null);

        var previewService = new ImapTransferPreviewService(store, new FakeTransferClientFactory(fakeClient), journal, new ImapCredentialResolver(store, new WindowsImapCredentialProtector(), new MsalMicrosoftAuthProvider()));

        var prev1 = await previewService.CreatePreviewAsync(new ImapTransferPreviewRequest
        {
            CompanyId = "comp_test",
            ProjectId = "proj_test",
            SourceAccountId = srcId,
            TargetAccountId = tgtId,
            SelectedFolders = new List<ImapFolderMappingRequest> { new() { SourceFolderPath = "INBOX", TargetFolderPath = "T1" } }
        });

        var prev2 = await previewService.CreatePreviewAsync(new ImapTransferPreviewRequest
        {
            CompanyId = "comp_test",
            ProjectId = "proj_test",
            SourceAccountId = srcId,
            TargetAccountId = tgtId,
            SelectedFolders = new List<ImapFolderMappingRequest> { new() { SourceFolderPath = "INBOX", TargetFolderPath = "T2" } }
        });

        var plan1 = journal.GetPlan(prev1.PreviewId)!;
        var plan2 = journal.GetPlan(prev2.PreviewId)!;

        var context = new ClientProjectContext { CompanyId = "comp_test", ProjectId = "proj_test" };

        var job1 = jobManager.StartImapTransferJob(plan1, "idemp-key-1", context, store, new FakeTransferClientFactory(fakeClient), journal);
        Assert.NotNull(job1);

        // Same idempotency key with different plan -> must throw InvalidOperationException
        var ex = Assert.Throws<InvalidOperationException>(() =>
            jobManager.StartImapTransferJob(plan2, "idemp-key-1", context, store, new FakeTransferClientFactory(fakeClient), journal));

        Assert.Contains("Idempotency key", ex.Message);
    }

    [Fact]
    public async Task StateTransitions_PlannedToAppendIntentToVerified()
    {
        string dir = CreateTestDir();
        var (store, srcId, tgtId) = SetupTestAccounts(dir);
        var journal = new ImapTransferJournal(dir);
        var fakeClient = new FakeImapTransferClient();

        var mime = CreateMime("Transition Test");
        fakeClient.AddSourceMessage("INBOX", 1, ImapSerializationAssumptions.Serialize(mime), mime, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None, null);

        var previewService = new ImapTransferPreviewService(store, new FakeTransferClientFactory(fakeClient), journal, new ImapCredentialResolver(store, new WindowsImapCredentialProtector(), new MsalMicrosoftAuthProvider()));
        var preview = await previewService.CreatePreviewAsync(new ImapTransferPreviewRequest
        {
            CompanyId = "comp_test",
            ProjectId = "proj_test",
            SourceAccountId = srcId,
            TargetAccountId = tgtId,
            SelectedFolders = new List<ImapFolderMappingRequest> { new() { SourceFolderPath = "INBOX", TargetFolderPath = "Target" } }
        });

        var plan = journal.GetPlan(preview.PreviewId)!;
        var jobRecord = new LocalJobRecord { JobId = "job-transition-1", JobKind = "imap-transfer" };

        var statesObserved = new List<ImapTransferItemStatus>();
        journal.OnBeforeWriteAppendIntent = entry => statesObserved.Add(entry.Status);
        journal.OnBeforeWriteVerified = entry => statesObserved.Add(entry.Status);

        ConversionReport? savedReport = null;
        var worker = new ImapTransferWorker(
            store,
            new FakeTransferClientFactory(fakeClient),
            journal,
            _ => { },
            r => savedReport = r);

        await worker.ExecuteAsync(jobRecord, plan);

        Assert.Equal("completed", jobRecord.Status);
        Assert.Contains(ImapTransferItemStatus.AppendIntent, statesObserved);
        Assert.Contains(ImapTransferItemStatus.Verified, statesObserved);

        var finalJournal = journal.GetJournal(jobRecord.JobId)!;
        Assert.Equal(ImapTransferItemStatus.Verified, finalJournal.Entries[plan.Items[0].ItemId].Status);
        Assert.NotNull(savedReport);
        Assert.True(savedReport.ConversionSuccess);
        Assert.Equal("imap-transfer", savedReport.JobKind);
        Assert.NotNull(savedReport.ImapTransfer);
        Assert.Equal(1, savedReport.ImapTransfer.TotalVerified);
    }

    [Fact]
    public async Task PersistenceFault_HaltsBeforeNextAppend_PreservesPriorTargetUidEvidence()
    {
        string dir = CreateTestDir();
        var (store, srcId, tgtId) = SetupTestAccounts(dir);
        var journal = new ImapTransferJournal(dir);
        var fakeClient = new FakeImapTransferClient();

        var mime1 = CreateMime("Item 1");
        var mime2 = CreateMime("Item 2");
        fakeClient.AddSourceMessage("INBOX", 1, ImapSerializationAssumptions.Serialize(mime1), mime1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None, null);
        fakeClient.AddSourceMessage("INBOX", 2, ImapSerializationAssumptions.Serialize(mime2), mime2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None, null);

        var previewService = new ImapTransferPreviewService(store, new FakeTransferClientFactory(fakeClient), journal, new ImapCredentialResolver(store, new WindowsImapCredentialProtector(), new MsalMicrosoftAuthProvider()));
        var preview = await previewService.CreatePreviewAsync(new ImapTransferPreviewRequest
        {
            CompanyId = "comp_test",
            ProjectId = "proj_test",
            SourceAccountId = srcId,
            TargetAccountId = tgtId,
            SelectedFolders = new List<ImapFolderMappingRequest> { new() { SourceFolderPath = "INBOX", TargetFolderPath = "Target" } }
        });

        var plan = journal.GetPlan(preview.PreviewId)!;
        var jobRecord = new LocalJobRecord { JobId = "job-fault-test", JobKind = "imap-transfer" };

        // Inject disk fault on writing Verified for item 1
        int verifiedWrites = 0;
        journal.OnBeforeWriteVerified = _ =>
        {
            verifiedWrites++;
            if (verifiedWrites == 1)
            {
                throw new IOException("Simulated disk write failure on Verified");
            }
        };

        var worker = new ImapTransferWorker(
            store,
            new FakeTransferClientFactory(fakeClient),
            journal,
            _ => { },
            _ => { });

        await worker.ExecuteAsync(jobRecord, plan);

        Assert.Equal("failed", jobRecord.Status);
        // Only item 1 was appended; item 2 was NOT appended because persistence failed
        Assert.Single(fakeClient.TargetAppendedMessages);
    }

    [Fact]
    public async Task ResumeReconciliation_ZeroToken_IsAmbiguous_SetsNeedsAttention_NoReappend()
    {
        string dir = CreateTestDir();
        var (store, srcId, tgtId) = SetupTestAccounts(dir);
        var journal = new ImapTransferJournal(dir);
        var fakeClient = new FakeImapTransferClient();

        var mime = CreateMime("Zero Token Test");
        fakeClient.AddSourceMessage("INBOX", 1, ImapSerializationAssumptions.Serialize(mime), mime, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None, null);

        var previewService = new ImapTransferPreviewService(store, new FakeTransferClientFactory(fakeClient), journal, new ImapCredentialResolver(store, new WindowsImapCredentialProtector(), new MsalMicrosoftAuthProvider()));
        var preview = await previewService.CreatePreviewAsync(new ImapTransferPreviewRequest
        {
            CompanyId = "comp_test",
            ProjectId = "proj_test",
            SourceAccountId = srcId,
            TargetAccountId = tgtId,
            SelectedFolders = new List<ImapFolderMappingRequest> { new() { SourceFolderPath = "INBOX", TargetFolderPath = "Target" } }
        });

        var plan = journal.GetPlan(preview.PreviewId)!;
        string jobId = "job-zero-token";
        var state = journal.InitializeJournal(jobId, plan);

        // Simulate crash right after AppendIntent persisted, before APPEND occurred on target
        state.Entries[plan.Items[0].ItemId].Status = ImapTransferItemStatus.AppendIntent;
        state.Entries[plan.Items[0].ItemId].BitigMailKeyword = "bitigmail_fake_token_0000000000000000";
        journal.UpdateEntry(jobId, state.Entries[plan.Items[0].ItemId]);

        var jobRecord = new LocalJobRecord { JobId = jobId, JobKind = "imap-transfer" };
        var worker = new ImapTransferWorker(
            store,
            new FakeTransferClientFactory(fakeClient),
            journal,
            _ => { },
            _ => { });

        await worker.ExecuteAsync(jobRecord, plan);

        // Zero token on target -> ambiguous, sets NeedsAttention, does NOT re-append
        var finalEntry = journal.GetJournal(jobId)!.Entries[plan.Items[0].ItemId];
        Assert.Equal(ImapTransferItemStatus.NeedsAttention, finalEntry.Status);
        Assert.Empty(fakeClient.TargetAppendedMessages);
    }

    [Fact]
    public async Task ResumeReconciliation_OneToken_ReconcilesToVerified()
    {
        string dir = CreateTestDir();
        var (store, srcId, tgtId) = SetupTestAccounts(dir);
        var journal = new ImapTransferJournal(dir);
        var fakeClient = new FakeImapTransferClient();

        var mime = CreateMime("One Token Test");
        byte[] raw = ImapSerializationAssumptions.Serialize(mime);
        fakeClient.AddSourceMessage("INBOX", 1, raw, mime, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None, null);

        var previewService = new ImapTransferPreviewService(store, new FakeTransferClientFactory(fakeClient), journal, new ImapCredentialResolver(store, new WindowsImapCredentialProtector(), new MsalMicrosoftAuthProvider()));
        var preview = await previewService.CreatePreviewAsync(new ImapTransferPreviewRequest
        {
            CompanyId = "comp_test",
            ProjectId = "proj_test",
            SourceAccountId = srcId,
            TargetAccountId = tgtId,
            SelectedFolders = new List<ImapFolderMappingRequest> { new() { SourceFolderPath = "INBOX", TargetFolderPath = "Target" } }
        });

        var plan = journal.GetPlan(preview.PreviewId)!;
        string jobId = "job-one-token";
        var state = journal.InitializeJournal(jobId, plan);

        string keyword = "bitigmail_test_token_1111111111111111";
        state.Entries[plan.Items[0].ItemId].Status = ImapTransferItemStatus.AppendIntent;
        state.Entries[plan.Items[0].ItemId].BitigMailKeyword = keyword;
        state.Entries[plan.Items[0].ItemId].TargetUidValidity = 12345;
        journal.UpdateEntry(jobId, state.Entries[plan.Items[0].ItemId]);

        // Manually place message on target with that keyword
        fakeClient.AddTargetMessage("Target", 888, raw, MessageFlags.None, new List<string> { keyword }, plan.Items[0].InternalDateUtc);

        var jobRecord = new LocalJobRecord { JobId = jobId, JobKind = "imap-transfer" };
        var worker = new ImapTransferWorker(
            store,
            new FakeTransferClientFactory(fakeClient),
            journal,
            _ => { },
            _ => { });

        await worker.ExecuteAsync(jobRecord, plan);

        // One token matching content -> reconciles to Verified, 0 new appends!
        var finalEntry = journal.GetJournal(jobId)!.Entries[plan.Items[0].ItemId];
        Assert.Equal(ImapTransferItemStatus.Verified, finalEntry.Status);
        Assert.Equal(888u, finalEntry.TargetUid);
        Assert.Empty(fakeClient.TargetAppendedMessages);
    }

    [Fact]
    public async Task ResumeReconciliation_MultipleTokens_SetsNeedsAttention()
    {
        string dir = CreateTestDir();
        var (store, srcId, tgtId) = SetupTestAccounts(dir);
        var journal = new ImapTransferJournal(dir);
        var fakeClient = new FakeImapTransferClient();

        var mime = CreateMime("Multi Token Test");
        byte[] raw = ImapSerializationAssumptions.Serialize(mime);
        fakeClient.AddSourceMessage("INBOX", 1, raw, mime, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None, null);

        var previewService = new ImapTransferPreviewService(store, new FakeTransferClientFactory(fakeClient), journal, new ImapCredentialResolver(store, new WindowsImapCredentialProtector(), new MsalMicrosoftAuthProvider()));
        var preview = await previewService.CreatePreviewAsync(new ImapTransferPreviewRequest
        {
            CompanyId = "comp_test",
            ProjectId = "proj_test",
            SourceAccountId = srcId,
            TargetAccountId = tgtId,
            SelectedFolders = new List<ImapFolderMappingRequest> { new() { SourceFolderPath = "INBOX", TargetFolderPath = "Target" } }
        });

        var plan = journal.GetPlan(preview.PreviewId)!;
        string jobId = "job-multi-token";
        var state = journal.InitializeJournal(jobId, plan);

        string keyword = "bitigmail_multi_token_222222222222";
        state.Entries[plan.Items[0].ItemId].Status = ImapTransferItemStatus.AppendIntent;
        state.Entries[plan.Items[0].ItemId].BitigMailKeyword = keyword;
        state.Entries[plan.Items[0].ItemId].TargetUidValidity = 12345;
        journal.UpdateEntry(jobId, state.Entries[plan.Items[0].ItemId]);

        // Place TWO messages on target with the same keyword
        fakeClient.AddTargetMessage("Target", 888, raw, MessageFlags.None, new List<string> { keyword }, DateTimeOffset.UtcNow);
        fakeClient.AddTargetMessage("Target", 889, raw, MessageFlags.None, new List<string> { keyword }, DateTimeOffset.UtcNow);

        var jobRecord = new LocalJobRecord { JobId = jobId, JobKind = "imap-transfer" };
        var worker = new ImapTransferWorker(
            store,
            new FakeTransferClientFactory(fakeClient),
            journal,
            _ => { },
            _ => { });

        await worker.ExecuteAsync(jobRecord, plan);

        var finalEntry = journal.GetJournal(jobId)!.Entries[plan.Items[0].ItemId];
        Assert.Equal(ImapTransferItemStatus.NeedsAttention, finalEntry.Status);
        Assert.Contains("birden çok ileti tespit edildi", finalEntry.ErrorMessage);
    }

    [Fact]
    public async Task VerifiedTargetMutationOrDeletion_BlocksTransfer()
    {
        string dir = CreateTestDir();
        var (store, srcId, tgtId) = SetupTestAccounts(dir);
        var journal = new ImapTransferJournal(dir);
        var fakeClient = new FakeImapTransferClient();

        var mime = CreateMime("Mutation Test");
        byte[] raw = ImapSerializationAssumptions.Serialize(mime);
        fakeClient.AddSourceMessage("INBOX", 1, raw, mime, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None, null);

        var previewService = new ImapTransferPreviewService(store, new FakeTransferClientFactory(fakeClient), journal, new ImapCredentialResolver(store, new WindowsImapCredentialProtector(), new MsalMicrosoftAuthProvider()));
        var preview = await previewService.CreatePreviewAsync(new ImapTransferPreviewRequest
        {
            CompanyId = "comp_test",
            ProjectId = "proj_test",
            SourceAccountId = srcId,
            TargetAccountId = tgtId,
            SelectedFolders = new List<ImapFolderMappingRequest> { new() { SourceFolderPath = "INBOX", TargetFolderPath = "Target" } }
        });

        var plan = journal.GetPlan(preview.PreviewId)!;
        string jobId = "job-mutation-test";
        var state = journal.InitializeJournal(jobId, plan);

        // Mark item as Verified with TargetUid 999
        state.Entries[plan.Items[0].ItemId].Status = ImapTransferItemStatus.Verified;
        state.Entries[plan.Items[0].ItemId].TargetUid = 999;
        state.Entries[plan.Items[0].ItemId].TargetUidValidity = 12345;
        journal.UpdateEntry(jobId, state.Entries[plan.Items[0].ItemId]);

        // TargetUid 999 DOES NOT exist on target (deleted)
        var jobRecord = new LocalJobRecord { JobId = jobId, JobKind = "imap-transfer" };
        var worker = new ImapTransferWorker(
            store,
            new FakeTransferClientFactory(fakeClient),
            journal,
            _ => { },
            _ => { });

        await worker.ExecuteAsync(jobRecord, plan);

        Assert.Equal("failed", jobRecord.Status);
        Assert.Contains("değiştirilmiş veya silinmiş", jobRecord.ErrorMessage);
    }

    [Fact]
    public async Task SharedSlotContention_BlocksConcurrentJobs()
    {
        string dir = CreateTestDir();
        var (store, srcId, tgtId) = SetupTestAccounts(dir);
        var journal = new ImapTransferJournal(dir);
        var jobManager = new JobManager(dir);
        var fakeClient = new FakeImapTransferClient();

        var mime = CreateMime("Contention Test");
        fakeClient.AddSourceMessage("INBOX", 1, ImapSerializationAssumptions.Serialize(mime), mime, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MessageFlags.None, null);

        var previewService = new ImapTransferPreviewService(store, new FakeTransferClientFactory(fakeClient), journal, new ImapCredentialResolver(store, new WindowsImapCredentialProtector(), new MsalMicrosoftAuthProvider()));
        var preview = await previewService.CreatePreviewAsync(new ImapTransferPreviewRequest
        {
            CompanyId = "comp_test",
            ProjectId = "proj_test",
            SourceAccountId = srcId,
            TargetAccountId = tgtId,
            SelectedFolders = new List<ImapFolderMappingRequest> { new() { SourceFolderPath = "INBOX", TargetFolderPath = "Target" } }
        });

        var plan = journal.GetPlan(preview.PreviewId)!;
        var context = new ClientProjectContext { CompanyId = "comp_test", ProjectId = "proj_test" };

        var job1 = jobManager.StartImapTransferJob(plan, "key-1", context, store, new FakeTransferClientFactory(fakeClient), journal);
        Assert.NotNull(job1);

        // Attempting to start another job while job1 is running should throw InvalidOperationException
        var ex = Assert.Throws<InvalidOperationException>(() =>
            jobManager.StartImapTransferJob(plan, "key-2", context, store, new FakeTransferClientFactory(fakeClient), journal));

        Assert.Contains("Halen devam eden etkin bir yerel işlem bulunmaktadır", ex.Message);
    }
}

// ==========================================
// In-Memory Fakes for Deterministic Unit Testing
// ==========================================

internal sealed class FakeTransferClientFactory : IImapTransferClientFactory
{
    private readonly IImapTransferClient _client;
    public FakeTransferClientFactory(IImapTransferClient client) => _client = client;
    public IImapTransferClient CreateClient() => _client;
}

internal sealed class FakeImapTransferClient : IImapTransferClient
{
    private readonly Dictionary<string, List<ImapSourceMessageSummary>> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<uint, ImapTargetMessage>> _targets = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Folder, MimeMessage Message, MessageFlags Flags, HashSet<string> Keywords)> TargetAppendedMessages { get; } = new();
    private uint _nextUid = 1000;
    public Func<int, Task>? BeforeAppendAsync { get; set; }
    private int _appendAttemptCount;

    public void AddSourceMessage(
        string folder,
        uint uid,
        byte[] rawBytes,
        MimeMessage message,
        DateTimeOffset? originalMimeDate,
        DateTimeOffset internalDate,
        MessageFlags flags,
        List<string>? keywords)
    {
        if (!_sources.ContainsKey(folder))
        {
            _sources[folder] = new List<ImapSourceMessageSummary>();
        }

        string rawSha = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();
        _sources[folder].Add(new ImapSourceMessageSummary
        {
            SourceUid = uid,
            SourceUidValidity = 12345,
            RawBytes = rawBytes,
            RawSha256 = rawSha,
            Message = message,
            OriginalMimeDateUtc = originalMimeDate,
            InternalDateUtc = internalDate,
            Flags = flags,
            Keywords = keywords ?? new List<string>()
        });
    }

    public void AddTargetMessage(
        string folder,
        uint uid,
        byte[] rawBytes,
        MessageFlags flags,
        List<string> keywords,
        DateTimeOffset internalDate)
    {
        if (!_targets.ContainsKey(folder))
        {
            _targets[folder] = new Dictionary<uint, ImapTargetMessage>();
        }

        _targets[folder][uid] = new ImapTargetMessage
        {
            Uid = uid,
            RawBytes = rawBytes,
            RawSha256 = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant(),
            Flags = flags,
            Keywords = keywords,
            InternalDate = internalDate
        };
    }

    public Task ConnectAndAuthenticateAsync(string host, int port, string tlsMode, string username, string password, CancellationToken ct) => Task.CompletedTask;

    public Task<uint> GetFolderUidValidityAsync(string folderPath, CancellationToken ct) => Task.FromResult(12345u);

    public Task<bool> SupportsUserKeywordsAsync(string folderPath, CancellationToken ct) => Task.FromResult(true);

    public Task<IReadOnlyList<ImapSourceMessageSummary>> InspectSourceFolderAsync(string folderPath, CancellationToken ct)
    {
        if (_sources.TryGetValue(folderPath, out var list))
        {
            return Task.FromResult<IReadOnlyList<ImapSourceMessageSummary>>(list);
        }
        return Task.FromResult<IReadOnlyList<ImapSourceMessageSummary>>(new List<ImapSourceMessageSummary>());
    }

    public async Task<uint> AppendMessageAsync(
        string folderPath,
        MimeMessage message,
        MessageFlags flags,
        HashSet<string> keywords,
        DateTimeOffset? internalDate,
        CancellationToken ct)
    {
        if (BeforeAppendAsync != null)
            await BeforeAppendAsync(Interlocked.Increment(ref _appendAttemptCount));
        TargetAppendedMessages.Add((folderPath, message, flags, keywords));
        uint uid = _nextUid++;
        byte[] rawBytes = ImapSerializationAssumptions.Serialize(message);

        AddTargetMessage(folderPath, uid, rawBytes, flags, keywords.ToList(), internalDate ?? DateTimeOffset.UtcNow);
        return uid;
    }

    public Task<IReadOnlyList<uint>> SearchByKeywordAsync(string folderPath, string keyword, CancellationToken ct)
    {
        if (!_targets.TryGetValue(folderPath, out var msgs))
        {
            return Task.FromResult<IReadOnlyList<uint>>(new List<uint>());
        }

        var matching = msgs.Values
            .Where(m => m.Keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase))
            .Select(m => m.Uid)
            .ToList();

        return Task.FromResult<IReadOnlyList<uint>>(matching);
    }

    public Task<ImapTargetVerificationResult> FetchAndVerifyAsync(string folderPath, uint uid, CancellationToken ct)
    {
        if (_targets.TryGetValue(folderPath, out var msgs) && msgs.TryGetValue(uid, out var msg))
        {
            return Task.FromResult(new ImapTargetVerificationResult
            {
                Exists = true,
                RawBytes = msg.RawBytes,
                RawSha256 = msg.RawSha256,
                Flags = msg.Flags,
                Keywords = msg.Keywords,
                InternalDateUtc = msg.InternalDate
            });
        }

        return Task.FromResult(new ImapTargetVerificationResult { Exists = false });
    }

    public Task DisconnectAsync(CancellationToken ct) => Task.CompletedTask;
    public void Dispose() { }

    private class ImapTargetMessage
    {
        public uint Uid { get; set; }
        public byte[] RawBytes { get; set; } = Array.Empty<byte>();
        public string RawSha256 { get; set; } = string.Empty;
        public MessageFlags Flags { get; set; }
        public List<string> Keywords { get; set; } = new();
        public DateTimeOffset InternalDate { get; set; }
    }
}

