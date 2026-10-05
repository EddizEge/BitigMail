TASK_ID: TASK-010
STATUS: DONE
EXECUTOR: GEMINI
CONTROLLER: SOL 5.6 LIMITED

CHANGED_FILES:
- .gitignore
- README.md
- engine/README.md
- engine/BitigMail.Engine/BitigMail.Engine.csproj
- engine/BitigMail.Engine/Models/OutlookStorageFormatInfo.cs
- engine/BitigMail.Engine/Models/FolderSummary.cs
- engine/BitigMail.Engine/Models/SampleMessageSummary.cs
- engine/BitigMail.Engine/Models/PreflightCheckResult.cs
- engine/BitigMail.Engine/Models/OstAnalysisResult.cs
- engine/BitigMail.Engine/Models/ClientProjectContext.cs
- engine/BitigMail.Engine/Models/ConversionJobProgress.cs
- engine/BitigMail.Engine/Models/ConversionReport.cs
- engine/BitigMail.Engine/Storage/OutlookStorageInspector.cs
- engine/BitigMail.Engine/Storage/AttachmentHelper.cs
- engine/BitigMail.Engine/Storage/OstAnalyzer.cs
- engine/BitigMail.Engine/Storage/OstToPstConverter.cs
- engine/BitigMail.Engine/Storage/NonClosingStream.cs
- engine/BitigMail.LocalHost/BitigMail.LocalHost.csproj
- engine/BitigMail.LocalHost/Security/SecurityConfig.cs
- engine/BitigMail.LocalHost/Security/SessionManager.cs
- engine/BitigMail.LocalHost/Security/LocalSecurityMiddleware.cs
- engine/BitigMail.LocalHost/Dialogs/FilePickResult.cs
- engine/BitigMail.LocalHost/Dialogs/FileHandleRegistry.cs
- engine/BitigMail.LocalHost/Dialogs/IFilePickerService.cs
- engine/BitigMail.LocalHost/Dialogs/WinFormsFilePickerService.cs
- engine/BitigMail.LocalHost/Jobs/LocalJobRecord.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.cs
- engine/BitigMail.LocalHost/Program.cs
- engine/BitigMail.TestingHost/BitigMail.TestingHost.csproj
- engine/BitigMail.TestingHost/TestingFilePickerService.cs
- engine/BitigMail.TestingHost/Program.cs
- engine/BitigMail.Engine.Tests/BitigMail.Engine.Tests.csproj
- engine/BitigMail.Engine.Tests/SecurityTests.cs
- engine/BitigMail.Engine.Tests/StorageValidationTests.cs
- engine/BitigMail.Engine.Tests/PreflightAndTrialLimitTests.cs
- engine/BitigMail.Engine.Tests/TargetSafetyAndIntegrityTests.cs
- engine/BitigMail.Engine.Tests/JobManagerTests.cs
- engine/BitigMail.Engine.Tests/FidelityAndMultisetVerificationTests.cs
- scripts/start-local-engine.ps1
- scripts/stop-local-engine.ps1
- scripts/start-testing-engine.ps1
- scripts/stop-testing-engine.ps1
- prototype/index.html
- prototype/src/App.tsx
- prototype/src/types/index.ts
- prototype/src/types/localEngine.ts
- prototype/src/api/localEngineClient.ts
- prototype/src/hooks/useLocalEngine.ts
- prototype/src/components/ui/Icons.tsx
- prototype/src/components/layout/StatusBar.tsx
- prototype/src/components/convert/LocalConvertWorkflow.tsx
- prototype/src/components/transfer/TransfersTabView.tsx
- prototype/src/components/jobs/JobCenter.tsx
- prototype/src/components/reports/ReportsView.tsx
- prototype/src/tests/localEngineClient.test.ts
- prototype/tests/e2e/local-convert-workflow.spec.ts
- prototype/tests/e2e/testinghost-real-convert.spec.ts
- .codex-coordination/results/TASK-010.md

SUMMARY:
- Implemented bounded first local OST workflow connecting genuine OST analysis, preflight verification, and new Unicode PST conversion to the BitigMail prototype exactly per docs/LOCAL_OST_WORKFLOW.md and TASK-010.
- Implemented Astra Security & Integrity Acceptance Criteria:
  1. Exact Host & Origin Validation Pre-OPTIONS:
     - `LocalSecurityMiddleware` validates exact Host (`127.0.0.1:6174` in production, `127.0.0.1:6175` in testing) and exact Origin `http://127.0.0.1:5173` BEFORE any OPTIONS or CORS handling.
     - Reject multiple Host, multiple Origin, multiple session token, and multiple Content-Type header values.
     - Reject missing, misleading (e.g. text/plain), or non-JSON Content-Type on mutating requests. No localhost or null origin bypasses.
  2. Multi-Session Concurrency:
     - `SessionManager` uses a thread-safe concurrent dictionary of valid tokens. New session creation does not invalidate concurrent tabs or reloaded pages. Tokens are never logged or persisted.
  3. Server-Bound Verified Analysis & Opaque Path Authority:
     - `OstAnalysisResult` is attached and bound directly to `SourceHandleEntry` in `FileHandleRegistry`.
     - `/api/jobs/start` ignores untrusted client parameters (`ExpectedSourceSha256`, `EstimatedTotalItems`) and enforces server-bound verified hash and item count, rejecting modified sources or missing analyses.
     - File-operation inputs remain server-issued opaque handles (`src_*`, `tgt_*`). Native-picker-derived read-only display paths and the authoritative completed output path are exposed/persisted for user visibility, but are never accepted back as filesystem authority.
  4. Shared Architecture for TestingHost:
     - `TestingHost` uses the identical `LocalSecurityMiddleware` and `LocalEngineApiEndpoints` as production.
     - Only picker (`TestingFilePickerService`), port (`6175`), and isolated runtime directory (`runtime/testing-engine`) differ.
     - No weaker copied middleware, no free-path picker endpoint, and zero state/history shared with production `runtime/local-engine`.
  5. Strict RFC Idempotency:
     - Same idempotency key with identical request parameters returns the existing job.
     - Same key with conflicting parameters throws `InvalidOperationException` and returns HTTP 409 Conflict.
  6. Simplified User-Facing Interruption Messaging:
     - Interrupted jobs recovered on startup display clean, professional user text: "Uygulama veya oturum kapandığı için işlem kesintiye uğradı. Veri bütünlüğünü korumak için işlem tamamlanmadı olarak işaretlendi."
  7. Exhaustive OstAnalyzer Inventory & Production Preflight Gate:
     - Evaluated directly via `OstAnalyzer.EvaluatePreflight(folders)`.
     - Counts attachments across ALL items throughout the entire store, not capping at 10 samples.
     - Enumerates physical items in root folder; ensures root items cannot disappear.
     - Enforces globally unique folder paths across sibling collisions.
     - Never swallows folder or item enumeration/extraction errors; unreadable/unsupported items yield explicit blockers (`CanConvert = false`).
  8. Single-Lock Source Invariance & NonClosingStream:
     - `NonClosingStream` decorates `ostStream` so third-party disposal by Aspose does not prematurely close the underlying stream.
     - Both `OstAnalyzer` and `OstToPstConverter` hold a single open read-only stream lock (`FileShare.Read`) covering format inspection, SHA-256 computation, and PersonalStorage inventory/conversion, eliminating the hash-before-lock race condition and preventing external mutations.
     - `OstToPstConverter` independently validates 50-item evaluation limit per folder and forbids empty-list fallbacks.
  9. Consuming Physical Multiset Reopen Verification:
     - Reopened PST verification uses consuming multiset matching (one-to-one physical pairing).
     - Compares Subject, Message-ID, Sender, DisplayTo, DateUtc, newline-normalized body SHA-256, attachment Name, Size, SHA-256, and TASK-009 inline Content-ID (`PR_ATTACH_CONTENT_ID_W`/`A`).
     - Any difference or missing/extra item/attachment sets `VerificationSuccess = false`.
     - Atomic move to final `.pst` occurs ONLY if verification succeeds 100%. Failure leaves no final success PST.
   10. PID Lifecycle & Script Safety:
      - `BitigMail.LocalHost` and `BitigMail.TestingHost` write their PID file strictly inside `ApplicationStarted.Register` (after successful TCP bind) at repo-root `runtime/local-engine/localhost.pid` and `runtime/testing-engine/testinghost.pid`.
      - On `ApplicationStopping`, only the matching process ID deletes the PID file.
      - `start-local-engine.ps1` and `start-testing-engine.ps1` report `[BAŞARILI]` only after active TCP port listen state is confirmed.
      - `stop-local-engine.ps1` and `stop-testing-engine.ps1` inspect `Win32_Process.CommandLine` and executable name to confirm project ownership before calling `Stop-Process`.
   11. Genuine OST Integrity Tests:
      - `TargetSafetyAndIntegrityTests` exercises real OST fixture `lab/ost-spike/input/bitigmail-lab-full.ost` (13 items, 4 attachments/CIDs), verifying that mutated OST copies and altered SHA-256 hashes are rejected with `InvalidOperationException`.
   12. Astra Visual & Copy Polish:
      - Removed `.NET 8 / Aspose 24.8` badge from `LocalConvertWorkflow`.
      - Workflow heading set to `OST → PST dönüştürme`.
      - Primary action button text set to `OST dosyası seç`.
      - Offline copy simplified to `Dönüştürmeye başlamak için BitigMail yerel hizmetini çalıştırın.` with start command snippet `.\scripts\start-local-engine.ps1` (removed technical jargon regarding ASP.NET Core, fake simulation, and security policies).
      - Suppressed demo counter text (`248 ileti filtreye uyuyor`) in `StatusBar` when in real Convert mode.
      - Page document title updated from `BitigMail — Sentetik Prototip` to `BitigMail — Yerel çalışma alanı` while preserving demo labels in other example flows.
   13. Real TestingHost E2E & Security Integration Acceptance (`prototype/tests/e2e/testinghost-real-convert.spec.ts`):
      - Spawns `BitigMail.TestingHost` strictly on `127.0.0.1:6175` with isolated runtime `runtime/testing-engine`.
      - Exercises HTTP security negatives against `TestingHost`:
        - Origin mismatch/missing rejected with 403 Forbidden.
        - Host mismatch/production port collision rejected with 400 Bad Request.
        - Missing/invalid session token rejected with 401 Unauthorized.
        - Missing/non-JSON Content-Type rejected with 415 Unsupported Media Type.
      - Runs full direct HTTP E2E on genuine 13-item fixture (`bitigmail-lab-full.ost`):
        - 13/13 items converted, 0 failed.
        - 4 attachments verified and Content-IDs preserved.
        - Source hash matched bit-for-bit (`sourceSha256Before == sourceSha256After`).
        - Reopened PST physical verification passed (`itemCountMatch == true`, `totalPhysicalItemsFound == 13`).
        - Target PST exists on disk under temporary path and is non-empty.
      - Runs full browser UI E2E against real `TestingHost`:
        - Confirms `127.0.0.1:6175 Çevrimiçi` status, heading, button text, and hidden demo status bar counter.
        - Selects real OST, completes analysis, selects target PST, starts conversion, and verifies rendered report card with 13/13 items, hash match, and CID verification.
    14. Contract Correction & Output Location Persistence (Astra Acceptance):
       - Reconciled security boundary: HTTP inputs strictly forbid arbitrary filesystem paths and remain bounded to opaque handles (`src_*`, `tgt_*`); no arbitrary filesystem read/open/path endpoints exist.
       - Server-derived read-only fields expose full paths selected by native pickers (`FilePickResult.DisplayPath`) and final output PST paths (`LocalJobRecord.OutputPath`, `ConversionReport.OutputPath`).
       - UI displays full source location (`data-testid="source-display-path"`) and full target location (`data-testid="target-display-path"`).
       - UI displays completed output location (`data-testid="output-location-path"`) in the verification report card with a dedicated `'Konumu kopyala'` copy action (`data-testid="copy-location-btn"`).
       - Across page reload / refresh, `useLocalEngine` restores the latest completed job and authoritative report from disk/memory (`runtime/local-engine/jobs` and `reports`), maintaining the completed output location and report card without losing state.

VERIFIED_EXECUTIONS:
- Backend Builds:
  - `& ".\.tools\dotnet\dotnet.exe" build engine/BitigMail.Engine/BitigMail.Engine.csproj -c Release`: Exit 0 (0 warnings, 0 errors).
  - `& ".\.tools\dotnet\dotnet.exe" build engine/BitigMail.LocalHost/BitigMail.LocalHost.csproj -c Release`: Exit 0 (0 warnings, 0 errors).
  - `& ".\.tools\dotnet\dotnet.exe" build engine/BitigMail.TestingHost/BitigMail.TestingHost.csproj -c Release`: Exit 0 (0 warnings, 0 errors).
  - `& ".\.tools\dotnet\dotnet.exe" build engine/BitigMail.Engine.Tests/BitigMail.Engine.Tests.csproj -c Release`: Exit 0 (0 warnings, 0 errors).
- Backend Unit Tests:
  - `& ".\.tools\dotnet\dotnet.exe" test engine/BitigMail.Engine.Tests/BitigMail.Engine.Tests.csproj`: Exit 0 (47 passed, 0 failed, 0 skipped).
    - Verified real production traversal: `OstAnalyzer.AnalyzeStorageCore` counting 15 attachments past 10th sample item.
    - Verified physical items at root folder via `OstAnalyzer` root discovery, `OstToPstConverter.ProcessFolderItems` root preservation, and `VerifyReopenedPst`.
    - Verified consuming multiset matching for same-subject items and duplicate/blank MessageId.
    - Verified that modified body, missing attachment, and changed CID each trigger verification failure and prevent final PST publication.
    - Verified `JobManager_ExposesAndPersistsOutputPath_AcrossRecovery` for output location exposure and persistence across runtime reloads.
- Frontend Builds & Tests:
  - `npm run typecheck` (in prototype): Exit 0 (0 errors).
  - `npm test` (in prototype, Vitest): Exit 0 (2 test files, 29 tests passed, 0 failed).
    - Added tests for `DisplayPath`, `OutputPath`, and verification that request bodies never send arbitrary paths.
  - `npm run lint` (in prototype): Exit 0 (0 errors).
  - `npm run build` (in prototype): Exit 0 (production assets generated).
  - `npx playwright test` (in prototype, Playwright): Exit 0 (64 passed, 2 skipped, 0 failed across desktop-reference, desktop-compact, and mobile-narrow).
    - Includes real TestingHost E2E verifying native picker display paths, output location on disk, 'Konumu kopyala' button interaction, and full page reload persistence.

RISKS:
- WinForms common file dialogs require an interactive Windows desktop session; headless testing uses `BitigMail.TestingHost` (port 6175) or mocked routes for automated E2E.
- Large OST files (> 50 items/folder) are blocked by the evaluation license preflight gate per Aspose evaluation constraints.

UNCERTAINTIES:
- Native OS dialog interaction is not directly automated by browser drivers (documented security boundary). Headless test adapter validates end-to-end conversion logic, contract negatives, and API state machine.

CAPABILITY_STATUS: VERIFIED_DONE

SOL_FINAL_VERIFICATION:
- Backend final: 48 passed, 0 failed, 0 skipped after restoring `/api/session/status` token enforcement and adding its middleware regression case.
- Frontend final: typecheck exit 0; lint exit 0; 32 unit tests passed; production build exit 0.
- Targeted browser/API acceptance: local convert workflow plus real TestingHost suite passed on desktop-reference and desktop-compact. The first combined long matrix had 19 passes and two mobile failures after port 6175 became unavailable mid-poll. The isolated mobile rerun then passed 4/4: HTTP security negatives, real 13-item conversion, real UI result, and older-job navigation/no-fake-pause. This records the observed evidence without attributing an unproven root cause.
- Real UI screenshot rerun: 3/3 passed and wrote analysis/result screenshots for 1660, 1366, and 390 widths under `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task010-qa/sol-real-*.png`.
- Real generated PST evidence retained under `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-qa/` (latest `headless-test-*.pst`, 271360 bytes); Astra's independently verified product PST and JSON evidence remain under `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task010-qa/astra-0b58e6981eff454f8ad1e9bfb9dad2a4.*`.
- Independent production boundary acceptance by ASTRA: 20/20 passed on normal port 6174; status requires token, concurrent tab tokens valid, Host/Origin/OPTIONS/JSON negatives rejected, testing-only endpoint returned 404, all 20 responses carried no-store, and native picker was not invoked. Evidence: `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task010-qa/astra-api-check.json`.
- Runtime final state: TestingHost 6175 stopped; production LocalHost listening on 127.0.0.1:6174 as PID 92428; frontend remains at http://127.0.0.1:5173/.
- Final narrow-screen QA: long target/output paths were made shrinkable and breakable without page overflow. On the real 390px conversion result, `document.documentElement.scrollWidth === window.innerWidth`; the targeted real UI test passed 1/1. The result-card locator screenshot is `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task010-qa/sol-real-result-card-mobile-narrow.png` and was visually inspected successfully.
- Independent final PST readback by ASTRA: job `job-12b5840e3c14`, SHA-256 `b1d76d3f49b344d333c4bb8486089b27972715b46dfdeffe9516869bccbbc151`; libpff export 13/13, folder counts 6/3/4, four attachment hashes, basic-header multiset and common-body differences all zero. Evidence: `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task010-qa/independent-e8ea28d1/report.json`.

RISKS:
- Native WinForms picker interaction is intentionally not browser-automated; the exact production API boundary was independently tested without opening it, while the deterministic TestingHost adapter exercised the complete real OST flow.
- The first long multi-project Playwright run lost TestingHost during the mobile direct-HTTP poll. The same four mobile acceptance scenarios passed immediately in an isolated rerun; root cause was not proven and is reported transparently.

UNCERTAINTIES:
- None blocking TASK-010 acceptance.
