# TASK-015 Backend Checkpoint: Microsoft 365 Delegated OAuth Connection

**Status:** VERIFICATION PENDING SOL  
**Date:** 2026-09-13  
**Approved Direct Package:** `Microsoft.Identity.Client` 4.89.0 (pinned .NET 8)  
**Security Contract:** Authoritative `docs/MICROSOFT365_CONNECTION_WORKFLOW.md`, `Microsoft365Policy.cs` (25/25 root test baseline preserved untouched), and root `Microsoft365LifecycleCriticalTests.cs` (untouched).

---

## 1. Overview & Architecture Boundaries

TASK-015 introduces Microsoft 365 delegated OAuth2 authentication (Exchange Online global cloud IMAP endpoint `outlook.office365.com:993` with SSL) integrated alongside existing username/password IMAP accounts.

### Key Architectural Boundaries:
- **Root Security Boundary (`Microsoft365Policy.cs`)**: Untouched. Enforces canonical GUID tenant/client, single IMAP scope `https://outlook.office.com/IMAP.AccessAsUser.All`, HTTPS `login.microsoftonline.com` authorize endpoint, PKCE S256 challenge, localhost redirect, and exact own-mailbox identity match (`requestedEmail == actualUsername`).
- **Official MSAL.NET (`Microsoft.Identity.Client 4.89.0`)**: Public client application with Authorization Code + PKCE, dynamic loopback redirect (`http://localhost`), and `SystemWebViewOptions.OpenBrowserAsync` capturing authorization URL. Backend never launches a browser. MSAL manages state, PKCE, and loopback listener.
- **Cache Callbacks**: Corrected to use `TokenCacheNotificationArgs.TokenCache.SerializeMsalV3()` and `DeserializeMsalV3()` public extensions within MSAL notification callbacks.
- **Identity Pinning**: Never substitutes requested email/tenant when auth result `Account.Username`/`TenantId` is missing; passes empty string to policy to trigger strict failure. Silent acquisition verifies actual `TenantId` and `HomeAccountId` are pinned before token return.
- **Credential Redaction**: `ImapPasswordCredential`, `ImapOAuth2Credential`, and `ResolvedImapCredential` explicitly override `ToString()` with `[REDACTED]` to guarantee zero secret, token, or password rendering in logs or diagnostics.
- **Single Atomic Envelope & DPAPI Encryption**: Existing single-file atomic JSON envelope (`{accountId}.json`) and DPAPI `CurrentUser` encryption store MSAL serialized token cache with account-bound entropy (`BitigMail.IMAP.Credential.v1\0{accountId}`).
- **Per-Account Serialization & Auth Generation**: `ImapCredentialResolver` serializes resolution per account ID via `SemaphoreSlim`. `ImapAccountRecord.AuthGeneration` tracks authorization lineage independently from account metadata `Version`. Same-identity reconnect updates credentials and increments `AuthGeneration` without bumping `Version`, ensuring frozen transfer resume remains viable. Cache save failure cannot publish success and throws fail-closed.
- **Short-Lived Protected Operations (`/api/oauth/microsoft/*`)**: 128-bit random IDs, 5-minute maximum lifetime, bounded capacity (32 operations including retained terminal operations) checked atomically under lock with disposal guards. Cancellation/expiry and persistence are under the same `op.StateLock` commit boundary, ensuring verifiers ignoring cancellation tokens cannot persist hidden accounts. Closed stable error mapping prevents leaking internal or provider exceptions.
- **Fake Isolation**: Fake OAuth providers and testers are completely isolated in `BitigMail.TestingHost` (`FakeMicrosoftAuthProvider`, `FakeImapOAuthConnectionTester`). Normal production `BitigMail.LocalHost` contains zero fake implementations and cannot enable fakes via query, body, or environment variables. Fakes never transmit bearer tokens to real Microsoft.

---

## 2. File Change Inventory

### Engine Core (`engine/BitigMail.Engine/`)
- `Imap/ImapAccountDtos.cs`: Extended DTOs (`AuthKind`, `TenantId`, `ClientId`, `HomeAccountId`).
- `Imap/ImapAccountPublicDto.cs`: Safe public DTO exposing `AuthKind`, `TenantId`, `ClientId` (zero secrets, ciphers, or cache).
- `Imap/OAuth/MicrosoftOAuthDtos.cs`: Start, status, cancel request and response DTOs for `/api/oauth/microsoft/*`.

### LocalHost Engine (`engine/BitigMail.LocalHost/`)
- `BitigMail.LocalHost.csproj`: Added approved direct dependency `Microsoft.Identity.Client` 4.89.0 with `InternalsVisibleTo` for tests.
- `Imap/ImapAccountRecord.cs`: Extended record with `AuthKind`, `TenantId`, `ClientId`, `HomeAccountId`, and `AuthGeneration`.
- `Imap/ImapAccountStore.cs`: Compatible envelope migration, OAuth update restrictions (displayName only), OAuth account creation, reconnect updating `AuthGeneration` without bumping `Version`, and `SaveRefreshedOAuthCache` verifying `AuthGeneration`.
- `Security/IImapCredentialResolver.cs`: Typed credential abstractions with explicitly redacted `ToString()` and stable `ReauthorizationRequiredException`.
- `Security/ImapCredentialResolver.cs`: Per-account semaphore locks, silent token refresh, account-bound DPAPI protection, and fail-closed buffered cache commits.
- `Security/IMicrosoftAuthProvider.cs` & `MsalMicrosoftAuthProvider.cs`: Fixed MSAL 4.89 cache callbacks, strict identity pinning, and authorization URL capture.
- `Security/IImapOAuthConnectionTester.cs`: Production `RealImapOAuthConnectionTester` executing IMAP SSL connection and OAuth2 authentication.
- `OAuth/MicrosoftOAuthOperationManager.cs`: 128-bit random ID, 5-minute TTL, bounded-32 operation capacity with atomic lock, `WaitForCompletionAsync`, cancellation/persistence commit boundary lock, and closed safe error mapping.
- `Imap/ImapClientService.cs`: Extended with `TestAccountConnectionAsync` and `ListAccountFoldersAsync` routing typed credentials through `IImapCredentialResolver`.
- `Imap/Transfer/MailKitTransferClient.cs`: Authenticates using `SaslMechanismOAuth2` for OAuth accounts.
- `Imap/Transfer/ImapTransferPreviewService.cs`: Uses `IImapCredentialResolver` for preview connection and plan validation.
- `Imap/Transfer/ImapTransferWorker.cs`: Uses `IImapCredentialResolver`, handles `reauthorization_required` cleanly preserving journals.
- `LocalEngineApiEndpoints.OAuth.cs`: Mapped `/api/oauth/microsoft/start`, `GET .../operations/{id}`, `POST .../operations/{id}/cancel`.
- `LocalEngineApiEndpoints.cs`: Wired account test and folder endpoints through `IImapCredentialResolver` and registered OAuth endpoints.
- `Program.cs`: Registered MSAL provider, real connection tester, credential resolver, and operation manager in DI.

### TestingHost & Test Suite (`engine/BitigMail.TestingHost/` & `engine/BitigMail.Engine.Tests/`)
- `BitigMail.TestingHost/Security/FakeOAuthServices.cs`: Deterministic `FakeMicrosoftAuthProvider` and `FakeImapOAuthConnectionTester` isolated in testing host.
- `BitigMail.TestingHost/Program.cs`: Registered fake OAuth services in DI.
- `engine/BitigMail.Engine.Tests/MicrosoftOAuthTests.cs`: Comprehensive test suite covering API states, credential `ToString` redaction, legacy password path, OAuth mechanism/resolver, capacity limits, and frozen transfer resume.
- `Microsoft365PolicyTests.cs` and `Microsoft365LifecycleCriticalTests.cs`: Preserved strictly untouched (root-owned).

### Legal / Notices
- `THIRD-PARTY-NOTICES.md`: Full notice and MIT License for `Microsoft.Identity.Client` 4.89.0.

---

## 3. Routes & Safe DTO Contracts (Dummy IDs)

### 3.1 Start Operation: `POST /api/oauth/microsoft/start`
- **Request (New Account)**:
```json
{
  "companyId": "comp_acme",
  "projectId": "proj_migration",
  "displayName": "Acme M365 Pilot",
  "email": "user@contoso.example",
  "clientId": "11111111-2222-3333-4444-555555555555",
  "tenantId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
}
```
- **Request (Reconnect Existing Account)**:
```json
{
  "companyId": "comp_acme",
  "projectId": "proj_migration",
  "displayName": "Acme M365 Pilot",
  "email": "user@contoso.example",
  "clientId": "11111111-2222-3333-4444-555555555555",
  "tenantId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "accountId": "acc_0191eb9a7c2a71b091730428ad5e0001",
  "expectedVersion": 1
}
```
- **Response (HTTP 200 OK)**:
```json
{
  "operationId": "op_0191eb9a7c2a71b091730428ad5e9999",
  "status": "preparing",
  "createdAtUtc": "2026-09-13T12:00:00Z",
  "expiresAtUtc": "2026-09-13T12:05:00Z"
}
```

### 3.2 Get Operation Status: `GET /api/oauth/microsoft/operations/{id}?companyId=comp_acme&projectId=proj_migration`
- **Response (Awaiting Sign-in)**:
```json
{
  "operationId": "op_0191eb9a7c2a71b091730428ad5e9999",
  "status": "awaiting-signin",
  "authorizationUrl": "https://login.microsoftonline.com/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/oauth2/v2.0/authorize?client_id=11111111-2222-3333-4444-555555555555&response_type=code&code_challenge_method=S256&code_challenge=exampleChallenge43CharsLongAndBase64UrlSafe_&state=opaque-state&redirect_uri=http%3A%2F%2Flocalhost%3A54321%2F&scope=https%3A%2F%2Foutlook.office.com%2FIMAP.AccessAsUser.All%20openid%20profile%20offline_access",
  "accountId": null,
  "message": null,
  "createdAtUtc": "2026-09-13T12:00:00Z",
  "expiresAtUtc": "2026-09-13T12:05:00Z"
}
```
- **Response (Terminal Connected)**:
```json
{
  "operationId": "op_0191eb9a7c2a71b091730428ad5e9999",
  "status": "connected",
  "authorizationUrl": null,
  "accountId": "acc_0191eb9a7c2a71b091730428ad5e0001",
  "message": null,
  "createdAtUtc": "2026-09-13T12:00:00Z",
  "expiresAtUtc": "2026-09-13T12:05:00Z"
}
```
- **Response (Terminal Cancelled / Failed)**:
```json
{
  "operationId": "op_0191eb9a7c2a71b091730428ad5e9999",
  "status": "cancelled",
  "authorizationUrl": null,
  "accountId": null,
  "message": "İşlem kullanıcı tarafından iptal edildi.",
  "createdAtUtc": "2026-09-13T12:00:00Z",
  "expiresAtUtc": "2026-09-13T12:05:00Z"
}
```

### 3.3 Cancel Operation: `POST /api/oauth/microsoft/operations/{id}/cancel`
- **Query Params or Body**: `companyId=comp_acme&projectId=proj_migration`
- **Response (HTTP 200 OK)**:
```json
{
  "operationId": "op_0191eb9a7c2a71b091730428ad5e9999",
  "status": "cancelled",
  "message": "İşlem başarıyla iptal edildi."
}
```

---

## 4. Verification Checkpoint

- Root security policy tests `Microsoft365PolicyTests.cs`: 25/25 PASS (strictly untouched).
- Root lifecycle tests `Microsoft365LifecycleCriticalTests.cs`: Preserved untouched.
- Additional test suite `MicrosoftOAuthTests.cs` covering API states, legacy password paths, credential `ToString` secret redaction, OAuth mechanism selection, bounded capacity, and frozen transfer resume viability.
- Compilation and deterministic test execution ready for SOL verification.
