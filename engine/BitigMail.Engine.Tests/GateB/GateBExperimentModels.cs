using System;
using System.Collections.Generic;

namespace BitigMail.Engine.Tests.GateB;

/// <summary>
/// Root data contract for the TASK-013 Gate B isolated decoding experiment report.
/// </summary>
public sealed class GateBReport
{
    public string TimestampUtc { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string TaskOwnedTempDir { get; set; } = string.Empty;
    public string ReportPath { get; set; } = string.Empty;
    public string OracleReportPath { get; set; } = string.Empty;
    public GateBCorpusMeta SourceCorpus { get; set; } = new();
    public GateBApisMeta ApisUsed { get; set; } = new();
    public GateBOracleSummary SourceOracle { get; set; } = new();
    public MimeKitEmlStageReport MimeKitEmlSourceStage { get; set; } = new();
    public MboxrdStageReport MboxrdContainerStage { get; set; } = new();
    public BodyMappingExperimentReport BodyMappingExperiment { get; set; } = new();
    public GateBPstStageReport EmlPstObservations { get; set; } = new();
    public GateBPstStageReport MboxrdPstObservations { get; set; } = new();
    public AttachmentAndCidReport AttachmentAndCidResults { get; set; } = new();
    public GateBTrialEvaluationReport TrialEvaluationObservations { get; set; } = new();
    public EscapeEdgesStageReport EscapeEdgesExperiment { get; set; } = new();
    public SourceToSdkFidelityReport SourceToSdkFidelity { get; set; } = new();
    public List<GateBRiskOrDecisionItem> RisksAndDecisionsNeeded { get; set; } = new();
}

public sealed class GateBCorpusMeta
{
    public string CorpusRoot { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string ManifestSha256 { get; set; } = string.Empty;
    public string MboxPath { get; set; } = string.Empty;
    public string MboxSha256 { get; set; } = string.Empty;
    public long MboxSizeBytes { get; set; }
    public List<GateBFileByteFact> EmlFiles { get; set; } = new();
}

public sealed class GateBFileByteFact
{
    public int Ordinal { get; set; }
    public string FixtureId { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string AbsolutePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class GateBApisMeta
{
    public string MimeKitAssembly { get; set; } = "MimeKitLite 4.17.0";
    public string SdkAssembly { get; set; } = "Aspose.Email 24.8.0";
    public string EmlLoaderApi { get; set; } = "MimeKit.MimeMessage.Load(Stream)";
    public string MboxrdReaderApi { get; set; } = "MboxrdContainerReader (one-pass unescape before MIME decode) + MimeKit.MimeMessage.Load";
    public string MapiConverterApi { get; set; } = "Aspose.Email.Mapi.MapiMessage.FromMailMessage + explicit KnownPropertyList.Body mapping";
    public string PstWriterApi { get; set; } = "Aspose.Email.Storage.Pst.PersonalStorage.Create(string, FileFormatVersion.Unicode)";
    public string PstReaderApi { get; set; } = "Aspose.Email.Storage.Pst.PersonalStorage.FromFile(string)";
}

public sealed class GateBOracleSummary
{
    public string OracleReportPath { get; set; } = string.Empty;
    public int TotalMessages { get; set; }
    public int UniqueContentCount { get; set; }
    public int Attachments { get; set; }
    public int CidCount { get; set; }
    public List<string> DuplicatePair { get; set; } = new();
    public List<string> SharedMessageIdPair { get; set; } = new();
    public List<OracleMessageRecord> Messages { get; set; } = new();
    public List<OracleMessageRecord> MboxMessages { get; set; } = new();
}

public sealed class OracleMessageRecord
{
    public string FixtureId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public List<string> From { get; set; } = new();
    public List<string> To { get; set; } = new();
    public string DateUtc { get; set; } = string.Empty;
    public List<OracleBodyRecord> Bodies { get; set; } = new();
    public List<OracleAttachmentRecord> Attachments { get; set; } = new();
}

public sealed class OracleBodyRecord
{
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class OracleAttachmentRecord
{
    public string Name { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string Cid { get; set; } = string.Empty;
    public string Disposition { get; set; } = string.Empty;
}

public sealed class MimeKitParsedMessageRecord
{
    public int Ordinal { get; set; }
    public string FixtureId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public List<string> From { get; set; } = new();
    public List<string> To { get; set; } = new();
    public string DateUtc { get; set; } = string.Empty;
    public List<OracleBodyRecord> Bodies { get; set; } = new();
    public List<OracleAttachmentRecord> Attachments { get; set; } = new();
    public bool MatchesOracle { get; set; }
    public List<string> Differences { get; set; } = new();
}

public sealed class MimeKitEmlStageReport
{
    public int TotalSourceCount { get; set; }
    public int ParsedCount { get; set; }
    public bool ExactMatchWithOracle { get; set; }
    public List<string> Differences { get; set; } = new();
    public List<MimeKitParsedMessageRecord> Messages { get; set; } = new();
}

public sealed class MboxrdStageReport
{
    public int PhysicalRecordCount { get; set; }
    public string Dialect { get; set; } = "mboxrd";
    public string ActualStrategy { get; set; } = string.Empty;
    public string NativeMimeKitParserBehavior { get; set; } = string.Empty;
    public string TaskOwnedMboxrdContainerBehavior { get; set; } = string.Empty;
    public MboxrdFromLineFacts Msg08FromLineFacts { get; set; } = new();
    public bool Msg09Msg10ExtraLfPreserved { get; set; }
    public bool ExactMatchWithOracleMboxMessages { get; set; }
    public List<string> Differences { get; set; } = new();
    public List<MimeKitParsedMessageRecord> Messages { get; set; } = new();
}

public sealed class MboxrdFromLineFacts
{
    public List<string> RawMboxBytesLines { get; set; } = new();
    public List<string> NativeMimeKitParsedLines { get; set; } = new();
    public List<string> TaskOwnedDecodedLines { get; set; } = new();
    public List<string> OracleExpectedLines { get; set; } = new();
    public bool UnescapedOnce { get; set; }
    public string FidelityAssessment { get; set; } = string.Empty;
    public string FidelityStatus
    {
        get => FidelityAssessment;
        set => FidelityAssessment = value;
    }
}

public sealed class BodyMappingExperimentReport
{
    public List<BodyMappingCandidateResult> Candidates { get; set; } = new();
    public string? SelectedSuccessfulMapping { get; set; }
    public string SelectionRationale { get; set; } = string.Empty;
}

public sealed class BodyMappingCandidateResult
{
    public string CandidateName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> PropertiesSet { get; set; } = new();
    public int PlainPreservedCount { get; set; }
    public int HtmlPreservedCount { get; set; }
    public int BothPreservedCount { get; set; }
    public bool ExactSdkToPstMatch { get; set; }
    public string OutputPstPath { get; set; } = string.Empty;
    public string OutputPstSha256 { get; set; } = string.Empty;
    public long OutputPstSizeBytes { get; set; }
    public List<BodyCandidateMessageDetail> PerMessageResults { get; set; } = new();
}

public sealed class BodyCandidateMessageDetail
{
    public string FixtureId { get; set; } = string.Empty;
    public int SdkPlainLength { get; set; }
    public int PstPlainLength { get; set; }
    public bool PlainMatchesSdk { get; set; }
    public int SdkHtmlLength { get; set; }
    public int PstHtmlLength { get; set; }
    public bool HtmlMatchesSdk { get; set; }
    public bool BothMatch { get; set; }
}

public sealed class GateBPstStageReport
{
    public string OutputPstPath { get; set; } = string.Empty;
    public string OutputPstSha256 { get; set; } = string.Empty;
    public long OutputPstSizeBytes { get; set; }
    public int TotalMessageCount { get; set; }
    public List<GateBStageObservationRecord> Messages { get; set; } = new();
}

public sealed class GateBStageObservationRecord
{
    public int Ordinal { get; set; }
    public string FixtureId { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public bool SubjectHasEvaluationWatermark { get; set; }
    public bool BodyHasEvaluationWatermark { get; set; }
    public string? Sender { get; set; }
    public string? Recipients { get; set; }
    public string? Date { get; set; }
    public string? MessageId { get; set; }
    public string? BodySnippet { get; set; }
    public string? RawBody { get; set; }
    public int BodyLength { get; set; }
    public string? BodySha256 { get; set; }
    public bool HasHtml { get; set; }
    public int HtmlLength { get; set; }
    public bool ContainsOriginalPlain { get; set; }
    public bool ContainsOriginalHtml { get; set; }
    public int AttachmentCount { get; set; }
    public List<GateBStageAttachmentRecord> Attachments { get; set; } = new();
}

public sealed class GateBStageAttachmentRecord
{
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public bool IsInline { get; set; }
    public string? ContentId { get; set; }
}

public sealed class AttachmentAndCidReport
{
    public int TotalAttachmentsPreserved { get; set; }
    public int AttachmentHashesMatchedCount { get; set; }
    public bool AllAttachmentHashesMatched { get; set; }
    public bool TrueInlineCidPreserved { get; set; }
    public string TrueInlineCidValue { get; set; } = string.Empty;
    public bool GeneratedCidDistinguished { get; set; }
    public List<GateBAttachmentVerificationRecord> AttachmentDetails { get; set; } = new();
}

public sealed class GateBAttachmentVerificationRecord
{
    public string FixtureId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public bool IsInline { get; set; }
    public string? ContentId { get; set; }
    public bool IsGeneratedCid { get; set; }
    public bool HashPreserved { get; set; }
}

public sealed class GateBTrialEvaluationReport
{
    public bool EvaluationStringsPreserved { get; set; }
    public string ExactSubjectSuffix { get; set; } = "(Aspose.Email Evaluation)";
    public string ExactBodyNoticeUpper { get; set; } = "EVALUATİON ONLY. CREATED WİTH ASPOSE.EMAİL FOR .NET. COPYRİGHT 2002-2026 ASPOSE PTY LTD.";
    public string ExactHtmlNotice { get; set; } = "<span style=\"color:red\">Evaluation Only. Created with Aspose.Email for .NET. Copyright 2002-2026 Aspose Pty Ltd.</span>";
    public int AffectedSubjectsCount { get; set; }
    public int AffectedBodiesCount { get; set; }
    public string Classification { get; set; } = "DIFFERENCES (deneme işaretleri içeriyor) — not lossless PASS";
    public string Notes { get; set; } = string.Empty;
}

public sealed class GateBRiskOrDecisionItem
{
    public string Area { get; set; } = string.Empty;
    public string Observation { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public string DecisionNeededFromRoot { get; set; } = string.Empty;
}

public sealed class EscapeEdgesStageReport
{
    public string ExpectedJsonPath { get; set; } = string.Empty;
    public string MboxPath { get; set; } = string.Empty;
    public string MboxSha256 { get; set; } = string.Empty;
    public bool MboxSha256Matches { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public int EmlCaseCount { get; set; }
    public int MboxCaseCount { get; set; }
    public bool AllEmlCasesMatched { get; set; }
    public bool AllMboxCasesMatched { get; set; }
    public bool RawContainerUnescapedExactlyOnce { get; set; }
    public bool DecodedTextNotDequoted { get; set; }
    public bool PreExistingTrailingLfPreserved { get; set; }
    public List<EscapeEdgeCaseResult> Cases { get; set; } = new();
    public List<string> Differences { get; set; } = new();
}

public sealed class EscapeEdgeCaseResult
{
    public string File { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string EmlSha256 { get; set; } = string.Empty;
    public bool EmlSha256Matches { get; set; }
    public string TransferEncoding { get; set; } = string.Empty;
    public bool EmlTextMatchesExpected { get; set; }
    public string EmlDecodedText { get; set; } = string.Empty;
    public bool MboxTextMatchesExpected { get; set; }
    public string MboxDecodedText { get; set; } = string.Empty;
    public bool PreExistingTrailingLfPreserved { get; set; }
    public bool RawContainerUnescapedOnce { get; set; }
    public bool DecodedTextDequoteWouldCorrupt { get; set; }
    public List<string> Differences { get; set; } = new();
}

public sealed class SourceToSdkFidelityReport
{
    public List<InitialSdkDiffRecord> InitialSdkDiffs { get; set; } = new();
    public List<SourcePlainRestorationRecord> SourcePlainRestorations { get; set; } = new();
    public List<RestoredSdkPstResultRecord> RestoredPstResults { get; set; } = new();
    public bool AllInitialLossesIdentified { get; set; }
    public bool AllSourcePlainsRestored { get; set; }
    public bool AllRestoredSdkPstMatchesAchieved { get; set; }
    public string ProductGateStatus { get; set; } = "BLOCKED (decision-needed from Root)";
    public string LineEndingPolicy { get; set; } = string.Empty;
    public string OverallFidelityClassification { get; set; } = "DIFFERENCES (deneme işaretleri içeriyor) — not lossless PASS";
    public string Summary { get; set; } = string.Empty;
    public List<InitialSdkApiObservation> InitialSdkApiObservations { get; set; } = new();
    public Msg11TransformationDiagnosis Msg11Diagnosis { get; set; } = new();
    public PstCanonicalizationReport PstCanonicalization { get; set; } = new();
}

public sealed class InitialSdkApiObservation
{
    public string FixtureId { get; set; } = string.Empty;
    public SdkApiPropertyObservation Body { get; set; } = new();
    public SdkApiPropertyObservation TextBody { get; set; } = new();
    public SdkApiPropertyObservation GetAlternateViewContentPlain { get; set; } = new();
    public List<SdkAlternateViewObservation> AlternateViews { get; set; } = new();
}

public sealed class SdkApiPropertyObservation
{
    public bool ApiExists { get; set; }
    public bool ValuePresent { get; set; }
    public string? RawValueSnippet { get; set; }
    public int Length { get; set; }
    public string? Sha256 { get; set; }
    public string LineEnding { get; set; } = string.Empty;
    public bool EndsWithTerminalLf { get; set; }
    public bool MatchesOracleExactly { get; set; }
    public string DiffAgainstOracle { get; set; } = string.Empty;
}

public sealed class SdkAlternateViewObservation
{
    public int Index { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public int Length { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string LineEnding { get; set; } = string.Empty;
    public string? ContentSnippet { get; set; }
    public bool MatchesOraclePlain { get; set; }
    public bool MatchesOracleHtml { get; set; }
    public string DiffAgainstOracle { get; set; } = string.Empty;
}

public sealed class Msg11TransformationDiagnosis
{
    public int OraclePlainLength { get; set; }
    public string OraclePlainSha256 { get; set; } = string.Empty;
    public string OraclePlainEscaped { get; set; } = string.Empty;
    public int InitialSdkPlainLength { get; set; }
    public string InitialSdkPlainSha256 { get; set; } = string.Empty;
    public string InitialSdkPlainEscaped { get; set; } = string.Empty;
    public int FirstDiffIndex { get; set; }
    public string BeforeSnippet { get; set; } = string.Empty;
    public string AfterSnippet { get; set; } = string.Empty;
    public string LineEndingAnalysis { get; set; } = string.Empty;
    public string SemanticTransformationSummary { get; set; } = string.Empty;
    public string PstCanonicalizationAnalysis { get; set; } = string.Empty;
}

public sealed class PstCanonicalizationReport
{
    public string LineEndingPolicy { get; set; } = string.Empty;
    public List<PstCanonicalizationItem> MessageItems { get; set; } = new();
    public string RootDecisionNeeded { get; set; } = string.Empty;
}

public sealed class PstCanonicalizationItem
{
    public string FixtureId { get; set; } = string.Empty;
    public int SourceLength { get; set; }
    public string SourceLineEnding { get; set; } = string.Empty;
    public int SdkRestoredLength { get; set; }
    public int PstRawLength { get; set; }
    public int PstNormalizedLength { get; set; }
    public string PstLineEnding { get; set; } = string.Empty;
    public int NewlineCount { get; set; }
    public int ByteExpansionCount { get; set; }
    public bool ExactSourcePlainUnderNormalizedPolicy { get; set; }
    public bool ExactByteForBytePreserved { get; set; }
    public string CanonicalizationNote { get; set; } = string.Empty;
}

public sealed class InitialSdkDiffRecord
{
    public string FixtureId { get; set; } = string.Empty;
    public string LossType { get; set; } = string.Empty;
    public bool HasLoss { get; set; }
    public string Description { get; set; } = string.Empty;
    public string OraclePlainSha256 { get; set; } = string.Empty;
    public string InitialSdkPlainSha256 { get; set; } = string.Empty;
    public int OraclePlainLength { get; set; }
    public int InitialSdkPlainLength { get; set; }
}

public sealed class SourcePlainRestorationRecord
{
    public string FixtureId { get; set; } = string.Empty;
    public string MeasuredEvaluationPrefix { get; set; } = string.Empty;
    public int MeasuredEvaluationPrefixLength { get; set; }
    public int OriginalPlainLength { get; set; }
    public int RestoredPlainLength { get; set; }
    public bool ContainsExactOriginalPlain { get; set; }
    public bool ContainsMeasuredEvaluationPrefix { get; set; }
    public bool TrailingLfPreserved { get; set; }
    public bool ExactSourcePlainTextPreserved { get; set; }
}

public sealed class RestoredSdkPstResultRecord
{
    public string FixtureId { get; set; } = string.Empty;
    public bool PlainMatchesRestoredSdk { get; set; }
    public bool HtmlMatchesSdk { get; set; }
    public bool BothMatch { get; set; }
    public bool ContainsOriginalPlain { get; set; }
    public bool ContainsOriginalHtml { get; set; }
    public bool EvaluationAdditionsRetainedOnly { get; set; }
    public int PstPlainLength { get; set; }
    public int PstHtmlLength { get; set; }
}


