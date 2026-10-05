TASK_ID: TASK-038
STATUS: DONE
EXECUTOR: SOL 5.6 LIMITED (AUTHORIZED ANTIGRAVITY FAILURE EXCEPTION)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.LocalHost/Security/* (identity catalog, sessions, bootstrap proof, profile lease, rate limiting, route/resource authorization)
- engine/BitigMail.LocalHost/IdentityApiEndpoints.cs
- engine/BitigMail.LocalHost/Program.cs
- engine/BitigMail.TestingHost/Program.cs
- engine/BitigMail.LocalHost/Jobs/* authorization-related files
- engine/BitigMail.LocalHost/OAuth/GoogleOAuthOperationManager.cs
- engine/BitigMail.LocalHost/OAuth/MicrosoftOAuthOperationManager.cs
- engine/BitigMail.LocalHost/Dialogs/FileHandleRegistry.cs
- engine/BitigMail.LocalHost/Archive/ArchivePlanStore.cs
- engine/BitigMail.LocalHost/Archive/ArchiveSelectedJobService.cs
- engine/BitigMail.LocalHost/Bridge/Transfer/*PreviewService.cs
- engine/BitigMail.LocalHost/Imap/Transfer/ImapTransferPreviewService.cs
- engine/BitigMail.LocalHost/Pop/PopSnapshotService.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints*.cs
- engine/BitigMail.Engine.Tests/*038*.cs and security boundary suites
- prototype/src/components/auth/IdentityGate.tsx
- prototype/src/api/localEngineClient.ts
- prototype/src/App.tsx
- prototype/tests/e2e/task038-production-identity.spec.ts
SUMMARY:
- Persistent local users, Admin/Operator roles, atomic company/project catalog and framework password hashing implemented. First administrator requires the single-use native proof; production legacy anonymous sessions are disabled.
- Memory-only bounded sessions enforce absolute/idle expiry, logout/revocation and security-version invalidation. Password/role/active/grant updates revoke existing sessions and preserve the final active administrator.
- Production authorization is deny-by-default. Exact HTTP method/template inventory is startup-pinned; unknown routes fail closed. Body size, malformed/chunked JSON, Origin, Host and content-type boundaries are enforced.
- Account, archive, job, report, handle, selection, preview, plan and OAuth operation ownership is validated against the authenticated user, exact session and authoritative stored scopes. Queued and first-launch dispatch recheck current authorization; already-running source-safe work completes, but revoked users cannot start/resume further work.
- Production and development profiles are separate; no implicit migration or deletion occurs.
- First-run/login/company/project/user management UI is rendered. Admin grants company/project access explicitly; both Admin and Operator have logout access.
VERIFICATION:
- Backend Release full regression: 808 passed, 0 failed, 0 skipped.
- Frontend Vitest: 151 passed, 0 failed across 16 files.
- TypeScript typecheck: PASS. ESLint: PASS. Production build: PASS.
- Secure TestingHost UI: first-run/admin/company/operator/login/logout across three viewports: PASS.
- Secure HTTP ownership matrix: owner handle/selection preview/start positive; other user and same user/new session 404; operator admin route 404; cross-company accountId substitution 404: PASS.
- Real two-company archive matrix: owner preview/start 200; other user and same user/new session 404; own-scope search non-empty; mixed-scope search 404; archiveId/scope substitution 404; operator catalog limited to authorized company: PASS.
- OAuth authorization matrix: 33/33 PASS. Dispatch authorization matrix: 11/11 PASS.
- Normal LocalHost port 6174 was not stopped or modified.
RISKS:
- Login throttling and transient ownership registries are process-local, matching the single production host model.
- Existing production bundle chunk-size warning (>500 kB) remains; build succeeds.
- Legacy production data stays isolated until a later explicit reviewed migration workflow; it is not silently imported.
UNCERTAINTIES:
- No unresolved Stage 8 authorization blocker. Real external provider behavior remains subject to existing provider/network qualification boundaries.
