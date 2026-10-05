namespace BitigMail.LocalHost.Security;

public class SecurityConfig
{
    public const string RequiredOrigin = "http://127.0.0.1:5173";
    public const string SessionHeaderName = "X-BitigMail-Session";
    public string ExpectedHost { get; set; } = "127.0.0.1:6174";
    public string AllowedOrigin { get; private init; } = RequiredOrigin;
    public bool DevelopmentAnonymousSession { get; set; }

    public static SecurityConfig ForDesktop(int port)
    {
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        string host = $"127.0.0.1:{port}";
        return new SecurityConfig { ExpectedHost = host, AllowedOrigin = $"http://{host}", DevelopmentAnonymousSession = false };
    }
}
