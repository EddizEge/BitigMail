using BitigMail.Engine.Distribution;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class DesktopNavigationPolicyTests
{
    private static readonly DesktopNavigationPolicy Policy = new(new Uri("http://127.0.0.1:6179/"));

    [Theory]
    [InlineData("http://127.0.0.1:6179/")]
    [InlineData("http://127.0.0.1:6179/index.html")]
    [InlineData("http://127.0.0.1:6179/#search")]
    public void OwnApplicationDocumentAllowed(string address) => Assert.True(Policy.IsTrustedDocument(address));

    [Theory]
    [InlineData("http://127.0.0.1:6178/")]
    [InlineData("https://127.0.0.1:6179/")]
    [InlineData("http://localhost:6179/")]
    [InlineData("http://127.0.0.1.evil.test:6179/")]
    [InlineData("http://127.0.0.1:6179/api/session")]
    [InlineData("http://127.0.0.1:6179/index.html?apiOrigin=https://evil.test")]
    [InlineData("http://username@127.0.0.1:6179/")]
    [InlineData("file:///C:/Windows/system.ini")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hello")]
    [InlineData("about:blank")]
    [InlineData("http://127.0.0.1:6179\\index.html")]
    [InlineData(" http://127.0.0.1:6179/")]
    [InlineData("http://127.0.0.1:6179/\n")]
    public void OtherDocumentsNeverReceiveNativePrivileges(string address) => Assert.False(Policy.IsTrustedDocument(address));

    [Theory]
    [InlineData("https://login.microsoftonline.com/authorize?state=opaque", true, true)]
    [InlineData("https://accounts.google.com/", false, false)]
    [InlineData("http://127.0.0.1:9999/private", true, false)]
    [InlineData("http://localhost/private", true, false)]
    [InlineData("http://[::1]/private", true, false)]
    [InlineData("https://username:password@example.test/", true, false)]
    [InlineData("file:///C:/test.exe", true, false)]
    [InlineData("bitigmail://privileged", true, false)]
    public void ExternalLinksNeedExplicitUserActionAndHttp(string address, bool initiated, bool expected)
        => Assert.Equal(expected, Policy.MayOpenInSystemBrowser(address, initiated));

    [Theory]
    [InlineData("https://example.test/")]
    [InlineData("http://localhost:6179/")]
    [InlineData("http://127.0.0.1:6179/?proof=secret")]
    [InlineData("http://127.0.0.1:6179/#proof")]
    [InlineData("http://127.0.0.1:80/")]
    public void InvalidLaunchOriginRejected(string address)
        => Assert.Throws<ArgumentException>(() => new DesktopNavigationPolicy(new Uri(address)));
}
