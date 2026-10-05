# TASK-016 Gemini Checkpoint: Personal Outlook.com / Hotmail OAuth Mode

**Status:** LOCAL_READY / LIVE_PERSONAL_PILOT_PENDING  
**Date:** 2026-09-13  
**Contract Reference:** `docs/OUTLOOK_PERSONAL_WORKFLOW.md`, `.codex-coordination/inbox/TASK-016.md`  
**Root Exclusive Boundaries Kept Untouched:**  
- `engine/BitigMail.LocalHost/Security/Microsoft365Policy.cs` (NEVER EDITED; root-owned policy with `GetExpectedTenantId`, `PersonalAuthority = "consumers"`, `PersonalTenantId = "9188040d-6c67-4c5b-b112-36a304b66dad"`).
- `engine/BitigMail.Engine.Tests/OutlookPersonalCriticalTests.cs` (NEVER EDITED; root-owned 18 critical tests passing untouched).

---

## 1. Summary of Changes

### Backend Implementation (Frozen & Verified)
- `engine/BitigMail.LocalHost/Security/MsalMicrosoftAuthProvider.cs`:
  - In `AcquireTokenSilentAsync`, updated tenant verification from literal string equality against `tenantId` to comparing `actualTenantId` against `Microsoft365Policy.GetExpectedTenantId(tenantId)`. This allows `tenantId: "consumers"` to validate against the actual MSA tenant GUID (`9188040d-6c67-4c5b-b112-36a304b66dad`) while organizational GUID tenants validate against themselves.
  - Interactive flow builds MSAL with authority `"consumers"` and relies on `Microsoft365Policy.ValidateIdentity` to enforce exact expected tenant GUID, username equality, and homeAccountId matching.
  - Missing identity claims are never substituted; `authKind` remains strictly `"microsoft365"`.
- `engine/BitigMail.TestingHost/Security/FakeOAuthServices.cs`:
  - Updated synthetic interactive auth result in `FakeMicrosoftAuthProvider` to return `TenantId = Microsoft365Policy.GetExpectedTenantId(tenantId)` so testing host flows with `"consumers"` correctly resolve to the expected MSA tenant.
- `engine/BitigMail.Engine.Tests/MicrosoftOAuthTests.cs`:
  - Added test cases covering:
    - `PersonalOutlook_StartOperation_PersistsConsumersTenant_AndAuthenticatesSuccessfully`: Verifies personal request with `tenantId: "consumers"` completes, persists `TenantId = "consumers"`, and keeps `AuthKind = "microsoft365"`.
    - `PersonalOutlook_Reconnect_PreservesConsumersTenant`: Verifies reconnecting a personal account preserves `tenantId: "consumers"` with `expectedVersion`.
    - `PersonalOutlook_RejectsBroadOrInvalidAuthorities`: Verifies `"common"`, `"organizations"`, uppercase `"Consumers"`, trailing slashes, spaces, and empty strings are rejected with `ArgumentException`.
    - `PersonalOutlook_SilentResolution_ValidatesExpectedTenantId`: Verifies credential resolution for personal accounts succeeds and enforces root policy tenant mapping.

### Frontend Implementation
- `prototype/src/api/localEngineClient.ts`:
  - In `validateMicrosoftAuthorizationUrl`:
    - Enforced `expectedTenantId` must be canonical GUID or literal `"consumers"`. Broad authorities (`common`, `organizations`, empty, invalid strings) are rejected immediately.
    - Enforced strict HTTPS scheme and exact host `login.microsoftonline.com`.
    - Enforced port 443 only (rejects non-standard ports).
    - Enforced absence of userinfo (`username`/`password`) and fragment (`hash`).
    - Enforced exact path match `/${cleanedExpected}/oauth2/v2.0/authorize`.
- `prototype/src/hooks/useMicrosoftOAuth.ts`:
  - In `resetSession(cancelBackend = false)`:
    - Supported optional backend cancellation via `cleanupOperation(cancelBackend)`.
    - Incremented `generationRef.current += 1` to guarantee any delayed or in-flight poll or start responses from a previous mode or scope are completely discarded.
    - Cleared all session state (authorization URLs, error, accountId, status) back to `'idle'`.
- `prototype/src/styles/index.css`:
  - Added responsive stacking rules for `.m365-mode-selector-row` and `.m365-mode-choice-btn`:
    - Desktop: flex row (`flex: 1 1 200px`) side-by-side with wrapping fallback.
    - Narrow viewports (<= 480px, including 390px mobile screens): stacks into full-width buttons (`width: 100%`) with `white-space: normal`, preventing label clipping of `"Kişisel Outlook.com / Hotmail"`.
- `prototype/src/components/clients/ProjectImapAccounts.tsx`:
  - Mode switcher in Create Account Modal between `Kurumsal Microsoft 365` (`data-testid="m365-choice-corporate"`) and `Kişisel Outlook.com / Hotmail` (`data-testid="m365-choice-personal"`).
  - Mode toggle elements use responsive CSS classes `.m365-mode-selector-row` and `.m365-mode-choice-btn` with flex wrapping and `whiteSpace: 'normal'`.
  - Mode switching triggers `m365OAuth.cancelOperation()`, `m365OAuth.resetSession(true)`, and clears errors, preventing stale URLs/responses from leaking across modes.
  - Personal Form:
    - Contains Display Name, Mailbox Email, and Application Client ID only.
    - Has STRICTLY NO Directory/Tenant ID input in the DOM.
    - Has STRICTLY NO Password/Host/Port/TLS inputs in the DOM.
    - Automatically sends literal `tenantId: "consumers"` upon submission.
  - Personal Setup Guide (`data-testid="m365-personal-setup-guide"`):
    - Clean, natural Turkish copy with redundant English parentheticals removed.
    - Preserves official identifiers `Client ID` and `IMAP.AccessAsUser.All`.
    - States requirements: personal MSA account support, desktop loopback (`http://localhost`), delegated IMAP permission, no client secret (PKCE), local browser sign-in, optional Outlook web POP/IMAP setting, and pilot safety boundary (`BitigMail-Test` folder with synthetic emails; no deletion or access to existing personal emails).
  - Saved Accounts Table:
    - Personal accounts (`authKind === 'microsoft365' && tenantId === 'consumers'`) render badge `"Kişisel Outlook"`, note `(Kişisel / consumers)`, and TLS note `Outlook.com / XOAUTH2`.
    - Corporate accounts render badge `"Microsoft 365"`, tenant GUID, and `OAuth2 / SSL`.
  - Reconnect Modal:
    - Labeled clearly as `"Kişisel Outlook Hesabını Yeniden Yetkilendir"` and `"Hesap Türü: Kişisel Outlook.com / Hotmail"`.
    - Preserves `Authority (Tenant): consumers (Kişisel)` without modifying client ID or mailbox email.
- `prototype/src/components/transfer/ImapTransferWorkflow.tsx`:
  - In `source-account-select` and `target-account-select`, personal accounts are formatted with `[Kişisel Outlook [OAuth2]]` while corporate accounts are formatted with `[Microsoft 365 [OAuth2] (Kurumsal)]`.
  - Under-selector metadata notes distinguish `Kişisel Outlook OAuth2` from `Microsoft 365 Kurumsal OAuth2`.
- `prototype/src/tests/microsoftOAuth.test.tsx`:
  - Comprehensive `describe('9. TASK-016 Personal Outlook.com / Hotmail OAuth Mode')` covering:
    - Authorization URL validator approving strict `login.microsoftonline.com/consumers/oauth2/v2.0/authorize` on port 443 with no userinfo/fragment.
    - Denial of non-443 port, userinfo, fragment, non-authorize path, and broad authorities (`common`, `organizations`, mismatched GUID, empty).
    - Explicit corporate vs personal choice with responsive wrapping and class checks, plus verification of personal form (display name, email, client ID ONLY; zero tenant/password inputs).
    - Natural Turkish security and pilot safety assertions on personal setup guide.
    - Submitting real-shaped personal request sending `tenantId: 'consumers'`.
    - Mode-switch isolation ensuring in-flight corporate operations are cancelled on backend and late responses do not overwrite personal form.
    - Saved personal account badge, `(Kişisel / consumers)` label, duplicate-safe reconnect assertion, and reconnect with `tenantId: 'consumers'`.
    - Source and target transfer selector labels.

---

## 2. Verification Results

### Frontend (Vitest & Vite in `prototype/`)
- **Typecheck (`npm run typecheck`):**  
  `tsc --noEmit` -> Exit code 0 (0 errors).
- **Lint (`npm run lint`):**  
  `eslint src` -> Exit code 0 (0 warnings, 0 errors).
- **Test Suite (`npm run test`):**  
  `Test Files: 7 passed (7), Tests: 90 passed (90), Duration: 1.94s`  
  (Preserves all prior 83 frontend assertions + 7 new TASK-016 personal assertions).
- **Build (`npm run build`):**  
  `tsc -b && vite build` -> Exit code 0.

### Backend (Frozen & Preserved Baseline)
- **Test Suite (`BitigMail.Engine.Tests.csproj`):**  
  `Başarılı! - Başarısız: 0, Başarılı: 305, Atlanan: 0, Toplam: 305, Süre: 10 s`  
  (305/305 passing: 278 baseline + 18 root OutlookPersonalCriticalTests + 9 new MicrosoftOAuthTests).
- **Compilation (`BitigMail.LocalHost.csproj -c Debug`):**  
  `0 Uyarı, 0 Hata, Çıkış: 0`.

### Safety & Operational Invariants
- Zero modification to `Microsoft365Policy.cs` or `OutlookPersonalCriticalTests.cs`.
- Zero modification to normal service ports or background processes (6174, 5173).
- Zero new NuGet or npm packages (`Microsoft.Identity.Client` stays exact 4.89.0).
- Zero browser automation, client ID scraping, or real cloud mailbox mutations.
