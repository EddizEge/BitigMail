namespace BitigMail.Engine.Imap;

public sealed record ProviderCapabilityDto
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string AuthKind { get; init; }
    public required string Endpoint { get; init; }
    public required bool Source { get; init; }
    public required bool Target { get; init; }
    public required bool OAuth { get; init; }
    public required string SupportLevel { get; init; }
    public string[] UnsupportedCapabilities { get; init; } = [];
}

public sealed record ProviderDiagnosticDto
{
    public required string Code { get; init; }
    public required string Category { get; init; }
    public required bool Retryable { get; init; }
    public int? RetryAfterSeconds { get; init; }
    public required string Message { get; init; }
}
