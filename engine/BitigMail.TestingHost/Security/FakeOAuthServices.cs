using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.LocalHost.Security;

namespace BitigMail.TestingHost.Security;

/// <summary>
/// Deterministic fake OAuth connection tester for TestingHost and testing assemblies only.
/// Never communicates with real Microsoft endpoints.
/// </summary>
public sealed class FakeImapOAuthConnectionTester : IImapOAuthConnectionTester
{
    public Func<string, int, string, string, string, CancellationToken, Task>? CustomVerifier { get; set; }

    public async Task VerifyConnectionAsync(string host, int port, string tlsMode, string username, string accessToken, CancellationToken ct)
    {
        if (CustomVerifier != null)
        {
            await CustomVerifier(host, port, tlsMode, username, accessToken, ct);
            return;
        }

        // Default verification: ensure valid endpoint parameters per Microsoft365Policy
        if ((!string.Equals(host, Microsoft365Policy.ImapHost, StringComparison.OrdinalIgnoreCase) ||
            port != Microsoft365Policy.ImapPort ||
            !string.Equals(tlsMode, "ssl", StringComparison.OrdinalIgnoreCase)) &&
            !GoogleOAuthPolicy.IsAllowedEndpoint(host, port, tlsMode))
        {
            throw new InvalidOperationException("Geçersiz Microsoft 365 IMAP uç noktası.");
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Kullanıcı adı ve OAuth erişim belirteci zorunludur.");
        }

        await Task.CompletedTask;
    }
}

public sealed class FakeGoogleAuthProvider : IGoogleAuthProvider
{
    public async Task<GoogleAuthResult> AcquireTokenInteractiveAsync(string clientId, string clientSecret,
        string requestedEmail, Action<Uri> onAuthorizationUrlCaptured, CancellationToken ct)
    {
        GoogleOAuthPolicy.ValidateClient(clientId, clientSecret);
        var state = Guid.NewGuid().ToString("N");
        const string challenge = "E9Melhoa2OwvFrGMTJguCH5ZwUR6SRS_Hn7xNT85WP8";
        var uri = new Uri($"https://accounts.google.com/o/oauth2/v2/auth?client_id={Uri.EscapeDataString(clientId)}&response_type=code&code_challenge_method=S256&code_challenge={challenge}&state={state}&redirect_uri=http%3A%2F%2F127.0.0.1%3A5144%2Fauthorize%2F&scope=https%3A%2F%2Fmail.google.com%2F%20openid%20email&access_type=offline");
        GoogleOAuthPolicy.ValidateAuthorizationUri(uri, clientId);
        onAuthorizationUrlCaptured(uri);
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        return new() { AccessToken = "fake-google-token", Email = requestedEmail, EmailVerified = true,
            Subject = "google-subject-" + requestedEmail.ToLowerInvariant(), SerializedCache = Encoding.UTF8.GetBytes("fake-google-cache") };
    }

    public Task<(string AccessToken, byte[]? UpdatedCacheBytes)> AcquireTokenSilentAsync(string clientId, string subject,
        byte[] cacheBytes, CancellationToken ct) => Task.FromResult<(string, byte[]?)>(("fake-google-refresh", Encoding.UTF8.GetBytes("fake-google-cache-refreshed")));
}

/// <summary>
/// Deterministic fake Microsoft authentication provider for TestingHost and test environments only.
/// Never opens a browser, calls real cloud services, or transmits secrets over the wire.
/// </summary>
public sealed class FakeMicrosoftAuthProvider : IMicrosoftAuthProvider
{
    public Func<string, string, string, Action<Uri>, CancellationToken, Task<MicrosoftAuthResult>>? CustomInteractiveHandler { get; set; }
    public Func<string, string, string, byte[], CancellationToken, Task<(string AccessToken, byte[]? UpdatedCacheBytes)>>? CustomSilentHandler { get; set; }

    public async Task<MicrosoftAuthResult> AcquireTokenInteractiveAsync(
        string tenantId,
        string clientId,
        string requestedEmail,
        Action<Uri> onAuthorizationUrlCaptured,
        CancellationToken ct)
    {
        Microsoft365Policy.ValidateConfiguration(tenantId, clientId);

        // Generate synthetic compliant authorization URI
        string syntheticState = Guid.NewGuid().ToString("N");
        string syntheticChallenge = "E9Melhoa2OwvFrGMTJguCH5ZwUR6SRS_Hn7xNT85WP8";
        var uri = new Uri($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize?client_id={clientId}&response_type=code&response_mode=form_post&code_challenge_method=S256&code_challenge={syntheticChallenge}&state={syntheticState}&redirect_uri=http%3A%2F%2Flocalhost%3A5143%2F&scope=https%3A%2F%2Foutlook.office.com%2FIMAP.AccessAsUser.All");

        Microsoft365Policy.ValidateAuthorizationUri(uri, tenantId, clientId);
        onAuthorizationUrlCaptured(uri);

        if (CustomInteractiveHandler != null)
        {
            return await CustomInteractiveHandler(tenantId, clientId, requestedEmail, onAuthorizationUrlCaptured, ct);
        }

        byte[] syntheticCache = Encoding.UTF8.GetBytes($"synthetic-cache-{tenantId}-{clientId}");
        return new MicrosoftAuthResult
        {
            AccessToken = "synthetic-access-token-" + Guid.NewGuid().ToString("N"),
            Username = requestedEmail,
            TenantId = Microsoft365Policy.GetExpectedTenantId(tenantId),
            HomeAccountId = $"home.{tenantId}",
            SerializedCache = syntheticCache
        };
    }

    public async Task<(string AccessToken, byte[]? UpdatedCacheBytes)> AcquireTokenSilentAsync(
        string tenantId,
        string clientId,
        string homeAccountId,
        byte[] cacheBytes,
        CancellationToken ct)
    {
        Microsoft365Policy.ValidateConfiguration(tenantId, clientId);
        if (string.IsNullOrWhiteSpace(homeAccountId))
            throw new ArgumentException("HomeAccountId boş olamaz.", nameof(homeAccountId));

        if (CustomSilentHandler != null)
        {
            return await CustomSilentHandler(tenantId, clientId, homeAccountId, cacheBytes, ct);
        }

        byte[] syntheticUpdatedCache = Encoding.UTF8.GetBytes($"refreshed-cache-{homeAccountId}");
        return ("synthetic-refreshed-token-" + Guid.NewGuid().ToString("N"), syntheticUpdatedCache);
    }
}
