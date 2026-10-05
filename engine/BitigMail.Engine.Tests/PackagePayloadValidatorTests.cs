using System.Security.Cryptography;
using BitigMail.Engine.Distribution;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class PackagePayloadValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bitigmail-package-test-" + Guid.NewGuid().ToString("N"));
    public PackagePayloadValidatorTests() => Directory.CreateDirectory(_root);
    private PackagePayloadManifest Fixture()
    {
        File.WriteAllBytes(Path.Combine(_root, "app.exe"), [1, 2, 3]);
        return new(1, "BitigMail", "0.9.0", 1, 2,
            [new("app.exe", 3, Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })))]);
    }
    [Fact] public async Task ValidPayloadIsIntegrityOnly()
    {
        var result = await new PackagePayloadValidator().ValidateAsync(_root, Fixture(), 1);
        Assert.Equal(1, result.FileCount);
        Assert.Equal("INTEGRITY_ONLY_PUBLISHER_NOT_VERIFIED", result.PublisherQualification);
    }
    [Theory]
    [InlineData("../outside")][InlineData("/absolute")][InlineData("a\\b")][InlineData("a:stream")]
    [InlineData("a//b")][InlineData("NUL.txt")][InlineData("com1")][InlineData("LPT¹.bin")]
    [InlineData("tail.")][InlineData("tail ")][InlineData("a/./b")]
    [InlineData("CON .txt")][InlineData("conout$")][InlineData("conin$.bin")]
    public void UnsafeWindowsNamesAreRejected(string path) =>
        Assert.Throws<InvalidDataException>(() => PackagePayloadValidator.ValidateRelativePath(path));
    [Fact] public async Task ChangedMissingAndExtraFilesFail()
    {
        var manifest = Fixture(); var validator = new PackagePayloadValidator();
        File.WriteAllBytes(Path.Combine(_root, "app.exe"), [3, 2, 1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => validator.ValidateAsync(_root, manifest, 1));
        File.Delete(Path.Combine(_root, "app.exe"));
        await Assert.ThrowsAsync<InvalidDataException>(() => validator.ValidateAsync(_root, manifest, 1));
        Fixture(); File.WriteAllText(Path.Combine(_root, "extra.dll"), "extra");
        await Assert.ThrowsAsync<InvalidDataException>(() => validator.ValidateAsync(_root, manifest, 1));
    }
    [Fact] public async Task CaseDuplicateAndIncompatibleSchemaFail()
    {
        var manifest = Fixture(); var validator = new PackagePayloadValidator();
        await Assert.ThrowsAsync<InvalidDataException>(() => validator.ValidateAsync(_root,
            manifest with { Files = [manifest.Files[0], manifest.Files[0] with { RelativePath = "APP.EXE" }] }, 1));
        await Assert.ThrowsAsync<InvalidDataException>(() => validator.ValidateAsync(_root, manifest, 3));
    }
    [Fact] public async Task CancellationNeverReturnsSuccessfulValidation()
    {
        var manifest = Fixture(); using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PackagePayloadValidator().ValidateAsync(_root, manifest, 1, source.Token));
    }
    [Fact] public async Task JunctionInsidePayloadIsRejectedWithoutReadingOutside()
    {
        var manifest = Fixture();
        string outside = Path.Combine(Path.GetTempPath(), "bitigmail-package-outside-" + Guid.NewGuid().ToString("N"));
        string junction = Path.Combine(_root, "linked");
        Directory.CreateDirectory(outside);
        string marker = Path.Combine(outside, "untouched.txt");
        File.WriteAllText(marker, "outside data");
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("powershell.exe")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
            // Paths are generated GUID paths, still quote apostrophes for PowerShell literals.
            start.ArgumentList.Add($"New-Item -ItemType Junction -Path '{junction.Replace("'", "''")}' -Target '{outside.Replace("'", "''")}' -ErrorAction Stop | Out-Null");
            using var process = System.Diagnostics.Process.Start(start)!;
            await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode);
            await Assert.ThrowsAsync<InvalidDataException>(() => new PackagePayloadValidator().ValidateAsync(_root, manifest, 1));
            Assert.Equal("outside data", File.ReadAllText(marker));
        }
        finally
        {
            if (Directory.Exists(junction)) Directory.Delete(junction); // Link itself only, nonrecursive.
            File.Delete(marker); Directory.Delete(outside);
        }
    }
    public void Dispose()
    {
        // Only this test's fixed direct children; no recursive cleanup or link traversal.
        foreach (string file in Directory.GetFiles(_root)) File.Delete(file);
        Directory.Delete(_root);
    }
}
