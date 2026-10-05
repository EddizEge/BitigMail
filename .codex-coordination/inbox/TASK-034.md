TASK_ID: TASK-034
STATUS: WAIT — Stage4 POP slice after032/033
EXECUTOR: SOL DIRECT — standing authorized exception, .codex-coordination/EXECUTION_OVERRIDE.md. No computer-use/Antigravity retries.
GOAL: POP source-only snapshot into verified EML tree and existing downstream archive/transfer paths. No POP target, no source deletion.

ROOT BOUNDARY:
- MailKit POP3 TLS client, generic password/app-password authentication first. Existing Microsoft OAuth IMAP-only scope must not be represented as POP authorization. Google POP OAuth separate acceptance if implemented, not inferred from IMAP success.
- Native/UI source account configuration held in DPAPI-bound backend store, company/project scope and metadata version. Prefer additive explicit protocol discriminator with old records defaultimap; ALL old IMAP paths must reject pop account before connect. If this entails widespread security changes, propose separate POP store instead, root decides before broad edits. Never infer protocol from port alone.
- Read-only protocol contract: UIDL snapshot + LIST sizes + RETR streams, never DELE/DeleteMessage/DeleteMessages/DeleteAllMessages. Disconnect QUIT only without deletions. UIDL absent/duplicates or list changes must fail clearly, no unstable sequence-number-based resume. Frozen account/UIDL plan validated before each download; network interruption persists only verified output and explicitresume needs freshUIDL mapping.
- Original POP server has no general folder hierarchy; output single source folder. Internal received date/readflag cannot be invented from POP; preserve RFC messageDate separately and report unavailable metadata. RETR/server line normalization distinguished from original mailbox-on-disk bytes. Compare retrieved rawhash between staging/output and independent localserveroracle.
- One message at time,64MiB messageguard, initialmessagecount/resourcepreflight beforelargeUIDL list; durable originalUIDL-to-output mapping, deterministic no-collision names, physicalduplicates preserved evenMessageID same. New outputdirectory/no overwrites; common diskcheck, queue, jobreports, ownerimmutability.
- Filter subset by retained MIME fields/date only; unavailable POP serverflags neither guessed nor claimed. UI labels source-only and mailsremainonserver. ExplicitoutputEMLtree downstream actionusesexisting flow, nofakeautoconnection.

VERIFICATION: owned localPOP server with synthetic12mail/4attachments, sourceUIDL/messagecount/content unchanged; instrumentserver rejectsDELE andrecords0 suchcommands. UIDL reorder/add/remove, noUIDL, duplicateUIDL, lostresponse/cancel/restart, changedsource, sameMessageIDdiffcontent/physicalduplicates, TLS/auth errors; frontenddesktop/mobile source-onlyconstraint. Neverusepersonalrealaccounts. Rootno100GB/liveproviderclaim.

ROOT IMPLEMENTATION DECISION 2026-09-19:
Use a separate POP account store/DTO/endpoints backed by the existing Windows credential protector. Do not retrofit a protocol flag through all existing IMAP account paths. Shared company/project authorization will be integrated in038; POP IDs must be rejected by IMAP store naturally. Reuse only secret-protection and common safe metadata primitives. This avoids accidental POP/IMAP OAuth or protocol confusion. Source-only POP UI may be a distinct flow under transfer; user-visible label POP kaynağından al, postalar sunucuda kalır. Production TLS remains mandatory; only owned testing profile may use local test transport.

ROOT DISPATCH 2026-09-19: STATUS READY. TASK033 qualified acceptance complete. Implement034 now including appended separatePOP store decision. Return result and verification;034B waits root acceptance.
