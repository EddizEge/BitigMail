TASK_ID: TASK-017
STATUS: DONE — ROOT ACCEPTED 2026-09-14
FROM: ASTRA HIGH
TO: existing SOL 5.6 LIMITED -> official AGY Gemini
GOAL: Deliver real, resumable EML/EML-tree/mboxrd -> IMAP import and IMAP -> EML-tree/mboxrd export in Transfers, with filters, immutable plans, source protection, durable reports and actual lab acceptance.
WHY: User approved file/account bridge, real archive/search and staged scale next; Azure/live Outlook deferred until these milestones. TASK017 is first milestone; search/scale will follow separate bounded task contracts. No real account required.
RISK: HIGH: root owns architecture/security/data-integrity decisions. Gemini normal implementation; SOL deterministic control. No new Codex tasks/agents. Use existing AGY CLI only.

AUTHORITATIVE_CONTRACT: docs/FILE_ACCOUNT_BRIDGE_WORKFLOW.md (root creating now; wait for file before implementation).
FIRST_ACTION: Read current MIME source/selection, IMAP transfer journal/client/worker and JobManager contracts. Produce .codex-coordination/results/TASK-017-discovery.md with proposed concrete integration points/DTO shape, dependencies, blockers. You may start this read-only discovery immediately. Do not yet change product until contract exists. Root will send completion notice.

SCOPE: EML/MBOX only in new bridge. PST/OST bridge follows separately under current vendor evaluation limits; no SDK bypass or new paid dependency. No Google/provider claims, no Azure signup/account access, no source DELETE/EXPUNGE/MOVE or reseeding original labs.
KNOWN_FILES: engine/BitigMail.Engine/Storage/{MimeSourceInspector,MboxrdRecordReader}.cs; engine/BitigMail.LocalHost/Imap/Transfer/*; Jobs/JobManager*.cs; Dialogs/FileHandleRegistry.cs; LocalEngineApiEndpoints*.cs; prototype/src/components/transfer/*; prototype/src/components/jobs/JobCenter.tsx; prototype/src/hooks/useImapTransfer.ts; lab/task014/dovecot; fixtures/mail-corpus-v1; fixtures/mime-import-v1.
VERIFICATION: Preserve 305 backend / 90 frontend baseline. SOL owns isolated TestingHost6175 and actual lab seeded separate folders; root independent integrity/failure oracles. Full relevant test/typecheck/lint/build; desktop+mobile real rendered interactions, actual both-direction API with Python independent raw/header/body/attachment/duplicate/date/flags evidence. No mock-only completion.
DELIVERY: checkpoint discovery, backend, frontend, acceptance separately with concise actual evidence. Final .codex-coordination/results/TASK-017.md plus docs/FILE_ACCOUNT_BRIDGE_VALIDATION.md. Root owns roadmap/state/task contract; do not race edits. Never report DONE before actual integrated UI+API acceptance.
