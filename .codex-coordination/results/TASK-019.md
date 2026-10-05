# TASK-019 Coordination Result: Scale & Resilience Experiment (128 MiB Stage)

**TASK_ID:** TASK-019  
**STATUS:** 128MiB ROOT ACCEPTED; overall TASK-019 staged experiment remains open/pending 1GiB root gate  
**EXECUTOR:** GEMINI  
**CONTROLLER:** SOL5.6LIMITED  
**CHANGED_FILES:**
- `docs/SCALE_AND_RESILIENCE_VALIDATION.md`
- `.codex-coordination/results/TASK-019.md`

### SUMMARY
- **Root Acceptance of 128 MiB Stage**: Root audit `root-audit-fe577ca27e` passed `3141/3141 PASS` across single-folder MBOX export (`job-280f867cb075`), EML export (`job-8c0f78e599bb`), and archive (`arc_ab5ec017b1d54a0c`), verifying exact raw/MIME/sidecar metadata, paged search, and Dovecot source baseline invariance (12 original messages / 4 attachments). Overall TASK-019 staged experiment remains open pending the 1 GiB root gate.
- **Resilience Multi-Set Verification**: Independent audit `scale-cont-753a4197/root-resilience-audit.json` confirmed physical source vs actual IMAP MIME multisets across crash recovery (400 items) and lost-response reconciliation (150 items) with 0 missing and 0 extra messages.
- **Crash Recovery Progression**: The mid-stream crash test observed durable progress at position 30, with 57 items persisted to the durable journal before process kill (PID 57224). Resumed with PID 22772 (`job-604ed5ccedae`) and completed 400/400 items in 19.58s with zero duplicates.
- **Isolated Fault Namespace**: Controlled APPEND lost-response test (`job-8d846509d05f`, fault ordinal 25) used the legacy isolated folder `TASK017-scale-cont-753a4197-LostResp` (naming deviation from task template); no original namespace was touched, and 150/150 items reconciled in 7.53s via `BitigMailKeyword`.
- **Byte Definitions & Measurements**:
  - **Generated Source Raw Bytes:** `133,846,960` bytes (~127.65 MiB) across 1,024 physical EML files on disk (deterministic seed `20260914`, 5 folders).
  - **Canonical / Wire / Archive Bytes:** `133,847,116` bytes (~127.65 MiB) in IMAP transfer checkpoints and SQLite archive `arc_ab5ec017b1d54a0c` (156-byte difference reflects standard MIME wire serialization and CRLF formatting).
  - **Bridge Import (`job-dbe137b82c56`):** Job-record elapsed `70.045s` (14.62 msg/s, 1.82 MiB/s) vs earlier runner wall `70.45s` (14.53 msg/s, 1.81 MiB/s).
  - **EML Export (`job-8c0f78e599bb`):** Job-record elapsed `26.369s` (38.83 msg/s, 4.84 MiB/s) vs earlier runner wall `26.67s` (38.39 msg/s, 4.79 MiB/s).
  - **MBOX 5-Folder Export (`job-89b7d45b64ed`):** Initial partial 800 items in 30.20s; recovery segment added 224 items in 7.61s (29.4 msg/s). Synthetic recovery bandwidth `3.67 MiB/s` removed as no measured raw-byte sum exists for the recovery segment.
  - **MBOX Single-Folder Retention (`job-280f867cb075`):** 1,024 items in 35.40s (28.9 msg/s, 3.61 MiB/s).
  - **Archive Ingest (`job-861042fbe03c`):** 1,024 items into `arc_ab5ec017b1d54a0c` in 10.57s (96.9 msg/s, 12.08 MiB/s).

### VERIFICATION
- **Root Audit `root-audit-fe577ca27e`**: `3141/3141 PASS` validating single MBOX export, EML export, archive ingest, sidecar consistency, paged search, and baseline invariance.
- **Root Resilience Audit `scale-cont-753a4197/root-resilience-audit.json`**: Physical message multiset comparison passed 100% on both `Task019/scale-cont-753a4197/CrashTest` (400 items, 182 attachments, 0 missing, 0 extra) and `TASK017-scale-cont-753a4197-LostResp` (150 items, 2 attachments, 0 missing, 0 extra).
- **Search Oracle**: Verified exclusively on single archive scope `arc_ab5ec017b1d54a0c` ($T_{\text{first}} = 390.2$ ms, $T_{\text{repeated}} = 39.9$ ms). Turkish I case folding exact match (isparta: 10 subject / 17 all, işlem: 9, diyarbakır: 11, sıkıştırma: 10, ankara: 0). Truncated preview verified on `arc_ab5ec017b1d54a0c:msg_00000161`. Does not constitute multi-scope proof.

### RISKS
- **MBOX Whole-Folder Buffering**: `BridgeExportWorker.cs` buffers a full folder `stagedEntries` list containing `byte[] RawBytes` prior to MBOX writing. While 128 MiB single-folder export succeeded (Peak WS: 836,599,808 bytes / 797.8 MiB, Peak Private: 831,700,992 bytes / 793.2 MiB sampled across whole `TestingHost` process at 100ms), unbounded memory growth under a 1 GiB single-folder workload remains an architectural risk until bounded streaming is implemented.

### UNCERTAINTIES
- **1 GiB Extrapolation**: Measured telemetry does not allocate all RSS exclusively to byte arrays nor empirically prove a 1 GiB OOM or validate a linear extrapolation model. 1 GiB resource usage remains unmeasured until the 1 GiB stage is authorized and executed.
- **Multi-Scope Search**: Search verification passed 100% against single archive `arc_ab5ec017b1d54a0c`; multi-scope search performance across concurrent archives remains to be evaluated in subsequent stages.
- **Provider Boundaries**: Scale and fault resilience are established on loopback Dovecot; live Microsoft 365 / Google Workspace behavior remains subject to upcoming pilot testing.
