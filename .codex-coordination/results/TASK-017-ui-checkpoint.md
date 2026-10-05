# TASK-017 UI Checkpoint: File ↔ Account Transfer Bridge Frontend

**Status:** FRONTEND IMPLEMENTATION & TEST VERIFICATION COMPLETE  
**Date:** 2026-09-14  
**Authoritative Contract:** `docs/FILE_ACCOUNT_BRIDGE_WORKFLOW.md`, `TASK-017.md`, and compiled engine DTOs/endpoints  
**Backend Gate Status:** `dotnet test -c Release`: **369 / 369 Passed, 0 Failed, 0 Skipped**  
**Frontend Gate Status:**  
- `npm run typecheck`: **0 Errors**  
- `npm run lint`: **0 Errors, 0 Warnings**  
- `npm test -- --run`: **106 / 106 Passed, 0 Failed** across 8 test suites (90 baseline + 16 bridge workflow tests)  
- `npm run build`: **Clean production bundle built** (`dist/` generated)  

---

## 1. Overview & UI Implementation Summary

TASK-017 frontend implementation introduces the user-facing bridge workflow for bidirectional transfers between local email files (EML files, EML tree, strict mboxrd archives) and registered IMAP accounts within BitigMail.

### User Flow & Capabilities:
1. **Direction Selector & State Ownership**:
   - Preserves existing `IMAP ↔ IMAP` migration via `ImapTransferWorkflow`.
   - Adds `Dosya → IMAP (İçe Aktarım)`: Local EML files, EML directory tree, or strict mboxrd archives imported into a selected registered IMAP account.
   - Adds `IMAP → Dosya (Dışa Aktarım)`: Registered IMAP account messages exported into structured EML directory trees or per-folder mboxrd files with a sidecar `manifest.json`.
   - Prop synchronization: `BridgeTransferWorkflow` accurately reacts to changes in `initialDirection` prop via `prevInitialDirectionRef` without resetting internal user button actions.
   - Job isolation: A previously completed or selected job from one direction (e.g. completed `bridge-import` job `job-4c868b2fc7ac`) is automatically filtered out when viewing or switching to the other direction (`imap-to-file`), preventing it from overriding the form or locking the view.
   - Top-level integration: `TransfersTabView` bidirectional state synchronization via `onDirectionChange`.
2. **Vendor-Free MIME Descriptor**:
   - Source inspection uses the dedicated `/api/transfer/bridge/source/describe` endpoint.
   - Completely avoids legacy PST/OST analysis endpoints (`/api/mime/analyze`, `/api/ost/analyze`).
   - Accurately renders folder hierarchy, item counts, and byte sizes using camelCase DTOs (`folderName`, `itemCount`, `totalSizeBytes`).
3. **Strict Account Identity & Badge Determination**:
   - Account provider labels are never inferred from email domains (e.g. `user@gmail.com` on generic IMAP is never labeled as Google).
   - Exact mapping rule:
     - `authKind === 'microsoft365'` and `tenantId === 'consumers'` -> `Bireysel Outlook`
     - `authKind === 'microsoft365'` and any other tenant -> `Kurumsal Microsoft 365`
     - Any other `authKind` -> `Standart IMAP`
4. **Immutable Previews & Stale State Isolation**:
   - Clean preflight validation enforcing required fields in actual validation order:
     - For File → IMAP: source file handle -> target account -> source folders -> date range.
     - For IMAP → File: source account -> target directory handle -> source folders -> date range.
   - Invalidation on any user modification: modifying source handle, destination, folders, format, or date immediately resets preview to `null` and cancels stale in-flight responses.
   - Preview card is strictly guarded by `isPreviewMatchingDirection`.
5. **Job Execution & JobCenter Recognition**:
   - Bridge jobs (`bridge-import`, `bridge-export`) are identified in JobCenter with informative titles: `Dosya → IMAP Aktarımı (Kaynak → Hedef)` and `IMAP → Dosya Aktarımı (Kaynak → Hedef)`.
   - Interrupted bridge jobs transition to `needs_attention` with resume action (`localEngineClient.resumeBridgeImport` / `localEngineClient.resumeBridgeExport`).
   - Irrelevant PST success fields are suppressed and replaced with bridge verification notes: `"Hedef sunucuda RFC822 SHA-256 ve BitigMail keyword ile doğrulanır (PST başarı alanları uygulanmaz)"`.
6. **Design Hygiene & Copy Polish**:
   - Clean product header without internal task tags (`(TASK-017)` removed from all visible text).
   - Replaced technical jargon like "deterministik" with clear Turkish ("güvenli, doğrulamalı ve kesintiden devam edebilir köprü").
   - Safety notices avoid technical protocol commands (DELETE/EXPUNGE/MOVE), stating simply that source emails are never deleted or modified.
   - Mboxrd notice kept simple without leaking raw escaping internals in primary warnings.
   - Internal technical identifiers (`msrc_...` and `bprev_...`) tucked into optional `<details>` disclosure tags.
   - Mock footer counter clearly marked as `(Örnek simülasyon) 248 ileti filtreye uyuyor` so it is not confused with real bridge job results.
   - Full responsive layout verified for 390px mobile viewports without horizontal clipping or overflow.

---

## 2. Modified & Verified Frontend Files

1. `prototype/src/api/localEngineClient.ts`:
   - Bridge endpoints: `describeMimeSource`, `createBridgeImportPreview`, `startBridgeImport`, `resumeBridgeImport`, `createBridgeExportPreview`, `startBridgeExport`, `resumeBridgeExport`.
   - Cleaned up unused types.
2. `prototype/src/types/localEngine.ts`:
   - DTOs aligned with compiled backend models (camelCase casing: `folderName`, `itemCount`, `totalSizeBytes`, `sourceHandle`, `FilePickResult` fields).
3. `prototype/src/hooks/useBridgeTransfer.ts`:
   - State management, folder discovery, immutable preview generation, stale response discarding, and transfer lifecycle.
   - Synchronizes `direction` when `initialDirection` prop changes via `prevInitialDirectionRef`.
   - `setDirection` invalidates `activeJob`, `preview`, `report`, and increments sequence refs.
4. `prototype/src/components/transfer/BridgeTransferWorkflow.tsx`:
   - UI workflow component rendering direction switchers, file pickers, folder mapping tables, immutable preview breakdowns, active job monitor, and resume controls.
   - Filtered `currentJob` by active direction to prevent opposite-direction jobs from blocking the interface.
   - Added `onDirectionChange` callback and polished copy.
5. `prototype/src/components/transfer/TransfersTabView.tsx`:
   - Segmented direction switcher wired between `BridgeTransferWorkflow` and `ImapTransferWorkflow`.
   - Bound `onDirectionChange` to update parent `realDirection`.
6. `prototype/src/components/jobs/JobCenter.tsx`:
   - Recognition of `bridge-import` and `bridge-export` job kinds.
   - Proper status mappings, resume bridge button execution, and suppression of PST success metrics.
7. `prototype/src/App.tsx` & `prototype/src/components/layout/StatusBar.tsx`:
   - Labeled sample data status text as `(Örnek simülasyon)` to prevent confusion after real jobs.
8. `prototype/src/tests/bridgeTransferWorkflow.test.tsx`:
   - 16 comprehensive unit & integration tests covering descriptor endpoints, validation sequencing, wire payloads, idempotency, stale preview isolation, direction prop change with completed import job, component rendering, mobile viewport (390px), and JobCenter states.

---

## 3. Test Verification Records

### A. Frontend Test Suite (Vitest)
```
> bitigmail-prototype@0.1.0 test
> vitest run --run

 RUN  v5.0.0 C:/Users/Eddiz/Documents/ChatGPT/Mail Manager/prototype

 Test Files  8 passed (8)
      Tests  106 passed (106)
   Start at  01:24:06
   Duration  1.89s
```
All 106 tests passed across all 8 suites (90 baseline tests preserved, 16 bridge workflow tests).

### B. Frontend Typecheck & Lint
```
> bitigmail-prototype@0.1.0 typecheck
> tsc --noEmit
Exit Code: 0 (0 errors)

> bitigmail-prototype@0.1.0 lint
> eslint src
Exit Code: 0 (0 errors, 0 warnings)
```

### C. Frontend Production Build
```
> bitigmail-prototype@0.1.0 build
> tsc -b && vite build

vite v8.3.0 building client environment for production...
transforming...
✓ 64 modules transformed.
rendering chunks...
computing gzip size...
dist/index.html                   0.61 kB │ gzip:   0.39 kB
dist/assets/index-BgERPgWb.css   24.41 kB │ gzip:   5.45 kB
dist/assets/index-_ReNtO6j.js   580.84 kB │ gzip: 134.28 kB

✓ built in 154ms
Exit Code: 0
```

### D. Backend Test Suite (.NET 8.0 Release)
```
.\.tools\dotnet\dotnet.exe test -c Release engine\BitigMail.Engine.Tests\BitigMail.Engine.Tests.csproj

Başarılı!  - Başarısız:     0, Başarılı:   369, Atlanan:     0, Toplam:   369, Süre: 13 s - BitigMail.Engine.Tests.dll (net8.0)
Exit Code: 0
```
369 / 369 backend unit tests passed with 0 failures, 0 skipped, and 0 warnings.
