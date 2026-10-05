TASK_ID: TASK-014
STATUS: DONE
FROM: ASTRA HIGH
TO: existing SOL 5.6 LIMITED / official AGY Gemini
GOAL: Real company/project IMAP accounts and filtered, resumable two-account copy with verified reports/UI.
RISK: HIGH (root owns security/integrity decisions)

Authoritative contract: docs/IMAP_TRANSFER_WORKFLOW.md. Read it before edits. No new Codex agents/tasks, no CUA, no original lab/profile changes. Gemini normal implementation; SOL direct commands/tests. Root independent acceptance/critical fixes only.

Gate A now: isolated lab + pinned MailKit/MimeKit restore/audit + real 12-message source serialization/APPEND/FETCH experiment, custom permanent keyword persistence/search, dates/flags and typed safe account/transport skeleton. Dependencies approved exactly by contract. Save concise actual wire/examples and results; do NOT claim lossless from same-SDK-only evidence. No product frontend until real contract DTOs established. Source fixture oracle immutable. Production secure credential store/TLS decisions must follow contract.

After Gate A PASS, proceed backend accounts/immutable preview/jobs/journal/API using contract; then frontend and deterministic tests. Record checkpoints to avoid monolithic silent AGY run. Quota/timeout requires bounded evidence; do not wait 15 minutes without any edit. No alternative editor until old AGY owned process/session finished.

Known files: engine/BitigMail.Engine.csproj; engine/BitigMail.LocalHost/{Program,LocalEngineApiEndpoints}.cs, Jobs/{JobManager,LocalJobRecord}.cs; engine/BitigMail.TestingHost/Program.cs; prototype/src/components/transfer/TransfersTabView.tsx, components/jobs/JobCenter.tsx, api/localEngineClient.ts, hooks/useLocalEngine.ts, types/localEngine.ts. Preserve TASK013 152 backend + 56 frontend acceptance; one MIME package replacement requires relevant full backend regression.

Verification: .tools/dotnet/dotnet.exe; npm typecheck/lint/build/test, targeted actual API/Playwright. SOL owns TestingHost6175 builds/start/stop; normal6174 stays usable until final verified update. Stop owned6175 before Release rebuild (DLL locks). Don't expose credentials/protocol dumps. Tests use isolated runtime and lab; old fixture hashes unchanged. Write .codex-coordination/results/TASK-014.md; never mark DONE before product UI and root acceptance.
