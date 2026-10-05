TASK_ID: TASK-034B
STATUS: DONE
EXECUTOR: SOL 5.6 LIMITED (AUTHORIZED ANTIGRAVITY FAILURE EXCEPTION)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.Engine/Storage/OutlookToEmlNormalizationService.cs
- engine/BitigMail.Engine/Storage/NormalizedSourceQualificationReader.cs
- engine/BitigMail.Engine/Bridge/BridgeTransferModels.cs
- engine/BitigMail.Engine/Archive/ArchiveModels.cs
- engine/BitigMail.Engine/Archive/ArchiveManifest.cs
- engine/BitigMail.Engine/Models/MimeImportModels.cs
- engine/BitigMail.Engine/Models/RegisteredSelection.cs
- engine/BitigMail.Engine/Models/SelectionPreviewResult.cs
- engine/BitigMail.Engine/Storage/MimeSelectionEngine.cs
- engine/BitigMail.LocalHost/Bridge/Transfer/BridgeImportPreviewService.cs
- engine/BitigMail.LocalHost/Bridge/Transfer/BridgeImportWorker.cs
- engine/BitigMail.LocalHost/Archive/ArchiveCatalogService.cs
- engine/BitigMail.LocalHost/Archive/ArchiveIngestWorker.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.cs
- engine/BitigMail.LocalHost/Jobs/LocalJobRecord.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.cs
- engine/BitigMail.Engine.Tests/NormalizedSourceQualificationReaderTests.cs
- engine/BitigMail.Engine.Tests/Task034BQualificationIntegrationTests.cs
- prototype/src/types/localEngine.ts
- prototype/src/components/transfer/BridgeTransferWorkflow.tsx
- prototype/src/components/archive/AddArchiveModal.tsx
- prototype/src/components/convert/LocalMimeWorkflow.tsx
- prototype/src/tests/task034bQualificationUi.test.tsx
- docs/FORMAT_DIRECTION_MATRIX.md
SUMMARY:
- Version-1 Outlook normalization manifests now include per-item OriginalXmlSha256; the root qualification reader requires and verifies it for schema-1 OLM while retaining legacy schema-0 compatibility.
- Bridge import preview freezes qualification fingerprint/date-block/partial/warnings in the immutable persisted plan. Start, handle-free resume and final pre-completion validation re-read and compare qualification state. Date filters are blocked for unresolved OLM/EMLX dates; warnings flow to preview UI and job report detail.
- Archive preview/plan/ingest freezes and revalidates the same qualification state. Managed archive manifests preserve it, search responses propagate warnings, and date-filtered search rejects scopes whose original date fidelity is unresolved.
- Added an implementation-backed direction matrix: explicit mboxrd only; mboxo and mboxcl/cl2 remain unsupported; no OST/OLM/POP target or universal conversion claim; Apple Mail package/flat mbox distinctions and mail-only scope are explicit.
- Wired ordinary EML→PST preview/start/queued dispatch to freeze and revalidate recognized qualification; qualified date filters fail closed and warnings persist in the job/report UI.
- Recognized BitigMail POP snapshot manifests as verified reusable output. POP EML hashes and warnings propagate without blocking RFC Message Date filters; changed bytes fail closed.
- Added the existing IMAP→IMAP direction to the matrix.
- Added one shared manifest-aware qualification entrypoint: `eml-files` evaluates exactly each selected canonical path and aggregates only those qualifications; `eml-tree` evaluates the selected root; `mbox` remains ordinary with no invented normalized-EML provenance. Bridge/archive persisted plans freeze the selected canonical qualification paths for resume.
VERIFICATION:
- Dedicated qualification adapters + reader: 23/23 PASS (`task034b-gap-focused-final.trx`).
- MIME regression plus new adapters: 33/33 PASS (`task034b-mime-regression-final.trx`).
- Manifest-aware scope tests: 25/25 PASS (`task034b-scope-focused.trx`), including an ordinary selected file beside an unrelated qualified sibling and two qualified selected files from different parents.
- Full backend after final scope correction: 584/584 PASS (`task034b-scope-final-full.trx`).
- Final scope-guard + affected adapter suite: 47/47 PASS (`task034b-scope-guard-final.trx`). An ordinary EML beside a nested generated sibling remains ordinary, while a renamed/unlisted EML inside a recognized generated root is rejected fail-closed.
- Frontend full suite: 146/146 PASS; dedicated rendered warning/date-block flow 1/1 PASS.
- Frontend typecheck: PASS.
- Frontend lint: PASS.
- Frontend production build: PASS (existing non-fatal >500 kB bundle warning).
- Existing root qualification core coverage includes actual vendor OLM 22-output tree and single-EML ancestor qualification; schema-1 sidecar tampering now rejects rather than silently changing the fingerprint.
RISKS:
- Legacy implicit schema-0 OLM manifests cannot prove original XML authenticity; their current sidecar bytes remain fingerprinted as a compatibility qualification, not authenticated provenance.
- mboxo escaping is inherently ambiguous and mboxcl/cl2 bounded Content-Length semantics are not implemented; these remain explicit blockers.
- Production provider/live-account interoperability was not exercised.
UNCERTAINTIES:
- Contacts, calendar and tasks remain outside the mail-only normalized output scope.
- Qualification integrity proves consistency with recognized local manifests, not publisher identity or universal semantic fidelity.

ROOT ACCEPTANCE — 2026-09-20: ACCEPTED, Stage4 local implementation accounting closed. Final scope guard + affected adapters47/47 PASS; preceding full584/584 and frontend146/146 retained. Exact selected-file provenance, PST/IMAP/archive propagation, POP warnings with RFC date support, schema1 OLM XML hashes, and unlisted-file fail-closed behavior are accepted within the documented matrix. Unsupported mboxo/cl/cl2 and OLM semantic dates remain explicit limitations; no universal conversion or live-provider acceptance. Proceed035.
