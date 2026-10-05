TASK_ID: TASK-036
STATUS: DONE — LOCAL QUALIFIED ACCEPTANCE
EXECUTOR: SOL initial implementation; ASTRA HIGH critical lifecycle/data-integrity completion after four SOL capacity interruptions.
SUMMARY:
- Read-only PST/OST recovery runs in an owned child process; same-volume private staging, frozen source hash/owner, shared persisted global queue, explicit cancellation and bounded timeout including bootstrap pipe writes.
- Parent and child use one immutable startup license snapshot; no other-profile discovery, no hot license reload, no secret in arguments/logs. Unconfirmed termination and crash-during-running recovery persist a conservative dispatch block. Failed/uncertain staging is preserved, not recursively deleted.
- Reports bind current invocation/job/source, validate bounded strict JSON/path/size/hash/exact output set, reject reparse ancestors and entries before descent, preserve folder path/source identity hashes and count excluded non-mail. Per-loop limits and SDK fallback are qualified; no undelete/block-carving claim.
- Recovery manifest is recognized by downstream EML workflows. Partial warnings and conservative date-filter block survive selection. Tampered output is rejected.
- Customer/project context comes from actual UI selection. Persisted outcome survives reopen; detailed report is downloadable from workflow and JobCenter, with persisted report hash checked on download. No anonymous-role authorization claim: Stage8 remains future.
VERIFICATION:
- Full backend639/639 PASS: root036-full-final.trx.
- Frontend148/148 PASS; type build/lint/Vite build PASS (existing >500kB advisory only).
- Actual TestingHost recovery/source selection/customer scope/report and JobCenter download at desktop1660,compact1366,mobile390:3/3 PASS, tests/e2e/task036-recovery-ui.spec.ts.
- Root focused content/integration38/38, persisted lifecycle4/4, owned pipe deadline/lifecycle+license17/17.
- Generated known PST:4 messages/2 folders/4 attachments; independent MIME oracle validates folder, complete UTC date/time, attachment filename and payload SHA256, body presence and explicit subject trial addition qualification.
- Controlled corruption: overwrite512bytes at offset28672 withFF in generated copy. Actual partial_recovered:4 verified messages and1 failed boundary, original total unknown. Bounded discovery evidence root036-controlled-damage-probe.trx; frozen regression root036-content-and-integration.trx. Truncation to4096bytes yields explicit unreadable_source withzero recovered. Healthy and both damaged input hashes remain unchanged.
- Nonmail synthetic IPM.Contact excluded and counted. Recovery qualification selected-file hash tamper rejected; persisted report tamper rejected.
LIMITS:
- This is local acceptance against controlled generated corruption, not representative damaged customer corpus or100GB acceptance. Trial body/subject additions remain; no trial bypass.
- Recovery may fail when root cannot open. Metadata enumeration/fallback may allocate inside vendorSDK; no hard SDK memory-bound guarantee. Explicit request/item/folder/deadline/output limits apply.
- Unknown original totals staynull in recovery views/reports. Recovered dates conservatively blocked in downstream filters until source fidelity is established.
- If worker termination cannot be proved after crash, dispatch stays blocked pending verification; automatic release is not claimed. Failed private staging retained for diagnosis.
- Recovery queued/running jobs do not automatically resume after process restart. User must re-plan supported interrupted work; existing outputs never overwritten.
- Normal6174 preview untouched; ownedTestingHost6175 used for acceptance.
ROOT_ACCEPTANCE: Stage6 local qualified implementation accepted; proceed Stage7 TASK037. Azure/realOutlook remainsStage10, excluded.
