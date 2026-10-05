TASK_ID: TASK-018
STATUS: DONE — ROOT ACTUAL ACCEPTANCE 2026-09-14
FROM: ASTRA HIGH
TO: existing SOL 5.6 LIMITED -> official AGY Gemini
GOAL: Real managed EML/mboxrd archives with multi-company/project/archive search and safe message preview.
AUTHORIZATION: User approved this milestone after TASK017 file/account bridge; Azure/liveOutlook remains deferred. Reminder is roadmap note only, automation PAUSED by user request. No new Codex thread/subagent.

AUTHORITATIVE_CONTRACT: docs/LOCAL_ARCHIVE_SEARCH_WORKFLOW.md plus docs/LOCAL_ARCHIVE_SEARCH_PLAN.md.
FIRST_ACTION: Discovery accepted and TASK017 DONE. Apply the root decision addendum in docs/LOCAL_ARCHIVE_SEARCH_WORKFLOW.md; backend checkpoint first, then frontend and actual acceptance. Existing SOL/Gemini is authorized to implement this task now. Baseline369backend/110frontend, plus38root policy tests. Normal6174 was stopped by root after PID/executable/port ownership checks; SOL owns isolated6175. Do not edit root-owned ArchiveSearchPolicy.cs or ArchiveSearchPolicyCriticalTests.cs.

DISCOVERY_DELIVERY: Concrete current integration points (source handle/vendor-free descriptor, job/report/storage/JobCenter, ArchiveSearchView/state/client company/project), proposed DTOs and durable state transitions, managed raw/manifest storage vs rebuildable SQLite index, scope validation and preview boundaries, restart/reindex strategy, bounded parsing/index limits, exact files to add/change. Identify any conflict with current contracts instead of inventing duplicate APIs. No broad redesign; preserve yellow/orange/white brand and existing PST/OST/IMAP/Microsoft paths.

LOCKED_ARCHITECTURE: Exact Microsoft.Data.Sqlite10.0.12 approved by root isolated net8/FTS5 probe10/10; no other new dependency without bounded rationale. Source raw MIME preserved, each physical record distinct including sameMessage-ID/samehash; source never deleted/changed. Catalog binds company/project/archive IDs; every search/count/preview validates selected triples, empty selection means empty. Raw HTML never executes or fetches remote images. FTS plain literal terms, parameterized SQL/closed fields, NFC+tr-TR lowercase then dotlessı→i for searchable copy only. Inline named attachments included; body excludes attachment contents. Stored manifest independent of SQLite for rebuild; partial import/index not completed. Full contract details are authoritative.

FUTURE_ACCEPTANCE: Root independent scope/content/durability oracles; Gemini ordinary implementation/tests; SOL deterministic test/typecheck/lint/build. Existing TASK017finalbaseline will be provided before productionwork. Actual two-company/three-archive(EML,MBOX,bridgeexport) ingestion/search/preview/API+desktop/mobile; source/storedSHA/physicalduplicates/Turkish/date/attachments; interrupted ingest/index/restart/reindex; schema/corruption/safe errors. Reference-only fixture expected searches in .codex-coordination/evidence/TASK-018/expected-fixture-search.json; no product acceptance claim yet.

NEXT: Once TASK018 accepted, staged128MiB then1GiB scale under docs/SCALE_AND_RESILIENCE_PLAN.md. 100GBPST/OST/repair remains license/data-dependent and unverified. Azure and realOutlook silmesizpilot manual reminder at milestone transition; do not create or reactivate scheduler.

