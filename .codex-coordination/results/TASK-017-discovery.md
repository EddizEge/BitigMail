# TASK-017 Architecture Discovery Gate

**TASK_ID:** TASK-017  
**STATUS:** DISCOVERY COMPLETE / PENDING AUTHORITATIVE CONTRACT  
**EXECUTOR:** GEMINI (Official Antigravity)  
**CONTROLLER:** SOL / ASTRA HIGH  
**SCOPE:** Bounded read-only architecture discovery for bidirectional EML/EML-tree/mboxrd ↔ IMAP file/account bridge. No product, source, test, or documentation file modified. No servers started, no accounts accessed, no mail modified or reseeded.

---

## 1. Exact Current Integration Seams & File Paths

### A. Direction 1: EML / EML-tree / mboxrd → IMAP (File to Account Import)
1. **Source Inspection & Parsing**:
   - [`MimeSourceInspector.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.Engine/Storage/MimeSourceInspector.cs):
     - `BuildEmlFilesManifest(IEnumerable<string> filePaths, string? defaultFolder)`: Scans selected files, rejects reparse points/symlinks, computes per-file SHA-256 and `AggregateFingerprint`.
     - `BuildEmlDirectoryManifest(string directoryPath)`: Recursively scans `.eml` tree, enforces boundary checks, computes relative path hierarchy, deterministic physical ordinals.
     - `BuildMboxManifest(string mboxFilePath, string? defaultFolder)`: Reads MBOX records via `MboxrdRecordReader`, computes per-record SHA-256 and `AggregateFingerprint`.
     - `RevalidateManifest(MimeSourceManifest manifest)`: Pre-execution validation ensuring files/records exist and hashes have not drifted.
     - `AnalyzeAsync(MimeSourceManifest manifest, string handle)`: Preflight checks, folder grouping, sample extraction.
   - [`MboxrdRecordReader.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.Engine/Storage/MboxrdRecordReader.cs):
     - `EnumerateRecords(Stream stream)`: Yields `MboxrdRecord` (`Ordinal`, `RawMimeBytes`, `EnvelopeFrom`).
     - `UnescapeMboxrdLine(byte[] line)`: Performs exact one-pass unescaping (`^>+From ` -> removes exactly one leading `>`).
   - [`MimeSelectionEngine.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.Engine/Storage/MimeSelectionEngine.cs):
     - `EvaluateSelection(...)`: Evaluates folder selection and date filters against `MimeFidelityPolicy.OriginalDate(mime)` within UTC+03:00 inclusive day bounds. Returns `(SelectionPreviewResult, RegisteredSelection)`.

2. **Handle Management & Registry**:
   - [`FileHandleRegistry.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Dialogs/FileHandleRegistry.cs):
     - `RegisterMimeSource(manifest, displayPath)`: Returns opaque handle `msrc_<guid>`.
     - `AttachMimeAnalysis(handle, analysis)`: Binds analysis result to handle.
     - `RegisterSelection(selection)`: Registers frozen selection `msel_<guid>`.
   - [`WinFormsFilePickerService.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Dialogs/WinFormsFilePickerService.cs):
     - `PickMimeSourceAsync(string mode)`: Native dialog for `eml-files`, `eml-tree`, or `mbox`.
   - [`TestingFilePickerService.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.TestingHost/TestingFilePickerService.cs):
     - Programmatic test fixture routing for `corpus-eml`, `corpus-tree`, `corpus-mbox`, and `escape-edges`.

3. **Target IMAP Account Resolution & Appending**:
   - [`ImapAccountStore.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Imap/ImapAccountStore.cs):
     - Resolves target account metadata, host, port, TLS mode, and version.
   - [`IImapCredentialResolver.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Security/ImapCredentialProtector.cs):
     - Resolves protected password or OAuth token.
   - [`IImapTransferClient.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Imap/Transfer/IImapTransferClient.cs) & [`MailKitTransferClient.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Imap/Transfer/MailKitTransferClient.cs):
     - `SupportsUserKeywordsAsync(folderPath, ct)`: Checks target folder supports `PERMANENTFLAGS \*` for safe per-item keyword token tagging.
     - `AppendMessageAsync(folderPath, message, flags, keywords, internalDate, ct)`: Public MailKit APPEND with FormatOptions (DOS newlines, hidden headers cleared, ensure newline true).
     - `SearchByKeywordAsync(folderPath, keyword, ct)`: Searches for the per-item idempotency keyword on target.
     - `FetchAndVerifyAsync(folderPath, uid, ct)`: Independent post-append verification fetching raw bytes, SHA-256, flags, internal date, and keywords.

4. **Job Orchestration & Durable Journal**:
   - [`JobManager.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Jobs/JobManager.cs):
     - Concurrency gating (`_activeRunningJobId`), idempotency index, recovery on startup.
   - [`ImapTransferJournal.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Imap/Transfer/ImapTransferJournal.cs):
     - Atomic write of frozen plans (`imap-transfers/plans/<planId>.json`) and journal states (`imap-transfers/journals/<jobId>.json`).

---

### B. Direction 2: IMAP → EML-tree / mboxrd (Account to File Export)
1. **Source IMAP Reading & Inspection**:
   - [`IImapTransferClient.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Imap/Transfer/IImapTransferClient.cs) & [`MailKitTransferClient.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Imap/Transfer/MailKitTransferClient.cs):
     - `InspectSourceFolderAsync(folderPath, ct)`: Opens folder strictly in read-only `EXAMINE` mode (`FolderAccess.ReadOnly`). Fetches raw MIME stream via `GetStreamAsync(uid, ct)` (`BODY.PEEK[]`), computes raw SHA-256, fetches `FLAGS` and `INTERNALDATE`, and extracts original MIME Date header.
     - `GetFolderUidValidityAsync(folderPath, ct)`: Enforces `UIDVALIDITY` invariant checks before and after export.
   - [`ImapTransferPreviewService.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Imap/Transfer/ImapTransferPreviewService.cs):
     - Date filtering on `OriginalMimeDateUtc` with explicit timezone validation.

2. **Target File / Directory Handle Management**:
   - [`FileHandleRegistry.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Dialogs/FileHandleRegistry.cs):
     - `RegisterOutputDir(fullPath)`: Registers target directory handle `dir_<guid>`.
     - Target MBOX file currently has no dedicated picker/handle type (only target PST `tgt_<guid>` exists).
   - [`WinFormsFilePickerService.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Dialogs/WinFormsFilePickerService.cs):
     - `PickOutputDirAsync()`: Folder browser dialog.

3. **Target Serialization & File Writing**:
   - **EML-tree**: Must create directory structure corresponding to IMAP folder names, write each message as an `.eml` file atomically (write to temp, flush, move).
   - **mboxrd**: Currently, **no C# mboxrd writer exists in the engine**. `MboxrdRecordReader.cs` only parses; reference writer exists in Python (`scripts/generate_mail_corpus.py`). An mboxrd writer requires writing `From ` envelope delimiter lines and escaping body lines matching `^>*From ` by prepending `>`.

4. **Job Orchestration & Reporting**:
   - [`JobManager.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Jobs/JobManager.cs) & [`LocalJobRecord.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Jobs/LocalJobRecord.cs):
     - Needs job kind distinction, progress tracking, and audit report generation.
   - [`ConversionReport.cs`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.Engine/Models/ConversionReport.cs):
     - Durable report saved to `reports/<jobId>.json`.

---

### C. Frontend UI & Hooks Integration Points
- **Tabs & Navigation**:
  - [`TransfersTabView.tsx`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/prototype/src/components/transfer/TransfersTabView.tsx):
    - Under `currentOperation === 'migration'`, renders mode switcher (`real` vs `sample`). In `real` mode, currently renders `<ImapTransferWorkflow ... />`.
    - Needs UI surface for choosing transfer direction:
      1. `IMAP ↔ IMAP` (Account to Account)
      2. `Dosya → IMAP` (EML/MBOX Import)
      3. `IMAP → Dosya` (Account Export to EML-tree/MBOX)
- **Workflow Components**:
  - [`ImapTransferWorkflow.tsx`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/prototype/src/components/transfer/ImapTransferWorkflow.tsx):
    - Stepper: Select Accounts & Folders → Configure Mappings & Date Range → Preview & Preflight → Execute & Audit Report.
  - [`LocalMimeWorkflow.tsx`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/prototype/src/components/convert/LocalMimeWorkflow.tsx):
    - Existing MIME source picker, analysis, and folder tree component used for MIME → PST.
- **Hooks & State**:
  - [`useImapTransfer.ts`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/prototype/src/hooks/useImapTransfer.ts): Manages IMAP accounts, folder tree, date bounds, preview generation, start/resume, and polling.
  - [`useMimeImport.ts`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/prototype/src/hooks/useMimeImport.ts): Manages MIME picker, source analysis, folder filtering, preview, and start.
- **Job Center**:
  - [`JobCenter.tsx`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/prototype/src/components/jobs/JobCenter.tsx):
    - Displays job status, progress, stage, error message, and report download button. Mapped by `jobKind`.

---

## 2. Existing Reusable Patterns vs. Architectural Mismatches

| Component / Layer | Existing Reusable Pattern | Architectural Mismatch for Bridge |
| :--- | :--- | :--- |
| **Source / Target Handles** | `msrc_<guid>` encapsulates `MimeSourceManifest` (EML files, EML tree, MBOX). `dir_<guid>` encapsulates output directory. | IMAP accounts are identified by `accountId` (scoped to company/project), NOT file handles. Bridge must accept hybrid source/target combinations (File Handle ↔ Account ID). No handle exists for single output MBOX file. |
| **Preview & Plan Models** | `ImapTransferPreviewResponse` and `ImapTransferPlan` freeze item-level metadata (`SourceSha256`, `InternalDateUtc`, `OriginalMimeDateUtc`, `Flags`, `Keywords`). | `ImapTransferPlannedItem` mandates `SourceUid` (`uint`) and `SourceUidValidity` (`uint`). Files have NO UIDs or UIDVALIDITY. Files have paths, relative paths, and record ordinals. In IMAP → File, target has no IMAP folder path or UID. |
| **Durable Journal** | `ImapTransferJournal` with atomic writes (`.tmp` + flush + atomic move) and strict DamagedRecord validation. Per-item status machine (`Planned` → `AppendIntent` → `Verified`). | `ImapTransferJournalEntry` requires `SourceUid`, `SourceUidValidity`, `TargetFolder`. For IMAP → File, target is on the filesystem (no target UID/UIDVALIDITY; verification is based on local file hash and size). |
| **Append Idempotency & Reconciliation** | Random 128-bit `bitigmail_<token>` keyword attached to appended message and searched via `SEARCH KEYWORD`. | Reusable for File → IMAP (since target is IMAP and supports user keywords). NOT applicable for IMAP → File (filesystem files don't support IMAP keyword search; filesystem atomicity relies on write-to-temp + rename). |
| **Job Concurrency & Lifecycle** | Single running job lock (`_activeRunningJobId`), startup recovery of in-flight jobs to `interrupted`, durable report saved before job marks `completed`. | `JobKind` in `LocalJobRecord` is currently limited to `"convert"`, `"split"`, `"mime-import"`, `"imap-transfer"`. Bridge jobs require distinct or extended `JobKind` identifiers. |
| **Date & Filter Engine** | `OstSelectionEngine.ParseDateBoundaries` with UTC+03:00 inclusive day bounds and `MimeFidelityPolicy.OriginalDate` explicit timezone requirement. | Fully reusable. Both MIME sources and IMAP sources already use this exact policy. |
| **MBOXRD Escaping** | `MboxrdRecordReader.cs` performs verified one-pass unescaping of `^>+From ` lines. | Engine has **no mboxrd writer**. Writing mboxrd requires prepending `>` to lines matching `^>*From ` and adding `From ` envelope separators. |

---

## 3. Proposed DTO & API Shapes (Options for Root Decision, Non-Authoritative)

The authoritative contract will decide the API layout. Here are three concrete options:

### Option A: Dedicated Directional Endpoints & DTOs (Recommended for Strict Typing)
Separate endpoints for Import and Export, keeping payload contracts explicit and fail-closed:

```typescript
// 1. File -> IMAP Import
POST /api/transfer/bridge/import/preview
Request: {
  companyId: string;
  projectId: string;
  sourceHandle: string; // msrc_<guid>
  targetAccountId: string;
  selectedFolders?: string[];
  targetFolderMappings?: Record<string, string>; // Source mappedFolder -> Target IMAP folder
  startDate?: string;
  endDate?: string;
}
Response: BridgeImportPreviewResponse {
  previewId: string;
  sourceHandle: string;
  sourceFingerprint: string;
  targetAccountId: string;
  targetAccountVersion: number;
  totalSourceItems: number;
  eligibleItemsCount: number;
  excludedCount: number;
  folders: BridgeFolderSummary[];
  canTransfer: boolean;
  blockerReason?: string;
}

POST /api/transfer/bridge/import/start
Request: {
  previewId: string;
  idempotencyKey: string;
  companyId: string;
  projectId: string;
}

// 2. IMAP -> File Export
POST /api/transfer/bridge/export/preview
Request: {
  companyId: string;
  projectId: string;
  sourceAccountId: string;
  targetFormat: "eml-tree" | "mboxrd";
  targetDirHandle: string; // dir_<guid>
  selectedFolders: string[];
  startDate?: string;
  endDate?: string;
}
Response: BridgeExportPreviewResponse {
  previewId: string;
  sourceAccountId: string;
  sourceAccountVersion: number;
  targetFormat: "eml-tree" | "mboxrd";
  targetDirHandle: string;
  totalSourceItems: number;
  eligibleItemsCount: number;
  folders: ImapTransferFolderSummary[];
  canTransfer: boolean;
  blockerReason?: string;
}

POST /api/transfer/bridge/export/start
Request: {
  previewId: string;
  idempotencyKey: string;
  companyId: string;
  projectId: string;
}
```

### Option B: Polymorphic Bridge API (Single Unified Route)
Polymorphic endpoints with a discriminated union for `source` and `target`:

```typescript
POST /api/transfer/bridge/preview
Request: {
  companyId: string;
  projectId: string;
  source: 
    | { kind: "file"; handle: string }
    | { kind: "imap"; accountId: string };
  target: 
    | { kind: "imap"; accountId: string; folderMapping?: Record<string, string> }
    | { kind: "file"; dirHandle: string; format: "eml-tree" | "mboxrd" };
  selectedFolders?: string[];
  startDate?: string;
  endDate?: string;
}
```
*Assessment:* Highly flexible, but requires complex polymorphic validation, polymorphic plan schemas, and discriminators in both backend and frontend.

### Option C: Parameterized Extension of Existing Endpoints
Extend `ImapTransferPreviewRequest` to optionally accept `sourceHandle` instead of `sourceAccountId`, and `targetDirHandle` + `targetFormat` instead of `targetAccountId`.  
*Assessment:* Risks breaking existing IMAP-to-IMAP invariants and muddying type safety. Not recommended.

---

## 4. Dependency Assessment

- **Current NuGet Packages in `BitigMail.Engine.csproj` & `BitigMail.LocalHost.csproj`**:
  - `MimeKit` (4.17.0): RFC 5322 parsing, streaming, format options, MIME structure inspection.
  - `MailKit` (4.17.0): IMAP client (`EXAMINE`, `BODY.PEEK`, `APPEND`, `SEARCH`, `FETCH`).
  - `Microsoft.Identity.Client` (4.89.0): MSAL for Microsoft 365 OAuth.
  - `Aspose.Email` (24.8.0): PST engine with trial calibration (TASK-010..013). **Not needed and must NOT be used for EML/MBOX ↔ IMAP bridge.**
  - `System.Formats.Asn1` (8.0.1).
- **Need for New Dependencies**: **NONE.**
  - Reading EML: MimeKit `MimeMessage.Load` is already present.
  - Reading MBOX: `MboxrdRecordReader.cs` is already present.
  - Writing EML: `MimeMessage.WriteTo(...)` or direct raw byte stream write is standard.
  - Writing MBOXRD: Escaping (`^>*From ` -> `>...`) and envelope prepending (`From ...`) requires fewer than 80 lines of clean, deterministic C# code. Reference logic is fully established in `scripts/generate_mail_corpus.py`.
- **Verdict**: Reuse existing MailKit, MimeKit, and native .NET streams. **Add zero new dependencies.**

---

## 5. Critical Integrity & Operational Questions for Contract to Settle

The authoritative contract (`docs/FILE_ACCOUNT_BRIDGE_WORKFLOW.md`) must explicitly resolve:

1. **Source Protection**:
   - For EML/MBOX sources: Files must be opened with `FileAccess.Read` and `FileShare.Read`. Must verify files are never deleted, truncated, moved, or renamed under any failure condition.
   - For IMAP sources: Must strictly enforce `FolderAccess.ReadOnly` (`EXAMINE`). No `STORE +FLAGS (\Deleted)`, no `EXPUNGE`, no `CLOSE (true)` under any circumstances.
2. **Resume & Idempotency**:
   - **File → IMAP**: Can per-item `bitigmail_<guid>` keywords be used on all target folders? What if target folder lacks `PERMANENTFLAGS \*` support? (Fail-closed in preview vs. alternative idempotency token).
   - **IMAP → EML-tree**: How does resume identify already-written files? (By comparing destination file SHA-256 against source SHA-256).
   - **IMAP → MBOX**: How does resume handle interrupted MBOX writing? (Write to `.tmp` file and rename upon job completion, OR journal byte offsets to truncate trailing partial records on resume).
3. **Output Atomicity & Crash Windows**:
   - EML-tree: Every file write must be atomic (`{path}.tmp.{guid}` -> flush to disk -> `File.Move(..., overwrite: true)`).
   - MBOX: If written directly, a process crash leaves an incomplete record at the end of the file. Recommend writing to a staged temp file or maintaining a strictly rollbackable byte count.
4. **Collision & Naming Rules**:
   - EML-tree filenames: Options:
     - (a) `{uid}.eml` (Deterministic, collisions impossible within same folder).
     - (b) `{ordinal:D6}_{sanitized_subject}.eml` (Human-readable, but risk of collisions and filename length overflows).
     - (c) `{uid}_{sha256[..8]}.eml`.
   - Windows reserved directory/file names (`CON`, `PRN`, `AUX`, `NUL`, `COM1..9`, `LPT1..9`) and reserved characters (`\ / : * ? " < > |`): Contract must define deterministic path sanitization.
5. **Filter, Date & Folder Semantics**:
   - Date filtering: Must confirm whether filter strictly evaluates `OriginalMimeDateUtc` (RFC 5322 Date header with explicit timezone) matching TASK-013/014, and whether missing dates are excluded.
   - Folder mapping: For EML-tree, does relative directory hierarchy map 1:1 to IMAP folder hierarchy? For MBOX (flat container), what is the default target IMAP folder?
6. **Raw MIME Byte Fidelity**:
   - File → IMAP: Does MailKit re-serialize the message upon APPEND (using `ImapSerializationAssumptions`), and must the preflight check ensure byte equality, or does it accept standard RFC 5322 re-serialization?
   - IMAP → File: When fetching from IMAP via `BODY.PEEK[]`, saving raw bytes unmodified guarantees 100% byte fidelity and identical SHA-256. Contract should mandate saving exact raw bytes.
7. **Flags, InternalDate & Keywords**:
   - File → IMAP: EML files do not have native IMAP flags. What initial flags are applied (`\Seen` vs `None`)? What `INTERNALDATE` is passed to APPEND? (MIME Date vs UTC now vs file modification time).
   - IMAP → File: Standard EML cannot store IMAP flags without altering headers (e.g. `X-Status`). Altering headers breaks the raw MIME SHA-256. Should flags be preserved solely in the companion audit report (`manifest.json` / `report.json`) to keep EML bytes intact?
8. **Duplicate Handling**:
   - `msg-01.eml` and `msg-02.eml` are byte-identical duplicates. Both must be imported as distinct items (separate UIDs, unique BitigMail keywords).
   - `msg-03.eml` and `msg-04.eml` share the same `Message-ID` but have different content. Re-affirms that `Message-ID` cannot be used for deduplication or indexing.

---

## 6. Concrete Test Seams & Dovecot / Fixture Acceptance Plan

### A. Test Seams
1. **Unit & State Machine Tests (`BitigMail.Engine.Tests`)**:
   - `BridgeImportTests.cs`:
     - Test EML files, EML tree, and MBOX manifest ingestion with `MimeSourceInspector`.
     - In-memory mock `IImapTransferClient` (`FakeImapTransferClient`) to test plan creation, journal transitions, keyword stamping, append failures, and interrupted resumes.
   - `BridgeExportTests.cs`:
     - Test IMAP folder inspection, EML tree writing with path sanitization, and MBOXRD round-trip serialization.
     - Verify MBOXRD writer escapes `^>*From ` correctly and reader unescapes losslessly.
2. **TestingHost Integration Tests (`BitigMail.TestingHost` on port 6175)**:
   - Full end-to-end API pipeline tests using `TestingFilePickerService` with approved fixtures.
   - Verify `/api/transfer/bridge/...` endpoints return expected DTOs, HTTP 400 on invalid paths, HTTP 409 on version conflicts.
3. **Frontend Vitest & Component Tests (`prototype/src/tests`)**:
   - Render tests for transfer tab direction switching.
   - Hook tests for bridge workflow, folder selection, date bounds, and report generation.

### B. Fixtures to Exercise
- `fixtures/mail-corpus-v1`:
  - 12 EML files in `eml/` (covering duplicates `msg-01`/`msg-02`, shared Message-ID `msg-03`/`msg-04`, Turkish attachments, binary payload, inline CID `msg-11`, and MBOXRD escapes in `msg-08`).
  - `corpus.mbox`: Unix MBOX with explicit mboxrd escaping.
  - `manifest.json`: Verification manifest with hard-coded SHA-256 hashes and filter oracles.
- `fixtures/mime-import-v1`:
  - `expected-mime.json`: Independent Python stdlib oracle baseline.
  - `escape-edges/`: Edge-case mboxrd escapes (raw, QP, base64).

### C. Live Dovecot Lab Acceptance Plan (`lab/task014/dovecot` on `127.0.0.1:5143`)
- **Safety Constraints**:
  - Do NOT delete, expunge, move, or modify existing seeded mail in `source`.
  - Do NOT reseed the original lab.
  - Use separate, freshly created target folders (e.g. `BridgeTest/Import_{runId}`) in the `target` user mailbox.
- **Bi-directional Verification Sequence**:
  1. **File → IMAP**: Import `fixtures/mail-corpus-v1/eml` and `corpus.mbox` into Dovecot target folder. Run independent Python script to verify all 12 messages exist with exact headers, bodies, attachments, and internal dates.
  2. **IMAP → File**: Export Dovecot `source` mailbox (`INBOX`, `Gönderilenler`, `Projeler/İstanbul`) to temporary local directory (both EML tree and MBOXRD).
  3. **Verification**: Run independent Python script to compare exported EML/MBOX against Dovecot server contents and fixture expectations (checking SHA-256, attachments, headers, body fidelity).

---

## 7. Blockers & Uncertainties (No Implementation Claims)

1. **Pending Authoritative Contract**:
   - `docs/FILE_ACCOUNT_BRIDGE_WORKFLOW.md` is being authored by root. Architecture implementation cannot begin until this contract is published and reviewed.
2. **MBOX Export Crash-Window Semantics**:
   - Decision needed on whether export to MBOX uses an atomic temp-file replacement pattern (`.tmp` -> rename at end) or byte-offset tracking.
3. **EML Flag Storage Contract**:
   - Decision needed on whether IMAP flags are recorded purely in the durable audit report or if synthetic headers (e.g. `X-BitigMail-Flags`) are permitted in exported EMLs.
4. **Baseline Status**:
   - 305 backend tests and 90 frontend tests remain completely untouched and passing. Zero product/test changes made during this discovery gate.
