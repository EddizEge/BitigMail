namespace BitigMail.Engine.Distribution;

/// <summary>
/// WebView2 user data folder of the desktop shell. The default desktop profile keeps the folder the installed
/// application has always used (existing data is neither moved nor lost); any other profile, e.g. a Temp test
/// profile, gets its own folder under the profile so it never shares the user's browser data.
/// </summary>
public static class DesktopWebViewDataFolder
{
    public const string FolderName = "WebView2";

    public static string DefaultProfile(string localApplicationData) =>
        Path.Combine(RequireRoot(localApplicationData), "BitigMail", "desktop");

    public static string Resolve(string profile, string localApplicationData)
    {
        if (string.IsNullOrWhiteSpace(profile)) throw new ArgumentException("Profil klasörü gereklidir.", nameof(profile));
        string root = RequireRoot(localApplicationData);
        return SamePath(profile, DefaultProfile(root))
            ? Path.Combine(root, "BitigMail", FolderName)
            : Path.Combine(Normalize(profile), FolderName);
    }

    static string RequireRoot(string localApplicationData)
    {
        if (string.IsNullOrWhiteSpace(localApplicationData) || !Path.IsPathFullyQualified(localApplicationData))
            throw new ArgumentException("Yerel uygulama verisi klasörü tam yol olmalıdır.", nameof(localApplicationData));
        return localApplicationData;
    }

    static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    static bool SamePath(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
}
