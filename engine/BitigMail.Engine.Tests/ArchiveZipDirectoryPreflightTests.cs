using System.Buffers.Binary;
using System.IO.Compression;
using BitigMail.Engine.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class ArchiveZipDirectoryPreflightTests
{
    private static byte[] Ordinary()
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        { using var entry = zip.CreateEntry("raw/mail.eml").Open(); entry.Write(new byte[] { 1, 2, 3 }); }
        return bytes.ToArray();
    }
    private static byte[] Zip64(ulong count = 1)
    {
        var ordinary = Ordinary(); int end = ordinary.Length - 22;
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(ordinary.AsSpan(end + 12));
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(ordinary.AsSpan(end + 16));
        using var output = new MemoryStream(); output.Write(ordinary.AsSpan(0, end));
        using var writer = new BinaryWriter(output);
        writer.Write(0x06064b50u); writer.Write(44UL); writer.Write((ushort)45); writer.Write((ushort)45);
        writer.Write(0u); writer.Write(0u); writer.Write(count); writer.Write(count); writer.Write((ulong)size); writer.Write((ulong)offset);
        writer.Write(0x07064b50u); writer.Write(0u); writer.Write((ulong)end); writer.Write(1u);
        var eocd = ordinary.AsSpan(end, 22).ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(eocd.AsSpan(8), ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(eocd.AsSpan(10), ushort.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(12), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(16), uint.MaxValue);
        writer.Write(eocd); writer.Flush(); return output.ToArray();
    }
    [Fact]
    public void OrdinaryAndZip64AreAdmittedBeforeZipArchiveAndPositionIsRestored()
    {
        foreach (byte[] bytes in new[] { Ordinary(), Zip64() })
        {
            using var source = new MemoryStream(bytes); source.Position = 3;
            Assert.Equal(1, ArchiveZipDirectoryPreflight.Validate(source)); Assert.Equal(3, source.Position);
            source.Position = 0; using var zip = new ZipArchive(source, ZipArchiveMode.Read); Assert.Single(zip.Entries);
        }
    }
    [Fact]
    public void OversizedZip64CountIsRejectedBeforeAllocation()
    {
        using var source = new MemoryStream(Zip64(100_001));
        Assert.Throws<InvalidDataException>(() => ArchiveZipDirectoryPreflight.Validate(source));
    }
    [Fact]
    public void TruncatedOrAppendedOrWrongCountDirectoryIsRejected()
    {
        var ordinary = Ordinary(); var badCount = ordinary.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(badCount.AsSpan(badCount.Length - 22 + 8), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(badCount.AsSpan(badCount.Length - 22 + 10), 2);
        foreach (var bytes in new[] { ordinary[..^1], ordinary.Concat(new byte[] { 0 }).ToArray(), badCount })
            Assert.Throws<InvalidDataException>(() => ArchiveZipDirectoryPreflight.Validate(new MemoryStream(bytes)));
    }
    [Fact]
    public void EncryptedAndMultiDiskDirectoriesAreRejected()
    {
        var encrypted = Ordinary(); int end = encrypted.Length - 22;
        int directory = (int)BinaryPrimitives.ReadUInt32LittleEndian(encrypted.AsSpan(end + 16));
        BinaryPrimitives.WriteUInt16LittleEndian(encrypted.AsSpan(directory + 8), 1);
        Assert.Throws<InvalidDataException>(() => ArchiveZipDirectoryPreflight.Validate(new MemoryStream(encrypted)));
        var multi = Ordinary(); BinaryPrimitives.WriteUInt16LittleEndian(multi.AsSpan(multi.Length - 22 + 4), 1);
        Assert.Throws<InvalidDataException>(() => ArchiveZipDirectoryPreflight.Validate(new MemoryStream(multi)));
    }
    [Fact]
    public void CancellationDoesNotMoveSource()
    {
        using var source = new MemoryStream(Ordinary()); source.Position = 4;
        Assert.ThrowsAny<OperationCanceledException>(() => ArchiveZipDirectoryPreflight.Validate(source, new CancellationToken(true)));
        Assert.Equal(4, source.Position);
    }
}
