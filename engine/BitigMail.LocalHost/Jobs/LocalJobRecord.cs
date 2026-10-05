using System;
using BitigMail.Engine.Bridge;
using BitigMail.Engine.Imap.Transfer;
using BitigMail.Engine.Models;

namespace BitigMail.LocalHost.Jobs;

public sealed record JobAuthorizationScope(string CompanyId, string ProjectId);

public class LocalJobRecord
{
    public string JobId { get; set; } = string.Empty;
    public string? ActorUserId { get; set; }
    public long? ActorSecurityVersion { get; set; }
    public IReadOnlyList<JobAuthorizationScope> RequiredScopes { get; set; } = Array.Empty<JobAuthorizationScope>();
    public string? IdempotencyKey { get; set; }
    public ClientProjectContext ClientContext { get; set; } = new();
    public string SourceFileName { get; set; } = string.Empty;
    public string TargetFileName { get; set; } = string.Empty;
    public string? OutputPath { get; set; }
    public string Status { get; set; } = "queued"; // "queued", "converting", "verifying", "completed", "failed", "interrupted"
    public string Stage { get; set; } = "Sırada";
    public int ItemsRead { get; set; }
    public int ItemsWritten { get; set; }
    public int FailedItems { get; set; }
    public int TotalItems { get; set; }
    public string CurrentFolder { get; set; } = string.Empty;
    public string? ProgressPhase { get; set; }
    public int? PhaseCompleted { get; set; }
    public int? PhaseTotal { get; set; }
    public int PercentComplete { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsFiltered { get; set; }
    public ConversionSelectionFilter? SelectionFilter { get; set; }
    public string? SelectionId { get; set; }
    public string? SelectionContentHash { get; set; }
    public int TotalSourceMessages { get; set; }
    public int SelectedMessagesCount { get; set; }
    public int ExcludedMessagesCount { get; set; }
    public int MissingDateExcludedCount { get; set; }
    public int SelectedAttachmentsCount { get; set; }
    public string JobKind { get; set; } = "convert"; // "convert" or "split"
    public string? SplitMode { get; set; } // "year" or "size"
    public long? SplitSizeCapBytes { get; set; }
    public string? OutputDirectoryPath { get; set; }
    public List<SplitPartReport> Parts { get; set; } = new();
    public string? SourceKind { get; set; }
    public string? Dialect { get; set; }
    public string? SourceSetFingerprint { get; set; }
    public int IgnoredNonEmlFilesCount { get; set; }
    public MimeImportReportDetail? MimeImport { get; set; }
    public string? QualificationFingerprint { get; set; }
    public bool DateFilterBlocked { get; set; }
    public bool QualificationIsPartial { get; set; }
    public List<string> QualificationWarnings { get; set; } = new();
    public string SdkQualification { get; set; } = "LICENSED_OUTPUT_ACCEPTANCE_PENDING";
    public ImapTransferReportDetail? ImapTransfer { get; set; }
    public BridgeTransferReportDetail? BridgeTransfer { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? RequestFingerprint { get; set; }
    public string? PlanId { get; set; }
    public string? ArchiveId { get; set; }
    public string? ArchiveName { get; set; }
    public bool WaitingAtShutdown { get; set; }
    public bool NeverStartedQueued { get; set; }
    public bool RecoveryWorkerUnresolved { get; set; }
    public int WaitingPriority { get; set; }
    public string? AdvancedFilterCanonicalJson { get; set; }
    public string? AdvancedFilterFingerprint { get; set; }
    public int AdvancedFilterUnknownCount { get; set; }
    public string? FolderMappingFingerprint { get; set; }
    public string? DuplicatePolicy { get; set; }
    public List<string> SkippedDuplicateItemIds { get; set; } = new();
    public string? FrozenSelectionFingerprint { get; set; }
    public string? RecoveryOutcome { get; set; }
    public int? RecoveryOriginalTotal { get; set; }
    public int RecoveryFailedBoundaryCount { get; set; }
    public string? RecoveryReportSha256 { get; set; }
}

public sealed class PagedJobsResult
{
    public List<LocalJobRecord> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}
