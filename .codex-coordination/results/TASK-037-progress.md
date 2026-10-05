TASK_ID: TASK-037
STATUS: IN_PROGRESS
EXECUTOR: SOL 5.6 LIMITED (AUTHORIZED ANTIGRAVITY FAILURE EXCEPTION)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.Engine/Planning/Stage7Policies.cs
- engine/BitigMail.Engine/Models/RegisteredSelection.cs
- engine/BitigMail.Engine/Models/SelectionPreviewResult.cs
- engine/BitigMail.Engine/Storage/MimeSelectionEngine.cs
- engine/BitigMail.LocalHost/Jobs/LocalJobRecord.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.Recovery.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.cs
- engine/BitigMail.Engine.Tests/Task037PolicyTests.cs
SUMMARY:
- Added bounded folder mapping with collision rejection, explicit physical/content/content+metadata duplicate policies, secret/live-handle-free template validation, and immutable archive-result selection with owner/content revalidation.
- Global waiting queue now selects priority then stable FIFO; only queued jobs can be reprioritized and active work is not interrupted.
- MIME advanced filters use the shared versioned AST against full parsed MIME body and all supported fields. Canonical JSON, fingerprint and unknown-metadata count are frozen with the registered selection/job. Unsupported PST/OST advanced filters fail explicitly.
VERIFICATION:
- Shared filter core, policy boundaries, full-body MIME parity and stable priority: 20/20 PASS (`task037-policy-queue.trx`).
- Current full backend regression: 656/656 PASS (`task037-full-fixed.trx`).
- Current frontend regression: 148/148 PASS; typecheck/lint/build PASS.
RISKS:
- Persisted secret-free template CRUD/apply endpoints and MIME advanced-filter UI are now present. IMAP/POP/archive filter adapters, selected-search job execution, mapping/dedup execution reports and rendered end-to-end Stage7 workflows remain incomplete.
UNCERTAINTIES:
- No Stage7 full regression or browser acceptance has been run yet; TASK037 is not accepted or DONE.
