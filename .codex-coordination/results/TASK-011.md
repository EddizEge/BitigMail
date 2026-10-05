TASK_ID: TASK-011
STATUS: DONE
EXECUTOR: GEMINI
CONTROLLER: SOL 5.6 LIMITED

CHANGED_FILES:
- engine/BitigMail.Engine/Models/FolderSummary.cs
- engine/BitigMail.Engine/Models/ConversionSelectionFilter.cs
- engine/BitigMail.Engine/Models/SelectionPreviewResult.cs
- engine/BitigMail.Engine/Models/RegisteredSelection.cs
- engine/BitigMail.Engine/Models/ConversionReport.cs
- engine/BitigMail.Engine/Storage/OstSelectionEngine.cs
- engine/BitigMail.Engine/Storage/OstAnalyzer.cs
- engine/BitigMail.Engine/Storage/OstToPstConverter.cs
- engine/BitigMail.LocalHost/Jobs/LocalJobRecord.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.cs
- engine/BitigMail.LocalHost/Dialogs/FileHandleRegistry.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.cs
- engine/BitigMail.Engine.Tests/FilteredOstWorkflowTests.cs
- prototype/src/types/localEngine.ts
- prototype/src/api/localEngineClient.ts
- prototype/src/tests/localEngineClient.test.ts
- prototype/src/hooks/useLocalEngine.ts
- prototype/src/components/convert/LocalConvertWorkflow.tsx
- prototype/tests/e2e/local-convert-workflow.spec.ts
- prototype/tests/e2e/testinghost-real-convert.spec.ts
- .codex-coordination/results/TASK-011.md

SUMMARY:
- Consolidated deterministic backend failure corrections:
  * Resolved Istanbul folder resolution in genuine OST fixture test (`FilteredConversion_GenuineOstFixture_SelectSubset_ConvertsAndVerifiesExactly`) by matching Turkish dotted uppercase 'İ' (`İstanbul`) and invariant/path suffixes.
  * Corrected `SelectionPreviewEndpoint_ProtectedUnderSecurityMiddleware_ValidatesHostOriginTokenAndHandles`: registered `ILoggerFactory` via `services.AddLogging()` on the test `ServiceProvider` and inspected `IStatusCodeHttpResult` directly to eliminate `InvalidOperationException` on mock context, adhering to established Host-before-Origin/Token middleware semantics (400 for Host, 403 for Origin, 401 for Token, 400 when reaching downstream endpoint with empty handle).
  * Made `ExtractMessageDate_ProductionMapiMessage_ClientSubmitTimeAndDeliveryTime_NormalizeProperly` deterministic: explicitly cleared `ClientSubmitTime` and removed `PR_CLIENT_SUBMIT_TIME` for the delivery-date fallback test and empty test, ensuring extraction fallback operates cleanly without contamination from Aspose's default constructor timestamps.
  * Corrected mutated OST preview endpoint assertions in `SelectionPreviewEndpoint_MutatedOstWithoutSizeChange_RejectedFailClosed_NoSelectionRegistered` using `JsonDocument.Parse` on `IValueHttpResult.Value` to assert decoded Turkish Unicode strings (`BÜTÜNLÜK ENGELİ`, `değişiklik tespit edildi`), avoiding raw JSON escape mismatches (`\u00DC`).
  * Resolved CS8602 warning in `OstSelectionEngine.cs` around line 299 using safe null-propagation and null guards (`preflight?.Blockers != null && preflight.Blockers.Count > 0`).
  * Implemented Astra's authoritative whole-source blocker contract: Whole-source fail-closed applies to all `preflight.CanConvert == false` / blocker conditions (trial >50 items as well as non-trial damaged folder, enumeration, or read errors) even when the affected folder is not selected. `EvaluateSelection` computes and preserves authoritative preview counts while marking `CanConvert = false` and setting `BlockerReason`. `/api/convert`, `JobManager.StartJob`, and `OstToPstConverter.Convert` all reject any selection with whole-source blockers fail-closed.
  * Corrected production precedence in `OstToPstConverter.cs`: Evaluates whole-source preflight/trial blockers (`!selection.CanConvert || selection.HasTrialBlocker || selection.HasPreflightBlocker`) before zero-match count (`selection.SelectedMessagesCount == 0`), ensuring whole-source blockers remain the authoritative `BlockerReason` and are never overwritten by zero-match messages.
  * Made `SelectionEvaluation_NonTrialWholeSourceBlocker_RejectsJobStartAndConverter` production-valid: Uses a copied genuine OST fixture (`ResolveApprovedFixturePath()`) and a blocked `RegisteredSelection` bound to its real SHA-256, proving the converter passes authoritative OST signature validation, reaches stream-locked selection defense, and publishes no target PST file.
  * Added/updated production-connected coverage for trial and non-trial whole-source blockers (`SelectionEvaluation_WholeSourceTrialBlocker_RejectsEvenIfSubsetIsSmall` and `SelectionEvaluation_NonTrialWholeSourceBlocker_RejectsJobStartAndConverter`), proving neither `JobManager.StartJob` nor `OstToPstConverter.Convert` can bypass them.
  * Fixed `SelectionEvaluation_MissingDates_ExcludedWhenDateFilterActive_IncludedWhenInactive` message fixtures to explicitly configure `ClientSubmitTime` and `DeliveryTime` for in-range, out-of-range, and truly missing-date items, and hardened `OstSelectionEngine.ExtractMessageDate` to treat `DateTime.MinValue` and year <= 1601 as missing dates.
- Frontend typecheck corrections:
  * Added stable distinct `folderId` ('f_inbox_01', 'f_projects_01') to `FolderSummary` objects in `prototype/src/tests/localEngineClient.test.ts` conforming to exact folder identity semantics.
- Preserved UI and workflow contracts:
  * Wire property `blockerReason` matches across C# backend, TypeScript models, and JSX components (with `blockReason` fallback).
  * Persisted report folders prefer full `folderPath` over `displayName`.
  * Preview attachment accent color uses `var(--brand-orange)` while selected messages retain success green.
  * Filtered report conversion metric visibly proves Successful Selected = Written: renders written/selected as `3 / 3 öğe` with label `DÖNÜŞTÜRÜLEN / SEÇİLEN` using `report.isFiltered` and `report.selectedMessagesCount`, while preserving unfiltered behavior (`itemsWritten / itemsRead` labeled `DÖNÜŞTÜRÜLEN / OKUNAN`) and separate authoritative selection summary.
  * User-visible preview section heading updated to `SEÇİM ÖZETİ`.
  * Preserved Astra's `SynchronousConversionProgress` implementation in `JobManager.cs`.
- Genuine fixture E2E acceptance tests in `prototype/tests/e2e/testinghost-real-convert.spec.ts`:
  * Updated real HTTP test to exercise the exact independent oracle scenario on `Projeler/İstanbul` (using opaque `folderId` with robust Turkish dotted `İ` resolution) and inclusive Türkiye dates `2024-01-01`..`2024-02-16`, verifying authoritative 3/1/10 preview, 3 written, 0 failed, reopened PST with exactly 3 physical messages, 1 attachment, CID preserved, source SHA-256 match, and retained output path.
  * Added real browser UI test directly driving TestingHost without route mocks: changes/selects `Projeler/İstanbul` and `2024-01-01`..`2024-02-16`, verifies authoritative 3/1/10 preview, executes conversion, asserts frozen report details (folder path, dates, UTC+03:00 timezone, and exact counts including 3 / 3 öğe), verifies persistence across page reload, exercises empty folder selection zero-match blocking (asserting start control is absent OR present and disabled, then reacquiring locator and requiring it visible/enabled upon restoring selection), and enforces zero body horizontal overflow on desktop and 390px viewports.
  * Preserved existing unfiltered 13-item HTTP and UI regression tests intact.

VERIFICATION:
- SOL backend production tests: 67 passed, 0 failed, 0 skipped (`BitigMail.Engine.Tests`, Release).
- SOL frontend gates: typecheck exit 0; lint exit 0; 32 unit tests passed; production build exit 0.
- SOL real filtered TestingHost matrix: 6/6 passed across 1660, 1366, and 390 widths. Both direct HTTP and browser flows verified exact `Projeler/İstanbul` + `2024-01-01..2024-02-16` oracle: source 13, selected 3, excluded 10, selected attachments 1, written 3, failed 0, CID preserved, source hash unchanged, report reload persistence, explicit-empty zero-match block, and no horizontal body overflow.
- ASTRA independent API selection acceptance: 16/16 passed on port 6175, including `3/1/10`, all-folders 2024 `8/4/5`, duplicate physical messages `2`, timezone/CID boundary `1`, open bounds, parent-own count `0`, explicit none `0`, invalid/reversed dates, unknown IDs, wrong source, zero-start rejection, no output, and no-store.
- ASTRA independent real UI acceptance: 1660/1366/390 passed with actual `3/1/10` conversion, frozen full folder path, report reload, zero JS errors, document width equal to viewport, and delayed stale-zero preview response unable to overwrite the newer valid selection.
- ASTRA independent libpff output acceptance: job `job-51f339161f01`; PST `c6921daf...ba34`; SHA-256 `2b0a515c0e5e0110f97a728dd97014446f8cd09e4a513b836fe731d0af841946`; 13 source / 3 selected / 10 excluded / 3 output / 1 attachment; header, folder, and attachment multisets missing 0 extra 0; common body hash differences 0. Evidence: `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task011-qa/independent-7b62251f/report.json`.
- Final runtime: TestingHost 6175 stopped; production LocalHost listening on `127.0.0.1:6174` as PID 58488; Vite remains at `http://127.0.0.1:5173/`.
- ASTRA final closure: production 6174 security boundary 20/20 passed; source SHA remained `B0801758A2E61D4CE6E86799701A81A7A60C38401F73B13C993D94C03A2EE57A`; desktop-reference direct HTTP regressions passed 2/2 (unfiltered 13/13 and filtered 3/1/10). Final listeners: LocalHost 6174 PID 58488, Vite 5173 PID 40012 with HTTP 200, TestingHost 6175 stopped.

RISKS:
- Native Windows picker UI remains intentionally outside browser automation; TestingHost exercises the same production middleware/endpoints with a deterministic picker adapter.

UNCERTAINTIES:
- None blocking TASK-011 acceptance.
