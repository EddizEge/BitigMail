using System.IO;
using MimeKit;

namespace BitigMail.Engine.Imap;

/// <summary>
/// Authoritative serialization assumptions matching MailKit APPEND behavior
/// as required by docs/IMAP_TRANSFER_WORKFLOW.md and TASK-014 Gate A.
/// </summary>
public static class ImapSerializationAssumptions
{
    /// <summary>
    /// Creates the exact FormatOptions used by MailKit during IMAP APPEND operations:
    /// DOS newlines, HiddenHeaders empty, EnsureNewLine true (MailKit CreateAppendOptions enforces this value).
    /// </summary>
    public static FormatOptions CreateAppendFormatOptions()
    {
        var options = FormatOptions.Default.Clone();
        options.NewLineFormat = NewLineFormat.Dos;
        options.HiddenHeaders.Clear();
        options.EnsureNewLine = true;
        return options;
    }

    /// <summary>
    /// Serializes a MimeMessage using the exact MailKit APPEND FormatOptions.
    /// </summary>
    public static byte[] Serialize(MimeMessage message)
    {
        var options = CreateAppendFormatOptions();
        using var stream = new MemoryStream();
        message.WriteTo(options, stream);
        return stream.ToArray();
    }
}
