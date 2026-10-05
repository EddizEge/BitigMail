using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Bridge;
using BitigMail.Engine.Imap;
using BitigMail.Engine.Imap.Transfer;
using BitigMail.Engine.Models;
using BitigMail.LocalHost.Bridge.Transfer;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.Imap.Transfer;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Security;
using MailKit;
using MimeKit;

namespace BitigMail.Engine.Tests;

public static class BridgeTestHelpers
{
    public static string CreateTestDir(string prefix = "bridge-test")
    {
        string path = Path.Combine(Path.GetTempPath(), $"bitigmail-{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    public static (ImapAccountStore store, string sourceId, string targetId, IImapCredentialResolver resolver) SetupTestAccounts(string dir)
    {
        var protector = new WindowsImapCredentialProtector();
        var policy = new ImapConnectionPolicy(allowTask014Loopback: true);
        var store = new ImapAccountStore(Path.Combine(dir, "accounts"), protector, policy);
        var resolver = new ImapCredentialResolver(store, protector, new MsalMicrosoftAuthProvider());

        var src = store.CreateAccount(new CreateImapAccountRequest
        {
            CompanyId = "company-1",
            ProjectId = "project-1",
            DisplayName = "Source Account",
            Email = "source@example.test",
            Host = "imap.example.test",
            Port = 993,
            TlsMode = "ssl",
            Username = "source_user",
            Password = "source-password-123"
        });

        var tgt = store.CreateAccount(new CreateImapAccountRequest
        {
            CompanyId = "company-1",
            ProjectId = "project-1",
            DisplayName = "Target Account",
            Email = "target@example.test",
            Host = "imap.example.test",
            Port = 993,
            TlsMode = "ssl",
            Username = "target_user",
            Password = "target-password-456"
        });

        return (store, src.AccountId, tgt.AccountId, resolver);
    }

    public static string CreateEmlFile(string directory, string filename, string subject, string? dateHeader = "Mon, 01 Jan 2024 10:00:00 +0300", string? messageId = null, string body = "Test message body")
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("Sender", "sender@example.test"));
        msg.To.Add(new MailboxAddress("Recipient", "recipient@example.test"));
        msg.Subject = subject;
        if (!string.IsNullOrEmpty(messageId))
        {
            msg.MessageId = messageId;
        }
        if (dateHeader != null)
        {
            msg.Headers[HeaderId.Date] = dateHeader;
        }
        else
        {
            msg.Headers.Remove(HeaderId.Date);
        }
        msg.Body = new TextPart("plain") { Text = body };

        byte[] serialized = ImapSerializationAssumptions.Serialize(msg);
        string filePath = Path.Combine(directory, filename);
        File.WriteAllBytes(filePath, serialized);
        return filePath;
    }

    public static async Task WaitForJobCompletionAsync(JobManager manager, string jobId, int timeoutMs = 15000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        while (manager.ActiveRunningJobId != null)
        {
            var job = manager.GetJob(jobId);
            if (job != null && job.Status is "completed" or "failed" or "interrupted")
            {
                break;
            }
            await Task.Delay(20, cts.Token);
        }
    }
}

public class BridgeFakeTransferClient : IImapTransferClient
{
    public int FetchSingleSourceMessageCallCount { get; private set; }
    public int UidValidityCallCount { get; private set; }
    public int TargetVerificationCallCount { get; private set; }
    public int KeywordSearchCallCount { get; private set; }
    public List<string> VerificationOperations { get; } = new();
    public bool SupportsKeywords { get; set; } = true;
    public uint UidValidity { get; set; } = 12345u;
    public Dictionary<string, uint> FolderUidValidity { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Folder, MimeMessage Message, MessageFlags Flags, HashSet<string> Keywords, DateTimeOffset? InternalDate)> AppendedMessages { get; } = new();
    public List<(string Folder, uint Uid, byte[] RawBytes, string RawSha256, MessageFlags Flags, List<string> Keywords, DateTimeOffset? InternalDate)> TargetMessages { get; } = new();
    public List<ImapSourceMessageSummary> SourceMessages { get; } = new();

    public Func<string, string, Task<IReadOnlyList<uint>>>? CustomKeywordSearch { get; set; }
    public Func<string, uint, Task<ImapTargetVerificationResult>>? CustomVerification { get; set; }
    public Func<string, MimeMessage, MessageFlags, HashSet<string>, DateTimeOffset?, Task<uint>>? CustomAppend { get; set; }

    private uint _nextUid = 1000;

    public void AddSourceMessage(
        string folder,
        uint uid,
        byte[] rawBytes,
        MimeMessage message,
        DateTimeOffset? originalMimeDate,
        DateTimeOffset internalDate,
        MessageFlags flags,
        List<string>? keywords = null)
    {
        string rawSha = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();
        SourceMessages.Add(new ImapSourceMessageSummary
        {
            SourceUid = uid,
            SourceUidValidity = UidValidity,
            RawBytes = rawBytes,
            RawSha256 = rawSha,
            Message = message,
            OriginalMimeDateUtc = originalMimeDate,
            InternalDateUtc = internalDate,
            Flags = flags,
            Keywords = keywords ?? new List<string>()
        });
    }

    public Task ConnectAndAuthenticateAsync(string host, int port, string tlsMode, string username, string password, CancellationToken ct) => Task.CompletedTask;

    public Task<uint> GetFolderUidValidityAsync(string folderPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        UidValidityCallCount++;
        VerificationOperations.Add("uidvalidity");
        if (FolderUidValidity.TryGetValue(folderPath, out var val))
        {
            return Task.FromResult(val);
        }
        return Task.FromResult(UidValidity);
    }

    public Task<bool> SupportsUserKeywordsAsync(string folderPath, CancellationToken ct) => Task.FromResult(SupportsKeywords);

    public Task<IReadOnlyList<ImapSourceMessageSummary>> InspectSourceFolderAsync(string folderPath, CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<ImapSourceMessageSummary>>(SourceMessages);
    }

    public Task<IReadOnlyList<ImapMessageHeaderSummary>> FetchHeaderSummariesAsync(string folderPath, CancellationToken ct)
    {
        var summaries = SourceMessages.Select(m => new ImapMessageHeaderSummary
        {
            SourceUid = m.SourceUid,
            SourceUidValidity = m.SourceUidValidity,
            OriginalMimeDateUtc = m.OriginalMimeDateUtc,
            InternalDateUtc = m.InternalDateUtc,
            Flags = m.Flags,
            Keywords = m.Keywords
        }).ToList();
        return Task.FromResult<IReadOnlyList<ImapMessageHeaderSummary>>(summaries);
    }

    public Task<ImapSourceMessageSummary?> FetchSingleSourceMessageAsync(string folderPath, uint uid, CancellationToken ct)
    {
        FetchSingleSourceMessageCallCount++;
        var item = SourceMessages.FirstOrDefault(m => m.SourceUid == uid);
        if (item == null) return Task.FromResult<ImapSourceMessageSummary?>(null);
        using var stream = new MemoryStream(item.RawBytes, writable: false);
        return Task.FromResult<ImapSourceMessageSummary?>(new ImapSourceMessageSummary
        {
            SourceUid = item.SourceUid,
            SourceUidValidity = item.SourceUidValidity,
            RawBytes = item.RawBytes,
            RawSha256 = item.RawSha256,
            Message = MimeMessage.Load(stream, persistent: false),
            OriginalMimeDateUtc = item.OriginalMimeDateUtc,
            InternalDateUtc = item.InternalDateUtc,
            Flags = item.Flags,
            Keywords = item.Keywords.ToList()
        });
    }

    public async Task<uint> AppendMessageAsync(
        string folderPath,
        MimeMessage message,
        MessageFlags flags,
        HashSet<string> keywords,
        DateTimeOffset? internalDate,
        CancellationToken ct)
    {
        if (CustomAppend != null)
        {
            return await CustomAppend(folderPath, message, flags, keywords, internalDate);
        }

        uint uid = _nextUid++;
        AppendedMessages.Add((folderPath, message, flags, keywords, internalDate));
        byte[] bytes = ImapSerializationAssumptions.Serialize(message);
        string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        TargetMessages.Add((folderPath, uid, bytes, sha, flags, keywords.ToList(), internalDate));
        return uid;
    }

    public Task<IReadOnlyList<uint>> SearchByKeywordAsync(string folderPath, string keyword, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        KeywordSearchCallCount++;
        VerificationOperations.Add("search");
        if (CustomKeywordSearch != null)
        {
            return CustomKeywordSearch(folderPath, keyword);
        }

        var matching = TargetMessages
            .Where(m => string.Equals(m.Folder, folderPath, StringComparison.OrdinalIgnoreCase) &&
                        m.Keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase))
            .Select(m => m.Uid)
            .ToList();

        return Task.FromResult<IReadOnlyList<uint>>(matching);
    }

    public Task<ImapTargetVerificationResult> FetchAndVerifyAsync(string folderPath, uint uid, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        TargetVerificationCallCount++;
        VerificationOperations.Add("fetch");
        if (CustomVerification != null)
        {
            return CustomVerification(folderPath, uid);
        }

        var found = TargetMessages.FirstOrDefault(m => string.Equals(m.Folder, folderPath, StringComparison.OrdinalIgnoreCase) && m.Uid == uid);
        if (found.Uid != 0)
        {
            return Task.FromResult(new ImapTargetVerificationResult
            {
                Exists = true,
                RawBytes = found.RawBytes,
                RawSha256 = found.RawSha256,
                Flags = found.Flags,
                Keywords = found.Keywords,
                InternalDateUtc = found.InternalDate
            });
        }

        return Task.FromResult(new ImapTargetVerificationResult { Exists = false });
    }

    public Task DisconnectAsync(CancellationToken ct) => Task.CompletedTask;
    public void Dispose() { }
}

public class BridgeFakeClientFactory : IImapTransferClientFactory
{
    private readonly IImapTransferClient _client;
    public BridgeFakeClientFactory(IImapTransferClient client) => _client = client;
    public IImapTransferClient CreateClient() => _client;
}
