# TASK-018 Frontend Checkpoint: Local Archive & Search Workflow

**Status:** CONDITIONAL_CHECKPOINT — PENDING_SOL_VERIFICATION (NOT claiming ROOT_ACCEPTED / DONE)  
**Date:** 2026-09-14  
**Scope:** Frontend ONLY (`prototype/`). Zero edits to backend/C#/tests/docs/root files.

---

## 1. Summary of Implemented Capabilities

TASK-018 implements the complete frontend workflow for BitigMail local archive management and full-text search against the local engine HTTP API (`/api/archive/*`, `/api/picker/archive-source`), including the authoritative UX delta completion.

### Key Decisions & UX Behaviors Implemented:
1. **Real vs. Demo Mode Isolation**: Clear separation between Managed Real Local Archive (default, connecting to `/api/archive/*`) and Sample Demo Data (isolated simulation state).
2. **Authoritative Catalog Grouping & True Tree Toggling**: The location tree groups authoritatively from frozen snapshot metadata in the backend catalog (`companyName`, `projectName`, `archiveName`). Archives remain visible and searchable even when browser `localStorage` company/project lists are empty. Default-expanded companies correctly invert state (`!(prev[id] ?? true)`), truly collapsing children on first click and re-expanding on subsequent click.
3. **Tri-State Multi-Selection**: Hierarchical multi-selection supporting company, project, and archive checkboxes with `indeterminate` state. Empty selection strictly yields 0 results and displays an empty scope prompt without issuing network search requests.
4. **Search Request Payload Precision**: Search requests dispatch with exact `ArchiveScopeTriple` objects, literal query string, closed field enum (`all`, `subject`, `sender`, `recipient`, `body`, `attachmentName`), UTC+03 calendar date strings (`startDate`, `endDate`), boolean/null attachment filter, and folder filter.
5. **Generation & Abort Guards**: All search and preview calls use `AbortController` and an incrementing generation ref. Changing query, filters, or scope synchronously clears previous preview state, aborts in-flight requests, and ignores stale responses arriving out of order.
6. **Preview Request Contract**: Clicking a search result sends the item's opaque stable `messageId` (e.g. `archiveId:itemId`) PLUS the full current `ArchiveSearchRequest`. The frontend never alters, splits, or synthesizes the message ID.
7. **Safe Plain-Text Rendering**: Message previews render headers, folder location, SHA-256, size, attachments, and body text using standard React JSX escaping. No `dangerouslySetInnerHTML` is used, and no remote images/scripts are executed.
8. **Truncation Warnings**: Explicit warning banners appear when search results or selected archives contain messages with truncated bodies (>512 KiB limit), warning the operator about search completeness.
9. **No Raw SourceJobId Input & Readable Completed Bridge-Export Selection**: `AddArchiveModal` eliminates user-facing raw `sourceJobId` textboxes. Completed bridge-export jobs are loaded from `client.getAllJobs()` and presented in a readable `<select data-testid="archive-completed-bridge-jobs-select">` with frozen company/project names, format, date, and job labels, retaining `jobId` strictly as the option value.
10. **Frozen Owner Context**: When creating an archive from a completed bridge-export, owner context is locked to the job's client context (`archive-frozen-owner-context`). Conflicting company/project dropdowns are hidden and cannot overwrite the frozen bridge owner. Separate MIME source selection retains company/project selection.
11. **One-Click "Arşive ekle" Action**: Completed bridge-export jobs in `JobCenter` (`data-testid="job-add-to-archive-btn"`) and `BridgeTransferWorkflow` (`data-testid="bridge-export-add-to-archive-btn"`) expose one-click navigation to Archive Search, opening `AddArchiveModal` with pre-selected export job and frozen owner context.
12. **Reindex & Recovery Actions**: Supports per-archive reindexing for failed/corrupted indexes and full catalog reindex-all (`/api/archive/reindex-all`).
13. **JobCenter Integration & Routing**: Real engine jobs for `archive-ingest` and `archive-reindex` are mapped and loaded from `client.getAllJobs()`. Navigation routes to Archive Search (`openArchiveSearchJob`) displaying frozen job context banners, never routing to PST split. Interrupted ingest jobs expose `data-testid="job-resume-archive-btn"`.

---

## 2. Modified & Created Files

### Type Definitions & Client
- `prototype/src/types/localEngine.ts`: Added DTOs for `ArchiveScopeTriple`, `ArchiveCatalogItemDto`, `ArchiveFolderSummary`, `ArchiveIngestPreviewRequest`, `ArchiveIngestPreviewResponse`, `ArchiveIngestStartRequest`, `ArchiveIngestResumeRequest`, `ArchiveSearchField`, `ArchiveSearchRequest`, `ArchiveSearchResultItem`, `ArchiveSearchResponse`, `ArchiveAttachmentInfo`, `ArchiveMessagePreviewRequest`, `ArchiveMessagePreviewResponse`, `ArchiveReindexRequest`, and extended `LocalJobRecord`.
- `prototype/src/types/index.ts`: Extended `Job` interface with `archiveId` and `archiveName`.
- `prototype/src/api/localEngineClient.ts`: Implemented all local archive endpoints (`pickArchiveSource`, `createArchivePreview`, `getArchivePreview`, `startArchiveIngest`, `resumeArchiveIngest`, `getArchiveCatalog`, `getArchiveManifest`, `searchArchive`, `previewArchiveMessage`, `reindexArchive`, `reindexAllArchives`).

### State & Navigation
- `prototype/src/state/useAppState.ts`: Added `selectedArchiveJobId`, `setSelectedArchiveJobId`, `openArchiveSearchJob`, `archiveAddInitialJobId`, `setArchiveAddInitialJobId`, and `openArchiveSearchAdd`.
- `prototype/src/App.tsx`: Wired `onAddToArchive` to `JobCenter` and routed `archive-ingest`/`archive-reindex` jobs to `openArchiveSearchJob`.

### Components
- `prototype/src/components/jobs/JobCenter.tsx`: Added `onAddToArchive` prop, rendered `data-testid="job-add-to-archive-btn"` for completed `bridge-export` jobs, mapped `archive-ingest` and `archive-reindex` jobs, added resume archive ingest action.
- `prototype/src/components/transfer/BridgeTransferWorkflow.tsx`: Added `onAddToArchive` prop, rendered `data-testid="bridge-export-add-to-archive-btn"` for completed `bridge-export` jobs.
- `prototype/src/components/transfer/TransfersTabView.tsx`: Passed `onAddToArchive` to `BridgeTransferWorkflow`, routing to `openArchiveSearchAdd`.
- `prototype/src/components/workspace/MessagePreview.tsx`: Safe text escaping, renders `ArchiveMessagePreviewResponse`, displays header metadata, attachments list, SHA-256, size, truncation alert, loading, error, and empty states.
- `prototype/src/components/archive/AddArchiveModal.tsx`: Removed manual `sourceJobId` text input; implemented readable completed bridge export `<select>`, frozen owner context banner (`archive-frozen-owner-context`), MIME separate file company/project selectors, preview breakdown, and start with `idempotencyKey`.
- `prototype/src/components/search/ArchiveSearchView.tsx`: Fixed company/project tree toggling logic with `!(prev[id] ?? true)`, added toggle test IDs (`tree-company-toggle-${id}`), automatic opening of `AddArchiveModal` with prefilled `initialSourceJobId`, tri-state selection, search controls, generation/abort guards, pagination, truncation warning banner, frozen context banner, and mobile toggle.

### Tests
- `prototype/src/tests/localArchiveSearchWorkflow.test.tsx`: 14 targeted unit and integration tests covering:
  1. Authoritative catalog grouping & metadata display
  2. Tri-state location selection (all, partial, clear)
  3. Empty location selection suppresses network search
  4. Search request payload precision with all filters combined
  5. Generation & abort cancellation guard against stale responses
  6. Opaque messageId preview fetch & metadata rendering
  7. Add flows (native MIME picker & preview/start)
  8. Resume / reindex actions from catalog tree
  9. JobCenter routing and frozen context display
  10. Safe plain-text escaping (no HTML/script execution)
  11. Mobile location panel toggle
  12. AddArchiveModal: no user-editable sourceJobId textbox, readable completed bridge-export selection hiding internal ID, and frozen company/project context
  13. Completed bridge-export one-click "Arşive ekle" from JobCenter and BridgeTransferWorkflow prefilling AddArchiveModal
  14. Default-expanded company collapse and re-expand on successive clicks

---

## 3. Verification Results

All checks executed from `prototype/`:

1. **TypeScript Typecheck (`npm run typecheck`)**:
   - Exit code: `0`
   - Output: `tsc --noEmit` clean with 0 errors.

2. **ESLint (`npm run lint`)**:
   - Exit code: `0`
   - Output: `eslint src` clean with 0 warnings/errors.

3. **Targeted Test Suite (`npx vitest run src/tests/localArchiveSearchWorkflow.test.tsx`)**:
   - Test Files: 1 passed (1)
   - Tests: 14 passed (14)
   - Duration: ~2.40s

4. **Full Test Suite (`npm test`)**:
   - Test Files: 9 passed (9)
   - Tests: 124 passed (124)
   - Duration: ~3.58s
   - Suites passing:
     - `src/tests/localArchiveSearchWorkflow.test.tsx` (14 tests)
     - `src/tests/localEngineWorkflow.test.tsx` (16 tests)
     - `src/tests/microsoftOAuthWorkflow.test.tsx` (12 tests)
     - `src/tests/bridgeTransferWorkflow.test.tsx` (11 tests)
     - `src/tests/imapTransferWorkflow.test.tsx` (9 tests)
     - `src/tests/localMimeWorkflow.test.tsx` (14 tests)
     - `src/tests/localConvertWorkflow.test.tsx` (17 tests)
     - `src/tests/planStorage.test.ts` (14 tests)
     - `src/tests/useAppState.test.tsx` (17 tests)

5. **Production Build (`npm run build`)**:
   - Exit code: `0`
   - Output: `tsc -b && vite build` completed successfully; bundles created under `dist/`.

---

## 4. Residual Risks & Notes for SOL / Root Verification

1. **Backend Dependency**: Full end-to-end integration requires running backend `BitigMail.LocalHost` on port 6174 with SQLite FTS5 enabled.
2. **Native Dialog Limitation**: Native file picker (`/api/picker/archive-source`) displays platform dialogs managed by the host OS / local engine; in headless testing environments, this is mocked or driven via testing host APIs.
3. **MessageId Opacity**: The frontend treats `messageId` strictly as an opaque token received from search items and passes it back to `/api/archive/search/preview` without transformation.
4. **Conditional Status**: This checkpoint documents frontend implementation completion and mock verification. Final task acceptance remains subject to root/SOL verification.
