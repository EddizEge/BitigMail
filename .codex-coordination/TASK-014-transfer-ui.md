# TASK-014 IMAP Transfer UI Specification & Frontend Plan

Status: IMPLEMENTED (verification pending SOL)

## 1. Overview & Scope
Implementation of the TASK-014 frontend IMAP transfer workflow and account UI boundary fixes:
1. **Account UI Boundary Fixes**:
   - `useImapAccounts`: Prevent stale async responses after companyId/projectId changes using generation counters and scope identity checking.
   - Immediate password clearance after draft connection testing (both success and error).
   - In edit mode, preserve stored lab account `tlsMode: "none"` without silently coercing/sending `ssl` on metadata-only edits (omits unchanged TLS fields).
   - In create mode, offer strictly `ssl` and `starttls` (never `none`).
2. **Transfer Workflow UI (`TransfersTabView.tsx` & subcomponents)**:
   - Preserve existing sample screen as an accessible "Örnek mod" toggle/tab.
   - Add real IMAP transfer workflow surface:
     - Scoped source & target account selectors (filtered by current company/project).
     - Multiple exact source folder selection with real counts, delimiters, and selectable flags.
     - Target folder mapping per selected source folder (defaulting to matching folder name).
     - Optional inclusive date range (Start / End) with clear UTC+03 / original MIME Date explanation.
     - Server-side preview generation via `POST /api/transfer/imap/preview`.
     - Frozen preview summary: displays total/eligible/excluded/missing-date/deleted counts, per-folder breakdown, and blockers.
     - Start transfer action sending strictly `{ previewId, idempotencyKey }` (no counts/UIDs/host sent by client).
3. **Progress, Status & Durable Reports (`JobCenter.tsx` & Transfer Progress)**:
   - Handle shared `LocalJobRecord` with `jobKind === "imap-transfer"`.
   - Distinct IMAP status handling: `Interrupted`, `NeedsAttention`, running, completed, failed.
   - Resume action for `Interrupted` / `NeedsAttention` jobs (`POST /api/transfer/imap/resume/{jobId}`).
   - Prevent PST verification fields (e.g., CRC32/MAPI properties) from falsely displaying as verified for IMAP.
   - Durable report view & download (`ConversionReport.imapTransfer`).
   - Customer/project switch does not mutate frozen report snapshot context.
4. **Client & Types**:
   - Update `types/localEngine.ts` with IMAP transfer DTOs (`ImapTransferPreviewRequest`, `ImapTransferPreviewResponse`, `ImapTransferStartRequest`, `ImapTransferReportDetail`, etc.).
   - Update `api/localEngineClient.ts` with `createImapTransferPreview`, `getImapTransferPreview`, `startImapTransfer`, and `resumeImapTransfer`.
   - New hook `useImapTransfer.ts` for managing transfer state, validation, preview generation, and execution.
5. **Vitest Test Suite**:
   - Account CRUD, expectedVersion/409 guidance, password clearing, stale scope protection, exact Turkish folders and unreadable counts.
   - Preview payload exactness (nonempty folders), start payload only previewId+idempotencyKey, date/blocker rendering helpers, jobKind-specific rendering, and resume action/state.
   - Ensure all existing 56 tests remain passing.

## 2. Files Modified & Added
- `prototype/src/types/localEngine.ts`: Added IMAP transfer request/response/report interfaces.
- `prototype/src/api/localEngineClient.ts`: Added transfer preview, start, resume client methods.
- `prototype/src/hooks/useImapAccounts.ts`: Fixed stale scope race conditions with generation ref & scope identity check.
- `prototype/src/components/clients/ProjectImapAccounts.tsx`: Fixed password clearing on test, safe edit for `none` TLS mode without silent overwrite, and strict create TLS options.
- `prototype/src/hooks/useImapTransfer.ts`: Created hook for IMAP transfer workflow state machine.
- `prototype/src/components/transfer/TransfersTabView.tsx`: Updated to support "Örnek mod" toggle and real IMAP transfer surface.
- `prototype/src/components/transfer/ImapTransferWorkflow.tsx`: Real IMAP transfer view (account pickers, folders, date filter, preview card, progress, audit report).
- `prototype/src/components/jobs/JobCenter.tsx`: Updated to render `imap-transfer` jobs, hide PST verification fields for IMAP, and support resume action.
- `prototype/src/tests/imapAccounts.test.ts`: Tests for account boundary fixes and scope protection.
- `prototype/src/tests/imapTransferWorkflow.test.ts`: Tests for preview payload exactness, start payload, resume, jobKind rendering, and Turkish folders.

Verification status: verification pending SOL
