using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public class OutlookPersonalCriticalTests
{
    private const string Client = "11111111-2222-3333-4444-555555555555";
    private const string Organization = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string Email = "synthetic@outlook.com";
    private static string Url(string authority = "consumers") =>
        $"https://login.microsoftonline.com/{authority}/oauth2/v2.0/authorize?client_id={Client}&response_type=code&response_mode=form_post&code_challenge_method=S256&code_challenge={new string('a',43)}&state=secret-state-sentinel&redirect_uri=http%3A%2F%2Flocalhost%3A54321&scope={Uri.EscapeDataString(Microsoft365Policy.ImapScope + " openid profile offline_access")}";

    [Fact]
    public void PersonalAuthorityMapsToMicrosoftConsumerTenantOnly()
    {
        Microsoft365Policy.ValidateConfiguration("consumers", Client);
        Assert.Equal(Microsoft365Policy.PersonalTenantId, Microsoft365Policy.GetExpectedTenantId("consumers"));
        Assert.Equal(Organization, Microsoft365Policy.GetExpectedTenantId(Organization));
        Microsoft365Policy.ValidateAuthorizationUri(new Uri(Url()), "consumers", Client);
        Microsoft365Policy.ValidateIdentity(Email, "consumers", Email.ToUpperInvariant(),
            Microsoft365Policy.PersonalTenantId, "immutable-home", "immutable-home");
        Assert.Throws<ArgumentException>(() => Microsoft365Policy.ValidateConfiguration("consumers", "consumers"));
    }

    [Theory]
    [InlineData("common")]
    [InlineData("organizations")]
    [InlineData("Consumers")]
    [InlineData("consumers/")]
    [InlineData(" consumers")]
    [InlineData("https://login.microsoftonline.com/consumers")]
    public void NoBroadOrAmbiguousPersonalAuthority(string value) =>
        Assert.Throws<ArgumentException>(() => Microsoft365Policy.ValidateConfiguration(value, Client));

    [Theory]
    [InlineData("common")]
    [InlineData("organizations")]
    [InlineData(Organization)]
    [InlineData(Microsoft365Policy.PersonalTenantId)]
    public void PersonalUrlCannotSubstituteAnotherAuthority(string authority)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Microsoft365Policy.ValidateAuthorizationUri(new Uri(Url(authority)), "consumers", Client));
        Assert.DoesNotContain("secret-state-sentinel", error.Message);
    }

    [Fact]
    public void OrganizationalModeCannotReceiveConsumersUrlOrIdentity()
    {
        Assert.Throws<InvalidOperationException>(() => Microsoft365Policy.ValidateAuthorizationUri(new Uri(Url()), Organization, Client));
        Assert.Throws<InvalidOperationException>(() => Microsoft365Policy.ValidateIdentity(Email, Organization,
            Email, Microsoft365Policy.PersonalTenantId, "immutable-home"));
    }

    [Theory]
    [InlineData(Email, Organization, "immutable-home")]
    [InlineData(Email, "", "immutable-home")]
    [InlineData(Email, "consumers", "immutable-home")]
    [InlineData("other@outlook.com", Microsoft365Policy.PersonalTenantId, "immutable-home")]
    [InlineData(Email, Microsoft365Policy.PersonalTenantId, "different-home")]
    [InlineData(Email, Microsoft365Policy.PersonalTenantId, "")]
    public void PersonalIdentityCannotChangeMailboxTenantOrHome(string email, string tenant, string home) =>
        Assert.Throws<InvalidOperationException>(() => Microsoft365Policy.ValidateIdentity(
            Email, "consumers", email, tenant, home, "immutable-home"));
}
