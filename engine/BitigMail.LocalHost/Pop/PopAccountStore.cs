using System.Text.Json;
using BitigMail.LocalHost.Security;

namespace BitigMail.LocalHost.Pop;

public sealed class PopAccountStore
{
    private readonly string _directory; private readonly IImapCredentialProtector _protector; private readonly bool _allowTestingLoopback;
    private readonly object _gate = new(); private readonly Dictionary<string, PopAccountEnvelope> _entries = new(StringComparer.Ordinal);
    public PopAccountStore(string directory, IImapCredentialProtector protector, bool allowTestingLoopback = false)
    { _directory = directory; _protector = protector; _allowTestingLoopback = allowTestingLoopback; Directory.CreateDirectory(directory); Load(); }
    public PopAccountPublicDto Create(CreatePopAccountRequest request)
    {
        ValidateScope(request.CompanyId, request.ProjectId); ValidateConnection(request.Host, request.Port, request.TlsMode, request.Username, request.Password, request.AllowUnencryptedConnection);
        if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.Email)) throw new ArgumentException("POP görünen adı ve e-posta zorunludur.");
        string id = "pop_" + Guid.NewGuid().ToString("N"); var now = DateTimeOffset.UtcNow;
        var account = new PopAccountPublicDto(id, request.CompanyId, request.ProjectId, request.DisplayName.Trim(), request.Email.Trim(), request.Host.Trim(), request.Port,
            request.TlsMode.Trim().ToLowerInvariant(), request.Username.Trim(), 1, now, now, request.TlsMode.Trim().ToLowerInvariant() == "none" && request.AllowUnencryptedConnection);
        var envelope = new PopAccountEnvelope { Account = account, ProtectedPasswordBase64 = Convert.ToBase64String(_protector.Protect(request.Password, id)) };
        lock (_gate) { WriteAtomic(envelope); _entries.Add(id, envelope); } return account;
    }
    public string StorageDirectory => _directory;
    public IReadOnlyList<PopAccountPublicDto> List(string companyId, string projectId)
    { ValidateScope(companyId, projectId); lock (_gate) return _entries.Values.Select(x => x.Account).Where(x => x.CompanyId == companyId && x.ProjectId == projectId).OrderBy(x => x.DisplayName, StringComparer.Ordinal).ToArray(); }
    public (PopAccountPublicDto Account, string Password) Resolve(string id, string companyId, string projectId)
    {
        ValidateId(id); ValidateScope(companyId, projectId); lock (_gate)
        {
            if (!_entries.TryGetValue(id, out var value)) throw new KeyNotFoundException("POP hesabı bulunamadı.");
            if (value.Account.CompanyId != companyId || value.Account.ProjectId != projectId) throw new InvalidOperationException("POP hesabı müşteri/proje kapsamıyla uyuşmuyor.");
            ValidateConnection(value.Account.Host, value.Account.Port, value.Account.TlsMode, value.Account.Username, "stored-credential", value.Account.AllowUnencryptedConnection);
            return (value.Account, _protector.Unprotect(Convert.FromBase64String(value.ProtectedPasswordBase64), id));
        }
    }
    private void Load()
    {
        foreach (string file in Directory.GetFiles(_directory, "pop_*.json"))
        {
            var envelope = JsonSerializer.Deserialize<PopAccountEnvelope>(File.ReadAllBytes(file));
            if (envelope is not null && IsValidId(envelope.Account.AccountId) && Path.GetFileNameWithoutExtension(file) == envelope.Account.AccountId) _entries[envelope.Account.AccountId] = envelope;
        }
    }
    private void WriteAtomic(PopAccountEnvelope envelope)
    {
        string final = Path.Combine(_directory, envelope.Account.AccountId + ".json"), temp = final + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { JsonSerializer.Serialize(fs, envelope, new JsonSerializerOptions { WriteIndented = true }); fs.Flush(true); }
        File.Move(temp, final, false);
    }
    private void ValidateConnection(string host, int port, string tlsMode, string username, string password, bool allowUnencryptedConnection = false)
    {
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password) || port is < 1 or > 65535) throw new ArgumentException("POP bağlantı alanları geçersiz.");
        string mode = tlsMode.Trim().ToLowerInvariant();
        if (mode is "ssl" or "starttls") return;
        if (mode == "none" && (allowUnencryptedConnection || (_allowTestingLoopback && host is "127.0.0.1" or "localhost"))) return;
        throw new ArgumentException("POP şifresiz bağlantısı için açık onay gereklidir.");
    }
    private static bool IsValidId(string? id) => id is { Length: 36 } && id.StartsWith("pop_", StringComparison.Ordinal) && id[4..].All(char.IsAsciiHexDigit);
    private static void ValidateId(string id) { if (!IsValidId(id)) throw new ArgumentException("Geçersiz POP hesap kimliği."); }
    private static void ValidateScope(string companyId, string projectId) { if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(projectId) || companyId.Any(char.IsControl) || projectId.Any(char.IsControl)) throw new ArgumentException("Müşteri ve proje kapsamı zorunludur."); }
}
