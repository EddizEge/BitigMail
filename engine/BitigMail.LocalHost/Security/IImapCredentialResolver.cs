using System;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.LocalHost.Imap;

namespace BitigMail.LocalHost.Security;

/// <summary>
/// Strongly typed IMAP connection credential abstraction.
/// Distinguishes password credentials from OAuth2 bearer token credentials.
/// OAuth2 credentials must NEVER be passed to password authentication overloads.
/// </summary>
public abstract record ImapConnectionCredential;

public sealed record ImapPasswordCredential(string Username, string Password, bool AllowUnencryptedConnection = false) : ImapConnectionCredential
{
    public override string ToString() => $"ImapPasswordCredential {{ Username = {Username}, Password = [REDACTED] }}";
}

public sealed record ImapOAuth2Credential(string Username, string AccessToken) : ImapConnectionCredential
{
    public override string ToString() => $"ImapOAuth2Credential {{ Username = {Username}, AccessToken = [REDACTED] }}";
}

/// <summary>
/// Disposable scoped resolved credential context.
/// Holds the cloned immutable account record and typed credential, plus an optional asynchronous
/// commit callback to persist buffered token cache refreshes only after successful operations.
/// </summary>
public sealed class ResolvedImapCredential : IAsyncDisposable
{
    public required ImapAccountRecord Account { get; init; }
    public required ImapConnectionCredential Credential { get; init; }
    public Func<Task>? CommitAsync { get; init; }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public override string ToString() => $"ResolvedImapCredential {{ AccountId = {Account.AccountId}, Credential = {Credential} }}";
}

/// <summary>
/// Stable exception indicating silent OAuth token acquisition failed and the user must reauthorize.
/// Translates directly to stable code "reauthorization_required".
/// </summary>
public sealed class ReauthorizationRequiredException : InvalidOperationException
{
    public const string StableCode = "reauthorization_required";
    public string AccountId { get; }

    public ReauthorizationRequiredException(string accountId, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        AccountId = accountId;
    }
}

/// <summary>
/// Scoped async credential resolver interface used by ImapClientService, preview, MailKitTransferClient, and worker.
/// </summary>
public interface IImapCredentialResolver
{
    Task<ResolvedImapCredential> ResolveCredentialAsync(
        string accountId,
        string companyId,
        string projectId,
        CancellationToken ct = default);
}
