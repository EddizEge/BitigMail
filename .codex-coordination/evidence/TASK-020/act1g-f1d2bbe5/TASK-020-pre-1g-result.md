# TASK-020 Coordination Result: Bounded MBOX Retention & Actual 128MiB Recovery

**TASK_ID:** TASK-020  
**STATUS:** CANDIDATE (pending root audit)  
**EXECUTOR:** GEMINI (Antigravity)  
**CONTROLLER:** SOL5.6LIMITED / ASTRA HIGH  
**EVIDENCE_DIRECTORY:** `.codex-coordination/evidence/TASK-020/act128-14946c2d`  
**PRESERVED_DIAGNOSTIC_RUN:** `.codex-coordination/evidence/TASK-020/act128-7c6952e8`  
**HARNESS:** `lab/task020/scripts/run_task020_actual128.py`  

---

### SUMMARY

1. **Preflight & Preserved Bulk Target**:
   - Normal service port 6174 (PID 49876) and Vite (port 5173, PID 13512) verified active and protected throughout.
   - Port 6175 verified free before launch.
   - Free disk space on `C:` verified (161.59 GiB >= 11.2 GiB requirement); available RAM verified (14.28 GiB >= 2.0 GiB).
   - Preserved bulk target `Task019/scale-cont-ff44e15b/Bulk` from `job-19ed705a3aad` verified intact with exactly 1,024 physical messages.
   - Initial Dovecot source baseline audit passed 100% (12 messages, 4 attachments across `INBOX`, `Gönderilenler`, `Projeler/İstanbul`).

2. **Job 1: Bounded Single-Folder MBOX Export (`job-40c4d33f6ccf`)**:
   - Executed single-folder MBOX export of all 1,024 messages from `Task019/scale-cont-ff44e15b/Bulk` on fresh exact TestingHost (PID 57392).
   - Output path: `C:\Users\Eddiz\AppData\Local\Temp\bitigmail-qa-split\split-out-ead8bf92b1bf486eb6f0865fe7f2b795\bridge-export-job-40c4d33f6ccf\fld_001.mbox`.
   - Elapsed time: `44.80s` (22.86 msg/s).
   - Verification: `1024/1024` verified, `0` failed, `0` needs attention.

3. **100ms Phase-Bounded Telemetry (`telemetry-task020-job1.csv`)**:
   - Telemetry sampled every 100ms across the whole TestingHost process with explicit phase transitions:
     - `idle_pre_export` (19 samples, 2.0s): Peak WS = `442,118,144` bytes (~421.6 MiB), Peak Private = `461,971,456` bytes (~440.6 MiB), Max CPU = `43.0%`.
     - `export_active` (418 samples, 44.8s): Peak WS = `582,524,928` bytes (~555.5 MiB), Peak Private = `649,703,424` bytes (~619.6 MiB), Max CPU = `401.4%`.
     - `idle_post_export` (19 samples, 2.0s): Peak WS = `506,916,864` bytes (~483.4 MiB), Peak Private = `565,370,880` bytes (~539.2 MiB), Max CPU = `28.4%`.
   - **Comparison against Baseline Single-Folder MBOX Export (`job-280f867cb075`)**:
     - Baseline Peak WS: `836,599,808` bytes (~797.8 MiB) vs TASK-020: `582,524,928` bytes (~555.5 MiB) -> **-242.3 MiB (-30.4% reduction)**.
     - Baseline Peak Private: `831,700,992` bytes (~793.2 MiB) vs TASK-020: `649,703,424` bytes (~619.6 MiB) -> **-173.6 MiB (-21.9% reduction)**.
     - Timing: Observed elapsed difference is +9.40s (44.80s vs baseline 35.399s). Baseline 35.399s job timestamp boundary versus TASK-020 44.80s harness wall/phase boundary are not directly like-for-like, so no precise performance regression attribution is made.
     - Note: Telemetry reflects whole-process Windows resource usage; reported without arbitrary threshold or causal allocation claims.

4. **Job 1 Independent Physical Comparisons & Sidecar Audit**:
   - `source_to_imap`: Canonical MIME multiset comparison passed (1,024 items, 308 attachments, 0 missing, 0 extra).
   - `imap_to_file`: Exact raw byte multiset comparison passed (`exactRaw: true`, 1,024 items, 308 attachments, 0 missing, 0 extra).
   - `verify_manifest`: Sidecar manifest audit passed 100% (1,024 items verified against IMAP UID validity, internal dates, flags, keywords, and folder mapping).
   - Midflight source baseline invariance recheck passed (12 items / 4 attachments untouched).

5. **Job 2: Interruption at Observed Durable Partial Position (`job-33846f93e87a`)**:
   - Started separate export job on changed path: `C:\Users\Eddiz\AppData\Local\Temp\bitigmail-qa-split\split-out-f2aa0006567b46aab978932b2bbf4958\bridge-export-job-33846f93e87a`.
   - Interruption trigger: observed durable partial progress at position 60 (`msg_00000001.raw` through `msg_00000060.raw` durably flushed and recorded in export journal).
   - Pre-kill evidence captured: exact sizes and SHA-256 hashes of all 60 staged files stored in `job2-pre-kill-staging.json`.
   - Terminated exact owned TestingHost process (PID 57392) via Popen child handle kill; verified port 6175 closed.

6. **Interrupted State & Same-Job Recovery (`job-33846f93e87a`)**:
   - Persisted state verified on disk before restart: export journal intact, 60 staging `.raw` files preserved with identical SHA-256 hashes, final `fld_001.mbox` and `manifest.json` absent.
   - Restarted exact TestingHost with fresh PID 5552.
   - API state verified: `GET /api/jobs/job-33846f93e87a` returned status `interrupted` (Stage: `Kesintiye Uğradı`).
   - One explicit same-job resume issued: `POST /api/transfer/bridge/export/resume/job-33846f93e87a`.
   - Resumed job completed in `39.85s` to `100%` verified (`1024/1024` items, `0` failed).

7. **Job 2 Final Output Verification & Prior Byte Invariance**:
   - Final MBOX file `fld_001.mbox` parsed via independent stdlib oracle: 1,024 physical messages, 308 attachments.
   - Exact raw IMAP-to-MBOX multiset match passed (`exactRaw: true`, 0 missing, 0 extra, 0 duplicates).
   - Sidecar manifest verification passed 100%.
   - **Unchanged prior published bytes**: Verified that all 60 pre-kill staged `.raw` files have identical SHA-256 hashes in the final sidecar manifest (`manifest.json`) and final MBOX records.

8. **Postflight Source Baseline Invariance**:
   - Independent oracle confirmed Dovecot source mailbox remained 100% untouched: exactly 12 original messages / 4 attachments across `INBOX` (5), `Gönderilenler` (3), `Projeler/İstanbul` (4); 0 missing, 0 extra.

9. **Process & Port Teardown**:
   - TestingHost PID 5552 terminated via root lifecycle helper.
   - Port 6175 confirmed stopped and released.
   - Protected normal service port 6174 (PID 49876) and Vite (port 5173, PID 13512) confirmed running and healthy.

---

### MEASUREMENTS AND COMPARISONS

| Metric | Baseline Single-Folder MBOX (`job-280f867cb075`) | TASK-020 Single-Folder MBOX (`job-40c4d33f6ccf`) | Delta / Improvement |
| :--- | :--- | :--- | :--- |
| **Output Messages** | 1,024 items | 1,024 items | Identical (1024) |
| **Elapsed Wall Time** | 35.40s (28.9 msg/s) [job timestamp: 35.399s] | 44.80s (22.86 msg/s) [harness wall/phase boundary] | +9.40s observed elapsed difference* |
| **Overall Peak WS (RSS)** | 836,599,808 bytes (~797.8 MiB) | 582,524,928 bytes (~555.5 MiB) | **-242.3 MiB (-30.4%)** |
| **Overall Peak Private** | 831,700,992 bytes (~793.2 MiB) | 649,703,424 bytes (~619.6 MiB) | **-173.6 MiB (-21.9%)** |
| **Active Export Peak WS** | N/A (unphased) | 582,524,928 bytes (~555.5 MiB) | Tagged phase boundary |
| **Active Export Peak Private** | N/A (unphased) | 649,703,424 bytes (~619.6 MiB) | Tagged phase boundary |
| **Post-Export Idle WS** | N/A | 506,916,864 bytes (~483.4 MiB) | Cool-down observed |
| **Post-Export Idle Private** | N/A | 565,370,880 bytes (~539.2 MiB) | Cool-down observed |
| **Physical Multiset Match** | `exactRaw: true`, 0 missing, 0 extra | `exactRaw: true`, 0 missing, 0 extra | Identical exact fidelity |
| **Sidecar Verification** | 100% PASS | 100% PASS | Identical |
| **Mid-Stream Interruption / Resume** | Not tested on MBOX single-folder | Tested at pos 60; resumed 1024/1024 in 39.85s | **0 duplicates, prior bytes invariant** |

\* **Timing Boundary Warning**: Observed elapsed difference is +9.40s. Baseline 35.399s job timestamp boundary versus TASK-020 44.80s harness wall/phase boundary are not directly like-for-like, so no precise performance regression attribution can be made.

---

### EVIDENCE FILES (`.codex-coordination/evidence/TASK-020/act128-14946c2d/`)

- `source-baseline-preflight.json`
- `preserved-bulk-target-check.json`
- `target-account.json`
- `task020-host1.log`
- `owned-process-start-57392.json`
- `job1-export-preview.json`
- `telemetry-task020-job1.csv`
- `job1-telemetry-summary.json`
- `job1-export-report.json`
- `job1-source-snapshot.json`
- `job1-imap-snapshot.json`
- `job1-mbox-snapshot.json`
- `job1-source-to-imap.json`
- `job1-imap-to-file.json`
- `job1-sidecar-audit.json`
- `job2-export-preview.json`
- `job2-pre-kill-staging.json`
- `owned-process-termination-57392.json`
- `task020-host2-resume.log`
- `owned-process-start-5552.json`
- `job2-recovered-interrupted.json`
- `job2-resume-response.json`
- `job2-export-report-final.json`
- `job2-mbox-snapshot-final.json`
- `job2-imap-to-file.json`
- `job2-sidecar-audit.json`
- `source-baseline-postflight.json`
- `owned-process-termination-5552.json`
- `task020-execution-summary.json`

---

### REMAINING RISKS & LIMITS

1. **Root Gate for 1 GiB**: 1 GiB experiment was NOT run and remains gated pending root audit acceptance of these 128 MiB actual results.
2. **Allocation Attribution & Timing Boundaries**: Measured process Working Set / Private bytes reflect whole-process memory sampled at 100ms and are reported without arbitrary threshold or causal allocation claims. Similarly, baseline 35.399s job timestamp boundary versus TASK-020 44.80s harness wall/phase boundary are not directly like-for-like, so no precise performance regression attribution can be made for the +9.40s observed elapsed difference.
3. **Provider Boundaries**: Tested exclusively against the local loopback Dovecot lab (`127.0.0.1:5143`); Microsoft 365 / Google Workspace behaviors remain subject to the manual next-stage pilot note.

---

TASK_ID: TASK-020 / ROOT-AUTHORIZED-1G-STAGE
STATUS: ESCALATED
CHANGED_FILES:
- `lab/task020/scripts/generate_task020_1g_corpus.py` (Gemini)
- `lab/task020/scripts/run_task020_1g_pipeline.py` (Gemini)
- `lab/task020/scripts/resume_task020_1g_pipeline.py` (Gemini; checkpoint defect remains)
- `lab/task020/scripts/verify_task020_1g_oracle.py` (Gemini)
FAILED_VERIFICATION:
- The resumed harness cannot continue from the completed import checkpoint because it unconditionally re-enters Step 3 and requires the already completed job to be `interrupted`.
- Exact failure: `Check failed: Job job-2c748a9f0510 status is 'interrupted'` at `resume_task020_1g_pipeline.py:342`.
- Three narrow Gemini/Antigravity repair attempts produced no file edit. The latest attempt reached the 8-minute print limit; Antigravity logs show Windows workspace-path search failures/timeouts (`grep_handler` path parse errors and `find_handler: context deadline exceeded`).
RELEVANT_ERROR:
- Checkpoint already includes `phase4_import`; job `job-2c748a9f0510` is terminal `completed` with 8,192 reported items. Harness must skip Steps 3-4 and restore `p4` from checkpoint before Phase 5.
- Source-size provenance is unresolved: generated source totals `1,070,776,928` bytes, which is 1,248 bytes above authorized `1,070,775,680`; this equals eight times the 156-byte canonical-vs-raw delta and must not be called exact-copy provenance until classified.
GEMINI_STATUS:
- Production optimization and accepted 128 MiB proof are complete.
- 1 GiB corpus generation and import/recovery phase completed; MBOX preview/export, EML export, archive ingest, search, and final oracle were not completed.
- No 1 GiB production-code changes were made.
WHY_ESCALATED:
- Continuing requires a small Gemini-owned harness correction, but repeated official Antigravity Gemini calls could not reliably access/edit the file in this Windows workspace. SOL did not implement the correction itself, per controller role constraints.
RECOMMENDED_NEXT_STEP:
- Astra should reissue the narrow harness-only correction in an Antigravity environment that can edit this workspace: if checkpoint `completed` contains `phase4_import`, restore `p4 = data['phase4_import']`, skip Steps 3-4, and continue Phase 5; preserve old behavior otherwise. Run `python -m py_compile`, then resume only the remaining checkpointed phases. Separately classify the 1,248-byte source-size delta before any exact-copy claim.

---

TASK_ID: TASK-020 / ROOT-AUTHORIZED-1G-STAGE / PHASE8
STATUS: ESCALATED
CHANGED_FILES:
- `lab/task020/scripts/resume_task020_1g_pipeline.py` (root-owned narrow checkpoint/timeout/recovery fixes after repeated AGY path-tool failures)
FAILED_VERIFICATION:
- Phase 8 managed-archive paged identity search oracle returned `pass: false`.
RELEVANT_ERROR:
- `isparta`: expected 80, observed/retrieved 136.
- `izmir` negative check: expected 0, observed/retrieved 144.
- `işlem`: expected 72, observed/retrieved 2,472.
- Passing controls: `diyarbakır` 88/88, `sıkıştırma` 80/80, negative `ankara` 0/0.
- Evidence: `.codex-coordination/evidence/TASK-020/act1g-f1d2bbe5/phase8-search-report.json`.
GEMINI_STATUS:
- Phase 5 MBOX export completed: `job-37141333f3b0`, 8,192/8,192, 1,194.3848021s, 0 reported failures.
- Phase 6 EML export completed: `job-2b10f9032e58`, 8,192/8,192, 765.6999798s, 0 reported failures.
- Phase 7 archive ingest completed and durably recovered without duplicate ingest: `job-514bd7b28005`, archive `arc_a682b2f758ca4298`, 8,192/8,192, durable timestamp duration 97.858285s.
- Phase 8 failed; Phase 9 was not run.
WHY_ESCALATED:
- Search requests all returned HTTP 200 and full pagination counts matched observed totals, but three identity expectations failed. This requires root classification of catalog/scope contamination versus oracle expectation before changing product or harness behavior.
RECOMMENDED_NEXT_STEP:
- Independently inspect archive scope/catalog contents and the manifest-derived expected-token sets for the three failed queries. Do not rerun import, exports, or archive ingest; preserve completed phase checkpoints and existing evidence.

ROOT harness exception: completed phase4 skip with durable job identity/status/count validation; preview timeout parameter default180, only Phase5/6 export previews1800 with max_retries1 to prevent duplicate long requests after timeout. py_compile PASS. Production unchanged; actual1GiB acceptance pending.
