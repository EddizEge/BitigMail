TASK_ID: TASK-025
STATUS: DONE
EXECUTOR: SOL 5.6 LIMITED (AUTHORIZED ANTIGRAVITY FAILURE EXCEPTION)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.LocalHost/Bridge/Transfer/BridgeExportWorker.cs
- engine/BitigMail.LocalHost/Bridge/Transfer/BridgeExportPreviewService.cs
- lab/task025/scripts/benchmark_preview_export.py
- .codex-coordination/results/TASK-025.md
SUMMARY:
- Replaced three full-file byte-array allocations used only for SHA-256 verification (existing staged raw file and two EML published/resume paths) with cancellable asynchronous stream hashing. Path, hash, output-length, journal, writer, and failure behavior are preserved.
- Deliberately retained the staged MBOX byte-array read because those bytes are passed to the root MBOXRD writer; it is not a hash-only allocation.
- Disposed the short-lived MimeMessage owned by FetchSingleSourceMessageAsync results in export preview and both export execution callers after last use. Raw/message ownership APIs were not changed.
- Added a TASK-025-only adapter around the accepted TASK-022 128 MiB oracle. It changes task/evidence context only and leaves TASK-022 accepted evidence untouched.
- Initial baseline preflight `baseline-59b875a3` stopped before measurement because the existing local Dovecot container was off. The same container was started without reseed/delete; the successful baseline is `baseline-071c02eb`.
VERIFICATION:
- Targeted Release tests: PASS, 16/16 (`BridgeExportTests` + `BridgeJournalAndResumeTests`). These cover successful exact EML/MBOX export, resume/output drift hash failure, corrupt/missing staging failure, cancellation, and same-job recovery. Existing missing-published-file fail-closed branch remains before hashing.
- Full backend Release tests: PASS, 460/460.
- TASK-025 benchmark adapter syntax: PASS (`python -m py_compile`).
- Baseline evidence: `.codex-coordination/evidence/TASK-025/baseline-071c02eb`. Preview 6.8849 s, peak WS 581,910,528 B, peak private 609,361,920 B. Export 45.2892 s, peak WS 804,315,136 B, peak private 867,885,056 B.
- After evidence: `.codex-coordination/evidence/TASK-025/after-09f01443`. Preview 6.6493 s, peak WS 496,398,336 B, peak private 525,160,448 B. Export 43.7907 s, peak WS 732,250,112 B, peak private 793,628,672 B.
- Sampling deltas: preview elapsed -3.42%, peak WS -14.70%, peak private -13.82%; export elapsed -3.31%, peak WS -8.96%, peak private -8.56%. These are observations from one before/after run, not universal performance claims.
- Both successful runs: 1024 eligible messages, exact IMAP-to-MBOX raw match PASS, MIME/canonical source match PASS, sidecar PASS, original source invariant 12/4 before and after.
- Final hosts: updated normal Release on 6174 PID 48492; Vite on 5173 PID 21576; 6175 off. Existing local Dovecot container is running and preserved.
RISKS:
- Stage 2 remains open. MBOX serialization still necessarily materializes each staged message because the unchanged root writer accepts bytes.
- Benchmark variation includes runtime/OS/cache noise; only deterministic integrity assertions are treated as pass/fail.
- The successful benchmark exercises MBOX export, so the EML resume hash paths are verified primarily by deterministic tests rather than this performance sample.
UNCERTAINTIES:
- Read-only next architecture: BridgeImportWorker final target loop (lines 647-670) and ImapTransferWorker final target loop (lines 568-586) each issue exactly 3 target operations per planned item: 1 UIDVALIDITY, 1 FetchAndVerify, and 1 keyword Search. Thus N items produce 3N target calls/checks in this phase. ImapTransferWorker additionally performs post-transfer source checks per distinct source folder: 1 UIDVALIDITY plus 1 full folder inspection, followed by in-memory item comparisons.
- Proposed later slice (not implemented): group final verification by target folder and introduce a scoped `IAsyncDisposable` read-only folder verification session that EXAMINEs once, captures/checks UIDVALIDITY without weakening it, batch-fetches the planned UIDs while returning the same raw SHA/date/flags/keywords fields, and performs exact per-keyword uniqueness searches within that same folder session (or a batch API with equivalent per-keyword result mapping). Preserve every UIDVALIDITY, existence, hash, date, flags, keyword-membership, and unique-token assertion. No folder/session cache may outlive the operation. This requires a root architecture decision before implementation.

ROOT ACCEPTANCE: PASS; independent before/after source+IMAP snapshot equality and all oracle flags verified in evidence/TASK-025/root-comparison.json. The 3N count describes client-method invocations in code, not measured IMAP wire command count. Future folder/session optimization remains unimplemented and requires preserving bounded memory plus UID epoch/uniqueness checks.
