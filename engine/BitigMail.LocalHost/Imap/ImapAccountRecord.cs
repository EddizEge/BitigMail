using System;
using BitigMail.Engine.Imap;

namespace BitigMail.LocalHost.Imap;

/// <summary>
/// Persisted account metadata record.
/// Saved to disk as JSON. Never stores plaintext passwords or secrets.
/// </summary>
public sealed class ImapAccountRecord
{
    public required string AccountId { get; set; }
    public required string CompanyId { get; set; }
    public required string ProjectId { get; set; }
    public required string DisplayName { get; set; }
    public required string Email { get; set; }
    public required string Host { get; set; }
    public required int Port { get; set; }
    public required string TlsMode { get; set; }
    public bool AllowUnencryptedConnection { get; set; }
    public required string Username { get; set; }
    public string? AuthKind { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? HomeAccountId { get; set; }
    public long Version { get; set; } = 1;
    public long AuthGeneration { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ImapAccountRecord Clone() => new()
    {
        AccountId = AccountId,
        CompanyId = CompanyId,
        ProjectId = ProjectId,
        DisplayName = DisplayName,
        Email = Email,
        Host = Host,
        Port = Port,
        TlsMode = TlsMode,
        AllowUnencryptedConnection = AllowUnencryptedConnection,
        Username = Username,
        AuthKind = AuthKind,
        TenantId = TenantId,
        ClientId = ClientId,
        HomeAccountId = HomeAccountId,
        Version = Version,
        AuthGeneration = AuthGeneration,
        CreatedAtUtc = CreatedAtUtc,
        UpdatedAtUtc = UpdatedAtUtc
    };

    public ImapAccountPublicDto ToPublicDto() => new()
    {
        AccountId = AccountId,
        CompanyId = CompanyId,
        ProjectId = ProjectId,
        DisplayName = DisplayName,
        Email = Email,
        Host = Host,
        Port = Port,
        Username = Username,
        AuthKind = string.IsNullOrEmpty(AuthKind) ? "password" : AuthKind,
        TenantId = TenantId,
        ClientId = ClientId,
        TlsMode = TlsMode,
        AllowUnencryptedConnection = AllowUnencryptedConnection,
        SecurityMode = TlsMode.ToLowerInvariant() switch
        {
            "ssl" => ImapSecurityMode.SslOnConnect,
            "starttls" => ImapSecurityMode.StartTls,
            "none" => AllowUnencryptedConnection ? ImapSecurityMode.PlaintextExplicitConsent : ImapSecurityMode.PlaintextLoopbackLabOnly,
            _ => ImapSecurityMode.SslOnConnect
        },
        Version = Version,
        CreatedAtUtc = CreatedAtUtc,
        UpdatedAtUtc = UpdatedAtUtc
    };
}
