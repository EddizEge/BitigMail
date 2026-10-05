using System;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Imap;
using BitigMail.LocalHost.Imap;

namespace BitigMail.LocalHost.Security;

/// <summary>
/// Verifies real IMAP OAuth2 connectivity and authentication before establishing durable connected account.
/// </summary>
public interface IImapOAuthConnectionTester
{
    Task VerifyConnectionAsync(string host, int port, string tlsMode, string username, string accessToken, CancellationToken ct);
}

public sealed class ImapOAuthConnectionException : InvalidOperationException
{
    public ProviderDiagnosticDto Diagnostic { get; }

    public ImapOAuthConnectionException(ProviderDiagnosticDto diagnostic)
        : base(diagnostic.Message)
    {
        Diagnostic = diagnostic;
    }
}

/// <summary>
/// Real production tester executing actual SSL connection and SaslMechanismOAuth2 authentication against outlook.office365.com:993.
/// </summary>
public sealed class RealImapOAuthConnectionTester : IImapOAuthConnectionTester
{
    private readonly ImapClientService _clientService;

    public RealImapOAuthConnectionTester(ImapClientService clientService)
    {
        _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
    }

    public async Task VerifyConnectionAsync(string host, int port, string tlsMode, string username, string accessToken, CancellationToken ct)
    {
        var result = await _clientService.TestOAuthConnectionAsync(host, port, tlsMode, username, accessToken, ct);
        if (!result.Success)
            throw new ImapOAuthConnectionException(result.Diagnostic ?? new ProviderDiagnosticDto
            {
                Code = "imap_connection_failed",
                Category = "provider",
                Retryable = false,
                Message = "Posta kutusu bağlantısı doğrulanamadı. IMAP erişimini ve sağlayıcı izinlerini kontrol edin."
            });
    }
}
