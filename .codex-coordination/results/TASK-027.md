TASK_ID: TASK-027
STATUS: DONE
EXECUTOR: SOL 5.6 LIMITED (AUTHORIZED ANTIGRAVITY FAILURE EXCEPTION)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.LocalHost/Imap/Transfer/ImapTransferPreviewService.cs
- engine/BitigMail.LocalHost/Imap/Transfer/ImapTransferWorker.cs
- engine/BitigMail.LocalHost/Imap/Transfer/IImapTransferClient.cs
- engine/BitigMail.Engine.Tests/BridgeTestHelpers.cs
- lab/task027/scripts/benchmark_imap_transfer.py
- .codex-coordination/results/TASK-027.md
SUMMARY:
- Production IMAP preview now lists lightweight headers, applies the same deleted/date filters, then fetches each eligible UID individually for exact serialization/hash/metadata planning and disposes it after use.
- Worker source preflight retains UIDVALIDITY-before-write and validates every selected UID sequentially before any target write. Planned items are fetched again individually immediately before APPEND, preserving the second integrity gate. Final source verification retains per-folder UIDVALIDITY and re-fetches every selected UID sequentially.
- No production call site retains `InspectSourceFolderAsync` results. The old API and default-interface compatibility paths remain for fakes/adapters; live MailKit paths use header-only plus single-UID fetches.
- Test fakes were corrected to return newly owned MIME objects from raw bytes on each single fetch, matching MailKit ownership semantics.
VERIFICATION:
- Targeted IMAP/Bridge export lifecycle, tamper, resume, cancellation tests: PASS 42/42.
- Full backend Release suite: PASS 462/462.
- Baseline evidence: `.codex-coordination/evidence/TASK-027/baseline-9159a3c6`; after: `.codex-coordination/evidence/TASK-027/after-2686f096`.
- Both real isolated 128 MiB runs transferred 1024 messages to unique new folders. Independent exact raw/MIME oracle PASS and original 12/4 source invariance PASS. Preserved bulk source was not deleted or reseeded.
- Baseline benchmark interval 94.1562 s, overall peak WS 2,151,235,584 B/private 2,394,841,088 B. After benchmark interval 103.9307 s, overall peak WS 1,037,832,192 B/private 1,108,848,640 B: WS -51.76%, private -53.70%, interval +10.38%. This interval includes terminal-job detection and independent oracle reads, so it is not pure transfer duration.
- Transfer-phase peak: WS 2,016,837,632 -> 1,037,832,192 B (-48.54%); private 2,219,286,528 -> 1,108,848,640 B (-50.03%). Final-verification peak: WS 2,151,235,584 -> 642,084,864 B (-70.15%); private 2,394,841,088 -> 717,975,552 B (-70.02%). Final-verification time 7.2033 -> 7.1781 s (-0.35%). These are one paired local observation, not universal claims.
- Independent after metadata audit `.codex-coordination/evidence/TASK-027/after-2686f096/metadata-audit.json`: PASS 1024/1024 for exact raw SHA, canonical/MIME SHA, internal date, preservation of every source flag/keyword except `Recent`, and exactly one newly added unique `bitigmail_` token per target. The regenerated read-only source snapshot is saved as `expected-source.json`.
- The final-source mismatch branch now scopes MIME disposal before comparison, so success, mismatch, and early-return paths all dispose. Post-fix targeted IMAP tests PASS 25/25 and full backend PASS 462/462; the successful 128 MiB path was unchanged and was not rerun.
- Final hosts: updated normal Release on 6174 PID 12116; Vite on 5173 PID 21576; 6175 off.
RISKS:
- Bounded memory intentionally costs extra source reads; the measured whole job was 10.38% slower. Integrity gates were prioritized over throughput.
- Telemetry starts with job execution after preview, so it does not isolate preview-active memory. The same process retains preview allocations, and code/test inspection deterministically confirms header O(N) plus one raw/MIME body at a time.
- First baseline attempt `.codex-coordination/evidence/TASK-027/baseline-c20a0f26` failed closed at item 221 because atomic journal publication received transient `UnauthorizedAccessException`. No permission bypass was added. Existing bounded journal publication retry behavior may absorb transient sharing races; permanent denial still fails closed. The clean unique-target rerun passed.
UNCERTAINTIES:
- Runtime/GC/filesystem cache noise affects absolute peaks and elapsed time. Physical oracle results and deterministic tests are authoritative for correctness.
- Stage 2 remains open for TASK-028 and TASK-029 closure gates.
