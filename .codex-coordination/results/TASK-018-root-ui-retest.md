# TASK-018 actual retest — 2026-09-14

STATUS: CHANGES_REQUIRED (frontend only); backend accepted within recorded limits.

- Backend full API `api-f952de49c9`: 649/649 PASS.
- Actual EML mid-copy crash `api-8ce9f2e3dc`: 203/203 PASS, killed at durable 2/72, resumed same job/archive with all raw bytes intact.
- Actual MBOX mid-copy crash `api-84170fe6c1`: 75/75 PASS, killed at durable 2/12, resumed same job/archive with all raw bytes intact. TestingHost PID11696, root-owned.
- First MBOX probe `api-f8e91d6487` failed before any termination with AccessDenied. The watcher used Python file reads that could deny Windows atomic replacement. The passing rerun used Win32 FILE_SHARE_READ|WRITE|DELETE. Retain this as observer-interference diagnostic, not evidence of a reproducible product defect.
- Previous real restart / missing and corrupt index recovery `api-18a523d2d7`: 83/83 PASS.
- Root production search policy and critical test hashes still match the accepted checkpoint.

Actual UI stable-source retest:
1. `ui-folder-retest-failure`: desktop selection16/28/24 and subject query8 passed; real `Corpus` option absent after30s. `availableFolders` still reads nonexistent catalog folders rather than selected archive manifests. Demo INBOX/SENT/ARCHIVE options remain. Root harness clear option corrected from empty string to the actual `all` sentinel; this does not explain the missing Corpus option.
2. `ui-bridge-refresh-failure`: desktop JobCenter -> completed MBOX export -> archive modal frozen names/source selector -> actual preview12/start/completed passed. Archive `arc_87125f516008499b` never appeared within15s without reload. Original post-ingest refresh issue remains. No console errors in either probe.
3. Footer now shows real context, but bridge DOM still contains FTS5 and literal jargon. Other original findings still require actual retest; do not claim all fixed from green unit counts alone.

SOL/Gemini directed to finish the bounded original findings. Frontend source freeze released; root actual UI scripts stopped. No changes to6175 without explicit ownership handoff. TASK019 still WAIT. Azure/real Outlook remains a manual milestone note only; no timer.
