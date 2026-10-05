TASK_ID: TASK-018
STATUS: ESCALATED
CHANGED_FILES:
- prototype/src/components/search/ArchiveSearchView.tsx
- prototype/src/components/archive/AddArchiveModal.tsx
- prototype/src/components/jobs/JobCenter.tsx
- prototype/src/components/transfer/BridgeTransferWorkflow.tsx
- prototype/src/components/transfer/TransfersTabView.tsx
- prototype/src/api/localEngineClient.ts
- prototype/src/types/localEngine.ts
- prototype/src/state/useAppState.ts
- prototype/src/App.tsx
- prototype/src/tests/localArchiveSearchWorkflow.test.tsx
FAILED_VERIFICATION:
- Root rendered UI still lacks manifest-derived real folder options; `Corpus` is absent and demo INBOX/SENT/ARCHIVE options remain.
- Completed native and bridge archive-ingest jobs complete successfully but the new archive does not appear within 15 seconds because catalog refresh occurs only immediately after start.
- Owner display names, user-facing FTS5/literal jargon, and mobile control spacing remain incompletely corrected in the frozen source.
RELEVANT_ERROR:
- `ArchiveSearchView.tsx` still derives `availableFolders` from `catalog.item.folders`, although the actual catalog DTO does not supply folders.
- `onArchiveAdded` still calls `fetchCatalog()` immediately without following the returned job to terminal completion.
- Multiple bounded AGY Gemini turns timed out or looped on Windows path parsing without landing the requested source changes.
GEMINI_STATUS:
- Backend implementation and deterministic verification succeeded, including actual API and real mid-copy crash recovery.
- Frontend baseline remains compilable and tested at 124/124, but the listed rendered-UI corrections were not implemented by Gemini.
WHY_ESCALATED:
- The same concrete UI failures persisted after more than three targeted Gemini attempts, including a terminal-only workaround for the AGY Windows grep-handler failure.
- Astra explicitly authorized exception handling after repeated AGY write failure.
RECOMMENDED_NEXT_STEP:
- Astra/root should apply the bounded UI correction directly: selected-archive manifest cache, terminal job polling followed by catalog refresh/reveal, catalog-snapshot owner labels, real-mode wording cleanup, and 390px control spacing; then rerun typecheck/lint/tests/build and the existing root desktop/mobile UI harness.
