TASK_ID: TASK-032
STATUS: DONE
EXECUTOR: SOL 5.6 LIMITED (AUTHORIZED ANTIGRAVITY FAILURE EXCEPTION)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.Engine/Storage/EmlxNormalizationService.cs
- engine/BitigMail.LocalHost/Dialogs/FileHandleRegistry.cs
- engine/BitigMail.LocalHost/Dialogs/IFilePickerService.cs
- engine/BitigMail.LocalHost/Dialogs/WinFormsFilePickerService.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.Emlx.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.cs
- engine/BitigMail.TestingHost/TestingFilePickerService.cs
- engine/BitigMail.Engine.Tests/EmlxNormalizationTests.cs
- prototype/src/types/localEngine.ts
- prototype/src/api/localEngineClient.ts
- prototype/src/components/convert/ConversionWorkspace.tsx
- prototype/src/components/convert/EmlxNormalizationWorkflow.tsx
- prototype/tests/e2e/task032-emlx-ui.spec.ts
- prototype/tests/e2e/task032-emlx-testinghost.spec.ts
- docs/EMLX_NORMALIZATION_WORKFLOW.md
SUMMARY:
- Added protected native-selected EMLX handles, immutable physical-file manifests, exact byte extraction, and explicit partial/external-attachment/reparse blockers.
- Added queued owner-frozen normalization jobs with disk preflight, source rehashing before writes and publication, deterministic ordinal names, retained hierarchy, and atomic create-only publication.
- Persisted raw MIME unchanged and Apple plist as opaque sidecars; verified on-disk hashes, MimeKit parsing and attachment counts.
- Replaced unbounded physical-file reads with bounded streaming parse plus streamed SHA-256, rejected oversized physical EMLX files before content allocation, validated source/output ancestor chains before reading, and traversed directory trees without entering reparse links.
- Added report/Job Center integration and a responsive Conversion UI with explicit limitations and guided Archive/Transfer reuse.
VERIFICATION:
- EMLX focused suite: 24/24 PASS (18 accepted reader tests plus 6 normalization/job tests, including sparse oversized-file rejection).
- Full backend regression: 518/518 PASS after bounded streaming/ancestor hardening; Release compilation has 0 warnings / 0 errors.
- Full frontend regression: 145/145 PASS on clean rerun (one earlier unrelated TASK-028 fake-timer check was transient).
- TypeScript typecheck: PASS.
- ESLint: PASS.
- Production build: PASS; existing greater-than-500-kB chunk warning only.
- Rendered Playwright UI: 3/3 PASS at desktop-reference, desktop-compact and mobile-narrow with zero horizontal overflow.
- Actual TestingHost picker → preview → queued job → completed report → Job Center flow: 1/1 PASS; verified 12 physical messages, 4 attachments, unchanged source corpus and completed history entry.
- Visual evidence: `.codex-coordination/evidence/TASK-032/emlx-*.png`.
RISKS:
- Apple external attachment stores remain deliberately unsupported and block normalization.
- Apple plist flags/dates are preserved byte-for-byte but not interpreted or propagated to IMAP/PST fields.
- Output reuse requires explicit selection in existing workflows; no automatic chaining is claimed.
UNCERTAINTIES:
- No real user Apple Mail corpus was accessed; verification used the accepted small synthetic corpus.
- SOL run did not create Windows links. ROOT additional real Windows junction acceptance afterwards: source-tree junction, source-ancestor junction and output junction all rejected; no output written. Evidence `.codex-coordination/evidence/TASK-032/root-real-junction-check.json`, lab `stage4-emlx-junction`. Only new owned runtime directories/fixture copy used; no existing user links or files changed. This adds real junction evidence, not every reparse type/race acceptance.
