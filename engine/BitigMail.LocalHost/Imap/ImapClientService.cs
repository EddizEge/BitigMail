using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Imap;
using BitigMail.LocalHost.Security;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;

namespace BitigMail.LocalHost.Imap;

/// <summary>
/// Service managing real IMAP connections, safe connection verification, and folder discovery.
/// Enforces strict redacted error handling with zero credential, cipher, or protocol trace leakage.
/// Supports both password and OAuth2 credentials (SaslMechanismOAuth2).
/// </summary>
public sealed class ImapClientService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private readonly ImapConnectionPolicy _connectionPolicy;
    private readonly IImapCredentialResolver? _credentialResolver;

    public ImapClientService(
        ImapConnectionPolicy connectionPolicy,
        IImapCredentialResolver? credentialResolver = null)
    {
        _connectionPolicy = connectionPolicy ?? throw new ArgumentNullException(nameof(connectionPolicy));
        _credentialResolver = credentialResolver;
    }

    public Task<TestConnectionResult> TestConnectionAsync(
        string host,
        int port,
        string tlsMode,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        return TestConnectionAsync(host, port, tlsMode, new ImapPasswordCredential(username, password), cancellationToken);
    }

    public Task<TestConnectionResult> TestOAuthConnectionAsync(
        string host,
        int port,
        string tlsMode,
        string username,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        return TestConnectionAsync(host, port, tlsMode, new ImapOAuth2Credential(username, accessToken), cancellationToken);
    }

    public async Task<TestConnectionResult> TestAccountConnectionAsync(
        string accountId,
        string companyId,
        string projectId,
        CancellationToken ct = default)
    {
        if (_credentialResolver == null)
            throw new InvalidOperationException("Credential resolver yapılandırılmamış.");

        await using var resolved = await _credentialResolver.ResolveCredentialAsync(accountId, companyId, projectId, ct);
        var result = await TestConnectionAsync(
            resolved.Account.Host,
            resolved.Account.Port,
            resolved.Account.TlsMode,
            resolved.Credential,
            ct);

        if (result.Success && resolved.CommitAsync != null)
        {
            await resolved.CommitAsync();
        }

        return result;
    }

    public async Task<TestConnectionResult> TestConnectionAsync(
        string host,
        int port,
        string tlsMode,
        ImapConnectionCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        // 1. Validate boundary policy
        try
        {
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
        }
        catch (ArgumentException ex)
        {
            return new TestConnectionResult
            {
                Success = false,
                Error = ex.Message
            };
        }

        var sw = Stopwatch.StartNew();
        using var client = new ImapClient
        {
            Timeout = (int)DefaultTimeout.TotalMilliseconds
        };

        var socketOptions = MapSocketOptions(tlsMode);

        try
        {
            await client.ConnectAsync(host, port, socketOptions, cancellationToken);
            if (credential is ImapPasswordCredential pwd)
            {
                await client.AuthenticateAsync(pwd.Username, pwd.Password, cancellationToken);
            }
            else if (credential is ImapOAuth2Credential oauth)
            {
                var oauth2 = new SaslMechanismOAuth2(oauth.Username, oauth.AccessToken);
                await client.AuthenticateAsync(oauth2, cancellationToken);
            }
            sw.Stop();

            return new TestConnectionResult
            {
                Success = true,
                Message = "Bağlantı ve kimlik doğrulama başarılı.",
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex) when (ex is AuthenticationException || ex is ImapCommandException)
        {
            return new TestConnectionResult
            {
                Success = false,
                Error = "Kimlik doğrulama başarısız oldu. Kullanıcı adı veya parola hatalı.",
                Diagnostic = Diagnostic("authentication_rejected", "authentication", false, "IMAP kimlik doğrulaması reddedildi.")
            };
        }
        catch (SslHandshakeException)
        {
            return new TestConnectionResult
            {
                Success = false,
                Error = "SSL/TLS el sıkışması başarısız oldu. Sunucu sertifikası veya TLS modu doğrulanamadı.",
                Diagnostic = Diagnostic("tls_validation_failed", "tls", false, "IMAP TLS doğrulaması başarısız oldu.")
            };
        }
        catch (SocketException)
        {
            return new TestConnectionResult
            {
                Success = false,
                Error = "IMAP sunucusuna bağlanılamadı. Sunucu adresi veya port erişilemiyor.",
                Diagnostic = Diagnostic("network_unavailable", "network", true, "IMAP sunucusuna ulaşılamadı.")
            };
        }
        catch (TimeoutException)
        {
            return new TestConnectionResult
            {
                Success = false,
                Error = "IMAP sunucusu zaman aşımına uğradı veya yanıt vermedi.",
                Diagnostic = Diagnostic("network_timeout", "network", true, "IMAP bağlantısı zaman aşımına uğradı.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new TestConnectionResult
            {
                Success = false,
                Error = "IMAP bağlantısı kurulamadı. Sunucu ayarlarınızı kontrol edin."
            };
        }
        finally
        {
            if (client.IsConnected)
            {
                try { await client.DisconnectAsync(true, cancellationToken); } catch { }
            }
        }
    }

    private static ProviderDiagnosticDto Diagnostic(string code, string category, bool retryable, string message) =>
        new() { Code = code, Category = category, Retryable = retryable, Message = message };

    public Task<IReadOnlyList<ImapFolderDto>> ListFoldersAsync(
        string host,
        int port,
        string tlsMode,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        return ListFoldersAsync(host, port, tlsMode, new ImapPasswordCredential(username, password), cancellationToken);
    }

    public async Task<IReadOnlyList<ImapFolderDto>> ListAccountFoldersAsync(
        string accountId,
        string companyId,
        string projectId,
        CancellationToken ct = default)
    {
        if (_credentialResolver == null)
            throw new InvalidOperationException("Credential resolver yapılandırılmamış.");

        await using var resolved = await _credentialResolver.ResolveCredentialAsync(accountId, companyId, projectId, ct);
        var folders = await ListFoldersAsync(
            resolved.Account.Host,
            resolved.Account.Port,
            resolved.Account.TlsMode,
            resolved.Credential,
            ct);

        if (resolved.CommitAsync != null)
        {
            await resolved.CommitAsync();
        }

        return folders;
    }

    public async Task<IReadOnlyList<ImapFolderDto>> ListFoldersAsync(
        string host,
        int port,
        string tlsMode,
        ImapConnectionCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        // 1. Validate boundary policy
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

        using var client = new ImapClient
        {
            Timeout = (int)DefaultTimeout.TotalMilliseconds
        };

        var socketOptions = MapSocketOptions(tlsMode);

        try
        {
            await client.ConnectAsync(host, port, socketOptions, cancellationToken);
            if (credential is ImapPasswordCredential passwordCred)
            {
                await client.AuthenticateAsync(passwordCred.Username, passwordCred.Password, cancellationToken);
            }
            else if (credential is ImapOAuth2Credential oauthCred)
            {
                var oauth2 = new SaslMechanismOAuth2(oauthCred.Username, oauthCred.AccessToken);
                await client.AuthenticateAsync(oauth2, cancellationToken);
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
        catch (SocketException)
        {
            throw new InvalidOperationException("IMAP sunucusuna bağlanılamadı. Sunucu adresi veya port erişilemiyor.");
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("IMAP sunucusu zaman aşımına uğradı veya yanıt vermedi.");
        }
        catch (Exception)
        {
            throw new InvalidOperationException("IMAP bağlantısı kurulamadı. Sunucu ayarlarınızı kontrol edin.");
        }

        try
        {
            var folders = new List<ImapFolderDto>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void DiscoverFolders(IMailFolder folder)
            {
                string fullName = folder.FullName;
                if (!string.IsNullOrEmpty(fullName) && visited.Add(fullName))
                {
                    ImapConnectionPolicy.ValidateFolderPath(fullName);

                    bool isSelectable = !folder.Attributes.HasFlag(FolderAttributes.NoSelect) &&
                                        !folder.Attributes.HasFlag(FolderAttributes.NonExistent);
                    int? messageCount = null;
                    int? unreadCount = null;
                    bool countValid = true;
                    string? statusError = null;

                    if (isSelectable)
                    {
                        var (valid, count, unread, error) = ResolveFolderCounts(
                            () =>
                            {
                                folder.Status(StatusItems.Count | StatusItems.Unread, cancellationToken);
                                return (folder.Count, (int?)folder.Unread);
                            },
                            () =>
                            {
                                folder.Open(FolderAccess.ReadOnly, cancellationToken);
                                int c = folder.Count;
                                int? u = folder.Unread;
                                folder.Close(false, cancellationToken);
                                return (c, u);
                            });

                        countValid = valid;
                        messageCount = count;
                        unreadCount = unread;
                        statusError = error;
                    }

                    folders.Add(new ImapFolderDto
                    {
                        Name = folder.Name,
                        FullPath = fullName,
                        Delimiter = folder.DirectorySeparator.ToString(),
                        IsSelectable = isSelectable,
                        MessageCount = messageCount,
                        UnreadCount = unreadCount,
                        CountValid = countValid,
                        StatusError = statusError
                    });
                }

                IEnumerable<IMailFolder> subfolders;
                try
                {
                    subfolders = folder.GetSubfolders(false, cancellationToken);
                }
                catch (Exception ex) when (ex is not InvalidOperationException && ex is not ArgumentException)
                {
                    throw new InvalidOperationException("IMAP alt klasörleri taranırken sunucu hatası oluştu.");
                }

                foreach (var sub in subfolders)
                {
                    DiscoverFolders(sub);
                }
            }

            if (client.PersonalNamespaces.Count > 0)
            {
                foreach (var ns in client.PersonalNamespaces)
                {
                    IMailFolder rootFolder;
                    try
                    {
                        rootFolder = client.GetFolder(ns);
                    }
                    catch (Exception ex) when (ex is not InvalidOperationException && ex is not ArgumentException)
                    {
                        throw new InvalidOperationException("IMAP kök klasörleri alınırken sunucu hatası oluştu.");
                    }
                    DiscoverFolders(rootFolder);
                }
            }
            else
            {
                IMailFolder rootFolder;
                try
                {
                    rootFolder = client.GetFolder("");
                }
                catch (Exception ex) when (ex is not InvalidOperationException && ex is not ArgumentException)
                {
                    throw new InvalidOperationException("IMAP kök klasörleri alınırken sunucu hatası oluştu.");
                }
                DiscoverFolders(rootFolder);
            }

            if (client.Inbox != null && !visited.Contains(client.Inbox.FullName))
            {
                DiscoverFolders(client.Inbox);
            }

            return folders
                .OrderBy(f => f.FullPath.Equals("INBOX", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            if (client.IsConnected)
            {
                try { await client.DisconnectAsync(true, cancellationToken); } catch { }
            }
        }
    }

    /// <summary>
    /// Deterministically resolves folder counts with STATUS, falling back to safe read-only Open.
    /// If both fail, marks count as invalid with stable redacted error and never silently returns messageCount=0.
    /// </summary>
    public static (bool countValid, int? messageCount, int? unreadCount, string? statusError) ResolveFolderCounts(
        Func<(int count, int? unread)> statusAttempt,
        Func<(int count, int? unread)> readOnlyOpenAttempt)
    {
        try
        {
            var (c, u) = statusAttempt();
            return (true, c, u, null);
        }
        catch
        {
            try
            {
                var (c, u) = readOnlyOpenAttempt();
                return (true, c, u, null);
            }
            catch
            {
                return (false, null, null, "Klasör ileti sayısı okunamadı.");
            }
        }
    }

    private static SecureSocketOptions MapSocketOptions(string tlsMode) =>
        tlsMode.ToLowerInvariant() switch
        {
            "ssl" => SecureSocketOptions.SslOnConnect,
            "starttls" => SecureSocketOptions.StartTls,
            "none" => SecureSocketOptions.None,
            _ => throw new ArgumentException($"Geçersiz TLS modu: {tlsMode}")
        };
}

