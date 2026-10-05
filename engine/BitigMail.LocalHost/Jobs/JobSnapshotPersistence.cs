using System;
using System.IO;
using System.Text;
using System.Threading;

namespace BitigMail.LocalHost.Jobs;

/// <summary>Durable job/report snapshots; never remove the previous snapshot before publication.</summary>
internal static class JobSnapshotPersistence
{
    private static readonly int[] RetryDelayMilliseconds = { 50, 100, 200, 400, 800 };

    internal static void Write(string destination, string json, Action<int>? onRetry = null)
    {
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            Publish(temporary, destination, overwrite: true, onRetry);
        }
        finally
        {
            try { File.Delete(temporary); } catch { /* Do not mask the publication failure. */ }
        }
    }

    // Caller owns the already-flushed temporary file and its cleanup. Preserve create-only
    // publication for immutable plans; a retry never changes the overwrite policy.
    internal static void Publish(string temporary, string destination, bool overwrite, Action<int>? onRetry = null)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temporary, destination, overwrite);
                return;
            }
            catch (Exception error) when (attempt < RetryDelayMilliseconds.Length && IsWindowsSharingFailure(error))
            {
                onRetry?.Invoke(attempt + 1);
                Thread.Sleep(RetryDelayMilliseconds[attempt]);
            }
        }
    }

    private static bool IsWindowsSharingFailure(Exception error)
    {
        if (!OperatingSystem.IsWindows() || error is not (IOException or UnauthorizedAccessException)) return false;
        return (error.HResult & 0xffff) is 5 or 32 or 33;
    }
}
