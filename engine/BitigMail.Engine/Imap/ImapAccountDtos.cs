namespace BitigMail.Engine.Imap;

/// <summary>
/// Request payload to create a new IMAP account.
/// Password is accepted only inbound and is never exposed in responses or logs.
/// </summary>
public sealed record CreateImapAccountRequest
{
    public required string CompanyId { get; init; }
    public required string ProjectId { get; init; }
    public required string DisplayName { get; init; }
    public required string Email { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string TlsMode { get; init; }
    public bool AllowUnencryptedConnection { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
}

/// <summary>
/// Request payload to update an existing IMAP account.
/// Password is optional; when omitted, the existing protected password is preserved.
/// ExpectedVersion is required for concurrency control.
/// </summary>
public sealed record UpdateImapAccountRequest
{
    public required string CompanyId { get; init; }
    public required string ProjectId { get; init; }
    public long? ExpectedVersion { get; init; }
    public string? DisplayName { get; init; }
    public string? Email { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? TlsMode { get; init; }
    public bool? AllowUnencryptedConnection { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
}

/// <summary>
/// Request payload to delete an IMAP account.
/// ExpectedVersion can optionally be specified for concurrency control.
/// </summary>
public sealed record DeleteAccountRequest
{
    public string? CompanyId { get; init; }
    public string? ProjectId { get; init; }
    public long? ExpectedVersion { get; init; }
}

/// <summary>
/// Exception thrown when an account update or delete encounters a version mismatch (concurrency conflict).
/// Inherits from InvalidOperationException and maps to HTTP 409 Conflict.
/// </summary>
public sealed class AccountVersionConflictException : InvalidOperationException
{
    public long CurrentVersion { get; }
    public long? ExpectedVersion { get; }

    public AccountVersionConflictException(long currentVersion, long? expectedVersion)
        : base($"Hesap sürüm çakışması. Beklenen sürüm: {expectedVersion?.ToString() ?? "belirtilmemiş"}, geçerli sürüm: {currentVersion}.")
    {
        CurrentVersion = currentVersion;
        ExpectedVersion = expectedVersion;
    }
}

/// <summary>
/// Request payload to test IMAP connection and credentials.
/// Can be invoked for an existing saved account (via AccountId) or draft parameters.
/// </summary>
public sealed record TestImapConnectionRequest
{
    public string? AccountId { get; init; }
    public string? CompanyId { get; init; }
    public string? ProjectId { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? TlsMode { get; init; }
    public bool? AllowUnencryptedConnection { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
}

/// <summary>
/// Safe result for IMAP connection test.
/// Never contains passwords, raw exceptions, protocol dumps, or ciphers.
/// </summary>
public sealed record TestConnectionResult
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public string? Error { get; init; }
    public long? LatencyMs { get; init; }
    public ProviderDiagnosticDto? Diagnostic { get; init; }
}
