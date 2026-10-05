TASK_ID: TASK-031
STATUS: DONE
EXECUTOR: SOL 5.6 LIMITED (AUTHORIZED ANTIGRAVITY FAILURE EXCEPTION)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.LocalHost/BitigMail.LocalHost.csproj
- engine/BitigMail.Engine/Imap/ProviderCapabilityModels.cs
- engine/BitigMail.Engine/Imap/ImapAccountDtos.cs
- engine/BitigMail.Engine/Imap/OAuth/GoogleOAuthDtos.cs
- engine/BitigMail.LocalHost/Security/GoogleOAuthPolicy.cs
- engine/BitigMail.LocalHost/Security/IGoogleAuthProvider.cs
- engine/BitigMail.LocalHost/Security/GoogleAuthProvider.cs
- engine/BitigMail.LocalHost/Security/IImapOAuthConnectionTester.cs
- engine/BitigMail.LocalHost/OAuth/GoogleOAuthOperationManager.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.GoogleOAuth.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.cs
- engine/BitigMail.LocalHost/Program.cs
- engine/BitigMail.LocalHost/Imap/ImapAccountStore.cs
- engine/BitigMail.LocalHost/Security/ImapCredentialResolver.cs
- engine/BitigMail.LocalHost/Imap/ImapClientService.cs
- engine/BitigMail.LocalHost/Imap/Transfer/MailKitTransferClient.cs
- engine/BitigMail.TestingHost/Security/FakeOAuthServices.cs
- engine/BitigMail.TestingHost/Program.cs
- engine/BitigMail.Engine.Tests/GoogleOAuthTests.cs
- engine/BitigMail.Engine.Tests/Microsoft365LifecycleCriticalTests.cs
- prototype/src/types/localEngine.ts
- prototype/src/api/localEngineClient.ts
- prototype/src/hooks/useGoogleOAuth.ts
- prototype/src/components/clients/ProjectImapAccounts.tsx
- prototype/src/components/transfer/ImapTransferWorkflow.tsx
- prototype/src/tests/googleOAuth.test.tsx
- prototype/tests/e2e/task031-google-oauth.spec.ts
- prototype/tests/e2e/task031-google-real-ui.spec.ts
- docs/GOOGLE_CONNECTION_WORKFLOW.md
- THIRD-PARTY-NOTICES.md
SUMMARY:
- Added the pinned official Google.Apis.Auth 1.76.0 dependency and a Gmail/Google Workspace own-mailbox delegated OAuth vertical slice using official PKCE code flow and LocalServerCodeReceiver infrastructure.
- Added a cryptographic 32-byte state wrapper around the official receiver. State is injected immediately before receiver execution and compared in constant time before code exchange may continue. Authorization URLs are allowlisted to exact Google HTTPS path, S256 PKCE, exact scopes and 127.0.0.1 loopback.
- Added validated ID-token identity binding (audience/issuer/expiry via official SDK, verified email, stable subject), fixed imap.gmail.com:993 SSL/XOAUTH2 enforcement, per-account DPAPI-protected cache/client configuration, refresh-token retention, client/subject/email cache binding, reconnect generation safety, and reauthorization mapping.
- Added bounded scope-checked start/status/cancel lifecycle, late-callback/timeout/dispose protection, transient authorization URL clearing, pre-persistence IMAP authentication, and no-success-on-persistence-failure behavior.
- Added provider-neutral public capability/diagnostic DTOs and capability endpoint. Trusted exception types map to authorization, network, TLS, authentication, quota/throttling, concurrency or safe fallback categories; no ambiguous APPEND retry was added.
- Bound stored and resolved OAuth credentials to their exact provider endpoint, port, TLS mode and mailbox identity. Unknown auth kinds and cross-provider tampered records are rejected before unprotecting credentials or requesting any bearer token.
- Propagated provider-neutral, redacted IMAP verification diagnostics through the real connection tester so Google UI status can distinguish authentication, TLS, network and timeout failures; caller cancellation remains cancellation rather than a generic connection error.
- Integrated Google add, cancel, reconnect, account test/folders and existing source/target selection labels. Client Secret is a password input, cleared after submission, never returned by public DTOs, and persisted only inside the account-bound protected backend envelope.
- Preserved existing password IMAP and Microsoft OAuth public behavior, including Microsoft-specific labels and legacy error compatibility.
- Added setup/security documentation, provider capability matrix and Apache-2.0 third-party notice.
VERIFICATION:
- Full backend regression: 511/511 PASS. This includes TASK-031 Google OAuth policy/lifecycle/cache/state/cancel/persistence tests, real Google SDK receiver/loopback critical tests, and two-direction persisted cross-provider tamper coverage proving neither token provider is called.
- Full frontend regression: 145/145 PASS across 12 files.
- TypeScript typecheck: PASS.
- ESLint: PASS.
- Production build: PASS; existing greater-than-500-kB chunk warning only.
- Rendered Playwright form: 3/3 PASS at 1660x948, 1366x768 and 390x844 with zero horizontal overflow.
- TestingHost add flow: 1/1 PASS. Exact connected-operation accountId was captured from the scoped status response, then its exact row was verified for unique owner, Google badge, Gmail/XOAUTH2 endpoint, connection test and reconnect action.
- Visual evidence: `.codex-coordination/evidence/TASK-031/google-connect-*.png`, `google-connected-testinghost.png`, `google-account-row-testinghost.png`.
- Browser plugin/skill was unavailable for this UI acceptance; the repository's Playwright harness was used as the deterministic rendered fallback.
- Owned TestingHost PIDs 57132 and 57872 were stopped; port 6175 verified OFF. Existing Vite 5173 PID 21576 and normal LocalHost 6174 PID 17200 were not interrupted.
RISKS:
- Google Cloud app configuration, consent-screen/audience policy, Workspace administrator restrictions, live throttling/quota headers and real Gmail APPEND/flags/keywords fidelity are not provable locally.
- The production LocalHost process on 6174 was intentionally preserved and has not been restarted onto this build; controlled restart remains a root acceptance/deployment step.
- Production bundle retains the pre-existing size warning.
UNCERTAINTIES:
- Provider-live behavior remains explicitly `LOCAL_READY / LIVE_GOOGLE_PILOT_PENDING`; no real Google sign-in, credential creation, mailbox read or mailbox write occurred.
- On-premises Exchange remains configured generic IMAP only and is not advertised as verified Exchange/EWS/MAPI support.
