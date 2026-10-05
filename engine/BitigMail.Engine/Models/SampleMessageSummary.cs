namespace BitigMail.Engine.Models;

public class SampleMessageSummary
{
    public string EntryId { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string DisplayTo { get; set; } = string.Empty;
    public string DateUtc { get; set; } = string.Empty;
    public bool HasAttachments { get; set; }
    public int AttachmentCount { get; set; }
}
