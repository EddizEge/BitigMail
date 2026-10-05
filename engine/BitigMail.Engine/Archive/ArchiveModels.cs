using System;
using System.Collections.Generic;

namespace BitigMail.Engine.Archive;

// --- Scope & Catalog DTOs ---

public sealed record ArchiveScopeTriple
{
    public required string CompanyId { get; init; }
    public required string ProjectId { get; init; }
    public required string ArchiveId { get; init; }

    public ArchiveScopeSelection ToSelection() => new(CompanyId, ProjectId, ArchiveId);

    public static ArchiveScopeTriple FromSelection(ArchiveScopeSelection selection) => new()
    {
        CompanyId = selection.CompanyId,
        ProjectId = selection.ProjectId,
        ArchiveId = selection.ArchiveId
    };
}

public sealed record ArchiveCatalogItemDto
{
    public required string ArchiveId { get; init; }
    public required string ArchiveName { get; init; }
    public required string CompanyId { get; init; }
    public required string ProjectId { get; init; }
    public string? CompanyName { get; init; }
    public string? ProjectName { get; init; }
    public required string SourceKind { get; init; }
    public required string Dialect { get; init; }
    public required string SourceFingerprint { get; init; }
    public int TotalItems { get; init; }
    public long TotalSizeBytes { get; init; }
    public int TruncatedItemsCount { get; init; }
    public required string Status { get; init; } // "indexing", "ready", "index_failed", "corrupted"
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? IndexedAtUtc { get; init; }
    public int IndexGeneration { get; init; }
    public bool DateFilterBlocked { get; init; }
    public List<string> QualificationWarnings { get; init; } = new();
}

// --- Ingest DTOs & Plans ---

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ArchiveIngestPreviewRequest
{
    public string? SourceHandle { get; init; }
    public string? SourceJobId { get; init; }
    public required string ArchiveName { get; init; }
    public required string CompanyId { get; init; }
    public required string ProjectId { get; init; }
    public string? CompanyName { get; init; }
    public string? ProjectName { get; init; }
}

public sealed record ArchiveIngestPreviewResponse
{
    public required string PreviewId { get; init; }
    public required string ArchiveName { get; init; }
    public required string CompanyId { get; init; }
    public required string ProjectId { get; init; }
    public string? CompanyName { get; init; }
    public string? ProjectName { get; init; }
    public required string SourceKind { get; init; }
    public required string Dialect { get; init; }
    public required string SourceFingerprint { get; init; }
    public int TotalFiles { get; init; }
    public int TotalItems { get; init; }
    public long TotalSizeBytes { get; init; }
    public List<ArchiveFolderSummary> Folders { get; init; } = new();
    public bool CanIngest { get; init; }
    public string? BlockerReason { get; init; }
    public long? EstimatedRequiredBytes { get; init; }
    public long? AvailableFreeBytes { get; init; }
    public string? EstimateBasis { get; init; }
    public bool DateFilterBlocked { get; init; }
    public bool QualificationIsPartial { get; init; }
    public List<string> QualificationWarnings { get; init; } = new();
}

public sealed class ArchiveIntegrityException : InvalidOperationException
{
    public ArchiveIntegrityException(string message) : base(message) { }
}

public sealed class ArchiveIngestPlannedItem
{
    public required int Ordinal { get; set; }
    public required string ItemId { get; set; }
    public required string SourcePath { get; set; }
    public string? RelativePath { get; set; }
    public string? MboxEnvelopeFrom { get; set; }
    public byte[]? MboxRecordBytes { get; set; }
    public required string MappedFolder { get; set; }
    public required long SizeBytes { get; set; }
    public required string SourceSha256 { get; set; }
    public DateTimeOffset? SourceInternalDateUtc { get; set; }
}

public sealed class ArchiveIngestPlan
{
    public required string PlanId { get; set; }
    public required string PreviewId { get; set; }
    public required string ArchiveId { get; set; }
    public required string ArchiveName { get; set; }
    public required string CompanyId { get; set; }
    public required string ProjectId { get; set; }
    public string? CompanyName { get; set; }
    public string? ProjectName { get; set; }
    public string? SourceHandle { get; set; }
    public string? SourceJobId { get; set; }
    public required string SourceKind { get; set; }
    public required string Dialect { get; set; }
    public required string SourceFingerprint { get; set; }
    public string? SourceRootPath { get; set; }
    public int TotalItems { get; set; }
    public long TotalSizeBytes { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<ArchiveFolderSummary> Folders { get; set; } = new();
    public List<ArchiveIngestPlannedItem> Items { get; set; } = new();
    public long? EstimatedRequiredBytes { get; set; }
    public string? EstimateBasis { get; set; }
    public bool CanIngest { get; set; } = true;
    public string? BlockerReason { get; set; }
    public long? AvailableFreeBytes { get; set; }
    public string? QualificationFingerprint { get; set; }
    public bool DateFilterBlocked { get; set; }
    public bool QualificationIsPartial { get; set; }
    public List<string> QualificationWarnings { get; set; } = new();
    public List<string> QualificationSourcePaths { get; set; } = new();
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ArchiveIngestStartRequest
{
    public required string PreviewId { get; init; }
    public required string IdempotencyKey { get; init; }
    public bool EnqueueIfBusy { get; init; }
}

public sealed record ArchiveIngestStartResponse
{
    public required string JobId { get; init; }
    public required string ArchiveId { get; init; }
    public required string Status { get; init; }
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ArchiveIngestResumeRequest
{
    public string? JobId { get; init; }
    public bool EnqueueIfBusy { get; init; }
}

// --- Search DTOs ---

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ArchiveSearchRequest
{
    public const string OmittedFieldDefault = "__DEFAULT_ALL__";

    public List<ArchiveScopeTriple> SelectedScopes { get; init; } = new();
    public string? Query { get; init; }
    public string? Field { get; init; } = OmittedFieldDefault;
    public string? StartDate { get; init; }
    public string? EndDate { get; init; }
    public bool? HasAttachment { get; init; }
    public string? Folder { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public BitigMail.Engine.Models.MailFilterDefinition? AdvancedFilter { get; init; }

    public ArchiveSearchField ResolveField()
    {
        if (ReferenceEquals(Field, OmittedFieldDefault) || Field == OmittedFieldDefault)
        {
            return ArchiveSearchField.All;
        }

        if (Field is null)
        {
            throw new ArgumentException("Arama alanı (field) açıkça null olamaz.");
        }

        return Field.Trim().ToLowerInvariant() switch
        {
            "all" => ArchiveSearchField.All,
            "subject" => ArchiveSearchField.Subject,
            "sender" => ArchiveSearchField.Sender,
            "recipient" or "recipients" => ArchiveSearchField.Recipient,
            "body" => ArchiveSearchField.Body,
            "attachmentname" or "attachment" or "attachments" or "attachment_search" => ArchiveSearchField.AttachmentName,
            _ => throw new ArgumentException($"Geçersiz arama alanı: '{Field}'.")
        };
    }
}

public sealed record ArchiveSearchResultItem
{
    public required string MessageId { get; init; }
    public required string ArchiveId { get; init; }
    public required string CompanyId { get; init; }
    public required string ProjectId { get; init; }
    public required string ArchiveName { get; init; }
    public required string OriginalFolder { get; init; }
    public required string Sender { get; init; }
    public string? SenderEmail { get; init; }
    public required string Recipients { get; init; }
    public required string Subject { get; init; }
    public required string Snippet { get; init; }
    public DateTimeOffset? OriginalMimeDateUtc { get; init; }
    public DateTimeOffset? SourceInternalDateUtc { get; init; }
    public required string FormattedDate { get; init; }
    public bool HasAttachments { get; init; }
    public List<string> AttachmentNames { get; init; } = new();
    public long SizeBytes { get; init; }
    public bool IsBodyTruncated { get; init; }
}

public sealed record ArchiveSearchResponse
{
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public List<ArchiveSearchResultItem> Items { get; init; } = new();
    public bool IndexHealthy { get; init; } = true;
    public string? Warning { get; init; }
    public List<string> QualificationWarnings { get; init; } = new();
    public string? AdvancedFilterFingerprint { get; init; }
    public int AdvancedFilterUnknownCount { get; init; }
}

// --- Preview DTOs ---

public sealed record ArchiveMessagePreviewRequest
{
    public required string MessageId { get; init; }
    public required ArchiveSearchRequest SearchRequest { get; init; }
}

public sealed record ArchiveAttachmentInfo
{
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public long? SizeBytes { get; init; }
    public bool IsInline { get; init; }
    public string? ContentId { get; init; }
}

public sealed record ArchiveMessagePreviewResponse
{
    public required string MessageId { get; init; }
    public required string ArchiveId { get; init; }
    public required string Subject { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
    public string? Cc { get; init; }
    public string? Bcc { get; init; }
    public DateTimeOffset? DateUtc { get; init; }
    public DateTimeOffset? SourceInternalDateUtc { get; init; }
    public string? MessageIdHeader { get; init; }
    public required string BodyText { get; init; }
    public bool IsBodyTruncated { get; init; }
    public List<ArchiveAttachmentInfo> Attachments { get; init; } = new();
    public long RawSizeBytes { get; init; }
    public required string Sha256 { get; init; }
}

public sealed record ArchiveReindexRequest
{
    public string? ArchiveId { get; init; }
    public bool EnqueueIfBusy { get; init; }
}
