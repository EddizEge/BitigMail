using System;
using System.Collections.Generic;

namespace BitigMail.Engine.Archive;

public sealed class ArchiveManifestItem
{
    public required int Ordinal { get; set; }
    public required string ItemId { get; set; }
    public required string RelativeEmlPath { get; set; }
    public required string SourceSha256 { get; set; }
    public required string StoredSha256 { get; set; }
    public required long ByteLength { get; set; }
    public required string OriginalFolder { get; set; }
    public DateTimeOffset? OriginalMimeDateUtc { get; set; }
    public DateTimeOffset? SourceInternalDateUtc { get; set; }
    public string? MessageIdHeader { get; set; }
    public string? EnvelopeFrom { get; set; }
    public bool IsBodyTruncated { get; set; }
}

public sealed class ArchiveFolderSummary
{
    public required string FolderName { get; set; }
    public int ItemCount { get; set; }
    public long TotalSizeBytes { get; set; }
}

public sealed class ArchiveManifest
{
    public required string ArchiveId { get; set; }
    public required string ArchiveName { get; set; }
    public required string CompanyId { get; set; }
    public required string ProjectId { get; set; }
    public string? CompanyName { get; set; }
    public string? ProjectName { get; set; }
    public required string SourceKind { get; set; } // "eml-files", "eml-tree", "mbox", "bridge-export"
    public required string Dialect { get; set; }
    public required string SourceFingerprint { get; set; }
    public int TotalItems { get; set; }
    public long TotalSizeBytes { get; set; }
    public int TruncatedItemsCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public List<ArchiveFolderSummary> Folders { get; set; } = new();
    public List<ArchiveManifestItem> Items { get; set; } = new();
    public string? QualificationFingerprint { get; set; }
    public bool DateFilterBlocked { get; set; }
    public bool QualificationIsPartial { get; set; }
    public List<string> QualificationWarnings { get; set; } = new();
}
