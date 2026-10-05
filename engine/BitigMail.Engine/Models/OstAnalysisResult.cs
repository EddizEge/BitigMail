namespace BitigMail.Engine.Models;

public class OstAnalysisResult
{
    public string SourceFileName { get; set; } = string.Empty;
    public long SourceSizeBytes { get; set; }
    public string SourceSha256 { get; set; } = string.Empty;
    public OutlookStorageFormatInfo FormatInfo { get; set; } = new();
    public int TotalFolders { get; set; }
    public int ActiveFoldersCount { get; set; }
    public int EmptyFoldersCount { get; set; }
    public int SystemFoldersCount { get; set; }
    public int TotalItems { get; set; }
    public int PhysicalTotalItems { get; set; }
    public int TotalAttachments { get; set; }
    public List<FolderSummary> Folders { get; set; } = new();
    public List<SampleMessageSummary> SampleMessages { get; set; } = new();
    public PreflightCheckResult Preflight { get; set; } = new();
}
