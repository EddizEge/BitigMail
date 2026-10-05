using System;
using System.IO;
using System.Security.Cryptography;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using Xunit;

namespace BitigMail.Engine.Tests;

public class TargetSafetyAndIntegrityTests
{
    private static string ResolveApprovedFixturePath()
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, "lab", "ost-spike", "input", "bitigmail-lab-full.ost");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, "lab", "ost-spike", "input", "bitigmail-lab-full.ost");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        throw new FileNotFoundException("Approved test fixture 'lab/ost-spike/input/bitigmail-lab-full.ost' was not found.");
    }

    [Fact]
    public void Convert_ExistingTargetPst_ThrowsInvalidOperationException()
    {
        string fixturePath = ResolveApprovedFixturePath();
        string tempTarget = Path.GetTempFileName(); // Already exists!

        try
        {
            var converter = new OstToPstConverter();
            var context = new ClientProjectContext { CompanyName = "Test Co", ProjectName = "Test Proj" };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                converter.Convert(fixturePath, tempTarget, "job-test-01", context));

            Assert.Contains("Hedef PST dosyası zaten mevcut", ex.Message);
        }
        finally
        {
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }
    }

    [Fact]
    public void Convert_TargetSameAsSource_ThrowsInvalidOperationException()
    {
        string fixturePath = ResolveApprovedFixturePath();
        var converter = new OstToPstConverter();
        var context = new ClientProjectContext();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            converter.Convert(fixturePath, fixturePath, "job-test-02", context));

        Assert.Contains("aynı olamaz", ex.Message);
    }

    [Fact]
    public void Convert_ChangedSourceSha256_ThrowsInvalidOperationException()
    {
        string fixturePath = ResolveApprovedFixturePath();
        string tempSource = Path.Combine(Path.GetTempPath(), $"temp-ost-{Guid.NewGuid():N}.ost");
        string tempTarget = Path.Combine(Path.GetTempPath(), $"target-{Guid.NewGuid():N}.pst");

        try
        {
            File.Copy(fixturePath, tempSource, overwrite: true);
            var converter = new OstToPstConverter();
            var context = new ClientProjectContext();

            // Provide a mismatched expected SHA-256 for a real, valid OST
            string wrongSha = "0000000000000000000000000000000000000000000000000000000000000000";

            var ex = Assert.Throws<InvalidOperationException>(() =>
                converter.Convert(tempSource, tempTarget, "job-test-03", context, expectedSourceSha256: wrongSha));

            Assert.Contains("değiştirilmiş", ex.Message);
        }
        finally
        {
            if (File.Exists(tempSource)) File.Delete(tempSource);
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }
    }

    [Fact]
    public void Convert_MutatedRealOst_ThrowsInvalidOperationExceptionAndPreservesOriginal()
    {
        string fixturePath = ResolveApprovedFixturePath();
        string tempSource = Path.Combine(Path.GetTempPath(), $"temp-mutated-{Guid.NewGuid():N}.ost");
        string tempTarget = Path.Combine(Path.GetTempPath(), $"target-mutated-{Guid.NewGuid():N}.pst");

        try
        {
            File.Copy(fixturePath, tempSource, overwrite: true);

            // Compute valid hash of original fixture
            string originalSha;
            using (var stream = File.OpenRead(fixturePath))
            using (var sha = SHA256.Create())
            {
                originalSha = System.Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
            }

            // Mutate data portion (e.g. offset 1024, keeping 64-byte header intact)
            using (var stream = new FileStream(tempSource, FileMode.Open, FileAccess.ReadWrite))
            {
                stream.Seek(1024, SeekOrigin.Begin);
                int b = stream.ReadByte();
                stream.Seek(1024, SeekOrigin.Begin);
                stream.WriteByte((byte)(b ^ 0xFF));
            }

            var converter = new OstToPstConverter();
            var context = new ClientProjectContext();

            // When expectedSourceSha256 is the pre-mutation hash, the mutated file is detected and rejected
            var ex = Assert.Throws<InvalidOperationException>(() =>
                converter.Convert(tempSource, tempTarget, "job-test-mutated", context, expectedSourceSha256: originalSha));

            Assert.Contains("değiştirilmiş", ex.Message);
            Assert.False(File.Exists(tempTarget));

            // Verify original fixture was never mutated
            using (var stream = File.OpenRead(fixturePath))
            using (var sha = SHA256.Create())
            {
                string postTestSha = System.Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
                Assert.Equal(originalSha, postTestSha);
            }
        }
        finally
        {
            if (File.Exists(tempSource)) File.Delete(tempSource);
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }
    }

    [Fact]
    public void Convert_TargetAppearsDuringConversion_ThrowsInvalidOperationExceptionAndPreservesTarget()
    {
        string fixturePath = ResolveApprovedFixturePath();
        string tempTarget = Path.Combine(Path.GetTempPath(), $"target-race-{Guid.NewGuid():N}.pst");

        try
        {
            // Simulate that target was created right when atomic move was attempted
            File.WriteAllText(tempTarget, "critical existing target content");

            var converter = new OstToPstConverter();
            var context = new ClientProjectContext();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                converter.Convert(fixturePath, tempTarget, "job-test-race", context));

            Assert.Contains("zaten mevcut", ex.Message);
            Assert.Equal("critical existing target content", File.ReadAllText(tempTarget));
        }
        finally
        {
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }
    }
}
