using System;
using Aspose.Email.Mapi;

namespace BitigMail.Engine.Storage;

public static class AttachmentHelper
{
    /// <summary>
    /// Reads inline Content-ID from MAPI attachment property bag.
    /// In Aspose.Email, MapiAttachment does not expose a ContentId CLR property.
    /// The stored property is retrieved from MapiAttachment.Properties using
    /// PR_ATTACH_CONTENT_ID_W (0x3712001F) first, falling back to PR_ATTACH_CONTENT_ID_A (0x3712001E).
    /// Returns the exact raw stored string; never synthesizes or alters metadata.
    /// </summary>
    public static string? GetAttachmentContentId(MapiAttachment att)
    {
        if (att?.Properties == null) return null;

        try
        {
            // PR_ATTACH_CONTENT_ID_W = 0x3712001F (Unicode)
            if (att.Properties.TryGetValue(MapiPropertyTag.PR_ATTACH_CONTENT_ID_W, out var propW) && propW != null)
            {
                string val = propW.GetString();
                if (!string.IsNullOrEmpty(val)) return val;
            }

            // PR_ATTACH_CONTENT_ID_A = 0x3712001E (ANSI fallback)
            if (att.Properties.TryGetValue(MapiPropertyTag.PR_ATTACH_CONTENT_ID_A, out var propA) && propA != null)
            {
                string val = propA.GetString();
                if (!string.IsNullOrEmpty(val)) return val;
            }
        }
        catch
        {
            // Ignore extraction errors and return null
        }

        return null;
    }
}
