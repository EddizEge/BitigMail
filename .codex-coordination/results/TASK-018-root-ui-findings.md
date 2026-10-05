# TASK018 root rendered UI findings — 2026-09-14

STATUS: CHANGES_REQUIRED. Backend partial-copy recovery also pending separately. Keep current yellow/orange/white design; targeted corrections only.

Evidence outside repo under `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task017-qa/`:
- `archive-ui-1789345786621`: first desktop failure, authoritative owner name shown as ID.
- `archive-ui-1789345899706`: desktop full flow until completed new archive failed to appear within15s.
- `archive-ui-1789346009528`: mobile390 same two failures. Safe HTML preview, two-company search and add-preview screenshots saved.
- One intermediate run lost its passive Playwright response-body observer when a stale request was aborted/navigation changed. That is a harness observer error, not a product failure. The observer was removed; reference results now come from an independent actual API query. Expected UI assertions were not changed to accept IDs or missing archives.

## Passing observed steps

Actual backend, not mocked mail data: fresh browser storage still shows registered company names in the tree; first company collapse works; company/archive tri-state selection gives16/28/24 results; subject İstanbul filter gives8; clicking companyB's result returns B's physical message; filter change clears preview; empty scope clears results/preview. HTML-only preview has expected visible text, no script/img/iframe/object/embed nodes, no script marker and no fixture remote requests. Native TestingHost picker -> preview -> start creates a real completed4-message archive on desktop and mobile.

## Required fixes

1. **Owner display names everywhere.** Tree names are correct, but preview breadcrumb uses companyId/projectId when browser-local company records are absent. Resolve preview owner names from the matching authoritative catalog snapshot. Search result location currently shows archive/folder only; include company/project names as required by workflow. No new backend company CRUD.
2. **Follow active ingest to terminal completion.** Start returns while copy/index is still running. Refetching catalog only immediately after start misses the new archive permanently until page reload. Observe/poll the saved job, show its progress/error, invalidate/refetch catalog when it is published/indexed, and select/reveal the new archive as appropriate. Desktop completed `arc_e0987a2b59fb421d` and mobile completed `arc_0bbddc0d221f4e95` were absent after15s. Do not fix by arbitrary frontend sleeps or assuming immediate completion. Cancellation/disposal and stale reply guards remain.
3. **Real folder options.** Real mode still shows demo INBOX/inbox/SENT/sent/ARCHIVE/archive options while actual archives contain Corpus/corpus/Projeler/İstanbul etc. Build options from all selected archives' immutable manifest folder summaries, not only the current paginated results. Existing manifest endpoint may be used and cached by archive ID; no new backend API is necessary. Keep demo folder choices confined to demo mode. Clear a selected folder when it no longer belongs to the selected scope.
4. **Real footer and clear labels.** Footer says `Örnek proje | 3 kaynak seçili arama kapsamı` while real-mode header says2 archives or no selection. Remove demo state from real footer; show actual scope summary or neutral real-archive context. Remove internal `FTS5` jargon from the user-facing description and `literal` from the query placeholder. Keep UTC+03 filter behavior understandable; avoid unlabelled UTC result dates that appear outside the chosen local calendar day.
5. **Mobile control spacing.** The390 safe-html screenshot shows roughly170px empty space with a floating magnifier between the query input and field/date selectors. Keep those search controls adjacent without the empty gap. Preserve accessible preview, modal scrolling, and table overflow within its own container.

One-click completed bridge export -> archive and readable source selection already landed under the prior UX delta; root still needs to exercise it. JobCenter archive reopen and final overflow/console checks occur after the failed post-ingest refresh assertion, so those are not yet accepted.

Run targeted frontend regressions including delayed real job completion and catalog owners missing from localStorage; preserve existing124 tests plus new meaningful cases, typecheck/lint/build. Root will rerun actual desktop/mobile flow after stable backend and UI handoff. Do not modify root harnesses or screenshots to hide failures.
