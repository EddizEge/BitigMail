using System;

namespace BitigMail.Engine.Imap.OAuth;

/// <summary>
/// Request payload to initiate a Microsoft 365 delegated OAuth2 connection operation.
/// Does not accept clientToken, callback URLs, or codes.
/// </summary>
public sealed record StartMicrosoftOAuthRequest
{
    public required string CompanyId { get; init; }
    public required string ProjectId { get; init; }
    public required string DisplayName { get; init; }
    public required string Email { get; init; }
    public required string ClientId { get; init; }
    public required string TenantId { get; init; }
    public string? AccountId { get; init; }
    public long? ExpectedVersion { get; init; }
}

/// <summary>
/// Immediate response upon starting a Microsoft OAuth operation.
/// </summary>
public sealed record StartMicrosoftOAuthResponse
{
    public required string OperationId { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
}

/// <summary>
/// Public query DTO for Microsoft OAuth operation status.
/// AuthorizationUrl is strictly populated ONLY while status is "awaiting-signin".
/// Never exposes tokens, codes, verifiers, states, or ciphers.
/// </summary>
public sealed record MicrosoftOAuthOperationStatusDto
{
    public required string OperationId { get; init; }
    public required string Status { get; init; }
    public string? AuthorizationUrl { get; init; }
    public string? AccountId { get; init; }
    public string? Message { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
}

/// <summary>
/// Request payload to cancel a Microsoft OAuth operation.
/// </summary>
public sealed record CancelMicrosoftOAuthRequest
{
    public string? CompanyId { get; init; }
    public string? ProjectId { get; init; }
}

/// <summary>
/// Response payload upon cancelling a Microsoft OAuth operation.
/// </summary>
public sealed record CancelMicrosoftOAuthResponse
{
    public required string OperationId { get; init; }
    public required string Status { get; init; }
    public required string Message { get; init; }
    public string? AccountId { get; init; }
}
