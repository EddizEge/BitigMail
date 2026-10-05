TASK_ID: TASK-023
STATUS: DONE
EXECUTOR: SOL 5.6 LIMITED (AUTHORIZED ANTIGRAVITY FAILURE EXCEPTION)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.Engine/Bridge/BridgeTransferModels.cs
- engine/BitigMail.LocalHost/Bridge/Transfer/DiskCapacityProbe.cs
- engine/BitigMail.LocalHost/Bridge/Transfer/BridgeExportPreviewService.cs
- engine/BitigMail.LocalHost/Bridge/Transfer/BridgeExportWorker.cs
- engine/BitigMail.LocalHost/Program.cs
- engine/BitigMail.Engine.Tests/BridgeTestHelpers.cs
- engine/BitigMail.Engine.Tests/BridgeExportCapacityTests.cs
- prototype/src/types/localEngine.ts
- prototype/src/components/transfer/BridgeTransferWorkflow.tsx
- prototype/src/tests/bridgeTransferWorkflow.test.tsx
- lab/task023/run_small_export_smoke.py
SUMMARY:
- Added the first Stage2 disk-capacity slice for IMAP-to-EML/mboxrd exports only.
- New previews persist a nullable estimated byte requirement computed from eligible fetched raw bytes without another IMAP fetch: EML = raw sum + 64 KiB/item + 256 MiB; mboxrd = 3x raw sum + 64 KiB/item + 256 MiB.
- Added an injectable Windows GetDiskFreeSpaceEx capacity probe using caller-available bytes and explicit unavailable/error results.
- Preview fails closed when the probe is unavailable or insufficient, while preserving earlier blockers, and exposes estimatedRequiredBytes/availableFreeBytes additively.
- Worker rechecks the complete conservative estimate before output directory creation and source access, including resume. Legacy plans whose estimate is null retain prior behavior and record a stage warning.
- Existing export preview now labels and displays the estimated requirement and available capacity.
VERIFICATION:
- Targeted backend capacity tests: 6/6 PASS.
- Full backend Release regression: 454/454 PASS, 0 failed, 0 skipped.
- Targeted frontend bridge workflow: 21/21 PASS.
- Frontend typecheck: PASS.
- Frontend lint: PASS.
- Frontend production build: PASS (existing non-blocking >500 kB chunk warning).
- Real isolated 12-message EML export smoke: PASS; preview transferable; estimatedRequiredBytes=269234974; availableFreeBytes=178881306624; exact raw output 12/12; missing=0; extra=0; original source remained unchanged at 12 messages/4 attachments.
- Smoke evidence: .codex-coordination/evidence/TASK-023/smoke-f06294ba/summary.json
- TestingHost 6175 stopped and released. Updated Release normal service restored on 6174 PID 24924; Vite 5173 PID 13512 preserved.
RISKS:
- The estimate is intentionally conservative and is not a reservation or guarantee; concurrent disk consumption can still affect execution after the worker recheck.
- Resume checks the entire original requirement and may reject a resumable job even when retained partial output means less additional space is actually needed. No existing output is deleted.
UNCERTAINTIES:
- This slice covers only IMAP-to-file EML/mboxrd export. Other local-output formats, broader resource/queue controls, and 1 GiB measurements remain open Stage2 work.
- Real Azure/Outlook coverage remains explicitly Stage3-only.
