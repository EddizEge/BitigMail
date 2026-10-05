using BitigMail.Engine.Archive;
using BitigMail.Engine.Models;
using MimeKit;

namespace BitigMail.Engine.Storage;

/// <summary>Evaluate complete raw MIME, never display/index snippets. Date is the explicit-zone MIME Date.</summary>
public static class MimeAdvancedFilterAdapter
{
    public static MailFilterMatch Evaluate(CompiledMailFilter filter, MimeMessage message, long rawByteLength)
    {
        string body = filter.UsesBody ? message.TextBody ?? ArchiveMimeParser.ExtractTextFromHtml(message.HtmlBody) : "";
        var names = message.Attachments.Select(a => a.ContentDisposition?.FileName ?? a.ContentType.Name ?? "").ToArray();
        DateTime? date = MimeFidelityPolicy.OriginalDate(message);
        return filter.Evaluate(new(message.Subject ?? "", body,
            message.From.Mailboxes.Select(a => a.Address).ToArray(),
            message.To.Mailboxes.Concat(message.Cc.Mailboxes).Concat(message.Bcc.Mailboxes).Select(a => a.Address).ToArray(),
            date.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(date.Value, DateTimeKind.Utc)) : null,
            rawByteLength, names.Length > 0, names));
    }
}
