using System.Diagnostics;

namespace BitigMail.Engine.Execution;

public sealed record OwnedWorkerResult(string Outcome, int ProcessId, int? ExitCode,
    bool TerminationConfirmed, long StandardOutputCharacters, long StandardErrorCharacters);

/// <summary>Runs only a caller-selected trusted executable; never accepts an HTTP-supplied command.</summary>
public sealed class OwnedWorkerProcessRunner
{
    public async Task<OwnedWorkerResult> RunAsync(string executablePath, IReadOnlyList<string> arguments,
        string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken = default, ReadOnlyMemory<byte> standardInput = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (standardInput.Length > 1024 * 1024) throw new ArgumentException("İşçi başlangıç verisi sınırı aşıldı.", nameof(standardInput));
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (!Path.IsPathFullyQualified(executablePath) || !File.Exists(executablePath))
            throw new ArgumentException("İşçi çalıştırılabilir dosyası tam ve mevcut bir yol olmalıdır.", nameof(executablePath));
        if (!Path.IsPathFullyQualified(workingDirectory) || !Directory.Exists(workingDirectory))
            throw new ArgumentException("İşçi çalışma dizini mevcut bir tam yol olmalıdır.", nameof(workingDirectory));
        var start = new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("İşçi başlatılamadı.");
        int processId = process.Id;
        // Force retention of the launched process handle. No later PID/name lookup or unrelated process termination.
        _ = process.SafeHandle;
        using var drainCancellation = new CancellationTokenSource();
        Task<long> output = DrainAsync(process.StandardOutput, drainCancellation.Token);
        Task<long> error = DrainAsync(process.StandardError, drainCancellation.Token);
        using var deadline = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        Task input = SendInputAsync(process, standardInput, linked.Token);
        string outcome;
        bool terminated;
        try
        {
            await Task.WhenAll(input, process.WaitForExitAsync(linked.Token)).WaitAsync(linked.Token);
            terminated = true;
            outcome = process.ExitCode == 0 ? "completed" : "failed";
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            outcome = cancellationToken.IsCancellationRequested ? "cancelled" : "timed_out";
            terminated = await TerminateOwnedAsync(process);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // A broken bootstrap pipe is a failed launch, even if the child happened to exit zero.
            outcome = "failed";
            linked.Cancel();
            terminated = await TerminateOwnedAsync(process);
        }
        linked.Cancel();
        try { await input.WaitAsync(TimeSpan.FromSeconds(1)); }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException or InvalidOperationException) { }
        // A child inheriting stdout must not keep the controller blocked after the worker exits.
        drainCancellation.CancelAfter(TimeSpan.FromSeconds(1));
        long stdout = await output;
        long stderr = await error;
        return new(outcome, processId, terminated ? process.ExitCode : null, terminated, stdout, stderr);
    }

    private static async Task SendInputAsync(Process process, ReadOnlyMemory<byte> input, CancellationToken token)
    {
        try
        {
            if (!input.IsEmpty) await process.StandardInput.BaseStream.WriteAsync(input, token);
        }
        finally { process.StandardInput.Close(); }
    }

    private static async Task<bool> TerminateOwnedAsync(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await process.WaitForExitAsync(cleanup.Token); return true; }
        catch (OperationCanceledException) { return false; }
    }

    private static async Task<long> DrainAsync(StreamReader reader, CancellationToken ct)
    {
        char[] buffer = new char[4096]; long total = 0;
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) != 0) total += count;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (IOException) { }
        // Contents are deliberately never retained in diagnostics: workers may handle private mail.
        return total;
    }
}
