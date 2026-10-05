using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace BitigMail.LocalHost.Security;

/// <summary>
/// Production Microsoft authentication provider backed by official MSAL.NET (Microsoft.Identity.Client 4.89.0).
/// Strictly adheres to Microsoft365Policy: Authorization Code + PKCE, system webview URL capture without launching
/// a browser, single IMAP scope, localhost loopback redirect, and per-account serialized token cache.
/// </summary>
public sealed class MsalMicrosoftAuthProvider : IMicrosoftAuthProvider
{
    public async Task<MicrosoftAuthResult> AcquireTokenInteractiveAsync(
        string tenantId,
        string clientId,
        string requestedEmail,
        Action<Uri> onAuthorizationUrlCaptured,
        CancellationToken ct)
    {
        Microsoft365Policy.ValidateConfiguration(tenantId, clientId);
        ArgumentNullException.ThrowIfNull(onAuthorizationUrlCaptured);

        var app = PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
            .WithRedirectUri("http://localhost")
            .Build();

        byte[]? serializedCache = null;
        app.UserTokenCache.SetAfterAccess(args =>
        {
            serializedCache = args.TokenCache.SerializeMsalV3();
        });

        var options = new SystemWebViewOptions
        {
            OpenBrowserAsync = uri =>
            {
                // Strict validation by root security policy before reflecting to operation
                Microsoft365Policy.ValidateAuthorizationUri(uri, tenantId, clientId);
                onAuthorizationUrlCaptured(uri);
                // Backend never launches a browser; user clicks link in UI
                return Task.CompletedTask;
            }
        };

        var interactive = app.AcquireTokenInteractive(new[] { Microsoft365Policy.ImapScope })
            .WithUseEmbeddedWebView(false)
            .WithSystemWebViewOptions(options);

        if (!string.IsNullOrWhiteSpace(requestedEmail))
        {
            interactive = interactive.WithLoginHint(requestedEmail);
        }

        var authResult = await interactive.ExecuteAsync(ct);

        if (serializedCache == null || serializedCache.Length == 0)
        {
            throw new InvalidOperationException("Microsoft token önbelleği alınamadı.");
        }

        // Never substitute requested email/tenant when missing; pass empty to policy and fail
        string username = authResult.Account?.Username ?? string.Empty;
        string actualTenantId = authResult.TenantId ?? string.Empty;
        string homeAccountId = authResult.Account?.HomeAccountId?.Identifier ?? string.Empty;

        return new MicrosoftAuthResult
        {
            AccessToken = authResult.AccessToken,
            Username = username,
            TenantId = actualTenantId,
            HomeAccountId = homeAccountId,
            SerializedCache = serializedCache
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

        var app = PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
            .WithRedirectUri("http://localhost")
            .Build();

        byte[]? updatedCache = null;
        app.UserTokenCache.SetBeforeAccess(args =>
        {
            if (cacheBytes != null && cacheBytes.Length > 0)
            {
                args.TokenCache.DeserializeMsalV3(cacheBytes);
            }
        });

        app.UserTokenCache.SetAfterAccess(args =>
        {
            if (args.HasStateChanged)
            {
                updatedCache = args.TokenCache.SerializeMsalV3();
            }
        });

        var accounts = await app.GetAccountsAsync();
        var account = accounts.FirstOrDefault(a => string.Equals(a.HomeAccountId.Identifier, homeAccountId, StringComparison.Ordinal));
        if (account == null)
        {
            throw new MsalUiRequiredException(
                MsalError.UserNullError,
                "Belirtilen HomeAccountId ile önbellekte kullanıcı bulunamadı. Yeniden yetkilendirme gerekiyor.");
        }

        var authResult = await app.AcquireTokenSilent(new[] { Microsoft365Policy.ImapScope }, account)
            .ExecuteAsync(ct);

        string actualTenantId = authResult.TenantId ?? string.Empty;
        string actualHomeAccountId = authResult.Account?.HomeAccountId?.Identifier ?? string.Empty;

        var expectedTenantId = Microsoft365Policy.GetExpectedTenantId(tenantId);
        if (!string.Equals(actualTenantId, expectedTenantId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(actualHomeAccountId, homeAccountId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Yenilenen Microsoft oturumunun kiracı veya hesap kimliği geçerli hesapla eşleşmiyor.");
        }

        return (authResult.AccessToken, updatedCache);
    }
}
