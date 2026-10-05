using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BitigMail.Engine.Storage;

public class MboxrdRecord
{
    public int Ordinal { get; set; }
    public byte[] RawMimeBytes { get; set; } = Array.Empty<byte>();
    public string EnvelopeFrom { get; set; } = string.Empty;
}

public static class MboxrdRecordReader
{
    private static readonly byte[] FromPrefix = "From "u8.ToArray();

    public const long DefaultMaxRecordSizeBytes = 64 * 1024 * 1024; // 64 MiB
    public const int DefaultMaxLineLengthBytes = 16 * 1024 * 1024; // 16 MiB line guard

    public static IEnumerable<MboxrdRecord> EnumerateRecords(Stream stream)
    {
        return EnumerateRecords(stream, maxRecordSizeBytes: 0, maxLineLengthBytes: 0);
    }

    public static IEnumerable<MboxrdRecord> EnumerateRecords(
        Stream stream,
        long maxRecordSizeBytes,
        int maxLineLengthBytes = DefaultMaxLineLengthBytes)
    {
        int ordinal = 0;
        using var currentRecordMs = new MemoryStream();
        string currentEnvelope = string.Empty;
        bool inHeaders = true;
        bool hasRecord = false;

        byte[] line;
        while ((line = ReadLineBytes(stream, maxLineLengthBytes)).Length > 0)
        {
            if (IsEnvelopeFromLine(line))
            {
                if (hasRecord)
                {
                    ordinal++;
                    yield return new MboxrdRecord
                    {
                        Ordinal = ordinal,
                        RawMimeBytes = currentRecordMs.ToArray(),
                        EnvelopeFrom = currentEnvelope
                    };
                    currentRecordMs.SetLength(0);
                }

                currentEnvelope = Encoding.ASCII.GetString(line).TrimEnd('\r', '\n');
                hasRecord = true;
                inHeaders = true;
                continue;
            }

            if (!hasRecord)
            {
                // Leading garbage or non-envelope line before first From line
                continue;
            }

            if (maxRecordSizeBytes > 0 && currentRecordMs.Length + line.Length > maxRecordSizeBytes)
            {
                throw new InvalidDataException($"[GÜVENLİK ENGELİ] MBOX kaydı izin verilen maksimum boyutu ({maxRecordSizeBytes} bayt) aşıyor.");
            }

            if (inHeaders)
            {
                currentRecordMs.Write(line, 0, line.Length);
                if (IsBlankLine(line))
                {
                    inHeaders = false;
                }
            }
            else
            {
                // In body: apply exactly one raw mboxrd unescape before MIME decoding
                byte[] unescaped = UnescapeMboxrdLine(line);
                currentRecordMs.Write(unescaped, 0, unescaped.Length);
            }
        }

        if (hasRecord && currentRecordMs.Length > 0)
        {
            ordinal++;
            yield return new MboxrdRecord
            {
                Ordinal = ordinal,
                RawMimeBytes = currentRecordMs.ToArray(),
                EnvelopeFrom = currentEnvelope
            };
        }
    }

    public static bool IsEnvelopeFromLine(byte[] line)
    {
        if (line.Length < FromPrefix.Length) return false;
        for (int i = 0; i < FromPrefix.Length; i++)
        {
            if (line[i] != FromPrefix[i]) return false;
        }
        return true;
    }

    public static bool IsBlankLine(byte[] line)
    {
        return line.Length == 0 ||
               (line.Length == 1 && line[0] == (byte)'\n') ||
               (line.Length == 2 && line[0] == (byte)'\r' && line[1] == (byte)'\n');
    }

    public static byte[] UnescapeMboxrdLine(byte[] line)
    {
        if (line.Length == 0 || line[0] != (byte)'>') return line;

        int i = 1;
        while (i < line.Length && line[i] == (byte)'>')
        {
            i++;
        }

        if (i + FromPrefix.Length <= line.Length)
        {
            bool matches = true;
            for (int k = 0; k < FromPrefix.Length; k++)
            {
                if (line[i + k] != FromPrefix[k])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                // Remove exactly one leading '>'
                byte[] stripped = new byte[line.Length - 1];
                Buffer.BlockCopy(line, 1, stripped, 0, stripped.Length);
                return stripped;
            }
        }

        return line;
    }

    public static byte[] ReadLineBytes(Stream stream) => ReadLineBytes(stream, maxLineLengthBytes: 0);

    public static byte[] ReadLineBytes(Stream stream, int maxLineLengthBytes)
    {
        using var ms = new MemoryStream();
        int b;
        while ((b = stream.ReadByte()) != -1)
        {
            if (maxLineLengthBytes > 0 && ms.Length >= maxLineLengthBytes)
            {
                throw new InvalidDataException($"[GÜVENLİK ENGELİ] Satır uzunluğu izin verilen sınırı ({maxLineLengthBytes} bayt) aşıyor.");
            }
            ms.WriteByte((byte)b);
            if (b == '\n') break;
        }
        return ms.ToArray();
    }
}
