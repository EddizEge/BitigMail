using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Models;
using BitigMail.LocalHost.Jobs;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class DesktopShutdownTests
{
    private static string Profile() => Path.Combine(Path.GetTempPath(), "bitigmail-shutdown-" + Guid.NewGuid().ToString("N"));
    private static ClientProjectContext Owner() => new() { CompanyId = "company", ProjectId = "project" };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownDrainsActiveWorkAndPreservesQueuedWork(bool failPersistence)
    {
        string profile = Profile();
        var manager = new JobManager(profile);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int queuedExecuted = 0;
        var active = manager.ScheduleStage7Job("convert", "source", "target", Owner(), async record =>
        {
            started.SetResult();
            await release.Task;
            record.Status = "completed";
            manager.UpdateRecovery(record);
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pending = manager.ScheduleStage7Job("convert", "next", "next-target", Owner(), _ => { Interlocked.Increment(ref queuedExecuted); return Task.CompletedTask; });
        try
        {
            if (failPersistence)
            {
                manager.OnBeforeSaveJobRecord = record => { if (record.JobId == pending.JobId) throw new IOException("Injected journal failure"); };
                Assert.Throws<IOException>(() => manager.BeginDesktopShutdown());
                Assert.True(manager.GetDesktopShutdownStatus().PersistenceFailure);
                manager.OnBeforeSaveJobRecord = null;
            }
            else Assert.False(manager.BeginDesktopShutdown().CanExit);
            Assert.Throws<InvalidOperationException>(() => manager.ScheduleStage7Job("convert", "third", "third-target", Owner(), _ => Task.CompletedTask));
            release.SetResult();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (manager.ActiveRunningJobId is not null) await Task.Delay(10, timeout.Token);
            if (failPersistence)
            {
                Assert.False(manager.GetDesktopShutdownStatus().CanExit);
                manager.BeginDesktopShutdown();
            }
            Assert.True(manager.GetDesktopShutdownStatus().CanExit);
            Assert.Equal(0, queuedExecuted);
            Assert.True(manager.GetJob(pending.JobId)!.WaitingAtShutdown);
            var reopened = new JobManager(profile);
            Assert.Equal("completed", reopened.GetJob(active.JobId)!.Status);
            Assert.Equal("interrupted", reopened.GetJob(pending.JobId)!.Status);
            Assert.True(reopened.GetJob(pending.JobId)!.NeverStartedQueued);
        }
        finally { manager.OnBeforeSaveJobRecord = null; release.TrySetResult(); }
    }

    [Fact]
    public void UnresolvedWorkerNeverClaimsSafeExit()
    {
        string profile = Profile();
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "recovery-worker-unresolved.flag"), "unresolved");
        var manager = new JobManager(profile);
        var status = manager.BeginDesktopShutdown();
        Assert.True(status.UnresolvedWorker);
        Assert.False(status.CanExit);
    }
}
