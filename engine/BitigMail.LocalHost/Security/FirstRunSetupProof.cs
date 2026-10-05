using System.Security.Cryptography;
using System.Text;

namespace BitigMail.LocalHost.Security;

/// <summary>Consumes only proof received through the native private startup channel, never an HTTP-created proof.</summary>
public sealed class FirstRunSetupProof : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly long _created;
    private readonly byte[] _digest;
    private bool _consumed;

    public FirstRunSetupProof(ReadOnlySpan<byte> nativeProof, TimeProvider? clock = null)
    {
        if (nativeProof.Length != 32) throw new ArgumentException("Kurulum kanıtı 32 bayt olmalıdır.", nameof(nativeProof));
        _clock = clock ?? TimeProvider.System; _created = _clock.GetTimestamp();
        _digest = SHA256.HashData(nativeProof);
    }

    public bool TryConsume(string? candidate)
    {
        if (candidate is not { Length: 64 } || candidate.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))) return false;
        byte[] bytes = Convert.FromHexString(candidate);
        byte[] digest = SHA256.HashData(bytes);
        try
        {
            lock (_gate)
            {
                long now = _clock.GetTimestamp();
                if (_consumed || now < _created || _clock.GetElapsedTime(_created, now) >= TimeSpan.FromMinutes(5)) return false;
                if (!CryptographicOperations.FixedTimeEquals(_digest, digest)) return false;
                _consumed = true;
                CryptographicOperations.ZeroMemory(_digest);
                return true;
            }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); CryptographicOperations.ZeroMemory(digest); }
    }

    public void Dispose()
    {
        lock (_gate) { _consumed = true; CryptographicOperations.ZeroMemory(_digest); }
    }

    public override string ToString() => "FirstRunSetupProof { Proof = [REDACTED] }";
}
