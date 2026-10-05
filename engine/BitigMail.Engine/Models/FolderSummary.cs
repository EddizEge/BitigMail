namespace BitigMail.Engine.Models;

public class FolderSummary
{
    public string FolderId { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public int SubFolderCount { get; set; }
    public string Category { get; set; } = "Empty"; // "Active", "Empty", "System"
    public bool IsIpmFolder { get; set; }
}
