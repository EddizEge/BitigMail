namespace BitigMail.Engine.Models;

public class SplitPartReport
{
    public string PartFileName { get; set; } = string.Empty;
    public string PartFullPath { get; set; } = string.Empty;
    public long PartSizeBytes { get; set; }
    public string PartSha256 { get; set; } = string.Empty;
    public int ItemsWritten { get; set; }
    public int TotalAttachmentsVerified { get; set; }
    public int TotalCidVerified { get; set; }
    public string? GroupKey { get; set; }
    public ReopenedPstVerificationInfo ReopenedPstVerification { get; set; } = new();
}
