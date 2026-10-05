using System;
using System.Collections.Generic;

namespace BitigMail.Engine.Models;

public enum MimeSourceKind
{
    EmlFiles,
    EmlDirectory,
    MboxFile
}

public class MimeSourceEntry
{
    public string CanonicalPath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string MappedFolder { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public int PhysicalOrdinal { get; set; }
}

public class MimeSourceManifest
{
    public string SourceKind { get; set; } = "eml-files"; // "eml-files", "eml-tree", "mbox"
    public string Dialect { get; set; } = "rfc822"; // "rfc822", "mboxrd"
    public string RootPath { get; set; } = string.Empty;
    public List<MimeSourceEntry> Entries { get; set; } = new();
    public string AggregateFingerprint { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public int IgnoredNonEmlFilesCount { get; set; }
}

public class MimeAnalysisResult
{
    public string SourceHandle { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string Dialect { get; set; } = string.Empty;
    public long SourceSizeBytes { get; set; }
    public string SourceSha256 { get; set; } = string.Empty;
    public string SourceFingerprint { get; set; } = string.Empty;
    public int TotalFolders { get; set; }
    public int ActiveFoldersCount { get; set; }
    public int EmptyFoldersCount { get; set; }
    public int SystemFoldersCount { get; set; }
    public int TotalItems { get; set; }
    public int PhysicalTotalItems { get; set; }
    public int TotalAttachments { get; set; }
    public List<FolderSummary> Folders { get; set; } = new();
    public List<SampleMessageSummary> SampleMessages { get; set; } = new();
    public PreflightCheckResult Preflight { get; set; } = new();
    public int IgnoredNonEmlFilesCount { get; set; }
}

public class MimeImportReportDetail
{
    public string? FolderMappingFingerprint { get; set; }
    public string DuplicatePolicy { get; set; } = "PreservePhysical";
    public List<string> SkippedDuplicateItemIds { get; set; } = new();
    public string? ExecutionPolicyFingerprint { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string Dialect { get; set; } = string.Empty;
    public string SourceSetFingerprint { get; set; } = string.Empty;
    public int TotalSourceEntries { get; set; }
    public int TotalSourceItems
    {
        get => TotalSourceEntries;
        set => TotalSourceEntries = value;
    }
    public int ImportedMessagesCount { get; set; }
    public int IgnoredNonEmlFilesCount { get; set; }
    public Dictionary<string, int> FolderCounts { get; set; } = new();

    public string CalibratedSubjectSuffix { get; set; } = string.Empty;
    public string CalibratedPlainPrefix { get; set; } = string.Empty;
    public string CalibratedMultipartPlainPrefix { get; set; } = string.Empty;
    public string CalibratedHtmlNotice { get; set; } = string.Empty;
    public string CalibratedHtmlPrefix { get; set; } = string.Empty;
    public string CalibratedHtmlSuffix { get; set; } = string.Empty;

    public int RestoredTrailingLfCount { get; set; }
    public int GeneratedCidCount { get; set; }
    public int AddedPlainRepresentationCount { get; set; }
    public int CheckedOriginalCidCount { get; set; }
    public int InlineCidCount
    {
        get => CheckedOriginalCidCount;
        set => CheckedOriginalCidCount = value;
    }
    public int CheckedAttachmentsCount { get; set; }
    public int AttachmentsWrittenCount
    {
        get => CheckedAttachmentsCount;
        set => CheckedAttachmentsCount = value;
    }
    public string OverallQualification { get; set; } = "DIFFERENCES";
    public List<string> ObservedDifferences { get; set; } = new();
    public List<MimeMessageDifferenceSummary> MessageDifferences { get; set; } = new();
    public List<MimeMessageDifferenceSummary> Differences
    {
        get => MessageDifferences;
        set => MessageDifferences = value;
    }
    public string? QualificationFingerprint { get; set; }
    public bool DateFilterBlocked { get; set; }
    public bool QualificationIsPartial { get; set; }
    public List<string> QualificationWarnings { get; set; } = new();
    public string SdkQualification { get; set; } = "LICENSED_OUTPUT_ACCEPTANCE_PENDING";
}

public class MimeMessageDifferenceSummary
{
    public List<string> MetadataAdditions { get; set; } = new();
    public bool RestoredHtmlTrailingLf { get; set; }
    public bool AddedHtmlRepresentation { get; set; }
    public int Ordinal { get; set; }
    public string MessageId { get; set; } = string.Empty;
    public string MessageKey { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string SubjectDifference { get; set; } = string.Empty;
    public string ObservedSubject { get; set; } = string.Empty;
    public string MappedFolder { get; set; } = string.Empty;
    public bool HasHtml { get; set; }
    public bool HasPlain { get; set; }
    public bool AddedPlainRepresentation { get; set; }
    public string? AddedRepresentation { get; set; }
    public bool InitialRawLoadExactWithVendorPrefix { get; set; }
    public bool RestoredTrailingLf { get; set; }
    public int AttachmentCount { get; set; }
    public int OriginalCidCount { get; set; }
    public int GeneratedCidCount { get; set; }
    public string? PlainBodySha256 { get; set; }
    public string? HtmlBodySha256 { get; set; }
}
