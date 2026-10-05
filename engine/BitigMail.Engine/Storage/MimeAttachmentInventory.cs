using MimeKit;

namespace BitigMail.Engine.Storage;

public static class MimeAttachmentInventory
{
    public static int CountTopLevelAttachments(MimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Count(message.Body);
    }
    private static int Count(MimeEntity? entity)
    {
        if (entity is null) return 0;
        if (entity is MessagePart) return 1; // Attached message is one item; do not descend into its message body.
        if (entity is MimePart part) return !string.IsNullOrEmpty(part.FileName) || part.IsAttachment || !string.IsNullOrEmpty(part.ContentId) ? 1 : 0;
        if (entity is Multipart multipart) return multipart.Sum(Count);
        return 0;
    }
}
