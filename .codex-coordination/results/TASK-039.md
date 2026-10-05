TASK_ID: TASK-039
STATUS: DONE
EXECUTOR: SOL 5.6 LIMITED (AUTHORIZED ANTIGRAVITY FAILURE EXCEPTION)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.Engine/Security/AuditChain.cs
- engine/BitigMail.Engine/Security/ArchiveZipDirectoryPreflight.cs
- engine/BitigMail.Engine/Security/ArchiveZipPayloadVerifier.cs
- engine/BitigMail.Engine/Security/VerifiedStreamCopy.cs
- engine/BitigMail.Engine/Archive/ArchiveStorageManager.cs
- engine/BitigMail.LocalHost/Security/AuditLogStore.cs
- engine/BitigMail.LocalHost/Security/LocalSecurityMiddleware.cs
- engine/BitigMail.LocalHost/Security/ApiRoutePolicy.cs
- engine/BitigMail.LocalHost/IdentityApiEndpoints.cs
- engine/BitigMail.LocalHost/Archive/ArchiveBackupService.cs
- engine/BitigMail.LocalHost/Archive/ArchiveRestoreService.cs
- engine/BitigMail.LocalHost/Archive/ArchiveGovernanceService.cs
- engine/BitigMail.LocalHost/ArchiveGovernanceEndpoints.cs
- engine/BitigMail.LocalHost/Dialogs/IFilePickerService.cs
- engine/BitigMail.LocalHost/Dialogs/WinFormsFilePickerService.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.cs
- engine/BitigMail.LocalHost/Program.cs
- engine/BitigMail.TestingHost/TestingFilePickerService.cs
- engine/BitigMail.TestingHost/Program.cs
- prototype/src/api/localEngineClient.ts
- prototype/src/components/search/ArchiveSearchView.tsx
- engine/BitigMail.Engine.Tests/AuditChainTests.cs
- engine/BitigMail.Engine.Tests/AuditLogStoreTests.cs
- engine/BitigMail.Engine.Tests/AuditLogStoreBoundaryTests.cs
- engine/BitigMail.Engine.Tests/ArchiveZipDirectoryPreflightTests.cs
- engine/BitigMail.Engine.Tests/ArchiveZipPayloadVerifierTests.cs
- engine/BitigMail.Engine.Tests/VerifiedStreamCopyTests.cs
- engine/BitigMail.Engine.Tests/FreshArchivePublicationTests.cs
- engine/BitigMail.Engine.Tests/ArchiveBackupRestoreTests.cs
- engine/BitigMail.Engine.Tests/RestoreCrashBoundaryTests.cs
- engine/BitigMail.Engine.Tests/RestorePackageBoundaryTests.cs
- engine/BitigMail.Engine.Tests/GovernanceReportBoundaryTests.cs
- prototype/tests/e2e/task039-governance-ui.spec.ts
SUMMARY:
- Added identifier-only persistent audit JSONL chain with independently retained atomic head, strict bounded JSON admission, cross-process serialization, tamper/truncation warnings, and valid flushed-tail recovery.
- Added admin audit retrieval and safe middleware events for authenticated actions, failures, and authorization denials without token/body/subject/path/exception payloads.
- Added bounded ZIP64-aware preflight, exact payload membership verification, duplicate/path/reparse/traversal rejection, streaming hash/length verification, and source-manifest recheck before atomic backup publication.
- Added fresh-ID restore with destination scope remapping, exact raw fidelity validation, create-only publication, durable bounded strict receipts, per-archive indexing, and idempotent repair endpoint that preserves mismatching/existing targets.
- Added non-destructive retention preview and redacted customer delivery report with evidence hash and explicit account-reconnection portability notice.
- Added native ZIP backup picker, API client methods, and rendered archive-security panel for backup, explicit authorized destination selection independent of existing archives, configurable retention age/size, restore, audit integrity, and delivery report.
VERIFICATION:
- Final backend Release regression: 852/852 passed; evidence engine/BitigMail.Engine.Tests/TestResults/task039-acceptance-final.trx.
- Audit storage boundary: 6/6 passed (tail recovery, invalid/oversized/duplicate JSON, concurrent two-instance append).
- Restore crash boundary: 4/4 passed (before/after publish and before/after index injection, two service restarts/repairs, stable fresh ID, no duplicate archive, byte-identical original/restored content).
- Abrupt process interruption probe passed: process exited with code 73 immediately after publication while the lease was held; two new processes repaired idempotently with the same archive ID, two total archives, and byte-identical original/restored content. Evidence: evidence/STAGE8/root039-abrupt-process-repair.json.
- Audit/ZIP/verified-copy/fresh-publication focused cores passed within full regression.
- Frontend typecheck passed.
- Frontend lint passed.
- Frontend production build passed.
- Frontend unit regression clean rerun: 151/151 passed. Initial run had one existing fake-timer polling flake; immediate unchanged rerun passed.
- Package/service acceptance passed: two archives from two companies restored byte-exactly into an empty third destination while originals remained; tampered raw, extra entry, duplicate/null manifest, second-archive corruption and unsafe repair paths fail closed before partial publication.
- Governance acceptance passed: private warning/path/subject/token data omitted, visible report changes alter evidence hash, and retention preview leaves raw and manifest bytes unchanged.
- Rendered secure-host Playwright passed 1/1: real 12-message archive creation, UI backup, generated ZIP selection, UI restore into an explicitly selected empty company/project, new catalog/message-count verification, configurable retention preview, and operator HTTP 404 checks for backup/restore/retention/audit/delivery-report.
- TestingHost started successfully on 127.0.0.1:6175 with approved API route inventory; normal 6174 was not disturbed.
RISKS:
- Audit chain detects accidental or partial modification but is not administrator-proof authenticity; a local administrator able to rewrite both log and head can forge a consistent history.
- Abrupt-exit verification is an actual process exit but is not a physical power-loss or storage-controller durability claim.
- Backup hashes prove internal consistency, not publisher authenticity.
UNCERTAINTIES:
- No real multi-gigabyte/100GB corpus qualification was claimed; configured package limits and streaming behavior are deterministically covered.
- Native production WinForms dialogs are compiled; automated acceptance uses the isolated TestingHost picker because operating-system dialogs are outside deterministic browser automation.


ROOT ACCEPTED 2026-09-21: latest852backend/151frontend and realUI12-messagebackup/restore toemptydestination plusoperator404matrix close039. Prior840checkpoint superseded. Stage8 locallyaccepted with documentedlimits; actualnativepicker/cleanWindows andsignedpublisher remainstage9externalgates.
