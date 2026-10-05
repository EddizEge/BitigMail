using BitigMail.Engine.Imap.OAuth;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.OAuth;
using BitigMail.LocalHost.Security;
using BitigMail.TestingHost.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class OAuthOperationAuthorizationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task OtherUserAndSameUserNewSessionCannotReadOrCancelOperation(bool google)
    {
        var f = await Create(); SetActor(f, f.Owner.Principal);
        using var runner = Run(f, google);
        string id = runner.Start(); Task completion = runner.Wait(id);
        await f.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.True(runner.Exists(id));
            SetActor(f, f.Other.Principal);
            Assert.False(runner.Exists(id)); Assert.False(runner.Cancel(id));
            SetActor(f, f.Sessions.Issue(f.Owner.Principal.UserId, f.Owner.Principal.SecurityVersion).Principal);
            Assert.False(runner.Exists(id)); Assert.False(runner.Cancel(id));
            SetActor(f, f.Owner.Principal);
            Assert.True(runner.Exists(id)); Assert.True(runner.Cancel(id));
        }
        finally { f.Release.TrySetResult(); }
        await completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(f.Store.ListAccounts(f.Company.CompanyId, f.Company.Projects[0].ProjectId));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task LogoutBeforeProviderCompletionCannotPersistAccount(bool google)
    {
        var f = await Create(); SetActor(f, f.Owner.Principal);
        using var runner = Run(f, google);
        string id = runner.Start(); Task completion = runner.Wait(id);
        await f.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Sessions.Revoke(f.Owner.Token);
        f.Release.TrySetResult();
        await completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(runner.Exists(id));
        Assert.Empty(f.Store.ListAccounts(f.Company.CompanyId, f.Company.Projects[0].ProjectId));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SecurityVersionChangeBeforeCommitCannotPersistAccount(bool google)
    {
        var f = await Create(); SetActor(f, f.Owner.Principal);
        using var runner = Run(f, google);
        string id = runner.Start(); Task completion = runner.Wait(id);
        await f.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { await f.Catalog.UpdateUserAsync(f.Other.Principal, f.Owner.Principal.UserId, active: false); }
        finally { f.Release.TrySetResult(); }
        await completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(runner.Exists(id));
        Assert.Empty(f.Store.ListAccounts(f.Company.CompanyId, f.Company.Projects[0].ProjectId));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ProductionCannotStartWithoutAuthenticatedSession(bool google)
    {
        var f = await Create(); f.Http.HttpContext = null;
        using var runner = Run(f, google);
        Assert.Throws<KeyNotFoundException>(() => runner.Start());
        Assert.False(f.Entered.Task.IsCompleted);
    }

    private static Runner Run(Fixture f, bool google)
    {
        var tester = new FakeImapOAuthConnectionTester { CustomVerifier = async (_, _, _, _, _, _) =>
            { f.Entered.TrySetResult(); await f.Release.Task; } };
        string company = f.Company.CompanyId, project = f.Company.Projects[0].ProjectId;
        if (google)
        {
            var manager = new GoogleOAuthOperationManager(f.Store, new FakeGoogleAuthProvider(), tester, f.Http, f.Catalog, f.Sessions, new());
            return new(manager, () => manager.StartOperation(new() { CompanyId = company, ProjectId = project, DisplayName = "Test", Email = "pilot@example.test",
                ClientId = "123456789-test.apps.googleusercontent.com", ClientSecret = "synthetic-client-secret" }).OperationId,
                id => manager.GetOperation(id, company, project) is not null, id => manager.Cancel(id, company, project) is not null, manager.WaitForCompletionAsync);
        }
        else
        {
            var manager = new MicrosoftOAuthOperationManager(f.Store, new FakeMicrosoftAuthProvider(), tester, f.Http, f.Catalog, f.Sessions, new());
            return new(manager, () => manager.StartOperation(new() { CompanyId = company, ProjectId = project, DisplayName = "Test", Email = "pilot@example.test",
                TenantId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", ClientId = "11111111-2222-3333-4444-555555555555" }).OperationId,
                id => manager.GetOperation(id, company, project) is not null, id => manager.CancelOperation(id, company, project), manager.WaitForCompletionAsync);
        }
    }

    private static async Task<Fixture> Create()
    {
        string root = Path.Combine(Path.GetTempPath(), "bitigmail-root038-oauth-" + Guid.NewGuid().ToString("N"));
        var catalog = new IdentityCatalog(Path.Combine(root, "identity"));
        var user = await catalog.BootstrapAdministratorAsync("admin", "correct horse battery staple", () => true);
        var sessions = new AuthenticatedSessionRegistry(); var owner = sessions.Issue(user.UserId, user.SecurityVersion);
        var company = await catalog.CreateCompanyAsync(owner.Principal, "Company", "Project");
        var other = await catalog.CreateUserAsync(owner.Principal, "second-admin", "correct horse battery staple", LocalUserRole.Admin, null);
        var store = new ImapAccountStore(Path.Combine(root, "accounts"), new WindowsImapCredentialProtector(), new(true));
        return new(catalog, sessions, owner, sessions.Issue(other.UserId, other.SecurityVersion), company, store, new(),
            new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously));
    }
    private static void SetActor(Fixture f, AuthenticatedSessionPrincipal actor)
    { f.Http.HttpContext = new DefaultHttpContext(); f.Http.HttpContext.Items[typeof(AuthenticatedSessionPrincipal)] = actor; }
    private sealed record Runner(IDisposable Manager, Func<string> Start, Func<string, bool> Exists, Func<string, bool> Cancel, Func<string, Task> Wait) : IDisposable
    { public void Dispose() => Manager.Dispose(); }
    private sealed record Fixture(IdentityCatalog Catalog, AuthenticatedSessionRegistry Sessions, IssuedAuthenticatedSession Owner,
        IssuedAuthenticatedSession Other, LocalCompany Company, ImapAccountStore Store, HttpContextAccessor Http, TaskCompletionSource Entered, TaskCompletionSource Release);
}
