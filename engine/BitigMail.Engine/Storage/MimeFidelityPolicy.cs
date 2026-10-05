using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Email;
using MimeKit;

namespace BitigMail.Engine.Storage;

// Source content is never searched for a vendor marker. Qualification compares
// entire strings against additions measured using synthetic, task-owned canaries.
internal static class MimeFidelityPolicy
{
    private static readonly ConcurrentDictionary<string, Lazy<MimeVendorCalibration>> Profiles = new();
    public static MimeVendorCalibration GetCalibration() => Profiles.GetOrAdd(
        CultureInfo.CurrentCulture.Name + "/" + CultureInfo.CurrentUICulture.Name,
        _ => new Lazy<MimeVendorCalibration>(Calibrate)).Value;

    private static MimeVendorCalibration Calibrate()
    {
        string token = "BITIG_CANARY_" + Guid.NewGuid().ToString("N");
        string head = "From: probe@test.local\r\nTo: probe@test.local\r\nSubject: " + token +
            "\r\nDate: Mon, 01 Jan 2024 00:00:00 +0300\r\nMIME-Version: 1.0\r\n";
        MailMessage Read(string raw) => MailMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(raw)), new EmlLoadOptions());
        string N(string text) => MimeVendorCalibration.Normalize(text);
        string PlainPrefix(string actual)
        {
            if (!actual.EndsWith(token, StringComparison.Ordinal)) throw new InvalidOperationException("SDK kalibrasyonu özgün düz metni değiştirdi.");
            return actual[..^token.Length];
        }
        using var plain = Read(head + "Content-Type: text/plain; charset=utf-8\r\n\r\n" + token);
        if (!plain.Subject.StartsWith(token, StringComparison.Ordinal)) throw new InvalidOperationException("SDK konu kalibrasyonu başarısız.");
        string node = "<p>" + token + "</p>";
        using var fragment = Read(head + "Content-Type: text/html; charset=utf-8\r\n\r\n" + node);
        string full = "<html><body>" + node + "</body></html>";
        using var document = Read(head + "Content-Type: text/html; charset=utf-8\r\n\r\n" + full);
        string actual = N(document.HtmlBody);
        int nodeIndex = actual.IndexOf(node, StringComparison.Ordinal);
        const string opening = "<html><body>";
        const string closing = "</body></html>";
        if (nodeIndex < opening.Length || !actual.StartsWith(opening, StringComparison.Ordinal) ||
            actual != actual[..nodeIndex] + node + closing) throw new InvalidOperationException("SDK HTML kalibrasyonu bilinmeyen değişiklik içeriyor.");
        string notice = actual[opening.Length..nodeIndex];
        string fragmentPrefix = opening + notice;
        if (N(fragment.HtmlBody) != fragmentPrefix + node + closing) throw new InvalidOperationException("SDK HTML parçası kalibrasyonu başarısız.");
        using var multi = Read(head + "Content-Type: multipart/alternative; boundary=\"bitig-canary\"\r\n\r\n" +
            "--bitig-canary\r\nContent-Type: text/plain; charset=utf-8\r\n\r\n" + token +
            "\r\n--bitig-canary\r\nContent-Type: text/html; charset=utf-8\r\n\r\n" + node + "\r\n--bitig-canary--\r\n");
        if (N(multi.HtmlBody) != fragmentPrefix + node + closing) throw new InvalidOperationException("SDK çok parçalı HTML kalibrasyonu başarısız.");
        return new MimeVendorCalibration {
            SubjectSuffix = plain.Subject[token.Length..], PlainPrefix = PlainPrefix(N(plain.Body)),
            MultipartPlainPrefix = PlainPrefix(N(multi.Body)), HtmlNotice = notice,
            HtmlPrefix = fragmentPrefix, HtmlSuffix = closing,
            MultipartHtmlPrefix = fragmentPrefix, MultipartHtmlSuffix = closing, IsCalibrated = true
        };
    }

    public static string QualifyPlain(string source, string observed, MimeVendorCalibration profile, out bool restoredLf)
    {
        foreach (string prefix in new[] { "", profile.PlainPrefix, profile.MultipartPlainPrefix }.Distinct(StringComparer.Ordinal))
        {
            if (observed == prefix + source) { restoredLf = false; return prefix + source; }
            if (source.EndsWith('\n') && observed == prefix + source[..^1]) { restoredLf = true; return prefix + source; }
        }
        throw new InvalidOperationException("[BÜTÜNLÜK ENGELİ] SDK düz metni kalibre edilmiş ekler dışında değiştirdi.");
    }

    public static string QualifyHtml(string source, string observed, MimeVendorCalibration profile, out bool restoredLf)
    {
        // The insertion positions come from source markup; only complete equality
        // with a measured notice insertion is accepted. No notice is removed.
        var positions = Regex.Matches(source, @"<body\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(m => m.Index + m.Length).ToArray();
        var transforms = new List<Func<string, string>> { s => s, s => profile.HtmlPrefix + s + profile.HtmlSuffix };
        foreach (int pos in positions) transforms.Add(s => s.Insert(Math.Min(pos, s.Length), profile.HtmlNotice));
        foreach (var transform in transforms)
        {
            string expected = transform(source);
            if (observed == expected) { restoredLf = false; return expected; }
            if (source.EndsWith('\n') && observed == transform(source[..^1])) { restoredLf = true; return expected; }
        }
        throw new InvalidOperationException("[BÜTÜNLÜK ENGELİ] SDK HTML içeriğini kalibre edilmiş ekler dışında değiştirdi.");
    }

    public static DateTime? OriginalDate(MimeMessage message)
    {
        string? raw = message.Headers[HeaderId.Date];
        if (!MimeSourceInspector.HasExplicitTimeZone(raw)) return null;
        if (!MimeKit.Utils.DateUtils.TryParse(raw, out var parsed)) return null;
        if (parsed.UtcDateTime.Year <= 1601) throw new InvalidOperationException("Kaynak tarihi PST tarih aralığında gösterilemiyor.");
        return parsed.UtcDateTime;
    }
}
