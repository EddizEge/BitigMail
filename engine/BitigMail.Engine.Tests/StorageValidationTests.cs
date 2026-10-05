using System;
using System.IO;
using BitigMail.Engine.Storage;
using Xunit;

namespace BitigMail.Engine.Tests;

public class StorageValidationTests
{
    [Fact]
    public void InspectSignature_NonExistentFile_ThrowsFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() =>
            OutlookStorageInspector.InspectSignature("C:/non-existent-file-123.ost"));
    }

    [Fact]
    public void InspectSignature_RandomFile_RejectedAsInvalidOutlookStorage()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "This is a plain text file, definitely not Outlook storage.");
            var info = OutlookStorageInspector.InspectSignature(tempFile);

            Assert.False(info.IsValidOutlookStorage);
            Assert.False(info.IsOstSignature);
            Assert.False(info.IsPstSignature);

            Assert.Throws<InvalidDataException>(() =>
                OutlookStorageInspector.ValidateOstAuthoritatively(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void InspectSignature_PstFile_RejectedWhenValidatedAsOst()
    {
        // Construct a synthetic 24-byte PST header:
        // Offset 0..3: !BDN (0x21, 0x42, 0x44, 0x4E)
        // Offset 8..9: 0x4D53 ('SM' = PST)
        // Offset 10..11: 0x0024 (Unicode)
        string tempFile = Path.GetTempFileName();
        try
        {
            byte[] pstHeader = new byte[64];
            pstHeader[0] = 0x21;
            pstHeader[1] = 0x42;
            pstHeader[2] = 0x44;
            pstHeader[3] = 0x4E;
            pstHeader[8] = 0x53; // 'S'
            pstHeader[9] = 0x4D; // 'M' -> Little endian 0x4D53 = PST
            pstHeader[10] = 0x24;
            pstHeader[11] = 0x00;

            File.WriteAllBytes(tempFile, pstHeader);

            var info = OutlookStorageInspector.InspectSignature(tempFile);

            Assert.True(info.IsValidOutlookStorage);
            Assert.True(info.IsPstSignature);
            Assert.False(info.IsOstSignature);

            // Validating PST as OST must throw InvalidDataException
            var ex = Assert.Throws<InvalidDataException>(() =>
                OutlookStorageInspector.ValidateOstAuthoritatively(tempFile));

            Assert.Contains("OST istemci magic 0x4F53", ex.Message);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
