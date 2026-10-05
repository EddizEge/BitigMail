using BitigMail.Engine.Models;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Recovery;
using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class RecoveryLifecyclePersistenceTests
{
    [Fact] public async Task UnresolvedWorkerLatchSurvivesRestartAndRejectsNewJobs()
    {
        using var fixture = new Fixture(); var manager = new JobManager(fixture.Root);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = manager.ScheduleRecovery("source.pst", "hash", new(), _ => gate.Task);
        manager.LatchUnresolvedWorker(job); gate.SetResult(); await Task.Delay(50);
        var reopened = new JobManager(fixture.Root);
        Assert.True(reopened.GetJob(job.JobId)!.RecoveryWorkerUnresolved);
        Assert.Throws<InvalidOperationException>(() => reopened.ScheduleRecovery("next.pst", "hash", new(), _ => Task.CompletedTask));
    }
    [Fact] public async Task CrashDuringRunningRecoveryIsConservativelyUnresolved()
    {
        using var fixture = new Fixture(); var manager = new JobManager(fixture.Root);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = manager.ScheduleRecovery("source.pst", "hash", new(), _ => gate.Task);
        var reopened = new JobManager(fixture.Root);
        Assert.Equal("unresolved_worker", reopened.GetJob(job.JobId)!.RecoveryOutcome);
        Assert.Throws<InvalidOperationException>(() => reopened.ScheduleRecovery("next.pst", "hash", new(), _ => Task.CompletedTask));
        gate.SetResult(); await Task.Delay(50);
    }
    [Fact] public async Task OwnerSnapshotAndCompletedOutcomePersistWithoutInMemoryViews()
    {
        using var fixture = new Fixture(); var manager = new JobManager(fixture.Root);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new ClientProjectContext { CompanyId = "original", ProjectId = "project" };
        var job = manager.ScheduleRecovery("source.pst", "hash", owner, _ => gate.Task);
        owner.CompanyId = "changed";
        Assert.Equal("original", manager.GetJob(job.JobId)!.ClientContext.CompanyId);
        job.Status = "completed"; job.RecoveryOutcome = "partial_recovered";
        job.ItemsWritten = 2; job.RecoveryOriginalTotal = null; job.RecoveryFailedBoundaryCount = 3;
        job.QualificationIsPartial = true; manager.UpdateRecovery(job); gate.SetResult(); await Task.Delay(50);
        var reopened = new JobManager(fixture.Root);
        using var bootstrap = new RecoverySdkBootstrap(null);
        var view = new DamagedStoreRecoveryService(reopened, bootstrap).Get(job.JobId)!;
        Assert.Equal("completed", view.Status); Assert.Equal("partial_recovered", view.Outcome);
        Assert.Null(view.OriginalTotal); Assert.Equal(2, view.RecoveredCount); Assert.Equal(3, view.FailedBoundaryCount);
        Assert.True(view.Partial);
    }
    [Fact] public void BootstrapSnapshotIsIndependentOfPendingDiskChanges()
    {
        using var fixture = new Fixture(); string path = Path.Combine(fixture.Root, "invalid-test.lic");
        File.WriteAllBytes(path, [1, 2, 3]);
        var store = new AsposeLicenseConfigurationStore(fixture.Root, new WindowsImapCredentialProtector());
        var context = RecoverySdkBootstrap.CreateAtStartup(store, path);
        using var bootstrap = context.Bootstrap;
        Assert.Equal("configuration_error", context.Service.Status.LicenseState);
        Assert.Throws<InvalidOperationException>(context.Service.EnsureReady);
        File.WriteAllBytes(path, [4, 5, 6]); Assert.Equal(new byte[] { 1, 2, 3 }, bootstrap.Copy());
        var copy = bootstrap.Copy(); copy[0] = 9; Assert.Equal(1, bootstrap.Copy()[0]);
        bootstrap.Dispose(); Assert.Throws<ObjectDisposedException>(() => bootstrap.Copy());
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "bitigmail-recovery-lifecycle-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            foreach (string directory in Directory.GetDirectories(Root))
            {
                foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
            foreach (string file in Directory.GetFiles(Root)) File.Delete(file);
            Directory.Delete(Root);
        }
    }
}
