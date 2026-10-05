# TASK-014 account checkpoint — ASTRA record

Status: account storage/transport compiled; API runtime/UI acceptance pending. Not TASK DONE.

SOL reported Release build 0 warnings/errors, account aggregate12/12 (service8 + rootcritical4), full backend194/194 PASS. Root independently ran critical4/4 and security30/30. Store implements one atomic metadata+DPAPI envelope, persist before dictionary swap, immutable snapshots, expectedVersion concurrency and durable delete. Root acceptance files must not be changed to hide failures.

Real source wire inspected in LocalEngineApiEndpoints.cs:

- GET /api/accounts?companyId=...&projectId=... returns public DTO array.
- POST /api/accounts CreateImapAccountRequest; no secrets in result.
- GET /api/accounts/{id}?companyId=...&projectId=... public DTO.
- POST /api/accounts/{id}/update accepts companyId/projectId/expectedVersion and changed fields; version conflict409.
- POST /api/accounts/{id}/delete accepts scope and expectedVersion; removes only local configuration.
- POST /api/accounts/test draft parameters or saved AccountId + scope; TestConnectionResult {success,error,message,latencyMs}.
- POST /api/accounts/{id}/test scope.
- GET /api/accounts/{id}/folders?companyId=...&projectId=... returns {accountId,folders:[{name,fullPath,delimiter,isSelectable,messageCount,unreadCount}]}.
- PUT/DELETE and POST/folders aliases currently exist. Complex optional request inference on GET/DELETE is a possible real-host startup blocker; verify startup and split query/body handler paths as needed.

Root actual HTTP checker prepared at Temp/bitigmail-task014-qa/api_accounts.py; not run yet, awaits SOL-owned TestingHost6175 readiness. Script uses fresh isolated company IDs and existing Dovecot test credentials without outputting them; tests original source account/folder counts and public/no-store/session boundaries. Never invoke a native picker.

Remaining explicit correctness fix: ListFolders must not report0 or partial success after STATUS/Open/GetSubfolders failures. Return safe error or explicit unreadable state that blocks preview. Prior source/discovery catches silently hid failures.

Authoritative lab config: lab/task014/dovecot/local-credentials.json, both accounts127.0.0.1:5143, delimiter '/',12source physical/four attachments. Root security policy test exception5143 only. Root public MailKit and independent Python full source/target proof PASS (docs/IMAP_TRANSFER_VALIDATION.md). GreenMail4143 preserved with independently reproduced noon bug; not the positive acceptance target.

Next: SOL/Gemini normal accountUI and frozen preview/shared job/journal/transfer UI/report; root critical acceptance. No new agents/tasks. Keep normal6174 and Vite5173 and user-requested LAN static preview192.168.1.51:5176 available; SOL coordinates6175 Release builds and hands it to root for readonly API acceptance windows.
