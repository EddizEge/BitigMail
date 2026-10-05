# TASK-017 Backend Checkpoint: File ↔ Account Transfer Bridge

**Status:** BACKEND IMPLEMENTATION & TEST VERIFICATION COMPLETE  
**Date:** 2026-09-14  
**Authoritative Contract:** `docs/FILE_ACCOUNT_BRIDGE_WORKFLOW.md`, `TASK-017.md`, and `TASK-017-discovery.md`  
**Root-Exclusive Integrity:** Strictly preserved untouched:  
- `engine/BitigMail.Engine/Storage/BridgeMimeBytePolicy.cs` (NEVER EDITED)  
- `engine/BitigMail.Engine.Tests/BridgeMimeBytePolicyCriticalTests.cs` (NEVER EDITED)  
- `engine/BitigMail.Engine.Tests/BridgeJournalCriticalTests.cs` (NEVER EDITED)  
- Root docs, oracles, and test lab fixtures (NEVER EDITED)  
**Compilation & Test Suite:**  
- dotnet build -c Release: **0 Warnings, 0 Errors** across all projects  
- dotnet test -c Release: **362 / 362 Passed, 0 Failed, 0 Skipped** (Baseline 305 + Root 26 + Bridge 18 + Security & Scale 13)  

---

## 1. Overview & Architecture Boundaries

TASK-017 delivers the production backend engine, API endpoints, durable journals, and workers for two real transfer directions:
1. **File to IMAP Import**: EML files, EML tree, and mboxrd archives into registered IMAP accounts.
2. **IMAP to File Export**: Registered IMAP account messages into structured EML directory trees or per-folder mboxrd files with a sidecar `manifest.json`.

### Key Architectural Boundaries & Enforcement:
- **Zero Third-Party / SDK Bypasses**: No Aspose or evaluation markers are used for EML/MBOX bridge workflows. MailKit public APIs and canonical MIME policies are used exclusively.
- **Strict Byte Policy & Canonicalization**:
  - `BridgeMimeBytePolicy.CanonicalizeForImap` standardizes line endings (LF → CRLF) and ensures a terminal CRLF without altering already-canonical CRLF or content.
  - Before any IMAP `APPEND`, MimeKit re-serialization is checked against canonical bytes (`serialized.SequenceEqual(canonical.Bytes)`). Any mismatch aborts fail-closed.
  - Raw source hash and canonical hash are tracked and reported separately.
- **Source Protection & Read-Only Access**:
  - Source IMAP mailboxes are accessed strictly in read-only `EXAMINE` mode using `BODY.PEEK[]` and `Close(false)`.
  - Zero message flags or unread statuses are altered on source accounts.
  - No source `DELETE`, `EXPUNGE`, `MOVE`, or `SMTP` operations exist anywhere in the bridge code.
  - Messages flagged with `\Deleted` are rejected fail-closed during export preview (`canTransfer = false`, counted in `deletedExcludedCount`).
- **Handle-Free Restart & Frozen Plan Autonomy**:
  - `BridgeImportPlan` stores immutable canonical paths, whole-file/MBOX SHA-256 fingerprints, and record ordinals.
  - `BridgeExportPlan` stores authorized resolved output parent root identity (`TargetDirectoryPath`).
  - If process restart or resume occurs when in-memory `FileHandleRegistry` state is empty, workers continue directly from frozen plan metadata and disk verification.
- **Atomic Disk Operations & Output Path Safety**:
  - EML export creates output files with unique staging temp paths (`.tmp`), calls `Flush(true)`, and moves them atomically with `overwrite: false`.
  - MBOXRD export stages raw items, streams them with `BridgeMimeBytePolicy.WriteMboxrdRecord` (escaping body `From ` lines), flushes to disk, and publishes atomically.
  - Safe folder names (`fld_001`, `fld_002`) and safe message filenames (`msg_00000001.eml`) eliminate OS traversal, collision, reserved names (`CON`, `PRN`), or casing issues.
  - Journal `JobOutputDir` tampering, escaping outside authorized parent directory, or pointing to reparse points / symlinks is checked and rejected fail-closed.
- **Idempotency & Single Active Job Lock**:
  - Shared single active slot with all existing jobs in `JobManager` (`ActiveRunningJobId`).
  - Same idempotency key + identical plan returns the existing job.
  - Same idempotency key + different plan throws a typed 409 Conflict (`InvalidOperationException`).
  - Different idempotency key + identical plan returns the existing job (no duplicate job creation).
- **TASK-014 Resume & Zero-Duplicate AppendIntent Reconciliation**:
  - Before every `APPEND`, item status `AppendIntent`, generated random 128-bit hex keyword (`bitigmail_{random16bytes}`), expected hash, and UIDVALIDITY are durably flushed to disk.
  - On restart / resume, if an item is in `AppendIntent`:
    - Exactly 1 keyword match on target with identical hash → reconciled to `Verified` with zero additional appends.
    - 0 keyword matches → marked `NeedsAttention`, stops the job, and NEVER issues an automatic re-append (eliminates duplicate message risks).
    - >1 keyword matches → marked `NeedsAttention` (ambiguous match).
- **Exception Sanitization & Anti-Leakage Policy**:
  - In `LocalEngineApiEndpoints.BridgeTransfer.cs` and all bridge API catch paths, raw exception details (`ex.Message`, stack traces, inner exceptions, file paths, and credentials) are strictly forbidden from escaping in HTTP responses.
  - An allowlist-based safe domain message validator preserves only known-safe, non-path, non-credential Turkish business/domain errors (e.g. `Geçersiz istek...`, `Kaynak klasör...`, `Hedef hesap...`).
  - All unexpected errors or exceptions containing absolute paths, drive letters, backslashes, colons, or stack frames are mapped to standard generic Turkish messages:
    - Preview: `"Önizleme oluşturulurken bir hata oluştu."`
    - Start: `"Aktarım işi başlatılırken bir hata oluştu."`
    - Resume: `"Aktarım işi devam ettirilirken bir hata oluştu."`
  - Corrupt or malformed preview plan files encountered during job start are caught with typed exceptions, producing safe 400 Bad Request or 409 Conflict instead of uncaught 500s.
- **Fail-Closed Preflight Revalidation on Handle-Free Resume**:
  - In `BridgeImportWorker`, handle-free restart does not assume source files remain intact.
  - Before connecting to IMAP or issuing the first `APPEND`, the entire frozen source set is revalidated against disk: all files must exist, match their original lengths, and their SHA-256 hashes must match `SourceSha256`.
  - The composite source manifest is reconstructed and verified against `plan.SourceFingerprint`.
  - If any source item (even the last of N items) has drifted or been modified, the worker halts fail-closed with 0 appends attempted.
- **Strict Canonical Output Directory Verification**:
  - In `BridgeExportWorker`, `JobOutputDir` must exactly match the canonical path `Path.Combine(plan.TargetDirectoryPath, "bridge-export-" + jobId)`.
  - Sibling directory traversal or attempts to redirect export output to unapproved subdirectories are detected and rejected fail-closed with security violations.
- **Scale Acceptance (>50 Sources)**:
  - Validated with root fixture `over50-fixture.json` containing 72 physical EML files across folders.
  - Successfully imported, parsed, previewed, and filtered cleanly at the service and picker layers without timeouts, memory spikes, or vendor SDK dependencies.
- **Persistence Ordering**:
  - For import: all items verified → destination rechecked → report saved → job marked `completed`.
  - For export: all items written → sidecar `manifest.json` flushed and moved → report saved → job marked `completed`.

---

## 2. File Inventory

### New Production Engine Files (`engine/BitigMail.Engine/` & `engine/BitigMail.LocalHost/`)
1. `engine/BitigMail.Engine/Bridge/BridgeTransferModels.cs`
   - DTOs: `BridgeImportPreviewRequest`, `BridgeImportPreviewResponse`, `BridgeExportPreviewRequest`, `BridgeExportPreviewResponse`, `BridgeResumeRequest`.
   - Immutable Plans: `BridgeImportPlan`, `BridgeImportPlannedItem`, `BridgeExportPlan`, `BridgeExportPlannedItem`, `BridgeFolderMapping`.
   - Journal States: `BridgeImportJournalState`, `BridgeImportJournalEntry`, `BridgeExportJournalState`, `BridgeExportJournalEntry`.
   - Manifests & Reports: `BridgeExportManifest`, `BridgeExportManifestFolder`, `BridgeExportManifestItem`, `BridgeTransferReportDetail`, `BridgeAuditItem`.
2. `engine/BitigMail.LocalHost/Bridge/Transfer/BridgeTransferJournal.cs`
   - Atomic disk persistence (`.plans/import`, `.plans/export`, `.journals/import`, `.journals/export`) with fail-closed corruption detection (`DamagedRecord`), security path traversal validation, and test fault injection hooks.
3. `engine/BitigMail.LocalHost/Bridge/Transfer/BridgeImportPreviewService.cs`
   - Validates sources via `FileHandleRegistry`, validates target account credentials, checks target keyword support (`SupportsUserKeywordsAsync`), applies UTC+03:00 date filters, handles date fallback to `CreatedAtUtc`, computes canonical hashes, and verifies MimeKit serialization equality.
4. `engine/BitigMail.LocalHost/Bridge/Transfer/BridgeExportPreviewService.cs`
   - Inspects source folders in read-only EXAMINE mode, validates folder UIDVALIDITY, rejects `\Deleted` messages fail-closed, paginates message headers without loading bodies into memory, maps folders to safe keys (`fld_001`), and maps items to safe names.
5. `engine/BitigMail.LocalHost/Bridge/Transfer/BridgeImportWorker.cs`
   - Executes File → IMAP import with handle-free restart, pre-flight disk revalidation, random 128-bit keyword stamping, durable `AppendIntent` flush, `AppendMessageAsync`, post-append independent verification via search and fetch, and post-completion destination check.
6. `engine/BitigMail.LocalHost/Bridge/Transfer/BridgeExportWorker.cs`
   - Executes IMAP → File export with handle-free restart, output directory path validation (tamper / escape / reparse rejection), atomic temp+flush+no-overwrite EML publishing, staged mboxrd writing, and sidecar `manifest.json` publication before job completion.
7. `engine/BitigMail.LocalHost/Jobs/JobManager.BridgeTransfer.cs`
   - Partial class extending `JobManager` with `StartBridgeImportJob`, `StartBridgeExportJob`, `ResumeBridgeImportJob`, `ResumeBridgeExportJob`, idempotency handling, single active slot locking, and background execution.
8. `engine/BitigMail.LocalHost/LocalEngineApiEndpoints.BridgeTransfer.cs`
   - Minimal API route mapping for bridge import/export endpoints.

### Existing Production Files Updated
1. `engine/BitigMail.Engine/Models/ConversionReport.cs`
   - Added `public BridgeTransferReportDetail? BridgeTransfer { get; set; }`.
2. `engine/BitigMail.LocalHost/Jobs/LocalJobRecord.cs`
   - Added `public BridgeTransferReportDetail? BridgeTransfer { get; set; }`.
3. `engine/BitigMail.LocalHost/Jobs/JobManager.cs`
   - Extended `CloneJobRecord` to copy `BridgeTransfer`.
4. `engine/BitigMail.LocalHost/Imap/Transfer/IImapTransferClient.cs`
   - Added `ImapMessageHeaderSummary`, `FetchHeaderSummariesAsync`, and `FetchSingleSourceMessageAsync` with default implementations.
5. `engine/BitigMail.LocalHost/Imap/Transfer/MailKitTransferClient.cs`
   - Implemented streaming header paging (`FetchHeaderSummariesAsync`) and single-message `BODY.PEEK[]` fetching (`FetchSingleSourceMessageAsync`).
6. `engine/BitigMail.LocalHost/LocalEngineApiEndpoints.cs`
   - Added `app.MapBridgeTransferEndpoints();`.
7. `engine/BitigMail.LocalHost/Program.cs` & `engine/BitigMail.TestingHost/Program.cs`
   - Registered `BridgeTransferJournal`, `BridgeImportPreviewService`, and `BridgeExportPreviewService` in DI.

### New Test Suite Files (`engine/BitigMail.Engine.Tests/`)
1. `engine/BitigMail.Engine.Tests/BridgeTestHelpers.cs`
   - Shared test helper creating isolated test folders, configuring `ImapAccountStore` and `IImapCredentialResolver`, serializing test EML files, and providing `BridgeFakeTransferClient` and `BridgeFakeClientFactory`.
2. `engine/BitigMail.Engine.Tests/BridgeImportTests.cs` (6 tests)
   - EML tree import with 12 physical items across folders (including Turkish names), duplicates (identical raw bytes and same Message-ID), filter 3/1, MimeKit byte equality check, and background execution.
   - MBOXRD import with 12 physical items, From-escaping (`From `, `>From `), filter 3/1, and background execution.
   - Date fallback to `plan.CreatedAtUtc` when Date header is missing.
   - Preflight blocker when target IMAP server does not support user keywords (`SupportsUserKeywordsAsync == false`).
   - Handle-free restart succeeding without `FileHandleRegistry` state.
   - Source file tampering detection failing job immediately.
3. `engine/BitigMail.Engine.Tests/BridgeExportTests.cs` (5 tests)
   - Rejection of `\Deleted` messages fail-closed in preflight.
   - EML tree export of 12 items with safe folder IDs (`fld_001`), atomic file write, and sidecar `manifest.json` validation (with zero secret leakage).
   - MBOXRD export verifying accurate From-escaping roundtrip with `MboxrdRecordReader`.
   - Rejection of tampered `JobOutputDir` escaping outside authorized parent directory.
   - Handle-free export restart succeeding using `plan.TargetDirectoryPath`.
4. `engine/BitigMail.Engine.Tests/BridgeJournalAndResumeTests.cs` (7 tests)
   - Import resume: `AppendIntent` with exactly 1 keyword match reconciles to `Verified` with zero additional appends.
   - Import resume: `AppendIntent` with 0 keyword matches marks `NeedsAttention` and never issues automatic re-appends.
   - Import resume: `AppendIntent` with multiple keyword matches marks `NeedsAttention`.
   - Export resume: Output file drift / tampering detected during verification.
   - Idempotency: same key + same plan returns same job.
   - Idempotency: same key + different plan throws typed 409 Conflict.
   - Idempotency: same plan + different key returns existing job (no duplicate jobs).
5. `engine/BitigMail.Engine.Tests/BridgeSecurityAndScaleTests.cs` (13 tests)
   - `BridgeApi_ImportPreview_HidesExceptionMessage_And_SensitivePath`: verifies injected secret sentinels and absolute paths never leak in import preview error responses.
   - `BridgeApi_ImportStart_HidesExceptionMessage_And_SensitivePath`: verifies secret sentinels and paths never leak during import start.
   - `BridgeApi_ImportResume_HidesExceptionMessage_And_SensitivePath`: verifies secret sentinels and paths never leak during import resume.
   - `BridgeApi_ExportPreview_HidesExceptionMessage_And_SensitivePath`: verifies secret sentinels and paths never leak during export preview.
   - `BridgeApi_ExportStart_HidesExceptionMessage_And_SensitivePath`: verifies secret sentinels and paths never leak during export start.
   - `BridgeApi_ExportResume_HidesExceptionMessage_And_SensitivePath`: verifies secret sentinels and paths never leak during export resume.
   - `BridgeApi_ImportStart_CorruptPlan_ReturnsBadRequestOrConflict`: verifies corrupted preview plan JSON produces safe 400/409 instead of uncaught 500.
   - `BridgeApi_ExportStart_CorruptPlan_ReturnsBadRequestOrConflict`: verifies corrupted export preview plan JSON produces safe 400/409 instead of uncaught 500.
   - `BridgeImport_HandleFreeRestart_RevalidatesEntireSourceSet_BeforeFirstAppend_SecondItemModifiedBlocks`: tests empty registry and new JobManager; 2nd of 2 items altered on disk blocks fail-closed with 0 appends.
   - `BridgeImport_HandleFreeRestart_Succeeds_WhenAllSourcesUnmodified`: verifies clean handle-free resume completes all items when disk matches frozen plan fingerprint.
   - `BridgeExport_JobOutputDir_SiblingDirectoryTamper_Rejected`: verifies that altering `JobOutputDir` to point to an unauthorized sibling directory is blocked with a security violation.
   - `BridgeScale_Over50Sources_EML_Fixture_AcceptedAtServiceAndPreviewLevel`: ingests the 72-item `over50-fixture.json` fixture, runs through import preview service, verifying accurate eligible item count, date filtering, and zero vendor SDK usage.
   - `BridgeApi_PreservesExplicitControlledDomainMessages`: verifies explicitly whitelisted business domain messages (e.g. invalid date ranges, missing accounts) are preserved for UI clarity while general exceptions are sanitized.

---

## 3. Routes & API Contracts

All endpoints enforce existing Origin/Host validation, session verification, and `Cache-Control: no-store` policies.

### 3.1 Import Endpoints
- `POST /api/transfer/bridge/import/preview`
  - Body:
    ```json
    {
      "companyId": "company-1",
      "projectId": "project-1",
      "sourceHandle": "msrc_0191eb9a7c2a71b091730428ad5e0001",
      "targetAccountId": "acc_0191eb9a7c2a71b091730428ad5e0002",
      "selectedFolders": ["INBOX", "Arşiv"],
      "targetFolderMappings": { "Arşiv": "Archive" },
      "startDate": "2024-01-01",
      "endDate": "2024-01-15"
    }
    ```
  - Response: `BridgeImportPreviewResponse` (PreviewId, CanTransfer, TotalSourceItems, EligibleItemsCount, ExcludedCount, Folders summary, BlockerReason).
- `GET /api/transfer/bridge/import/preview/{previewId}?companyId=...&projectId=...`
  - Retrieves frozen preview response from durable storage.
- `POST /api/transfer/bridge/import/start`
  - Body: `{"previewId": "bprev_...", "idempotencyKey": "uuid-v4", "companyId": "...", "projectId": "..."}`
  - Response: `LocalJobRecord` (`jobKind: "bridge-import"`).
- `POST /api/transfer/bridge/import/resume`
  - Body: `{"jobId": "job-...", "companyId": "...", "projectId": "..."}`
  - Response: `LocalJobRecord`.

### 3.2 Export Endpoints
- `POST /api/transfer/bridge/export/preview`
  - Body:
    ```json
    {
      "companyId": "company-1",
      "projectId": "project-1",
      "sourceAccountId": "acc_0191eb9a7c2a71b091730428ad5e0002",
      "targetDirHandle": "dir_0191eb9a7c2a71b091730428ad5e0003",
      "targetFormat": "eml-tree",
      "selectedFolders": ["INBOX"],
      "startDate": "2024-01-01",
      "endDate": "2024-01-15"
    }
    ```
  - Response: `BridgeExportPreviewResponse` (PreviewId, CanTransfer, TotalSourceItems, EligibleItemsCount, ExcludedCount, DeletedExcludedCount, Folders summary, BlockerReason).
- `GET /api/transfer/bridge/export/preview/{previewId}?companyId=...&projectId=...`
  - Retrieves frozen preview response from durable storage.
- `POST /api/transfer/bridge/export/start`
  - Body: `{"previewId": "bprev_...", "idempotencyKey": "uuid-v4", "companyId": "...", "projectId": "..."}`
  - Response: `LocalJobRecord` (`jobKind: "bridge-export"`).
- `POST /api/transfer/bridge/export/resume`
  - Body: `{"jobId": "job-...", "companyId": "...", "projectId": "..."}`
  - Response: `LocalJobRecord`.

---

## 4. Test Verification Results

### Build (Release)
Command: `.\.tools\dotnet\dotnet.exe build -c Release engine\BitigMail.Engine.Tests\BitigMail.Engine.Tests.csproj`  
Result: **Build succeeded with 0 Warnings and 0 Errors.**

### Full Test Suite (Release)
Command: `.\.tools\dotnet\dotnet.exe test -c Release engine\BitigMail.Engine.Tests\BitigMail.Engine.Tests.csproj`  
Result:
```
Başarılı!  - Başarısız:     0, Başarılı:   362, Atlanan:     0, Toplam:   362, Süre: 13 s - BitigMail.Engine.Tests.dll (net8.0)
```
- Pre-existing baseline tests: 305
- Root-exclusive critical tests: 26 (including `BridgeMimeBytePolicyCriticalTests` and `BridgeJournalCriticalTests`)
- TASK-017 Original Bridge tests: 18
- TASK-017 Security Hardening, Preflight Revalidation & Scale tests: 13
- Total passed: **362 / 362 (100% pass rate)**

---

## 5. Known Blockers & Readiness for Next Phases

### 5.1 Actual API & TestingHost Lab Integration
- `BitigMail.TestingHost` DI configuration is updated to register `BridgeTransferJournal`, `BridgeImportPreviewService`, and `BridgeExportPreviewService`.
- Dovecot test instance on port 5143 / TestingHost on port 6175 has separate task folders ready. Real network roundtrip tests with Python standard library mail parsing can execute directly against these endpoints.
- No new third-party dependencies were introduced, ensuring zero license conflicts.

### 5.2 Frontend UI Integration (Next Scope)
- The frontend `Transfers` view needs dedicated UI tabs or modes for:
  - File → Account (Import): EML directory / Mbox selection via existing file dialogs, target account dropdown, folder mapping, and date range filters.
  - Account → File (Export): Source account dropdown, folder multiselect, target directory selection, format picker (`eml-tree` vs. `mboxrd`), and date filters.
- Job Center must recognize `bridge-import` and `bridge-export` job kinds and display `BridgeTransferReportDetail` (transferred/verified item counts, folder summaries, and audit logs).
- Idempotency and error states (`NeedsAttention`, `Interrupted`, `Failed`) must render with clear restart/resume buttons.
