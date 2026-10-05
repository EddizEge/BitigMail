using BitigMail.Engine.Imap;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.Pop;
using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class UnencryptedAccountConsentTests
{
    [Theory]
    [InlineData("none", true, true)]
    [InlineData("none", false, false)]
    [InlineData("starttls", true, false)]
    public async Task ActualImapSocketRequiresConsentAndNeverDowngradesStartTls(string mode, bool consent, bool expected)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        int authentications = 0;
        var incoming = listener.AcceptTcpClientAsync(stop.Token);
        var server = Task.Run(async () =>
        {
            try
            {
                using var socket = await incoming;
                using var stream = socket.GetStream();
                using var reader = new StreamReader(stream);
                using var writer = new StreamWriter(stream) { AutoFlush = true, NewLine = "\r\n" };
                await writer.WriteLineAsync("* OK [CAPABILITY IMAP4rev1] Synthetic server");
                while (await reader.ReadLineAsync(stop.Token) is { } line)
                {
                    var parts = line.Split(' ', 3); string tag = parts[0], command = parts[1].ToUpperInvariant();
                    if (command == "CAPABILITY") await writer.WriteLineAsync("* CAPABILITY IMAP4rev1");
                    if (command == "LOGIN") Interlocked.Increment(ref authentications);
                    if (command == "LOGOUT") { await writer.WriteLineAsync("* BYE"); await writer.WriteLineAsync(tag + " OK logout"); break; }
                    await writer.WriteLineAsync(tag + " OK completed");
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        });
        try
        {
            var result = await new ImapClientService(new ImapConnectionPolicy()).TestConnectionAsync(
                "127.0.0.1", port, mode, new ImapPasswordCredential("synthetic", "synthetic-password", consent), stop.Token);
            Assert.Equal(expected, result.Success);
            Assert.Equal(expected ? 1 : 0, Volatile.Read(ref authentications));
        }
        finally { stop.Cancel(); listener.Stop(); await server; }
    }

    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "bitigmail-transport-consent", Guid.NewGuid().ToString("N"));
    private static ImapAccountStore Open(string path) => new(path, new WindowsImapCredentialProtector(), new ImapConnectionPolicy());
    private static CreateImapAccountRequest Request(bool consent) => new()
    {
        CompanyId = "company", ProjectId = "project", DisplayName = "Synthetic", Email = "test@example.test",
        Host = "mail.example.test", Port = 143, TlsMode = "none", Username = "test", Password = "synthetic-test-password",
        AllowUnencryptedConnection = consent
    };

    [Fact]
    public void ImapRequiresConsentAndPersistsItAcrossRestart()
    {
        string path = NewDirectory(); var store = Open(path);
        Assert.Throws<ArgumentException>(() => store.CreateAccount(Request(false)));
        var account = store.CreateAccount(Request(true));
        var saved = Open(path).GetAccount(account.AccountId, "company", "project");
        Assert.NotNull(saved);
        Assert.True(saved.AllowUnencryptedConnection);
        Assert.Equal(ImapSecurityMode.PlaintextExplicitConsent, saved.SecurityMode);
    }

    [Fact]
    public void EndpointChangesRequireFreshConsentAndTlsClearsConsent()
    {
        var store = Open(NewDirectory()); var account = store.CreateAccount(Request(true));
        var update = new UpdateImapAccountRequest { CompanyId = "company", ProjectId = "project", ExpectedVersion = account.Version, Host = "other.example.test" };
        Assert.Throws<ArgumentException>(() => store.UpdateAccount(account.AccountId, update));
        var changed = store.UpdateAccount(account.AccountId, update with { AllowUnencryptedConnection = true });
        Assert.True(changed.AllowUnencryptedConnection);
        Assert.Equal(account.Version + 1, changed.Version);
        var secure = store.UpdateAccount(account.AccountId, update with { ExpectedVersion = changed.Version, TlsMode = "ssl", Port = 993 });
        Assert.False(secure.AllowUnencryptedConnection);
        Assert.Throws<ArgumentException>(() => store.UpdateAccount(account.AccountId, update with { ExpectedVersion = secure.Version, TlsMode = "none", Port = 143 }));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("invalid")]
    public void ConsentDoesNotAllowUnknownTransportModes(string mode)
    {
        Assert.Throws<ArgumentException>(() => new ImapConnectionPolicy().Validate("mail.example.test", 143, mode, "test", "synthetic", true));
    }

    [Fact]
    public void PopRequiresConsentAndResolvesSavedAccountAfterRestart()
    {
        string path = NewDirectory(); var protector = new WindowsImapCredentialProtector();
        var store = new PopAccountStore(path, protector);
        var request = new CreatePopAccountRequest("company", "project", "Synthetic", "test@example.test", "mail.example.test", 110, "none", "test", "synthetic");
        Assert.Throws<ArgumentException>(() => store.Create(request));
        var account = store.Create(request with { AllowUnencryptedConnection = true });
        var saved = new PopAccountStore(path, protector).Resolve(account.AccountId, "company", "project");
        Assert.True(saved.Account.AllowUnencryptedConnection);
        Assert.Equal("synthetic", saved.Password);
    }
}
