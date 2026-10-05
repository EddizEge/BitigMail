using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BitigMail.Engine.Storage;

public sealed record BridgeCanonicalMime(byte[] Bytes, string OriginalSha256, string CanonicalSha256,
    bool ConvertedLfToCrLf, bool AddedTerminalNewline);

public sealed record BridgeMboxRecordInfo(string OriginalSha256, string StoredSha256,
    long OriginalLength, bool AddedTerminalNewline);

/// <summary>Explicit byte transformations allowed by the file/account bridge; never rewrites MIME headers or payloads.</summary>
public static class BridgeMimeBytePolicy
{
    private readonly record struct Layout(bool CrLf, bool BareLf, int BodyStart);

    private static Layout Inspect(byte[] raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length == 0) throw new InvalidDataException("Boş ileti aktarılamaz.");
        bool crLf = false, bareLf = false;
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == 0) throw new InvalidDataException("İkili MIME gövdesi bu aktarım yolunda desteklenmiyor.");
            if (raw[i] == '\r')
            {
                if (i + 1 >= raw.Length || raw[i + 1] != '\n')
                    throw new InvalidDataException("Tek CR satır sonu içeren ileti desteklenmiyor.");
                crLf = true;
                i++;
            }
            else if (raw[i] == '\n') bareLf = true;
        }
        int crlfBoundary = raw.AsSpan().IndexOf("\r\n\r\n"u8);
        int lfBoundary = raw.AsSpan().IndexOf("\n\n"u8);
        int boundary = crlfBoundary >= 0 && (lfBoundary < 0 || crlfBoundary < lfBoundary) ? crlfBoundary : lfBoundary;
        int separatorLength = boundary == crlfBoundary ? 4 : 2;
        if (boundary <= 0) throw new InvalidDataException("MIME başlık ve gövde ayracı bulunamadı.");
        // A structurally valid header block is required before body escaping is safe.
        int position = 0;
        bool previousHeader = false;
        while (position < boundary)
        {
            int end = Array.IndexOf(raw, (byte)'\n', position);
            if (end < 0 || end > boundary) end = boundary;
            int length = end - position;
            if (length > 0 && raw[position + length - 1] == '\r') length--;
            var line = raw.AsSpan(position, length);
            if (line.Length == 0) throw new InvalidDataException("Geçersiz MIME başlığı.");
            if (line[0] is (byte)' ' or (byte)'\t')
            {
                if (!previousHeader) throw new InvalidDataException("Geçersiz MIME başlık devamı.");
            }
            else
            {
                int colon = line.IndexOf((byte)':');
                if (colon < 1) throw new InvalidDataException("Geçersiz MIME başlık alanı.");
                for (int i = 0; i < colon; i++)
                    if (line[i] < 33 || line[i] > 126)
                        throw new InvalidDataException("Geçersiz MIME başlık adı.");
                previousHeader = true;
            }
            position = end + 1;
        }
        return new Layout(crLf, bareLf, boundary + separatorLength);
    }

    public static BridgeCanonicalMime CanonicalizeForImap(byte[] rawMime)
    {
        var layout = Inspect(rawMime);
        bool added = rawMime[^1] != '\n';
        byte[] result;
        if (!layout.BareLf && !added) result = rawMime.ToArray();
        else
        {
            using var output = new MemoryStream();
            for (int i = 0; i < rawMime.Length; i++)
            {
                if (rawMime[i] == '\n' && (i == 0 || rawMime[i - 1] != '\r')) output.WriteByte((byte)'\r');
                output.WriteByte(rawMime[i]);
            }
            if (added) output.Write("\r\n"u8);
            result = output.ToArray();
        }
        return new BridgeCanonicalMime(result, Hash(rawMime), Hash(result), layout.BareLf, added);
    }

    /// <summary>Writes one mboxrd record. StoredSha256 hashes the unescaped stored MIME, excluding the envelope.</summary>
    public static BridgeMboxRecordInfo WriteMboxrdRecord(Stream output, byte[] rawMime, DateTimeOffset envelopeDate)
    {
        ArgumentNullException.ThrowIfNull(output);
        var layout = Inspect(rawMime);
        if (!output.CanWrite) throw new ArgumentException("Çıktı yazılabilir olmalıdır.", nameof(output));
        bool added = rawMime[^1] != '\n';
        byte[] newline = layout.CrLf ? "\r\n"u8.ToArray() : "\n"u8.ToArray();
        string envelope = "From bitigmail@archive.invalid " +
            envelopeDate.UtcDateTime.ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture);
        output.Write(Encoding.ASCII.GetBytes(envelope));
        output.Write(newline);
        output.Write(rawMime.AsSpan(0, layout.BodyStart));
        int start = layout.BodyStart;
        while (start < rawMime.Length)
        {
            int lf = Array.IndexOf(rawMime, (byte)'\n', start);
            int end = lf < 0 ? rawMime.Length : lf + 1;
            int marker = start;
            while (marker < end && rawMime[marker] == '>') marker++;
            if (rawMime.AsSpan(marker, end - marker).StartsWith("From "u8)) output.WriteByte((byte)'>');
            output.Write(rawMime.AsSpan(start, end - start));
            start = end;
        }
        if (added) output.Write(newline);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(rawMime);
        if (added) hash.AppendData(newline);
        return new BridgeMboxRecordInfo(Hash(rawMime), Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
            rawMime.LongLength, added);
    }

    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
