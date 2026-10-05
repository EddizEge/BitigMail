TASK_ID: TASK-037C
STATUS: READY after037B
EXECUTOR: SOL DIRECT, same existing task, no new agent
GOAL: Finish Stage7 user-facing controls and remaining archive filter integration, then final acceptance evidence.

Root backend now supports versioned AdvancedFilter on local MIME→PST, IMAP→IMAP, Bridge EML/mboxrd→IMAP and IMAP→EML/mboxrd. Shared MimeAdvancedFilterAdapter parses full MIME visible text and FrozenMailFilter validates persisted canonical fingerprints. IMAP44/44; Bridge oldregression122/122 +newactualoutput3/3. Do not reimplement these cores.

1. Replace sole JSON textarea with Turkish field/operator/value rule editor and bounded VE/VEYA grouping. Apply same component to MIME/IMAP/Bridge workflows. Reset/re-preview when filter/mapping/dedup/source changes. Surface unknown metadata count. Templates save/apply configuration only and require freshpreview. Waiting-job priority realcontrols; no activejob interrupt.
2. Finish local MIME mapping/dedup execution +report if not037B. No selected-ID or filteredsource bypass.
3. Archive advancedAST search must evaluate complete bounded raw MIME, not truncated index/display body. Existing catalog search scope validation precedes raw read. Filtering BEFORE pagination/counts; itempreview andselected-result job must validate sameAST. Source qualification dates block. Add cancellation and clear bound errors rather than partialcount success; stream/bounded memory. Report unknownmetadata; no rawbody inlogs. A knownlate-body match beyondindexcutoff must befound. Separate legacy indexquery semantics from AST; do not advertise legacytruncatedFTS as exactfullbody.
4. POP is read-only snapshot source: direct pre-download fullbodyfilter remains unsupported, strictunknown-property rejection. User flow may normalize verifiedPOP→EML then apply sharedfilter. PST/OST/OLM/EMLX can use qualifiedEML normalization thenfilter, preserving sourcefidelity flags. Show clear supportedroute guidance; no false one-step support. Contacts/calendar/tasks remainexcluded/countable.
5. Actual rendered UI tests using ownedTestingHost6175 andsourcefixtures, fullbackend/frontend/type/lint/build. Report detailed Stage7matrix/durableplan/filtermetadata/mapping/dedup evidence. Stage7cannotclosewithonlyAPI orJSONtextarea.

Scope andcriticalboundaries in037/037B remain. Do not overwrite normal6174 or read credentials file. SDK .tools/dotnet/dotnet.exe, isolated artifacts path. Stage8starts onlyafterrootacceptance butprepare concise038securitycontract (useprovidedcores/docs).
