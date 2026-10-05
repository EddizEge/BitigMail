TASK_ID: TASK-030
STATUS: DONE
EXECUTOR: SOL 5.6 LIMITED (AUTHORIZED ANTIGRAVITY FAILURE EXCEPTION)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- .codex-coordination/results/TASK-030.md
SUMMARY:
- Completed the requested read-only Stage 3 connection inventory. No product code, account, host, port, credential, or provider state was changed.

CURRENT_SUPPORT:
- Generic IMAP password accounts support source and target use through account test, folder listing, preview, IMAP-to-IMAP transfer, EML/mboxrd-to-IMAP import, and IMAP-to-EML/mboxrd export. TLS modes are SSL-on-connect and STARTTLS; plaintext is restricted to loopback lab use.
- Microsoft delegated OAuth supports corporate Exchange Online with a canonical tenant GUID and personal Outlook.com/Hotmail with the explicit `consumers` authority. Both use MSAL Authorization Code + PKCE, system-browser/localhost callback, `IMAP.AccessAsUser.All`, XOAUTH2, `outlook.office365.com:993`, per-account DPAPI-protected token cache, silent refresh, reconnect, account test/folders, and all existing IMAP source/target directions.
- Microsoft support is own-mailbox delegated access only. Graph, SMTP, POP, shared mailboxes, app-only/tenant-wide access, sovereign clouds, arbitrary endpoints, and automatic live sign-in are outside the current contract.

SOURCE_EVIDENCE:
- OAuth request/status contract: `engine/BitigMail.Engine/Imap/OAuth/MicrosoftOAuthDtos.cs` (`StartMicrosoftOAuthRequest`, `MicrosoftOAuthOperationStatusDto`).
- Authority, tenant, scope, endpoint and identity policy: `engine/BitigMail.LocalHost/Security/Microsoft365Policy.cs` (`GetExpectedTenantId`, `ValidateIdentity`, `ValidateAuthorizationUri`).
- Interactive and silent MSAL flows: `engine/BitigMail.LocalHost/Security/MsalMicrosoftAuthProvider.cs`.
- Bounded operation lifecycle/API: `engine/BitigMail.LocalHost/OAuth/MicrosoftOAuthOperationManager.cs`; `engine/BitigMail.LocalHost/LocalEngineApiEndpoints.OAuth.cs`.
- Account persistence/resolution: `engine/BitigMail.LocalHost/Imap/ImapAccountRecord.cs`; `ImapAccountStore.cs`; `Security/ImapCredentialResolver.cs`.
- XOAUTH2 and endpoint enforcement in transfer/test paths: `engine/BitigMail.LocalHost/Imap/ImapClientService.cs`; `Imap/Transfer/MailKitTransferClient.cs`.
- UI and transfer selection: `prototype/src/components/clients/ProjectImapAccounts.tsx`; `prototype/src/hooks/useMicrosoftOAuth.ts`; `prototype/src/components/transfer/ImapTransferWorkflow.tsx`.
- Current deterministic coverage: `MicrosoftOAuthTests.cs`, `Microsoft365PolicyTests.cs`, `Microsoft365LifecycleCriticalTests.cs`, `ImapAccountServiceTests.cs`, `ImapAccountCriticalTests.cs`, `ImapSecurityTests.cs`, and frontend `microsoftOAuth.test.tsx`, `imapAccounts.test.ts`, `imapTransferWorkflow.test.ts`.

MISSING_STAGE3_CAPABILITIES:
- Google/Gmail/Google Workspace delegated OAuth is absent: no Google authorization provider/policy, OAuth endpoints/DTO discriminator, Google token-cache lifecycle, `imap.gmail.com:993` profile, Gmail identity validation, reconnect UI, or deterministic Google-provider tests. Generic password IMAP is not equivalent to Google OAuth.
- Provider retry/quota diagnostics are not a stable public contract. OAuth operation and IMAP layers mostly return redacted free-text failures; there is no provider-neutral error category, retryability flag, retry-after value, throttling/quota classification, correlation-safe diagnostic code, or Job Center remediation model.
- On-premises Exchange has no declared protocol/authentication matrix or product profile. Arbitrary Microsoft OAuth endpoints are intentionally rejected. Basic/password IMAP may technically target a configured server under the generic policy, but that is not validated or claimed as Exchange support; EWS, NTLM/Kerberos, hybrid modern auth, certificate/private-CA policy, autodiscover, and Exchange version boundaries are absent.

PROPOSED_NEXT_PACKAGE:
- Implement a bounded Google OAuth IMAP vertical slice while preserving the existing Microsoft contract: corporate Google Workspace and consumer Gmail delegated OAuth, own mailbox only, `imap.gmail.com:993` SSL, XOAUTH2, per-account protected cache, test/folder discovery, reconnect, and reuse of every existing IMAP transfer direction.
- Add an explicit provider discriminator and provider-neutral public connection error contract with `code`, `category`, `retryable`, optional `retryAfterSeconds`, and redacted message. Initially classify authorization-required, authentication-rejected, TLS/certificate, timeout/network, provider-throttled/quota, and unsupported-capability cases. Do not add automatic APPEND retries in this slice.
- Expected contract/files: extend IMAP account DTO/record/store and credential resolver; add Google OAuth DTOs, policy/provider/operation manager and API endpoints under `engine/BitigMail.Engine/Imap/OAuth` and `engine/BitigMail.LocalHost/{OAuth,Security}`; update `ImapClientService.cs` and `MailKitTransferClient.cs` to enforce the Google profile; extend `localEngine.ts`, `localEngineClient.ts`, account hooks, `ProjectImapAccounts.tsx`, and transfer labels.
- Add backend policy/lifecycle/security/account/transfer regression tests and frontend connect/cancel/reconnect/scope-switch/source-target tests. Verification for the implementation package: full backend tests, full frontend tests, typecheck, lint, production build, and rendered desktop/mobile fake-provider flows.
- Live Gmail/Workspace authorization, tenant/admin consent, provider quotas, mailbox policy, and real APPEND/flags/keywords fidelity remain a separate provider-live acceptance gate. Local fake-provider and local IMAP tests must be reported only as `LOCAL_READY / LIVE_GOOGLE_PILOT_PENDING`.

RISKS:
- Google OAuth consent/audience rules and Workspace admin restrictions require an approved app configuration and cannot be proven locally.
- Provider throttling and quota behavior cannot be accepted from synthetic errors alone; the structured contract can be tested locally, but real limits require a controlled live pilot.
- On-premises Exchange should remain a later discovery/decision package rather than being silently treated as generic IMAP support.

UNCERTAINTIES:
- Exact Google OAuth scopes, app type, verification requirements, and current Gmail/Workspace IMAP enablement policy must be revalidated against official provider documentation when implementation begins.
- No live provider acceptance is claimed by this inventory.

VERIFICATION:
- Read-only inspection referenced concrete current symbols, UI paths, workflow documents, and test suites.
- Product source and running processes were not modified; no tests were required or run for this proposal-only task.
