using System;
using System.Collections.Generic;

namespace BitigMail.Engine.Bridge;

public enum BridgeTransferDirection
{
    Import,
    Export
}

public enum BridgeExportFormat
{
    EmlTree,
    Mboxrd
}

public enum BridgeItemStatus
{
    Planned,
    Staged,
    AppendIntent,
    Verified,
    NeedsAttention,
    Failed
}

public sealed record BridgeFolderSummary
{
    public required string SourceFolder { get; init; }
    public required string TargetFolder { get; init; }
    public int TotalItems { get; init; }
    public int EligibleItems { get; init; }
    public int ExcludedCount { get; init; }
    public int MissingDateExcludedCount { get; init; }
    public int DeletedExcludedCount { get; init; }
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record BridgeImportPreviewRequest
{
    public BitigMail.Engine.Models.MailFilterDefinition? AdvancedFilter { get; init; }
    public string? CompanyName { get; init; }
    public string? ProjectName { get; init; }
    public required string CompanyId { get; init; }
    public required string ProjectId { get; init; }
    public required string SourceHandle { get; init; }
    public required string TargetAccountId { get; init; }
    public required List<string> SelectedFolders { get; init; }
    public Dictionary<string, string>? TargetFolderMappings { get; init; }
    public string? StartDate { get; init; }
    public string? EndDate { get; init; }
}

public sealed record BridgeImportPreviewResponse
{
    public string? AdvancedFilterFingerprint { get; init; }
    public int AdvancedFilterUnknownCount { get; init; }
    public required string PreviewId { get; init; }
    public required string CompanyId { get; init; }
    public required string ProjectId { get; init; }
    public required string SourceHandle { get; init; }
    public required string SourceFingerprint { get; init; }
    public required string SourceKind { get; init; }
    public required string TargetAccountId { get; init; }
    public long TargetAccountVersion { get; init; }
    public int TotalSourceItems { get; init; }
    public int EligibleItemsCount { get; init; }
    public int ExcludedCount { get; init; }
    public int MissingDateExcludedCount { get; init; }
    public required List<BridgeFolderSummary> Folders { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public bool CanTransfer { get; init; }
    public string? BlockerReason { get; init; }
    public bool DateFilterBlocked { get; init; }
    public List<string> QualificationWarnings { get; init; } = new();
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record BridgeExportPreviewRequest
{
    public BitigMail.Engine.Models.MailFilterDefinition? AdvancedFilter { get; init; }
    public string? CompanyName { get; init; }
    public string? ProjectName { get; init; }
    public required string CompanyId { get; init; }
    public required string ProjectId { get; init; }
    public required string SourceAccountId { get; init; }
    public required string TargetDirHandle { get; init; }
    public required string TargetFormat { get; init; }
    public required List<string> SelectedFolders { get; init; }
    public string? StartDate { get; init; }
    public string? EndDate { get; init; }
}

public sealed record BridgeExportPreviewResponse
{
    public string? AdvancedFilterFingerprint { get; init; }
    public int AdvancedFilterUnknownCount { get; init; }
    public required string PreviewId { get; init; }
    public required string CompanyId { get; init; }
    public required string ProjectId { get; init; }
    public required string SourceAccountId { get; init; }
    public long SourceAccountVersion { get; init; }
    public required string TargetDirHandle { get; init; }
    public required string TargetFormat { get; init; }
    public int TotalSourceItems { get; init; }
    public int EligibleItemsCount { get; init; }
    public int ExcludedCount { get; init; }
    public int MissingDateExcludedCount { get; init; }
    public int DeletedExcludedCount { get; init; }
    public required List<BridgeFolderSummary> Folders { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public bool CanTransfer { get; init; }
    public string? BlockerReason { get; init; }
    public long? EstimatedRequiredBytes { get; init; }
    public long? AvailableFreeBytes { get; init; }
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record BridgeStartRequest
{
    public required string PreviewId { get; init; }
    public required string IdempotencyKey { get; init; }
    public string? CompanyId { get; init; }
    public string? ProjectId { get; init; }
    public bool EnqueueIfBusy { get; init; }
}

public sealed record BridgeSourceFolderDescriptor
{
    public required string FolderName { get; init; }
    public int ItemCount { get; init; }
    public long TotalSizeBytes { get; init; }
}

public sealed record BridgeSourceDescribeRequest
{
    public required string SourceHandle { get; init; }
}

public sealed record BridgeSourceDescriptorResponse
{
    public required string SourceHandle { get; init; }
    public required string SourceKind { get; init; }
    public required string Dialect { get; init; }
    public required string DisplayPath { get; init; }
    public int TotalFiles { get; init; }
    public int TotalItems { get; init; }
    public long TotalSizeBytes { get; init; }
    public required List<BridgeSourceFolderDescriptor> Folders { get; init; }
    public int IgnoredNonEmlFilesCount { get; init; }
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record BridgeResumeRequest
{
    public string? JobId { get; init; }
    public string? CompanyId { get; init; }
    public string? ProjectId { get; init; }
    public bool EnqueueIfBusy { get; init; }
}

public sealed class BridgeImportPlannedItem
{
    public required string ItemId { get; set; }
    public required string SourceRelativePath { get; set; }
    public required string SourceCanonicalPath { get; set; }
    public required int PhysicalOrdinal { get; set; }
    public required string SourceMappedFolder { get; set; }
    public required string TargetFolder { get; set; }
    public required string SourceSha256 { get; set; }
    public required string CanonicalSha256 { get; set; }
    public bool ConvertedLfToCrLf { get; set; }
    public bool AddedTerminalNewline { get; set; }
    public DateTimeOffset? OriginalMimeDateUtc { get; set; }
    public DateTimeOffset PlannedInternalDateUtc { get; set; }
    public List<string> PlannedFlags { get; set; } = new();
    public List<string> PlannedKeywords { get; set; } = new();
}

public sealed class BridgeImportPlan
{
    public string? AdvancedFilterCanonicalJson { get; set; }
    public string? AdvancedFilterFingerprint { get; set; }
    public int AdvancedFilterUnknownCount { get; set; }
    public string? CompanyName { get; set; }
    public string? ProjectName { get; set; }
    public string? SourceDisplayName { get; set; }
    public string? TargetDisplayName { get; set; }
    public required string PlanId { get; set; }
    public required string PreviewId { get; set; }
    public required string CompanyId { get; set; }
    public required string ProjectId { get; set; }
    public required string SourceHandle { get; set; }
    public required string SourceFingerprint { get; set; }
    public required string SourceKind { get; set; }
    public string? SourceRootPath { get; set; }
    public required string TargetAccountId { get; set; }
    public required long TargetAccountVersion { get; set; }
    public List<BridgeImportPlannedItem> Items { get; set; } = new();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool CanTransfer { get; set; }
    public BridgeImportPreviewResponse? Preview { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? BlockerReason { get; set; }
    public string? QualificationFingerprint { get; set; }
    public bool DateFilterBlocked { get; set; }
    public bool QualificationIsPartial { get; set; }
    public List<string> QualificationWarnings { get; set; } = new();
    public List<string> QualificationSourcePaths { get; set; } = new();
}

public sealed class BridgeExportPlannedItem
{
    public required string ItemId { get; set; }
    public required string SourceFolder { get; set; }
    public required uint SourceUid { get; set; }
    public required uint SourceUidValidity { get; set; }
    public required string SourceSha256 { get; set; }
    public DateTimeOffset? OriginalMimeDateUtc { get; set; }
    public DateTimeOffset InternalDateUtc { get; set; }
    public List<string> Flags { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public required string FolderKey { get; set; }
    public required string RelativeOutputPath { get; set; }
}

public sealed class BridgeFolderMapping
{
    public required string FolderKey { get; set; }
    public required string OriginalFolder { get; set; }
}

public sealed class BridgeExportPlan
{
    public string? AdvancedFilterCanonicalJson { get; set; }
    public string? AdvancedFilterFingerprint { get; set; }
    public int AdvancedFilterUnknownCount { get; set; }
    public string? CompanyName { get; set; }
    public string? ProjectName { get; set; }
    public string? SourceDisplayName { get; set; }
    public string? TargetDisplayName { get; set; }
    public required string PlanId { get; set; }
    public required string PreviewId { get; set; }
    public required string CompanyId { get; set; }
    public required string ProjectId { get; set; }
    public required string SourceAccountId { get; set; }
    public required long SourceAccountVersion { get; set; }
    public required string TargetDirHandle { get; set; }
    public string? TargetDirectoryPath { get; set; }
    public required string TargetFormat { get; set; }
    public List<BridgeExportPlannedItem> Items { get; set; } = new();
    public List<BridgeFolderMapping> FolderMappings { get; set; } = new();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool CanTransfer { get; set; }
    public BridgeExportPreviewResponse? Preview { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? BlockerReason { get; set; }
    public long? EstimatedRequiredBytes { get; set; }
}

public sealed class BridgeImportJournalEntry
{
    public required string ItemId { get; set; }
    public BridgeItemStatus Status { get; set; } = BridgeItemStatus.Planned;
    public string? BitigMailKeyword { get; set; }
    public required string SourceRelativePath { get; set; }
    public required int PhysicalOrdinal { get; set; }
    public required string TargetFolder { get; set; }
    public uint? TargetUidValidity { get; set; }
    public uint? TargetUid { get; set; }
    public required string ExpectedSha256 { get; set; }
    public string? VerifiedSha256 { get; set; }
    public DateTimeOffset? TargetInternalDateUtc { get; set; }
    public List<string>? TargetFlags { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BridgeImportJournalState
{
    public required string JobId { get; set; }
    public required string PlanId { get; set; }
    public Dictionary<string, BridgeImportJournalEntry> Entries { get; set; } = new(StringComparer.Ordinal);
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BridgeExportJournalEntry
{
    public required string ItemId { get; set; }
    public BridgeItemStatus Status { get; set; } = BridgeItemStatus.Planned;
    public required string SourceFolder { get; set; }
    public required uint SourceUid { get; set; }
    public required uint SourceUidValidity { get; set; }
    public required string FolderKey { get; set; }
    public required string RelativeOutputPath { get; set; }
    public required string ExpectedSha256 { get; set; }
    public string? VerifiedSha256 { get; set; }
    public long? OutputLength { get; set; }
    public bool AddedTerminalNewline { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BridgeExportJournalState
{
    public required string JobId { get; set; }
    public required string PlanId { get; set; }
    public required string JobOutputDir { get; set; }
    public Dictionary<string, BridgeExportJournalEntry> Entries { get; set; } = new(StringComparer.Ordinal);
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BridgeExportManifestFolder
{
    public required string FolderKey { get; set; }
    public required string OriginalFolder { get; set; }
    public required string OutputTarget { get; set; }
    public int MessageCount { get; set; }
}

public sealed class BridgeExportManifestItem
{
    public required string ItemId { get; set; }
    public required string OriginalFolder { get; set; }
    public required uint SourceUid { get; set; }
    public required uint SourceUidValidity { get; set; }
    public required string SourceSha256 { get; set; }
    public long OriginalLength { get; set; }
    public required string StoredSha256 { get; set; }
    public bool AddedTerminalNewline { get; set; }
    public required string RelativeOutputPath { get; set; }
    public DateTimeOffset InternalDateUtc { get; set; }
    public DateTimeOffset? OriginalMimeDateUtc { get; set; }
    public List<string> Flags { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
}

public sealed class BridgeExportManifest
{
    public string ManifestVersion { get; set; } = "1.0";
    public required string JobId { get; set; }
    public required string PlanId { get; set; }
    public required string CompanyId { get; set; }
    public required string ProjectId { get; set; }
    public string? CompanyName { get; set; }
    public string? ProjectName { get; set; }
    public required string SourceAccountId { get; set; }
    public required long SourceAccountVersion { get; set; }
    public required string TargetFormat { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<BridgeExportManifestFolder> Folders { get; set; } = new();
    public List<BridgeExportManifestItem> Items { get; set; } = new();
    public string LimitationsNote { get; set; } = "EML and MBOX standard file formats do not encode IMAP flags, internal dates, or keywords natively. All IMAP metadata is preserved in this sidecar manifest.";
}

public sealed class BridgeAuditItem
{
    public required string ItemId { get; set; }
    public required string Status { get; set; }
    public required string SourceIdentity { get; set; }
    public required string TargetIdentity { get; set; }
    public required string SourceSha256 { get; set; }
    public string? VerifiedSha256 { get; set; }
    public DateTimeOffset? OriginalMimeDateUtc { get; set; }
    public DateTimeOffset? InternalDateUtc { get; set; }
    public string? BitigMailKeyword { get; set; }
    public bool ConvertedLfToCrLf { get; set; }
    public bool AddedTerminalNewline { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class BridgeTransferReportDetail
{
    public string? AdvancedFilterCanonicalJson { get; set; }
    public string? AdvancedFilterFingerprint { get; set; }
    public int AdvancedFilterUnknownCount { get; set; }
    public required string JobId { get; set; }
    public required string PlanId { get; set; }
    public required string Direction { get; set; } // "import" or "export"
    public required string SourceIdentifier { get; set; }
    public required string TargetIdentifier { get; set; }
    public string? TargetFormat { get; set; }
    public string? OutputPath { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public int TotalPlanned { get; set; }
    public int TotalVerified { get; set; }
    public int TotalFailed { get; set; }
    public int TotalNeedsAttention { get; set; }
    public List<BridgeFolderSummary> Folders { get; set; } = new();
    public List<BridgeAuditItem> Items { get; set; } = new();
    public string? SidecarManifestPath { get; set; }
    public List<string> QualificationWarnings { get; set; } = new();
}
