TASK_ID: TASK-038 SECURITY CONTRACT (PREPARATION ONLY)
STATUS: READY_FOR_ROOT_REVIEW — NOT IMPLEMENTED

1. Deny-by-default route inventory
- Enumerate every runtime `/api` route and assign exactly one policy: public setup-status, setup-proof, authenticated, operator-scoped, or admin.
- CI fails for an unclassified route. Counts and pagination run only after authorization. Unknown and unauthorized resources use the same 404 policy.

2. Identity, bootstrap, and sessions
- Production is default; development uses an explicit profile and separate data directory.
- First-admin proof: inherited private pipe, >=32 random bytes, one use, short lifetime; never argv/URL/log/plaintext disk. Atomic single winner.
- Versioned password hashing, >=12-character minimum, bounded maximum/concurrency, gradual user+profile throttling, generic errors.
- Sessions: 32 random bytes, memory only, 8-hour absolute and 30-minute idle expiry; restart/logout/password or role change invalidates them through a user security version.

3. Ownership and immutable execution
- Handles, previews, OAuth operations, templates, jobs, archives, and recovery artifacts bind to actor+session and persisted company/project ownership.
- Server resolves ownership from stored records; request clientContext is never authority. Mixed-scope requests fail as a whole.
- Dispatch/resume rechecks current access plus frozen source/query/filter/mapping/dedup fingerprints. Revoked queued jobs cannot start.

4. Roles and sensitive operations
- Operators act only in allowed customer projects. User/role/grant management, global audit, backup/restore, and SDK license choose/save are admin-only.
- Last active admin cannot be removed. Projects/customers with archives require deactivate/explicit migration, not destructive deletion.
- License status is authenticated and never exposes paths or license bytes.

5. Stage-7 endpoint additions
- Templates are creator-owned portable configuration, never capabilities; apply always requires a fresh preview under caller permissions.
- Selected-archive preview/start/report verifies every archive owner and target project before metadata, counts, scanning, or pagination.
- Waiting priority/cancel/resume authorize against the recorded job. POP/OLM/EMLX/recovery/native picker handles follow the same actor/session binding.
- Raw MIME search authorizes first, supports cancellation and the 64 MiB/message gate, and leaks no unauthorized unknown-count/warning data.

6. Browser/WebView boundary
- Exact Host; authenticated API; mutation requires exact Origin and JSON. Origin-less GET is limited to authenticated safe reads.
- SPA fallback never turns `/api` or unknown asset paths into HTML success. No wildcard CORS.
- Mail HTML/scripts/remote images/attachments never execute in the privileged page. External navigation/new-window is denied by default; explicit user action may open allowlisted HTTP(S) in the system browser. Reject file/javascript/data/custom protocols.

7. Deterministic acceptance
- Runtime route inventory test covers every discovered endpoint and policy.
- Negative matrix: cross-user/company/project handles, mixed archives, stale sessions/previews, revoked queued dispatch, operator admin actions, CSRF/Origin/Host/content-type failures, enumeration-safe 404s, template capability escalation, unsafe WebView navigation, and legacy-dev-data implicit ownership.
- Positive matrix covers minimum permitted actions for setup, authenticated operator, and admin. Test evidence must come from production-profile middleware plus real loopback browser flow; development anonymous behavior is not acceptance evidence.

ROOT REVIEW20September: APPROVED. UseframeworkIdentityPasswordHasher andexistingroot session/bootstrapcores. Catalogfirstadmincreate mustcrossprocesssinglewinner viaexclusivecanonicalprofilelock ornamedmutex plusatomicwrite; corruptcatalogfailsclosed. Ephemeralhandles/previews/OAuthbinduser+session. Persistedjobsretainactor+storedscope; freshauthorizedsession mayexplicitlyresume underauditednewrunactor, notpermanentexpirylock. Queueddispatch rechecksactiveactor/currentsecurityversion/scope; revokedworkdoesnotstart. Alreadyrunningauthorizedsource-safejobs mayfinish. Templatecreatorownership/adminmanagement andnew037routefamilies aremandatory. Implementationstartsafter037acceptance; contractapprovalaloneisnotsecurityacceptance.
