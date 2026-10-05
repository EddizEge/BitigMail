# TASK-015 Architecture Discovery Gate

**TASK_ID:** TASK-015  
**STATUS:** DISCOVERY COMPLETE / IMPLEMENTATION BLOCKED ON ROOT CONTRACT  
**EXECUTOR:** GEMINI (Official Antigravity)  
**CONTROLLER:** SOL / ASTRA HIGH  
**SCOPE:** Architecture discovery, seam map, public API/DTO proposal, NuGet package evaluation. No product source or test code modified. No real tenant, cloud login, protocol logs, or secrets created.

---

## 1. Concise Current Seam Map

### A. Backend Account DTO, Record & Storage
- **`engine/BitigMail.Engine/Imap/ImapAccountDtos.cs`**
  - [`CreateImapAccountRequest`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.Engine/Imap/ImapAccountDtos.cs#L7): Inbound payload requiring `Host`, `Port`, `TlsMode`, `Username`, `Password`.
  - [`UpdateImapAccountRequest`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.Engine/Imap/ImapAccountDtos.cs#L25): Update payload with optional `Password` and required `ExpectedVersion`.
  - [`DeleteAccountRequest`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.Engine/Imap/ImapAccountDtos.cs#L43): Scoped deletion request with optional `ExpectedVersion`.
  - [`TestImapConnectionRequest`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.Engine/Imap/ImapAccountDtos.cs#L71): Connection test input (supports either saved `AccountId` or draft parameters).
  - [`TestConnectionResult`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.Engine/Imap/ImapAccountDtos.cs#L87): Public sanitized test result (`Success`, `Message`, `Error`, `LatencyMs`).
  - [`AccountVersionConflictException`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.Engine/Imap/ImapAccountDtos.cs#L54): Concurrency violation exception mapping to HTTP 409.
- **`engine/BitigMail.Engine/Imap/ImapAccountPublicDto.cs`**
  - [`ImapAccountPublicDto`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.Engine/Imap/ImapAccountPublicDto.cs#L9): Sanitized wire projection exposing `AccountId`, `CompanyId`, `ProjectId`, `DisplayName`, `Email`, `Host`, `Port`, `Username`, `TlsMode`, `SecurityMode`, `Version`, `CreatedAtUtc`, `UpdatedAtUtc`.
- **`engine/BitigMail.Engine/Imap/ImapSecurityMode.cs`**
  - [`ImapSecurityMode`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.Engine/Imap/ImapSecurityMode.cs#L8): Enum defining `SslOnConnect` (1), `StartTls` (2), `PlaintextLoopbackLabOnly` (3).
- **`engine/BitigMail.LocalHost/Imap/ImapAccountRecord.cs`**
  - [`ImapAccountRecord`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Imap/ImapAccountRecord.cs#L10): In-memory/persisted account entity with `Clone()` and `ToPublicDto()`.
- **`engine/BitigMail.LocalHost/Imap/ImapAccountStore.cs`**
  - [`ImapAccountEnvelope`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Imap/ImapAccountStore.cs#L18): Atomic single JSON envelope on disk (`{accountId}.json`) containing `Account` record and `ProtectedPasswordBase64`.
  - [`ImapAccountStore`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Imap/ImapAccountStore.cs#L35): Durable atomic store performing concurrency control via `Version`, atomic temp-file replace, and scoped isolation.

### B. Credential Protection & Transport Policy
- **`engine/BitigMail.LocalHost/Security/ImapCredentialProtector.cs`**
  - [`IImapCredentialProtector`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Security/ImapCredentialProtector.cs#L6): Abstraction for `Protect(string secret, string accountId)` and `Unprotect(byte[] cipher, string accountId)`.
  - [`WindowsImapCredentialProtector`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Security/ImapCredentialProtector.cs#L13): Direct Windows DPAPI `DataProtectionScope.CurrentUser` implementation with account-specific SHA-256 entropy.
- **`engine/BitigMail.LocalHost/Security/ImapConnectionPolicy.cs`**
  - [`ImapConnectionPolicy`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Security/ImapConnectionPolicy.cs#L7): Validates host, port, TLS mode, username (max 320 chars), and password (required, max 16384 chars).

### C. MailKit Account & Transfer Clients
- **`engine/BitigMail.LocalHost/Imap/ImapClientService.cs`**
  - [`ImapClientService`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Imap/ImapClientService.cs#L21): Real MailKit IMAP connection test (`TestConnectionAsync`) and folder discovery (`ListFoldersAsync`) with strict redacted error mapping.
- **`engine/BitigMail.LocalHost/Imap/Transfer/IImapTransferClient.cs`**
  - [`IImapTransferClient`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Imap/Transfer/IImapTransferClient.cs#L44): Transfer contract with `ConnectAndAuthenticateAsync(host, port, tlsMode, username, password, ct)`.
  - [`IImapTransferClientFactory`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Imap/Transfer/IImapTransferClient.cs#L78): Factory seam for creating transfer clients.
- **`engine/BitigMail.LocalHost/Imap/Transfer/MailKitTransferClient.cs`**
  - [`MailKitTransferClient`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Imap/Transfer/MailKitTransferClient.cs#L24): Real MailKit transfer client calling `client.AuthenticateAsync(username, password, ct)`.
  - [`MailKitTransferClientFactory`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Imap/Transfer/MailKitTransferClient.cs#L308): Instantiates `MailKitTransferClient`.
- **`engine/BitigMail.LocalHost/Imap/Transfer/ImapTransferPreviewService.cs`**
  - [`ImapTransferPreviewService`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Imap/Transfer/ImapTransferPreviewService.cs#L18): Captures immutable snapshot of source/target accounts and freezes `SourceAccountVersion` and `TargetAccountVersion` into `ImapTransferPlan`.
- **`engine/BitigMail.LocalHost/Imap/Transfer/ImapTransferWorker.cs`**
  - [`ImapTransferWorker`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/engine/BitigMail.LocalHost/Imap/Transfer/ImapTransferWorker.cs#L21): Verifies `sourceRecord.Version == plan.SourceAccountVersion` and `targetRecord.Version == plan.TargetAccountVersion` before opening connections, failing closed if changed.

### D. DI & API Endpoints
- **`engine/BitigMail.LocalHost/Program.cs`**
  - Binds Kestrel to `127.0.0.1:6174`. Registers `ImapAccountStore`, `ImapClientService`, `MailKitTransferClientFactory`, `ImapTransferJournal`, and `ImapTransferPreviewService`.
- **`engine/BitigMail.LocalHost/LocalEngineApiEndpoints.cs`**
  - Account REST routes: `GET /api/accounts`, `GET /api/accounts/{accountId}`, `POST /api/accounts`, `POST /api/accounts/{accountId}`, `DELETE /api/accounts/{accountId}`, `POST /api/accounts/test`, `POST /api/accounts/{accountId}/test`, `GET /api/accounts/{accountId}/folders`.
- **`engine/BitigMail.LocalHost/LocalEngineApiEndpoints.ImapTransfer.cs`**
  - Transfer REST routes: `POST /api/transfer/imap/preview`, `GET /api/transfer/imap/preview/{previewId}`, `POST /api/transfer/imap/start`, `GET /api/transfer/imap/{jobId}/journal`.
- **`engine/BitigMail.LocalHost/Jobs/JobManager.ImapTransfer.cs`**
  - Bridges background job execution with `ImapTransferWorker`.

### E. Frontend Types, Client, Hooks & Form
- **`prototype/src/types/localEngine.ts`**
  - Types: [`ImapAccountPublicDto`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/prototype/src/types/localEngine.ts#L303), [`CreateImapAccountRequest`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/prototype/src/types/localEngine.ts#L318), [`UpdateImapAccountRequest`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/prototype/src/types/localEngine.ts#L330), [`TestImapConnectionRequest`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/prototype/src/types/localEngine.ts#L343), [`TestConnectionResult`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/prototype/src/types/localEngine.ts#L354), [`ImapFolderDto`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/prototype/src/types/localEngine.ts#L361).
- **`prototype/src/api/localEngineClient.ts`**
  - Account API client methods: `listAccounts`, `getAccount`, `createAccount`, `updateAccount`, `deleteAccount`, `testConnection`, `testSavedAccount`, `listAccountFolders`.
- **`prototype/src/hooks/useImapAccounts.ts`**
  - [`useImapAccounts`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/prototype/src/hooks/useImapAccounts.ts#L32): React hook providing reactive account management, conflict detection (HTTP 409), connection test status, and folder browsing.
- **`prototype/src/components/clients/ProjectImapAccounts.tsx`**
  - [`ProjectImapAccounts`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/prototype/src/components/clients/ProjectImapAccounts.tsx#L20): Account list table, Create modal with password/TLS inputs, Edit modal, Folders modal, and Delete confirmation dialog.

---

## 2. Proposed Public API & DTO Changes for Microsoft 365 Delegated OAuth

*Note: Architectural proposal only; security and authorization semantics remain subject to root contract.*

### A. Provider & Authentication Mode Fields
- Add to `ImapAccountPublicDto`, `CreateImapAccountRequest`, and `UpdateImapAccountRequest`:
  - `provider`: `"GenericImap" | "Microsoft365"` (defaulting to `"GenericImap"`).
  - `authMode`: `"Password" | "OAuthDelegated"` (defaulting to `"Password"`).
- For `Microsoft365` accounts:
  - Fixed host: `outlook.office365.com`
  - Fixed port: `993`
  - Fixed TLS mode: `"ssl"` (SslOnConnect)
  - `username` / `email`: Bound to authenticated M365 UPN / email address.
  - `password`: Not required and not accepted when `authMode == "OAuthDelegated"`.

### B. Public Connection Status
- Add non-sensitive connection indicators to `ImapAccountPublicDto`:
  - `oauthStatus`: `"Unconnected" | "Connected" | "Expired" | "ConsentRequired"` (optional, present only for OAuth accounts).
  - `isConnected`: boolean.
  - `tokenExpiresAtUtc`: optional ISO-8601 string (indicates whether local token is fresh without exposing token contents).
  - `statusMessage`: optional safe user-facing message (e.g. `"Oturum geçerli"`, `"Yeniden yetkilendirme gerekli"`).

### C. Begin / Callback / Status / Disconnect Endpoint Shapes
1. **Begin Connection Flow:**
   - `POST /api/accounts/m365/connect/begin`
   - Request: `{ companyId: string, projectId: string, accountId?: string, displayName?: string }`
   - Response: `{ authUri: string, correlationId: string, state: string }`
2. **Callback / Complete Connection Flow:**
   - Handled via local loopback listener or local engine callback endpoint:
   - `POST /api/accounts/m365/connect/callback`
   - Request: `{ correlationId: string, code?: string, state?: string, error?: string }`
   - Response: `ImapAccountPublicDto` (updated account with `oauthStatus: "Connected"`).
3. **Status Flow:**
   - `GET /api/accounts/{accountId}/m365/status?companyId=...&projectId=...`
   - Response: `{ accountId: string, isConnected: boolean, oauthStatus: string, tokenExpiresAtUtc?: string, statusMessage?: string }`
4. **Disconnect Flow:**
   - `POST /api/accounts/{accountId}/m365/disconnect`
   - Request: `{ companyId: string, projectId: string, expectedVersion: number }`
   - Response: `ImapAccountPublicDto` (clears token cache, increments `Version`, updates `oauthStatus: "Unconnected"`).

### D. Account Version Behavior & Concurrency Control
- In `ImapAccountStore`, every OAuth token lifecycle change (initial connection, manual token disconnect, account settings update) **must increment `Version`**.
- This strictly preserves the existing safety invariant:
  - If a user disconnects or re-links an M365 account while a transfer preview or job is active, `ImapTransferWorker` detects `currentRecord.Version != plan.SourceAccountVersion` (or `TargetAccountVersion`) and **fails closed**, preventing transfers with mismatched credentials.

### E. Zero Token & Secret Exposure Guarantee
- Neither access tokens, refresh tokens, auth codes, PKCE verifiers, client secrets, nor MSAL cache blobs will ever appear in:
  - Wire DTOs (`ImapAccountPublicDto`, `TestConnectionResult`, preview/transfer responses).
  - API HTTP responses or error bodies.
  - Browser query parameters or frontend application state.
  - Engine diagnostic logs, exception messages, or report files.

### F. Compatibility with Username/Password Accounts & Frozen Jobs
- **Zero Breaking Changes:**
  - Existing accounts stored on disk retain `authMode: "Password"`, deserializing cleanly into `ImapAccountRecord`.
  - Existing password connection tests (`POST /api/accounts/test`) and transfer workers remain completely intact.
  - When executing transfer against an M365 account, `MailKitTransferClient` authenticates via MailKit's public SASL mechanism:
    `client.AuthenticateAsync(new SaslMechanismOAuth2(account.Username, accessToken), ct)`
    while password accounts continue using:
    `client.AuthenticateAsync(account.Username, password, ct)`.

---

## 3. Decision Points: ROOT CONTRACT PENDING

The following architectural choices are explicitly deferred to the upcoming root security contract (`docs/MICROSOFT365_CONNECTION_WORKFLOW.md`):

1. **Local Redirect & Listener Architecture [ROOT CONTRACT PENDING]:**
   - *Option A: MSAL Built-in System Browser Loopback Listener (`http://localhost:<port>`).*
     MSAL opens the user's default browser and temporarily binds a local loopback port to intercept the OAuth redirect. Avoids custom HTTP endpoints but requires dynamic port registration compatibility in Azure Entra ID.
   - *Option B: LocalEngine Kestrel Callback Endpoint (`http://127.0.0.1:6174/api/auth/m365/callback`).*
     Uses the existing Kestrel server. Requires explicit redirect URI in Azure Entra ID and requires reconciling `LocalSecurityMiddleware` (which currently rejects cross-origin browser redirects lacking `X-Bitig-Session`).
   - *Option C: Device Code Flow or Embedded WebView2.*
     Device code flow requires zero local listener ports (user copies a code to `microsoft.com/devicelogin`), ideal for locked-down local host environments, while WebView2 requires Windows runtime dependencies.

2. **Token Cache & Storage Persistence Architecture [ROOT CONTRACT PENDING]:**
   - *Option A: Single-Envelope Integration.*
     Serialize MSAL cache partition into DPAPI-encrypted ciphertext within the existing atomic `{accountId}.json` envelope (`protectedTokenCacheBase64`), utilizing `WindowsImapCredentialProtector`.
   - *Option B: Separate DPAPI Token Cache File.*
     Keep account metadata strictly separate from `{accountId}.msalcache.bin` protected with DPAPI `CurrentUser`.
   - *Option C: In-Memory Only (Ephemerally Authenticated).*
     Tokens are held only in process memory for the duration of the LocalEngine process; restart requires re-connection.

3. **Entra ID App Registration & Client Identity [ROOT CONTRACT PENDING]:**
   - Decision whether BitigMail uses a pre-registered Public Client Application (ClientId) owned by root, or requires customer-supplied Azure Entra Tenant ID / Client ID in settings.

4. **Background Token Refresh Timing [ROOT CONTRACT PENDING]:**
   - Strategy for long-running multi-hour IMAP transfers: whether token refresh is evaluated per folder, per batch, or via proactive background token renewal before access token expiry (typically 60-90 minutes).

---

## 4. Test Seams & Compatibility Gates

To preserve the **229 backend** and **65 frontend** passing tests without regression:

1. **Backend Seam Isolation (`BitigMail.Engine.Tests`):**
   - Introduce an abstraction seam for OAuth token acquisition (e.g. `IMicrosoft365TokenProvider` or `IPublicClientApplication` wrapper).
   - In unit/integration tests, supply a deterministic mock/fake token provider returning test OAuth tokens.
   - All 229 existing tests (including `ImapAccountCriticalTests`, `ImapAccountServiceTests`, `ImapSecurityTests`, `ImapTransferTests`, and `ImapTransferLifecycleCriticalTests`) will continue testing password-based IMAP and Dovecot lab fixtures with 0 modifications.
2. **Offline & Zero-Cloud Testing Gate:**
   - Adhere strictly to the constraint: **No real tenant, cloud login, or remote Microsoft endpoint calls during test execution**.
   - Offline tests will verify:
     - Request validation and DTO transformations.
     - DPAPI protection of token cache payloads.
     - Account version increments upon OAuth connect/disconnect.
     - Fail-closed behavior on version conflicts during transfer execution.
     - Proper instantiation of `SaslMechanismOAuth2` when `authMode == "OAuthDelegated"`.
3. **Frontend Seam Isolation (`prototype/src/tests`):**
   - Mock OAuth connect/disconnect endpoints in `localEngineClient.ts` test suites.
   - Ensure `imapAccounts.test.ts` (14 tests) and `imapTransferWorkflow.test.ts` (11 tests) run unmodified and pass with existing 65/65 test count.

---

## 5. Official NuGet Metadata Evaluation: `Microsoft.Identity.Client`

Official NuGet catalog and registration index were queried directly via the NuGet v3 API (`https://api.nuget.org/v3/registration5-semver1/microsoft.identity.client/index.json` and catalog entry `2026.09.10.20.27.18/microsoft.identity.client.4.89.0.json`).

### A. Proposed Exact Version
- **Package:** `Microsoft.Identity.Client` (MSAL.NET)
- **Proposed Version:** `4.89.0`
- **Release Status:** Official Latest Stable (`isPrerelease: false`)
- **Published Date:** 2026-09-10T20:23:43.867Z
- **Package License:** `MIT` (`licenseExpression`: `"MIT"`, `requireLicenseAcceptance`: true)
- **Repository:** `https://github.com/AzureAD/microsoft-authentication-library-for-dotnet` (Commit: `f56a637ccd0dd7a6a4b4ec24240d541bdc53f19a`)

### B. Target Frameworks & Binary Layout
In `Microsoft.Identity.Client 4.89.0`, the package directly includes native compiled binaries for:
- `lib/net8.0/Microsoft.Identity.Client.dll` (Length: 1,258,312 bytes)
- `lib/net8.0-android34.0/`
- `lib/net8.0-ios18.0/`
- `lib/net462/`
- `lib/net472/`
- `lib/netstandard2.0/`

**Project Fit:** Both `BitigMail.Engine` (`net8.0`) and `BitigMail.LocalHost` (`net8.0-windows`) directly resolve the first-class `net8.0` asset group with zero compatibility shims.

### C. Direct & Transitive Dependencies for `net8.0`
For the `net8.0` target framework, `Microsoft.Identity.Client 4.89.0` has an exceptionally lean dependency list:
1. `Microsoft.IdentityModel.Abstractions` (`>= 8.14.0`)
2. `System.Diagnostics.DiagnosticSource` (`>= 6.0.1`)

**Dependency Conflict Analysis:**
- Existing project packages:
  - `MailKit 4.17.0`
  - `MimeKit 4.17.0`
  - `Aspose.Email 24.8.0`
  - `System.Formats.Asn1 8.0.1`
- Neither `Microsoft.IdentityModel.Abstractions` nor `System.Diagnostics.DiagnosticSource` conflicts with existing pinned dependencies. No breaking transitive dependency tree is introduced.

### D. Native, Browser & Runtime Implications
1. **Interactive Browser on .NET 8 Windows:**
   - MSAL.NET on Windows desktop supports system browser loopback redirect (`.WithRedirectUri("http://localhost")`).
   - If embedded browser is chosen in the future, it would require `Microsoft.Identity.Client.Desktop` and WebView2; system browser loopback avoids adding any native GUI dependencies to the host.
2. **Token Cache:**
   - MSAL core package provides an in-memory token cache with hook callbacks (`ITokenCache.SetBeforeAccessAsync`, `ITokenCache.SetAfterAccessAsync`).
   - Persistent cross-process caching can be backed by our existing DPAPI `WindowsImapCredentialProtector` without requiring external unvetted cache libraries.

### E. Distinguishing Stable from Prerelease & Uncertainties
- **Certainty:** `4.89.0` is unequivocally marked as a full stable release (not `-preview`, `-alpha`, or `-beta`).
- **Uncertainty / Consideration:**
  - `4.89.0` was published on September 10, 2026 (very recent).
  - For enterprise environments that require long-term production baking, `4.67.2` (71.9M downloads, published 2024) is the proven LTS baseline. However, `4.89.0` is the latest stable release containing up-to-date Entra ID security protocols, and it directly targets .NET 8.
  - **The package has NOT been added to any csproj and will NOT be restored until the root contract arrives.**

---

## 6. Changed Files & Status

- **Changed Files:**
  - `.codex-coordination/results/TASK-015-discovery.md` (new discovery document only)
- **Product Source Code Changes:** None
- **Package Reference Changes:** None (evaluation only)
- **Status:** `DISCOVERY COMPLETE / IMPLEMENTATION BLOCKED ON ROOT CONTRACT`
