TASK_ID: TASK-018
STATUS: DONE
EXECUTOR: GEMINI (ordinary implementation); ASTRA/ROOT (bounded UI exception after documented AGY escalation)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.Engine/Archive/*
- engine/BitigMail.LocalHost/Archive/*
- engine/BitigMail.LocalHost/Jobs/JobManager.Archive.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.Archive.cs
- engine/BitigMail.TestingHost/TestingFilePickerService.cs
- engine/BitigMail.Engine.Tests/ArchiveCorrectionsTask018Tests.cs
- prototype/src/components/archive/AddArchiveModal.tsx
- prototype/src/components/search/ArchiveSearchView.tsx
- prototype/src/components/jobs/JobCenter.tsx
- prototype/src/components/transfer/BridgeTransferWorkflow.tsx
- prototype/src/components/transfer/TransfersTabView.tsx
- prototype/src/api/localEngineClient.ts
- prototype/src/types/localEngine.ts
- prototype/src/state/useAppState.ts
- prototype/src/App.tsx
- prototype/src/tests/localArchiveSearchWorkflow.test.tsx
- docs/LOCAL_ARCHIVE_SEARCH_VALIDATION.md
SUMMARY:
- Immutable managed EML/mboxrd archives, manifest-independent rebuildable SQLite FTS5 search, strict company/project/archive scope validation, safe preview, restart/reindex and JobCenter/UI integration completed.
- Gemini produced the ordinary backend/frontend implementation. After repeated documented AGY Windows-path/time-out failures left bounded rendered-UI defects unresolved, ASTRA/root applied only the escalated manifest-folder, job-poll/catalog-refresh, owner-label, wording/footer and mobile-spacing corrections.
VERIFICATION:
- Backend Release: 434/434 tests PASS; archive subset 65/65; focused corrections 11/11 with 0 analyzer warnings.
- Root actual API: 649/649 PASS; restart/missing-corrupt-index recovery 83/83 PASS.
- Root real mid-copy EML process-kill/resume: 203/203 PASS, same job/archive, 72/72, no extras, source/raw hashes unchanged.
- Root real mid-copy MBOX process-kill/resume: PASS, 2/12 durable interruption then 12/12 intact after restart.
- Frontend final: 129/129 tests; typecheck, lint and production build PASS.
- Root rendered UI: desktop 1660 and mobile 390 PASS, correct footer/scope, real manifest folders, owner names, native ingest refresh, JobCenter, safe HTML/no egress and overflow 0. Completed bridge-export entry passed at both widths.
RISKS:
- Production frontend bundle remains above the default Vite 500 kB advisory threshold; build succeeds.
- Corporate authorization/RBAC is not claimed; this remains a single-local-user ownership boundary.
UNCERTAINTIES:
- Large-scale behavior beyond the accepted 72-message archive flows is intentionally deferred to TASK-019 staged 128 MiB then conditional 1 GiB measurements.
- Azure registration and real personal Outlook pilot remain manual, deferred milestones; no scheduler is active.
