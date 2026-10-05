using System;
using System.Threading;
using System.Threading.Tasks;

namespace BitigMail.LocalHost.Security;

/// <summary>
/// Result of an interactive Microsoft authentication operation.
/// Exposes tokens and cache strictly within the trusted internal process.
/// </summary>
public sealed class MicrosoftAuthResult
{
    public required string AccessToken { get; init; }
    public required string Username { get; init; }
    public required string TenantId { get; init; }
    public required string HomeAccountId { get; init; }
    public required byte[] SerializedCache { get; init; }
}

/// <summary>
/// Seam for Microsoft OAuth authentication.
/// Allows injecting real MSAL provider in production or deterministic fake provider in TestingHost.
/// </summary>
public interface IMicrosoftAuthProvider
{
    Task<MicrosoftAuthResult> AcquireTokenInteractiveAsync(
        string tenantId,
        string clientId,
        string requestedEmail,
        Action<Uri> onAuthorizationUrlCaptured,
        CancellationToken ct);

    Task<(string AccessToken, byte[]? UpdatedCacheBytes)> AcquireTokenSilentAsync(
        string tenantId,
        string clientId,
        string homeAccountId,
        byte[] cacheBytes,
        CancellationToken ct);
}
