TASK_ID: TASK-029
STATUS: DONE — ROOT ACCEPTED
EXECUTOR: ASTRA HIGH — YÖNETİCİ (measurement harness and independent acceptance)
DATE: 2026-09-15

SCOPE: Complete the missing approximately 1GiB phase measurements, preserving immutable source data and independent output verification. No Azure/Outlook actions, no source deletion or reseeding.

EVIDENCE:
- Run: .codex-coordination/evidence/TASK-029/scale-027-a/attempt-47534874
- Checkpoint: .codex-coordination/evidence/TASK-029/scale-027-a/checkpoint.json
- Measurement summary: .codex-coordination/evidence/TASK-029/scale-027-a/measurement-summary.json
- Separate phase CSVs: .codex-coordination/evidence/TASK-029/scale-027-a/phase-csv/ (20 sampled phase labels, 45,797 rows; summary counts agree exactly)
- Physical source byte audit: .codex-coordination/evidence/TASK-029/scale-027-a/physical-source-audit.json
- Snapshot compatibility: .codex-coordination/evidence/TASK-029/scale-027-a/final-source-compatibility.json

RESULT:
- Existing source: 8,192 messages, 2,464 attachments, 1,070,776,928 physical raw bytes. Independent source-to-IMAP canonical/MIME multiset PASS, zero missing/extra.
- IMAP-to-MBOX and IMAP-to-EML exact raw/MIME/attachment multisets PASS. Both sidecars PASS for all 8,192 source identities, metadata and folder mapping.
- Managed archive exact raw/MIME multiset PASS. SQLite integrity=ok; 8,192 metadata rows and 8,192 FTS rows, exact manifest identities.
- Public paged search/decoded subject identity oracle PASS across its queries, including full 8,192-result retrieval.
- Original laboratory mailbox baseline 12 messages/4 attachments unchanged. Full source snapshot equality checked by the runner; an additional persisted byte-level audit independently confirms every source path/hash/length, zero missing/extra.
- All four jobs completed in one attempt, no resume or repeated transfer. Runner exit0; only owned snapshot PID48760 stopped and port6175 released to TASK028.

MEASUREMENTS (separate preview and job windows; seconds and MiB):

| Job | Job elapsed s | Host CPU s | Peak WS MiB | Peak private MiB | Active samples |
| --- | ---: | ---: | ---: | ---: | ---: |
| EML -> IMAP | 2121.56 | 1496.94 | 3079.4 | 3167.2 | 19742 |
| IMAP -> MBOX | 1263.19 | 1132.89 | 3214.8 | 3228.0 | 11700 |
| IMAP -> EML | 743.78 | 599.59 | 2954.4 | 3038.3 | 6906 |
| Archive ingest/index | 97.76 | 118.98 | 2696.5 | 2743.2 | 901 |

Preview elapsed: import8.58s, MBOX199.81s, EML196.37s, archive0.98s. Preview CPU/memory samples are in the measurement summary. Final import verification has 2,179 memory samples (approximately232.84s observed phase); the earlier tiny TASK026 phase gap is closed. MBOX has no separately observed final-verifying sample label; its full substantial execution window has11,700 samples. No zero-memory inference is made for unobserved subphases. Archive search is measured separately and excluded from the archive job's sample count.

METHOD/LIMITS:
- Isolated accepted027 Release snapshot runtime/task029/snapshot027-ec18950f. Final028 data worker/client source hashes remain unchanged; three request-DTO containers gained optional queue flags. Queue lifecycle is accepted separately in TASK028. This is not a claim that the entire final028 binary ran the large benchmark.
- Nominal100ms PID-specific sampler; host CPU is cumulative user+system CPU, not whole-machine or Dovecot CPU. Whole-process memory includes prior-phase runtime/GC allocations; peaks are not causal attribution to one mail buffer. Concurrent SOL development/build activity and cache/runtime variation prevent causal speed comparisons.
- Durations are monotonic start-request-to-observed-terminal windows, excluding preview and independent oracles. They are observations, not throughput guarantees. Substantial latency and about3.37GB observed peak WS remain relevant sizing information.
- Scope is local Dovecot and the stated corpus. No100GB PST/OST, repair, production SDK license, or live-provider acceptance is implied.
- Read-only report finalization repaired archive-search grouping and persisted the extra physical-source audit; no completed job was rerun. Original attempts and phase evidence remain intact.

CHANGED ROOT FILES: lab/task029/measure_pipeline.py, summarize_run.py, split_telemetry.py, audit_physical_source.py; task/acceptance documentation. Stage2 remains open until TASK028 real queue gate and final roadmap closure.

FINAL CLOSURE: TASK028 real queue acceptance passed (two13/13OST->PST jobs, sequential, unchanged source); root closed Stage2 and updated both roadmaps and checklist on2026-09-15. Azure/Outlook remains Stage3, no timer.
