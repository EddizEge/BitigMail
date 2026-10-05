using System.Collections.Generic;

namespace BitigMail.Engine.Models;

public class SplitPlanResult
{
    public string PlanId { get; set; } = string.Empty;
    public string SourceHandle { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public string SelectionId { get; set; } = string.Empty;
    public string SplitMode { get; set; } = "year";
    public long? SizeCapBytes { get; set; }
    public int TotalSourceMessages { get; set; }
    public int SelectedMessagesCount { get; set; }
    public int ExcludedMessagesCount { get; set; }
    public int SelectedAttachmentsCount { get; set; }
    public List<SplitYearGroupPreview>? YearGroups { get; set; }
    public bool CanSplit { get; set; } = true;
    public string? BlockerReason { get; set; }
    public long? EstimatedRequiredBytes { get; set; }
    public int? EstimatedPartCount { get; set; }
    public string? EstimateBasis { get; set; }
}

public class SplitYearGroupPreview
{
    public string Year { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public int AttachmentCount { get; set; }
    public string TargetFileName { get; set; } = string.Empty;
}

public class YearGroupSummary : SplitYearGroupPreview
{
}
