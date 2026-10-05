namespace BitigMail.Engine.Distribution;

/// <summary>The owned engine holds this for its full lifetime, including shutdown drain.</summary>
public sealed class DesktopInstanceLease : IDisposable
{
    public const string FileName = ".bitigmail-desktop-instance.lock";
    private FileStream? _stream;
    private DesktopInstanceLease(FileStream stream) => _stream = stream;

    internal string HeldProfileDirectory => Path.GetDirectoryName(
        (_stream ?? throw new ObjectDisposedException(nameof(DesktopInstanceLease))).Name)!;

    public static DesktopInstanceLease Acquire(string profileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        string root = Path.GetFullPath(profileDirectory);
        RejectLinks(root);
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, FileName);
        RejectLinks(path);
        try
        {
            return new(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
        {
            throw new InvalidOperationException("BitigMail bu veri profiliyle zaten çalışıyor. Açık pencereye dönün veya kapanmasını bekleyin.", ex);
        }
    }

    private static void RejectLinks(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Uygulama veri profili bağlantılı bir yol içeremez.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    public void Dispose() => Interlocked.Exchange(ref _stream, null)?.Dispose();
}
