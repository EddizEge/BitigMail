TASK_ID: TASK-028
STATUS: DONE
EXECUTOR: SOL 5.6 LIMITED (AUTHORIZED ANTIGRAVITY FAILURE EXCEPTION)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.LocalHost/Jobs/LocalJobRecord.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.ImapTransfer.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.BridgeTransfer.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.Archive.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.ImapTransfer.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.BridgeTransfer.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.Archive.cs
- engine/BitigMail.LocalHost/Archive/ArchiveModels.cs
- engine/BitigMail.LocalHost/Bridge/BridgeTransferModels.cs
- engine/BitigMail.LocalHost/Imap/Transfer/ImapTransferPreviewModels.cs
- engine/BitigMail.Engine.Tests/ImapTransferTests.cs
- engine/BitigMail.Engine.Tests/ImapTransferLifecycleCriticalTests.cs
- engine/BitigMail.Engine.Tests/BridgeSecurityAndScaleTests.cs
- engine/BitigMail.Engine.Tests/JobHistoryPagingTests.cs
- prototype/src/App.tsx
- prototype/src/types/index.ts
- prototype/src/types/localEngine.ts
- prototype/src/api/localEngineClient.ts
- prototype/src/hooks/useLocalEngine.ts
- prototype/src/hooks/useLocalSplitEngine.ts
- prototype/src/hooks/useMimeImport.ts
- prototype/src/hooks/useImapTransfer.ts
- prototype/src/hooks/useBridgeTransfer.ts
- prototype/src/components/jobs/JobCenter.tsx
- prototype/src/components/archive/AddArchiveModal.tsx
- prototype/src/components/archive/LocalArchiveWorkflow.tsx
- prototype/src/components/reports/ReportsView.tsx
- prototype/src/components/search/ArchiveSearchView.tsx
- prototype/src/components/convert/LocalConvertWorkflow.tsx
- prototype/src/components/convert/LocalMimeWorkflow.tsx
- prototype/src/components/transfer/ImapTransferWorkflow.tsx
- prototype/src/components/transfer/BridgeTransferWorkflow.tsx
- prototype/src/tests/imapTransferWorkflow.test.ts
- prototype/src/tests/bridgeTransferWorkflow.test.tsx
- prototype/src/tests/localArchiveSearchWorkflow.test.tsx
- prototype/src/tests/jobProgress.test.tsx
- prototype/src/tests/jobQueuePaging.test.tsx
- prototype/tests/e2e/task028-jobcenter.spec.ts
- prototype/tests/e2e/task028-real-queue.spec.ts
SUMMARY:
- Added opt-in enqueueIfBusy (default false), one global FIFO worker queue, 32-pending bound, durable pre-ACK queued records, idempotent start/resume behavior, persist-first scoped pending cancellation, restart interruption/replan semantics, and all current job-kind routing.
- Added bounded server paging (default 50, max 100), stable CreatedAt+JobId ordering, public-field search, status/job-kind filters, bounded active/recovery discovery, non-overlapping response-settled polling, pending visibility, and consistent filtered selection.
- JobCenter is paged. Reports and AddArchive intentionally retain their legacy complete one-shot history reads so older reports/exports are not silently hidden; explicit AddArchive source IDs are resolved directly.
VERIFICATION:
- Backend full regression: 469/469 PASS.
- Frontend full regression: 143/143 PASS.
- TypeScript typecheck: PASS.
- ESLint: PASS.
- Production build: PASS (existing large-chunk warning only).
- 10,000-record backend paging/filter/search/stable-order test: PASS.
- FIFO, max concurrency 1, 32 limit, duplicate start/resume, persistence rollback, activation failure, cancellation race/scope, queued restart, queued-resume preservation, and mixed-kind activation tests: PASS.
- Rendered Playwright: 3/3 PASS at 1660x948, 1366x768, and 390x844; 50/10,000 all-history rows, 2 real queued rows, next-page request, queued filter reset, zero body overflow.
- Real TestingHost two-job queue: job-582f3e6c5ad0 started converting; job-7e3670848f2f ACKed queued with waitingAtShutdown=true. Both completed 13/13 with zero failures, SUCCESS/PASS reports, unique 271,360-byte PST targets, and strict sequential timing (job 2 started after job 1 completed).
- Real JobCenter against TestingHost: 1/1 Playwright PASS; both persisted jobs visible as completed with frozen TASK028 Queue owner and 13/13 detail.
- Source bitigmail-lab-full.ost SHA-256 remained B0801758A2E61D4CE6E86799701A81A7A60C38401F73B13C993D94C03A2EE57A before/after.
- Canonical UI evidence: .codex-coordination/evidence/TASK-028/ui/jobcenter-all-*.png, jobcenter-queued-*.png, and jobcenter-*.json.
- Persistent real-run evidence: .codex-coordination/evidence/TASK-028/real-queue/manifest.md, two terminal job records, two SUCCESS/PASS reports, and both unique output PST artifacts.
- Normal updated LocalHost 6174 PID 17200 and Vite 5173 PID 21576 are running. TestingHost 6175 is verified off after its owned acceptance run.
RISKS:
- Reports/AddArchive complete one-shot history reads remain intentionally unbounded legacy behavior; TASK-028 bounds JobCenter and polling/recovery paths without hiding older selectable artifacts.
- Production bundle retains the pre-existing greater-than-500-kB warning.
UNCERTAINTIES:
- None for TASK-028 acceptance scope. Advanced autonomous scheduling/templates remain outside this Stage 2 task.
