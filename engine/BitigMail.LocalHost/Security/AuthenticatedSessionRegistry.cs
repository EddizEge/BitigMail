using System.Security.Cryptography;
using System.Text;

namespace BitigMail.LocalHost.Security;

public sealed record AuthenticatedSessionPrincipal(string UserId, string SessionId, long SecurityVersion);
public sealed record IssuedAuthenticatedSession(string Token, AuthenticatedSessionPrincipal Principal)
{
    public override string ToString() => "IssuedAuthenticatedSession { Token = [REDACTED] }";
}

/// <summary>Memory-only session lifecycle. Caller must authenticate credentials before issuing a session.</summary>
public sealed class AuthenticatedSessionRegistry
{
    private static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(8);
    private static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(30);
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _sessions = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private readonly int _maximumSessions;
    private sealed class Entry(AuthenticatedSessionPrincipal principal, long now)
    {
        public AuthenticatedSessionPrincipal Principal { get; } = principal;
        public long Created { get; } = now;
        public long LastUsed { get; set; } = now;
    }

    public AuthenticatedSessionRegistry(TimeProvider? timeProvider = null, int maximumSessions = 10_000)
    {
        if (maximumSessions is < 1 or > 100_000) throw new ArgumentOutOfRangeException(nameof(maximumSessions));
        _time = timeProvider ?? TimeProvider.System; _maximumSessions = maximumSessions;
    }

    public IssuedAuthenticatedSession Issue(string authenticatedUserId, long securityVersion)
    {
        if (string.IsNullOrWhiteSpace(authenticatedUserId) || authenticatedUserId.Length > 128 || securityVersion < 1)
            throw new ArgumentException("Oturum kullanıcısı geçersiz.");
        lock (_gate)
        {
            long now = _time.GetTimestamp();
            foreach (string key in _sessions.Where(pair => IsExpired(pair.Value, now)).Select(pair => pair.Key).ToArray())
                _sessions.Remove(key);
            if (_sessions.Count >= _maximumSessions || _sessions.Values.Count(e => e.Principal.UserId == authenticatedUserId) >= 10)
                throw new InvalidOperationException("Etkin oturum sınırına ulaşıldı.");
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var principal = new AuthenticatedSessionPrincipal(authenticatedUserId, Guid.NewGuid().ToString("N"), securityVersion);
            _sessions.Add(Hash(token), new Entry(principal, now));
            return new(token, principal);
        }
    }

    public AuthenticatedSessionPrincipal? Validate(string? token, Func<string, long?> currentSecurityVersion)
    {
        ArgumentNullException.ThrowIfNull(currentSecurityVersion);
        if (!IsToken(token)) return null;
        string hash = Hash(token!);
        // Query catalog without holding session lock: catalog mutation may revoke sessions.
        Entry entry;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(hash, out entry!)) return null;
            if (IsExpired(entry, _time.GetTimestamp())) { _sessions.Remove(hash); return null; }
        }
        long? version = currentSecurityVersion(entry.Principal.UserId);
        lock (_gate)
        {
            // Recheck after the catalog read: logout/revocation may have occurred while it ran.
            if (!_sessions.TryGetValue(hash, out var current) || !ReferenceEquals(entry, current)) return null;
            long now = _time.GetTimestamp();
            if (version != entry.Principal.SecurityVersion || IsExpired(entry, now))
            {
                _sessions.Remove(hash); return null;
            }
            entry.LastUsed = now;
            return entry.Principal;
        }
    }

    public void Revoke(string? token)
    {
        if (!IsToken(token)) return;
        lock (_gate) _sessions.Remove(Hash(token!));
    }

    // Background operations may check the originating session without retaining its bearer or extending idle life.
    public bool IsActive(AuthenticatedSessionPrincipal principal)
    {
        lock (_gate)
        {
            var entry = _sessions.Values.FirstOrDefault(e => e.Principal == principal);
            return entry is not null && !IsExpired(entry, _time.GetTimestamp());
        }
    }

    public void RevokeUser(string userId)
    {
        lock (_gate)
            foreach (string key in _sessions.Where(pair => pair.Value.Principal.UserId == userId).Select(pair => pair.Key).ToArray())
                _sessions.Remove(key);
    }

    private bool IsExpired(Entry entry, long now) =>
        now < entry.Created || now < entry.LastUsed ||
        _time.GetElapsedTime(entry.Created, now) >= AbsoluteLifetime ||
        _time.GetElapsedTime(entry.LastUsed, now) >= IdleLifetime;

    private static bool IsToken(string? token) => token is { Length: 64 } && token.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
}
