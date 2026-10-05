namespace BitigMail.Engine.Storage;

/// <summary>Exact byte extraction only; Apple metadata is preserved, not interpreted.</summary>
public sealed record EmlxRecord(byte[] RawMimeBytes, byte[] AppleMetadataBytes);

public static class EmlxRecordReader
{
    public const int DefaultMaxMessageBytes = 64 * 1024 * 1024;
    public const int DefaultMaxMetadataBytes = 1024 * 1024;

    public static EmlxRecord Read(Stream source,
        int maxMessageBytes = DefaultMaxMessageBytes,
        int maxMetadataBytes = DefaultMaxMetadataBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("Kaynak okunabilir olmalıdır.", nameof(source));
        if (maxMessageBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxMessageBytes));
        if (maxMetadataBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxMetadataBytes));

        // Parse the ASCII byte count, never a character count. Bound it before allocation.
        long count = 0;
        int digits = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int b = source.ReadByte();
            if (b == '\n') break;
            if (b == '\r')
            {
                if (source.ReadByte() != '\n') throw InvalidHeader();
                break;
            }
            if (b < '0' || b > '9' || ++digits > 20) throw InvalidHeader();
            if (count > (maxMessageBytes - (b - '0')) / 10L)
                throw new InvalidDataException("EMLX ileti boyutu izin verilen sınırı aşıyor.");
            count = count * 10 + (b - '0');
            if (count > maxMessageBytes)
                throw new InvalidDataException("EMLX ileti boyutu izin verilen sınırı aşıyor.");
        }
        if (digits == 0 || count == 0) throw InvalidHeader();

        var mime = new byte[(int)count];
        int offset = 0;
        while (offset < mime.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = source.Read(mime, offset, Math.Min(81920, mime.Length - offset));
            if (read == 0) throw new InvalidDataException("EMLX ileti içeriği eksik; belirtilen bayt sayısı okunamadı.");
            offset += read;
        }

        // Do not parse XML, resolve external entities, trim whitespace or normalize MIME line endings.
        using var metadata = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = source.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            if (metadata.Length + read > maxMetadataBytes)
                throw new InvalidDataException("EMLX ek bilgileri izin verilen sınırı aşıyor.");
            metadata.Write(buffer, 0, read);
        }
        return new EmlxRecord(mime, metadata.ToArray());
    }

    public static EmlxRecord ReadFile(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!path.EndsWith(".emlx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("EMLX dosyası seçilmelidir.");
        if (path.EndsWith(".partial.emlx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Kısmi Apple Mail dosyası tam ileti ve ek kabulü için kullanılamaz.");
        var file = new FileInfo(Path.GetFullPath(path));
        if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Bağlantılı EMLX dosyaları kabul edilmez.");
        for (var directory = file.Directory; directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Bağlantılı EMLX dizinleri kabul edilmez.");
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(stream, cancellationToken: cancellationToken);
    }

    private static InvalidDataException InvalidHeader() =>
        new("EMLX başlangıcı geçerli ve pozitif bir ASCII bayt sayısı içermelidir.");
}
