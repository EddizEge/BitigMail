using System;
using System.Collections.Generic;

namespace BitigMail.Engine.Imap.Transfer;

/// <summary>
/// Detailed audit report data for an IMAP transfer job.
/// Stored inside ConversionReport.ImapTransfer and LocalJobRecord.ImapTransfer.
/// </summary>
public sealed class ImapTransferReportDetail
{
    public string? AdvancedFilterCanonicalJson { get; set; }
    public string? AdvancedFilterFingerprint { get; set; }
    public int AdvancedFilterUnknownCount { get; set; }
    public required string JobId { get; set; }
    public required string PlanId { get; set; }
    public required string SourceAccountId { get; set; }
    public long SourceAccountVersion { get; set; }
    public required string TargetAccountId { get; set; }
    public long TargetAccountVersion { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public List<ImapTransferFolderSummary> Folders { get; set; } = new();
    public int TotalPlanned { get; set; }
    public int TotalVerified { get; set; }
    public int TotalFailed { get; set; }
    public int TotalNeedsAttention { get; set; }
    public List<ImapTransferItemAuditRecord> Items { get; set; } = new();
}

/// <summary>
/// Audit trail for an individual transferred item.
/// </summary>
public sealed class ImapTransferItemAuditRecord
{
    public required string ItemId { get; set; }
    public required string SourceFolder { get; set; }
    public required uint SourceUid { get; set; }
    public required string TargetFolder { get; set; }
    public uint? TargetUid { get; set; }
    public required string BitigMailKeyword { get; set; }
    public required string Status { get; set; }
    public required string SourceSha256 { get; set; }
    public string? VerifiedSha256 { get; set; }
    public DateTimeOffset? OriginalMimeDateUtc { get; set; }
    public DateTimeOffset InternalDateUtc { get; set; }
    public string? ErrorMessage { get; set; }
}
