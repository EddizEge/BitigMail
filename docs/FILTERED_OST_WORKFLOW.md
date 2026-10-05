# Tarih ve klasör filtreli OST → PST — TASK-011

Status: DONE / ASTRA ACCEPTED, 2026-09-12. Owner: Astra; implementation Gemini through existing SOL. Evidence: [FILTERED_OST_VALIDATION.md](FILTERED_OST_VALIDATION.md).

## Scope and user flow

Extend the real local OST conversion: select source, analyze, select folders/date range, inspect authoritative selected message/attachment counts, pick new PST destination, convert, reopen/verify, persist a report. Keep existing company/project context and historical jobs. Splitting, recovery, additional providers and licensed large-file work are later phases.

## Authoritative selection contract

- Folder checkboxes use exact stable source folder identities (storage EntryId anchored to source SHA), never display-path substring matching. Each checkbox includes that folder's own messages; descendants have separate checkboxes. Explain this in UI. Start with all folders selected; explicit empty selection means zero, never all. Unknown IDs reject.
- Dates are optional ISO calendar dates. Both visible days inclusive, using fixed Türkiye saati (UTC+03:00) in this first version, shown beside inputs and persisted. Internally use UTC start-inclusive/end-exclusive. Open bounds allowed; reversed/invalid ranges reject. Use stored message submission date, falling back to delivery date when absent; treat unspecified SDK MAPI times as UTC, never machine-local conversion. Missing dates included without a date filter, excluded with one and separately counted. Persist the date policy. Do not infer dates from subject or filesystem timestamps.
- Server computes preview over all physical source messages under one read-only lock and checks the source hash against registered analysis. Preview returns opaque selection ID, canonical filters, source/selected/excluded message counts, selected attachment count, missing-date exclusion count and folder counts. Keep physical entry IDs internal; do not deduplicate by Message-ID.
- Selection is immutable and bound to the source handle, source SHA and exact filters. Filtered start consumes a valid server selection ID, not browser counts or a client-supplied selected-ID list. Unknown/stale/wrong-source selections reject. UI discards stale asynchronous responses and invalidates preview/start immediately after any filter/source change.
- Conversion rechecks SHA under its existing read lock and applies the same immutable plan/predicate to physical messages; selected entries must all be accounted for, no extra output. Preserve original hierarchy (empty ancestor containers allowed). Reopen verification compares selected source snapshots only, preserving duplicate multiplicity, full primary-body and attachment/CID checks.
- Preserve whole-source preflight blockers, including ANY folder above the vendor 50-item evaluation limit, even if selected subset is smaller. No filtering-based license bypass. Fail-closed extraction/traversal and count reconciliation remain.
- Zero-match preview is valid but conversion is blocked, producing no PST. SourceTotal = Selected + Excluded; selected attachment count matches verified output. MissingDateExcluded is a subset of Excluded, not an extra amount. Successful Selected = Written with zero Failed.
- Reports and jobs persist frozen filters, timezone/date policy and source/selected/excluded counts. Old unfiltered jobs remain readable. Existing internal/API calls without selection preserve unfiltered behavior; new UI always obtains a preview. Idempotency includes canonical selection content, not just an opaque freshly-created ID; conflicting selection under same key is 409, also after restart.

## Existing boundaries

No new dependencies, external services, uploads, arbitrary path endpoints, nonce exceptions or cleanup of shared output directories. Preserve server-derived output location display, native picker architecture, exact Host/Origin validation before CORS, no-store including rejects, protected session/status, separate TestingHost, atomic new-output-only publication and interrupted-job behavior. No SDK/runtime upgrade in this task.

## Acceptance evidence

Tests must exercise production selection and converter code: inclusive boundaries and timezone independence, missing dates/open bounds/reversed ranges, exact folder identity including similar names/parent-child, empty/unknown folders, zero matches, physical duplicates, selected attachments/CID, whole-source trial blocker, stale source/selection, selection-sensitive idempotency and historical reports. Use generated synthetic PST storage internally where appropriate, but genuine OST end-to-end must still be tested.

Real fixture: lab/ost-spike/input/bitigmail-lab-full.ost, SHA B0801758A2E61D4CE6E86799701A81A7A60C38401F73B13C993D94C03A2EE57A; 13 physical messages (6 inbox, 3 sent, 4 Istanbul), 4 attachments. Record selected expected subjects/dates/attachment hashes independently before accepting filtered PST, and keep unfiltered regression. UI test change selection, zero result, real conversion/report/reload, desktop and 390px width. Run targeted backend and UI tests plus typecheck/lint/build; no native dialog automation.
