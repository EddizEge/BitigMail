TASK_ID: TASK-015
STATUS: READY — architecture gate
FROM: ASTRA HIGH
TO: Existing SOL / official Antigravity Gemini
GOAL: Microsoft 365 delegated OAuth account connection integrated into existing IMAP transfer, provider pilot readiness, honest local validation when real tenant unavailable.
RISK: HIGH — authentication decisions owned by root.
NOW: Have Gemini inspect existing account/store/client/transfer authentication seams and frontend account form. Write a concise implementation map to .codex-coordination/results/TASK-015-discovery.md (file paths, public API proposal, necessary compatibility points). Check official NuGet Microsoft.Identity.Client stable package metadata and dependency suitability for pinned net8; report a proposed exact version, do not add it yet. No production implementation before root docs/MICROSOFT365_CONNECTION_WORKFLOW.md contract arrives. Do not spend more than one bounded CLI turn on this gate.
CONSTRAINTS: USER_PROTOCOL applies. No new Codex threads or agents. No CUA/native automation. No actual tenant changes, cloud login, real messages, protocol logs or secrets. Preserve TASK014 229backend/65frontend and fixture/lab/runtime. Root preparing security contract and independent tests. Existing normal6174/dev5173/LAN5176 remain. Testing6175 available to SOL when needed. Report promptly; no full review.

IMPLEMENTATION AUTHORIZED: authoritative docs/MICROSOFT365_CONNECTION_WORKFLOW.md. Exact Microsoft.Identity.Client4.89.0 and required official transitives approved; retain existing pins. Root owns Microsoft365Policy.cs/tests (25/25PASS). Gemini owns backend/API/UI/tests; SOL deterministic validation. Target local-ready/provider-live-pending if tenant unavailable.
