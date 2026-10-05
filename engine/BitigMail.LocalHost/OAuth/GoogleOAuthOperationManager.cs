using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using BitigMail.Engine.Imap;
using BitigMail.Engine.Imap.OAuth;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.Security;

namespace BitigMail.LocalHost.OAuth;

internal sealed class GoogleOAuthOperation
{
    public AuthenticatedSessionPrincipal? Actor { get; init; }
    public required string OperationId { get; init; }
    public required string CompanyId { get; init; }
    public required string ProjectId { get; init; }
    public required string DisplayName { get; init; }
    public required string Email { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; set; }
    public string? AccountId { get; init; }
    public long? ExpectedVersion { get; init; }
    public long? ExpectedAuthGeneration { get; init; }
    public string? ExpectedSubject { get; init; }
    public string Status { get; set; } = "preparing";
    public string? AuthorizationUrl { get; set; }
    public string? ConnectedAccountId { get; set; }
    public ProviderDiagnosticDto? Diagnostic { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public CancellationTokenSource Cts { get; } = new();
    public object Gate { get; } = new();
    public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Terminal => Status is "connected" or "cancelled" or "expired" or "failed";
}

public sealed class GoogleOAuthOperationManager : IDisposable
{
    private readonly EphemeralOperationAuthorization _authorization;
    private const int MaxOperations = 32;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, GoogleOAuthOperation> _operations = new(StringComparer.Ordinal);
    private readonly ImapAccountStore _store;
    private readonly IGoogleAuthProvider _provider;
    private readonly IImapOAuthConnectionTester _tester;
    private bool _disposed;
    private readonly object _gate = new();

    public GoogleOAuthOperationManager(ImapAccountStore store, IGoogleAuthProvider provider, IImapOAuthConnectionTester tester, IHttpContextAccessor? http=null, IdentityCatalog? identities=null, AuthenticatedSessionRegistry? sessions=null, SecurityConfig? config=null)
    { _store = store; _provider = provider; _tester = tester; _authorization=new(http,identities,sessions,config); }

    public Task WaitForCompletionAsync(string id) => _operations.TryGetValue(id, out var op) && _authorization.IsCaller(op.Actor,op.CompanyId,op.ProjectId) ? op.Completion.Task : throw new KeyNotFoundException("İşlem bulunamadı.");

    public StartGoogleOAuthResponse StartOperation(StartGoogleOAuthRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = _authorization.Capture(request.CompanyId,request.ProjectId);
        ImapConnectionPolicy.ValidateScope(request.CompanyId, request.ProjectId);
        ImapAccountStore.ValidateDisplayName(request.DisplayName);
        ImapAccountStore.ValidateEmail(request.Email);
        GoogleOAuthPolicy.ValidateClient(request.ClientId, request.ClientSecret);
        long? generation = null;
        string? subject = null;
        if (!string.IsNullOrWhiteSpace(request.AccountId))
        {
            if (!request.ExpectedVersion.HasValue) throw new ArgumentException("Yeniden bağlama için expectedVersion zorunludur.");
            var existing = _store.GetAccount(request.AccountId, request.CompanyId, request.ProjectId) ?? throw new KeyNotFoundException("Yeniden bağlanacak hesap bulunamadı.");
            if (existing.Version != request.ExpectedVersion) throw new AccountVersionConflictException(existing.Version, request.ExpectedVersion);
            if (existing.AuthKind != GoogleOAuthPolicy.AuthKind || existing.ClientId != request.ClientId ||
                !string.Equals(existing.Email, request.Email, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Google yeniden bağlama kimliği mevcut hesapla uyuşmuyor.");
            var (record, _) = _store.GetAccountRecordAndCipher(request.AccountId, request.CompanyId, request.ProjectId);
            generation = record.AuthGeneration;
            subject = record.HomeAccountId;
        }
        var now = DateTimeOffset.UtcNow;
        var op = new GoogleOAuthOperation
        {
            Actor = actor,
            OperationId = "gop_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            CompanyId = request.CompanyId, ProjectId = request.ProjectId, DisplayName = request.DisplayName.Trim(),
            Email = request.Email.Trim(), ClientId = request.ClientId.Trim(), ClientSecret = request.ClientSecret,
            AccountId = request.AccountId, ExpectedVersion = request.ExpectedVersion, ExpectedAuthGeneration = generation,
            ExpectedSubject = subject, CreatedAtUtc = now, ExpiresAtUtc = now.Add(Lifetime)
        };
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GoogleOAuthOperationManager));
            Prune();
            if (_operations.Count >= MaxOperations) throw new InvalidOperationException("Eşzamanlı Google oturum açma işlem sınırı (32) aşıldı.");
            _operations[op.OperationId] = op;
            op.Cts.CancelAfter(Lifetime);
            _ = Task.Run(() => RunAsync(op));
        }
        return new() { OperationId = op.OperationId, Status = op.Status, CreatedAtUtc = now, ExpiresAtUtc = op.ExpiresAtUtc };
    }

    public GoogleOAuthOperationStatusDto? GetOperation(string id, string companyId, string projectId)
    {
        if (!_operations.TryGetValue(id, out var op)) return null;
        if (!_authorization.IsCaller(op.Actor,op.CompanyId,op.ProjectId)) return null;
        Scope(op, companyId, projectId);
        lock (op.Gate)
        {
            if (!op.Terminal && DateTimeOffset.UtcNow >= op.ExpiresAtUtc) End(op, "expired", "timeout", "timeout", true, "Google oturum açma süresi doldu.");
            return new() { OperationId = id, Status = op.Status, AuthorizationUrl = op.Status == "awaiting-signin" ? op.AuthorizationUrl : null,
                AccountId = op.Status == "connected" ? op.ConnectedAccountId : null, Message = op.Diagnostic?.Message,
                Diagnostic = op.Diagnostic, CreatedAtUtc = op.CreatedAtUtc, ExpiresAtUtc = op.ExpiresAtUtc };
        }
    }

    public CancelMicrosoftOAuthResponse? Cancel(string id, string companyId, string projectId)
    {
        if (!_operations.TryGetValue(id, out var op)) return null;
        if (!_authorization.IsCaller(op.Actor,op.CompanyId,op.ProjectId)) return null;
        Scope(op, companyId, projectId);
        lock (op.Gate)
        {
            if (!op.Terminal) End(op, "cancelled", "cancelled", "authorization", false, "İşlem kullanıcı tarafından iptal edildi.");
            return new() { OperationId = id, Status = op.Status, AccountId = op.ConnectedAccountId,
                Message = op.Status == "connected" ? "Bağlantı iptal isteğinden önce tamamlandı." : op.Diagnostic?.Message ?? "İşlem sona erdi." };
        }
    }

    private async Task RunAsync(GoogleOAuthOperation op)
    {
        try
        {
            var auth = await _provider.AcquireTokenInteractiveAsync(op.ClientId, op.ClientSecret, op.Email, uri =>
            {
                _authorization.DemandCurrent(op.Actor,op.CompanyId,op.ProjectId);
                GoogleOAuthPolicy.ValidateAuthorizationUri(uri, op.ClientId);
                lock (op.Gate) if (!op.Terminal) { op.AuthorizationUrl = uri.ToString(); op.Status = "awaiting-signin"; }
            }, op.Cts.Token);
            _authorization.DemandCurrent(op.Actor,op.CompanyId,op.ProjectId);
            lock (op.Gate) { if (op.Terminal) return; op.AuthorizationUrl = null; op.Status = "verifying"; }
            GoogleOAuthPolicy.ValidateIdentity(op.Email, op.ClientId, auth.Email, auth.EmailVerified, auth.Subject, op.ExpectedSubject);
            await _tester.VerifyConnectionAsync(GoogleOAuthPolicy.ImapHost, GoogleOAuthPolicy.ImapPort, "ssl", auth.Email, auth.AccessToken, op.Cts.Token);
            lock (op.Gate)
            {
                if (op.Terminal) return;
                if (op.Cts.IsCancellationRequested || DateTimeOffset.UtcNow >= op.ExpiresAtUtc)
                { End(op, DateTimeOffset.UtcNow >= op.ExpiresAtUtc ? "expired" : "cancelled", "cancelled", "authorization", false, "Google işlemi sona erdi."); return; }
                _authorization.DemandCurrent(op.Actor,op.CompanyId,op.ProjectId);
                var account = op.AccountId is null
                    ? _store.CreateOAuthAccount(op.CompanyId, op.ProjectId, op.DisplayName, auth.Email, string.Empty, op.ClientId, auth.Subject, auth.SerializedCache, GoogleOAuthPolicy.AuthKind)
                    : _store.UpdateOAuthAccountOnReconnect(op.AccountId, op.CompanyId, op.ProjectId, op.ExpectedVersion!.Value, auth.Subject, auth.SerializedCache, op.DisplayName, op.ExpectedAuthGeneration);
                op.ConnectedAccountId = account.AccountId; op.Status = "connected"; op.Diagnostic = null;
            }
        }
        catch (OperationCanceledException) { lock (op.Gate) if (!op.Terminal) End(op, DateTimeOffset.UtcNow >= op.ExpiresAtUtc ? "expired" : "cancelled", "cancelled", "authorization", false, "Google işlemi sona erdi."); }
        catch (Exception ex) { lock (op.Gate) if (!op.Terminal) { op.Status = "failed"; op.AuthorizationUrl = null; op.ConnectedAccountId = null; op.Diagnostic = Classify(ex); } }
        finally
        {
            lock (op.Gate)
            {
                op.ClientSecret = string.Empty;
                if (!op.Terminal) End(op, "cancelled", "cancelled", "authorization", false, "Google işlemi sona erdi.");
            }
            op.Completion.TrySetResult();
        }
    }

    private static string Safe(Exception ex) => ex switch
    {
        AccountVersionConflictException => "Hesap başka bir işlem tarafından güncellenmiş.",
        _ when ex.Message is "Giriş yapılan Google hesabı istenen posta kutusuyla eşleşmiyor." or "Google giriş bağlantısı güvenli bağlantı kurallarını karşılamıyor." => ex.Message,
        _ => "Google bağlantısı sırasında güvenli bir hata oluştu. Ayarları kontrol edip tekrar deneyin."
    };
    private static ProviderDiagnosticDto Classify(Exception ex) => ex switch
    {
        ImapOAuthConnectionException connection => connection.Diagnostic,
        GoogleReauthorizationRequiredException => Diagnostic("reauthorization_required", "authorization", false, "Google hesabı yeniden yetkilendirilmelidir."),
        TimeoutException => Diagnostic("network_timeout", "network", true, "Google IMAP bağlantısı zaman aşımına uğradı."),
        System.Net.Sockets.SocketException => Diagnostic("network_unavailable", "network", true, "Google IMAP sunucusuna ulaşılamadı."),
        MailKit.Security.SslHandshakeException => Diagnostic("tls_validation_failed", "tls", false, "Google IMAP TLS doğrulaması başarısız oldu."),
        MailKit.Security.AuthenticationException => Diagnostic("authentication_rejected", "authentication", false, "Google IMAP kimlik doğrulaması reddedildi."),
        Google.Apis.Auth.OAuth2.Responses.TokenResponseException token when token.Error?.Error is "temporarily_unavailable" or "slow_down" => Diagnostic("provider_throttled", "quota", true, "Google sağlayıcısı geçici olarak isteği sınırladı."),
        AccountVersionConflictException => Diagnostic("account_version_conflict", "concurrency", false, "Hesap başka bir işlem tarafından güncellenmiş."),
        _ => Diagnostic("google_connection_failed", "provider", false, Safe(ex))
    };
    private static ProviderDiagnosticDto Diagnostic(string code, string category, bool retryable, string message) =>
        new() { Code = code, Category = category, Retryable = retryable, Message = message };
    private static void Scope(GoogleOAuthOperation op, string companyId, string projectId)
    { if (op.CompanyId != companyId || op.ProjectId != projectId) throw new InvalidOperationException("Müşteri veya proje kapsamı bu işlemle uyuşmuyor."); }
    private static void End(GoogleOAuthOperation op, string status, string code, string category, bool retryable, string message)
    { op.Status = status; op.AuthorizationUrl = null; op.ConnectedAccountId = null; op.Diagnostic = new() { Code = code, Category = category, Retryable = retryable, Message = message }; try { op.Cts.Cancel(); } catch { } }
    private void Prune() { foreach (var pair in _operations) if (pair.Value.Terminal && pair.Value.CreatedAtUtc < DateTimeOffset.UtcNow.AddMinutes(-10)) _operations.TryRemove(pair.Key, out _); }
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var op in _operations.Values)
            {
                lock (op.Gate) if (!op.Terminal) End(op, "cancelled", "cancelled", "authorization", false, "Google işlemi sona erdi.");
            }
            _operations.Clear();
        }
    }
}
