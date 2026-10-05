using System.Text;
using System.Text.Json;
using BitigMail.Engine.Imap;
using BitigMail.Engine.Imap.OAuth;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.OAuth;
using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public class Microsoft365LifecycleCriticalTests
{
    private const string Tenant = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string Client = "11111111-2222-3333-4444-555555555555";
    private const string Mailbox = "test@example.test";
    private const string Home = "home.tenant";
    private const string SecretSentinel = "never-expose-this-oauth-token";
    private static StartMicrosoftOAuthRequest Request(string? id = null, long? version = null) => new()
    {
        CompanyId = "critical-company", ProjectId = "critical-project", DisplayName = "Pilot",
        Email = Mailbox, ClientId = Client, TenantId = Tenant, AccountId = id, ExpectedVersion = version
    };
    private static MicrosoftAuthResult Auth(string username = Mailbox) => new()
    {
        Username = username, TenantId = Tenant, HomeAccountId = Home, AccessToken = SecretSentinel,
        SerializedCache = Encoding.UTF8.GetBytes("synthetic-cache-original")
    };
    private static ImapAccountStore Store(out WindowsImapCredentialProtector protector)
    {
        protector = new WindowsImapCredentialProtector();
        return new ImapAccountStore(Path.Combine(Path.GetTempPath(), "bitigmail-task015-qa", "critical-" + Guid.NewGuid().ToString("N")), protector, new ImapConnectionPolicy());
    }
    private static TaskCompletionSource<bool> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task Finish(MicrosoftOAuthOperationManager manager, string id) =>
        manager.WaitForCompletionAsync(id).WaitAsync(TimeSpan.FromSeconds(8));

    [Fact]
    public async Task CancelAfterCommitReturnsConnectedAccountInsteadOfFalseCancellation()
    {
        var store = Store(out _);
        using var manager = new MicrosoftOAuthOperationManager(store, new Provider(), new Tester());
        var op = manager.StartOperation(Request());
        await Finish(manager, op.OperationId);
        var result = manager.CancelAndGetResult(op.OperationId, "critical-company", "critical-project")!;
        var account = Assert.Single(store.ListAccounts("critical-company", "critical-project"));
        Assert.Equal("connected", result.Status);
        Assert.Equal(account.AccountId, result.AccountId);
        Assert.DoesNotContain("başarıyla iptal", result.Message);
        Assert.DoesNotContain(SecretSentinel, JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task CancelAfterFailurePreservesFailedStatus()
    {
        var store = Store(out _);
        using var manager = new MicrosoftOAuthOperationManager(store,
            new Provider { InteractiveError = new InvalidOperationException(SecretSentinel) }, new Tester());
        var op = manager.StartOperation(Request());
        await Finish(manager, op.OperationId);
        var result = manager.CancelAndGetResult(op.OperationId, "critical-company", "critical-project")!;
        Assert.Equal("failed", result.Status);
        Assert.Null(result.AccountId);
        Assert.Empty(store.ListAccounts("critical-company", "critical-project"));
        Assert.DoesNotContain(SecretSentinel, JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task CancellationDuringVerifierCannotCreateHiddenAccount()
    {
        var store = Store(out _); var entered = Gate(); var release = Gate();
        var tester = new Tester(async () => { entered.TrySetResult(true); await release.Task; });
        using var manager = new MicrosoftOAuthOperationManager(store, new Provider(), tester);
        var op = manager.StartOperation(Request());
        try {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(manager.CancelOperation(op.OperationId, "critical-company", "critical-project"));
        } finally { release.TrySetResult(true); }
        await Finish(manager, op.OperationId);
        Assert.Empty(store.ListAccounts("critical-company", "critical-project"));
        Assert.Empty(Directory.GetFiles(store.StorageDirectory, "acc_*.json"));
        Assert.Equal("cancelled", manager.GetOperation(op.OperationId, "critical-company", "critical-project")!.Status);
    }

    [Fact]
    public async Task ProviderExceptionMessageCannotBecomePublicError()
    {
        var store = Store(out _);
        using var manager = new MicrosoftOAuthOperationManager(store, new Provider { InteractiveError = new InvalidOperationException(SecretSentinel) }, new Tester());
        var op = manager.StartOperation(Request());
        await Finish(manager, op.OperationId);
        var status = manager.GetOperation(op.OperationId, "critical-company", "critical-project")!;
        Assert.Equal("failed", status.Status);
        Assert.DoesNotContain(SecretSentinel, JsonSerializer.Serialize(status));
        Assert.Empty(store.ListAccounts("critical-company", "critical-project"));
    }

    [Fact]
    public async Task FailedPersistenceNeverPublishesConnected()
    {
        var store = Store(out _);
        store.OnBeforeAtomicWrite = _ => throw new IOException(SecretSentinel);
        using var manager = new MicrosoftOAuthOperationManager(store, new Provider(), new Tester());
        var op = manager.StartOperation(Request());
        await Finish(manager, op.OperationId);
        var status = manager.GetOperation(op.OperationId, "critical-company", "critical-project")!;
        Assert.Equal("failed", status.Status);
        Assert.Null(status.AccountId);
        Assert.DoesNotContain(SecretSentinel, JsonSerializer.Serialize(status));
        Assert.Empty(store.ListAccounts("critical-company", "critical-project"));
    }

    [Fact]
    public async Task WrongMailboxIsBlockedBeforeConnectingToMailServer()
    {
        var store = Store(out _); var tester = new Tester();
        using var manager = new MicrosoftOAuthOperationManager(store, new Provider { Identity = Auth("different@example.test") }, tester);
        var op = manager.StartOperation(Request());
        await Finish(manager, op.OperationId);
        Assert.Equal(0, tester.Calls);
        Assert.Equal("failed", manager.GetOperation(op.OperationId, "critical-company", "critical-project")!.Status);
        Assert.Empty(store.ListAccounts("critical-company", "critical-project"));
    }

    [Fact]
    public async Task DeletedAccountCannotBeResurrectedByReconnect()
    {
        var store = Store(out _);
        var account = store.CreateOAuthAccount("critical-company", "critical-project", "Pilot", Mailbox, Tenant, Client, Home, Auth().SerializedCache);
        var entered = Gate(); var release = Gate();
        using var manager = new MicrosoftOAuthOperationManager(store, new Provider(), new Tester(async () => { entered.TrySetResult(true); await release.Task; }));
        var op = manager.StartOperation(Request(account.AccountId, account.Version));
        try {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(store.DeleteAccount(account.AccountId, "critical-company", "critical-project", account.Version));
        } finally { release.TrySetResult(true); }
        await Finish(manager, op.OperationId);
        Assert.Empty(store.ListAccounts("critical-company", "critical-project"));
        Assert.Equal("failed", manager.GetOperation(op.OperationId, "critical-company", "critical-project")!.Status);
    }

    [Fact]
    public async Task StaleRefreshCannotOverwriteNewReconnectCache()
    {
        var store = Store(out var protector);
        var account = store.CreateOAuthAccount("critical-company", "critical-project", "Pilot", Mailbox, Tenant, Client, Home, Auth().SerializedCache);
        var provider = new Provider();
        var resolver = new ImapCredentialResolver(store, protector, provider);
        await using var resolved = await resolver.ResolveCredentialAsync(account.AccountId, "critical-company", "critical-project");
        var newCache = Encoding.UTF8.GetBytes("new-reconnected-cache");
        var updated = store.UpdateOAuthAccountOnReconnect(account.AccountId, "critical-company", "critical-project", account.Version, Home, newCache);
        Assert.Equal(account.Version, updated.Version);
        Assert.NotNull(resolved.CommitAsync);
        await Assert.ThrowsAnyAsync<Exception>(() => resolved.CommitAsync!());
        var (_, cipher) = store.GetAccountRecordAndCipher(account.AccountId, "critical-company", "critical-project");
        Assert.Equal(newCache, Convert.FromBase64String(protector.Unprotect(cipher, account.AccountId)));
    }

    [Fact]
    public async Task DeletedAccountRefreshCommitMustReportFailure()
    {
        var store = Store(out var protector);
        var account = store.CreateOAuthAccount("critical-company", "critical-project", "Pilot", Mailbox, Tenant, Client, Home, Auth().SerializedCache);
        var resolver = new ImapCredentialResolver(store, protector, new Provider());
        await using var resolved = await resolver.ResolveCredentialAsync(account.AccountId, "critical-company", "critical-project");
        Assert.True(store.DeleteAccount(account.AccountId, "critical-company", "critical-project", account.Version));
        Assert.NotNull(resolved.CommitAsync);
        await Assert.ThrowsAnyAsync<Exception>(() => resolved.CommitAsync!());
        Assert.Empty(store.ListAccounts("critical-company", "critical-project"));
    }

    [Fact]
    public void DisposedManagerCannotStartAnotherAuthentication()
    {
        var store = Store(out _);
        var manager = new MicrosoftOAuthOperationManager(store, new Provider(), new Tester());
        manager.Dispose();
        Assert.ThrowsAny<Exception>(() => manager.StartOperation(Request()));
        Assert.Empty(store.ListAccounts("critical-company", "critical-project"));
    }

    [Fact]
    public void ReconnectCannotReplaceStoredHomeIdentity()
    {
        var store = Store(out _);
        var account = store.CreateOAuthAccount("critical-company", "critical-project", "Pilot", Mailbox, Tenant, Client, Home, Auth().SerializedCache);
        var before = File.ReadAllBytes(store.GetAccountFilePath(account.AccountId));
        Assert.ThrowsAny<Exception>(() => store.UpdateOAuthAccountOnReconnect(account.AccountId,
            "critical-company", "critical-project", account.Version, "different-home.tenant", Auth().SerializedCache));
        Assert.Equal(before, File.ReadAllBytes(store.GetAccountFilePath(account.AccountId)));
    }

    [Fact]
    public async Task CompetingBufferedRefreshesCannotBothOverwriteSameGeneration()
    {
        var store = Store(out var protector);
        var account = store.CreateOAuthAccount("critical-company", "critical-project", "Pilot", Mailbox, Tenant, Client, Home, Auth().SerializedCache);
        var resolver = new ImapCredentialResolver(store, protector, new Provider());
        await using var first = await resolver.ResolveCredentialAsync(account.AccountId, "critical-company", "critical-project");
        await using var second = await resolver.ResolveCredentialAsync(account.AccountId, "critical-company", "critical-project");
        await second.CommitAsync!();
        var committed = File.ReadAllBytes(store.GetAccountFilePath(account.AccountId));
        await Assert.ThrowsAnyAsync<Exception>(() => first.CommitAsync!());
        Assert.Equal(committed, File.ReadAllBytes(store.GetAccountFilePath(account.AccountId)));
        Assert.Equal(account.Version, store.GetAccount(account.AccountId, "critical-company", "critical-project")!.Version);
    }

    [Fact]
    public async Task FailedConnectionResultCannotPassOAuthVerification()
    {
        var service = new ImapClientService(new ImapConnectionPolicy());
        var tester = new RealImapOAuthConnectionTester(service);
        // Invalid endpoint is refused before any network call; its false result must still fail the registration gate.
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => tester.VerifyConnectionAsync(
            "invalid.example.test", 993, "ssl", Mailbox, SecretSentinel, CancellationToken.None));
    }

    private sealed class Tester(Func<Task>? verify = null) : IImapOAuthConnectionTester
    {
        public int Calls;
        public async Task VerifyConnectionAsync(string host, int port, string tlsMode, string username, string token, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            Assert.Equal(Microsoft365Policy.ImapHost, host);
            Assert.Equal(993, port);
            Assert.Equal("ssl", tlsMode);
            if (verify is not null) await verify();
        }
    }
    private sealed class Provider : IMicrosoftAuthProvider
    {
        public MicrosoftAuthResult Identity = Auth();
        public Exception? InteractiveError;
        public Task<MicrosoftAuthResult> AcquireTokenInteractiveAsync(string tenant, string client, string email, Action<Uri> callback, CancellationToken ct) =>
            InteractiveError is null ? Task.FromResult(Identity) : Task.FromException<MicrosoftAuthResult>(InteractiveError);
        public Task<(string AccessToken, byte[]? UpdatedCacheBytes)> AcquireTokenSilentAsync(string tenant, string client, string home, byte[] cache, CancellationToken ct) =>
            Task.FromResult<(string, byte[]?)>((SecretSentinel, Encoding.UTF8.GetBytes("stale-refreshed-cache")));
    }
}
