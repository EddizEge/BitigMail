using BitigMail.Engine.Models;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class JobDispatchAuthorizationTests
{
    private static async Task<Fixture> Create()
    {
        string root = Path.Combine(Path.GetTempPath(), "bitigmail-root038-dispatch-" + Guid.NewGuid().ToString("N"));
        var catalog = new IdentityCatalog(Path.Combine(root, "identity"));
        var admin = await catalog.BootstrapAdministratorAsync("admin", "correct horse battery staple", () => true);
        var administrator = new AuthenticatedSessionPrincipal(admin.UserId, "admin-session", admin.SecurityVersion);
        var company = await catalog.CreateCompanyAsync(administrator, "Company", "Project");
        var other = await catalog.CreateCompanyAsync(administrator, "Other", "Project");
        var user = await catalog.CreateUserAsync(administrator, "operator", "correct horse battery staple", LocalUserRole.Operator,
            new[] { new CompanyGrant(company.CompanyId, new[] { company.Projects[0].ProjectId }) });
        var http = new HttpContextAccessor();
        var manager = new JobManager(Path.Combine(root, "jobs-runtime"), httpContextAccessor: http, identityCatalog: catalog);
        return new(root, catalog, administrator, new(user.UserId, "operator-session", user.SecurityVersion), http, manager,
            new() { CompanyId = company.CompanyId, ProjectId = company.Projects[0].ProjectId },
            new(other.CompanyId, other.Projects[0].ProjectId));
    }
    private static void SetActor(Fixture fixture)
    {
        fixture.Http.HttpContext = new DefaultHttpContext();
        fixture.Http.HttpContext.Items[typeof(AuthenticatedSessionPrincipal)] = fixture.Operator;
    }
    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    [Fact]
    public async Task ProductionIdleStartWithoutActorIsRejected()
    {
        var f = await Create(); f.Http.HttpContext = null;
        Assert.Throws<UnauthorizedAccessException>(() => f.Jobs.ScheduleRecovery("source", "hash", f.Owner, _ => throw new Exception("Must not execute")));
        Assert.Null(f.Jobs.ActiveRunningJobId); Assert.Empty(f.Jobs.GetAllJobs());
    }

    [Fact]
    public async Task TargetPermissionCannotAuthorizeForeignSourceScope()
    {
        var f = await Create(); SetActor(f);
        Assert.Throws<UnauthorizedAccessException>(() => f.Jobs.ScheduleStage7Job("test", "source", "target", f.Owner,
            _ => throw new Exception("Must not execute"), requiredScopes: new[] { f.ForeignScope }));
        Assert.Empty(f.Jobs.GetAllJobs());
    }

    [Fact]
    public async Task RevokedQueuedWorkNeverExecutesAndFailureIsPersisted()
    {
        var f = await Create(); SetActor(f);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int executed = 0;
        f.Jobs.ScheduleRecovery("active", "hash", f.Owner, async _ => { entered.SetResult(); await release.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pending = f.Jobs.ScheduleRecovery("pending", "hash", f.Owner, _ => { Interlocked.Increment(ref executed); return Task.CompletedTask; });
        try { await f.Catalog.UpdateUserAsync(f.Admin, f.Operator.UserId, grants: Array.Empty<CompanyGrant>()); }
        finally { release.TrySetResult(); }
        await WaitUntil(() => f.Jobs.ActiveRunningJobId is null);
        Assert.Equal(0, executed); Assert.Equal("failed", f.Jobs.GetJob(pending.JobId)!.Status);
        Assert.Equal("failed", new JobManager(f.Jobs.RuntimeDirectory).GetJob(pending.JobId)!.Status);
    }

    [Fact]
    public async Task RevocationBetweenAdmissionAndFirstLaunchIsChecked()
    {
        var f = await Create(); SetActor(f); int executed = 0, changed = 0;
        f.Jobs.OnBeforeSaveJobRecord = _ =>
        {
            if (Interlocked.Exchange(ref changed, 1) == 0)
                f.Catalog.UpdateUserAsync(f.Admin, f.Operator.UserId, active: false).GetAwaiter().GetResult();
        };
        var job = f.Jobs.ScheduleRecovery("idle", "hash", f.Owner, _ => { Interlocked.Increment(ref executed); return Task.CompletedTask; });
        await WaitUntil(() => f.Jobs.ActiveRunningJobId is null);
        Assert.Equal(0, executed); Assert.Equal("failed", f.Jobs.GetJob(job.JobId)!.Status);
    }

    [Fact]
    public async Task CorruptCatalogDuringQueueBecomesVisibleFailure()
    {
        var f = await Create(); SetActor(f);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Jobs.ScheduleRecovery("active", "hash", f.Owner, async _ => { entered.SetResult(); await release.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pending = f.Jobs.ScheduleRecovery("pending", "hash", f.Owner, _ => throw new Exception("Must not execute"));
        File.WriteAllText(Path.Combine(f.Root, "identity", "identity-catalog.json"), "{broken");
        release.SetResult(); await WaitUntil(() => f.Jobs.ActiveRunningJobId is null);
        Assert.Equal("failed", f.Jobs.GetJob(pending.JobId)!.Status);
        Assert.Contains("doğrulanamadığı", f.Jobs.GetJob(pending.JobId)!.ErrorMessage);
    }

    [Fact]
    public async Task SecurityRefusalPersistenceFailureLatchesDispatchAcrossRestart()
    {
        var f = await Create(); SetActor(f);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Jobs.ScheduleRecovery("active", "hash", f.Owner, async _ => { entered.SetResult(); await release.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Jobs.ScheduleRecovery("pending", "hash", f.Owner, _ => throw new Exception("Must not execute"));
        f.Jobs.OnBeforeSaveJobRecord = record => { if (record.Status == "failed") throw new IOException("Simulated write failure"); };
        await f.Catalog.UpdateUserAsync(f.Admin, f.Operator.UserId, active: false);
        release.SetResult(); await WaitUntil(() => File.Exists(Path.Combine(f.Jobs.RuntimeDirectory, "security-dispatch-unresolved.flag")));
        var reopened = new JobManager(f.Jobs.RuntimeDirectory);
        Assert.Throws<InvalidOperationException>(() => reopened.ScheduleRecovery("next", "hash", f.Owner, _ => Task.CompletedTask));
    }

    private sealed record Fixture(string Root, IdentityCatalog Catalog, AuthenticatedSessionPrincipal Admin,
        AuthenticatedSessionPrincipal Operator, HttpContextAccessor Http, JobManager Jobs, ClientProjectContext Owner, JobAuthorizationScope ForeignScope);
}
