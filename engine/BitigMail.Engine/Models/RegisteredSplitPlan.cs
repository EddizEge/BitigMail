using System;
using System.Collections.Generic;

namespace BitigMail.Engine.Models;

public class RegisteredSplitPlan
{
    public string PlanId { get; set; } = string.Empty;
    public string SourceHandle { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public string? SelectionId { get; set; }
    public RegisteredSelection? Selection { get; set; }
    public string SplitMode { get; set; } = "year";
    public long? SizeCapBytes { get; set; }
    public SplitOptions Options { get; set; } = new();
    public List<SplitYearGroupPreview>? YearGroups { get; set; }
    public bool CanSplit { get; set; } = true;
    public string? BlockerReason { get; set; }
    public long? EstimatedRequiredBytes { get; set; }
    public int? EstimatedPartCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
