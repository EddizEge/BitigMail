TASK_ID: TASK-036
STATUS: IN_PROGRESS
EXECUTOR: SOL 5.6 LIMITED (AUTHORIZED ANTIGRAVITY FAILURE EXCEPTION)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.Engine/Recovery/DamagedStoreProtocol.cs
- engine/BitigMail.Engine.Tests/Task036DamagedStoreProtocolTests.cs
SUMMARY:
- Added the bounded parent-side worker request/result protocol and publication validator.
- Bound schema version, random invocation ID, job ID, source SHA-256, frozen output root and explicit traversal/item/deadline bounds.
- Worker result acceptance now rejects stale invocations, source mutation, path traversal, reparse/missing/hash/length-invalid MIME files, excessive manifest counts, inconsistent totals, fabricated healthy outcomes and orphan outputs.
- Unknown original totals remain nullable; partial, unreadable, cancelled and failed outcomes remain distinct.
- Added a packaged read-only Aspose worker using `Writable=false`, normal traversal followed by supported ID fallback, per-folder/message guards, deduplication and bounded scan limits.
- Added parent-owned execution, stale/nonzero/timeout/cancel rejection, new-directory publication, API preview/start/status/cancel, recovery workflow and Job Center rendering.
VERIFICATION:
- Owned process lifecycle plus new result-boundary suite: 10/10 PASS (`task036-boundary.trx`, Release).
- Focused worker, real generated PST, controlled truncation, global recovery queue and validator suite: 13/13 PASS (`task036-final-focused.trx`).
- Frontend recovery workflow: 148/148 PASS; typecheck/lint/build PASS.
RISKS:
- Root review identified remaining acceptance gaps: the recovery queue must be folded into the persisted global JobManager queue; unresolved worker termination must latch dispatch; child SDK license/warmup must inherit the approved startup context; folder/source-entry qualification metadata must be preserved. Current separate recovery queue is therefore not accepted for release.
UNCERTAINTIES:
- No corrupted PST/OST oracle has been executed yet; no damaged-content recovery claim is made.
