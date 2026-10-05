using System;
using System.IO;
using System.Text;
using Aspose.Email;
using Aspose.Email.Mapi;
using MimeKit;

namespace BitigMail.Engine.Storage;

public class MimeVendorCalibration
{
    public string SubjectSuffix { get; init; } = string.Empty;
    public string PlainPrefix { get; init; } = string.Empty;
    public string MultipartPlainPrefix { get; init; } = string.Empty;
    public string HtmlNotice { get; init; } = string.Empty;
    public string HtmlPrefix { get; init; } = string.Empty;
    public string HtmlSuffix { get; init; } = string.Empty;
    public string MultipartHtmlPrefix { get; init; } = string.Empty;
    public string MultipartHtmlSuffix { get; init; } = string.Empty;
    public bool IsCalibrated { get; init; }

    public static string Normalize(string? v) => (v ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
}

public static class MimeVendorCalibrator
{
    public static MimeVendorCalibration GetCalibration() => MimeFidelityPolicy.GetCalibration();
}
