using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Imap;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Security;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace BitigMail.LocalHost.Imap.Transfer;

/// <summary>
/// Real production MailKit client implementation for IMAP transfer.
/// Strictly uses public MailKit APIs. Read-only source operations (EXAMINE/BODY.PEEK),
/// permanent keyword verification, and APPEND byte equality compliance.
/// </summary>
public sealed class MailKitTransferClient : IImapTransferClient
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private readonly ImapConnectionPolicy _connectionPolicy;
    private readonly ImapClient _client;
    private bool _disposed;

    public MailKitTransferClient(ImapConnectionPolicy connectionPolicy)
    {
        _connectionPolicy = connectionPolicy ?? throw new ArgumentNullException(nameof(connectionPolicy));
        _client = new ImapClient
        {
            Timeout = (int)DefaultTimeout.TotalMilliseconds
        };
    }

    public Task ConnectAndAuthenticateAsync(
        string host,
        int port,
        string tlsMode,
        string username,
        string password,
        CancellationToken ct)
    {
        return ConnectAndAuthenticateAsync(host, port, tlsMode, new ImapPasswordCredential(username, password), ct);
    }

    public async Task ConnectAndAuthenticateAsync(
        string host,
        int port,
        string tlsMode,
        ImapConnectionCredential credential,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(credential);

        if (credential is ImapPasswordCredential pwd)
        {
            _connectionPolicy.Validate(host, port, tlsMode, pwd.Username, pwd.Password, pwd.AllowUnencryptedConnection);
        }
        else if (credential is ImapOAuth2Credential oauth)
        {
            if ((!string.Equals(host, Microsoft365Policy.ImapHost, StringComparison.OrdinalIgnoreCase) ||
                port != Microsoft365Policy.ImapPort ||
                !string.Equals(tlsMode, "ssl", StringComparison.OrdinalIgnoreCase)) &&
                !GoogleOAuthPolicy.IsAllowedEndpoint(host, port, tlsMode))
            {
                throw new ArgumentException("OAuth2 bağlantısı yalnızca tanımlı sağlayıcıların sabit IMAP/TLS uç noktalarını destekler.");
            }
            if (string.IsNullOrWhiteSpace(oauth.Username) || string.IsNullOrWhiteSpace(oauth.AccessToken))
            {
                throw new ArgumentException("Kullanıcı adı ve OAuth erişim belirteci zorunludur.");
            }
        }
        else
        {
            throw new NotSupportedException($"Desteklenmeyen kimlik bilgisi türü: {credential.GetType().Name}");
        }

        var socketOptions = tlsMode.ToLowerInvariant() switch
        {
            "ssl" => SecureSocketOptions.SslOnConnect,
            "starttls" => SecureSocketOptions.StartTls,
            "none" => SecureSocketOptions.None,
            _ => SecureSocketOptions.SslOnConnect
        };

        try
        {
            await _client.ConnectAsync(host, port, socketOptions, ct);
            if (credential is ImapPasswordCredential passwordCred)
            {
                await _client.AuthenticateAsync(passwordCred.Username, passwordCred.Password, ct);
            }
            else if (credential is ImapOAuth2Credential oauthCred)
            {
                var oauth2 = new SaslMechanismOAuth2(oauthCred.Username, oauthCred.AccessToken);
                await _client.AuthenticateAsync(oauth2, ct);
            }
        }
        catch (Exception ex) when (ex is AuthenticationException || ex is ImapCommandException)
        {
            throw new InvalidOperationException("Kimlik doğrulama başarısız oldu. Kullanıcı adı veya parola hatalı.");
        }
        catch (SslHandshakeException)
        {
            throw new InvalidOperationException("SSL/TLS el sıkışması başarısız oldu. Sunucu sertifikası veya TLS modu doğrulanamadı.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException("IMAP sunucusuna bağlanılamadı. Bağlantı parametrelerini veya ağ durumunu kontrol edin.");
        }
    }

    public async Task<uint> GetFolderUidValidityAsync(string folderPath, CancellationToken ct)
    {
        ImapConnectionPolicy.ValidateFolderPath(folderPath);
        var folder = await _client.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);
        uint validity = folder.UidValidity;
        await folder.CloseAsync(false, ct);
        return validity;
    }

    public async Task<bool> SupportsUserKeywordsAsync(string folderPath, CancellationToken ct)
    {
        ImapConnectionPolicy.ValidateFolderPath(folderPath);
        var folder = await GetOrCreateTargetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);
        bool supports = folder.PermanentFlags.HasFlag(MessageFlags.UserDefined);
        await folder.CloseAsync(false, ct);
        return supports;
    }

    private async Task<IMailFolder> GetOrCreateTargetFolderAsync(string folderPath, CancellationToken ct)
    {
        try { return await _client.GetFolderAsync(folderPath, ct); }
        catch (FolderNotFoundException)
        {
            if (_client.PersonalNamespaces.Count == 0) throw new InvalidOperationException("Hedef klasör oluşturulabilecek posta alanı bulunamadı.");
            var parent = _client.GetFolder(_client.PersonalNamespaces[0]);
            if (parent.DirectorySeparator == '\0') throw new InvalidOperationException("Hedef sunucu klasör hiyerarşisini desteklemiyor.");
            var parts = folderPath.Split(parent.DirectorySeparator);
            if (parts.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Geçersiz hedef klasör yolu.");
            foreach (string part in parts)
            {
                try { parent = await parent.GetSubfolderAsync(part, ct); }
                catch (FolderNotFoundException) { parent = await parent.CreateAsync(part, true, ct) ?? throw new InvalidOperationException("Hedef klasör oluşturulamadı."); }
            }
            return parent;
        }
    }

    public async Task<IReadOnlyList<ImapSourceMessageSummary>> InspectSourceFolderAsync(string folderPath, CancellationToken ct)
    {
        ImapConnectionPolicy.ValidateFolderPath(folderPath);
        var folder = await _client.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        try
        {
            uint uidValidity = folder.UidValidity;
            var uids = await folder.SearchAsync(SearchQuery.All, ct);
            var results = new List<ImapSourceMessageSummary>();

            foreach (var uid in uids)
            {
                ct.ThrowIfCancellationRequested();

                byte[] rawBytes;
                using (var rawStream = await folder.GetStreamAsync(uid, ct))
                using (var mem = new MemoryStream())
                {
                    await rawStream.CopyToAsync(mem, ct);
                    rawBytes = mem.ToArray();
                }

                string rawSha256 = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();
                var message = await folder.GetMessageAsync(uid, ct);

                var summaries = await folder.FetchAsync(new[] { uid }, MessageSummaryItems.Flags | MessageSummaryItems.InternalDate, ct);
                var summary = summaries != null && summaries.Count > 0 ? summaries[0] : null;

                var flags = summary?.Flags ?? MessageFlags.None;
                var internalDate = summary?.InternalDate ?? throw new InvalidOperationException("Kaynak iletinin sunucu tarihi okunamadı.");
                var keywords = summary?.Keywords != null ? summary.Keywords.ToList() : new List<string>();

                DateTimeOffset? originalMimeDate = null;
                var dateHeader = message.Headers[HeaderId.Date];
                if (MimeSourceInspector.HasExplicitTimeZone(dateHeader) &&
                    MimeKit.Utils.DateUtils.TryParse(dateHeader, out var parsedDto))
                {
                    originalMimeDate = parsedDto.ToUniversalTime();
                }

                results.Add(new ImapSourceMessageSummary
                {
                    SourceUid = uid.Id,
                    SourceUidValidity = uidValidity,
                    RawBytes = rawBytes,
                    RawSha256 = rawSha256,
                    Message = message,
                    OriginalMimeDateUtc = originalMimeDate,
                    InternalDateUtc = internalDate,
                    Flags = flags,
                    Keywords = keywords
                });
            }

            return results;
        }
        finally
        {
            if (folder.IsOpen)
            {
                await folder.CloseAsync(false, ct);
            }
        }
    }

    public async Task<IReadOnlyList<ImapMessageHeaderSummary>> FetchHeaderSummariesAsync(string folderPath, CancellationToken ct)
    {
        ImapConnectionPolicy.ValidateFolderPath(folderPath);
        var folder = await _client.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        try
        {
            uint uidValidity = folder.UidValidity;
            var uids = await folder.SearchAsync(SearchQuery.All, ct);
            var results = new List<ImapMessageHeaderSummary>();

            if (uids.Count == 0) return results;

            const int pageSize = 250;
            for (int i = 0; i < uids.Count; i += pageSize)
            {
                ct.ThrowIfCancellationRequested();
                var chunk = uids.Skip(i).Take(pageSize).ToList();
                var summaries = await folder.FetchAsync(chunk, MessageSummaryItems.Flags | MessageSummaryItems.InternalDate | MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId, ct);
                foreach (var s in summaries)
                {
                    var flags = s.Flags ?? MessageFlags.None;
                    var internalDate = s.InternalDate ?? DateTimeOffset.UtcNow;
                    var keywords = s.Keywords != null ? s.Keywords.ToList() : new List<string>();

                    DateTimeOffset? originalMimeDate = null;
                    if (s.Envelope?.Date != null)
                    {
                        originalMimeDate = s.Envelope.Date.Value.ToUniversalTime();
                    }

                    results.Add(new ImapMessageHeaderSummary
                    {
                        SourceUid = s.UniqueId.Id,
                        SourceUidValidity = uidValidity,
                        OriginalMimeDateUtc = originalMimeDate,
                        InternalDateUtc = internalDate,
                        Flags = flags,
                        Keywords = keywords
                    });
                }
            }

            return results;
        }
        finally
        {
            if (folder.IsOpen)
            {
                await folder.CloseAsync(false, ct);
            }
        }
    }

    public async Task<ImapSourceMessageSummary?> FetchSingleSourceMessageAsync(string folderPath, uint uid, CancellationToken ct)
    {
        ImapConnectionPolicy.ValidateFolderPath(folderPath);
        var folder = await _client.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        try
        {
            uint uidValidity = folder.UidValidity;
            var uniqueId = new UniqueId(uid);

            byte[] rawBytes;
            using (var rawStream = await folder.GetStreamAsync(uniqueId, ct))
            using (var mem = new MemoryStream())
            {
                await rawStream.CopyToAsync(mem, ct);
                rawBytes = mem.ToArray();
            }

            string rawSha256 = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();
            // Parse the exact bytes already fetched and hashed instead of downloading
            // the same message again. Non-persistent parsing keeps MIME content usable
            // after this temporary stream is disposed.
            MimeMessage message;
            using (var parseStream = new MemoryStream(rawBytes, writable: false))
            {
                message = await MimeMessage.LoadAsync(parseStream, persistent: false, cancellationToken: ct);
            }

            var summaries = await folder.FetchAsync(new[] { uniqueId }, MessageSummaryItems.Flags | MessageSummaryItems.InternalDate, ct);
            var summary = summaries != null && summaries.Count > 0 ? summaries[0] : null;

            var flags = summary?.Flags ?? MessageFlags.None;
            var internalDate = summary?.InternalDate ?? throw new InvalidOperationException("Kaynak iletinin sunucu tarihi okunamadı.");
            var keywords = summary?.Keywords != null ? summary.Keywords.ToList() : new List<string>();

            DateTimeOffset? originalMimeDate = null;
            var dateHeader = message.Headers[HeaderId.Date];
            if (MimeSourceInspector.HasExplicitTimeZone(dateHeader) &&
                MimeKit.Utils.DateUtils.TryParse(dateHeader, out var parsedDto))
            {
                originalMimeDate = parsedDto.ToUniversalTime();
            }

            return new ImapSourceMessageSummary
            {
                SourceUid = uid,
                SourceUidValidity = uidValidity,
                RawBytes = rawBytes,
                RawSha256 = rawSha256,
                Message = message,
                OriginalMimeDateUtc = originalMimeDate,
                InternalDateUtc = internalDate,
                Flags = flags,
                Keywords = keywords
            };
        }
        finally
        {
            if (folder.IsOpen)
            {
                await folder.CloseAsync(false, ct);
            }
        }
    }

    public async Task<uint> AppendMessageAsync(
        string folderPath,
        MimeMessage message,
        MessageFlags flags,
        HashSet<string> keywords,
        DateTimeOffset? internalDate,
        CancellationToken ct)
    {
        ImapConnectionPolicy.ValidateFolderPath(folderPath);
        var folder = await _client.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);

        try
        {
            var options = ImapSerializationAssumptions.CreateAppendFormatOptions();
            var request = new AppendRequest(message, flags, keywords)
            {
                InternalDate = internalDate
            };
            var appendUid = await folder.AppendAsync(options, request, ct);
            if (!appendUid.HasValue)
            {
                throw new InvalidOperationException("Hedef IMAP sunucusu APPEND işlemine UID döndürmedi.");
            }
            return appendUid.Value.Id;
        }
        finally
        {
            if (folder.IsOpen)
            {
                await folder.CloseAsync(false, ct);
            }
        }
    }

    public async Task<IReadOnlyList<uint>> SearchByKeywordAsync(string folderPath, string keyword, CancellationToken ct)
    {
        ImapConnectionPolicy.ValidateFolderPath(folderPath);
        var folder = await _client.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        try
        {
            var uids = await folder.SearchAsync(SearchQuery.HasKeyword(keyword), ct);
            return uids.Select(u => u.Id).ToList();
        }
        finally
        {
            if (folder.IsOpen)
            {
                await folder.CloseAsync(false, ct);
            }
        }
    }

    public async Task<ImapTargetVerificationResult> FetchAndVerifyAsync(string folderPath, uint uid, CancellationToken ct)
    {
        ImapConnectionPolicy.ValidateFolderPath(folderPath);
        var folder = await _client.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        try
        {
            var targetUid = new UniqueId(uid);
            byte[] rawBytes;
            using (var stream = await folder.GetStreamAsync(targetUid, ct))
            using (var mem = new MemoryStream())
            {
                await stream.CopyToAsync(mem, ct);
                rawBytes = mem.ToArray();
            }

            string rawSha256 = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();
            var summaries = await folder.FetchAsync(new[] { targetUid }, MessageSummaryItems.Flags | MessageSummaryItems.InternalDate, ct);
            if (summaries == null || summaries.Count == 0)
            {
                return new ImapTargetVerificationResult { Exists = false };
            }

            var summary = summaries[0];
            return new ImapTargetVerificationResult
            {
                Exists = true,
                RawBytes = rawBytes,
                RawSha256 = rawSha256,
                Flags = (summary.Flags ?? MessageFlags.None) & ~MessageFlags.Recent,
                Keywords = summary.Keywords?.ToList() ?? new List<string>(),
                InternalDateUtc = summary.InternalDate
            };
        }
        catch
        {
            return new ImapTargetVerificationResult { Exists = false };
        }
        finally
        {
            if (folder.IsOpen)
            {
                await folder.CloseAsync(false, ct);
            }
        }
    }

    public async Task<ImapCompositeTargetVerificationResult> VerifyTargetItemAsync(
        string folderPath, uint uid, string keyword, CancellationToken ct)
    {
        ImapConnectionPolicy.ValidateFolderPath(folderPath);
        var folder = await _client.GetFolderAsync(folderPath, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        try
        {
            uint validity = folder.UidValidity;
            var targetUid = new UniqueId(uid);
            ImapTargetVerificationResult message;
            try
            {
                byte[] rawBytes;
                using (var stream = await folder.GetStreamAsync(targetUid, ct))
                using (var mem = new MemoryStream())
                {
                    await stream.CopyToAsync(mem, ct);
                    rawBytes = mem.ToArray();
                }

                string rawSha256 = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();
                var summaries = await folder.FetchAsync(new[] { targetUid }, MessageSummaryItems.Flags | MessageSummaryItems.InternalDate, ct);
                if (summaries == null || summaries.Count == 0)
                {
                    message = new ImapTargetVerificationResult { Exists = false };
                }
                else
                {
                    var summary = summaries[0];
                    message = new ImapTargetVerificationResult
                    {
                        Exists = true,
                        RawBytes = rawBytes,
                        RawSha256 = rawSha256,
                        Flags = (summary.Flags ?? MessageFlags.None) & ~MessageFlags.Recent,
                        Keywords = summary.Keywords?.ToList() ?? new List<string>(),
                        InternalDateUtc = summary.InternalDate
                    };
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message = new ImapTargetVerificationResult { Exists = false };
            }

            var matches = await folder.SearchAsync(SearchQuery.HasKeyword(keyword), ct);
            return new ImapCompositeTargetVerificationResult
            {
                UidValidity = validity,
                Message = message,
                MatchingKeywordUids = matches.Select(value => value.Id).ToList()
            };
        }
        finally
        {
            if (folder.IsOpen)
            {
                await folder.CloseAsync(false, CancellationToken.None);
            }
        }
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        if (_client.IsConnected)
        {
            try
            {
                await _client.DisconnectAsync(true, ct);
            }
            catch { }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _client.Dispose();
        }
    }
}

public sealed class MailKitTransferClientFactory : IImapTransferClientFactory
{
    private readonly ImapConnectionPolicy _connectionPolicy;

    public MailKitTransferClientFactory(ImapConnectionPolicy connectionPolicy)
    {
        _connectionPolicy = connectionPolicy ?? throw new ArgumentNullException(nameof(connectionPolicy));
    }

    public IImapTransferClient CreateClient() => new MailKitTransferClient(_connectionPolicy);
}
