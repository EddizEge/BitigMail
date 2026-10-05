TASK_ID: TASK-016
STATUS: READY
FROM: ASTRA HIGH
TO: SOL 5.6 LIMITED
GOAL: Add personal Outlook.com/Hotmail OAuth mode beside accepted corporate Microsoft365; ready for user-assisted login, no real account access this task.
WHY: User has a personal account and authorizes testing only if no email is deleted. No client registration or login exists yet.
ACCEPTANCE_CRITERIA:
- Follow docs/OUTLOOK_PERSONAL_WORKFLOW.md exact root architecture.
- Personal UI mode uses tenantId literal consumers internally; no tenant field; clientId remains required; no password. Organization GUID mode unchanged.
- Backend interactive/silent/cache/reconnect use consumers with actual MSA tenant identity validation. Normal host still real MSAL and strict IMAP TLS.
- Personal accounts render clearly in saved accounts, reconnect and source/target selectors. Preserve authKind microsoft365 for backward compatibility, label personal from tenantId consumers.
- Switching UI type clears/cancels old OAuth session; no stale URL/response leaks.
- User-facing copy says pilot uses separate BitigMail-Test folder and synthetic emails, no deletion; no existing personal mail read/write or real login in automated tests.
- Test real-shaped personal request, mode-switch isolation, wrong authority denial, saved personal reconnect and both selectors, no tenant/password in personal form. Keep old assertions.
- Update docs/README/ROADMAP/state/result with LOCAL_READY / LIVE_PERSONAL_PILOT_PENDING and separate corporate pilot pending.
KNOWN_RELEVANT_FILES: prototype/src/components/clients/ProjectImapAccounts.tsx, prototype/src/api/localEngineClient.ts, prototype/src/hooks/useMicrosoftOAuth.ts, engine/BitigMail.LocalHost/Security/MsalMicrosoftAuthProvider.cs, existing Microsoft OAuth tests.
CONSTRAINTS:
- ROOT EXCLUSIVE: Microsoft365Policy.cs and new OutlookPersonalCriticalTests.cs; root owns policy design and critical tests. Gemini MUST NOT edit those files. Other ordinary implementation/tests Gemini owns.
- MSAL4.89.0 exact unchanged; no new package, common/organizations authority, arbitrary URL, client secret, copied third-party client IDs, Graph/SMTP fallback.
- No cloud app registration, account sign-in, browser OAuth initiation/URL extraction, mailbox commands, tenant mutations or deletion. Prior browser automation approval rejection remains respected.
- No new Codex subagents/tasks; use existing SOL -> official AGY -> Gemini. Terminal background only. Normal6174 root controls; no need6175 for frontend mocks/unit tests.
RISK: HIGH auth boundary; root has supplied exact policy and owns critical checks.
VERIFICATION: baseline backend278/frontend83 preserved; full backend tests, frontend tests/typecheck/lint/build; report warnings. ROOT separately real MSAL consumers URL/state cancellation and critical identity tests. No real personal provider acceptance claim.
