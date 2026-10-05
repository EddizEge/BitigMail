# TASK-019 Scale & Resilience Validation Report (128 MiB Stage)

**Date:** 2026-09-14  
**Run ID:** `scale-cont-753a4197`  
**Status:** 128MiB ROOT ACCEPTED; overall TASK-019 staged experiment remains open/pending 1GiB root gate  
**Scope:** Staged synthetic scale (128 MiB, 1,024 physical messages, 5 folders) and fault resilience experiment.

---

## 1. Byte Definitions & Corpus Scope

- **Generated Source Raw Bytes (`133,846,960` bytes, ~127.65 MiB):** The exact sum of raw file bytes across the 1,024 generated physical EML files on disk (deterministic seed `20260914`, 5 folders).
- **Canonical / Wire / Archive Bytes (`133,847,116` bytes, ~127.65 MiB):** The canonical MIME wire byte count recorded across the 1,024 messages during IMAP bridge transfers and stored in the managed archive (`arc_ab5ec017b1d54a0c`). The 156-byte variance between source raw bytes and canonical wire bytes reflects standard MIME header serialization and CRLF wire formatting.

---

## 2. Executive Summary & Root-Accepted 128 MiB Checkpoints

Root has accepted the 128 MiB stage following independent audit `root-audit-fe577ca27e` (`3141/3141 PASS`) covering single-folder MBOX export (`job-280f867cb075`), EML export (`job-8c0f78e599bb`), and archive (`arc_ab5ec017b1d54a0c`), verifying raw/MIME/sidecar metadata, paged search, and source baseline invariance (12 original messages / 4 attachments). Independent audit `scale-cont-753a4197/root-resilience-audit.json` confirmed physical source vs actual IMAP MIME multisets (400 + 150) with 0 missing and 0 extra. Overall TASK-019 staged experiment remains open/pending 1GiB root gate.

All 1,024 deterministic physical messages completed end-to-end processing across the protected APIs:
1. **File -> IMAP (Bridge Import)**: Checkpoint `job-dbe137b82c56`: 1,024 messages imported across 5 folders.
   - **Job-record elapsed:** 70.045s (14.62 msg/s, 1.82 MiB/s canonical)
   - **Earlier runner wall elapsed:** 70.45s (14.53 msg/s, 1.81 MiB/s canonical)
2. **IMAP -> File (EML Tree Export)**: Checkpoint `job-8c0f78e599bb` (Root-accepted): 1,024 messages re-exported across 5 folders.
   - **Job-record elapsed:** 26.369s (38.83 msg/s, 4.84 MiB/s canonical)
   - **Earlier runner wall elapsed:** 26.67s (38.39 msg/s, 4.79 MiB/s canonical)
3. **IMAP -> File (MBOXRD 5-Folder Export)**: Checkpoint `job-89b7d45b64ed` (independent root audit `22/22 PASS`): initial partial 800 items in 30.20s; recovery 800->1024 (224 items added) in 7.61s (29.4 msg/s). Recovery MiB/s (previously reported as 3.67 MiB/s derived from 224/1024 proportional bytes) is removed because no measured raw-byte sum exists for the recovery segment. Process telemetry: Peak RSS: 653.5 MiB, Peak Private: 659.6 MiB.
4. **MBOX Retention Stress (Single-Folder 128 MiB Export)**: Checkpoint `job-280f867cb075` (from `scale-cont-ff44e15b`, Root-accepted): 1,024 messages exported from single folder in 35.40s (28.9 msg/s, 3.61 MiB/s). Process telemetry across 332 samples (100ms interval): Peak Working Set (WS): 797.8 MiB (836,599,808 bytes), Peak Private: 793.2 MiB (831,700,992 bytes).
5. **File -> Managed Archive (Ingest & FTS5 Indexing)**: Checkpoint `job-861042fbe03c` (Archive `arc_ab5ec017b1d54a0c` from `scale-cont-ff44e15b`, Root-accepted): ingested from EML export in 10.57s (96.9 msg/s, 12.08 MiB/s).
6. **Single Archive Scope Search (`arc_ab5ec017b1d54a0c`) & Exact Manifest Oracle**:
   - Verification was conducted exclusively on one archive scope (`arc_ab5ec017b1d54a0c`) and does **not** constitute a multi-scope proof.
   - $T_{\text{first}}$: 390.2 ms
   - $T_{\text{repeated}}$: 39.9 ms
   - Turkish I case folding verified on `arc_ab5ec017b1d54a0c`:
     - "isparta" (subject): exactly 10 matches (100% exact match against derived manifest identity set).
     - "isparta" (all fields): exactly 17 matches (10 subject + 7 long body text items).
     - "işlem" (subject): exactly 9 matches.
     - "diyarbakır" (subject): exactly 11 matches.
     - "sıkıştırma" (subject): exactly 10 matches.
     - "ankara" (negative query): exactly 0 matches.
   - Exact Manifest-Derived Oracle: 100% identity match across all result pages on all queries.
   - Sanitized message preview verified on scalar-safe truncated long body item (`arc_ab5ec017b1d54a0c:msg_00000161`).
7. **Resilience & Zero Duplicates**:
   - Mid-stream hard process crash (checkpoint `job-604ed5ccedae`): observed durable progress at position 30, then persisted 57 items in durable journal before process termination (crashed PID 57224); resumed with PID 22772 cleanly in 19.58s to 100% verified with exactly 400 messages (0 duplicate copies).
   - Controlled APPEND lost-response (checkpoint `job-8d846509d05f`, fault ordinal 25): executed in legacy isolated `TASK017-scale-cont-753a4197-LostResp` folder (naming deviation from task template, no original namespace touched); reconciled via `BitigMailKeyword` in 7.53s with exactly 150 messages (0 duplicate copies).
8. **Source Baseline Invariance**: Original 12 messages and 4 attachments in `INBOX`, `Gönderilenler`, `Projeler/İstanbul` remained 100% identical and intact throughout all operations (confirmed in `root-audit-fe577ca27e`).

---

## 3. Measurement Table

| Operation | Format / Scope | Items | Total Bytes | Elapsed (s) | Throughput (msg/s) | Bandwidth (MiB/s) | Peak RSS / WS (MiB) | Peak Private (MiB) |
|---|---|---|---|---|---|---|---|---|
| **Bridge Import (Fresh)** | EML-Tree -> IMAP (5 folders) | 1,024 | 133847116 (canonical) / 133846960 (raw source) | Job-record: 70.045<br>Runner wall: 70.45 | Job-record: 14.62<br>Runner wall: 14.53 | Job-record: 1.82<br>Runner wall: 1.81 | — | — |
| **Bridge Export (Fresh)** | IMAP -> EML-Tree (5 folders) | 1,024 | 133847116 (canonical) / 133846960 (raw source) | Job-record: 26.369<br>Runner wall: 26.67 | Job-record: 38.83<br>Runner wall: 38.39 | Job-record: 4.84<br>Runner wall: 4.79 | — | — |
| **Bridge Export (5-Folder)** | IMAP -> MBOXRD (5 folders) | 1,024 | 133847116 (canonical) | Partial: 30.20s<br>Recovery: 7.61s | Recovery: 29.4 | — *(recovery byte sum unmeasured)* | 653.5 | 659.6 |
| **Retention Stress (Single)** | IMAP -> MBOXRD (Single folder) | 1,024 | 133847116 (canonical) | 35.40 | 28.9 | 3.61 | 797.8 (WS) | 793.2 |
| **Archive Ingest** | EML -> SQLite FTS5 (`arc_ab5ec017b1d54a0c`) | 1,024 | 133847116 (canonical) | 10.57 | 96.9 | 12.08 | — | — |

---

## 4. MBOX Memory Retention Findings & Interpretation Correction

- **Telemetry Scope:** The observed Peak Working Set of **797.8 MiB** (836,599,808 bytes) and Peak Private Bytes of **793.2 MiB** (831,700,992 bytes) during the single-folder stress export (`job-280f867cb075`) represent whole-process samples of `TestingHost` taken at 100ms intervals (332 total samples). In the 5-folder export (`job-89b7d45b64ed`), Peak RSS reached **653.5 MiB** and Peak Private reached **659.6 MiB**.
- **Code Inspection Evidence:** Static inspection of `BridgeExportWorker.cs` separately proves that the exporter constructs an in-memory `stagedEntries` list holding `(item, stagedPath, byte[] RawBytes)` for all messages in a folder prior to serializing the MBOX stream.
- **Attribution & Extrapolation Limits:** The measured telemetry does **not** causally allocate all measured process RSS / working set exclusively to these raw byte arrays, as the measurement encompasses the full host runtime (ASP.NET Core/TestingHost services, runtime assemblies, GC heap, SQLite/IMAP buffers).
- **1 GiB Extrapolation Caveat:** The current evidence does **not** prove a 1 GiB OOM or validate a linear extrapolation model. While whole-folder in-memory buffering represents an architectural hazard requiring a bounded streaming refactor (reading staged files individually during MBOX writing), empirical memory usage at 1 GiB remains unproven until measured under bounded streaming.

---

## 5. Format, Provider, and Scope Boundaries

1. **Archive Scope Limitation**: Search verification was performed exclusively against a single archive scope (`arc_ab5ec017b1d54a0c`). This does not constitute a multi-scope proof.
2. **EML vs MBOX Distinction**: EML-tree throughput measurements do not establish MBOX streaming capability.
3. **PST / OST Scale**: This experiment does not establish 100 GB PST/OST support, OST conversion capability, or corrupted archive repair.
4. **Provider Scope**: Loopback Dovecot testing does not establish Microsoft 365 Graph or Google Workspace IMAP provider support. Live provider validation remains deferred to the personal Outlook pilot.
5. **1 GiB Gate & Experiment Status**: The 128 MiB stage has achieved root acceptance (`root-audit-fe577ca27e` 3141/3141 PASS). Overall TASK-019 staged experiment remains open and pending the 1 GiB root gate.

## ROOT acceptance — TASK020 1GiB / 2026-09-14
Integrity gate ACCEPTED, performance measurement QUALIFIED/PARTIAL. Independent root-audit-4bd5f02a8a24858/24858 PASS: fresh full8192 source/IMAP/MBOX/EML physical message raw/MIME multisets2464attachments, sidecar UID/date/flags/keywords, managedarchive raw8192 + pagedpublicIDs/decodedsubjects, original12/4 invariant. Jobs37141333f3b0(MBOX),2b10f9032e58(EML), archive a682b2f758ca4298. Root1gprovenance exact8x physicalhash/length accepted128source,1070776928bytes; no missing/extra. FullRelease445PASS preserved.
MBOX retention change stores paths/metadata and loads one message before hash/write; metadata still scales with item count. 128MiB actual staging interruption/resume accepted; no claim for all assembly/publication crash windows. Large import completed8192 after parent-timeout recovery; phase timestamps/counters must not be treated as uninterrupted end-to-end timing. MBOX elapsed1194.3848s, EML765.69998s are harness export windows; archive97.858285s is durablejob window. Previews are separate and exceeded initial180sec harness deadline. Initial import1800sec value was hardcoded, not measured. 1GiB MBOX-phase memory telemetry unavailable/overwritten; no0MiB, boundedprocess-memory or complete1GiB performance acceptance claim.
Root repaired only test harness/oracle after repeatedAGY workspace-tool failures: completed import and archive checkpoint restoration, explicitpreviewtimeouts, rawSubjecttoken/fullID oracle, globalSQLite read-onlyscopedchecks, datetime shadow. Failed evidence retained. Production changes limited to bounded MBOX staging andtests.
Next work: performance/preview and finalverification progress visibility, reliable perphase telemetry; Azure app registration and personal Outlook pilot in isolatedtestfolder with nooriginalmaildeletion is a manual milestone reminder, NO TIMER. LocalDovecot1GiB success does not establish provider,100GBPST/OST or repair support.

## ROOT acceptance — TASK029 / 2026-09-15
The missing large-run phase measurements are complete for the accepted027 data engine snapshot.8192messages/2464attachments/1070776928physicalsourcebytes; source->IMAP canonical/MIME, IMAP->MBOX/EML exactraw plus sidecars, archive raw/SQLite/FTS/paged search identities and unchanged sources PASS. Job windows: import2121.56s, MBOX1263.19s, EML743.78s, archive97.76s; previews separate. Peak wholeprocess WS approximately3.37GB; not a memory ceiling or100GB/PST/OST/provider guarantee.20separate phaseCSVs/45797samples retained; substantial MBOX memory window measured, importfinalverification2179samples. Missing tiny subphase samples remain unavailable. Full method, timing, limits and immutable evidence: .codex-coordination/results/TASK-029.md. Final queue changes validated separately in TASK028; dataworker/client source hashes unchanged.
