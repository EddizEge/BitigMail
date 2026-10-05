using System.Diagnostics;
using BitigMail.Engine.Execution;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class OwnedWorkerProcessRunnerTests
{
    private static string Shell => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
    private static string DirectoryPath => Path.GetTempPath();
    private static string[] Command(string script) => ["-NoProfile", "-NonInteractive", "-Command", script];
    [Theory]
    [InlineData(0, "completed")]
    [InlineData(7, "failed")]
    public async Task CapturesExitAndDrainsBothStreamsWithoutKeepingPrivateContents(int exitCode, string expected)
    {
        var result = await new OwnedWorkerProcessRunner().RunAsync(Shell,
            Command($"[Console]::Out.Write('private-data'); [Console]::Error.Write('private-error'); exit {exitCode}"), DirectoryPath, TimeSpan.FromSeconds(15));
        Assert.Equal(expected, result.Outcome); Assert.Equal(exitCode, result.ExitCode); Assert.True(result.TerminationConfirmed);
        Assert.Equal(12, result.StandardOutputCharacters); Assert.Equal(13, result.StandardErrorCharacters);
    }
    [Fact] public async Task TimeoutTerminatesOnlyOwnedWorker()
    {
        var result = await new OwnedWorkerProcessRunner().RunAsync(Shell, Command("Start-Sleep -Seconds 30"), DirectoryPath, TimeSpan.FromSeconds(1));
        Assert.Equal("timed_out", result.Outcome); Assert.True(result.TerminationConfirmed);
        Assert.True(Process.GetCurrentProcess().Id != result.ProcessId);
    }
    [Fact] public async Task CancellationTerminatesOwnedWorker()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var result = await new OwnedWorkerProcessRunner().RunAsync(Shell, Command("Start-Sleep -Seconds 30"), DirectoryPath, TimeSpan.FromSeconds(15), cancel.Token);
        Assert.Equal("cancelled", result.Outcome); Assert.True(result.TerminationConfirmed);
    }
    [Fact] public async Task AlreadyCancelledDoesNotLaunch()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() => new OwnedWorkerProcessRunner().RunAsync(Shell, Command("exit 0"), DirectoryPath, TimeSpan.FromSeconds(1), new CancellationToken(true)));
    }
    [Fact] public async Task InvalidExecutableAndUnboundedTimeoutAreRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new OwnedWorkerProcessRunner().RunAsync("powershell.exe", [], DirectoryPath, TimeSpan.FromSeconds(1)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new OwnedWorkerProcessRunner().RunAsync(Shell, [], DirectoryPath, Timeout.InfiniteTimeSpan));
    }
    [Fact] public async Task BlockedBootstrapPipeIsCoveredByDeadlineAndKillsOwnedChild()
    {
        var timer = Stopwatch.StartNew();
        var result = await new OwnedWorkerProcessRunner().RunAsync(Shell, Command("Start-Sleep -Seconds 30"),
            DirectoryPath, TimeSpan.FromSeconds(1), standardInput: new byte[1024 * 1024]);
        Assert.Equal("timed_out", result.Outcome); Assert.True(result.TerminationConfirmed);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10));
    }
    [Fact] public async Task BootstrapBytesAreSentOverOwnedPipeWithoutOutputRetention()
    {
        var result = await new OwnedWorkerProcessRunner().RunAsync(Shell,
            Command("$s = [Console]::OpenStandardInput(); $m = New-Object System.IO.MemoryStream; $s.CopyTo($m); if ($m.Length -eq 3) { exit 0 } else { exit 8 }"),
            DirectoryPath, TimeSpan.FromSeconds(10), standardInput: new byte[] { 1, 2, 3 });
        Assert.Equal("completed", result.Outcome); Assert.Equal(0, result.StandardOutputCharacters);
    }
}
