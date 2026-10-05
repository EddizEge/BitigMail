TASK_ID: TASK-012
STATUS: DONE
FROM: ASTRA HIGH
TO: SOL 5.6 LIMITED
GOAL: Genuine PST/OST → new verified multipart PST archive by year or actual file-size limit, in existing Archive operation.
WHY: User approved next PST splitting phase; existing PST files must be supported as sources, not just OST outputs.
ACCEPTANCE_CRITERIA:
- Implement docs/PST_SPLIT_WORKFLOW.md fixed architecture and its acceptance tests.
- Preserve legacy strict OST→singlePST and all TASK010/011 security, whole-source preflight, source lock, exact coverage and progress/persistence protections.
- Year exact grouping UTC+03 and Tarihsiz; size actual closed bytes hard cap, oversized single message fails before publishing.
- Atomic new output directory bundle after verifying every part and exact aggregate selected-entry coverage; immutable plan/options, typed handles, idempotency and persisted multipart reports/job navigation.
- Actual PST input + OST input tests and real UI/HTTP year/size output, report/reload, independent root acceptance.
KNOWN_RELEVANT_FILES:
- engine/BitigMail.Engine/Storage/OstAnalyzer.cs, OstSelectionEngine.cs, OstToPstConverter.cs, OutlookStorageInspector.cs, Models/
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.cs, Dialogs/, Jobs/
- engine/BitigMail.TestingHost/TestingFilePickerService.cs
- prototype/src/components/transfer/TransfersTabView.tsx, components/convert/LocalConvertWorkflow.tsx, hooks/useLocalEngine.ts, types/localEngine.ts, api/localEngineClient.ts, App/state job mapping
CONSTRAINTS:
- Existing fixed SOL→Gemini CLI workflow. No new Codex agent/task, no CUA/native picker automation, no providers/recovery/resume/license bypass/SDK upgrade.
- Gemini FILE EDITS ONLY. SOL tests directly; no nested test wait. Batch errors before corrections. Prefer backend core + tests first, then UI/integration after compiler passes, rather than one huge speculative edit across everything.
- Reuse full verifier; do not create weaker splitter fidelity checks. Root owns architecture/docs/independent oracle; no full secondary code review.
- Keep all prior inputs/outputs intact. Directory target uses new typed native handle; new final bundle only. No bulk temp cleanup or recursive delete of user/shared directories.
- Root budget not specified; no need ask confirmation. If hard-cap atomic publication architecture ambiguous, send a concise question to root while doing independent work.
RISK: HIGH (multi-output integrity/publication); concrete decisions in linked contract.
VERIFICATION:
- Backend production tests; previous single-PST and filtered regression.
- Frontend relevant tests/type/lint/build; actual PST/OST year and size multipart HTTP/UI with frozen report/reload, native picker replaced only in TestingHost.
- Unique ignored synthetic size fixtures with manifest; provide exact source/output/manifest paths for root libpff read. Do not mark DONE before real multipart artifacts.
- Coordinate stable6175 window with root acceptance; final normal6174+Vite5173up,6175down. Record commands/results in results/TASK-012.md.
