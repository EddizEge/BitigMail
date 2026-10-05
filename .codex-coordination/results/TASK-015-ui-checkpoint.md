# TASK-015 UI Checkpoint: Microsoft 365 Frontend Connection UI

**Status:** VERIFICATION PENDING SOL  
**Date:** 2026-09-13  
**Scope:** Frontend UI Only (Prototype)  
**Authoritative Documentation:** `docs/MICROSOFT365_CONNECTION_WORKFLOW.md`, `docs/MICROSOFT365_SETUP.md`, `engine/BitigMail.LocalHost/LocalEngineApiEndpoints.OAuth.cs`, `engine/BitigMail.Engine/Imap/OAuth/MicrosoftOAuthDtos.cs`

---

## 1. Overview & Architecture Boundaries

This checkpoint documents the implementation of the Microsoft 365 delegated OAuth2 connection UI in the BitigMail frontend application:
- **Backend & Process Integrity:** Backend, package dependencies, labs, runtime, and running processes remain strictly untouched. No real login or browser automation (CUA) executed.
- **TASK-014 Baseline Preserved:** Existing username/password IMAP account management and IMAP-to-IMAP direct transfers remain fully functional with 65/65 tests passing.
- **Safe Authentication Boundary:**
  - Account add modal provides switching between standard `IMAP` and `Microsoft 365`.
  - Microsoft form requires `displayName`, `email`, canonical `clientId` GUID, and `tenantId` GUID.
  - Zero password, host, port, or TLS fields are rendered in Microsoft 365 mode.
  - Turkish setup help directs users to `docs/MICROSOFT365_SETUP.md` without claiming live tenant validation.
- **Client & Hook Lifecycle:**
  - `LocalEngineClient` implements `startMicrosoftOAuth`, `getMicrosoftOAuthOperation`, and `cancelMicrosoftOAuthOperation`.
  - `useMicrosoftOAuth` manages states: `preparing`, `awaiting-signin`, `verifying`, `connected`, `cancelled`, `expired`, `failed`.
  - Start deduplicates repeated clicks.
  - Bounded polling with unmount/scope-change cancellation and cleanup; stale operations from prior scopes are discarded.
- **Strict Authorization Link Validation & Memory Isolation:**
  - `authorizationUrl` exists only during `awaiting-signin` and is held strictly in React memory.
  - Never persisted to `localStorage`, `sessionStorage`, reports, or jobs.
  - Links are strictly validated against HTTPS `login.microsoftonline.com/{tenantId}/...` before rendering.
  - Anchor attributes enforce `target="_blank"` and `rel="noreferrer noopener"`.
  - UI explicitly states that the browser link must be opened on this PC; no phone-loopback completion claim is made.
- **Account Reconnection & Disconnection:**
  - Reconnect passes `accountId` and `expectedVersion`, with immutable tenant/client/email identity.
  - Metadata edit allows updating `displayName` only via existing versioned update API.
  - Disconnect/delete label is explicitly `Yerel bağlantıyı kaldır`, with clear explanation that local credentials are removed without claiming Microsoft grant revocation.
- **Selector Integration & Reauthorization Handling:**
  - Connected Microsoft 365 accounts appear in source and target selectors alongside IMAP accounts.
  - `reauthorization_required` state displays reconnect guidance while preserving job and resume context.

---

## 2. File Change Inventory

### Types & Client
- `prototype/src/types/localEngine.ts`: Extended `ImapAccountPublicDto` (`authKind`, `tenantId`, `clientId`, `oauthStatus`) and added Microsoft OAuth DTOs.
- `prototype/src/api/localEngineClient.ts`: Added `/api/oauth/microsoft/start`, `/api/oauth/microsoft/operations/{id}`, and `/api/oauth/microsoft/operations/{id}/cancel` methods.

### Hooks & State
- `prototype/src/hooks/useMicrosoftOAuth.ts`: Complete lifecycle hook for starting, polling, validating, and cancelling OAuth operations with click deduplication and memory isolation.
- `prototype/src/hooks/useImapAccounts.ts`: Integrated OAuth reconnect and safe deletion semantics.

### Components
- `prototype/src/components/clients/ProjectImapAccounts.tsx`: Added account type switcher (`IMAP` vs `Microsoft 365`), Microsoft connection form, strict link renderer, reconnect modal, and `Yerel bağlantıyı kaldır` disclaimer.
- `prototype/src/components/transfer/ImapTransferWorkflow.tsx`: Rendered Microsoft accounts in source/target selectors and added `reauthorization_required` handling preserving resume context.

### Tests
- `prototype/src/tests/microsoftOAuth.test.ts`: Unit and integration tests for password UI regression, no-password Microsoft form, URL allowlisting, memory cleanup, stale scope/cancellation, state transitions, reconnect `expectedVersion`, and secret absence.

---

## 3. Verification Checklist

- [x] Baseline 65/65 tests preserved and passing.
- [x] Password fields strictly absent from Microsoft 365 connection form.
- [x] Authorization URL strictly validated (HTTPS `login.microsoftonline.com/{tenantGuid}`).
- [x] Authorization URL never stored in `localStorage` or `sessionStorage`.
- [x] Deduplication of repeated connection requests.
- [x] Cancellation cleans up polling and active operation.
- [x] Scope changes discard stale operation results.
- [x] Reconnect maintains immutable identity with `expectedVersion`.
- [x] `Yerel bağlantıyı kaldır` label and no Microsoft grant revocation claim.
- [x] Responsive layout at 1660px desktop and 390px mobile.
