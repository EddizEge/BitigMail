namespace BitigMail.Engine.Models;

public class OutlookStorageFormatInfo
{
    public bool IsValidOutlookStorage { get; set; }
    public bool IsOstSignature { get; set; }
    public bool IsPstSignature { get; set; }
    public string FormatName { get; set; } = string.Empty;
    public string MagicHex { get; set; } = string.Empty;
    public ushort ClientMagic { get; set; }
    public string ClientMagicHex { get; set; } = string.Empty;
    public string ClientMagicDescription { get; set; } = string.Empty;
    public ushort Version { get; set; }
    public string VersionHex { get; set; } = string.Empty;
    public string VersionDescription { get; set; } = string.Empty;
    public string AuthoritativeRuntimeFormat { get; set; } = string.Empty;
    public string AuthoritativeRuntimeValidation { get; set; } = string.Empty;
}
