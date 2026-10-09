using BitigMail.Engine.Distribution;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class DesktopWebViewDataFolderTests
{
    private const string Local = @"C:\Users\Someone\AppData\Local";
    private const string InstalledFolder = @"C:\Users\Someone\AppData\Local\BitigMail\WebView2";

    [Theory]
    [InlineData(@"C:\Users\Someone\AppData\Local\BitigMail\desktop")]
    [InlineData(@"C:\Users\Someone\AppData\Local\BitigMail\desktop\")]
    [InlineData(@"c:\users\someone\appdata\local\bitigmail\DESKTOP")]
    [InlineData(@"C:/Users/Someone/AppData/Local/BitigMail/desktop")]
    [InlineData(@"C:\Users\Someone\AppData\Local\BitigMail\other\..\desktop")]
    public void DefaultProfileKeepsInstalledFolder(string profile) =>
        Assert.Equal(InstalledFolder, DesktopWebViewDataFolder.Resolve(profile, Local));

    [Fact]
    public void ExplicitDefaultProfileFromSetupKeepsInstalledFolder() =>
        Assert.Equal(InstalledFolder, DesktopWebViewDataFolder.Resolve(DesktopWebViewDataFolder.DefaultProfile(Local), Local));

    [Theory]
    [InlineData(@"C:\Temp\BitigMail-smoke\profile", @"C:\Temp\BitigMail-smoke\profile\WebView2")]
    [InlineData(@"C:\Temp\BitigMail-smoke\profile\", @"C:\Temp\BitigMail-smoke\profile\WebView2")]
    [InlineData(@"C:\Users\Someone\AppData\Local\BitigMail\desktop-test", @"C:\Users\Someone\AppData\Local\BitigMail\desktop-test\WebView2")]
    [InlineData(@"C:\Users\Someone\AppData\Local\BitigMail", @"C:\Users\Someone\AppData\Local\BitigMail\WebView2")]
    [InlineData(@"C:\Users\Someone\AppData\Local\BitigMail\desktop\nested", @"C:\Users\Someone\AppData\Local\BitigMail\desktop\nested\WebView2")]
    public void OtherProfilesAreIsolated(string profile, string expected)
    {
        string resolved = DesktopWebViewDataFolder.Resolve(profile, Local);
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void TempProfileNeverSharesInstalledFolder()
    {
        string profile = Path.Combine(Path.GetTempPath(), "BitigMail-test-" + Guid.NewGuid().ToString("N"), "profile");
        string resolved = DesktopWebViewDataFolder.Resolve(profile, Local);
        Assert.NotEqual(InstalledFolder, resolved, StringComparer.OrdinalIgnoreCase);
        Assert.StartsWith(Path.GetFullPath(profile), resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("", Local)]
    [InlineData("   ", Local)]
    [InlineData(@"C:\Temp\profile", "")]
    [InlineData(@"C:\Temp\profile", @"relative\Local")]
    public void InvalidInputsAreRejected(string profile, string local) =>
        Assert.Throws<ArgumentException>(() => DesktopWebViewDataFolder.Resolve(profile, local));
}
