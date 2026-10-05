namespace BitigMail.LocalHost.Security;

public sealed class GoogleAuthResult
{
    public required string AccessToken { get; init; }
    public required string Email { get; init; }
    public required bool EmailVerified { get; init; }
    public required string Subject { get; init; }
    public required byte[] SerializedCache { get; init; }
}

public interface IGoogleAuthProvider
{
    Task<GoogleAuthResult> AcquireTokenInteractiveAsync(string clientId, string clientSecret, string requestedEmail,
        Action<Uri> onAuthorizationUrlCaptured, CancellationToken ct);
    Task<(string AccessToken, byte[]? UpdatedCacheBytes)> AcquireTokenSilentAsync(string clientId, string subject,
        byte[] cacheBytes, CancellationToken ct);
}
