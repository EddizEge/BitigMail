TASK_ID: TASK-026
STATUS: DONE
EXECUTOR: SOL 5.6 LIMITED (AUTHORIZED ANTIGRAVITY FAILURE EXCEPTION)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.LocalHost/Imap/Transfer/IImapTransferClient.cs
- engine/BitigMail.LocalHost/Imap/Transfer/MailKitTransferClient.cs
- engine/BitigMail.LocalHost/Bridge/Transfer/BridgeImportWorker.cs
- engine/BitigMail.LocalHost/Imap/Transfer/ImapTransferWorker.cs
- engine/BitigMail.TestingHost/TestingBridgeFaults.cs
- engine/BitigMail.Engine.Tests/BridgeTestHelpers.cs
- engine/BitigMail.Engine.Tests/CompositeTargetVerificationTests.cs
- lab/task026/scripts/benchmark_import_verification.py
- .codex-coordination/results/TASK-026.md
SUMMARY:
- Added a backward-compatible composite target-verification interface result and default fallback. The fallback performs the prior UIDVALIDITY, message verification, and exact keyword search calls once each and in the same order.
- MailKit implements the composite operation with one fresh read-only folder open per item, one UID raw fetch in memory at a time, the same metadata fetch and exact keyword search, and close in `finally`. No folder/session state crosses item boundaries.
- BridgeImportWorker and ImapTransferWorker final-target loops consume the composite result while retaining every UIDVALIDITY, existence, SHA-256, internal-date, flags, keyword-membership, and unique-token assertion plus existing progress/error behavior.
- TestingHost forwards directly to the composite implementation when no fault is active. When a test fault is active it deliberately uses the wrapper's existing three method seams so fault instrumentation is not bypassed.
VERIFICATION:
- Pre-change real local import evidence: `.codex-coordination/evidence/TASK-026/baseline-15ce4bb5`. Isolated new target folder, 12 messages, independent physical MIME oracle PASS, original source 12/4 before/after PASS. Whole job 0.830826 s; observed final verification 0.088326 s.
- Post-change real local import evidence: `.codex-coordination/evidence/TASK-026/after-4016a80a`. Separate new target folder, same 12-message oracle and source invariance PASS. Whole job 0.764716 s; observed final verification 0.077530 s.
- Single paired observation: whole job -7.96%; observed final-verification phase -12.22%. This is not a universal speed claim.
- Phase memory sampling interval was 100 ms. Baseline's 88 ms phase produced one sample (WS 259,670,016 B; private 248,406,016 B); after's 78 ms phase produced zero samples. Therefore no phase-memory improvement claim is made. The implementation's bounded-memory property is structural: one item raw buffer and no cross-item cache.
- Targeted Release tests PASS 27/27 across composite fallback, BridgeImport, ImapTransfer, and journal/resume suites. Added deterministic checks for exact fallback operation order/count and cancellation before work.
- Full backend Release tests PASS 462/462 (prior 460 plus 2 new regression tests).
- Existing worker tests continue covering raw/date/flags/keywords/UIDVALIDITY/duplicate-token tamper failures, cancellation, resume, and failure outcomes.
- Final hosts: updated normal Release on 6174 PID 56172; Vite on 5173 PID 21576; 6175 off. Local Dovecot preserved; no reseed/delete.
RISKS:
- The 12-message local verification phase is shorter than one telemetry interval, so only monotonic phase time is useful in this paired sample.
- The default interface fallback remains three client method calls for compatible fakes/adapters. That is not a wire-operation count. The MailKit override is the optimized production path.
- Stage 2 remains open pending TASK-027 through TASK-029 gates.
UNCERTAINTIES:
- No protocol replay fixture exists in this repository that exposes MailKit folder-open wire counts without logging content. Deterministic coverage is at the interface/folder-operation seam plus the real local before/after phase comparison.
