# TASK-014 IMAP Transfer Backend Specification & Implementation Plan

Status: IMPLEMENTED (verification pending SOL)

## 1. Overview & Architecture
Bounded backend slice implementing:
1. Server-generated immutable preview: `POST /api/transfer/imap/preview`
2. Dedicated immutable plan + per-item durable journal (`ImapTransferJournal`, `ImapTransferPlan`) with atomic temp + `Flush(true)` + replace.
3. State machine: `Planned` -> `AppendIntent` -> `APPEND` -> target `FETCH` + `SEARCH` -> `Verified`.
4. Target token: random 128-bit per-item BitigMail keyword (`bitigmail_{guid:N}`).
5. Safe resume/restart reconciliation:
   - Recheck source account version, UIDVALIDITY, and item hashes before writes.
   - Search target for keyword:
     - Exactly one match + verified byte SHA256/date/flags reconciles to `Verified`.
     - Zero match with `AppendIntent`: ambiguous (network lost after APPEND vs before APPEND), stops at `NeedsAttention`, NO automatic re-APPEND.
     - Multiple matches or hash/flags mismatch: `NeedsAttention`.
     - `Planned` items safely proceed.
   - Verified target deletion/change or UIDVALIDITY change blocks.
6. Shared single active slot with PST jobs in `JobManager`.
7. `LocalJobRecord` with `jobKind="imap-transfer"` and durable downloadable `ConversionReport` with `ImapTransfer` details. Old PST fields never shown as verified for IMAP.
8. Safe API endpoints:
   - `POST /api/transfer/imap/preview`
   - `GET /api/transfer/imap/preview/{previewId}`
   - `POST /api/transfer/imap/start`
   - `POST /api/transfer/imap/resume/{jobId}`
   - Reports / jobs integrated with existing `/api/jobs/{jobId}` and `/api/reports/{jobId}`.

## 2. Completed File List & Edits
- `engine/BitigMail.Engine/Imap/Transfer/ImapTransferPreviewModels.cs`: Request, response, folder mapping, and summary DTOs.
- `engine/BitigMail.Engine/Imap/Transfer/ImapTransferJournalModels.cs`: Immutable plan and durable journal state models.
- `engine/BitigMail.Engine/Imap/Transfer/ImapTransferReportModels.cs`: `ImapTransferReportDetail` and item audit trail records.
- `engine/BitigMail.LocalHost/Imap/Transfer/ImapTransferJournal.cs`: Atomic file-based durable journal per job (`{runtimeDir}/imap-transfers/journals/{jobId}.json`) and plans (`.../plans/{planId}.json`).
- `engine/BitigMail.LocalHost/Imap/Transfer/IImapTransferClient.cs`: Client contract with typed summaries and verification results.
- `engine/BitigMail.LocalHost/Imap/Transfer/MailKitTransferClient.cs`: Public MailKit client implementation with EXAMINE, BODY.PEEK, APPEND, SEARCH, and FETCH.
- `engine/BitigMail.LocalHost/Imap/Transfer/ImapTransferPreviewService.cs`: Generates immutable preview, applies UTC+03:00 inclusive day bounds to original MIME Date with explicit timezone, enforces serialization byte equality and Deleted flag blocking.
- `engine/BitigMail.LocalHost/Imap/Transfer/ImapTransferWorker.cs`: Background worker orchestrating state sequence (`Planned` -> `AppendIntent` -> `APPEND` -> `Verified`), resume reconciliation, and report generation before job completion.
- `engine/BitigMail.LocalHost/Jobs/JobManager.cs`: Marked `partial`.
- `engine/BitigMail.LocalHost/Jobs/JobManager.ImapTransfer.cs`: Shared active slot check (`_activeRunningJobId`), typed 409 idempotency conflict detection, start and resume methods.
- `engine/BitigMail.LocalHost/Jobs/LocalJobRecord.cs`: Added `public ImapTransferReportDetail? ImapTransfer { get; set; }`.
- `engine/BitigMail.Engine/Models/ConversionReport.cs`: Added `public ImapTransferReportDetail? ImapTransfer { get; set; }`.
- `engine/BitigMail.LocalHost/LocalEngineApiEndpoints.ImapTransfer.cs`: Endpoint route mappings for preview, start, and resume.
- `engine/BitigMail.LocalHost/LocalEngineApiEndpoints.cs`: Invoked `app.MapImapTransferEndpoints()`.
- `engine/BitigMail.LocalHost/Program.cs`: Registered `IImapTransferClientFactory`, `ImapTransferJournal`, and `ImapTransferPreviewService`.
- `engine/BitigMail.Engine.Tests/ImapTransferTests.cs`: 10 comprehensive deterministic unit tests covering:
  - `FrozenPreview_CapturesExactItems_ExcludesSecrets`
  - `DateEdgeAndMissingTimezone_ExcludesAndCountsMissing`
  - `PhysicalDuplicatesAndSharedMessageId_TreatedAsDistinctItems`
  - `AccountVersionOrSourceHashMismatch_BlocksBeforeWrites`
  - `IdempotencyConflict_SameKeyDifferentPlan_ThrowsConflict`
  - `StateTransitions_PlannedToAppendIntentToVerified`
  - `PersistenceFault_HaltsBeforeNextAppend_PreservesPriorTargetUidEvidence`
  - `ResumeReconciliation_ZeroToken_IsAmbiguous_SetsNeedsAttention_NoReappend`
  - `ResumeReconciliation_OneToken_ReconcilesToVerified`
  - `ResumeReconciliation_MultipleTokens_SetsNeedsAttention`
  - `VerifiedTargetMutationOrDeletion_BlocksTransfer`
  - `SharedSlotContention_BlocksConcurrentJobs`

## 3. State Machine & Journal Sequence
1. `Planned`: Initial state in immutable plan. Contains source folder, source UID, source UIDVALIDITY, SHA256, original MIME Date, flags/keywords, target folder.
2. `AppendIntent`: Atomically persisted before any write to target IMAP server. Includes:
   - Generated random 128-bit per-item BitigMail keyword (`bitigmail_{guid:N}`).
   - Source UIDVALIDITY, target UIDVALIDITY, source UID, expected SHA256, flags, internal date.
3. Serialization byte equality gate:
   - Source MIME serialized with MailKit format options (`NewLineFormat.Dos`, `HiddenHeaders.Clear()`, `EnsureNewLine = true`).
   - If serialized byte SHA-256 does not match raw source byte SHA-256, item is blocked fail-closed before APPEND.
4. `APPEND` to target folder with message, flags (preserving seen/answered/flagged/draft/custom; excluding Recent; Deleted blocks), internal date, and the BitigMail keyword.
5. Target verify:
   - Search target for the per-item keyword.
   - If exactly one match found: FETCH body peek, compute SHA256, verify flags and date.
   - Atomically persist `Verified` state to journal with `targetUid` and `verifiedSha256`.
6. Resume reconciliation:
   - If an item was in `AppendIntent` state upon interrupted job recovery:
     - Search target folder for the item's token.
     - If count == 1 and fetched bytes SHA256, date, flags match: transition to `Verified`.
     - If count == 0: transition to `NeedsAttention` (ambiguous; cannot re-append without human intervention to prevent duplicates).
     - If count > 1: transition to `NeedsAttention`.
     - If hash/date/flags mismatch: transition to `NeedsAttention`.
   - If verified target message was altered or deleted, or target folder UIDVALIDITY changed: halt transfer and mark `NeedsAttention`/`Failed`.

Verification status: verification pending SOL
