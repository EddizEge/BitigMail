TASK_ID: TASK-013
STATUS: DONE — ASTRA ACCEPTED
FROM: ASTRA HIGH
TO: SOL 5.6 LIMITED
GOAL: EML file set/tree or plain MBOX → new verified PST, integrated into existing conversion UI; output reusable in current archive splitter.
WHY: User approved EML/MBOX next, after TASK012 delivery.
ACCEPTANCE_CRITERIA:
- Follow docs/MIME_IMPORT_WORKFLOW.md. Preserve all TASK010/011/012 contracts and tests.
- First complete Gate A: bounded pinned-SDK decoding/MIME→MAPI/PST experiment and observations before production implementation.
- Existing 12 EML/corpus.mbox and manifest are immutable; use separate task-owned outputs. Exercise mboxrd quoted From lines, duplicate physical copies, Turkish content, four attachments/CID and source-before-SDK fidelity.
- After root decision, implement typed native MIME source-set handles, exact folder/date preview, new PST, shared job concurrency/persistence/idempotency, honest source→output report, actual UI and independent verification.
KNOWN_RELEVANT_FILES:
- docs/MIME_IMPORT_WORKFLOW.md
- fixtures/mail-corpus-v1/{eml,corpus.mbox,manifest.json,README.md}
- engine/BitigMail.Engine/Storage/{OstToPstConverter,PstSplitter}.cs
- engine/BitigMail.LocalHost/{LocalEngineApiEndpoints.cs,Dialogs,Jobs}
- prototype/src/components/transfer/TransfersTabView.tsx
- prototype/src/components/workspace/LocalOstWorkflow.tsx (resolve actual current file path)
- C:/Users/Eddiz/.nuget/packages/aspose.email/24.8.0/lib/net6.0/Aspose.Email.xml
CONSTRAINTS:
- Gemini FILE EDITS ONLY via official agy CLI. SOL runs commands/tests. No new Codex task/subagent.
- Root owns parser/dependency strategy decision after Gate A. Do not strip evaluation marks or weaken fidelity checks.
- Headless Playwright only, not native desktop/Outlook/Gemini CUA. No real accounts touched.
- No SDK upgrades, big files, broad cleanup/process kills or modification of original fixtures.
RISK: HIGH (MIME mapping, source-set integrity, shared job state).
VERIFICATION:
- Gate A small console/tests report includes actual installed API calls, EML+mboxrd counts/body escape behavior/header/attachment/CID changes at each stage, trial observations, new PST paths. Root independent Python oracle runs alongside.
- Later targeted/backend regression, UI unit/type/lint/build, real TestingHost EML/MBOX flow and root independent output proof.
NOTES:
- Last accepted baseline TASK012: .NET120, UI49, mock10, real7; normal6174 PID47032/Vite5173 PID40012 last known, verify ownership before any service stop.
- First response should confirm Gate A dispatch; report concrete checkpoint without waiting on huge all-feature generation.
