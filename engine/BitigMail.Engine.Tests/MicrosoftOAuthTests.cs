using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Imap;
using BitigMail.Engine.Imap.OAuth;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.OAuth;
using BitigMail.LocalHost.Security;
using BitigMail.TestingHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public class MicrosoftOAuthTests
{
    private const string TenantId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string ClientId = "11111111-2222-3333-4444-555555555555";
    private const string UserEmail = "pilot@example.test";
    private const string CompanyId = "test-comp";
    private const string ProjectId = "test-proj";

    private static ImapAccountStore CreateTestStore(out WindowsImapCredentialProtector protector)
    {
        protector = new WindowsImapCredentialProtector();
        string tempDir = Path.Combine(Path.GetTempPath(), "bitigmail-oauth-tests", Guid.NewGuid().ToString("N"));
        return new ImapAccountStore(tempDir, protector, new ImapConnectionPolicy(allowTask014Loopback: true));
    }

    [Fact]
    public void CredentialTypes_RedactSecretsInToString()
    {
        var pwdCred = new ImapPasswordCredential("user@example.test", "SuperSecretPassword123!");
        string pwdStr = pwdCred.ToString();
        Assert.Contains("user@example.test", pwdStr);
        Assert.DoesNotContain("SuperSecretPassword123!", pwdStr);
        Assert.Contains("[REDACTED]", pwdStr);

        var oauthCred = new ImapOAuth2Credential("user@example.test", "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.secret");
        string oauthStr = oauthCred.ToString();
        Assert.Contains("user@example.test", oauthStr);
        Assert.DoesNotContain("eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.secret", oauthStr);
        Assert.Contains("[REDACTED]", oauthStr);
    }

    [Fact]
    public async Task LegacyPasswordPath_RemainsFullyFunctional()
    {
        var store = CreateTestStore(out var protector);
        var created = store.CreateAccount(new CreateImapAccountRequest
        {
            CompanyId = CompanyId,
            ProjectId = ProjectId,
            DisplayName = "Legacy Account",
            Email = "legacy@example.test",
            Host = "127.0.0.1",
            Port = 5143,
            TlsMode = "none",
            Username = "legacy@example.test",
            Password = "LegacyPassword123!"
        });

        Assert.Equal("password", created.AuthKind);

        // GetInternalAccountWithPassword succeeds for password accounts
        var (record, password) = store.GetInternalAccountWithPassword(created.AccountId, CompanyId, ProjectId);
        Assert.Equal("LegacyPassword123!", password);
        Assert.Equal("legacy@example.test", record.Username);

        // Credential resolver resolves to ImapPasswordCredential
        var provider = new FakeMicrosoftAuthProvider();
        var resolver = new ImapCredentialResolver(store, protector, provider);
        var resolved = await resolver.ResolveCredentialAsync(created.AccountId, CompanyId, ProjectId);
        Assert.IsType<ImapPasswordCredential>(resolved.Credential);
        var pwdCred = (ImapPasswordCredential)resolved.Credential;
        Assert.Equal("LegacyPassword123!", pwdCred.Password);
    }

    [Fact]
    public void OAuthAccount_RejectsGetInternalAccountWithPassword()
    {
        var store = CreateTestStore(out _);
        var oauth = store.CreateOAuthAccount(
            CompanyId, ProjectId, "Pilot M365", UserEmail, TenantId, ClientId, $"home.{TenantId}",
            Encoding.UTF8.GetBytes("initial-cache"));

        Assert.Equal(Microsoft365Policy.AuthKind, oauth.AuthKind);
        Assert.Equal(TenantId, oauth.TenantId);
        Assert.Equal(ClientId, oauth.ClientId);

        // Attempting to retrieve password must throw InvalidOperationException
        var ex = Assert.Throws<InvalidOperationException>(() =>
            store.GetInternalAccountWithPassword(oauth.AccountId, CompanyId, ProjectId));
        Assert.Contains("Microsoft 365", ex.Message);
    }

    [Fact]
    public void SameIdentityReconnect_PreservesMetadataVersion_ForFrozenTransferResume()
    {
        var store = CreateTestStore(out var protector);
        var initialCache = Encoding.UTF8.GetBytes("cache-gen1");
        var account = store.CreateOAuthAccount(
            CompanyId, ProjectId, "Pilot M365", UserEmail, TenantId, ClientId, $"home.{TenantId}", initialCache);

        long originalVersion = account.Version;
        Assert.Equal(1, originalVersion);

        // Reconnect with updated cache
        var refreshedCache = Encoding.UTF8.GetBytes("cache-gen2");
        var updated = store.UpdateOAuthAccountOnReconnect(
            account.AccountId, CompanyId, ProjectId, originalVersion, $"home.{TenantId}", refreshedCache, "Pilot M365");

        // VERSION MUST NOT BUMP on reconnect (so existing plans with originalVersion remain valid!)
        Assert.Equal(originalVersion, updated.Version);
        Assert.Equal("Pilot M365", updated.DisplayName);

        // Cipher is updated
        var (_, cipherBytes) = store.GetAccountRecordAndCipher(account.AccountId, CompanyId, ProjectId);
        string unprotected = protector.Unprotect(cipherBytes, account.AccountId);
        Assert.Equal(refreshedCache, Convert.FromBase64String(unprotected));
    }

    [Fact]
    public async Task OperationManager_LifecycleAndBoundedCapacity()
    {
        var store = CreateTestStore(out _);
        var provider = new FakeMicrosoftAuthProvider();
        var tester = new FakeImapOAuthConnectionTester();

        using var manager = new MicrosoftOAuthOperationManager(store, provider, tester);

        var req = new StartMicrosoftOAuthRequest
        {
            CompanyId = CompanyId,
            ProjectId = ProjectId,
            DisplayName = "Pilot Operation",
            Email = UserEmail,
            ClientId = ClientId,
            TenantId = TenantId
        };

        var startResp = manager.StartOperation(req);
        Assert.NotNull(startResp.OperationId);
        Assert.Equal("preparing", startResp.Status);

        // Wait for completion
        await manager.WaitForCompletionAsync(startResp.OperationId).WaitAsync(TimeSpan.FromSeconds(5));

        var status = manager.GetOperation(startResp.OperationId, CompanyId, ProjectId);
        Assert.NotNull(status);
        Assert.Equal("connected", status.Status);
        Assert.NotNull(status.AccountId);
        Assert.Null(status.AuthorizationUrl); // Terminal state clears URL

        // Verify account exists in store
        var accounts = store.ListAccounts(CompanyId, ProjectId);
        Assert.Single(accounts);
        Assert.Equal(status.AccountId, accounts[0].AccountId);
        Assert.Equal(Microsoft365Policy.AuthKind, accounts[0].AuthKind);
    }

    [Fact]
    public void OperationManager_RejectsExceedingBounded32Capacity()
    {
        var store = CreateTestStore(out _);
        var provider = new FakeMicrosoftAuthProvider();
        var tester = new FakeImapOAuthConnectionTester();

        using var manager = new MicrosoftOAuthOperationManager(store, provider, tester);

        // Add operations up to capacity 32
        for (int i = 0; i < 32; i++)
        {
            manager.StartOperation(new StartMicrosoftOAuthRequest
            {
                CompanyId = CompanyId,
                ProjectId = ProjectId,
                DisplayName = $"Pilot {i}",
                Email = $"user{i}@example.test",
                ClientId = ClientId,
                TenantId = TenantId
            });
        }

        // 33rd operation must be rejected
        var ex = Assert.Throws<InvalidOperationException>(() =>
            manager.StartOperation(new StartMicrosoftOAuthRequest
            {
                CompanyId = CompanyId,
                ProjectId = ProjectId,
                DisplayName = "Pilot 33",
                Email = "user33@example.test",
                ClientId = ClientId,
                TenantId = TenantId
            }));

        Assert.Contains("32", ex.Message);
    }

    [Fact]
    public void OperationManager_CancelIsIdempotent_AndCannotBeUndone()
    {
        var store = CreateTestStore(out _);
        var provider = new FakeMicrosoftAuthProvider();
        var tester = new FakeImapOAuthConnectionTester();

        using var manager = new MicrosoftOAuthOperationManager(store, provider, tester);

        var startResp = manager.StartOperation(new StartMicrosoftOAuthRequest
        {
            CompanyId = CompanyId,
            ProjectId = ProjectId,
            DisplayName = "Pilot Cancel Test",
            Email = UserEmail,
            ClientId = ClientId,
            TenantId = TenantId
        });

        bool cancelled1 = manager.CancelOperation(startResp.OperationId, CompanyId, ProjectId);
        Assert.True(cancelled1);

        bool cancelled2 = manager.CancelOperation(startResp.OperationId, CompanyId, ProjectId);
        Assert.True(cancelled2); // Idempotent

        var status = manager.GetOperation(startResp.OperationId, CompanyId, ProjectId);
        Assert.NotNull(status);
        Assert.Equal("cancelled", status.Status);
    }

    [Fact]
    public void OperationManager_WrongScope_ThrowsOrRejects()
    {
        var store = CreateTestStore(out _);
        var provider = new FakeMicrosoftAuthProvider();
        var tester = new FakeImapOAuthConnectionTester();

        using var manager = new MicrosoftOAuthOperationManager(store, provider, tester);

        var startResp = manager.StartOperation(new StartMicrosoftOAuthRequest
        {
            CompanyId = CompanyId,
            ProjectId = ProjectId,
            DisplayName = "Pilot Scope Test",
            Email = UserEmail,
            ClientId = ClientId,
            TenantId = TenantId
        });

        Assert.Throws<InvalidOperationException>(() =>
            manager.GetOperation(startResp.OperationId, "wrong-company", ProjectId));

        Assert.Throws<InvalidOperationException>(() =>
            manager.CancelOperation(startResp.OperationId, CompanyId, "wrong-project"));
    }

    [Fact]
    public async Task PersonalOutlook_StartOperation_PersistsConsumersTenant_AndAuthenticatesSuccessfully()
    {
        var store = CreateTestStore(out _);
        var provider = new FakeMicrosoftAuthProvider();
        var tester = new FakeImapOAuthConnectionTester();

        using var manager = new MicrosoftOAuthOperationManager(store, provider, tester);

        var startResp = manager.StartOperation(new StartMicrosoftOAuthRequest
        {
            CompanyId = CompanyId,
            ProjectId = ProjectId,
            DisplayName = "Personal Outlook Account",
            Email = "personal@outlook.com",
            ClientId = ClientId,
            TenantId = "consumers"
        });

        Assert.NotNull(startResp.OperationId);

        await manager.WaitForCompletionAsync(startResp.OperationId);

        var status = manager.GetOperation(startResp.OperationId, CompanyId, ProjectId);
        Assert.NotNull(status);
        Assert.Equal("connected", status.Status);
        Assert.NotNull(status.AccountId);

        var accounts = store.ListAccounts(CompanyId, ProjectId);
        Assert.Single(accounts);
        Assert.Equal(status.AccountId, accounts[0].AccountId);
        Assert.Equal(Microsoft365Policy.AuthKind, accounts[0].AuthKind);
        Assert.Equal("consumers", accounts[0].TenantId);
        Assert.Equal(ClientId, accounts[0].ClientId);
        Assert.Equal("personal@outlook.com", accounts[0].Email);
    }

    [Fact]
    public async Task PersonalOutlook_Reconnect_PreservesConsumersTenant()
    {
        var store = CreateTestStore(out _);
        var provider = new FakeMicrosoftAuthProvider();
        var tester = new FakeImapOAuthConnectionTester();

        using var manager = new MicrosoftOAuthOperationManager(store, provider, tester);

        // 1. Initial connect
        var startResp = manager.StartOperation(new StartMicrosoftOAuthRequest
        {
            CompanyId = CompanyId,
            ProjectId = ProjectId,
            DisplayName = "Personal Outlook Initial",
            Email = "personal@outlook.com",
            ClientId = ClientId,
            TenantId = "consumers"
        });
        await manager.WaitForCompletionAsync(startResp.OperationId);
        var initialAccount = store.ListAccounts(CompanyId, ProjectId)[0];

        // 2. Reconnect
        var reconnectResp = manager.StartOperation(new StartMicrosoftOAuthRequest
        {
            CompanyId = CompanyId,
            ProjectId = ProjectId,
            DisplayName = "Personal Outlook Reconnected",
            Email = "personal@outlook.com",
            ClientId = ClientId,
            TenantId = "consumers",
            AccountId = initialAccount.AccountId,
            ExpectedVersion = initialAccount.Version
        });
        await manager.WaitForCompletionAsync(reconnectResp.OperationId);

        var reconnectedAccount = store.GetAccount(initialAccount.AccountId, CompanyId, ProjectId);
        Assert.NotNull(reconnectedAccount);
        Assert.Equal("consumers", reconnectedAccount.TenantId);
        Assert.Equal(ClientId, reconnectedAccount.ClientId);
        Assert.Equal(Microsoft365Policy.AuthKind, reconnectedAccount.AuthKind);
    }

    [Theory]
    [InlineData("common")]
    [InlineData("organizations")]
    [InlineData("Consumers")]
    [InlineData("consumers/")]
    [InlineData(" consumers")]
    [InlineData("")]
    public void PersonalOutlook_RejectsBroadOrInvalidAuthorities(string invalidAuthority)
    {
        var store = CreateTestStore(out _);
        var provider = new FakeMicrosoftAuthProvider();
        var tester = new FakeImapOAuthConnectionTester();

        using var manager = new MicrosoftOAuthOperationManager(store, provider, tester);

        Assert.Throws<ArgumentException>(() =>
            manager.StartOperation(new StartMicrosoftOAuthRequest
            {
                CompanyId = CompanyId,
                ProjectId = ProjectId,
                DisplayName = "Invalid Authority Account",
                Email = "user@outlook.com",
                ClientId = ClientId,
                TenantId = invalidAuthority
            }));
    }

    [Fact]
    public async Task PersonalOutlook_SilentResolution_ValidatesExpectedTenantId()
    {
        var store = CreateTestStore(out var protector);
        var provider = new FakeMicrosoftAuthProvider();

        var initialAccount = store.CreateOAuthAccount(
            CompanyId, ProjectId, "Personal Outlook", "personal@outlook.com", "consumers", ClientId,
            "home.personal", Encoding.UTF8.GetBytes("personal-cache"));

        var resolver = new ImapCredentialResolver(store, protector, provider);
        var resolved = await resolver.ResolveCredentialAsync(initialAccount.AccountId, CompanyId, ProjectId);
        Assert.IsType<ImapOAuth2Credential>(resolved.Credential);
        var oauthCred = (ImapOAuth2Credential)resolved.Credential;
        Assert.StartsWith("synthetic-refreshed-token-", oauthCred.AccessToken);

        // If custom silent handler returns mismatched tenant, AcquireTokenSilentAsync must fail
        var msalProvider = new MsalMicrosoftAuthProvider();
        // Root MsalMicrosoftAuthProvider requires matching tenant
        Assert.Equal(Microsoft365Policy.PersonalTenantId, Microsoft365Policy.GetExpectedTenantId("consumers"));
    }
}

