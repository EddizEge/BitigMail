using Microsoft.AspNetCore.WebUtilities;

namespace BitigMail.LocalHost.Security;

/// <summary>Global-cloud, own-mailbox pilot boundary. MSAL owns the OAuth protocol.</summary>
public static class Microsoft365Policy
{
    public const string AuthKind = "microsoft365";
    public const string PersonalAuthority = "consumers";
    public const string PersonalTenantId = "9188040d-6c67-4c5b-b112-36a304b66dad";
    public const string ImapHost = "outlook.office365.com";
    public const int ImapPort = 993;
    public const string ImapScope = "https://outlook.office.com/IMAP.AccessAsUser.All";

    public static void ValidateConfiguration(string tenantId, string clientId)
    {
        _ = GetExpectedTenantId(tenantId);
        RequireGuid(clientId);
    }

    // The stored tenantId is an authority selector. The consumers selector must
    // never be confused with the actual GUID returned by a Microsoft account.
    public static string GetExpectedTenantId(string tenantId)
    {
        if (string.Equals(tenantId, PersonalAuthority, StringComparison.Ordinal))
            return PersonalTenantId;
        return RequireGuid(tenantId).ToString("D");
    }

    private static Guid RequireGuid(string value)
    {
        if (value is null || value.Length != 36 || !Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty)
            throw new ArgumentException("Microsoft kiracı ve uygulama kimlikleri geçerli GUID biçiminde olmalıdır.");
        return id;
    }

    public static void ValidateIdentity(string requestedEmail, string requestedTenantId,
        string actualUsername, string actualTenantId, string actualHomeAccountId,
        string? expectedHomeAccountId = null)
    {
        var expectedTenant = Guid.Parse(GetExpectedTenantId(requestedTenantId));
        if (!Guid.TryParseExact(actualTenantId, "D", out var actualTenant) || actualTenant != expectedTenant ||
            string.IsNullOrWhiteSpace(actualHomeAccountId) || actualHomeAccountId.Length > 512 || actualHomeAccountId.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(requestedEmail) || requestedEmail.Length > 320 || requestedEmail.Any(char.IsControl) ||
            !string.Equals(requestedEmail, actualUsername, StringComparison.OrdinalIgnoreCase) ||
            (expectedHomeAccountId is not null && !string.Equals(expectedHomeAccountId, actualHomeAccountId, StringComparison.Ordinal)))
            throw new InvalidOperationException("Giriş yapılan Microsoft hesabı, istenen posta kutusu veya kurumla eşleşmiyor.");
    }

    public static void ValidateAuthorizationUri(Uri uri, string tenantId, string clientId)
    {
        ValidateConfiguration(tenantId, clientId);
        if (uri is null || !uri.IsAbsoluteUri || uri.Scheme != "https" ||
            !string.Equals(uri.Host, "login.microsoftonline.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            !string.Equals(uri.AbsolutePath, $"/{tenantId}/oauth2/v2.0/authorize", StringComparison.OrdinalIgnoreCase))
            throw InvalidUrl();

        var query = QueryHelpers.ParseQuery(uri.Query);
        string Single(string key)
        {
            if (!query.TryGetValue(key, out var values) || values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
                throw InvalidUrl();
            return values[0]!;
        }

        if (!string.Equals(Single("client_id"), clientId, StringComparison.OrdinalIgnoreCase) ||
            Single("response_type") != "code" || Single("response_mode") != "form_post" || Single("code_challenge_method") != "S256")
            throw InvalidUrl();
        var challenge = Single("code_challenge");
        if (challenge.Length != 43 || challenge.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw InvalidUrl();
        var state = Single("state");
        if (state.Length > 2048 || state.Any(char.IsControl)) throw InvalidUrl();
        if (!Uri.TryCreate(Single("redirect_uri"), UriKind.Absolute, out var redirect) ||
            redirect.Scheme != "http" || redirect.Host != "localhost" || redirect.UserInfo.Length != 0 ||
            redirect.Fragment.Length != 0 || redirect.Query.Length != 0 || redirect.AbsolutePath != "/" ||
            redirect.Port < 1)
            throw InvalidUrl();
        var scopes = Single("scope").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!scopes.Contains(ImapScope, StringComparer.Ordinal) ||
            scopes.Any(s => s != ImapScope && s is not ("openid" or "profile" or "offline_access")))
            throw InvalidUrl();
    }

    private static InvalidOperationException InvalidUrl() =>
        new("Microsoft giriş bağlantısı güvenli bağlantı kurallarını karşılamıyor.");
}
