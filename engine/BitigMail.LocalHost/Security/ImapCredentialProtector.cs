using System.Security.Cryptography;
using System.Text;

namespace BitigMail.LocalHost.Security;

public interface IImapCredentialProtector
{
    byte[] Protect(string secret, string accountId);
    string Unprotect(byte[] cipher, string accountId);
}

/// <summary>No plaintext fallback. Ciphertext is bound to the Windows user and account identity.</summary>
public sealed class WindowsImapCredentialProtector : IImapCredentialProtector
{
    public byte[] Protect(string secret, string accountId)
    {
        EnsureWindows();
        ArgumentNullException.ThrowIfNull(secret);
        byte[] entropy = Entropy(accountId);
        byte[] plaintext = Encoding.UTF8.GetBytes(secret);
        try { return ProtectedData.Protect(plaintext, entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public string Unprotect(byte[] cipher, string accountId)
    {
        EnsureWindows();
        ArgumentNullException.ThrowIfNull(cipher);
        byte[] plaintext = ProtectedData.Unprotect(cipher, Entropy(accountId), DataProtectionScope.CurrentUser);
        try { return new UTF8Encoding(false, true).GetString(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private static byte[] Entropy(string accountId)
    {
        if (string.IsNullOrEmpty(accountId) || accountId.Length > 128 ||
            accountId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new ArgumentException("Hesap kimliği geçersiz.");
        return SHA256.HashData(Encoding.UTF8.GetBytes("BitigMail.IMAP.Credential.v1\0" + accountId));
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("IMAP parola deposu Windows kullanıcı koruması gerektirir.");
    }
}
