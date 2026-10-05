using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Archive;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class FreshArchivePublicationTests
{
    private static async Task<string> Stage(ArchiveStorageManager storage, string id)
    {
        string staging = storage.CreateStagingDirectory("stage");
        await storage.WriteManifestAsync(staging, new ArchiveManifest
        {
            ArchiveId = id, ArchiveName = "test", CompanyId = "company", ProjectId = "project",
            SourceKind = "eml-files", Dialect = "eml", SourceFingerprint = "test"
        }, CancellationToken.None);
        File.WriteAllText(Path.Combine(staging, "messages", "original.eml"), "preserve");
        return staging;
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("manifest")]
    [InlineData("file")]
    [InlineData("none")]
    public async Task PublicationNeverOverwritesExistingTarget(string collision)
    {
        string root = Path.Combine(Path.GetTempPath(), "bitigmail-fresh-" + Guid.NewGuid().ToString("N"));
        var storage = new ArchiveStorageManager(root);
        string staging = await Stage(storage, "restored");
        string target = Path.Combine(storage.ArchivesBaseDirectory, "restored");
        if (collision == "file") File.WriteAllText(target, "existing");
        else if (collision != "none")
        {
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, collision == "manifest" ? "manifest.json" : "keep.txt"), "existing");
        }
        if (collision == "none")
        {
            storage.PublishFreshStagingToManagedArchive(staging, "restored");
            Assert.False(Directory.Exists(staging));
            Assert.Equal("preserve", File.ReadAllText(Path.Combine(target, "messages", "original.eml")));
        }
        else
        {
            Assert.Throws<IOException>(() => storage.PublishFreshStagingToManagedArchive(staging, "restored"));
            Assert.True(Directory.Exists(staging));
            Assert.Equal("existing", File.ReadAllText(collision == "file" ? target : Path.Combine(target, collision == "manifest" ? "manifest.json" : "keep.txt")));
        }
    }

    [Fact]
    public async Task RejectsLinkedPayloadWithoutTouchingLinkTarget()
    {
        string root = Path.Combine(Path.GetTempPath(), "bitigmail-fresh-" + Guid.NewGuid().ToString("N"));
        var storage = new ArchiveStorageManager(root);
        string staging = await Stage(storage, "restored");
        string outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "existing");
        if (OperatingSystem.IsWindows())
        {
            var start = new System.Diagnostics.ProcessStartInfo("powershell.exe")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add($"New-Item -ItemType Junction -Path '{Path.Combine(staging, "linked")}' -Target '{outside}' -ErrorAction Stop | Out-Null");
            using var process = System.Diagnostics.Process.Start(start)!;
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(Path.Combine(staging, "linked"), outside);
        Assert.Throws<InvalidDataException>(() => storage.PublishFreshStagingToManagedArchive(staging, "restored"));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(outside, "keep.txt")));
        Assert.True(Directory.Exists(staging));
    }

    [Fact]
    public async Task RejectsForeignStagingAndMismatchedIdentity()
    {
        string root = Path.Combine(Path.GetTempPath(), "bitigmail-fresh-" + Guid.NewGuid().ToString("N"));
        var storage = new ArchiveStorageManager(root);
        string staging = await Stage(storage, "original");
        Assert.Throws<InvalidDataException>(() => storage.PublishFreshStagingToManagedArchive(staging, "different"));
        var foreign = new ArchiveStorageManager(Path.Combine(root, "foreign"));
        Assert.Throws<InvalidOperationException>(() => foreign.PublishFreshStagingToManagedArchive(staging, "original"));
        Assert.True(Directory.Exists(staging));
    }
}
