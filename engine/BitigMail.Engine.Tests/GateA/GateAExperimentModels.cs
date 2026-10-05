using System;
using System.Collections.Generic;

namespace BitigMail.Engine.Tests.GateA;

/// <summary>
/// Root model for the TASK-013 Gate A experiment JSON report.
/// </summary>
public sealed class GateAReport
{
    public string TimestampUtc { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string TaskOwnedTempDir { get; set; } = string.Empty;
    public string ReportPath { get; set; } = string.Empty;
    public string OracleReportPath { get; set; } = string.Empty;
    public CorpusSourceMeta SourceCorpus { get; set; } = new();
    public InstalledApisMeta ApisUsed { get; set; } = new();
    public SourceOracleSummary SourceOracle { get; set; } = new();
    public EmlStageReport EmlStageObservations { get; set; } = new();
    public MboxModeReport MboxrdObservations { get; set; } = new();
    public MboxModeReport MboxoObservations { get; set; } = new();
    public StageDiffSummary StageDiffs { get; set; } = new();
    public TrialEvaluationSummary TrialEvaluationObservations { get; set; } = new();
    public List<RiskOrDecisionItem> RisksAndDecisionsNeeded { get; set; } = new();
}

public sealed class CorpusSourceMeta
{
    public string CorpusRoot { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string ManifestSha256 { get; set; } = string.Empty;
    public string MboxPath { get; set; } = string.Empty;
    public string MboxSha256 { get; set; } = string.Empty;
    public long MboxSizeBytes { get; set; }
    public List<FileByteFact> EmlFiles { get; set; } = new();
}

public sealed class FileByteFact
{
    public int Ordinal { get; set; }
    public string FixtureId { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string AbsolutePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class InstalledApisMeta
{
    public string SdkAssembly { get; set; } = "Aspose.Email 24.8.0";
    public string EmlLoaderApi { get; set; } = "Aspose.Email.MailMessage.Load(string, EmlLoadOptions)";
    public string MapiConverterApi { get; set; } = "Aspose.Email.Mapi.MapiMessage.FromMailMessage(MailMessage, MapiConversionOptions.UnicodeFormat)";
    public string MboxrdReaderApi { get; set; } = "Aspose.Email.Storage.Mbox.MboxrdStorageReader(string, MboxLoadOptions)";
    public string MboxoReaderApi { get; set; } = "Aspose.Email.Storage.Mbox.MboxoStorageReader(string, MboxLoadOptions)";
    public string PstWriterApi { get; set; } = "Aspose.Email.Storage.Pst.PersonalStorage.Create(string, FileFormatVersion.Unicode)";
    public string PstReaderApi { get; set; } = "Aspose.Email.Storage.Pst.PersonalStorage.FromStream(Stream)";
}

public sealed class SourceOracleSummary
{
    public string OracleReportPath { get; set; } = string.Empty;
    public int TotalMessages { get; set; }
    public int UniqueContentCount { get; set; }
    public List<string> DuplicatePair { get; set; } = new();
    public List<string> SharedMessageIdPair { get; set; } = new();
    public List<SourceMessageOracleRecord> Messages { get; set; } = new();
    public RawMboxByteFacts RawMboxByteFacts { get; set; } = new();
}

public sealed class SourceMessageOracleRecord
{
    public int Ordinal { get; set; }
    public string FixtureId { get; set; } = string.Empty;
    public string EmlPath { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string DateRfc { get; set; } = string.Empty;
    public string DateIso { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string RawSha256 { get; set; } = string.Empty;
    public long RawSizeBytes { get; set; }
    public bool IsDuplicate { get; set; }
    public string? DuplicateOf { get; set; }
    public bool SameMessageIdDiffContent { get; set; }
    public string? SharedMessageIdWith { get; set; }
    public int AttachmentCount { get; set; }
    public List<SourceAttachmentRecord> Attachments { get; set; } = new();
    public FromLineScanFacts RawBodyFromLineFacts { get; set; } = new();
}

public sealed class SourceAttachmentRecord
{
    public string Filename { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Size { get; set; }
    public bool IsInline { get; set; }
    public string? ContentId { get; set; }
}

public sealed class FromLineScanFacts
{
    public int FromLinesCount { get; set; }
    public int GtFromLinesCount { get; set; }
    public int GtGtFromLinesCount { get; set; }
    public List<string> MatchedLines { get; set; } = new();
}

public sealed class RawMboxByteFacts
{
    public int TotalEnvelopeCount { get; set; }
    public List<MboxEnvelopeRecord> Envelopes { get; set; } = new();
    public FromLineScanFacts Msg08SegmentFacts { get; set; } = new();
}

public sealed class MboxEnvelopeRecord
{
    public int Ordinal { get; set; }
    public string EnvelopeLine { get; set; } = string.Empty;
    public long ByteOffset { get; set; }
    public string? MessageId { get; set; }
}

public sealed class StageObservationRecord
{
    public int Ordinal { get; set; }
    public string FixtureId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty; // "MailMessage", "MapiMessage", "ReopenedPst"
    public string? FolderPath { get; set; }
    public string? Subject { get; set; }
    public bool SubjectHasEvaluationWatermark { get; set; }
    public bool BodyHasEvaluationWatermark { get; set; }
    public string? Sender { get; set; }
    public string? Recipients { get; set; }
    public string? Date { get; set; }
    public string? MessageId { get; set; }
    public string? BodySnippet { get; set; }
    public int BodyLength { get; set; }
    public string? BodySha256 { get; set; }
    public bool HasHtml { get; set; }
    public int HtmlLength { get; set; }
    public int AttachmentCount { get; set; }
    public List<StageAttachmentRecord> Attachments { get; set; } = new();
    public FromLineScanFacts FromLineFacts { get; set; } = new();
}

public sealed class StageAttachmentRecord
{
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public bool IsInline { get; set; }
    public string? ContentId { get; set; }
    public string? SourceCollection { get; set; } // "Attachments", "LinkedResources", "MapiAttachments", "PstAttachments"
}

public sealed class EmlStageReport
{
    public int TotalSourceCount { get; set; }
    public int MailMessageStageCount { get; set; }
    public int MapiStageCount { get; set; }
    public int ReopenedPstStageCount { get; set; }
    public string OutputPstPath { get; set; } = string.Empty;
    public string OutputPstSha256 { get; set; } = string.Empty;
    public long OutputPstSizeBytes { get; set; }
    public List<StageObservationRecord> MailMessageObservations { get; set; } = new();
    public List<StageObservationRecord> MapiObservations { get; set; } = new();
    public List<StageObservationRecord> ReopenedPstObservations { get; set; } = new();
}

public sealed class MboxModeReport
{
    public string DialectMode { get; set; } = string.Empty; // "mboxrd" or "mboxo"
    public string ReaderClass { get; set; } = string.Empty;
    public bool IsCompatibleWithCorpus { get; set; } = true;
    public string DialectSupportStatus { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public int TotalReportedByReader { get; set; }
    public int PhysicalReadCount { get; set; }
    public int ReopenedPstCount { get; set; }
    public string OutputPstPath { get; set; } = string.Empty;
    public string OutputPstSha256 { get; set; } = string.Empty;
    public long OutputPstSizeBytes { get; set; }
    public FromLineBehaviorComparison Msg08FromLineBehavior { get; set; } = new();
    public List<StageObservationRecord> Messages { get; set; } = new();
    public List<StageObservationRecord> ReopenedPstMessages { get; set; } = new();
}

public sealed class FromLineBehaviorComparison
{
    public string SourceEmlLines { get; set; } = string.Empty;
    public string RawMboxBytesLines { get; set; } = string.Empty;
    public string SdkMailMessageLines { get; set; } = string.Empty;
    public string MapiMessageLines { get; set; } = string.Empty;
    public string ReopenedPstLines { get; set; } = string.Empty;
    public bool UnescapedOnceByReader { get; set; }
    public bool LiteralFromPreserved { get; set; }
    public bool QuotedFromPreserved { get; set; }
    public bool IsFidelityBlocker { get; set; }
    public string FidelityAssessment { get; set; } = string.Empty;
}

public sealed class StageDiffSummary
{
    public List<MessageDiffRecord> MessageDiffs { get; set; } = new();
    public int TotalSubjectWatermarkAlteredCount { get; set; }
    public int TotalBodyWatermarkAlteredCount { get; set; }
    public int AttachmentIntegrityMatchCount { get; set; }
    public int InlineCidMatchCount { get; set; }
    public int TrueInlineCidPreservedCount { get; set; }
    public int GeneratedCidAdditionCount { get; set; }
}

public sealed class MessageDiffRecord
{
    public string FixtureId { get; set; } = string.Empty;
    public string SourceSubject { get; set; } = string.Empty;
    public string? MailMessageSubject { get; set; }
    public string? MapiSubject { get; set; }
    public string? ReopenedPstSubject { get; set; }
    public bool SubjectEvaluationWatermarkFound { get; set; }
    public bool BodyEvaluationWatermarkFound { get; set; }
    public string SourceFrom { get; set; } = string.Empty;
    public string? SdkSender { get; set; }
    public string? MapiSender { get; set; }
    public string SourceMessageId { get; set; } = string.Empty;
    public string? SdkMessageId { get; set; }
    public string? MapiMessageId { get; set; }
    public int SourceAttachmentCount { get; set; }
    public int SdkAttachmentCount { get; set; }
    public int MapiAttachmentCount { get; set; }
    public int ReopenedPstAttachmentCount { get; set; }
    public bool AllAttachmentsShaMatched { get; set; }
    public string? InlineCidSource { get; set; }
    public string? InlineCidSdk { get; set; }
    public string? InlineCidMapi { get; set; }
    public string? InlineCidReopenedPst { get; set; }
    public bool HasGeneratedContentId { get; set; }
    public bool TrueInlineCidPreserved { get; set; }
}

public sealed class TrialEvaluationSummary
{
    public bool WatermarkDetected { get; set; }
    public string? WatermarkPatternSample { get; set; }
    public List<string> AffectedSubjectFixtureIds { get; set; } = new();
    public bool TruncationDetected { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class RiskOrDecisionItem
{
    public string Area { get; set; } = string.Empty;
    public string Observation { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public string DecisionNeededFromRoot { get; set; } = string.Empty;
}
