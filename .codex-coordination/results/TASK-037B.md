TASK_ID: TASK-037B
STATUS: DONE
EXECUTOR: SOL 5.6 LIMITED (AUTHORIZED ANTIGRAVITY FAILURE EXCEPTION)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.Engine/Planning/Stage7Policies.cs
- engine/BitigMail.Engine/Storage/NormalizedSourceQualificationReader.cs
- engine/BitigMail.LocalHost/Archive/ArchiveSelectedJobService.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.Stage7.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.Recovery.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.cs
- engine/BitigMail.LocalHost/Jobs/LocalJobRecord.cs
- engine/BitigMail.LocalHost/Program.cs
- engine/BitigMail.TestingHost/Program.cs
- engine/BitigMail.Engine.Tests/Task037BArchiveSelectedJobTests.cs
- prototype/src/api/localEngineClient.ts
- prototype/src/types/localEngine.ts
- prototype/src/components/search/ArchiveSearchView.tsx
- prototype/tests/e2e/task037b-selected-export.spec.ts
SUMMARY:
- Selected archive search results can be frozen as physical item IDs plus raw SHA-256 values, owner/query scope and an explicit folder mapping/duplicate policy, then dispatched through the shared stable-priority local queue.
- Start and dispatch both revalidate source hashes, mappings and ownership. Export uses task-owned same-volume staging, verifies every copied EML hash and atomically publishes only a complete output directory.
- Duplicate policy defaults to preserving physical messages; content and content+available-metadata identities are explicit. Metadata identity includes original folder, MIME date, source internal date, envelope-from and Message-ID, never Message-ID alone.
- A recognized schema-1 selected-archive normalization manifest carries logical folders, item hashes, source qualification warnings, date-filter blocking and partial status into downstream EML reuse. The reader reopens and verifies every declared hash.
- Job/report evidence includes mapping fingerprint, duplicate policy, skipped physical IDs, frozen selection fingerprint, item counts and qualification state.
- Archive Search renders per-result selection and starts the real EML export flow; the duplicate-policy enum now has an explicit JSON string contract used by the browser client.
VERIFICATION:
- Backend full regression: 683/683 PASS (`engine/BitigMail.Engine.Tests/TestResults/task037b-full.trx`).
- Focused selected-export plus qualification-reader regression: 20/20 PASS (`engine/BitigMail.Engine.Tests/TestResults/task037b-focus.trx`).
- Frontend unit tests: 148/148 PASS.
- Frontend typecheck, lint and production build: PASS (only existing Vite chunk-size advisory).
- Real rendered TestingHost workflow on isolated port 6175: 1/1 PASS (`prototype/tests/e2e/task037b-selected-export.spec.ts`, desktop-reference). The test creates and indexes a real 12-message archive, selects an actual search row, starts the selected EML job, waits for completion and verifies the returned report fingerprints/count.
- Qualification regression proves qualified archive EML -> selected export -> downstream date-filter rejection; output manifest is reopened and hash-verified.
- Normal port 6174 was not stopped or modified; owned TestingHost 6175 was stopped after acceptance.
RISKS:
- Archive manifests currently expose internal date, MIME date, envelope-from, folder and Message-ID but no flags/keywords fields; the metadata identity includes every available field and can be extended when those fields enter the archive schema.
- Full regression reports two pre-existing/concurrent duplicate-using compiler warnings in Bridge preview services; no failures.
UNCERTAINTIES:
- TASK037B is complete. The normal Turkish field/operator/value AND/OR advanced-filter builder and remaining Stage7 product-surface integration belong to TASK037C/full TASK037, not this bounded result.
