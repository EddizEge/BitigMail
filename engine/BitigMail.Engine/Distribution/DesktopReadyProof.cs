using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BitigMail.Engine.Distribution;

public static class DesktopReadyProof
{
    private const string Prefix = "BITIGMAIL_READY ";
    public static string CreateLine(ReadOnlySpan<byte> startupProof, int port)
    {
        Validate(startupProof, port);
        byte[] tag = Compute(startupProof, port);
        try { return Prefix + port.ToString(CultureInfo.InvariantCulture) + " " + Convert.ToHexString(tag); }
        finally { CryptographicOperations.ZeroMemory(tag); }
    }

    public static bool VerifyLine(ReadOnlySpan<byte> startupProof, int expectedPort, string? line)
    {
        Validate(startupProof, expectedPort);
        string prefix = Prefix + expectedPort.ToString(CultureInfo.InvariantCulture) + " ";
        if (line is null || line.Length != prefix.Length + 64 || !line.StartsWith(prefix, StringComparison.Ordinal)) return false;
        byte[] supplied;
        try { supplied = Convert.FromHexString(line.AsSpan(prefix.Length)); }
        catch (FormatException) { return false; }
        byte[] expected = Compute(startupProof, expectedPort);
        try { return CryptographicOperations.FixedTimeEquals(expected, supplied); }
        finally { CryptographicOperations.ZeroMemory(expected); CryptographicOperations.ZeroMemory(supplied); }
    }

    private static byte[] Compute(ReadOnlySpan<byte> proof, int port) => HMACSHA256.HashData(proof,
        Encoding.UTF8.GetBytes("BitigMail.Desktop.Ready.v1:" + port.ToString(CultureInfo.InvariantCulture)));
    private static void Validate(ReadOnlySpan<byte> proof, int port)
    {
        if (proof.Length != 32 || port is < 1024 or > 65535) throw new ArgumentException("Invalid private desktop startup identity.");
    }
}
