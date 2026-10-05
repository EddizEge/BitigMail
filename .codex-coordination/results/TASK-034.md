TASK_ID: TASK-034
STATUS: DONE
EXECUTOR: SOL 5.6 LIMITED (AUTHORIZED ANTIGRAVITY FAILURE EXCEPTION)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.LocalHost/Pop/PopModels.cs
- engine/BitigMail.LocalHost/Pop/PopAccountStore.cs
- engine/BitigMail.LocalHost/Pop/PopSnapshotService.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.Pop.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.Pop.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.cs
- engine/BitigMail.LocalHost/Program.cs
- engine/BitigMail.TestingHost/Program.cs
- engine/BitigMail.Engine.Tests/PopSnapshotTests.cs
- prototype/src/types/localEngine.ts
- prototype/src/api/localEngineClient.ts
- prototype/src/components/transfer/PopSnapshotWorkflow.tsx
- prototype/src/components/transfer/TransfersTabView.tsx
- prototype/src/components/jobs/JobCenter.tsx
- prototype/tests/e2e/task034-pop-ui.spec.ts
- docs/POP_SOURCE_TO_EML_WORKFLOW.md
SUMMARY:
- Added a separate company/project-scoped POP account store using the existing Windows credential protector; production accounts require TLS, while plaintext is accepted only by the owned TestingHost loopback profile.
- Added immutable UIDL/LIST snapshot plans, message-count and 64 MiB guards, validation before every RETR and before publication, source-only RETR behavior with zero DELE, deterministic verified EML output, durable partial journals, and explicit restart/resume.
- Job fingerprint freezes account ID/version and plan ID; queue ownership is frozen and reports preserve POP fidelity qualifications.
- Added POP source-only UI, output picker/start flow, and explicit Job Center resume.
- Fixed TestingHost DI registration for IDiskCapacityProbe discovered by the actual UI acceptance flow.
- Hardened completed-output recovery: the fast path now revalidates canonical final path, frozen plan/snapshot/count/UIDLs, deterministic paths, every EML size/hash, and rejects extra files/directories before returning completed.
- Hardened partial recovery with a guarded non-following tree walk; journal and referenced paths are reparse-checked before reads, and unknown files/temps fail closed without deletion.
VERIFICATION:
- Backend full suite after root hardening: 577/577 PASS (`task034-root-hardening-full.trx`).
- POP hardening-focused suite: 9/9 PASS (`task034-root-hardening-final.trx`), including published EML tamper/missing rejection and unknown temp preservation.
- POP durability-focused suite: 7/7 PASS (`task034-pop-durable-final.trx`).
- Frontend unit suite: 145/145 PASS.
- Frontend typecheck: PASS.
- Frontend lint: PASS.
- Frontend production build: PASS (existing non-fatal bundle-size warning only).
- TASK-034 Playwright: 4 PASS / 2 intentional skips across desktop-reference, desktop-compact, and mobile-narrow. The desktop-reference test exercised actual TestingHost seed -> preview -> output picker -> UIDL-reorder failure -> Job Center explicit resume -> completed.
- Owned TestingHost PID 20012 stopped; port 6175 verified clear. Existing port 6174 remained listening on PID 9984 and was not disturbed.
- Synthetic backend POP server covers 12 messages / 4 attachments, duplicate Message-ID with distinct content, exact RETR hash oracle, zero DELE, unchanged source count/UIDLs, missing/duplicate/reordered/added/removed UIDLs, auth failure, count guard, queue ownership, restart resume, and crash recovery before journal/publication.
RISKS:
- MailKit GetStreamAsync can internally buffer the RETR response before the local bounded-copy guard observes it; the implementation reports this qualification honestly.
- Production TLS/provider interoperability has not been tested against personal or live provider accounts, by design.
- The generated frontend bundle retains the pre-existing >500 kB advisory warning.
UNCERTAINTIES:
- No claim is made for POP folders, server internal dates, read flags, OAuth authorization, original mailbox-on-disk bytes, or POP as a target.
- Filtering remains limited to retained MIME/date fields; unavailable POP server metadata is neither inferred nor advertised.

ROOT ACCEPTANCE — 2026-09-20: ACCEPTED, local source-only POP snapshot with documented qualifications. Verified 9/9 final POP tests and 577/577 full backend TRX; prior real TestingHost UI4PASS and frontend145/type/lint/build retained. Published-artifact revalidation, guarded staging walk, unknown-temp preservation and account-aware identity corrections addressed. Live provider/TLS interoperability and hard pre-MailKit allocation bound remain explicitly unaccepted. Proceed034B; Stage4 is still in progress.
