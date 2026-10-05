using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Imap;
using BitigMail.Engine.Imap.OAuth;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.Security;
using Microsoft.Identity.Client;

namespace BitigMail.LocalHost.OAuth;

/// <summary>
/// In-memory state tracking for an active or terminal Microsoft OAuth connection operation.
/// Authorization URL and sensitive parameters are kept strictly in transient memory (never persisted to disk or logged).
/// </summary>
internal sealed class MicrosoftOAuthOperation
{
    public AuthenticatedSessionPrincipal? Actor { get; init; }
    public required string OperationId { get; init; }
    public required string CompanyId { get; init; }
    public required string ProjectId { get; init; }
    public required string DisplayName { get; init; }
    public required string RequestedEmail { get; init; }
    public required string ClientId { get; init; }
    public required string TenantId { get; init; }
    public string? AccountId { get; init; }
    public long? ExpectedVersion { get; init; }
    public long? ExpectedAuthGeneration { get; init; }

    public string Status { get; set; } = "preparing";
    public string? AuthorizationUrl { get; set; }
    public string? ConnectedAccountId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public CancellationTokenSource Cts { get; } = new();
    public object StateLock { get; } = new();
    public TaskCompletionSource<bool> CompletionSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task CompletionTask => CompletionSource.Task;

    public bool IsTerminal => Status is "connected" or "cancelled" or "expired" or "failed";
}

/// <summary>
/// Lifecycle manager for short-lived, bounded, protected Microsoft OAuth connection operations.
/// </summary>
public sealed class MicrosoftOAuthOperationManager : IDisposable
{
    private readonly EphemeralOperationAuthorization _authorization;
    private const int MaxOperations = 32;
    private static readonly TimeSpan MaxOperationLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CleanupRetentionTtl = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, MicrosoftOAuthOperation> _operations = new(StringComparer.Ordinal);
    private readonly ImapAccountStore _accountStore;
    private readonly IMicrosoftAuthProvider _authProvider;
    private readonly IImapOAuthConnectionTester _connectionTester;
    private readonly object _gateLock = new();
    private bool _disposed;

    public MicrosoftOAuthOperationManager(
        ImapAccountStore accountStore,
        IMicrosoftAuthProvider authProvider,
        IImapOAuthConnectionTester connectionTester, IHttpContextAccessor? http=null, IdentityCatalog? identities=null, AuthenticatedSessionRegistry? sessions=null, SecurityConfig? config=null)
    {
        _authorization = new(http,identities,sessions,config);
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _authProvider = authProvider ?? throw new ArgumentNullException(nameof(authProvider));
        _connectionTester = connectionTester ?? throw new ArgumentNullException(nameof(connectionTester));
    }

    public Task WaitForCompletionAsync(string operationId)
    {
        if (_operations.TryGetValue(operationId, out var op))
        {
            if (!_authorization.IsCaller(op.Actor,op.CompanyId,op.ProjectId)) throw new KeyNotFoundException("İşlem bulunamadı.");
            return op.CompletionTask;
        }
        throw new KeyNotFoundException("İşlem bulunamadı.");
    }

    public StartMicrosoftOAuthResponse StartOperation(StartMicrosoftOAuthRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);
        var actor = _authorization.Capture(req.CompanyId,req.ProjectId);

        // 1. Strict validation
        ImapConnectionPolicy.ValidateScope(req.CompanyId, req.ProjectId);
        Microsoft365Policy.ValidateConfiguration(req.TenantId, req.ClientId);
        ImapAccountStore.ValidateDisplayName(req.DisplayName);
        ImapAccountStore.ValidateEmail(req.Email);

        long? expectedAuthGeneration = null;
        // Reconnect validation
        if (!string.IsNullOrWhiteSpace(req.AccountId))
        {
            ImapAccountStore.ValidateAccountId(req.AccountId);
            if (!req.ExpectedVersion.HasValue)
            {
                throw new ArgumentException("Yeniden yetkilendirme işlemi için expectedVersion parametresi zorunludur.");
            }

            var existingAccount = _accountStore.GetAccount(req.AccountId, req.CompanyId, req.ProjectId);
            if (existingAccount == null)
            {
                throw new KeyNotFoundException($"Yeniden bağlanacak hesap bulunamadı: {req.AccountId}");
            }

            if (existingAccount.Version != req.ExpectedVersion.Value)
            {
                throw new AccountVersionConflictException(existingAccount.Version, req.ExpectedVersion.Value);
            }

            if (!string.Equals(existingAccount.AuthKind, Microsoft365Policy.AuthKind, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Yalnızca Microsoft 365 hesapları bu akışla yeniden bağlanabilir.");
            }

            if (!string.Equals(existingAccount.TenantId, req.TenantId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(existingAccount.ClientId, req.ClientId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Yeniden yetkilendirme kiracı veya uygulama kimliği mevcut hesapla uyuşmuyor.");
            }

            if (!string.Equals(existingAccount.Email, req.Email, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Yeniden yetkilendirme e-posta adresi mevcut hesapla uyuşmuyor.");
            }
            var (record, _) = _accountStore.GetAccountRecordAndCipher(req.AccountId, req.CompanyId, req.ProjectId);
            expectedAuthGeneration = record.AuthGeneration;
        }

        // Generate 128-bit random operation ID
        byte[] randomBytes = RandomNumberGenerator.GetBytes(16);
        string operationId = "op_" + Convert.ToHexString(randomBytes).ToLowerInvariant();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAt = now.Add(MaxOperationLifetime);

        var op = new MicrosoftOAuthOperation
        {
            Actor = actor,
            OperationId = operationId,
            CompanyId = req.CompanyId,
            ProjectId = req.ProjectId,
            DisplayName = req.DisplayName.Trim(),
            RequestedEmail = req.Email.Trim(),
            ClientId = req.ClientId.Trim(),
            TenantId = req.TenantId.Trim(),
            AccountId = req.AccountId,
            ExpectedVersion = req.ExpectedVersion,
            ExpectedAuthGeneration = expectedAuthGeneration,
            Status = "preparing",
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAt
        };

        lock (_gateLock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MicrosoftOAuthOperationManager));

            PruneExpiredAndOldOperationsUnderLock();

            // Check bounded capacity: max 32 including retained terminal ops
            if (_operations.Count >= MaxOperations)
            {
                throw new InvalidOperationException("Eşzamanlı Microsoft oturum açma işlem sınırı (32) aşıldı. Lütfen bekleyin.");
            }

            _operations[operationId] = op;
            op.Cts.Token.Register(() =>
            {
                lock (op.StateLock) HandleCancellationOrTimeoutUnderLock(op);
            });
            op.Cts.CancelAfter(MaxOperationLifetime);
            _ = Task.Run(() => ExecuteFlowAsync(op));
        }

        return new StartMicrosoftOAuthResponse
        {
            OperationId = op.OperationId,
            Status = op.Status,
            CreatedAtUtc = op.CreatedAtUtc,
            ExpiresAtUtc = op.ExpiresAtUtc
        };
    }

    public MicrosoftOAuthOperationStatusDto? GetOperation(string operationId, string companyId, string projectId)
    {
        if (!_operations.TryGetValue(operationId, out var op))
            return null;

        if (!_authorization.IsCaller(op.Actor,op.CompanyId,op.ProjectId)) return null;

        if (!string.Equals(op.CompanyId, companyId, StringComparison.Ordinal) ||
            !string.Equals(op.ProjectId, projectId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Müşteri veya proje kapsamı bu işlemle uyuşmuyor.");
        }

        lock (op.StateLock)
        {
            // Check timeout
            if (!op.IsTerminal && DateTimeOffset.UtcNow >= op.ExpiresAtUtc)
            {
                op.Status = "expired";
                op.AuthorizationUrl = null;
                op.ErrorMessage = "Oturum açma işlemi 5 dakikalık süre sınırını aştı.";
                try { op.Cts.Cancel(); } catch { }
    
            }

            return new MicrosoftOAuthOperationStatusDto
            {
                OperationId = op.OperationId,
                Status = op.Status,
                // authorizationUrl strictly returned ONLY while status is awaiting-signin
                AuthorizationUrl = op.Status == "awaiting-signin" ? op.AuthorizationUrl : null,
                AccountId = op.Status == "connected" ? op.ConnectedAccountId : null,
                Message = op.ErrorMessage,
                CreatedAtUtc = op.CreatedAtUtc,
                ExpiresAtUtc = op.ExpiresAtUtc
            };
        }
    }

    public CancelMicrosoftOAuthResponse? CancelAndGetResult(string operationId, string companyId, string projectId)
    {
        if (!CancelOperation(operationId, companyId, projectId)) return null;
        var operation = GetOperation(operationId, companyId, projectId);
        if (operation is null) return null;
        return new CancelMicrosoftOAuthResponse
        {
            OperationId = operationId,
            Status = operation.Status,
            AccountId = operation.AccountId,
            Message = operation.Status == "connected"
                ? "Bağlantı iptal isteğinden önce tamamlandı."
                : operation.Message ?? "İşlem sona erdi."
        };
    }

    public bool CancelOperation(string operationId, string companyId, string projectId)
    {
        if (!_operations.TryGetValue(operationId, out var op))
            return false;

        if (!_authorization.IsCaller(op.Actor,op.CompanyId,op.ProjectId)) return false;

        if (!string.Equals(op.CompanyId, companyId, StringComparison.Ordinal) ||
            !string.Equals(op.ProjectId, projectId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Müşteri veya proje kapsamı bu işlemle uyuşmuyor.");
        }

        lock (op.StateLock)
        {
            if (op.IsTerminal)
                return true;

            op.Status = "cancelled";
            op.AuthorizationUrl = null;
            op.ErrorMessage = "İşlem kullanıcı tarafından iptal edildi.";
            try { op.Cts.Cancel(); } catch { }

            return true;
        }
    }

    private async Task ExecuteFlowAsync(MicrosoftOAuthOperation op)
    {
        try
        {
            // 1. AcquireTokenInteractive via MSAL provider
            // OpenBrowserAsync captures authorization URL and transitions status to awaiting-signin
            var authResult = await _authProvider.AcquireTokenInteractiveAsync(
                op.TenantId,
                op.ClientId,
                op.RequestedEmail,
                capturedUri =>
                {
                    _authorization.DemandCurrent(op.Actor,op.CompanyId,op.ProjectId);
                    Microsoft365Policy.ValidateAuthorizationUri(capturedUri, op.TenantId, op.ClientId);
                    lock (op.StateLock)
                    {
                        if (op.IsTerminal || op.Cts.IsCancellationRequested) return;
                        op.AuthorizationUrl = capturedUri.ToString();
                        op.Status = "awaiting-signin";
                    }
                },
                op.Cts.Token);

            _authorization.DemandCurrent(op.Actor,op.CompanyId,op.ProjectId);
            // 2. Transition to verifying; remove authorizationUrl
            lock (op.StateLock)
            {
                if (op.IsTerminal || op.Cts.IsCancellationRequested || DateTimeOffset.UtcNow >= op.ExpiresAtUtc)
                {
                    HandleCancellationOrTimeoutUnderLock(op);
                    return;
                }
                op.AuthorizationUrl = null;
                op.Status = "verifying";
            }

            // 3. Identity validation via root Microsoft365Policy
            string? expectedHomeAccountId = null;
            if (!string.IsNullOrWhiteSpace(op.AccountId))
            {
                var (existingRecord, _) = _accountStore.GetAccountRecordAndCipher(op.AccountId, op.CompanyId, op.ProjectId);
                expectedHomeAccountId = existingRecord.HomeAccountId;
            }

            Microsoft365Policy.ValidateIdentity(
                op.RequestedEmail,
                op.TenantId,
                authResult.Username,
                authResult.TenantId,
                authResult.HomeAccountId,
                expectedHomeAccountId);

            // 4. Real IMAP OAuth2 authentication check before durable persistence
            await _connectionTester.VerifyConnectionAsync(
                Microsoft365Policy.ImapHost,
                Microsoft365Policy.ImapPort,
                "ssl",
                authResult.Username,
                authResult.AccessToken,
                op.Cts.Token);

            // 5. Durable atomic persistence under the same op.StateLock commit boundary
            lock (op.StateLock)
            {
                if (op.IsTerminal || op.Cts.IsCancellationRequested || DateTimeOffset.UtcNow >= op.ExpiresAtUtc)
                {
                    HandleCancellationOrTimeoutUnderLock(op);
                    return;
                }

                _authorization.DemandCurrent(op.Actor,op.CompanyId,op.ProjectId);
                string connectedAccountId;
                if (!string.IsNullOrWhiteSpace(op.AccountId))
                {
                    // Reconnect existing account
                    var updatedAccount = _accountStore.UpdateOAuthAccountOnReconnect(
                        op.AccountId,
                        op.CompanyId,
                        op.ProjectId,
                        op.ExpectedVersion!.Value,
                        authResult.HomeAccountId,
                        authResult.SerializedCache,
                        op.DisplayName,
                        op.ExpectedAuthGeneration);
                    connectedAccountId = updatedAccount.AccountId;
                }
                else
                {
                    // Create brand new account
                    var createdAccount = _accountStore.CreateOAuthAccount(
                        op.CompanyId,
                        op.ProjectId,
                        op.DisplayName,
                        authResult.Username,
                        op.TenantId,
                        op.ClientId,
                        authResult.HomeAccountId,
                        authResult.SerializedCache);
                    connectedAccountId = createdAccount.AccountId;
                }

                op.Status = "connected";
                op.ConnectedAccountId = connectedAccountId;
                op.AuthorizationUrl = null;
                op.ErrorMessage = null;
    
            }
        }
        catch (OperationCanceledException)
        {
            lock (op.StateLock)
            {
                HandleCancellationOrTimeoutUnderLock(op);
            }
        }
        catch (Exception ex)
        {
            lock (op.StateLock)
            {
                if (!op.IsTerminal)
                {
                    op.Status = "failed";
                    op.ConnectedAccountId = null;
                    op.AuthorizationUrl = null;
                    op.ErrorMessage = FormatSafeErrorMessage(ex);
                }
    
            }
        }
        finally
        {
            op.CompletionSource.TrySetResult(true);
        }
    }

    private static void HandleCancellationOrTimeoutUnderLock(MicrosoftOAuthOperation op)
    {
        if (!op.IsTerminal)
        {
            if (DateTimeOffset.UtcNow >= op.ExpiresAtUtc)
            {
                op.Status = "expired";
                op.ErrorMessage = "Oturum açma işlemi 5 dakikalık süre sınırını aştı.";
            }
            else
            {
                op.Status = "cancelled";
                op.ErrorMessage = "İşlem kullanıcı tarafından iptal edildi.";
            }
            op.AuthorizationUrl = null;
            op.ConnectedAccountId = null;
        }

    }

    private static string FormatSafeErrorMessage(Exception ex)
    {
        if (ex is MsalUiRequiredException)
        {
            return "Kullanıcı etkileşimi veya yönetici onayı gerekli. Lütfen tekrar deneyin.";
        }

        if (ex is AccountVersionConflictException)
        {
            return "Hesap başka bir işlem tarafından güncellenmiş.";
        }

        string msg = ex.Message;
        if (msg == "Giriş yapılan Microsoft hesabı, istenen posta kutusu veya kurumla eşleşmiyor." ||
            msg == "Microsoft giriş bağlantısı güvenli bağlantı kurallarını karşılamıyor." ||
            msg == "Microsoft kiracı ve uygulama kimlikleri geçerli GUID biçiminde olmalıdır." ||
            msg == "Yalnızca Microsoft 365 hesapları bu akışla yeniden bağlanabilir." ||
            msg == "Yeniden yetkilendirme kiracı veya uygulama kimliği mevcut hesapla uyuşmuyor." ||
            msg == "Yeniden yetkilendirme e-posta adresi mevcut hesapla uyuşmuyor." ||
            msg == "Yeniden bağlanacak hesap bulunamadı.")
        {
            return msg;
        }

        return "Microsoft 365 bağlantısı sırasında güvenli bir hata oluştu. Ayarları kontrol edip tekrar deneyin.";
    }

    private void PruneExpiredAndOldOperationsUnderLock()
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - CleanupRetentionTtl;
        foreach (var kvp in _operations)
        {
            if (kvp.Value.CompletionTask.IsCompleted && kvp.Value.IsTerminal && (kvp.Value.CreatedAtUtc < cutoff || DateTimeOffset.UtcNow >= kvp.Value.ExpiresAtUtc.Add(CleanupRetentionTtl)))
            {
                if (_operations.TryRemove(kvp.Key, out var removed))
                {
                    try { removed.Cts.Dispose(); } catch { }

                }
            }
        }
    }

    public void Dispose()
    {
        lock (_gateLock)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var op in _operations.Values)
            {
                try { op.Cts.Cancel(); } catch { }
                try { op.Cts.Dispose(); } catch { }

            }
            _operations.Clear();
        }
    }
}

