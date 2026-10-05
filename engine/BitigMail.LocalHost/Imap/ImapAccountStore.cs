using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BitigMail.Engine.Imap;
using BitigMail.LocalHost.Security;

namespace BitigMail.LocalHost.Imap;

/// <summary>
/// On-disk single-file atomic JSON envelope containing account metadata and DPAPI-protected password ciphertext.
/// Never exposed publicly or over API.
/// </summary>
internal sealed class ImapAccountEnvelope
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("account")]
    public required ImapAccountRecord Account { get; set; }

    [JsonPropertyName("protectedPasswordBase64")]
    public string? ProtectedPasswordBase64 { get; set; }

    [JsonPropertyName("protectedCredentialBase64")]
    public string? ProtectedCredentialBase64 { get; set; }
}

/// <summary>
/// Encrypted persistent storage for IMAP accounts.
/// Stored durably as a single atomic JSON envelope per account containing metadata plus DPAPI ciphertext.
/// Public projections never expose passwords, ciphers, secrets, or internal file paths.
/// </summary>
public sealed class ImapAccountStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly string _storageDir;
    private readonly IImapCredentialProtector _credentialProtector;
    private readonly ImapConnectionPolicy _connectionPolicy;
    private readonly ConcurrentDictionary<string, StoreEntry> _accounts = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    // Controlled hooks for fault-injection testing without timing dependencies
    internal Action<string>? OnBeforeAtomicWrite { get; set; }
    internal Action<string, string>? OnBeforeFileMove { get; set; }
    internal Action<string>? OnBeforeFileDelete { get; set; }

    private sealed class StoreEntry
    {
        public ImapAccountRecord Record { get; }
        public byte[] CipherBytes { get; }

        public StoreEntry(ImapAccountRecord record, byte[] cipherBytes)
        {
            Record = record;
            CipherBytes = cipherBytes;
        }
    }

    public ImapAccountStore(
        string storageDir,
        IImapCredentialProtector credentialProtector,
        ImapConnectionPolicy connectionPolicy)
    {
        _storageDir = storageDir ?? throw new ArgumentNullException(nameof(storageDir));
        _credentialProtector = credentialProtector ?? throw new ArgumentNullException(nameof(credentialProtector));
        _connectionPolicy = connectionPolicy ?? throw new ArgumentNullException(nameof(connectionPolicy));

        Directory.CreateDirectory(_storageDir);
        LoadExistingAccounts();
    }

    public string StorageDirectory => _storageDir;

    public static bool IsValidAccountId(string? accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId) ||
            accountId.Length != 36 ||
            !accountId.StartsWith("acc_", StringComparison.Ordinal))
            return false;

        for (int i = 4; i < accountId.Length; i++)
        {
            if (!char.IsAsciiHexDigit(accountId[i]))
                return false;
        }

        return !accountId.Contains('/') && !accountId.Contains('\\') && !accountId.Contains("..");
    }

    public static void ValidateAccountId(string? accountId)
    {
        if (!IsValidAccountId(accountId))
            throw new ArgumentException("Geçersiz hesap tanıtıcısı veya yol karakterleri içeriyor.");
    }

    public static void ValidateDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 256 || displayName.Any(char.IsControl))
            throw new ArgumentException("Görünen ad boş, 256 karakterden uzun veya kontrol karakterleri içeremez.");
    }

    public static void ValidateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320 || email.Any(char.IsControl))
            throw new ArgumentException("E-posta adresi boş, 320 karakterden uzun veya kontrol karakterleri içeremez.");

        int atIndex = email.IndexOf('@');
        if (atIndex <= 0 || atIndex != email.LastIndexOf('@') || atIndex >= email.Length - 1)
            throw new ArgumentException("E-posta adresi geçerli bir formatta olmalıdır.");

        if (email.Contains(' ') || email.Contains('\t') || email.Contains('\r') || email.Contains('\n'))
            throw new ArgumentException("E-posta adresi boşluk veya kontrol karakterleri içeremez.");
    }

    private void LoadExistingAccounts()
    {
        lock (_lock)
        {
            _accounts.Clear();
            var jsonFiles = Directory.GetFiles(_storageDir, "*.json");
            foreach (var jsonFile in jsonFiles)
            {
                string fileName = Path.GetFileName(jsonFile);
                if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    continue;

                string basename = Path.GetFileNameWithoutExtension(fileName);
                if (!IsValidAccountId(basename))
                {
                    // Reject non-compliant filenames, path traversal or foreign files
                    continue;
                }

                try
                {
                    string json = File.ReadAllText(jsonFile, Encoding.UTF8);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    string? base64 = null;
                    if (root.TryGetProperty("protectedCredentialBase64", out var credEl) && credEl.ValueKind == JsonValueKind.String)
                    {
                        base64 = credEl.GetString();
                    }
                    if (string.IsNullOrEmpty(base64) && root.TryGetProperty("protectedPasswordBase64", out var pwdEl) && pwdEl.ValueKind == JsonValueKind.String)
                    {
                        base64 = pwdEl.GetString();
                    }

                    if (root.TryGetProperty("account", out var accountEl) && !string.IsNullOrEmpty(base64))
                    {
                        var record = JsonSerializer.Deserialize<ImapAccountRecord>(accountEl.GetRawText());

                        if (record != null && string.Equals(record.AccountId, basename, StringComparison.Ordinal))
                        {
                            ValidateRecordIntegrity(record);
                            byte[] cipher = Convert.FromBase64String(base64);
                            _accounts[record.AccountId] = new StoreEntry(record, cipher);
                        }
                    }
                    else
                    {
                        // Controlled migration of legacy two-file artifacts: {accountId}.json + {accountId}.secret.bin
                        var legacyRecord = JsonSerializer.Deserialize<ImapAccountRecord>(json);
                        if (legacyRecord != null && string.Equals(legacyRecord.AccountId, basename, StringComparison.Ordinal))
                        {
                            string legacySecretFile = Path.Combine(_storageDir, $"{basename}.secret.bin");
                            if (File.Exists(legacySecretFile))
                            {
                                ValidateRecordIntegrity(legacyRecord);
                                byte[] cipher = File.ReadAllBytes(legacySecretFile);
                                MigrateLegacyRecord(legacyRecord, cipher, jsonFile, legacySecretFile);
                            }
                        }
                    }
                }
                catch
                {
                    // Corrupted or invalid individual files are safely skipped during startup
                }
            }
        }
    }

    private void ValidateRecordIntegrity(ImapAccountRecord record)
    {
        ValidateAccountId(record.AccountId);
        ImapConnectionPolicy.ValidateScope(record.CompanyId, record.ProjectId);
        ValidateDisplayName(record.DisplayName);
        ValidateEmail(record.Email);

        if (string.IsNullOrEmpty(record.AuthKind) || string.Equals(record.AuthKind, "password", StringComparison.OrdinalIgnoreCase))
        {
            _connectionPolicy.Validate(record.Host, record.Port, record.TlsMode, record.Username, "stored-credential", record.AllowUnencryptedConnection);
            return;
        }

        if (!string.Equals(record.Username, record.Email, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("OAuth posta kutusu ve kullanıcı adı eşleşmiyor.");

        if (string.Equals(record.AuthKind, Microsoft365Policy.AuthKind, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(record.Host, Microsoft365Policy.ImapHost, StringComparison.OrdinalIgnoreCase) ||
                record.Port != Microsoft365Policy.ImapPort ||
                !string.Equals(record.TlsMode, "ssl", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Microsoft 365 OAuth hesabının IMAP uç noktası geçersiz.");
            Microsoft365Policy.ValidateConfiguration(record.TenantId ?? string.Empty, record.ClientId ?? string.Empty);
            if (string.IsNullOrWhiteSpace(record.HomeAccountId))
                throw new InvalidDataException("Microsoft 365 OAuth hesap kimliği eksik.");
            return;
        }

        if (string.Equals(record.AuthKind, GoogleOAuthPolicy.AuthKind, StringComparison.OrdinalIgnoreCase))
        {
            if (!GoogleOAuthPolicy.IsAllowedEndpoint(record.Host, record.Port, record.TlsMode) ||
                string.IsNullOrWhiteSpace(record.ClientId) ||
                string.IsNullOrWhiteSpace(record.HomeAccountId))
                throw new InvalidDataException("Google OAuth hesap yapılandırması geçersiz.");
            return;
        }

        throw new InvalidDataException("Desteklenmeyen kimlik doğrulama türü.");
    }

    private void MigrateLegacyRecord(ImapAccountRecord record, byte[] cipher, string jsonPath, string legacySecretPath)
    {
        try
        {
            var envelope = new ImapAccountEnvelope
            {
                SchemaVersion = 1,
                Account = record,
                ProtectedPasswordBase64 = Convert.ToBase64String(cipher)
            };
            byte[] envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);

            // Write new envelope atomically
            AtomicWrite(jsonPath, envelopeBytes);

            // Only delete legacy secret file after atomic write succeeds
            try
            {
                File.Delete(legacySecretPath);
            }
            catch
            {
                // If deletion of legacy file fails, keep files safely
            }

            _accounts[record.AccountId] = new StoreEntry(record, cipher);
        }
        catch
        {
            // Migration failure preserves original files untouched
        }
    }

    public IReadOnlyList<ImapAccountPublicDto> ListAccounts(string companyId, string projectId)
    {
        lock (_lock)
        {
            ImapConnectionPolicy.ValidateScope(companyId, projectId);
            return _accounts.Values
                .Where(a => string.Equals(a.Record.CompanyId, companyId, StringComparison.Ordinal) &&
                            string.Equals(a.Record.ProjectId, projectId, StringComparison.Ordinal))
                .OrderBy(a => a.Record.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(a => a.Record.ToPublicDto())
                .ToList();
        }
    }

    public ImapAccountPublicDto? GetAccount(string accountId, string companyId, string projectId)
    {
        lock (_lock)
        {
            ValidateAccountId(accountId);
            ImapConnectionPolicy.ValidateScope(companyId, projectId);

            if (!_accounts.TryGetValue(accountId, out var entry))
                return null;

            if (!string.Equals(entry.Record.CompanyId, companyId, StringComparison.Ordinal) ||
                !string.Equals(entry.Record.ProjectId, projectId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Müşteri veya proje kapsamı bu hesapla uyuşmuyor.");
            }

            return entry.Record.ToPublicDto();
        }
    }

    public ImapAccountPublicDto CreateAccount(CreateImapAccountRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);

        // 1. Strict validation via ImapConnectionPolicy and bounded fields
        ImapConnectionPolicy.ValidateScope(req.CompanyId, req.ProjectId);
        _connectionPolicy.Validate(req.Host, req.Port, req.TlsMode, req.Username, req.Password, req.AllowUnencryptedConnection);
        ValidateDisplayName(req.DisplayName);
        ValidateEmail(req.Email);

        // 2. Generate safe account ID compliant with entropy requirements
        string accountId = $"acc_{Guid.NewGuid():N}";

        lock (_lock)
        {
            // 3. Encrypt password using DPAPI CurrentUser protector
            byte[] cipherBytes = _credentialProtector.Protect(req.Password, accountId);

            var record = new ImapAccountRecord
            {
                AccountId = accountId,
                CompanyId = req.CompanyId,
                ProjectId = req.ProjectId,
                DisplayName = req.DisplayName.Trim(),
                Email = req.Email.Trim(),
                Host = req.Host.Trim(),
                Port = req.Port,
                TlsMode = req.TlsMode.Trim().ToLowerInvariant(),
                AllowUnencryptedConnection = req.TlsMode == "none" && req.AllowUnencryptedConnection,
                Username = req.Username.Trim(),
                Version = 1,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            var envelope = new ImapAccountEnvelope
            {
                SchemaVersion = 1,
                Account = record,
                ProtectedPasswordBase64 = Convert.ToBase64String(cipherBytes)
            };

            // 4. Atomically persist envelope to single JSON file
            string metaPath = GetAccountFilePath(accountId);
            AtomicWrite(metaPath, JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions));

            // 5. Swap in-memory store only after durable success
            _accounts[accountId] = new StoreEntry(record, cipherBytes);
            return record.ToPublicDto();
        }
    }

    public ImapAccountPublicDto UpdateAccount(string accountId, UpdateImapAccountRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);

        ValidateAccountId(accountId);
        ImapConnectionPolicy.ValidateScope(req.CompanyId, req.ProjectId);

        lock (_lock)
        {
            if (!_accounts.TryGetValue(accountId, out var currentEntry))
                throw new KeyNotFoundException($"Hesap bulunamadı: {accountId}");

            if (!string.Equals(currentEntry.Record.CompanyId, req.CompanyId, StringComparison.Ordinal) ||
                !string.Equals(currentEntry.Record.ProjectId, req.ProjectId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Müşteri veya proje kapsamı bu hesapla uyuşmuyor.");
            }

            // Concurrency check: require expectedVersion
            if (!req.ExpectedVersion.HasValue)
            {
                throw new ArgumentException("expectedVersion parametresi zorunludur.", nameof(req.ExpectedVersion));
            }

            if (req.ExpectedVersion.Value != currentEntry.Record.Version)
            {
                throw new AccountVersionConflictException(currentEntry.Record.Version, req.ExpectedVersion.Value);
            }

            // OAuth accounts: only displayName can be updated; host/port/tls/username/email/password cannot be changed
            if (!string.IsNullOrEmpty(currentEntry.Record.AuthKind) &&
                !string.Equals(currentEntry.Record.AuthKind, "password", StringComparison.OrdinalIgnoreCase))
            {
                if ((req.Host != null && !string.Equals(req.Host.Trim(), currentEntry.Record.Host, StringComparison.OrdinalIgnoreCase)) ||
                    (req.Port.HasValue && req.Port.Value != currentEntry.Record.Port) ||
                    (req.TlsMode != null && !string.Equals(req.TlsMode.Trim(), currentEntry.Record.TlsMode, StringComparison.OrdinalIgnoreCase)) ||
                    (req.Username != null && !string.Equals(req.Username.Trim(), currentEntry.Record.Username, StringComparison.OrdinalIgnoreCase)) ||
                    (req.Email != null && !string.Equals(req.Email.Trim(), currentEntry.Record.Email, StringComparison.OrdinalIgnoreCase)) ||
                    !string.IsNullOrWhiteSpace(req.Password))
                {
                    throw new InvalidOperationException("OAuth hesaplarında sunucu, port, TLS, kullanıcı adı, e-posta veya parola güncellenemez. Yalnızca görünen ad güncellenebilir.");
                }

                string oAuthDisplayName = req.DisplayName != null ? req.DisplayName.Trim() : currentEntry.Record.DisplayName;
                ValidateDisplayName(oAuthDisplayName);

                var oAuthRecord = currentEntry.Record.Clone();
                oAuthRecord.DisplayName = oAuthDisplayName;
                oAuthRecord.Version = currentEntry.Record.Version + 1;
                oAuthRecord.UpdatedAtUtc = DateTimeOffset.UtcNow;

                var oAuthEnvelope = new ImapAccountEnvelope
                {
                    SchemaVersion = 1,
                    Account = oAuthRecord,
                    ProtectedPasswordBase64 = Convert.ToBase64String(currentEntry.CipherBytes),
                    ProtectedCredentialBase64 = Convert.ToBase64String(currentEntry.CipherBytes)
                };

                string oAuthMetaPath = GetAccountFilePath(accountId);
                AtomicWrite(oAuthMetaPath, JsonSerializer.SerializeToUtf8Bytes(oAuthEnvelope, JsonOptions));

                _accounts[accountId] = new StoreEntry(oAuthRecord, currentEntry.CipherBytes);
                return oAuthRecord.ToPublicDto();
            }

            string newHost = req.Host != null ? req.Host.Trim() : currentEntry.Record.Host;
            int newPort = req.Port ?? currentEntry.Record.Port;
            string newTlsMode = req.TlsMode != null ? req.TlsMode.Trim().ToLowerInvariant() : currentEntry.Record.TlsMode;
            string newUsername = req.Username != null ? req.Username.Trim() : currentEntry.Record.Username;
            string newDisplayName = req.DisplayName != null ? req.DisplayName.Trim() : currentEntry.Record.DisplayName;
            string newEmail = req.Email != null ? req.Email.Trim() : currentEntry.Record.Email;

            ValidateDisplayName(newDisplayName);
            ValidateEmail(newEmail);

            // Password replacement: if provided, validate and protect new password; otherwise keep existing cipher
            string effectivePassword;
            byte[] effectiveCipherBytes;
            if (!string.IsNullOrWhiteSpace(req.Password))
            {
                effectivePassword = req.Password;
                effectiveCipherBytes = _credentialProtector.Protect(effectivePassword, accountId);
            }
            else
            {
                effectiveCipherBytes = currentEntry.CipherBytes;
                effectivePassword = _credentialProtector.Unprotect(effectiveCipherBytes, accountId);
            }

            bool endpointUnchanged = newHost == currentEntry.Record.Host && newPort == currentEntry.Record.Port && newTlsMode == currentEntry.Record.TlsMode;
            bool allowUnencrypted = newTlsMode == "none" && (req.AllowUnencryptedConnection ?? (endpointUnchanged && currentEntry.Record.AllowUnencryptedConnection));

            // Validate full updated configuration against policy
            _connectionPolicy.Validate(newHost, newPort, newTlsMode, newUsername, effectivePassword, allowUnencrypted);

            // Build brand new immutable record (NEVER mutate currentEntry.Record before durable persistence)
            var newRecord = new ImapAccountRecord
            {
                AccountId = accountId,
                CompanyId = currentEntry.Record.CompanyId,
                ProjectId = currentEntry.Record.ProjectId,
                DisplayName = newDisplayName,
                Email = newEmail,
                Host = newHost,
                Port = newPort,
                TlsMode = newTlsMode,
                AllowUnencryptedConnection = allowUnencrypted,
                Username = newUsername,
                AuthKind = currentEntry.Record.AuthKind ?? "password",
                TenantId = currentEntry.Record.TenantId,
                ClientId = currentEntry.Record.ClientId,
                HomeAccountId = currentEntry.Record.HomeAccountId,
                Version = currentEntry.Record.Version + 1,
                CreatedAtUtc = currentEntry.Record.CreatedAtUtc,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            var newEnvelope = new ImapAccountEnvelope
            {
                SchemaVersion = 1,
                Account = newRecord,
                ProtectedPasswordBase64 = Convert.ToBase64String(effectiveCipherBytes),
                ProtectedCredentialBase64 = Convert.ToBase64String(effectiveCipherBytes)
            };

            // Atomically write new envelope to disk
            string metaPath = GetAccountFilePath(accountId);
            AtomicWrite(metaPath, JsonSerializer.SerializeToUtf8Bytes(newEnvelope, JsonOptions));

            // Swap in-memory store only after durable write succeeds
            _accounts[accountId] = new StoreEntry(newRecord, effectiveCipherBytes);
            return newRecord.ToPublicDto();
        }
    }

    public bool DeleteAccount(string accountId, string companyId, string projectId, long? expectedVersion = null)
    {
        ValidateAccountId(accountId);
        ImapConnectionPolicy.ValidateScope(companyId, projectId);

        lock (_lock)
        {
            if (!_accounts.TryGetValue(accountId, out var currentEntry))
                return false;

            if (!string.Equals(currentEntry.Record.CompanyId, companyId, StringComparison.Ordinal) ||
                !string.Equals(currentEntry.Record.ProjectId, projectId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Müşteri veya proje kapsamı bu hesapla uyuşmuyor.");
            }

            if (expectedVersion.HasValue && expectedVersion.Value != currentEntry.Record.Version)
            {
                throw new AccountVersionConflictException(currentEntry.Record.Version, expectedVersion.Value);
            }

            string metaPath = GetAccountFilePath(accountId);
            string legacySecretPath = Path.Combine(_storageDir, $"{accountId}.secret.bin");

            OnBeforeFileDelete?.Invoke(metaPath);

            // Durable deletion of files must succeed before removing from memory.
            // Do NOT swallow filesystem errors.
            if (File.Exists(metaPath))
            {
                try
                {
                    File.Delete(metaPath);
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new IOException($"Hesap dosyası silinemedi veya kullanımda: {metaPath}", ex);
                }
            }

            if (File.Exists(legacySecretPath))
            {
                try
                {
                    File.Delete(legacySecretPath);
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new IOException($"Artık parola dosyası silinemedi: {legacySecretPath}", ex);
                }
            }

            // Only remove from memory after durable deletion succeeds on disk
            _accounts.TryRemove(accountId, out _);
            return true;
        }
    }

    /// <summary>
    /// Internal retrieval of account record snapshot with unprotected password for connection/folder operations.
    /// Strictly internal; never exposed to public endpoints or wire DTOs.
    /// Rejects OAuth accounts to enforce typed async credential resolver usage.
    /// </summary>
    public (ImapAccountRecord Record, string Password) GetInternalAccountWithPassword(string accountId, string companyId, string projectId)
    {
        lock (_lock)
        {
            ValidateAccountId(accountId);
            ImapConnectionPolicy.ValidateScope(companyId, projectId);

            if (!_accounts.TryGetValue(accountId, out var entry))
                throw new KeyNotFoundException($"Hesap bulunamadı: {accountId}");

            if (!string.Equals(entry.Record.CompanyId, companyId, StringComparison.Ordinal) ||
                !string.Equals(entry.Record.ProjectId, projectId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Müşteri veya proje kapsamı bu hesapla uyuşmuyor.");
            }

            if (!string.IsNullOrEmpty(entry.Record.AuthKind) && !string.Equals(entry.Record.AuthKind, "password", StringComparison.OrdinalIgnoreCase))
            {
                string provider = string.Equals(entry.Record.AuthKind, Microsoft365Policy.AuthKind, StringComparison.OrdinalIgnoreCase) ? "Microsoft 365" : "OAuth";
                throw new InvalidOperationException($"{provider} hesabı parola kimlik doğrulaması kullanamaz. Scoped credential resolver kullanın.");
            }

            string password = _credentialProtector.Unprotect(entry.CipherBytes, accountId);
            return (entry.Record.Clone(), password);
        }
    }

    /// <summary>
    /// Internal retrieval of account record snapshot and cipher bytes for scoped credential resolver.
    /// </summary>
    public (ImapAccountRecord Record, byte[] CipherBytes) GetAccountRecordAndCipher(string accountId, string companyId, string projectId)
    {
        lock (_lock)
        {
            ValidateAccountId(accountId);
            ImapConnectionPolicy.ValidateScope(companyId, projectId);

            if (!_accounts.TryGetValue(accountId, out var entry))
                throw new KeyNotFoundException($"Hesap bulunamadı: {accountId}");

            if (!string.Equals(entry.Record.CompanyId, companyId, StringComparison.Ordinal) ||
                !string.Equals(entry.Record.ProjectId, projectId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Müşteri veya proje kapsamı bu hesapla uyuşmuyor.");
            }

            return (entry.Record.Clone(), entry.CipherBytes.ToArray());
        }
    }

    /// <summary>
    /// Durably creates a new Microsoft 365 OAuth account and stores serialized token cache in DPAPI envelope.
    /// </summary>
    public ImapAccountPublicDto CreateOAuthAccount(
        string companyId,
        string projectId,
        string displayName,
        string email,
        string tenantId,
        string clientId,
        string homeAccountId,
        byte[] serializedCache,
        string authKind = Microsoft365Policy.AuthKind)
    {
        ImapConnectionPolicy.ValidateScope(companyId, projectId);
        if (string.Equals(authKind, Microsoft365Policy.AuthKind, StringComparison.OrdinalIgnoreCase))
            Microsoft365Policy.ValidateConfiguration(tenantId, clientId);
        else if (!string.Equals(authKind, GoogleOAuthPolicy.AuthKind, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Desteklenmeyen OAuth kimlik doğrulama türü.");
        ValidateDisplayName(displayName);
        ValidateEmail(email);

        if (string.IsNullOrWhiteSpace(homeAccountId))
            throw new ArgumentException("HomeAccountId boş olamaz.", nameof(homeAccountId));
        if (serializedCache == null || serializedCache.Length == 0)
            throw new ArgumentException("Serialized token cache boş olamaz.", nameof(serializedCache));

        string accountId = $"acc_{Guid.NewGuid():N}";

        lock (_lock)
        {
            byte[] cipherBytes = _credentialProtector.Protect(Convert.ToBase64String(serializedCache), accountId);

            var record = new ImapAccountRecord
            {
                AccountId = accountId,
                CompanyId = companyId,
                ProjectId = projectId,
                DisplayName = displayName.Trim(),
                Email = email.Trim(),
                Host = string.Equals(authKind, GoogleOAuthPolicy.AuthKind, StringComparison.OrdinalIgnoreCase) ? GoogleOAuthPolicy.ImapHost : Microsoft365Policy.ImapHost,
                Port = string.Equals(authKind, GoogleOAuthPolicy.AuthKind, StringComparison.OrdinalIgnoreCase) ? GoogleOAuthPolicy.ImapPort : Microsoft365Policy.ImapPort,
                TlsMode = "ssl",
                Username = email.Trim(),
                AuthKind = authKind,
                TenantId = tenantId.Trim(),
                ClientId = clientId.Trim(),
                HomeAccountId = homeAccountId.Trim(),
                Version = 1,
                AuthGeneration = 1,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            var envelope = new ImapAccountEnvelope
            {
                SchemaVersion = 1,
                Account = record,
                ProtectedPasswordBase64 = Convert.ToBase64String(cipherBytes),
                ProtectedCredentialBase64 = Convert.ToBase64String(cipherBytes)
            };

            string metaPath = GetAccountFilePath(accountId);
            AtomicWrite(metaPath, JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions));

            _accounts[accountId] = new StoreEntry(record, cipherBytes);
            return record.ToPublicDto();
        }
    }

    /// <summary>
    /// Durably updates an existing Microsoft 365 OAuth account upon reconnection.
    /// Refreshes auth secret and bumps auth generation without bumping metadata Version so frozen transfer resume remains viable.
    /// </summary>
    public ImapAccountPublicDto UpdateOAuthAccountOnReconnect(
        string accountId,
        string companyId,
        string projectId,
        long expectedVersion,
        string homeAccountId,
        byte[] serializedCache,
        string? displayName = null,
        long? expectedAuthGeneration = null)
    {
        ValidateAccountId(accountId);
        ImapConnectionPolicy.ValidateScope(companyId, projectId);

        lock (_lock)
        {
            if (!_accounts.TryGetValue(accountId, out var currentEntry))
                throw new KeyNotFoundException($"Hesap bulunamadı: {accountId}");

            if (!string.Equals(currentEntry.Record.CompanyId, companyId, StringComparison.Ordinal) ||
                !string.Equals(currentEntry.Record.ProjectId, projectId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Müşteri veya proje kapsamı bu hesapla uyuşmuyor.");
            }

            if (currentEntry.Record.Version != expectedVersion)
            {
                throw new AccountVersionConflictException(currentEntry.Record.Version, expectedVersion);
            }

            if (string.IsNullOrEmpty(currentEntry.Record.AuthKind) ||
                string.Equals(currentEntry.Record.AuthKind, "password", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Yalnızca OAuth hesapları bu akışla yeniden bağlanabilir.");
            }

            if (!string.Equals(currentEntry.Record.HomeAccountId, homeAccountId, StringComparison.Ordinal) ||
                (expectedAuthGeneration.HasValue && currentEntry.Record.AuthGeneration != expectedAuthGeneration.Value))
                throw new InvalidOperationException("Microsoft hesap oturumu başka bir işlem tarafından değiştirilmiş.");
            if (displayName is not null && !string.Equals(displayName.Trim(), currentEntry.Record.DisplayName, StringComparison.Ordinal))
                throw new InvalidOperationException("Hesap adını yeniden bağlama işleminden önce hesap düzenleme ekranında değiştirin.");
            if (serializedCache is null || serializedCache.Length == 0)
                throw new ArgumentException("Microsoft oturum önbelleği boş olamaz.");
            byte[] cipherBytes = _credentialProtector.Protect(Convert.ToBase64String(serializedCache), accountId);

            var newRecord = currentEntry.Record.Clone();
            newRecord.Version = currentEntry.Record.Version; // Keep version for frozen transfer resume
            newRecord.AuthGeneration = currentEntry.Record.AuthGeneration + 1; // Bump auth generation
            newRecord.UpdatedAtUtc = DateTimeOffset.UtcNow;

            var envelope = new ImapAccountEnvelope
            {
                SchemaVersion = 1,
                Account = newRecord,
                ProtectedPasswordBase64 = Convert.ToBase64String(cipherBytes),
                ProtectedCredentialBase64 = Convert.ToBase64String(cipherBytes)
            };

            string metaPath = GetAccountFilePath(accountId);
            AtomicWrite(metaPath, JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions));

            _accounts[accountId] = new StoreEntry(newRecord, cipherBytes);
            return newRecord.ToPublicDto();
        }
    }

    /// <summary>
    /// Atomically commits refreshed OAuth token cache without incrementing account metadata version.
    /// If the account was deleted, reconnected, or changed concurrently, does not resurrect or overwrite.
    /// </summary>
    public bool SaveRefreshedOAuthCache(
        string accountId,
        string companyId,
        string projectId,
        long expectedVersion,
        long expectedAuthGeneration,
        string expectedHomeAccountId,
        byte[] updatedCache)
    {
        ValidateAccountId(accountId);
        ImapConnectionPolicy.ValidateScope(companyId, projectId);

        lock (_lock)
        {
            if (!_accounts.TryGetValue(accountId, out var currentEntry))
                return false; // Deleted, cannot resurrect!

            if (!string.Equals(currentEntry.Record.CompanyId, companyId, StringComparison.Ordinal) ||
                !string.Equals(currentEntry.Record.ProjectId, projectId, StringComparison.Ordinal))
                return false;

            if (currentEntry.Record.Version != expectedVersion)
                return false; // Version changed concurrently, do not overwrite!

            if (currentEntry.Record.AuthGeneration != expectedAuthGeneration)
                return false; // Reconnected or auth generation bumped concurrently, do not overwrite!

            if (!string.Equals(currentEntry.Record.HomeAccountId, expectedHomeAccountId, StringComparison.Ordinal))
                return false;

            byte[] newCipher = _credentialProtector.Protect(Convert.ToBase64String(updatedCache), accountId);

            var refreshedRecord = currentEntry.Record.Clone();
            refreshedRecord.AuthGeneration = checked(refreshedRecord.AuthGeneration + 1);
            // Keep metadata version unchanged; secret replacements advance their own generation.
            var envelope = new ImapAccountEnvelope
            {
                SchemaVersion = 1,
                Account = refreshedRecord,
                ProtectedPasswordBase64 = Convert.ToBase64String(newCipher),
                ProtectedCredentialBase64 = Convert.ToBase64String(newCipher)
            };

            string metaPath = GetAccountFilePath(accountId);
            AtomicWrite(metaPath, JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions));

            _accounts[accountId] = new StoreEntry(refreshedRecord, newCipher);
            return true;
        }
    }

    public string GetAccountFilePath(string accountId) => Path.Combine(_storageDir, $"{accountId}.json");

    private void AtomicWrite(string targetPath, byte[] data)
    {
        OnBeforeAtomicWrite?.Invoke(targetPath);

        string dir = Path.GetDirectoryName(targetPath) ?? _storageDir;
        string tmpPath = Path.Combine(dir, $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush(true);
            }

            OnBeforeFileMove?.Invoke(tmpPath, targetPath);

            try
            {
                File.Move(tmpPath, targetPath, overwrite: true);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException($"Hedef dosyaya yazılamadı veya dosya kullanımda: {targetPath}", ex);
            }
        }
        catch
        {
            try
            {
                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);
            }
            catch { }
            throw;
        }
    }
}
