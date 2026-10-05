TASK_ID: TASK-024
STATUS: DONE
EXECUTOR: SOL 5.6 LIMITED (AUTHORIZED ANTIGRAVITY FAILURE EXCEPTION)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.Engine/Storage/DiskCapacityProbe.cs
- engine/BitigMail.LocalHost/Bridge/Transfer/DiskCapacityProbe.cs (moved to Engine.Storage)
- engine/BitigMail.Engine/Storage/OstAnalyzer.cs
- engine/BitigMail.Engine/Storage/OstToPstConverter.cs
- engine/BitigMail.Engine/Storage/PstSplitter.cs
- engine/BitigMail.Engine/Storage/MimeToPstConverter.cs
- engine/BitigMail.Engine/Models/PreflightCheckResult.cs
- engine/BitigMail.Engine/Models/RegisteredSplitPlan.cs
- engine/BitigMail.Engine/Models/SplitPlanResult.cs
- engine/BitigMail.Engine/Archive/ArchiveModels.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.Archive.cs
- engine/BitigMail.LocalHost/Archive/ArchiveCatalogService.cs
- engine/BitigMail.LocalHost/Archive/ArchiveIngestWorker.cs
- engine/BitigMail.Engine.Tests/BridgeExportCapacityTests.cs
- engine/BitigMail.Engine.Tests/LocalOutputDiskCapacityTests.cs
- prototype/src/types/localEngine.ts
- prototype/src/components/archive/AddArchiveModal.tsx
- prototype/src/components/archive/LocalArchiveWorkflow.tsx
- prototype/src/tests/localArchiveSearchWorkflow.test.tsx
- docs/DISK_CAPACITY_SCOPE.md
- .codex-coordination/evidence/TASK-024/verification-summary.json
SUMMARY:
- Moved the TASK-023 Windows caller-available capacity probe and explicit unavailable/error result into shared Engine.Storage without adding a dependency.
- Added conservative, checked, nonnegative planning allowances for OST-to-PST, PST/OST split, EML/mboxrd-to-PST, and managed archive ingest.
- OST and MIME PST allowance: 4x whole immutable source bytes + 64 KiB/selected item + 256 MiB. Whole source is used because selected byte totals are not authoritative in the SDK plan.
- Split allowance: 4x whole source + 64 KiB/selected item + 1 MiB/estimated part + 256 MiB. Year groups determine year-mode parts; overflow-safe ceil((2x source)/cap) estimates size-mode parts.
- Managed archive allowance: 4x raw bytes + 64 KiB/item + 256 MiB, aggregating raw staging/published data and SQLite index/WAL temporary allowance once on the currently shared runtime/archives volume.
- Converter/worker checks run before first output creation. Split and archive checks also run on supported restart/resume paths; legacy missing estimates are freshly derived from authoritative stored source sizes where possible.
- Unknown capacity is never treated as zero or sufficient. Turkish blockers use existing error contracts; no source or partial output is deleted to free space.
- Added additive estimate/availability metadata and existing-screen labels for split and archive previews. Old OST numeric fields remain compatible while explicit nullable availability/error metadata separates unknown from zero.
VERIFICATION:
- Shared/remaining-path targeted backend capacity suite plus preserved TASK-023 coverage: 12/12 PASS.
- Full backend Release regression: 460/460 PASS, 0 failed, 0 skipped.
- Actual SDK fixture paths included in that full run: OST-to-PST physical-item preservation, multi-part PST split with hard caps, real 12-message EML-to-PST preservation, and real corpus managed-archive ingest/search. Evaluation SDK marks remain accepted; no licensed-output claim is made.
- Deterministic forced gates: OST insufficient produced no target/partial; MIME unavailable produced no target/partial; split insufficient produced no partial/final directory; archive capacity drop produced no staging/published archive; archive unavailable preview remained blocked after persistence/reload.
- Frontend targeted archive/split tests: 36/36 PASS.
- Frontend typecheck: PASS; lint: PASS; production build: PASS (existing non-blocking >500 kB chunk warning).
- Immutable fixture inventory: OST 16,818,176 bytes SHA-256 B0801758A2E61D4CE6E86799701A81A7A60C38401F73B13C993D94C03A2EE57A; corpus.mbox 13,726 bytes SHA-256 C5B1C21511DA24177704BE4671392B80C49556C37668E5C85E280D094C1738B7; EML corpus 12 files / 13,013 bytes.
- Evidence: .codex-coordination/evidence/TASK-024/verification-summary.json
- Updated normal Release service restored hidden on 6174 PID 20792; Vite restored hidden on 5173 PID 21576; TestingHost 6175 remains off.
RISKS:
- All formulas are conservative planning allowances, not filesystem reservations or proven maximum output sizes. Concurrent disk consumption and SDK expansion can still cause runtime disk exhaustion, handled by existing failure paths.
- Size-mode split part count is estimated from a PST expansion allowance; actual SDK output may require a different number of parts.
- Archive aggregation is valid for the deployed shared runtime/archives base. Unexpected separate archive/index directories fail closed rather than being represented as independent checks.
UNCERTAINTIES:
- Licensed production SDK output-growth calibration remains future measurement work; evaluation fixture outputs must not be generalized as licensed production sizes.
- Provider quotas are not represented by local disk checks. Azure/real Outlook remains Stage3-only.
- Final Stage2 verification, performance/memory, list/queue behavior, and 1 GiB telemetry remain open; this result does not mark all Stage2 complete.
