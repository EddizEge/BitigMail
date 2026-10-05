using System;
using System.IO;

namespace BitigMail.Engine.Bridge;

/// <summary>
/// Strict mboxrd validator for the bridge transfer path.
/// Enforces:
/// 1. Zero bytes before the first From-envelope line (offset 0 must start with "From ").
/// 2. No empty or malformed records (must have valid envelope line and RFC2822/5322 header block).
/// 3. No ambiguous delimiter (must have blank line delimiter between headers and body, valid header syntax).
/// Does not modify legacy MboxrdRecordReader or old PST/MIME paths.
/// </summary>
public static class BridgeMboxrdValidator
{
    private static readonly byte[] FromPrefix = "From "u8.ToArray();

    public static void ValidateStrictMboxrd(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException($"MBOX dosyası bulunamadı: {filePath}", filePath);
        }

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        ValidateStrictMboxrd(fs, filePath);
    }

    public static void ValidateStrictMboxrd(Stream stream, string identifier = "mbox")
    {
        if (stream == null || stream.Length == 0)
        {
            throw new InvalidOperationException($"[STRICT MBOXRD] MBOX dosyası boş: {identifier}");
        }

        long originalPos = stream.CanSeek ? stream.Position : 0;
        try
        {
            // 1. Reject bytes before first From-envelope
            byte[] firstLine = ReadLineBytes(stream);
            if (!IsEnvelopeFromLine(firstLine))
            {
                throw new InvalidOperationException($"[STRICT MBOXRD] Dosya başında 'From ' zarfı bulunamadı; baştaki baytlar veya geçersiz biçim reddedildi: {identifier}");
            }

            if (!IsValidEnvelopeHeader(firstLine))
            {
                throw new InvalidOperationException($"[STRICT MBOXRD] Geçersiz 'From ' zarf başlığı: {identifier}");
            }

            int recordCount = 0;
            bool inHeaders = true;
            int headerLinesInRecord = 0;
            bool hasBlankDelimiter = false;
            byte[] line;

            while ((line = ReadLineBytes(stream)).Length > 0)
            {
                if (IsEnvelopeFromLine(line))
                {
                    // Transition to new record: previous record MUST have valid headers and blank line delimiter
                    if (!IsValidEnvelopeHeader(line))
                    {
                        throw new InvalidOperationException($"[STRICT MBOXRD] Bozuk veya geçersiz zarf başlığı: {identifier}");
                    }

                    if (inHeaders || !hasBlankDelimiter || headerLinesInRecord == 0)
                    {
                        throw new InvalidOperationException($"[STRICT MBOXRD] Boş veya hatalı e-posta kaydı (başlık veya ayırıcı eksik): {identifier}");
                    }

                    recordCount++;
                    inHeaders = true;
                    headerLinesInRecord = 0;
                    hasBlankDelimiter = false;
                    continue;
                }

                if (inHeaders)
                {
                    if (IsBlankLine(line))
                    {
                        if (headerLinesInRecord == 0)
                        {
                            throw new InvalidOperationException($"[STRICT MBOXRD] Boş başlık bloğu: {identifier}");
                        }
                        inHeaders = false;
                        hasBlankDelimiter = true;
                    }
                    else
                    {
                        if (!IsValidHeaderLine(line, headerLinesInRecord > 0))
                        {
                            throw new InvalidOperationException($"[STRICT MBOXRD] Belirsiz veya bozuk başlık satırı: {identifier}");
                        }
                        headerLinesInRecord++;
                    }
                }
            }

            // Validate trailing record
            if (inHeaders || !hasBlankDelimiter || headerLinesInRecord == 0)
            {
                throw new InvalidOperationException($"[STRICT MBOXRD] Son kayıt tamamlanmamış veya bozuk (başlık/ayırıcı eksik): {identifier}");
            }

            recordCount++;
            if (recordCount == 0)
            {
                throw new InvalidOperationException($"[STRICT MBOXRD] MBOX dosyasında hiç geçerli kayıt bulunamadı: {identifier}");
            }
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = originalPos;
            }
        }
    }

    private static bool IsEnvelopeFromLine(byte[] line)
    {
        if (line.Length < FromPrefix.Length) return false;
        for (int i = 0; i < FromPrefix.Length; i++)
        {
            if (line[i] != FromPrefix[i]) return false;
        }
        return true;
    }

    private static bool IsValidEnvelopeHeader(byte[] line)
    {
        if (line.Length <= FromPrefix.Length) return false;
        byte next = line[FromPrefix.Length];
        if (next == (byte)' ' || next == (byte)'\t' || next == (byte)'\r' || next == (byte)'\n' || next == 0)
            return false;
        return true;
    }

    private static bool IsBlankLine(byte[] line)
    {
        return line.Length == 0 ||
               (line.Length == 1 && (line[0] == (byte)'\n' || line[0] == (byte)'\r')) ||
               (line.Length == 2 && line[0] == (byte)'\r' && line[1] == (byte)'\n');
    }

    private static bool IsValidHeaderLine(byte[] line, bool canBeContinuation)
    {
        if (line.Length == 0) return false;
        if (line[0] == (byte)' ' || line[0] == (byte)'\t')
        {
            return canBeContinuation;
        }

        int colonIdx = -1;
        for (int i = 0; i < line.Length; i++)
        {
            byte b = line[i];
            if (b == (byte)':')
            {
                colonIdx = i;
                break;
            }
            if (b <= 32 || b >= 127)
            {
                return false;
            }
        }

        return colonIdx > 0;
    }

    private static byte[] ReadLineBytes(Stream stream)
    {
        using var ms = new MemoryStream();
        int b;
        while ((b = stream.ReadByte()) != -1)
        {
            ms.WriteByte((byte)b);
            if (b == '\n') break;
        }
        return ms.ToArray();
    }
}
