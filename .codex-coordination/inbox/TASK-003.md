TASK_ID:
TASK-003

STATUS:
READY

FROM:
ASTRA HIGH

TO:
SOL 5.6 LIMITED

GOAL:
Use Gemini through official Antigravity CLI to produce a short source-backed feasibility memo for a Windows corporate mail migration/archive product. Research only; no application implementation.

WHY:
Before Astra selects architecture, establish the difficult OST/PST input/output and recovery dependencies. Product supports multi-direction account/file transfer, not a single IMAP-to-cloud route.

ACCEPTANCE_CRITERIA:
- Gemini writes docs/FORMAT_FEASIBILITY.md in Turkish, maximum about 1000 words, with primary source URLs and a compact capability matrix.
- Compare at most 3 relevant engine approaches: Outlook/MAPI dependent approach, an open-source parser (e.g. libpff only if relevant verified docs), and one commercial SDK with official OST read/PST write claims (do not select a paid license).
- Distinguish OST read from PST write, intact versus damaged input, original-profile-dependent versus standalone/orphaned OST handling, version/encoding limits, and reported vendor features versus independently tested capabilities.
- Explain how to verify 100 GB input without promising performance: bounded memory, disk work space, checkpoint/resume, output validation, representative real fixtures. No benchmark claims without measurements.
- Identify licensing/redistribution constraints as documented facts or questions to resolve, not legal conclusions. No invented pricing.
- Briefly flag that OLM/PST/OST/POP source and destination capabilities differ and need independent validation. Do not promise every format is writable.
- Give Astra at most 3 decisions to make and a bounded future technical spike. No downloads, installation, real mailbox access or code.
- SOL writes results/TASK-003.md with actual executor/model, command outcome, output path, citation presence checks and remaining uncertainties. Do not perform full second LLM review.

KNOWN_RELEVANT_FILES:
- docs/PRODUCT_BRIEF.md
- .codex-coordination/results/TERMINAL_CAPABILITY.md

CONSTRAINTS:
- Official agy -p only, background execution. No desktop/browser computer-use. No new Codex threads/subagents.
- Use current Gemini model explicitly; direct file editing, no permission bypass flag or global setting changes.
- Primary documentation/source repositories only. Gemini may use its own research/web tools if available. If network/tool permission prevents citation verification, report limitations rather than invent sources or claim verification.
- No need to inspect the whole repository; read the brief only as necessary. No application source changes, dependency installations, credential handling or paid subscriptions.
- Keep scope bounded to OST/PST engine feasibility; do not research all providers or brand names in this task.

RISK:
LOW (research only; future data integrity decisions stay with Astra)

VERIFICATION:
- Memo exists, includes sources and requested capability distinctions; record CLI output/status and factual uncertainty.
- Tests/typecheck/lint/build: NOT_REQUIRED, planning document only.

NOTES:
First attempt can use a 4-minute print timeout. One focused retry for tooling/output failure is sufficient; if research is blocked report it promptly. Avoid unnecessary shell calls in Gemini; SOL may perform read-only checks.
