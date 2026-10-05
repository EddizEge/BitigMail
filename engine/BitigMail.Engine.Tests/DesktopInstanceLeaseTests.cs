using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using BitigMail.Engine.Distribution;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class DesktopInstanceLeaseTests
{
    private static string Profile() => Path.Combine(Path.GetTempPath(), "bitigmail-instance-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SameProfileRefusedUntilLeaseReleased()
    {
        string profile = Profile();
        using (var first = DesktopInstanceLease.Acquire(profile))
            Assert.Throws<InvalidOperationException>(() => DesktopInstanceLease.Acquire(Path.Combine(profile, ".")));
        using var reopened = DesktopInstanceLease.Acquire(profile);
        Assert.True(File.Exists(Path.Combine(profile, DesktopInstanceLease.FileName)));
    }

    [Fact]
    public void DifferentProfilesRemainIndependent()
    {
        using var first = DesktopInstanceLease.Acquire(Profile());
        using var second = DesktopInstanceLease.Acquire(Profile());
    }

    [Fact]
    public async Task SeparateWindowsProcessCannotOpenActiveProfileLease()
    {
        if (!OperatingSystem.IsWindows()) return;
        string profile = Profile();
        using var lease = DesktopInstanceLease.Acquire(profile);
        string lockPath = Path.Combine(profile, DesktopInstanceLease.FileName).Replace("'", "''");
        var start = new ProcessStartInfo("powershell.exe")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"try {{ $probeStream = [System.IO.File]::Open('{lockPath}', [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None); $probeStream.Dispose(); exit 0 }} catch {{ exit 23 }}");
        using var process = Process.Start(start)!;
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(23, process.ExitCode);
    }

    [Fact]
    public void DisposalIsIdempotentAndDoesNotDeleteProfileData()
    {
        string profile = Profile();
        var lease = DesktopInstanceLease.Acquire(profile);
        string data = Path.Combine(profile, "preserve.txt");
        File.WriteAllText(data, "original");
        lease.Dispose();
        lease.Dispose();
        Assert.Equal("original", File.ReadAllText(data));
        using var reopened = DesktopInstanceLease.Acquire(profile);
    }
}
