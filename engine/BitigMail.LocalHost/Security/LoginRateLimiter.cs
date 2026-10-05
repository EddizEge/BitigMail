using System.Security.Cryptography;
using System.Text;

namespace BitigMail.LocalHost.Security;

/// <summary>Bounded process-local throttling. Password operations also have a separate concurrency bound.</summary>
public sealed class LoginRateLimiter
{
    private sealed record Entry(int Failures, long LastFailure, TimeSpan Delay);
    private const int MaximumKeys = 2048;
    private static readonly TimeSpan HistoryLifetime = TimeSpan.FromMinutes(15);
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private Entry? _profile;
    public LoginRateLimiter(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public bool IsAllowed(string key)
    {
        string? digest = Digest(key);
        if (digest is null) return false;
        lock (_gate)
        {
            long now = _time.GetTimestamp();
            Purge(now);
            if (_profile is not null && !Elapsed(_profile, now)) return false;
            if (_entries.TryGetValue(digest, out var entry)) return Elapsed(entry, now);
            return _entries.Count < MaximumKeys;
        }
    }

    public void Failed(string key)
    {
        string? digest = Digest(key);
        lock (_gate)
        {
            long now = _time.GetTimestamp();
            Purge(now);
            int globalFailures = Math.Min(30, (_profile?.Failures ?? 0) + 1);
            _profile = new(globalFailures, now, globalFailures < 10 ? TimeSpan.Zero : Delay(globalFailures - 9));
            if (digest is null) return;
            _entries.TryGetValue(digest, out var old);
            if (old is null && _entries.Count >= MaximumKeys) return;
            int failures = Math.Min(30, (old?.Failures ?? 0) + 1);
            _entries[digest] = new(failures, now, Delay(failures));
        }
    }

    public void Succeeded(string key)
    {
        string? digest = Digest(key);
        if (digest is null) return;
        // Success on an attacker's own account must not clear profile-wide failures.
        lock (_gate) _entries.Remove(digest);
    }

    private bool Elapsed(Entry entry, long now) => _time.GetElapsedTime(entry.LastFailure, now) >= entry.Delay;
    private void Purge(long now)
    {
        foreach (string key in _entries.Where(p => _time.GetElapsedTime(p.Value.LastFailure, now) >= HistoryLifetime).Select(p => p.Key).ToArray())
            _entries.Remove(key);
        if (_profile is not null && _time.GetElapsedTime(_profile.LastFailure, now) >= HistoryLifetime) _profile = null;
    }
    private static TimeSpan Delay(int failures) => TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Max(0, failures - 3))));
    private static string? Digest(string key) => string.IsNullOrWhiteSpace(key) || key.Length > 256 ? null :
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
