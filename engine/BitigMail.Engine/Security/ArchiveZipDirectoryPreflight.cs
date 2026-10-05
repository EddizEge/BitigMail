using System.Buffers.Binary;

namespace BitigMail.Engine.Security;

/// <summary>Bounds the central directory before ZipArchive allocates entry objects. Supports single-disk ZIP/ZIP64.</summary>
public static class ArchiveZipDirectoryPreflight
{
    public const long MaximumDirectoryBytes = 64L * 1024 * 1024;
    public static int Validate(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead || !source.CanSeek) throw new InvalidDataException("Yedek paketi salt okunur ve aranabilir akış olmalıdır.");
        long original = source.Position;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            long length = source.Length;
            if (length < 22 || length > ArchiveZipPayloadVerifier.MaximumTotalBytes + MaximumDirectoryBytes)
                throw new InvalidDataException("Yedek paket boyutu sınır dışında.");
            byte[] tail = new byte[(int)Math.Min(length, 65535 + 22)];
            long tailStart = length - tail.Length; source.Position = tailStart; source.ReadExactly(tail);
            int end = -1;
            for (int i = tail.Length - 22; i >= 0; i--)
                if (U32(tail, i) == 0x06054b50 && i + 22 + U16(tail, i + 20) == tail.Length) { end = i; break; }
            if (end < 0 || U16(tail, end + 4) != 0 || U16(tail, end + 6) != 0) throw new InvalidDataException("ZIP son kaydı veya disk yapısı geçersiz.");
            ulong count = U16(tail, end + 10), size = U32(tail, end + 12), offset = U32(tail, end + 16);
            long directoryBoundary = tailStart + end;
            if (count == ushort.MaxValue || size == uint.MaxValue || offset == uint.MaxValue)
            {
                long locatorOffset = directoryBoundary - 20;
                if (locatorOffset < 0) throw new InvalidDataException("ZIP64 konum kaydı eksik.");
                byte[] locator = new byte[20]; source.Position = locatorOffset; source.ReadExactly(locator);
                if (U32(locator, 0) != 0x07064b50 || U32(locator, 4) != 0 || U32(locator, 16) != 1)
                    throw new InvalidDataException("Çok diskli ZIP64 desteklenmiyor.");
                ulong recordOffset = U64(locator, 8);
                if (recordOffset > (ulong)Math.Max(0, locatorOffset - 56)) throw new InvalidDataException("ZIP64 konumu geçersiz.");
                byte[] record = new byte[56]; source.Position = (long)recordOffset; source.ReadExactly(record);
                ulong recordLength = U64(record, 4);
                if (U32(record, 0) != 0x06064b50 || recordLength is < 44 or > 1_048_576 ||
                    recordOffset + 12 + recordLength != (ulong)locatorOffset || U32(record, 16) != 0 || U32(record, 20) != 0 || U64(record, 24) != U64(record, 32))
                    throw new InvalidDataException("ZIP64 son kaydı geçersiz.");
                count = U64(record, 32); size = U64(record, 40); offset = U64(record, 48);
                directoryBoundary = (long)recordOffset;
            }
            else if (U16(tail, end + 8) != count) throw new InvalidDataException("ZIP dosya sayısı uyuşmuyor.");
            if (count is < 1 or > ArchiveZipPayloadVerifier.MaximumEntries || size > MaximumDirectoryBytes ||
                offset > (ulong)directoryBoundary || size != (ulong)directoryBoundary - offset || size < count * 46)
                throw new InvalidDataException("ZIP dizin sınırı veya aralığı geçersiz.");
            source.Position = (long)offset;
            byte[] header = new byte[46];
            for (ulong i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (source.Position > directoryBoundary - header.Length) throw new InvalidDataException("ZIP dizini kesilmiş.");
                source.ReadExactly(header);
                if (U32(header, 0) != 0x02014b50 || (U16(header, 8) & 1) != 0 || U16(header, 10) is not (0 or 8) || U16(header, 34) != 0)
                    throw new InvalidDataException("ZIP girdisi şifreli, çok diskli veya desteklenmeyen biçimde.");
                int name = U16(header, 28), extra = U16(header, 30), comment = U16(header, 32);
                long next = source.Position + name + extra + comment;
                if (name == 0 || next > directoryBoundary) throw new InvalidDataException("ZIP girdisi sınır dışında.");
                source.Position = next;
            }
            if (source.Position != directoryBoundary) throw new InvalidDataException("ZIP dizininde kayıt dışı girdiler var.");
            return (int)count;
        }
        catch (EndOfStreamException ex) { throw new InvalidDataException("Yedek ZIP kaydı kesilmiş.", ex); }
        finally { source.Position = original; }
    }
    private static ushort U16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
    private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    private static ulong U64(byte[] data, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset, 8));
}
