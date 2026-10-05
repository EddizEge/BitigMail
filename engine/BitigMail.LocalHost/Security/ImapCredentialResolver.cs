using System;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.LocalHost.Imap;
using Microsoft.Identity.Client;

namespace BitigMail.LocalHost.Security;

/// <summary>
/// Scoped credential resolver providing strongly typed credentials for IMAP connections.
/// Transparently handles DPAPI-protected password accounts and MSAL token cache acquisition
/// with silent token refresh, account-bound DPAPI protection, and buffered cache commits.
/// </summary>
public sealed class ImapCredentialResolver : IImapCredentialResolver
{
    private readonly ImapAccountStore _accountStore;
    private readonly IImapCredentialProtector _credentialProtector;
    private readonly IMicrosoftAuthProvider _authProvider;
    private readonly IGoogleAuthProvider? _googleAuthProvider;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _accountLocks = new(StringComparer.Ordinal);

    public ImapCredentialResolver(
        ImapAccountStore accountStore,
        IImapCredentialProtector credentialProtector,
        IMicrosoftAuthProvider authProvider,
        IGoogleAuthProvider? googleAuthProvider = null)
    {
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _credentialProtector = credentialProtector ?? throw new ArgumentNullException(nameof(credentialProtector));
        _authProvider = authProvider ?? throw new ArgumentNullException(nameof(authProvider));
        _googleAuthProvider = googleAuthProvider;
    }

    public async Task<ResolvedImapCredential> ResolveCredentialAsync(
        string accountId,
        string companyId,
        string projectId,
        CancellationToken ct = default)
    {
        var accountLock = _accountLocks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
        await accountLock.WaitAsync(ct);
        try
        {
            var (record, cipherBytes) = _accountStore.GetAccountRecordAndCipher(accountId, companyId, projectId);

            // 1. Password account (legacy or authKind missing / "password")
            if (string.IsNullOrEmpty(record.AuthKind) || string.Equals(record.AuthKind, "password", StringComparison.OrdinalIgnoreCase))
            {
                string password = _credentialProtector.Unprotect(cipherBytes, accountId);
                return new ResolvedImapCredential
                {
                    Account = record,
                    Credential = new ImapPasswordCredential(record.Username, password, record.AllowUnencryptedConnection),
                    CommitAsync = null
                };
            }

            // 2. Microsoft 365 OAuth account
            if (string.Equals(record.AuthKind, Microsoft365Policy.AuthKind, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(record.TenantId) ||
                    string.IsNullOrWhiteSpace(record.ClientId) ||
                    string.IsNullOrWhiteSpace(record.HomeAccountId) ||
                    !string.Equals(record.Host, Microsoft365Policy.ImapHost, StringComparison.OrdinalIgnoreCase) ||
                    record.Port != Microsoft365Policy.ImapPort ||
                    !string.Equals(record.TlsMode, "ssl", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(record.Username, record.Email, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Microsoft 365 hesap kimlik bilgileri eksik veya hasarlı.");
                }

                // Unprotect cache bound to this exact accountId entropy
                string cacheBase64 = _credentialProtector.Unprotect(cipherBytes, accountId);
                byte[] cacheBytes = Convert.FromBase64String(cacheBase64);

                string accessToken;
                byte[]? updatedCacheBytes;

                try
                {
                    (accessToken, updatedCacheBytes) = await _authProvider.AcquireTokenSilentAsync(
                        record.TenantId,
                        record.ClientId,
                        record.HomeAccountId,
                        cacheBytes,
                        ct);
                }
                catch (MsalUiRequiredException ex)
                {
                    throw new ReauthorizationRequiredException(
                        accountId,
                        "Microsoft 365 oturumunun süresi dolmuş veya yeniden yetkilendirme gerekiyor. Lütfen hesabı yeniden bağlayın.",
                        ex);
                }
                catch (Exception ex) when (ex is not ReauthorizationRequiredException && ex is not OperationCanceledException)
                {
                    throw new InvalidOperationException("Microsoft 365 oturumu yenilenirken bir hata oluştu.", ex);
                }

                // Cache updates are buffered and atomically saved only after successful operation.
                // Refresh-only cache changes do not bump metadata version.
                // Deleted/changed account/generation cannot be resurrected by late auth.
                long expectedVersion = record.Version;
                long expectedAuthGeneration = record.AuthGeneration;
                Func<Task>? commitCallback = null;

                if (updatedCacheBytes != null && updatedCacheBytes.Length > 0)
                {
                    byte[] bufferedCache = updatedCacheBytes;
                    commitCallback = async () =>
                    {
                        await accountLock.WaitAsync();
                        try
                        {
                            bool saved = _accountStore.SaveRefreshedOAuthCache(
                                accountId,
                                companyId,
                                projectId,
                                expectedVersion,
                                expectedAuthGeneration,
                                record.HomeAccountId,
                                bufferedCache);
                            if (!saved)
                            {
                                throw new InvalidOperationException("Microsoft 365 token önbelleği kaydedilemedi veya hesap durumu değişti.");
                            }
                        }
                        finally
                        {
                            accountLock.Release();
                        }
                    };
                }

                return new ResolvedImapCredential
                {
                    Account = record,
                    Credential = new ImapOAuth2Credential(record.Username, accessToken),
                    CommitAsync = commitCallback
                };
            }

            if (string.Equals(record.AuthKind, GoogleOAuthPolicy.AuthKind, StringComparison.OrdinalIgnoreCase))
            {
                if (_googleAuthProvider is null || string.IsNullOrWhiteSpace(record.ClientId) || string.IsNullOrWhiteSpace(record.HomeAccountId) ||
                    !string.Equals(record.Host, GoogleOAuthPolicy.ImapHost, StringComparison.OrdinalIgnoreCase) || record.Port != GoogleOAuthPolicy.ImapPort ||
                    !string.Equals(record.TlsMode, "ssl", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(record.Username, record.Email, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Google OAuth hesap yapılandırması eksik veya hasarlı.");
                string cacheBase64 = _credentialProtector.Unprotect(cipherBytes, accountId);
                byte[] cacheBytes = Convert.FromBase64String(cacheBase64);
                string accessToken;
                byte[]? updatedCacheBytes;
                try
                {
                    (accessToken, updatedCacheBytes) = await _googleAuthProvider.AcquireTokenSilentAsync(record.ClientId, record.HomeAccountId, cacheBytes, ct);
                }
                catch (GoogleReauthorizationRequiredException ex)
                {
                    throw new ReauthorizationRequiredException(accountId, "Google oturumunun yeniden yetkilendirilmesi gerekiyor.", ex);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new InvalidOperationException("Google oturumu yenilenirken bir hata oluştu.", ex);
                }
                var expectedVersion = record.Version;
                var expectedGeneration = record.AuthGeneration;
                Func<Task>? commit = null;
                if (updatedCacheBytes is { Length: > 0 })
                {
                    commit = async () =>
                    {
                        await accountLock.WaitAsync();
                        try
                        {
                            if (!_accountStore.SaveRefreshedOAuthCache(accountId, companyId, projectId, expectedVersion,
                                    expectedGeneration, record.HomeAccountId, updatedCacheBytes))
                                throw new InvalidOperationException("Google token önbelleği kaydedilemedi veya hesap durumu değişti.");
                        }
                        finally { accountLock.Release(); }
                    };
                }
                return new ResolvedImapCredential
                {
                    Account = record,
                    Credential = new ImapOAuth2Credential(record.Username, accessToken),
                    CommitAsync = commit
                };
            }

            throw new NotSupportedException($"Desteklenmeyen kimlik doğrulama türü: {record.AuthKind}");
        }
        finally
        {
            accountLock.Release();
        }
    }
}
