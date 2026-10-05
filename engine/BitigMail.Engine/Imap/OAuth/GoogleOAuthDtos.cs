namespace BitigMail.Engine.Imap.OAuth;

public sealed record StartGoogleOAuthRequest
{
    public required string CompanyId { get; init; }
    public required string ProjectId { get; init; }
    public required string DisplayName { get; init; }
    public required string Email { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public string? AccountId { get; init; }
    public long? ExpectedVersion { get; init; }
}

public sealed record StartGoogleOAuthResponse
{
    public required string OperationId { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
}

public sealed record GoogleOAuthOperationStatusDto
{
    public required string OperationId { get; init; }
    public required string Status { get; init; }
    public string? AuthorizationUrl { get; init; }
    public string? AccountId { get; init; }
    public string? Message { get; init; }
    public ProviderDiagnosticDto? Diagnostic { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
}
