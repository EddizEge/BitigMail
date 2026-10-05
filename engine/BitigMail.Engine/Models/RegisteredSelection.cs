using System;
using System.Collections.Generic;

namespace BitigMail.Engine.Models;

public class RegisteredSelection
{
    public BitigMail.Engine.Planning.MimeExecutionPolicySnapshot? ExecutionPolicy { get; set; }
    public string SelectionId { get; set; } = string.Empty;
    public string SourceHandle { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public ConversionSelectionFilter Filters { get; set; } = new();
    public HashSet<string> SelectedFolderIds { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> SelectedMessageKeys { get; set; } = new(StringComparer.Ordinal);
    public int TotalSourceMessages { get; set; }
    public int SelectedMessagesCount { get; set; }
    public int ExcludedMessagesCount { get; set; }
    public int SelectedAttachmentsCount { get; set; }
    public int MissingDateExcludedCount { get; set; }
    public bool CanConvert { get; set; } = true;
    public bool HasTrialBlocker { get; set; }
    public string? TrialBlockerReason { get; set; }
    public bool HasPreflightBlocker { get; set; }
    public string? PreflightBlockerReason { get; set; }
    public string? BlockerReason => PreflightBlockerReason ?? TrialBlockerReason;
    public string SelectionContentHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? QualificationFingerprint { get; set; }
    public bool DateFilterBlocked { get; set; }
    public bool QualificationIsPartial { get; set; }
    public List<string> QualificationWarnings { get; set; } = new();
    public string SdkQualification { get; set; } = "LICENSED_OUTPUT_ACCEPTANCE_PENDING";
    public string? AdvancedFilterCanonicalJson { get; set; }
    public string? AdvancedFilterFingerprint { get; set; }
    public int AdvancedFilterUnknownCount { get; set; }
}
