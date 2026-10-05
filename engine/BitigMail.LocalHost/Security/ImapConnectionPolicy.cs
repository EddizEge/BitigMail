using System.Globalization;
using System.Net;

namespace BitigMail.LocalHost.Security;

/// <summary>Transport and input boundary. The lab exception is host-owned DI configuration.</summary>
public sealed class ImapConnectionPolicy
{
    private readonly bool _allowTask014Loopback;

    public ImapConnectionPolicy(bool allowTask014Loopback = false)
    {
        _allowTask014Loopback = allowTask014Loopback;
    }

    public void Validate(string host, int port, string tlsMode, string username, string password, bool allowUnencryptedConnection = false)
    {
        ValidateText(host, 253, "Sunucu adresi");
        if (host != host.Trim() || host.Contains('/') || host.Contains('\\') ||
            host.Contains('@') || host.Contains('?') || host.Contains('#') || host.Contains('%'))
            throw new ArgumentException("Sunucu alanına yalnız DNS adı veya IP adresi yazın.");

        if (!IPAddress.TryParse(host, out _))
        {
            string ascii;
            try { ascii = new IdnMapping().GetAscii(host); }
            catch (ArgumentException) { throw new ArgumentException("Sunucu adresi geçersiz."); }
            if (ascii.Length > 253 || ascii.Split('.').Any(label =>
                label.Length is < 1 or > 63 || label[0] == '-' || label[^1] == '-' ||
                label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
                throw new ArgumentException("Sunucu adresi geçersiz.");
        }

        if (port is < 1 or > 65535) throw new ArgumentException("Port 1 ile 65535 arasında olmalıdır.");
        if (tlsMode is not ("ssl" or "starttls"))
        {
            if (tlsMode != "none" || !(allowUnencryptedConnection || (_allowTask014Loopback && host == "127.0.0.1" && port == 5143)))
                throw new ArgumentException("SSL/TLS veya STARTTLS seçin; şifresiz bağlantı için açık onay gereklidir.");
        }

        ValidateText(username, 320, "Kullanıcı adı");
        ValidateText(password, 16384, "Parola");
    }

    public static void ValidateFolderPath(string path) => ValidateText(path, 1024, "Klasör yolu");

    public static void ValidateScope(string companyId, string projectId)
    {
        ValidateText(companyId, 128, "Müşteri kimliği");
        ValidateText(projectId, 128, "Proje kimliği");
    }

    private static void ValidateText(string? value, int maxLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || value.Any(char.IsControl))
            throw new ArgumentException($"{label} boş, çok uzun veya geçersiz kontrol karakterleri içeriyor.");
    }
}

