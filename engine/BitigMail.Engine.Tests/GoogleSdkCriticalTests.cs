using BitigMail.LocalHost.Security;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Util.Store;
using BitigMail.LocalHost.Imap;
using System.Text.Json.Nodes;

namespace BitigMail.Engine.Tests;

/// <summary>Exercises the real production SDK URL/receiver path; never signs in or exchanges a code.</summary>
public sealed class GoogleSdkCriticalTests
{
    [Fact]
    public async Task ProductionProviderCreatesStateAndPkceAndCancelsItsActualListener()
    {
        const string clientId = "123456789-synthetic.apps.googleusercontent.com";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Uri? captured = null;
        var provider = new GoogleAuthProvider();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.AcquireTokenInteractiveAsync(
            clientId, "synthetic-desktop-config", "test@example.invalid", uri =>
            {
                captured = uri;
                GoogleOAuthPolicy.ValidateAuthorizationUri(uri, clientId);
                cancellation.Cancel();
            }, cancellation.Token));
        Assert.NotNull(captured);
        var query = QueryHelpers.ParseQuery(captured.Query);
        Assert.True(query["state"].ToString().Length >= 32);
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.False(query.ContainsKey("client_secret"));
        Assert.False(query.ContainsKey("code_verifier"));
        Assert.Equal("127.0.0.1", new Uri(query["redirect_uri"].ToString()).Host);
        using var socket = new System.Net.Sockets.TcpClient();
        await Assert.ThrowsAsync<System.Net.Sockets.SocketException>(() => socket.ConnectAsync(
            "127.0.0.1", new Uri(query["redirect_uri"].ToString()).Port));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActualSdkReceiverValidatesLoopbackCallbackBeforeReturningCode(bool validState)
    {
        var captured = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new CallbackReceiver(uri => captured.TrySetResult(uri));
        var receiver = new GoogleAuthProvider.StateValidatingReceiver(inner);
        using var flow = new PkceGoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new() { ClientId = "123456789-synthetic.apps.googleusercontent.com", ClientSecret = "synthetic" },
            Scopes = [GoogleOAuthPolicy.MailScope, GoogleOAuthPolicy.OpenIdScope, GoogleOAuthPolicy.EmailScope],
            DataStore = new NullDataStore()
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = flow.CreateAuthorizationCodeRequest(receiver.RedirectUri, out _);
        var pending = receiver.ReceiveCodeAsync(request, timeout.Token);
        Uri authorization = await captured.Task.WaitAsync(timeout.Token);
        var state = QueryHelpers.ParseQuery(authorization.Query)["state"].ToString();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
        var callback = QueryHelpers.AddQueryString(receiver.RedirectUri, new Dictionary<string, string?>
        {
            ["code"] = "synthetic-never-exchanged",
            ["state"] = validState ? state : "wrong-state"
        });
        using var response = await client.GetAsync(callback, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (validState)
            Assert.Equal("synthetic-never-exchanged", (await pending).Code);
        else
            Assert.Contains("state", (await Assert.ThrowsAsync<InvalidOperationException>(() => pending)).Message);
    }

    private sealed class CallbackReceiver(Action<Uri> capture) : LocalServerCodeReceiver(
        "Local synthetic test", CallbackUriChooserStrategy.ForceLoopbackIp)
    {
        protected override bool OpenBrowser(string url) { capture(new Uri(url)); return true; }
    }

    [Theory]
    [InlineData("microsoft365", "imap.gmail.com")]
    [InlineData("google", "outlook.office365.com")]
    public async Task TamperedCrossProviderAccountNeverAcquiresBearerToken(string authKind, string wrongHost)
    {
        string directory = Path.Combine(Path.GetTempPath(), "bitigmail-root-provider-boundary", Guid.NewGuid().ToString("N"));
        var protector = new WindowsImapCredentialProtector();
        var policy = new ImapConnectionPolicy(false);
        var store = new ImapAccountStore(directory, protector, policy);
        var account = store.CreateOAuthAccount("company", "project", "Synthetic", "test@example.invalid",
            authKind == "google" ? "" : "12345678-1234-1234-1234-123456789abc",
            authKind == "google" ? "123456789-synthetic.apps.googleusercontent.com" : "12345678-1234-1234-1234-123456789abd",
            "synthetic-subject", "synthetic-cache"u8.ToArray(), authKind);
        string file = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories).Single();
        var envelope = JsonNode.Parse(File.ReadAllText(file))!;
        Assert.NotNull(envelope["account"]!["Host"]);
        envelope["account"]!["Host"] = wrongHost;
        File.WriteAllText(file, envelope.ToJsonString());
        var reloaded = new ImapAccountStore(directory, protector, policy);
        var microsoft = new NeverAcquireMicrosoft();
        var google = new NeverAcquireGoogle();
        var resolver = new ImapCredentialResolver(reloaded, protector, microsoft, google);
        await Assert.ThrowsAnyAsync<Exception>(() => resolver.ResolveCredentialAsync(account.AccountId, "company", "project"));
        Assert.Equal(0, microsoft.Calls + google.Calls);
    }

    private sealed class NeverAcquireMicrosoft : IMicrosoftAuthProvider
    {
        public int Calls;
        public Task<MicrosoftAuthResult> AcquireTokenInteractiveAsync(string tenantId, string clientId, string requestedEmail, Action<Uri> capture, CancellationToken ct) => throw new NotSupportedException();
        public Task<(string AccessToken, byte[]? UpdatedCacheBytes)> AcquireTokenSilentAsync(string tenantId, string clientId, string homeAccountId, byte[] cacheBytes, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("Token provider must not be reached."); }
    }

    private sealed class NeverAcquireGoogle : IGoogleAuthProvider
    {
        public int Calls;
        public Task<GoogleAuthResult> AcquireTokenInteractiveAsync(string clientId, string secret, string requestedEmail, Action<Uri> capture, CancellationToken ct) => throw new NotSupportedException();
        public Task<(string AccessToken, byte[]? UpdatedCacheBytes)> AcquireTokenSilentAsync(string clientId, string subject, byte[] cacheBytes, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("Token provider must not be reached."); }
    }
}
