TASK_ID: TASK-014
STATUS: DONE
EXECUTOR: GEMINI
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- `lab/task014/**` and the separate Dovecot lab/harness
- `engine/BitigMail.Engine/Imap/**`
- `engine/BitigMail.LocalHost/Imap/**`
- `engine/BitigMail.LocalHost/Jobs/**` and IMAP API/DI integration
- `engine/BitigMail.Engine.Tests/Imap*Tests.cs`
- `prototype/src/**` account and IMAP transfer types, client, hooks, views, jobs, and tests
- `prototype/package.json`, `prototype/package-lock.json`
- `THIRD-PARTY-NOTICES.md`, TASK-014 coordination and validation documents
SUMMARY:
- Separate two-account IMAP labs, MailKit/MimeKit 4.17.0 integration, encrypted scoped account CRUD, immutable previews, filtered resumable copy, durable journal, shared jobs, reports, and account/transfer UI are implemented.
- Root independent Gate A, account/transfer wire acceptance, rendered UI, resume/reload flow and full regressions passed.
VERIFICATION:
- Gate A Dovecot: 12/12 messages, 4/4 attachments, exact serialization/target bytes/date/flags/keyword search and source unchanged PASS.
- Account API HTTP: 120/120 PASS; account UI desktop/mobile: 2/2 PASS before final transfer UI changes.
- Final transfer API HTTP + independent Python oracle: 155/155 PASS; filtered 3/1, full 12/4, reverse 3/1, missing 0, extra 0, source unchanged.
- Resume UI: missing journal 409; restored 2 Verified + 1 AppendIntent completed the same 3/1 with 0 new copies.
- Final regressions: backend 229/229; frontend 65/65; frontend typecheck/lint/build PASS.
- Final rendered UI: desktop 1660px and mobile 390px PASS, including frozen customer/project names, date filters, report download and reload.
- Normal rebuilt Debug host 6174: legacy security 20/20 and new IMAP boundary 4/4 PASS; plaintext lab TLS and missing session rejected, TestingHost account store isolated.
- Latest LAN 5176 build: desktop 1660px and mobile 390px loading/details smoke 2/2 PASS; physical phone access remains user-unconfirmed.
- Approved exact dev test packages installed; npm audit reported 0 vulnerabilities.
RISKS:
- The 12-message lab does not establish large-mailbox performance or provider-wide permanent-keyword behavior.
- This release supports username/password IMAP with required TLS. OAuth and provider-specific Google/Exchange models are outside this task.
UNCERTAINTIES:
- 100 GB operation and provider-wide behavior remain unaccepted and require separate validation.
- Physical-phone access to the LAN preview was not confirmed by the user; automated desktop/mobile viewport checks passed.
