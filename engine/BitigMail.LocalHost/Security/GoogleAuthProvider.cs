using System.Text.Json;
using Google.Apis.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using Google.Apis.Auth.OAuth2.Requests;
using System.Security.Cryptography;

namespace BitigMail.LocalHost.Security;

public sealed class GoogleAuthProvider : IGoogleAuthProvider
{
    private sealed record CacheEnvelope(string ClientId, string ClientSecret, string Subject, string Email, TokenResponse Token);

    private sealed class CapturingReceiver(Action<Uri> capture) : LocalServerCodeReceiver(
        "BitigMail Google bağlantısı tamamlandı. Bu pencereyi kapatabilirsiniz.", CallbackUriChooserStrategy.ForceLoopbackIp)
    {
        protected override bool OpenBrowser(string url)
        {
            capture(new Uri(url, UriKind.Absolute));
            return true;
        }
    }

    internal sealed class StateValidatingReceiver(ICodeReceiver inner) : ICodeReceiver
    {
        public string RedirectUri => inner.RedirectUri;
        public async Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(AuthorizationCodeRequestUrl url, CancellationToken taskCancellationToken)
        {
            string expected = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            url.State = expected;
            var response = await inner.ReceiveCodeAsync(url, taskCancellationToken);
            if (string.IsNullOrEmpty(response.State) ||
                !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(expected), System.Text.Encoding.ASCII.GetBytes(response.State)))
                throw new InvalidOperationException("Google OAuth state doğrulaması başarısız.");
            return response;
        }
    }

    public async Task<GoogleAuthResult> AcquireTokenInteractiveAsync(string clientId, string clientSecret,
        string requestedEmail, Action<Uri> onAuthorizationUrlCaptured, CancellationToken ct)
    {
        GoogleOAuthPolicy.ValidateClient(clientId, clientSecret);
        using var flow = CreateFlow(clientId, clientSecret);
        var receiver = new CapturingReceiver(uri =>
        {
            GoogleOAuthPolicy.ValidateAuthorizationUri(uri, clientId);
            onAuthorizationUrlCaptured(uri);
        });
        var installed = new AuthorizationCodeInstalledApp(flow, new StateValidatingReceiver(receiver));
        var credential = await installed.AuthorizeAsync(requestedEmail, ct);
        var token = credential.Token ?? throw new InvalidOperationException("Google OAuth belirteci alınamadı.");
        if (string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(token.RefreshToken) || string.IsNullOrWhiteSpace(token.IdToken))
            throw new InvalidOperationException("Google OAuth yanıtı gerekli alanları içermiyor.");
        var identity = await GoogleJsonWebSignature.ValidateAsync(token.IdToken, new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = [clientId]
        });
        GoogleOAuthPolicy.ValidateIdentity(requestedEmail, clientId, identity.Email, identity.EmailVerified, identity.Subject);
        return new GoogleAuthResult
        {
            AccessToken = token.AccessToken,
            Email = identity.Email,
            EmailVerified = identity.EmailVerified,
            Subject = identity.Subject,
            SerializedCache = JsonSerializer.SerializeToUtf8Bytes(new CacheEnvelope(clientId, clientSecret, identity.Subject, identity.Email, token))
        };
    }

    public async Task<(string AccessToken, byte[]? UpdatedCacheBytes)> AcquireTokenSilentAsync(string clientId,
        string subject, byte[] cacheBytes, CancellationToken ct)
    {
        var envelope = JsonSerializer.Deserialize<CacheEnvelope>(cacheBytes) ??
            throw new InvalidOperationException("Google OAuth önbelleği okunamadı.");
        if (!string.Equals(envelope.ClientId, clientId, StringComparison.Ordinal) ||
            !string.Equals(envelope.Subject, subject, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(envelope.Email))
            throw new InvalidOperationException("Google OAuth önbelleği hesap kimliğiyle eşleşmiyor.");
        GoogleOAuthPolicy.ValidateClient(clientId, envelope.ClientSecret);
        if (string.IsNullOrWhiteSpace(envelope.Token.RefreshToken))
            throw new GoogleReauthorizationRequiredException("Google yeniden yetkilendirmesi gerekiyor.");
        using var flow = CreateFlow(clientId, envelope.ClientSecret);
        TokenResponse refreshed;
        try { refreshed = await flow.RefreshTokenAsync(subject, envelope.Token.RefreshToken, ct); }
        catch (TokenResponseException ex) when (ex.Error?.Error is "invalid_grant" or "unauthorized_client")
        { throw new GoogleReauthorizationRequiredException("Google yeniden yetkilendirmesi gerekiyor.", ex); }
        if (string.IsNullOrWhiteSpace(refreshed.AccessToken))
            throw new GoogleReauthorizationRequiredException("Google yeniden yetkilendirmesi gerekiyor.");
        refreshed.RefreshToken ??= envelope.Token.RefreshToken;
        if (!string.IsNullOrWhiteSpace(refreshed.IdToken))
        {
            var identity = await GoogleJsonWebSignature.ValidateAsync(refreshed.IdToken, new GoogleJsonWebSignature.ValidationSettings { Audience = [clientId] });
            GoogleOAuthPolicy.ValidateIdentity(envelope.Email, clientId, identity.Email, identity.EmailVerified, identity.Subject, subject);
        }
        return (refreshed.AccessToken, JsonSerializer.SerializeToUtf8Bytes(new CacheEnvelope(clientId, envelope.ClientSecret, subject, envelope.Email, refreshed)));
    }

    private static PkceGoogleAuthorizationCodeFlow CreateFlow(string clientId, string clientSecret) => new(
        new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
            Scopes = [GoogleOAuthPolicy.MailScope, GoogleOAuthPolicy.OpenIdScope, GoogleOAuthPolicy.EmailScope],
            DataStore = new NullDataStore()
        });
}

public sealed class GoogleReauthorizationRequiredException(string message, Exception? inner = null) : Exception(message, inner);
