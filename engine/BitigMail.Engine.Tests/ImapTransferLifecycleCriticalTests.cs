using System;
using System.IO;
using System.Linq;
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

public sealed class ImapTransferLifecycleCriticalTests
{
    private sealed record Fixture(string Directory, ImapAccountStore Store, ImapTransferJournal Journal,
        ImapTransferPlan Plan, FakeImapTransferClient Client, FakeTransferClientFactory Factory, JobManager Manager);

    private static async Task<Fixture> Setup(int messageCount = 1)
    {
        string directory = Path.Combine(Path.GetTempPath(), "bitigmail-imap-lifecycle", Guid.NewGuid().ToString("N"));
        var store = new ImapAccountStore(Path.Combine(directory, "accounts"), new WindowsImapCredentialProtector(), new ImapConnectionPolicy());
        var accounts = new[] { "source", "target" }.Select(role => store.CreateAccount(new CreateImapAccountRequest
        {
            CompanyId = "company", ProjectId = "project", DisplayName = role, Email = role + "@example.test",
            Host = "imap.example.test", Port = 993, TlsMode = "ssl", Username = role, Password = "synthetic-critical-password"
        })).ToArray();
        var client = new FakeImapTransferClient();
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse("source@example.test"));
        message.To.Add(MailboxAddress.Parse("target@example.test"));
        message.Subject = "Synthetic persistence boundary";
        message.Date = DateTimeOffset.Parse("2024-01-01T12:00:00Z");
        message.MessageId = "critical@example.test";
        message.Body = new TextPart("plain") { Text = "Synthetic body\r\n" };
        for (uint uid = 1; uid <= messageCount; uid++)
            client.AddSourceMessage("INBOX", uid, ImapSerializationAssumptions.Serialize(message), message,
                message.Date, message.Date, MessageFlags.Seen, new());
        var factory = new FakeTransferClientFactory(client);
        var journal = new ImapTransferJournal(directory);
        var preview = await new ImapTransferPreviewService(store, factory, journal, new ImapCredentialResolver(store, new WindowsImapCredentialProtector(), new MsalMicrosoftAuthProvider())).CreatePreviewAsync(new()
        {
            CompanyId = "company", ProjectId = "project", SourceAccountId = accounts[0].AccountId, TargetAccountId = accounts[1].AccountId,
            SelectedFolders = new() { new() { SourceFolderPath = "INBOX", TargetFolderPath = "Target" } }
        }, CancellationToken.None);
        Assert.True(preview.CanTransfer);
        return new(directory, store, journal, journal.GetPlan(preview.PreviewId)!, client, factory, new JobManager(directory));
    }

    private static ClientProjectContext Scope() => new() { CompanyId = "company", ProjectId = "project" };
    private static LocalJobRecord Start(Fixture f, string key = "critical-key") =>
        f.Manager.StartImapTransferJob(f.Plan, key, Scope(), f.Store, f.Factory, f.Journal);

    private static LocalJobRecord Enqueue(Fixture f, string key) =>
        f.Manager.StartImapTransferJob(f.Plan, key, Scope(), f.Store, f.Factory, f.Journal, enqueueIfBusy: true);

    private static async Task WaitUntil(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }

    private static async Task WaitForSlot(JobManager manager)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (manager.ActiveRunningJobId != null) await Task.Delay(10, timeout.Token);
    }

    [Fact]
    public async Task InitialPersistenceFailure_DoesNotPublishPhantomJobOrConsumeIdempotency()
    {
        var f = await Setup();
        int? publiclyVisibleBeforeFirstWrite = null;
        f.Manager.OnBeforeSaveJobRecord = _ =>
        {
            publiclyVisibleBeforeFirstWrite = f.Manager.GetAllJobs().Count;
            throw new IOException("synthetic-critical-password");
        };
        Assert.ThrowsAny<Exception>(() => Start(f));
        Assert.Equal(0, publiclyVisibleBeforeFirstWrite);
        Assert.Empty(f.Manager.GetAllJobs());
        Assert.Null(f.Manager.ActiveRunningJobId);
        Assert.Empty(f.Client.TargetAppendedMessages);
        f.Manager.OnBeforeSaveJobRecord = null;
        var real = Start(f);
        await WaitForSlot(f.Manager);
        Assert.Equal("completed", f.Manager.GetJob(real.JobId)!.Status);
        Assert.Single(f.Client.TargetAppendedMessages);
    }

    [Fact]
    public async Task Queue_IsDurableFifo_Bounded_Idempotent_AndRunsOneWorker()
    {
        var f = await Setup();
        var permits = new SemaphoreSlim(0);
        int entered = 0;
        f.Client.BeforeAppendAsync = async _ =>
        {
            Interlocked.Increment(ref entered);
            await permits.WaitAsync();
        };

        var active = Start(f, "queue-active");
        await WaitUntil(() => Volatile.Read(ref entered) == 1);
        var queued = Enumerable.Range(0, 32).Select(i => Enqueue(f, $"queue-{i:D2}")).ToArray();
        Assert.All(queued, job =>
        {
            Assert.Equal("queued", job.Status);
            Assert.True(job.WaitingAtShutdown);
            Assert.True(job.NeverStartedQueued);
        });
        Assert.Equal(queued[0].JobId, Enqueue(f, "queue-00").JobId);
        Assert.Throws<InvalidOperationException>(() => Enqueue(f, "queue-overflow"));
        Assert.Equal(33, f.Manager.GetAllJobs().Count);
        var activePage = f.Manager.GetJobsPage(pageSize: 100, status: "active", jobKind: "imap-transfer");
        Assert.Equal(33, activePage.TotalCount);
        Assert.All(activePage.Items, job => Assert.Equal("imap-transfer", job.JobKind));

        permits.Release();
        await WaitUntil(() => f.Manager.ActiveRunningJobId == queued[0].JobId && Volatile.Read(ref entered) == 2);
        Assert.Equal("converting", f.Manager.GetJob(queued[0].JobId)!.Status);
        Assert.False(f.Manager.GetJob(queued[0].JobId)!.WaitingAtShutdown);

        permits.Release(32);
        await WaitForSlot(f.Manager);
        Assert.Equal(33, f.Client.TargetAppendedMessages.Count);
        Assert.Equal("completed", f.Manager.GetJob(active.JobId)!.Status);
        Assert.All(queued, job => Assert.Equal("completed", f.Manager.GetJob(job.JobId)!.Status));
    }

    [Fact]
    public async Task PendingCancel_IsScopeSafePersistFirst_AndRestartRequiresReplan()
    {
        var f = await Setup();
        var permits = new SemaphoreSlim(0);
        int entered = 0;
        f.Client.BeforeAppendAsync = async _ =>
        {
            Interlocked.Increment(ref entered);
            await permits.WaitAsync();
        };
        Start(f, "cancel-active");
        await WaitUntil(() => Volatile.Read(ref entered) == 1);
        var queued = Enqueue(f, "cancel-pending");

        Assert.Throws<InvalidOperationException>(() => f.Manager.CancelPendingJob(queued.JobId, "other", "project"));
        f.Manager.OnBeforeSaveJobRecord = record =>
        {
            if (record.JobId == queued.JobId && record.Status == "cancelled") throw new IOException("cancel-save-fault");
        };
        Assert.Throws<IOException>(() => f.Manager.CancelPendingJob(queued.JobId, "company", "project"));
        Assert.Equal("queued", f.Manager.GetJob(queued.JobId)!.Status);
        f.Manager.OnBeforeSaveJobRecord = null;

        var restarted = new JobManager(f.Directory);
        var recovered = restarted.GetJob(queued.JobId)!;
        Assert.Equal("interrupted", recovered.Status);
        Assert.True(recovered.NeverStartedQueued);
        Assert.Contains("yeniden plan", recovered.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() => restarted.ResumeImapTransferJob(
            queued.JobId, Scope(), f.Store, f.Factory, f.Journal));

        var cancelled = f.Manager.CancelPendingJob(queued.JobId, "company", "project");
        Assert.Equal("cancelled", cancelled.Status);
        permits.Release();
        await WaitForSlot(f.Manager);
        Assert.Single(f.Client.TargetAppendedMessages);
    }

    [Fact]
    public async Task QueuedInitialPersistenceFailure_RollsBackWithoutPhantomOrPoisonedKey()
    {
        var f = await Setup();
        var permits = new SemaphoreSlim(0);
        int entered = 0;
        f.Client.BeforeAppendAsync = async _ =>
        {
            Interlocked.Increment(ref entered);
            await permits.WaitAsync();
        };
        Start(f, "persist-active");
        await WaitUntil(() => Volatile.Read(ref entered) == 1);
        f.Manager.OnBeforeSaveJobRecord = record =>
        {
            if (record.IdempotencyKey == "queued-save-fail") throw new IOException("queued-save-fault");
        };
        Assert.Throws<IOException>(() => Enqueue(f, "queued-save-fail"));
        Assert.DoesNotContain(f.Manager.GetAllJobs(), job => job.IdempotencyKey == "queued-save-fail");
        f.Manager.OnBeforeSaveJobRecord = null;
        var queued = Enqueue(f, "queued-save-fail");
        Assert.Equal("queued", queued.Status);
        permits.Release(2);
        await WaitForSlot(f.Manager);
    }

    [Fact]
    public async Task ActivationPersistenceFailure_DoesNotCreateGhostActive_AndDispatchesNext()
    {
        var f = await Setup();
        var permits = new SemaphoreSlim(0);
        int entered = 0;
        f.Client.BeforeAppendAsync = async _ =>
        {
            Interlocked.Increment(ref entered);
            await permits.WaitAsync();
        };
        Start(f, "activation-active");
        await WaitUntil(() => Volatile.Read(ref entered) == 1);
        var failing = Enqueue(f, "activation-fail");
        var next = Enqueue(f, "activation-next");
        f.Manager.OnBeforeSaveJobRecord = record =>
        {
            if (record.JobId == failing.JobId && !record.WaitingAtShutdown) throw new IOException("activation-save-fault");
        };

        permits.Release();
        await WaitUntil(() => f.Manager.ActiveRunningJobId == next.JobId && Volatile.Read(ref entered) == 2);
        Assert.Equal("failed", f.Manager.GetJob(failing.JobId)!.Status);
        permits.Release();
        await WaitForSlot(f.Manager);
        Assert.Equal("completed", f.Manager.GetJob(next.JobId)!.Status);
        Assert.Equal(2, f.Client.TargetAppendedMessages.Count);
    }

    [Fact]
    public async Task QueuedConversion_ReceivesStartedStateOnlyWhenActivated()
    {
        var f = await Setup();
        var permit = new SemaphoreSlim(0);
        int entered = 0;
        f.Client.BeforeAppendAsync = async _ =>
        {
            Interlocked.Increment(ref entered);
            await permit.WaitAsync();
        };
        Start(f, "mixed-active");
        await WaitUntil(() => Volatile.Read(ref entered) == 1);
        string source = Path.Combine(f.Directory, "synthetic.ost");
        string target = Path.Combine(f.Directory, "synthetic.pst");
        byte[] bytes = { 1, 2, 3, 4 };
        File.WriteAllBytes(source, bytes);
        var activated = new TaskCompletionSource<LocalJobRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        LocalJobRecord queued = f.Manager.StartJob(source, target, "mixed-convert", Scope(),
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), 1, enqueueIfBusy: true);
        Assert.Null(queued.StartedAt);
        Assert.True(queued.NeverStartedQueued);
        f.Manager.OnBeforeSaveJobRecord = record =>
        {
            if (record.JobId == queued.JobId && !record.WaitingAtShutdown && record.StartedAt != null)
                activated.TrySetResult(record);
        };

        permit.Release();
        LocalJobRecord running = await activated.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("converting", running.Status);
        Assert.False(running.NeverStartedQueued);
        await WaitForSlot(f.Manager);
    }

    [Fact]
    public async Task QueuedResume_PreservesResumeEligibilityAcrossCancelAndRestart()
    {
        var f = await Setup();
        f.Manager.OnBeforeSaveJobRecord = record =>
        {
            if (record.Status == "completed") throw new IOException("make-resumable");
        };
        var resumable = Start(f, "resume-source");
        await WaitForSlot(f.Manager);
        Assert.Equal("failed", f.Manager.GetJob(resumable.JobId)!.Status);
        f.Manager.OnBeforeSaveJobRecord = null;

        var permit = new SemaphoreSlim(0);
        int entered = 0;
        f.Client.BeforeAppendAsync = async _ =>
        {
            Interlocked.Increment(ref entered);
            await permit.WaitAsync();
        };
        var blocker = Start(f, "resume-blocker");
        await WaitUntil(() => Volatile.Read(ref entered) == 1);
        Assert.Equal(blocker.JobId, f.Manager.ResumeImapTransferJob(
            blocker.JobId, Scope(), f.Store, f.Factory, f.Journal, enqueueIfBusy: true).JobId);
        Assert.Equal(0, f.Manager.PendingJobCount);
        var queuedResume = f.Manager.ResumeImapTransferJob(
            resumable.JobId, Scope(), f.Store, f.Factory, f.Journal, enqueueIfBusy: true);
        Assert.True(queuedResume.WaitingAtShutdown);
        Assert.False(queuedResume.NeverStartedQueued);
        Assert.Equal(1, f.Manager.PendingJobCount);
        Assert.Equal(queuedResume.JobId, f.Manager.ResumeImapTransferJob(
            resumable.JobId, Scope(), f.Store, f.Factory, f.Journal, enqueueIfBusy: true).JobId);
        Assert.Equal(1, f.Manager.PendingJobCount);

        string restartCopy = Path.Combine(Path.GetTempPath(), "bitigmail-resume-copy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(restartCopy);
        foreach (string dir in Directory.GetDirectories(f.Directory, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(f.Directory, restartCopy));
        foreach (string file in Directory.GetFiles(f.Directory, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(f.Directory, restartCopy), overwrite: true);
        var restarted = new JobManager(restartCopy);
        var recovered = restarted.GetJob(resumable.JobId)!;
        Assert.Equal("interrupted", recovered.Status);
        Assert.False(recovered.NeverStartedQueued);
        Assert.DoesNotContain("yeniden plan", recovered.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);

        var cancelled = f.Manager.CancelPendingJob(resumable.JobId, "company", "project");
        Assert.Equal("interrupted", cancelled.Status);
        Assert.False(cancelled.NeverStartedQueued);
        var requeued = f.Manager.ResumeImapTransferJob(
            resumable.JobId, Scope(), f.Store, f.Factory, f.Journal, enqueueIfBusy: true);
        Assert.Equal("queued", requeued.Status);
        Assert.False(requeued.NeverStartedQueued);
        f.Manager.CancelPendingJob(resumable.JobId, "company", "project");
        permit.Release();
        await WaitForSlot(f.Manager);
        try { Directory.Delete(restartCopy, recursive: true); } catch { }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalPersistenceFailure_NeverPublishesCompleted_ReleasesSlot_PreservesTargetEvidence(bool failReport)
    {
        var f = await Setup();
        string? publiclyVisibleBeforeCompletedWrite = null;
        if (failReport) f.Manager.OnBeforeSaveReport = _ => throw new IOException("synthetic-critical-password");
        else f.Manager.OnBeforeSaveJobRecord = record =>
        {
            if (record.Status != "completed") return;
            publiclyVisibleBeforeCompletedWrite = f.Manager.GetJob(record.JobId)?.Status;
            throw new IOException("synthetic-critical-password");
        };
        var job = Start(f);
        await WaitForSlot(f.Manager);
        var record = f.Manager.GetJob(job.JobId)!;
        Assert.NotEqual("completed", record.Status);
        Assert.Contains(record.Status, new[] { "failed", "interrupted" });
        if (!failReport) Assert.NotEqual("completed", publiclyVisibleBeforeCompletedWrite);
        Assert.DoesNotContain("synthetic-critical-password", record.ErrorMessage ?? "");
        var entry = Assert.Single(f.Journal.GetJournal(job.JobId)!.Entries.Values);
        Assert.Equal(ImapTransferItemStatus.Verified, entry.Status);
        Assert.NotNull(entry.TargetUid);
        Assert.Single(f.Client.TargetAppendedMessages);
        Assert.NotEqual("completed", new JobManager(f.Directory).GetJob(job.JobId)!.Status);
    }

    [Fact]
    public async Task RestartIdempotency_RequiresTheSamePersistedPlan()
    {
        var f = await Setup();
        var first = Start(f);
        await WaitForSlot(f.Manager);
        Assert.Equal("completed", f.Manager.GetJob(first.JobId)!.Status);
        var restarted = new JobManager(f.Directory);
        var same = restarted.StartImapTransferJob(f.Plan, "critical-key", Scope(), f.Store, f.Factory, f.Journal);
        Assert.Equal(first.JobId, same.JobId);
        var different = f.Journal.GetPlan(f.Plan.PlanId)!;
        different.PlanId = different.PreviewId = "preview-different";
        f.Journal.SavePlan(different);
        Assert.Throws<InvalidOperationException>(() => restarted.StartImapTransferJob(different, "critical-key", Scope(), f.Store, f.Factory, f.Journal));
        Assert.Single(f.Client.TargetAppendedMessages);
    }

    [Theory]
    [InlineData(false, "date")]
    [InlineData(false, "flag")]
    [InlineData(false, "keyword")]
    [InlineData(true, "date")]
    [InlineData(true, "flag")]
    [InlineData(true, "keyword")]
    public async Task Resume_RechecksTargetMetadataEvenWhenRawBytesStillMatch(bool ambiguousIntent, string changedField)
    {
        var f = await Setup();
        var job = Start(f);
        await WaitForSlot(f.Manager);
        Assert.Equal("completed", f.Manager.GetJob(job.JobId)!.Status);
        var entry = Assert.Single(f.Journal.GetJournal(job.JobId)!.Entries.Values);
        if (ambiguousIntent)
        {
            entry.Status = ImapTransferItemStatus.AppendIntent;
            f.Journal.UpdateEntry(job.JobId, entry);
        }
        var source = Assert.Single(await f.Client.InspectSourceFolderAsync("INBOX", CancellationToken.None));
        f.Client.AddTargetMessage("Target", entry.TargetUid!.Value, source.RawBytes,
            changedField == "flag" ? MessageFlags.None : MessageFlags.Seen,
            changedField == "keyword" ? new() : new() { entry.BitigMailKeyword! },
            changedField == "date" ? source.InternalDateUtc.AddHours(1) : source.InternalDateUtc);
        var resumed = f.Manager.GetJob(job.JobId)!;
        resumed.Status = "interrupted";
        var worker = new ImapTransferWorker(f.Store, f.Factory, f.Journal, _ => { }, _ => { });
        await worker.ExecuteAsync(resumed, f.Plan, CancellationToken.None);
        Assert.NotEqual("completed", resumed.Status);
        Assert.Single(f.Client.TargetAppendedMessages);
    }

    [Fact]
    public async Task AmbiguousFirstItem_StopsBeforeAppendingLaterPlannedItems()
    {
        var f = await Setup(2);
        var state = f.Journal.InitializeJournal("job-ambiguous", f.Plan);
        var first = state.Entries[f.Plan.Items[0].ItemId];
        first.Status = ImapTransferItemStatus.AppendIntent;
        first.BitigMailKeyword = "bm_0123456789abcdef0123456789abcdef";
        first.TargetUidValidity = 12345;
        f.Journal.UpdateEntry(state.JobId, first);
        var job = new LocalJobRecord { JobId = state.JobId, JobKind = "imap-transfer", Status = "interrupted", ClientContext = Scope() };
        var worker = new ImapTransferWorker(f.Store, f.Factory, f.Journal, _ => { }, _ => { });
        await worker.ExecuteAsync(job, f.Plan, CancellationToken.None);
        Assert.NotEqual("completed", job.Status);
        Assert.Empty(f.Client.TargetAppendedMessages);
        Assert.Equal(ImapTransferItemStatus.Planned, f.Journal.GetJournal(state.JobId)!.Entries[f.Plan.Items[1].ItemId].Status);
    }

    [Fact]
    public async Task InitialReauthorizationInterruptionRetainsJournalAndResumesSameJob()
    {
        var f = await Setup();
        var resolver = new ControlledResolver(f.Store) { RequireAuthorization = true };
        var job = f.Manager.StartImapTransferJob(f.Plan, "oauth-resume", Scope(), f.Store, f.Factory, f.Journal, resolver);
        await WaitForSlot(f.Manager);
        Assert.Equal("interrupted", f.Manager.GetJob(job.JobId)!.Status);
        Assert.NotNull(f.Journal.GetJournal(job.JobId));
        Assert.Empty(f.Client.TargetAppendedMessages);
        resolver.RequireAuthorization = false;
        f.Manager.ResumeImapTransferJob(job.JobId, Scope(), f.Store, f.Factory, f.Journal, resolver);
        await WaitForSlot(f.Manager);
        Assert.Equal("completed", f.Manager.GetJob(job.JobId)!.Status);
        Assert.Single(f.Client.TargetAppendedMessages);
    }

    [Fact]
    public async Task CacheCommitFailureCannotPublishCompletedTransfer()
    {
        var f = await Setup();
        var resolver = new ControlledResolver(f.Store) { FailCommit = true };
        bool completedPublished = false;
        f.Manager.OnBeforeSaveJobRecord = record => { if (record.Status == "completed") completedPublished = true; };
        var job = f.Manager.StartImapTransferJob(f.Plan, "oauth-cache-failure", Scope(), f.Store, f.Factory, f.Journal, resolver);
        await WaitForSlot(f.Manager);
        Assert.False(completedPublished);
        Assert.NotEqual("completed", f.Manager.GetJob(job.JobId)!.Status);
        Assert.Single(f.Client.TargetAppendedMessages);
        Assert.Equal(ImapTransferItemStatus.Verified, Assert.Single(f.Journal.GetJournal(job.JobId)!.Entries.Values).Status);
    }

    private sealed class ControlledResolver(ImapAccountStore store) : IImapCredentialResolver
    {
        public bool RequireAuthorization;
        public bool FailCommit;
        public async Task<ResolvedImapCredential> ResolveCredentialAsync(string id, string company, string project, CancellationToken ct = default)
        {
            if (RequireAuthorization) throw new ReauthorizationRequiredException(id, "Yeniden yetkilendirme gerekli.");
            var resolved = await new ImapCredentialResolver(store, new WindowsImapCredentialProtector(), new MsalMicrosoftAuthProvider()).ResolveCredentialAsync(id, company, project, ct);
            return new ResolvedImapCredential
            {
                Account = resolved.Account, Credential = resolved.Credential,
                CommitAsync = FailCommit ? () => Task.FromException(new IOException("synthetic-secret-cache-failure")) : null
            };
        }
    }
}

