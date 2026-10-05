# TASK-018 Backend Production Implementation & Targeted Corrections Checkpoint

**TASK_ID:** TASK-018  
**MILESTONE:** Local Archive Search & Managed Ingestion (Backend Production Slice)  
**STATUS:** CONDITIONALLY READY — BACKEND ACTUAL BEHAVIOR ROOT ACCEPTED (UI PENDING, NOT DONE)  
**TIMESTAMP:** 2026-09-14T03:25:00+03:00  
**AUTHOR:** GEMINI (Official Antigravity)  
**CONTROLLER:** SOL / ASTRA HIGH  

---

## 1. Executive Summary & Root Governance Compliance

All five targeted backend corrections required by `.codex-coordination/results/TASK-018-root-api-findings.md` have been fully implemented, regression-tested, and verified:

1. **Root Actual Facts**:
   - `evidence/TASK-018/api-f420241575`: 646/646 checks PASS.
   - `evidence/TASK-018/api-18a523d2d7`: 83/83 process restart and recovery checks PASS.
   - Product status: **UI PENDING, NOT DONE**. Frontend implementation (`ArchiveSearchView.tsx`, source picker wiring, preview modal) remains pending before final milestone signoff.
2. **Targeted Regressions Added**: Concise ordinary regression tests added in `engine/BitigMail.Engine.Tests/ArchiveCorrectionsTask018Tests.cs` (5 tests covering all 5 defects).
3. **Strict Root Ownership**: Zero edits to root policy or tests (`ArchiveSearchPolicy.cs`, `ArchiveSearchPolicyCriticalTests.cs`), root harness, evidence, or raw fixtures.
4. **Zero Process Leaks**: TestingHost build clean, no persistent service started or background workers left hanging.

---

## 2. Five Targeted Corrections Delivered

### 1. Public Stable Globally Unique MessageId Wire Format (`archiveId:itemId`)
- **Wire Format**: Public identity is strictly formatted as `{archiveId}:{itemId}` (e.g. `arc_cbab019c:msg_00000010`). Manifest internal item ID remains local (`msg_00000010`).
- **Exact Binding**: Preview binds the exact archive owner and full current search/filter request; no first-match fallback across archives. Cross-archive attempts without active scope return HTTP 404.
- **Reindex Preservation**: SQLite rebuild from disk manifests preserves the exact public `archiveId:itemId` format and item mappings.

#### Wire Example: Search Result Item
```json
{
  "messageId": "arc_cbab019c:msg_00000001",
  "archiveId": "arc_cbab019c",
  "companyId": "comp-alpha",
  "projectId": "proj-alpha",
  "archiveName": "Archive Alpha",
  "originalFolder": "INBOX",
  "sender": "sender@example.com",
  "recipients": "recipient@example.com",
  "subject": "Financial Report Alpha",
  "snippet": "Detailed quarterly report for Alpha.",
  "originalMimeDateUtc": "2026-09-14T01:00:00.0000000+00:00",
  "sourceInternalDateUtc": "2026-09-14T03:15:00.0000000+00:00",
  "formattedDate": "14.09.2026 04:00",
  "hasAttachments": false,
  "attachmentNames": [],
  "sizeBytes": 256,
  "isBodyTruncated": false
}
```

#### Wire Example: Message Preview Request & Response
```json
// POST /api/archive/message/preview Request:
{
  "messageId": "arc_cbab019c:msg_00000001",
  "searchRequest": {
    "selectedScopes": [
      { "companyId": "comp-alpha", "projectId": "proj-alpha", "archiveId": "arc_cbab019c" }
    ],
    "query": "Financial Report"
  }
}

// Response (200 OK):
{
  "messageId": "arc_cbab019c:msg_00000001",
  "archiveId": "arc_cbab019c",
  "subject": "Financial Report Alpha",
  "from": "sender@example.com",
  "to": "recipient@example.com",
  "cc": null,
  "bcc": null,
  "dateUtc": "2026-09-14T01:00:00.0000000+00:00",
  "sourceInternalDateUtc": "2026-09-14T03:15:00.0000000+00:00",
  "messageIdHeader": null,
  "bodyText": "Detailed quarterly report for Alpha.",
  "isBodyTruncated": false,
  "attachments": [],
  "rawSizeBytes": 256,
  "sha256": "4b4f058091176b9bd603be02d5a3f12bb5909249e0b82f6f32a76f2f0b784a0d"
}
```

### 2. Closed Field Mapping & Controlled HTTP 400 Validation
- In `ArchiveSearchRequest`:
  - `public const string OmittedFieldDefault = "__DEFAULT_ALL__";`
  - `public string? Field { get; init; } = OmittedFieldDefault;`
  - `ResolveField()` validates:
    - Omitted field defaults to `ArchiveSearchField.All`.
    - Explicit `null` throws `ArgumentException("Arama alanı (field) açıkça null olamaz.")`.
    - Explicit unknown/invalid strings throw `ArgumentException($"Geçersiz arama alanı: '{Field}'.")`.
- `LocalEngineApiEndpointsArchive.HandleSearch` and `HandleMessagePreview` validate `req.ResolveField()` immediately before service execution, mapping `ArgumentException` to HTTP 400 Bad Request.

### 3. Managed Raw Integrity Verification (Length + SHA256) with Controlled HTTP 409
- After scope/filter validation but before reading/returning preview body in `ArchiveCatalogService.GetMessagePreview`:
  - File byte length is verified against `manifestItem.ByteLength`. Mismatch throws `ArchiveIntegrityException`.
  - Raw stream SHA-256 hash is computed and verified against `manifestItem.StoredSha256`. Mismatch throws `ArchiveIntegrityException`.
- In `LocalEngineApiEndpointsArchive.HandleMessagePreview`:
  - `ArchiveIntegrityException` is caught and mapped strictly to `Results.Conflict(new { error = ex.Message })` (HTTP 409 Conflict), returning no message body.
- Verified by deterministic raw mutation regression (appending bytes and in-place byte flipping), restoring original bytes in `finally`.

### 4. Completed Bridge MBOXRD Export Ingestion
- Supports both `eml-tree` and `mboxrd` completed bridge export formats (`job-3b6cab7b2f7c` pattern).
- For `mboxrd`, repeated `relativeOutputPath` (e.g. `INBOX.mbox`) is handled as a legitimate shared container path:
  - Parses each container file once using `MboxrdRecordReader.EnumerateRecords` with bounded 64 MiB guards.
  - Retains ordinal, folder, and duplicate identity.
  - Validates every record against export manifest item's `StoredSha256` (does NOT compare whole container hash to per-record hash).
  - Verifies completed bridge job and manifest identity, safe relative paths, record counts, and frozen source hash digests (`bridge-export:{jobId}:{completedAtUtc}:{hashesDigest}`).

### 5. Preservation of SourceInternalDateUtc
- Added `SourceInternalDateUtc` separately from MIME `Date`:
  - DTO: `ArchiveIngestPlannedItem`, `ArchiveSearchResultItem`, `ArchiveMessagePreviewResponse`.
  - Manifest: `ArchiveManifestItem.SourceInternalDateUtc`.
  - SQLite Index: `source_internal_date_utc TEXT` column in `message_metadata` table, populated during initial ingest and reindex.
  - Catalog and Preview: Preserved from bridge export sidecar (`BridgeExportManifestItem.InternalDateUtc`), returned in search and preview responses, and stable across full reindexes.

---

## 3. Test Verification Summary

### A. All Archive Subsystem Tests
```powershell
& ".\.tools\dotnet\dotnet.exe" test engine\BitigMail.Engine.Tests\BitigMail.Engine.Tests.csproj -c Release --filter "FullyQualifiedName~Archive" --blame-hang-timeout 60s
```
**Outcome:**
- **Total Tests:** 59
- **Passed:** 59
- **Failed:** 0
- **Skipped:** 0
- **Duration:** 1.0 s

### B. Full Repository Release Test Suite
```powershell
& ".\.tools\dotnet\dotnet.exe" test engine\BitigMail.Engine.Tests\BitigMail.Engine.Tests.csproj -c Release
```
**Outcome:**
- **Baseline Tests:** 423
- **New TASK-018 Regression Tests:** 5
- **Total Tests:** 428
- **Passed:** 428
- **Failed:** 0
- **Skipped:** 0
- **Duration:** 14 s

### C. Release Build Verification
- `BitigMail.Engine`: Release build clean (0 Warnings, 0 Errors).
- `BitigMail.LocalHost`: Release build clean (0 Warnings, 0 Errors).
- `BitigMail.TestingHost`: Release build clean (0 Warnings, 0 Errors).
- `BitigMail.Engine.Tests`: Release build clean (0 Warnings, 0 Errors).

---

## 4. Exact File Modification Log

| File | Change Category | Description |
|---|---|---|
| `engine/BitigMail.Engine/Archive/ArchiveModels.cs` | Backend Production | Added `ArchiveIntegrityException`, `SourceInternalDateUtc` to DTOs, and closed `Field` mapping with `ResolveField()`. |
| `engine/BitigMail.Engine/Archive/ArchiveManifest.cs` | Backend Production | Added `SourceInternalDateUtc` to `ArchiveManifestItem`. |
| `engine/BitigMail.Engine/Archive/ArchiveStorageManager.cs` | Backend Production | Added `sourceInternalDateUtc` parameter to `StoreMimeItemAsync`. |
| `engine/BitigMail.Engine/Archive/ArchiveSearchIndex.cs` | Backend Production | Added `source_internal_date_utc` column, unique public `archiveId:itemId` wire mapping, exact scope owner binding, and reindex preservation. |
| `engine/BitigMail.LocalHost/Archive/ArchiveCatalogService.cs` | Backend Production | Added bridge export mboxrd container handling, `SourceInternalDateUtc` mapping, and raw byte length + SHA256 verification before preview. |
| `engine/BitigMail.LocalHost/Archive/ArchiveIngestWorker.cs` | Backend Production | Added single-pass bounded mboxrd container reader, per-record hash validation, and `SourceInternalDateUtc` persistence. |
| `engine/BitigMail.LocalHost/LocalEngineApiEndpoints.Archive.cs` | Backend Production / Seam | Added `HandleSearch` and `HandleMessagePreview` static handlers with HTTP 400 for invalid fields and HTTP 409 for integrity mismatches. |
| `engine/BitigMail.Engine.Tests/ArchiveCorrectionsTask018Tests.cs` | Regression Tests | Added 5 comprehensive regression tests covering all 5 items. |
| `.codex-coordination/results/TASK-018-backend-checkpoint.md` | Checkpoint Documentation | Updated status, root actual facts, wire examples, and test counts. |

---

## 5. Next Steps & Handoff

The backend corrections for TASK-018 are complete and verified. Backend actual behavior is root accepted. Frontend implementation remains pending (UI pending, not DONE).
