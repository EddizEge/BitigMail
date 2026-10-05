using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public class Microsoft365PolicyTests
{
    private const string Tenant = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string Client = "11111111-2222-3333-4444-555555555555";
    private static string ValidUrl => $"https://login.microsoftonline.com/{Tenant}/oauth2/v2.0/authorize?client_id={Client}&response_type=code&response_mode=form_post&code_challenge_method=S256&code_challenge={new string('a', 43)}&state=opaque-state&redirect_uri=http%3A%2F%2Flocalhost%3A54321&scope={Uri.EscapeDataString(Microsoft365Policy.ImapScope + " openid profile offline_access")}";

    [Fact]
    public void AcceptsPinnedPublicClientAuthorization() =>
        Microsoft365Policy.ValidateAuthorizationUri(new Uri(ValidUrl), Tenant, Client);

    [Theory]
    [InlineData("")]
    [InlineData("common")]
    [InlineData("organizations")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/")]
    [InlineData("{aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee}")]
    public void RejectsUnboundedAuthoritiesAndInvalidConfiguration(string value)
    {
        Assert.Throws<ArgumentException>(() => Microsoft365Policy.ValidateConfiguration(value, Client));
        Assert.Throws<ArgumentException>(() => Microsoft365Policy.ValidateConfiguration(Tenant, value));
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com", "http://login.microsoftonline.com")]
    [InlineData("https://login.microsoftonline.com", "https://login.microsoftonline.com.evil.test")]
    [InlineData("https://login.microsoftonline.com", "https://user@login.microsoftonline.com")]
    [InlineData("https://login.microsoftonline.com", "https://login.microsoftonline.com:444")]
    [InlineData("/oauth2/v2.0/authorize", "/oauth2/v2.0/token")]
    [InlineData("response_type=code", "response_type=token")]
    [InlineData("response_mode=form_post", "response_mode=query")]
    [InlineData("code_challenge_method=S256", "code_challenge_method=plain")]
    [InlineData("state=opaque-state", "state=opaque-state&state=another")]
    [InlineData("localhost%3A54321", "evil.test%3A54321")]
    [InlineData("localhost%3A54321", "localhost%3A54321%2Fcallback")]
    [InlineData("scope=", "scope=https%3A%2F%2Fgraph.microsoft.com%2FMail.Read%20")]
    [InlineData("11111111-2222-3333-4444-555555555555", "22222222-2222-3333-4444-555555555555")]
    public void RefusesUriSubstitutionWithoutReflectingSensitiveUrl(string from, string to)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Microsoft365Policy.ValidateAuthorizationUri(new Uri(ValidUrl.Replace(from, to)), Tenant, Client));
        Assert.DoesNotContain("opaque-state", ex.Message);
        Assert.DoesNotContain("evil.test", ex.Message);
    }

    [Fact]
    public void RejectsFragmentAndDuplicateClient() {
        Assert.Throws<InvalidOperationException>(() => Microsoft365Policy.ValidateAuthorizationUri(new Uri(ValidUrl + "#token=secret"), Tenant, Client));
        Assert.Throws<InvalidOperationException>(() => Microsoft365Policy.ValidateAuthorizationUri(new Uri(ValidUrl + "&client_id=" + Client), Tenant, Client));
    }

    [Fact]
    public void OwnMailboxAndSameReconnectIdentityAccepted() =>
        Microsoft365Policy.ValidateIdentity("USER@example.test", Tenant, "user@example.test", Tenant.ToUpperInvariant(), "home.tenant", "home.tenant");

    [Theory]
    [InlineData("other@example.test", Tenant, "home.tenant", "home.tenant")]
    [InlineData("user@example.test", Client, "home.tenant", "home.tenant")]
    [InlineData("user@example.test", Tenant, "", null)]
    [InlineData("user@example.test", Tenant, "different-home.tenant", "home.tenant")]
    public void RefusesDifferentMailboxTenantOrReconnectIdentity(string user, string tenant, string home, string? expected) =>
        Assert.Throws<InvalidOperationException>(() => Microsoft365Policy.ValidateIdentity("user@example.test", Tenant, user, tenant, home, expected));
}
