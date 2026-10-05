using System;
using System.Collections.Generic;

namespace BitigMail.Engine.Imap.Transfer;

/// <summary>
/// Source to target folder mapping specification.
/// </summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ImapFolderMappingRequest
{
    public required string SourceFolderPath { get; init; }
    public required string TargetFolderPath { get; init; }
}

/// <summary>
/// Request payload to generate an immutable IMAP transfer preview.
/// Excludes secrets and credentials.
/// </summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ImapTransferPreviewRequest
{
    public string? CompanyName { get; init; }
    public string? ProjectName { get; init; }
    public required string CompanyId { get; init; }
    public required string ProjectId { get; init; }
    public required string SourceAccountId { get; init; }
    public required string TargetAccountId { get; init; }
    public required List<ImapFolderMappingRequest> SelectedFolders { get; init; }
    public BitigMail.Engine.Models.MailFilterDefinition? AdvancedFilter { get; init; }
    public string? StartDate { get; init; }
    public string? EndDate { get; init; }
}

/// <summary>
/// Summary of a single mapped folder in the preview.
/// </summary>
public sealed record ImapTransferFolderSummary
{
    public required string SourceFolder { get; init; }
    public required string TargetFolder { get; init; }
    public uint SourceUidValidity { get; init; }
    public int TotalItems { get; init; }
    public int EligibleItems { get; init; }
    public int ExcludedCount { get; init; }
}

/// <summary>
/// Safe public summary of the generated immutable preview.
/// Never contains host, username, password, ciphertext, or raw MIME bytes.
/// </summary>
public sealed record ImapTransferPreviewResponse
{
    public required string PreviewId { get; init; }
    public required string CompanyId { get; init; }
    public required string ProjectId { get; init; }
    public required string SourceAccountId { get; init; }
    public long SourceAccountVersion { get; init; }
    public required string TargetAccountId { get; init; }
    public long TargetAccountVersion { get; init; }
    public int TotalSourceItems { get; init; }
    public int EligibleItemsCount { get; init; }
    public int ExcludedCount { get; init; }
    public string? AdvancedFilterFingerprint { get; init; }
    public int AdvancedFilterUnknownCount { get; init; }
    public int MissingDateExcludedCount { get; init; }
    public int DeletedExcludedCount { get; init; }
    public required List<ImapTransferFolderSummary> Folders { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public bool CanTransfer { get; init; }
    public string? BlockerReason { get; init; }
}

/// <summary>
/// Request to start an IMAP transfer job from a frozen preview.
/// Accepts only previewId and idempotencyKey.
/// </summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ImapTransferStartRequest
{
    public required string PreviewId { get; init; }
    public required string IdempotencyKey { get; init; }
    public string? CompanyId { get; init; }
    public string? ProjectId { get; init; }
    public bool EnqueueIfBusy { get; init; }
}

/// <summary>
/// Request to resume an interrupted IMAP transfer job.
/// </summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ImapTransferResumeRequest
{
    public string? CompanyId { get; init; }
    public string? ProjectId { get; init; }
    public bool EnqueueIfBusy { get; init; }
}
