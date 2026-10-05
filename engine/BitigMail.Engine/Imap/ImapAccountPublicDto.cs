using System;

namespace BitigMail.Engine.Imap;

/// <summary>
/// Public safe DTO for IMAP account representation.
/// Guaranteed to never expose passwords, secrets, or internal connection handles.
/// </summary>
public sealed record ImapAccountPublicDto
{
    public required string AccountId { get; init; }
    public string CompanyId { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
    public required string DisplayName { get; init; }
    public required string Email { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public string Username { get; init; } = string.Empty;
    public string AuthKind { get; init; } = "password";
    public string? TenantId { get; init; }
    public string? ClientId { get; init; }
    public string TlsMode { get; init; } = "ssl";
    public bool AllowUnencryptedConnection { get; init; }
    public required ImapSecurityMode SecurityMode { get; init; }
    public long Version { get; init; } = 1;
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
