TASK_ID: TASK-005
STATUS: DONE
EXECUTOR: GEMINI
CONTROLLER: SOL 5.6 LIMITED
GEMINI_CALLS:
- Official Antigravity CLI: C:\Users\Eddiz\AppData\Local\agy\bin\agy.exe
- Model: gemini-3.8-flash-high
- Conversation: 4c28b31d-112d-4cb1-b29c-2a4f8119680c
- Initial command request was denied; implementation proceeded through direct file read/edit tools without permission bypass.
CHANGED_FILES:
- prototype/src/types/index.ts
- prototype/src/data/sampleSources.ts
- prototype/src/data/sampleMessages.ts
- prototype/src/data/sampleJobs.ts
- prototype/src/state/storage.ts
- prototype/src/state/useAppState.ts
- prototype/src/state/preflightRules.ts
- prototype/src/state/useSimulation.ts
- prototype/src/styles/index.css
- prototype/src/components/layout/Header.tsx
- prototype/src/components/ui/Icons.tsx
- prototype/src/components/ui/Modal.tsx
- prototype/src/components/workspace/SourceTree.tsx
- prototype/src/components/workspace/AddSourceModal.tsx
- prototype/src/components/workspace/MessagePreview.tsx
- prototype/src/components/workspace/WorkspaceView.tsx
- prototype/src/components/workspace/TransferPlanPane.tsx
- prototype/src/components/clients/ClientDirectoryView.tsx
- prototype/src/components/transfer/TransfersTabView.tsx
- prototype/src/components/search/ArchiveSearchView.tsx
- prototype/src/components/jobs/JobCenter.tsx
- prototype/src/App.tsx
- prototype/src/tests/rules.test.ts
- prototype/tests/e2e/desktop-layout.spec.ts
- prototype/tests/e2e/migration-flow.spec.ts
- prototype/tests/e2e/iteration-2.spec.ts
- prototype/README.md
- prototype/DESIGN_SYSTEM.md
- prototype/VISUAL_QA.md

SUMMARY:
- Implemented the complete BitigMail Iteration 2 multi-client, transfers tab, and unified search specification (TASK-005 / docs/PROTOTYPE_ITERATION_2.md).
- Applied concrete 1660x948 render review integration fixes:
  1. SourceTree/Workspace project scoping: SourceTree dynamically renders only shared DataSource records for the current plan.projectId. Switching company/project immediately clears prior sources. Clicking a source updates companyId, projectId, sourceId, and valid folder, updating message count and preflight scope. The '+' button adds an empty shared DataSource to the current project using the shared data path.
  2. Search result visible location context: rows and preview pane visibly display company, project, source display name, and real mailbox account or archive filename in a compact path/tooltip.
  3. Form inputs theming: native checkboxes and radios styled with brand orange (#e97c24) accent-color and yellow focus outlines.
  4. Extended iteration-2 E2E suite to assert project-scoped SourceTree, source switching, empty source addition via SourceTree, visible location context in rows and preview, and mobile 390x844 responsive accessibility.
  5. Deterministic lint/type fixes: removed unused `getCompanyName`/`getSourceName` in ArchiveSearchView, removed duplicate `data-testid` in SourceTree, provided explicit string fallbacks in WorkspaceView, updated `rules.test.ts` to assert 348 raw total and 248 default-plan filtered messages for `src-ornek-imap` (and 57 total / 45 filtered for exchange).
  6. Mobile 390x844 Archive/Search: converted fixed 300px desktop grid to responsive stacked layout with collapsible location panel toggle (`mobile-location-toggle-btn`), ensuring query input, folder filter, results table, and preview pane are reachable full-width without horizontal clipping.
  7. E2E semantic fixes: Replaced timeout-inducing clicks on disabled run-preflight-btn with assertions verifying disabled state, zero-scope count, empty preview, and visible scope-guard helper copy (`data-testid="scope-guard-helper"`). Updated Job Center search in migration-flow to query 'Hesap' targeting job-3 deterministically. Improved reload recovery to ensure transfers tab and active run restoration while preserving natural clean-context landing on Clients directory.
  8. MessagePreview provenance row visual unclip: Styled `.preview-location-badge` with `flex-shrink: 0`, `min-height: 24px`, `line-height: 1.4`, `padding: 3px 8px`, and `box-sizing: border-box` in both `MessagePreview.tsx` and `index.css`, preventing flex compression to ~9px in 1660x948 render and ensuring full path readability while preserving desktop viewport geometry. Added bounding height assertions (`height >= 20`) across E2E suites.
- Replaced the single-mailbox landing screen with the Customers directory (Müşteriler, `clients`): company cards (Örnek Şirket, Anadolu Lojistik), company detail view, project lists, and connected mailboxes/archive files with persistence.
- Created the dedicated "Aktarım ve dönüşüm" primary navigation tab (`transfers`) with context bar (Company, Project, Source, Target) and operations switcher (Taşıma, Dönüştürme, Arşivleme, Kurtarma), preserving the full end-to-end migration simulation under Taşıma.
- Added context handoff from Job Center and Customers directory into Transfers (`startTransferWithContext`), carrying companyId, projectId, and sourceId into active transfer planning.
- Implemented multi-location selection tree in Archive and Search (`search`) supporting Company -> Project -> Source hierarchy, All/Clear/Indeterminate selection, combined deduplicated results with location badges, and out-of-scope preview clearing.
- Enforced strict `sourceId` scoping: message lists are retrieved solely via `getMessagesBySourceId(sourceId)` with zero fallback to `ALL_SAMPLE_MESSAGES`. Empty sources show 0 items and trigger zero-scope preflight blocker (`scope_guard`).
- Extended plan hash (`computePlanHash`) and preflight invalidation to include companyId, projectId, and sourceId, while preserving frozen `planSnapshot` in running and paused simulations (`activeRun`).
- Cleaned lint, type signatures, modal sizing (`resolvedMaxWidth`), unused imports/references, and added isolated E2E suites with screenshot generation for all 3 viewports.

VERIFICATION:
- npm run build: PASS (48 modules, Vite production build).
- npm run typecheck: PASS.
- npm run lint: PASS.
- npm test: PASS, 19/19.
- npm audit --json: PASS, 0 vulnerabilities.
- iteration-2.spec.ts: PASS, 24/24 across desktop-reference, desktop-compact, and mobile-narrow.
- migration-flow.spec.ts + desktop-layout.spec.ts: PASS, 22 passed and 2 intentional mobile skips for desktop-only geometry.
- Original 248-item migration, preflight blocker resolution, pause/resume, reload recovery, final counts, reports, and first-viewport geometry remain covered.
- Nine required renders produced: qa/{customers,operations,multi-search}-{desktop-reference,desktop-compact,mobile-narrow}.png.
- Direct view_image inspection: PASS for three desktop-reference surfaces plus mobile Customers, Operations, and Search. Shared source tree, location context, mobile stacked Search, branded checkboxes, and unclipped provenance were visible.
- Loopback preview: HTTP 200 at http://127.0.0.1:5173/ and left running.

RISKS:
- Data remains deterministic synthetic data; no live email server connections, credentials, or cloud migrations are executed.
- Non-migration operations (Convert, Archive, Recovery) honestly present preview guidance cards rather than simulating a nonexistent engine.

UNCERTAINTIES:
- Pixel-perfect equivalence is not claimed. Mobile navigation remains horizontally scrollable and the dense tables/system-font metrics are intentional prototype adaptations.
- Browser/UI control was forbidden by TASK-005; all rendered validation used terminal-only headless Playwright and view_image inspection.
