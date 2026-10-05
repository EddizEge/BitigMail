TASK_ID: TASK-011
STATUS: DONE_ACCEPTED_BY_ASTRA
FROM: ASTRA HIGH
TO: SOL 5.6 LIMITED
GOAL: Add real date + folder filtered OST-to-PST conversion with authoritative preview and persisted report.
WHY: User approved next stage; support selective export before splitting.
ACCEPTANCE_CRITERIA:
- Implement docs/FILTERED_OST_WORKFLOW.md contract in existing engine/API/React workflow.
- Authoritative immutable source-bound preview, exact folder IDs, Türkiye UTC+03 inclusive dates; no empty-selection fallback or trial bypass.
- Selected-only reopen integrity verification; report original/selected/excluded/converted counts and frozen filters.
- Preserve unfiltered callers/old reports and TASK010 security/data integrity boundaries.
- Production-code tests and real filtered HTTP/UI output evidence; no fake-only completion.
KNOWN_RELEVANT_FILES:
- engine/BitigMail.Engine/Storage/OstAnalyzer.cs, OstToPstConverter.cs, Models/
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.cs, Dialogs/FileHandleRegistry.cs, Jobs/
- prototype/src/components/convert/LocalConvertWorkflow.tsx, hooks/useLocalEngine.ts, api/localEngineClient.ts, types/localEngine.ts
- docs/FILTERED_OST_WORKFLOW.md, docs/LOCAL_OST_VALIDATION.md
CONSTRAINTS:
- Existing user protocol. Gemini normal coding via agy CLI; no new Codex agents/tasks, no CUA.
- IMPORTANT TASK010 latency lesson: Gemini prompt FILE EDITS ONLY, no tests/server startup/nested test waits. SOL runs deterministic verification directly. Use prompt file. If CLI edit phase finishes but waits on nested tasks, stop only own known session and verify directly instead of 15-minute blind waits.
- Prefer bounded backend and UI/test edit phases. Preserve existing real source fixture and outputs.
- No new dependency, SDK upgrade, splitting, provider work, broad cleanup or desktop dialog automation.
- Root owns architecture/independent oracle/final acceptance; do not re-review everything.
RISK: HIGH (selection/data integrity); architecture fixed by linked contract.
VERIFICATION:
- Backend production-code tests including selection contracts and unfiltered regression.
- Frontend relevant tests; typecheck, lint, build.
- Real fixture subset conversion via TestingHost6175 and real UI, report/reload, zero matches; responsive smoke. SOL owns server lifecycle while verifying; final normal6174 + Vite5173 up, testing6175 down.
- Record exact commands/results/counts/output paths in results/TASK-011.md; root will independently inspect selected output.
NOTES:
- Keep user-facing explanations simple Turkish. No arbitrary client path endpoints.
- Send architecture ambiguity promptly; no permission needed for authorized edits/tests.
- Do not mark DONE until actual filtered PST and persisted UI/report verified. Source unchanged hash required.
