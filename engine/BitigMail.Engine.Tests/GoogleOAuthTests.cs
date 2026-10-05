using System.Text;
using System.Text.Json;
using BitigMail.Engine.Imap.OAuth;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.OAuth;
using BitigMail.LocalHost.Security;
using BitigMail.TestingHost.Security;
using Xunit;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Util.Store;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;

namespace BitigMail.Engine.Tests;

public sealed class GoogleOAuthTests
{
    private const string Client = "123456789-test.apps.googleusercontent.com";
    private const string Secret = "synthetic-client-secret";
    private static ImapAccountStore Store(out WindowsImapCredentialProtector protector)
    {
        protector = new();
        return new(Path.Combine(Path.GetTempPath(), "bitigmail-google-tests", Guid.NewGuid().ToString("N")), protector, new(true));
    }

    private sealed class BlockingProvider : IGoogleAuthProvider
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<GoogleAuthResult> AcquireTokenInteractiveAsync(string clientId, string clientSecret, string requestedEmail, Action<Uri> capture, CancellationToken ct)
        {
            Entered.TrySetResult();
            await Release.Task;
            return new() { AccessToken = "late", Email = requestedEmail, EmailVerified = true, Subject = "sub", SerializedCache = Encoding.UTF8.GetBytes("cache") };
        }
        public Task<(string AccessToken, byte[]? UpdatedCacheBytes)> AcquireTokenSilentAsync(string clientId, string subject, byte[] cacheBytes, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class EchoReceiver(bool mismatch = false) : ICodeReceiver
    {
        public string RedirectUri => "http://127.0.0.1:5144/authorize/";
        public string? ObservedState { get; private set; }
        public Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(AuthorizationCodeRequestUrl url, CancellationToken taskCancellationToken)
        {
            taskCancellationToken.ThrowIfCancellationRequested(); ObservedState = url.State;
            return Task.FromResult(new AuthorizationCodeResponseUrl { Code = "synthetic", State = mismatch ? "wrong" : url.State });
        }
    }

    [Fact]
    public void PolicyAcceptsOnlyExactGooglePkceLoopbackUrl()
    {
        var url = new Uri($"https://accounts.google.com/o/oauth2/v2/auth?client_id={Client}&response_type=code&code_challenge_method=S256&code_challenge=E9Melhoa2OwvFrGMTJguCH5ZwUR6SRS_Hn7xNT85WP8&state=12345678901234567890123456789012&redirect_uri=http%3A%2F%2F127.0.0.1%3A5144%2Fauthorize%2F&scope=https%3A%2F%2Fmail.google.com%2F%20openid%20email&access_type=offline");
        GoogleOAuthPolicy.ValidateAuthorizationUri(url, Client);
        Assert.Throws<InvalidOperationException>(() => GoogleOAuthPolicy.ValidateAuthorizationUri(new Uri(url.ToString().Replace("accounts.google.com", "evil.example")), Client));
        Assert.Throws<InvalidOperationException>(() => GoogleOAuthPolicy.ValidateAuthorizationUri(new Uri(url.ToString().Replace("S256", "plain")), Client));
    }

    [Fact]
    public async Task FakeLifecyclePublishesProtectedGoogleAccountWithoutSecretDto()
    {
        var store = Store(out _);
        using var manager = new GoogleOAuthOperationManager(store, new FakeGoogleAuthProvider(), new FakeImapOAuthConnectionTester());
        var start = manager.StartOperation(new StartGoogleOAuthRequest { CompanyId = "company", ProjectId = "project", DisplayName = "Gmail", Email = "me@gmail.com", ClientId = Client, ClientSecret = Secret });
        await manager.WaitForCompletionAsync(start.OperationId).WaitAsync(TimeSpan.FromSeconds(5));
        var status = manager.GetOperation(start.OperationId, "company", "project")!;
        Assert.Equal("connected", status.Status); Assert.Null(status.AuthorizationUrl);
        var account = Assert.Single(store.ListAccounts("company", "project"));
        Assert.Equal("google", account.AuthKind); Assert.Equal("imap.gmail.com", account.Host);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(account));
        Assert.Throws<InvalidOperationException>(() => store.GetInternalAccountWithPassword(account.AccountId, "company", "project"));
    }

    [Fact]
    public async Task ResolverUsesTypedGoogleOAuthAndRejectsWrongEndpoint()
    {
        var store = Store(out var protector);
        var account = store.CreateOAuthAccount("company", "project", "Gmail", "me@gmail.com", "", Client, "subject", Encoding.UTF8.GetBytes("cache"), "google");
        var resolver = new ImapCredentialResolver(store, protector, new FakeMicrosoftAuthProvider(), new FakeGoogleAuthProvider());
        var resolved = await resolver.ResolveCredentialAsync(account.AccountId, "company", "project");
        Assert.IsType<ImapOAuth2Credential>(resolved.Credential);
        Assert.False(GoogleOAuthPolicy.IsAllowedEndpoint("outlook.office365.com", 993, "ssl"));
    }

    [Fact]
    public void IdentityRequiresVerifiedMatchingMailboxAndStableSubject()
    {
        GoogleOAuthPolicy.ValidateIdentity("me@gmail.com", Client, "ME@gmail.com", true, "sub", "sub");
        Assert.Throws<InvalidOperationException>(() => GoogleOAuthPolicy.ValidateIdentity("me@gmail.com", Client, "other@gmail.com", true, "sub"));
        Assert.Throws<InvalidOperationException>(() => GoogleOAuthPolicy.ValidateIdentity("me@gmail.com", Client, "me@gmail.com", false, "sub"));
        Assert.Throws<InvalidOperationException>(() => GoogleOAuthPolicy.ValidateIdentity("me@gmail.com", Client, "me@gmail.com", true, "changed", "sub"));
    }

    [Fact]
    public void OfficialSdkBuildsS256PkceAndExposesCancellableLoopbackReceiver()
    {
        using var flow = new PkceGoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new() { ClientId = Client, ClientSecret = Secret },
            Scopes = [GoogleOAuthPolicy.MailScope, GoogleOAuthPolicy.OpenIdScope, GoogleOAuthPolicy.EmailScope],
            DataStore = new NullDataStore()
        });
        var request = flow.CreateAuthorizationCodeRequest("http://127.0.0.1:5144/authorize/", out var verifier);
        request.State = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var uri = request.Build();
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(43, query["code_challenge"].ToString().Length);
        Assert.True(verifier.Length >= 43); Assert.Equal(32, request.State.Length);
        GoogleOAuthPolicy.ValidateAuthorizationUri(uri, Client);
        var receiver = new LocalServerCodeReceiver("done", LocalServerCodeReceiver.CallbackUriChooserStrategy.ForceLoopbackIp);
        Assert.Equal("127.0.0.1", new Uri(receiver.RedirectUri).Host);
        Assert.Contains(typeof(CancellationToken), typeof(LocalServerCodeReceiver).GetMethod(nameof(LocalServerCodeReceiver.ReceiveCodeAsync))!.GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public async Task CancelledDelayedProviderCannotPublishAccount()
    {
        var store = Store(out _); var provider = new BlockingProvider();
        using var manager = new GoogleOAuthOperationManager(store, provider, new FakeImapOAuthConnectionTester());
        var start = manager.StartOperation(new() { CompanyId = "company", ProjectId = "project", DisplayName = "Gmail", Email = "me@gmail.com", ClientId = Client, ClientSecret = Secret });
        await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("cancelled", manager.Cancel(start.OperationId, "company", "project")!.Status);
        provider.Release.TrySetResult();
        await manager.WaitForCompletionAsync(start.OperationId).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Empty(store.ListAccounts("company", "project"));
    }

    [Fact]
    public async Task PersistenceFailureNeverPublishesConnectedOrSecret()
    {
        var store = Store(out _); store.OnBeforeAtomicWrite = _ => throw new IOException("sentinel-secret");
        using var manager = new GoogleOAuthOperationManager(store, new FakeGoogleAuthProvider(), new FakeImapOAuthConnectionTester());
        var start = manager.StartOperation(new() { CompanyId = "company", ProjectId = "project", DisplayName = "Gmail", Email = "me@gmail.com", ClientId = Client, ClientSecret = Secret });
        await manager.WaitForCompletionAsync(start.OperationId).WaitAsync(TimeSpan.FromSeconds(2));
        var status = manager.GetOperation(start.OperationId, "company", "project")!;
        Assert.Equal("failed", status.Status); Assert.DoesNotContain("sentinel-secret", JsonSerializer.Serialize(status));
        Assert.Empty(store.ListAccounts("company", "project"));
    }

    [Fact]
    public async Task StateWrapperCreatesFreshStateRejectsMismatchAndHonorsCancellation()
    {
        using var flow = new PkceGoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer { ClientSecrets = new() { ClientId = Client, ClientSecret = Secret }, Scopes = [GoogleOAuthPolicy.MailScope], DataStore = new NullDataStore() });
        var request = flow.CreateAuthorizationCodeRequest("http://127.0.0.1:5144/authorize/", out _);
        var echo = new EchoReceiver();
        await new GoogleAuthProvider.StateValidatingReceiver(echo).ReceiveCodeAsync(request, CancellationToken.None);
        Assert.NotNull(echo.ObservedState); Assert.Equal(64, echo.ObservedState!.Length);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GoogleAuthProvider.StateValidatingReceiver(new EchoReceiver(true)).ReceiveCodeAsync(request, CancellationToken.None));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GoogleAuthProvider.StateValidatingReceiver(new EchoReceiver()).ReceiveCodeAsync(request, cts.Token));
    }
}
