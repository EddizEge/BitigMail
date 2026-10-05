using Microsoft.AspNetCore.WebUtilities;

namespace BitigMail.LocalHost.Security;

public static class GoogleOAuthPolicy
{
    public const string AuthKind = "google";
    public const string ImapHost = "imap.gmail.com";
    public const int ImapPort = 993;
    public const string MailScope = "https://mail.google.com/";
    public const string OpenIdScope = "openid";
    public const string EmailScope = "email";

    public static void ValidateClient(string clientId, string clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientId) || clientId.Length > 512 || clientId.Any(char.IsControl) ||
            !clientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal))
            throw new ArgumentException("Google masaüstü OAuth Client ID geçersiz.");
        if (string.IsNullOrWhiteSpace(clientSecret) || clientSecret.Length > 1024 || clientSecret.Any(char.IsControl))
            throw new ArgumentException("Google masaüstü OAuth istemci yapılandırması eksik.");
    }

    public static bool IsAllowedEndpoint(string host, int port, string tlsMode) =>
        string.Equals(host, ImapHost, StringComparison.OrdinalIgnoreCase) && port == ImapPort &&
        string.Equals(tlsMode, "ssl", StringComparison.OrdinalIgnoreCase);

    public static void ValidateAuthorizationUri(Uri uri, string clientId)
    {
        if (uri is null || !uri.IsAbsoluteUri || uri.Scheme != "https" || uri.Port != 443 ||
            !string.Equals(uri.Host, "accounts.google.com", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath != "/o/oauth2/v2/auth" || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw InvalidUrl();
        var query = QueryHelpers.ParseQuery(uri.Query);
        string One(string key)
        {
            if (!query.TryGetValue(key, out var values) || values.Count != 1 || string.IsNullOrWhiteSpace(values[0])) throw InvalidUrl();
            return values[0]!;
        }
        if (One("client_id") != clientId || One("response_type") != "code" || One("code_challenge_method") != "S256" ||
            One("access_type") != "offline") throw InvalidUrl();
        var challenge = One("code_challenge");
        if (challenge.Length != 43 || challenge.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) throw InvalidUrl();
        var state = One("state");
        if (state.Length < 32 || state.Length > 512 || state.Any(char.IsControl)) throw InvalidUrl();
        if (!Uri.TryCreate(One("redirect_uri"), UriKind.Absolute, out var redirect) || redirect.Scheme != "http" ||
            redirect.Host != "127.0.0.1" || redirect.Port < 1 || redirect.UserInfo.Length != 0 || redirect.Fragment.Length != 0)
            throw InvalidUrl();
        var scopes = One("scope").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (!scopes.SetEquals([MailScope, OpenIdScope, EmailScope])) throw InvalidUrl();
    }

    public static void ValidateIdentity(string requestedEmail, string clientId, string actualEmail, bool emailVerified,
        string subject, string? expectedSubject = null)
    {
        if (!emailVerified || string.IsNullOrWhiteSpace(subject) || subject.Length > 255 || subject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(actualEmail) || !string.Equals(requestedEmail, actualEmail, StringComparison.OrdinalIgnoreCase) ||
            (expectedSubject is not null && !string.Equals(expectedSubject, subject, StringComparison.Ordinal)))
            throw new InvalidOperationException("Giriş yapılan Google hesabı istenen posta kutusuyla eşleşmiyor.");
        if (string.IsNullOrWhiteSpace(clientId)) throw new InvalidOperationException("Google istemci kimliği eksik.");
    }

    private static InvalidOperationException InvalidUrl() => new("Google giriş bağlantısı güvenli bağlantı kurallarını karşılamıyor.");
}
