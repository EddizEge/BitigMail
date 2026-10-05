using System.Security.Cryptography;
using System.Text;
using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public class ImapSecurityTests
{
    [Theory]
    [InlineData("imap.example", 993, "ssl")]
    [InlineData("imap.example", 143, "starttls")]
    [InlineData("192.168.1.2", 993, "ssl")]
    [InlineData("::1", 993, "ssl")]
    [InlineData("posta.örnek.test", 993, "ssl")]
    public void ProductionAcceptsExplicitTls(string host, int port, string mode) =>
        new ImapConnectionPolicy().Validate(host, port, mode, "user@example.test", "test-password");

    [Theory]
    [InlineData("imap.example", 143, "none")]
    [InlineData("127.0.0.1", 5143, "none")]
    [InlineData("imap.example", 143, "auto")]
    [InlineData("imap.example", 143, "starttlswhenavailable")]
    [InlineData("imap.example", 0, "ssl")]
    [InlineData("imap.example", 65536, "ssl")]
    [InlineData("imap.example\r\nLOGIN", 993, "ssl")]
    [InlineData("https://imap.example", 993, "ssl")]
    [InlineData("user@imap.example", 993, "ssl")]
    [InlineData("imap.example/path", 993, "ssl")]
    [InlineData("imap.example ", 993, "ssl")]
    [InlineData("*.example", 993, "ssl")]
    [InlineData("imap..example", 993, "ssl")]
    public void ProductionRejectsDowngradeAndInvalidAddress(string host, int port, string mode) =>
        Assert.Throws<ArgumentException>(() => new ImapConnectionPolicy().Validate(host, port, mode, "user", "secret"));

    [Theory]
    [InlineData("localhost", 5143)]
    [InlineData("127.0.0.2", 5143)]
    [InlineData("127.0.0.1", 143)]
    [InlineData("::1", 5143)]
    [InlineData("2130706433", 5143)]
    public void LabExceptionIsOnlyOneExactEndpoint(string host, int port) =>
        Assert.Throws<ArgumentException>(() => new ImapConnectionPolicy(true).Validate(host, port, "none", "user", "secret"));

    [Fact]
    public void TrustedTestingPolicyAllowsOnlyConfiguredLab() =>
        new ImapConnectionPolicy(true).Validate("127.0.0.1", 5143, "none", "source", "secret");

    [Theory]
    [InlineData("user\r\n", "secret")]
    [InlineData("user", "secret\0")]
    [InlineData("", "secret")]
    [InlineData("user", "")]
    public void CredentialsCannotInjectProtocolTokens(string user, string password) =>
        Assert.Throws<ArgumentException>(() => new ImapConnectionPolicy().Validate("mail.example", 993, "ssl", user, password));

    [Fact]
    public void FolderAndScopeRejectControlCharacters()
    {
        ImapConnectionPolicy.ValidateFolderPath("Projeler.İstanbul");
        ImapConnectionPolicy.ValidateScope("company-a", "project-a");
        Assert.Throws<ArgumentException>(() => ImapConnectionPolicy.ValidateFolderPath("Inbox\r\nAPPEND"));
        Assert.Throws<ArgumentException>(() => ImapConnectionPolicy.ValidateScope("company-a", ""));
    }

    [Fact]
    public void DpapiRoundTripAcrossInstancesNeverStoresPlaintext()
    {
        const string secret = "synthetic-şifre-秘密-014";
        byte[] cipher = new WindowsImapCredentialProtector().Protect(secret, "account-test-014");
        Assert.False(cipher.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret)) >= 0);
        Assert.Equal(secret, new WindowsImapCredentialProtector().Unprotect(cipher, "account-test-014"));
        Assert.Throws<CryptographicException>(() => new WindowsImapCredentialProtector().Unprotect(cipher, "account-other"));
        cipher[^1] ^= 0x55;
        Assert.Throws<CryptographicException>(() => new WindowsImapCredentialProtector().Unprotect(cipher, "account-test-014"));
    }
}

