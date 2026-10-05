using System.Collections.Generic;

namespace BitigMail.Engine.Models;

public class SelectionPreviewResult
{
    public string? FolderMappingFingerprint { get; set; }
    public string DuplicatePolicy { get; set; } = "PreservePhysical";
    public List<string> SkippedDuplicateItemIds { get; set; } = new();
    public string? ExecutionPolicyFingerprint { get; set; }
    public string SelectionId { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public ConversionSelectionFilter Filters { get; set; } = new();
    public int TotalSourceMessages { get; set; }
    public int TotalCount
    {
        get => TotalSourceMessages;
        set => TotalSourceMessages = value;
    }
    public int SelectedMessagesCount { get; set; }
    public int SelectedCount
    {
        get => SelectedMessagesCount;
        set => SelectedMessagesCount = value;
    }
    public int ExcludedMessagesCount { get; set; }
    public int ExcludedCount
    {
        get => ExcludedMessagesCount;
        set => ExcludedMessagesCount = value;
    }
    public int SelectedAttachmentsCount { get; set; }
    public int MissingDateExcludedCount { get; set; }
    public int TotalFoldersCount { get; set; }
    public int SelectedFoldersCount { get; set; }
    public bool CanConvert { get; set; }
    public string? BlockerReason { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? BlockReason => BlockerReason;
    public List<FolderSelectionSummary> FolderBreakdown { get; set; } = new();
    public bool DateFilterBlocked { get; set; }
    public List<string> QualificationWarnings { get; set; } = new();
    public string SdkQualification { get; set; } = "LICENSED_OUTPUT_ACCEPTANCE_PENDING";
    public string? AdvancedFilterFingerprint { get; set; }
    public int AdvancedFilterUnknownCount { get; set; }
}

public class FolderSelectionSummary
{
    public string FolderId { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int TotalItems { get; set; }
    public int SelectedItems { get; set; }
    public bool IsSelected { get; set; }
}
