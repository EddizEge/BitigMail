using BitigMail.Engine.Bridge;
using BitigMail.Engine.Imap.Transfer;

namespace BitigMail.Engine.Models;

public class ConversionReport
{
    public string JobId { get; set; } = string.Empty;
    public string EvidenceLabel { get; set; } = "GENUINE_OST_CONVERSION (Local-Engine Reopen Verification)";
    public ClientProjectContext ClientContext { get; set; } = new();
    public string SourceFileName { get; set; } = string.Empty;
    public long SourceSizeBytes { get; set; }
    public string SourceSha256Before { get; set; } = string.Empty;
    public string SourceSha256After { get; set; } = string.Empty;
    public bool SourceHashMatch { get; set; }
    public string OutputPstFileName { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public long OutputPstSizeBytes { get; set; }
    public string OutputPstSha256 { get; set; } = string.Empty;
    public bool ConversionSuccess { get; set; }
    public int ItemsRead { get; set; }
    public int ItemsWritten { get; set; }
    public int FailedItems { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public int TotalFoldersProcessed { get; set; }
    public string FidelityStatus { get; set; } = "DIFFERENCES";
    public string OverallStatus { get; set; } = "SUCCESS";
    public TrialDifferencesInfo TrialDifferences { get; set; } = new();
    public UnmeasuredFieldsInfo UnmeasuredFields { get; set; } = new();
    public ReopenedPstVerificationInfo ReopenedPstVerification { get; set; } = new();
    public bool IsFiltered { get; set; }
    public ConversionSelectionFilter? SelectionFilter { get; set; }
    public int TotalSourceMessages { get; set; }
    public int TotalItems
    {
        get => TotalSourceMessages > 0 ? TotalSourceMessages : (ItemsRead > 0 ? ItemsRead : ItemsWritten);
        set => TotalSourceMessages = value;
    }
    public int SelectedMessagesCount { get; set; }
    public int ExcludedMessagesCount { get; set; }
    public int MissingDateExcludedCount { get; set; }
    public int SelectedAttachmentsCount { get; set; }
    public string? SelectionId { get; set; }
    public string JobKind { get; set; } = "convert"; // "convert" or "split"
    public string? SplitMode { get; set; } // "year" or "size"
    public long? SplitSizeCapBytes { get; set; }
    public string? OutputDirectoryPath { get; set; }
    public List<SplitPartReport> Parts { get; set; } = new();
    public string? SourceSetFingerprint { get; set; }
    public string? SourceKind { get; set; }
    public string? Dialect { get; set; }
    public int IgnoredNonEmlFilesCount { get; set; }
    public MimeImportReportDetail? MimeImport { get; set; }
    public ImapTransferReportDetail? ImapTransfer { get; set; }
    public BridgeTransferReportDetail? BridgeTransfer { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class TrialDifferencesInfo
{
    public bool HasObservedTrialModifications { get; set; }
    public List<string> ObservedModificationsInThisRun { get; set; } = new();
    public string ObservedSummary { get; set; } = "None observed (0 trial watermarks detected).";
    public List<string> DocumentedTrialCapabilitiesAndLimits { get; set; } = new()
    {
        "Evaluation license enforces a 50-item limit per folder (vendor constraint).",
        "Documented potential watermark: '(Aspose.Email Evaluation)' suffix appended to subject.",
        "Documented potential watermark: 'Evaluation Only. Created with Aspose.Email for .NET...' prepended to body."
    };
    public string WatermarkPolicy { get; set; } = "Evaluation watermarks are strictly monitored and preserved without stripping; never synthesized.";
}

public class UnmeasuredFieldsInfo
{
    public string Status { get; set; } = "UNKNOWN";
    public List<string> Fields { get; set; } = new()
    {
        "PR_TRANSPORT_MESSAGE_HEADERS (Raw RFC 822 transport headers)",
        "PR_INTERNET_CPID (Codepage identifiers)",
        "MAPI Named Properties (Outlook custom property stream / GUID namespaces)",
        "RTF Compressed Body Stream (PR_RTF_COMPRESSED) and RTF Sync status",
        "PR_ENTRYID and PR_RECORD_KEY (Binary storage identifiers)"
    };
    public string Note { get; set; } = "Unmeasured fields are explicitly recorded as UNKNOWN per contract; never assumed matching.";
}

public class ReopenedPstVerificationInfo
{
    public string VerifiedWith { get; set; } = "Aspose.Email 24.8 (Strictly SAME_SDK_ONLY)";
    public bool VerificationSuccess { get; set; }
    public int TotalFoldersFound { get; set; }
    public int ActiveFoldersFound { get; set; }
    public int EmptyFoldersFound { get; set; }
    public int SystemFoldersFound { get; set; }
    public int TotalPhysicalItemsFound { get; set; }
    public bool ItemCountMatch { get; set; }
    public int TotalAttachmentsVerified { get; set; }
    public int TotalCidVerified { get; set; }
    public List<string> VerificationNotes { get; set; } = new();
}
