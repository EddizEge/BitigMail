using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace BitigMail.LocalHost.Security;

public class SessionManager
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _validSessions = new(StringComparer.OrdinalIgnoreCase);

    public string CreateSession()
    {
        byte[] bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        string token = Convert.ToHexString(bytes).ToLowerInvariant();
        _validSessions[token] = DateTimeOffset.UtcNow;
        return token;
    }

    public bool ValidateSession(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        string cleanToken = token.Trim().ToLowerInvariant();
        return _validSessions.ContainsKey(cleanToken);
    }

    public bool HasActiveSession => !_validSessions.IsEmpty;
}
