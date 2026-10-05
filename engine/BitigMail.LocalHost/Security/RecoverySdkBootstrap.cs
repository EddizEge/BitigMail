using System.Security.Cryptography;
namespace BitigMail.LocalHost.Security;
public sealed class RecoverySdkBootstrap:IDisposable
{
    private byte[] _snapshot;
    private readonly object _gate = new();
    private bool _disposed;
    public RecoverySdkBootstrap(byte[]? licenseBytes)
    {
        if (licenseBytes?.Length > AsposeLicenseConfigurationStore.MaxPlainBytes) throw new InvalidDataException("Lisans başlangıç verisi sınırı aşıldı.");
        _snapshot = licenseBytes?.ToArray() ?? [];
    }
    public byte[] Copy() { lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); return _snapshot.ToArray(); } }
    public void Dispose() { lock (_gate) { CryptographicOperations.ZeroMemory(_snapshot); _snapshot = []; _disposed = true; } }

    public static (BitigMail.Engine.Storage.AsposeSdkStartupService Service, RecoverySdkBootstrap Bootstrap)
        CreateAtStartup(AsposeLicenseConfigurationStore store, string? explicitOverride)
    {
        byte[]? bytes;
        try
        {
            bytes = string.IsNullOrWhiteSpace(explicitOverride) ? store.Load()
                : AsposeLicenseConfigurationStore.ReadSelectedLicense(explicitOverride);
            if (bytes is { Length: 0 }) throw new InvalidDataException("Boş lisans yapılandırması.");
        }
        catch { bytes = [0]; } // Deliberately invalid SDK input preserves configuration_error.
        var snapshot = new RecoverySdkBootstrap(bytes);
        try { return (new BitigMail.Engine.Storage.AsposeSdkStartupService(bytes), snapshot); }
        finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
    }
}
