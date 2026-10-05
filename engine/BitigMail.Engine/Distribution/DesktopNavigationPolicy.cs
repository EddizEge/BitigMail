namespace BitigMail.Engine.Distribution;

/// <summary>Decisions only. The native shell must enforce these on navigation, popups and native-message dispatch.</summary>
public sealed class DesktopNavigationPolicy
{
    public Uri ApplicationOrigin { get; }

    public DesktopNavigationPolicy(Uri applicationOrigin)
    {
        if (!applicationOrigin.IsAbsoluteUri || applicationOrigin.Scheme != Uri.UriSchemeHttp ||
            applicationOrigin.Host != "127.0.0.1" || applicationOrigin.Port is < 1024 or > 65535 ||
            applicationOrigin.UserInfo.Length != 0 || applicationOrigin.AbsolutePath != "/" ||
            applicationOrigin.Query.Length != 0 || applicationOrigin.Fragment.Length != 0 ||
            applicationOrigin.OriginalString.Contains('\\'))
            throw new ArgumentException("Masaüstü uygulama adresi tam yerel HTTP adresi olmalıdır.", nameof(applicationOrigin));
        ApplicationOrigin = applicationOrigin;
    }

    public bool IsTrustedDocument(string? address)
    {
        if (!TryStrictUri(address, out var uri)) return false;
        return uri!.Scheme == ApplicationOrigin.Scheme && uri.Host == ApplicationOrigin.Host &&
            uri.Port == ApplicationOrigin.Port && uri.Query.Length == 0 &&
            uri.AbsolutePath is "/" or "/index.html";
    }

    public bool MayOpenInSystemBrowser(string? address, bool userInitiated)
    {
        if (!userInitiated || !TryStrictUri(address, out var uri)) return false;
        return uri!.Scheme is "http" or "https" && !uri.IsLoopback;
    }

    private static bool TryStrictUri(string? address, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(address) || address.Length > 8192 || address != address.Trim() ||
            address.Any(char.IsControl) || address.Contains('\\')) return false;
        return Uri.TryCreate(address, UriKind.Absolute, out uri) && uri.UserInfo.Length == 0 &&
            !string.IsNullOrEmpty(uri.Host) && uri.Scheme is "http" or "https";
    }
}
