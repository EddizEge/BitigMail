using System.Diagnostics;

namespace BitigMail.LocalHost.Security;

/// <summary>Hold across catalog read, validation and atomic publication. Not an OS boundary against the same Windows user.</summary>
public sealed class ProfileWriteLease : IDisposable, IAsyncDisposable
{
    public const string LockFileName = ".bitigmail-catalog-write.lock";
    private FileStream? _stream;
    private ProfileWriteLease(FileStream stream) => _stream = stream;

    public static async Task<ProfileWriteLease> AcquireAsync(string profileDirectory,
        CancellationToken cancellationToken = default, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        TimeSpan budget = timeout ?? TimeSpan.FromSeconds(5);
        if (budget <= TimeSpan.Zero || budget > TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(timeout));
        string root = Path.GetFullPath(profileDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        RejectExistingLinks(root);
        Directory.CreateDirectory(root);
        string lockPath = Path.Combine(root, LockFileName);
        long started = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(started) >= budget)
                throw new TimeoutException("Kullanıcı kataloğu başka bir işlem tarafından kullanılıyor; işlem tamamlanınca yeniden deneyin.");
            RejectExistingLinks(lockPath);
            try
            {
                return new(new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1));
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
            {
                TimeSpan remaining = budget - Stopwatch.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero) throw new TimeoutException("Kullanıcı kataloğu başka bir işlem tarafından kullanılıyor; işlem tamamlanınca yeniden deneyin.");
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, Math.Min(25, remaining.TotalMilliseconds))), cancellationToken);
            }
        }
    }

    private static void RejectExistingLinks(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Katalog yolu bağlantı içermemelidir.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    public void Dispose() => Interlocked.Exchange(ref _stream, null)?.Dispose();
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
