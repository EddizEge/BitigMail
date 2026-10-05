using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MimeKit;
using MimeKit.Text;

namespace BitigMail.Engine.Archive;

public sealed class ParsedArchiveMessage
{
    public required string Subject { get; init; }
    public required string SenderDisplay { get; init; }
    public required string SenderAddress { get; init; }
    public required string RecipientsDisplay { get; init; }
    public required string BodyText { get; init; }
    public bool IsBodyTruncated { get; init; }
    public DateTimeOffset? DateUtc { get; init; }
    public string? MessageIdHeader { get; init; }
    public List<ArchiveAttachmentInfo> Attachments { get; init; } = new();
    public bool HasAttachments => Attachments.Count > 0;
    public long RawSizeBytes { get; init; }
    public required string Sha256 { get; init; }
}

public static class ArchiveMimeParser
{
    public const long MaxRawMessageSizeBytes = 64 * 1024 * 1024; // 64 MiB
    public const int MaxBodySizeBytes = 512 * 1024; // 512 KiB

    public static ParsedArchiveMessage Parse(Stream stream, long streamLength = 0)
    {
        if (streamLength > MaxRawMessageSizeBytes)
        {
            throw new InvalidOperationException($"[GÜVENLİK ENGELİ] İleti boyutu izin verilen tavanı (64 MiB) aşıyor: {streamLength} bayt.");
        }

        using var memoryStream = new MemoryStream();
        byte[] buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            totalRead += bytesRead;
            if (totalRead > MaxRawMessageSizeBytes)
            {
                throw new InvalidOperationException($"[GÜVENLİK ENGELİ] İleti akışı izin verilen tavanı (64 MiB) aşıyor: {totalRead} bayt okundu.");
            }
            memoryStream.Write(buffer, 0, bytesRead);
        }

        byte[] rawBytes = memoryStream.ToArray();
        string sha256 = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();

        memoryStream.Position = 0;
        var message = MimeMessage.Load(memoryStream);

        return ExtractParsedMessage(message, rawBytes.Length, sha256);
    }

    public static ParsedArchiveMessage ParseBytes(byte[] rawBytes)
    {
        if (rawBytes.LongLength > MaxRawMessageSizeBytes)
        {
            throw new InvalidOperationException($"[GÜVENLİK ENGELİ] İleti boyutu izin verilen tavanı (64 MiB) aşıyor: {rawBytes.LongLength} bayt.");
        }

        string sha256 = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();

        using var memoryStream = new MemoryStream(rawBytes, writable: false);
        var message = MimeMessage.Load(memoryStream);

        return ExtractParsedMessage(message, rawBytes.Length, sha256);
    }

    private static ParsedArchiveMessage ExtractParsedMessage(MimeMessage message, long rawSizeBytes, string sha256)
    {
        // 1. Subject
        string subject = message.Subject ?? string.Empty;

        // 2. Sender
        string senderDisplay = string.Empty;
        string senderAddress = string.Empty;
        if (message.From.Count > 0)
        {
            var firstFrom = message.From[0];
            if (firstFrom is MailboxAddress mb)
            {
                senderDisplay = !string.IsNullOrWhiteSpace(mb.Name) ? mb.Name : mb.Address;
                senderAddress = mb.Address ?? string.Empty;
            }
            else
            {
                senderDisplay = firstFrom.Name ?? firstFrom.ToString();
                senderAddress = firstFrom.ToString();
            }
        }

        // 3. Recipients (To, Cc, Bcc)
        var recipients = new List<string>();
        foreach (var r in message.To.Concat(message.Cc).Concat(message.Bcc))
        {
            if (r is MailboxAddress mb)
            {
                if (!string.IsNullOrWhiteSpace(mb.Name))
                    recipients.Add($"{mb.Name} <{mb.Address}>");
                else
                    recipients.Add(mb.Address);
            }
            else
            {
                recipients.Add(r.ToString());
            }
        }
        string recipientsDisplay = string.Join(", ", recipients);

        // 4. Attachments (including named inline parts, e.g. logo.png)
        var attachments = new List<ArchiveAttachmentInfo>();
        var attachmentParts = new HashSet<MimeEntity>();

        foreach (var entity in message.BodyParts)
        {
            bool isExplicitAttachment = entity.IsAttachment ||
                (entity.ContentDisposition != null &&
                 string.Equals(entity.ContentDisposition.Disposition, ContentDisposition.Attachment, StringComparison.OrdinalIgnoreCase));

            string? fileName = entity.ContentDisposition?.FileName ?? (entity as MimePart)?.FileName;
            bool hasFileName = !string.IsNullOrWhiteSpace(fileName);

            if (isExplicitAttachment || hasFileName)
            {
                attachmentParts.Add(entity);

                long? sizeBytes = null;
                if (entity is MimePart mimePart && mimePart.Content != null)
                {
                    try
                    {
                        using var ms = new MemoryStream();
                        mimePart.Content.DecodeTo(ms);
                        sizeBytes = ms.Length;
                    }
                    catch
                    {
                        sizeBytes = null;
                    }
                }

                bool isInline = entity.ContentDisposition != null &&
                    string.Equals(entity.ContentDisposition.Disposition, ContentDisposition.Inline, StringComparison.OrdinalIgnoreCase);

                attachments.Add(new ArchiveAttachmentInfo
                {
                    FileName = fileName ?? "adsiz_ek",
                    ContentType = entity.ContentType?.MimeType ?? "application/octet-stream",
                    SizeBytes = sizeBytes,
                    IsInline = isInline,
                    ContentId = entity.ContentId
                });
            }
        }

        // 5. Body Extraction
        // Prefer TextBody; if empty, fallback to HtmlBody safely tokenized
        string rawBody = string.Empty;
        if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            rawBody = message.TextBody;
        }
        else if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            rawBody = ExtractTextFromHtml(message.HtmlBody);
        }

        // 6. UTF-8 Scalar-Safe Truncation
        var (bodyText, isBodyTruncated) = TruncateUtf8Scalars(rawBody, MaxBodySizeBytes);

        // 7. Date
        DateTimeOffset? dateUtc = null;
        if (message.Date != DateTimeOffset.MinValue)
        {
            dateUtc = message.Date.ToUniversalTime();
        }

        return new ParsedArchiveMessage
        {
            Subject = subject,
            SenderDisplay = senderDisplay,
            SenderAddress = senderAddress,
            RecipientsDisplay = recipientsDisplay,
            BodyText = bodyText,
            IsBodyTruncated = isBodyTruncated,
            DateUtc = dateUtc,
            MessageIdHeader = message.MessageId,
            Attachments = attachments,
            RawSizeBytes = rawSizeBytes,
            Sha256 = sha256
        };
    }

    /// <summary>
    /// Safely extracts visible text from HTML using MimeKit.Text.HtmlTokenizer only.
    /// Excludes script, style, and head elements. Preserves block linebreaks.
    /// </summary>
    public static string ExtractTextFromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        using var reader = new StringReader(html);
        var tokenizer = new HtmlTokenizer(reader)
        {
            DecodeCharacterReferences = true
        };

        var sb = new StringBuilder();
        int ignoredDepth = 0;
        bool needSpace = false;

        while (tokenizer.ReadNextToken(out var token))
        {
            if (token.Kind == HtmlTokenKind.Tag)
            {
                var tag = (HtmlTagToken)token;
                if (tag.Id is HtmlTagId.Script or HtmlTagId.Style or HtmlTagId.Head)
                {
                    if (!tag.IsEndTag && !tag.IsEmptyElement)
                    {
                        ignoredDepth++;
                    }
                    else if (tag.IsEndTag && ignoredDepth > 0)
                    {
                        ignoredDepth--;
                    }
                }
                else if (ignoredDepth == 0)
                {
                    // Block elements produce line breaks
                    if (tag.Id is HtmlTagId.Br or HtmlTagId.P or HtmlTagId.Div or HtmlTagId.TR or HtmlTagId.LI or
                                 HtmlTagId.H1 or HtmlTagId.H2 or HtmlTagId.H3 or HtmlTagId.H4 or HtmlTagId.H5 or HtmlTagId.H6 or
                                 HtmlTagId.HR or HtmlTagId.BlockQuote or HtmlTagId.Table)
                    {
                        if (sb.Length > 0 && sb[^1] != '\n')
                        {
                            sb.AppendLine();
                        }
                        needSpace = false;
                    }
                    else
                    {
                        needSpace = true;
                    }
                }
            }
            else if (token.Kind == HtmlTokenKind.Data)
            {
                if (ignoredDepth == 0)
                {
                    var data = (HtmlDataToken)token;
                    string text = data.Data;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        if (needSpace && sb.Length > 0 && !char.IsWhiteSpace(sb[^1]))
                        {
                            sb.Append(' ');
                        }
                        sb.Append(text.Trim());
                        needSpace = true;
                    }
                }
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Truncates string so its UTF-8 encoding does not exceed maxBytes.
    /// Operates strictly on Unicode scalar boundaries (System.Text.Rune) to avoid breaking surrogate pairs.
    /// </summary>
    public static (string text, bool isTruncated) TruncateUtf8Scalars(string? input, int maxBytes)
    {
        if (string.IsNullOrEmpty(input))
            return (string.Empty, false);

        int totalBytes = Encoding.UTF8.GetByteCount(input);
        if (totalBytes <= maxBytes)
            return (input, false);

        var sb = new StringBuilder();
        int currentBytes = 0;

        foreach (var rune in input.EnumerateRunes())
        {
            int runeBytes = rune.Utf8SequenceLength;
            if (currentBytes + runeBytes > maxBytes)
            {
                return (sb.ToString(), true);
            }

            sb.Append(rune.ToString());
            currentBytes += runeBytes;
        }

        return (sb.ToString(), true);
    }
}
