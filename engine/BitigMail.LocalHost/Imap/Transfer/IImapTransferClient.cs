using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MimeKit;

namespace BitigMail.LocalHost.Imap.Transfer;

/// <summary>
/// Source message details inspected from IMAP server in read-only EXAMINE mode.
/// </summary>
public sealed class ImapSourceMessageSummary
{
    public required uint SourceUid { get; init; }
    public required uint SourceUidValidity { get; init; }
    public required byte[] RawBytes { get; init; }
    public required string RawSha256 { get; init; }
    public required MimeMessage Message { get; init; }
    public DateTimeOffset? OriginalMimeDateUtc { get; init; }
    public DateTimeOffset InternalDateUtc { get; init; }
    public MessageFlags Flags { get; init; }
    public List<string> Keywords { get; init; } = new();
    public bool HasDeletedFlag => Flags.HasFlag(MessageFlags.Deleted);
}

/// <summary>
/// Target verification result after independent FETCH from target server.
/// </summary>
public sealed class ImapTargetVerificationResult
{
    public required bool Exists { get; init; }
    public byte[]? RawBytes { get; init; }
    public string? RawSha256 { get; init; }
    public MessageFlags Flags { get; init; }
    public List<string> Keywords { get; init; } = new();
    public DateTimeOffset? InternalDateUtc { get; init; }
}

public sealed class ImapCompositeTargetVerificationResult
{
    public required uint UidValidity { get; init; }
    public required ImapTargetVerificationResult Message { get; init; }
    public required IReadOnlyList<uint> MatchingKeywordUids { get; init; }
}

/// <summary>
/// Client contract for IMAP transfer operations.
/// Public MailKit APIs only: EXAMINE, BODY.PEEK, APPEND, SEARCH, FETCH.
/// </summary>
public interface IImapTransferClient : IDisposable
{
    Task ConnectAndAuthenticateAsync(
        string host,
        int port,
        string tlsMode,
        string username,
        string password,
        CancellationToken ct);

    Task ConnectAndAuthenticateAsync(
        string host,
        int port,
        string tlsMode,
        BitigMail.LocalHost.Security.ImapConnectionCredential credential,
        CancellationToken ct) => credential is BitigMail.LocalHost.Security.ImapPasswordCredential password
            ? ConnectAndAuthenticateAsync(host, port, tlsMode, password.Username, password.Password, ct)
            : throw new NotSupportedException("Bu IMAP bağdaştırıcısı OAuth kimlik doğrulamasını desteklemiyor.");

    Task<uint> GetFolderUidValidityAsync(string folderPath, CancellationToken ct);

    Task<bool> SupportsUserKeywordsAsync(string folderPath, CancellationToken ct);

    Task<IReadOnlyList<ImapSourceMessageSummary>> InspectSourceFolderAsync(string folderPath, CancellationToken ct);

    Task<uint> AppendMessageAsync(
        string folderPath,
        MimeMessage message,
        MessageFlags flags,
        HashSet<string> keywords,
        DateTimeOffset? internalDate,
        CancellationToken ct);

    Task<IReadOnlyList<uint>> SearchByKeywordAsync(string folderPath, string keyword, CancellationToken ct);

    Task<ImapTargetVerificationResult> FetchAndVerifyAsync(string folderPath, uint uid, CancellationToken ct);

    async Task<ImapCompositeTargetVerificationResult> VerifyTargetItemAsync(
        string folderPath, uint uid, string keyword, CancellationToken ct)
    {
        uint validity = await GetFolderUidValidityAsync(folderPath, ct);
        var message = await FetchAndVerifyAsync(folderPath, uid, ct);
        var matches = await SearchByKeywordAsync(folderPath, keyword, ct);
        return new ImapCompositeTargetVerificationResult
        {
            UidValidity = validity,
            Message = message,
            MatchingKeywordUids = matches
        };
    }

    Task<IReadOnlyList<ImapMessageHeaderSummary>> FetchHeaderSummariesAsync(string folderPath, CancellationToken ct)
    {
        return InspectSourceFolderAsync(folderPath, ct).ContinueWith(t =>
        {
            var list = t.Result;
            var result = new List<ImapMessageHeaderSummary>(list.Count);
            foreach (var m in list)
            {
                result.Add(new ImapMessageHeaderSummary
                {
                    SourceUid = m.SourceUid,
                    SourceUidValidity = m.SourceUidValidity,
                    OriginalMimeDateUtc = m.OriginalMimeDateUtc,
                    InternalDateUtc = m.InternalDateUtc,
                    Flags = m.Flags,
                    Keywords = m.Keywords
                });
            }
            return (IReadOnlyList<ImapMessageHeaderSummary>)result;
        }, ct);
    }

    async Task<ImapSourceMessageSummary?> FetchSingleSourceMessageAsync(string folderPath, uint uid, CancellationToken ct)
    {
        var found = (await InspectSourceFolderAsync(folderPath, ct)).FirstOrDefault(m => m.SourceUid == uid);
        if (found == null) return null;
        using var stream = new System.IO.MemoryStream(found.RawBytes, writable: false);
        return new ImapSourceMessageSummary
        {
            SourceUid = found.SourceUid,
            SourceUidValidity = found.SourceUidValidity,
            RawBytes = found.RawBytes,
            RawSha256 = found.RawSha256,
            Message = MimeMessage.Load(stream, persistent: false),
            OriginalMimeDateUtc = found.OriginalMimeDateUtc,
            InternalDateUtc = found.InternalDateUtc,
            Flags = found.Flags,
            Keywords = found.Keywords.ToList()
        };
    }

    Task DisconnectAsync(CancellationToken ct);
}

/// <summary>
/// Light-weight header summary for paging and inspection without downloading bodies into memory.
/// </summary>
public sealed class ImapMessageHeaderSummary
{
    public required uint SourceUid { get; init; }
    public required uint SourceUidValidity { get; init; }
    public DateTimeOffset? OriginalMimeDateUtc { get; init; }
    public DateTimeOffset InternalDateUtc { get; init; }
    public MessageFlags Flags { get; init; }
    public List<string> Keywords { get; init; } = new();
    public bool HasDeletedFlag => Flags.HasFlag(MessageFlags.Deleted);
}

/// <summary>
/// Factory to instantiate transfer clients.
/// </summary>
public interface IImapTransferClientFactory
{
    IImapTransferClient CreateClient();
}
