TASK_ID: TASK-012
STATUS: DONE
EXECUTOR: GEMINI
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.Engine/Models/{SplitOptions,SplitPartReport,SplitPlanResult,RegisteredSplitPlan,SplitManifest,ConversionReport}.cs
- engine/BitigMail.Engine/Storage/{OutlookStorageInspector,OstAnalyzer,OstSelectionEngine,OstToPstConverter,PstSplitter}.cs
- engine/BitigMail.LocalHost/{LocalEngineApiEndpoints.cs,Program.cs,BitigMail.LocalHost.csproj}
- engine/BitigMail.LocalHost/Dialogs/{FileHandleRegistry,IFilePickerService,WinFormsFilePickerService}.cs
- engine/BitigMail.LocalHost/Jobs/{LocalJobRecord,JobManager}.cs
- engine/BitigMail.TestingHost/{Program,TestingFilePickerService}.cs
- engine/BitigMail.Engine.Tests/{BitigMail.Engine.Tests.csproj,SecurityTests,SplitFixtureBuilder,PstSplitWorkflowTests,SplitCompletionPublicationTests}.cs
- prototype/src/api/localEngineClient.ts
- prototype/src/types/localEngine.ts
- prototype/src/hooks/{useLocalEngine,useLocalSplitEngine}.ts
- prototype/src/components/archive/LocalArchiveWorkflow.tsx
- prototype/src/components/transfer/TransfersTabView.tsx
- prototype/src/components/jobs/JobCenter.tsx
- prototype/src/components/reports/ReportsView.tsx
- prototype/src/state/useAppState.ts
- prototype/src/App.tsx
- prototype/src/tests/localSplitWorkflow.test.ts
- prototype/tests/e2e/{local-archive-workflow,testinghost-real-split}.spec.ts
- docs/{PST_SPLIT_WORKFLOW,PST_SPLIT_VALIDATION}.md
- .codex-coordination/PROJECT_STATE.md
SUMMARY:
- Added genuine PST/OST analysis and immutable selection-aware split plans for year mode or a closed-file byte hard cap.
- Added typed source/output-directory handles, canonical request fingerprints, idempotent split jobs, atomic partial-to-final bundle publication, collision/containment protections, manifest/report persistence, source and output SHA checks, post-verification mutation detection, and exact final-directory membership checks.
- Preserved physical folder hierarchy and verified every output PST by reopening it; aggregate and per-part message, attachment, CID, byte, SHA-256, and hierarchy evidence are reported.
- Added real Turkish “Arşivleme ve bölümleme” UI: PST/OST picker, exact folder/date filters, honest year/size preview, output-directory picker, progress, multipart results, JSON download, copy location, reload/history/Job Center restoration, and no fake pause control.
- Added synchronous stale-preview/plan invalidation and workflow epochs so delayed plan, poll, job, or report responses cannot overwrite a changed source/filter/options workflow.
- Astra-owned controller corrections covered terminal persistence ordering, lifecycle-test synchronization, exact folder traversal, physical folder histograms, versioned synthetic-size fixture reuse, and aggregate reopened verification totals.
VERIFICATION:
- .NET full suite: 120/120 PASS, 0 skipped.
- Frontend Vitest: 49/49 PASS.
- Frontend typecheck: PASS.
- Frontend lint: PASS.
- Frontend production build: PASS.
- Mocked Playwright desktop + 390px: 10/10 PASS.
- Real TestingHost Playwright on 6175: 7/7 PASS; TestingHost stopped afterward.
- Real HTTP acceptance matrix: 81/81 PASS for genuine PST year split, filtered genuine OST year split, and synthetic size-cap multipart split, including auth/nonce/handle/plan/idempotency/manifest/closed-byte/SHA negative controls.
- Independent libpff text+HTML+attachment proof: genuine PST 13 messages -> 5 year parts, 4 attachments, missing 0, extra 0, body diffs 0, physical folder histogram preserved; synthetic size fixture 9 messages -> 5 parts, 9 attachments, every closed part <= 1,000,000 bytes, missing 0, extra 0, body diffs 0.
- Independent UI matrix: 1660px PST year (13/13, 5 parts), 1366px size (9/9, 5 parts), 390px filtered OST (3/13, 1 part); reload, JSON download, exact Job Center reopening, stale 1 MB vs 2 MB response isolation, zero horizontal overflow, and zero JS/console errors PASS.
- Versioned synthetic-size fixture independent proof: 9 messages, 9 x 256 KiB attachments, expected years, and 2 genuinely missing-date messages; SHA-256 b550a53a389b469c168b03752f39658fd47a6eeccf3cf433a1005dc573784b59.
RISKS:
- Output correctness depends on the pinned Aspose.Email runtime used by the engine; hard-cap enforcement applies to actual closed PST byte sizes and intentionally rejects a single message whose serialized size exceeds the selected cap.
UNCERTAINTIES:
- Independent libpff `-f all` cannot validate RTF bodies for the supplied fixtures because of an external RTF checksum incompatibility. Independent text, HTML, attachments, counts, hashes, sizes, and folder hierarchy all passed; Aspose reopen verification passed for every part.
- The requested browser plugin was unavailable, so deterministic Playwright plus independent scripted browser evidence was used.
