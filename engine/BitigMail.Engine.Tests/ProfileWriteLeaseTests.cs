using System.Diagnostics;
using System.Text;
using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class ProfileWriteLeaseTests
{
    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "bitigmail-catalog-lease-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExclusiveLeaseTimesOutAndReleaseAllowsNextWriter()
    {
        string root = NewRoot();
        using (await ProfileWriteLease.AcquireAsync(root))
            await Assert.ThrowsAsync<TimeoutException>(() => ProfileWriteLease.AcquireAsync(root, timeout: TimeSpan.FromMilliseconds(80)));
        using var next = await ProfileWriteLease.AcquireAsync(root);
    }

    [Fact]
    public async Task CancellationDoesNotStealCurrentLease()
    {
        string root = NewRoot(); using var first = await ProfileWriteLease.AcquireAsync(root);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProfileWriteLease.AcquireAsync(root, cancellation.Token));
        await Assert.ThrowsAsync<TimeoutException>(() => ProfileWriteLease.AcquireAsync(root, timeout: TimeSpan.FromMilliseconds(30)));
    }

    [Fact]
    public async Task ConcurrentFirstAdminCheckAndPublishHasOneWinner()
    {
        string root = NewRoot(); int winners = 0;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var lease = await ProfileWriteLease.AcquireAsync(root);
            string catalog = Path.Combine(root, "catalog.json");
            if (File.Exists(catalog)) return;
            await Task.Delay(10);
            await File.WriteAllTextAsync(catalog, "{\"firstAdmin\":true}");
            Interlocked.Increment(ref winners);
        }));
        Assert.Equal(1, winners);
    }

    [Fact]
    public async Task SeparateWindowsProcessCannotBeBypassed()
    {
        string root = NewRoot(); Directory.CreateDirectory(root);
        string path = Path.Combine(root, ProfileWriteLease.LockFileName).Replace("'", "''", StringComparison.Ordinal);
        string script = "$lease=[System.IO.File]::Open('" + path + "',[System.IO.FileMode]::OpenOrCreate,[System.IO.FileAccess]::ReadWrite,[System.IO.FileShare]::None); [Console]::Out.WriteLine('locked'); [Console]::Out.Flush(); [Console]::In.ReadLine() | Out-Null; $lease.Dispose()";
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-EncodedCommand"); start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var child = Process.Start(start)!;
        try
        {
            Assert.Equal("locked", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            await Assert.ThrowsAsync<TimeoutException>(() => ProfileWriteLease.AcquireAsync(root, timeout: TimeSpan.FromMilliseconds(80)));
            await child.StandardInput.WriteLineAsync("release"); await child.StandardInput.FlushAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, child.ExitCode);
            using var acquired = await ProfileWriteLease.AcquireAsync(root);
        }
        finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); } }
    }

    [Fact]
    public async Task JunctionProfileDoesNotCreateLockOutsideRequestedDirectory()
    {
        string root = NewRoot(), outside = NewRoot(); Directory.CreateDirectory(root); Directory.CreateDirectory(outside);
        string junction = Path.Combine(root, "linked");
        var start = new ProcessStartInfo("powershell.exe")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"New-Item -ItemType Junction -Path '{junction.Replace("'", "''")}' -Target '{outside.Replace("'", "''")}' -ErrorAction Stop | Out-Null");
        using var child = Process.Start(start)!;
        try
        {
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(0, child.ExitCode);
            await Assert.ThrowsAsync<InvalidDataException>(() => ProfileWriteLease.AcquireAsync(junction));
            Assert.False(File.Exists(Path.Combine(outside, ProfileWriteLease.LockFileName)));
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
            if (Directory.Exists(junction)) Directory.Delete(junction); // Junction only, never recurse into target.
        }
    }
}
