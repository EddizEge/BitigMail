TASK_ID: TASK-032
STATUS: DONE — root local acceptance 2026-09-15: backend518/518, frontend145/145, EMLX24/24, responsive3/3 and actual TestingHost workflow1/1. Original source unchanged12mail/4attachments. Synthetic complete EMLX only; opaque Apple metadata and external attachment exclusions retained. Normal6174 untouched.
FROM: ASTRA HIGH
TO: Existing SOL
EXECUTOR: SOL DIRECT — explicitly authorized standing Antigravity failure exception for TASK032 and remaining stages3–9. Read .codex-coordination/EXECUTION_OVERRIDE.md. Do not use computer-use/Antigravity UI or re-escalate that unavailable surface.
GOAL: Stage4 first production slice: EMLX file/tree to verified EML tree, exposed in Transfer/Conversion, reusable in archive/IMAP/PST existing workflows.

ROOT DESIGN:
- Use accepted Engine/Storage/EmlxRecordReader exact byte parser; preserve metadata in sidecar as opaque bytes, do not silently map flags/dates. Source is native selected files/directory via protected handle registry, no arbitrary path request or upload.
- Build immutable manifest of original physical files hash/size/relative path; reject symlinks/reparse/out-of-root traversal. Only complete .emlx accepted; .partial.emlx must cause explicit blocker, never silently ignored as successful archive. Unsupported external attachment storage detected or clearly excluded from accepted scope. Preserve physicalduplicates and folderhierarchy, deterministic safe output names with physicalordinal; no collisions/case aliases or original overwrites.
- Separate explicit normalization job writes fresh EML tree + source/metadata/output-hash manifest in new selected output directory. Use global queue, owner scope and disk preflight, report/JobCenter metadata. No file writes during preview except existing safely managed planning artifacts. Rehash source beforewrite and finalcheck; any mismatch/parseerror yields incomplete/failedreport. Atomic create-only output publication, no claims of resume until tested.
- Validate emitted MIME with MimeKit but retain exact raw bytes. Compare output bytes and MIME attachments with input segment. EMLX plist stays sidecar; don't claim sourceflagmetadata fully propagated toIMAP yet. Expose known qualification in UI/report.
- UI clear source EMLX/Apple Mail files, output EML tree, company/project retained. Completed job action can open existing archive/transfer workflow using supported sourcehandle; if direct action requires newcontracts, use explicit selectoutput with saved path and guide, no fake autochain. Never inject reader as live-supported OLM accidentally.

VERIFICATION: real synthetic EMLX set derived from accepted mail-corpus-v1 using exactbytecounts, Turkishnestedfolders/attachments/duplicates/metadata; missingtruncated/partial blockers, diskinsufficient, sourcechanged, collisions/reparse, queuedjob/frozenowner. Fullregression+type/lint/build and actual rendereddesktop/mobile. Only small syntheticfixtures, no existingmaildeletion, no scale rerun. Document matrix direction and metadata limitations. Root Emlx+OLM corefiles may be read; changes coordinate first.

NEXT Stage4 slices (separate packages after this): PST/OST/OLM-to-EML preservation adapter (root OLMbinding+rawattachment oracle), POP source-only capture, MBOXvariant decision. No Stage4 complete until required slices accounted for.
