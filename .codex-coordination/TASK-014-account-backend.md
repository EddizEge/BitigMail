# TASK-014 IMAP Account Backend Specification & Verification

## 1. Overview & Verified Baseline
- **Baseline State**: 12/12 account tests passing (4 root critical + 8 service/security) and 194/194 full repository baseline.
- **Root Security Policy**: Strict production TLS policy (`ssl` or `starttls`), DPAPI `CurrentUser` encrypted storage in single atomic JSON envelope (`{accountId}.json`), immutable connection snapshots, expectedVersion concurrency conflict detection (HTTP 409). Plaintext loopback restricted to test lab `127.0.0.1:5143`.
- **Protections**: All account routes pass through `LocalSecurityMiddleware`:
  - Loopback Host validation (`127.0.0.1:6174`)
  - Origin verification (`http://localhost:5173`, `http://127.0.0.1:5173`)
  - Mandatory session token (`X-BitigMail-Session`)
  - No-store response headers (`Cache-Control: no-store, no-cache, must-revalidate`, `Pragma: no-cache`)

---

## 2. Exact Routes & Safe Dummy Request/Response Contracts

### Route 1: `GET /api/accounts`
- **Description**: List all accounts scoped to companyId and projectId.
- **Query Params**: `companyId=comp_acme&projectId=proj_migration`
- **Safe Dummy Response (HTTP 200 OK)**:
```json
[
  {
    "accountId": "acc_0191eb9a-7c2a-71b0-9173-0428ad5e0001",
    "companyId": "comp_acme",
    "projectId": "proj_migration",
    "displayName": "Acme Kurumsal Destek",
    "email": "support@example.test",
    "host": "imap.example.test",
    "port": 993,
    "tlsMode": "ssl",
    "username": "support@example.test",
    "version": 1,
    "createdAtUtc": "2026-09-13T12:00:00Z",
    "updatedAtUtc": "2026-09-13T12:00:00Z"
  }
]
```

### Route 2: `GET /api/accounts/{accountId}`
- **Description**: Get single account public DTO (zero password/secret properties).
- **Query Params**: `companyId=comp_acme&projectId=proj_migration`
- **Safe Dummy Response (HTTP 200 OK)**:
```json
{
  "accountId": "acc_0191eb9a-7c2a-71b0-9173-0428ad5e0001",
  "companyId": "comp_acme",
  "projectId": "proj_migration",
  "displayName": "Acme Kurumsal Destek",
  "email": "support@example.test",
  "host": "imap.example.test",
  "port": 993,
  "tlsMode": "ssl",
  "username": "support@example.test",
  "version": 1,
  "createdAtUtc": "2026-09-13T12:00:00Z",
  "updatedAtUtc": "2026-09-13T12:00:00Z"
}
```

### Route 3: `POST /api/accounts`
- **Description**: Create a new IMAP account with encrypted password storage.
- **Safe Dummy Request**:
```json
{
  "companyId": "comp_acme",
  "projectId": "proj_migration",
  "displayName": "Acme Kurumsal Destek",
  "email": "support@example.test",
  "host": "imap.example.test",
  "port": 993,
  "tlsMode": "ssl",
  "username": "support@example.test",
  "password": "synthetic-user-secret-1"
}
```
- **Safe Dummy Response (HTTP 200 OK)**:
```json
{
  "accountId": "acc_0191eb9a-7c2a-71b0-9173-0428ad5e0001",
  "companyId": "comp_acme",
  "projectId": "proj_migration",
  "displayName": "Acme Kurumsal Destek",
  "email": "support@example.test",
  "host": "imap.example.test",
  "port": 993,
  "tlsMode": "ssl",
  "username": "support@example.test",
  "version": 1,
  "createdAtUtc": "2026-09-13T12:00:00Z",
  "updatedAtUtc": "2026-09-13T12:00:00Z"
}
```

### Route 4: `PUT /api/accounts/{accountId}` and `POST /api/accounts/{accountId}/update`
- **Description**: Update account metadata or replace password; requires `expectedVersion`.
- **Safe Dummy Request**:
```json
{
  "companyId": "comp_acme",
  "projectId": "proj_migration",
  "expectedVersion": 1,
  "displayName": "Acme Müşteri Hizmetleri",
  "port": 993,
  "tlsMode": "ssl"
}
```
- **Safe Dummy Response (HTTP 200 OK)**:
```json
{
  "accountId": "acc_0191eb9a-7c2a-71b0-9173-0428ad5e0001",
  "companyId": "comp_acme",
  "projectId": "proj_migration",
  "displayName": "Acme Müşteri Hizmetleri",
  "email": "support@example.test",
  "host": "imap.example.test",
  "port": 993,
  "tlsMode": "ssl",
  "username": "support@example.test",
  "version": 2,
  "createdAtUtc": "2026-09-13T12:00:00Z",
  "updatedAtUtc": "2026-09-13T12:05:00Z"
}
```
- **Conflict Response (HTTP 409 Conflict on stale expectedVersion)**:
```json
{
  "error": "Hesap sürüm uyuşmazlığı: beklenen 1, güncel 2.",
  "currentVersion": 2,
  "expectedVersion": 1
}
```

### Route 5: `DELETE /api/accounts/{accountId}` and `POST /api/accounts/{accountId}/delete`
- **Description**: Delete local account metadata and DPAPI encrypted secret (never remote mailboxes).
- **Safe Dummy Request (JSON body for POST or query for DELETE)**:
```json
{
  "companyId": "comp_acme",
  "projectId": "proj_migration",
  "expectedVersion": 2
}
```
- **Safe Dummy Response (HTTP 200 OK)**:
```json
{
  "success": true,
  "accountId": "acc_0191eb9a-7c2a-71b0-9173-0428ad5e0001"
}
```

### Route 6: `POST /api/accounts/test`
- **Description**: Safe IMAP connection test without leaking credentials or raw socket stack traces.
- **Safe Dummy Request**:
```json
{
  "host": "imap.example.test",
  "port": 993,
  "tlsMode": "ssl",
  "username": "support@example.test",
  "password": "synthetic-user-secret-1"
}
```
- **Safe Dummy Response (Success - HTTP 200 OK)**:
```json
{
  "success": true,
  "message": "Bağlantı ve kimlik doğrulama başarılı.",
  "latencyMs": 42
}
```
- **Safe Dummy Response (Failure - HTTP 400 Bad Request)**:
```json
{
  "success": false,
  "error": "Kimlik doğrulama başarısız oldu. Kullanıcı adı veya parola hatalı."
}
```

### Route 7: `POST /api/accounts/{accountId}/test`
- **Description**: Safe connection test using securely stored DPAPI credentials for an account.
- **Safe Dummy Request**:
```json
{
  "companyId": "comp_acme",
  "projectId": "proj_migration"
}
```
- **Safe Dummy Response (HTTP 200 OK)**:
```json
{
  "success": true,
  "message": "Bağlantı ve kimlik doğrulama başarılı.",
  "latencyMs": 38
}
```

### Route 8: `GET /api/accounts/{accountId}/folders` and `POST /api/accounts/{accountId}/folders`
- **Description**: Real folder enumeration with exact path, delimiter, selectable status, and count validity.
- **Query / Body**: `companyId=comp_acme&projectId=proj_migration`
- **Safe Dummy Response (HTTP 200 OK)**:
```json
{
  "accountId": "acc_0191eb9a-7c2a-71b0-9173-0428ad5e0001",
  "folders": [
    {
      "name": "INBOX",
      "fullPath": "INBOX",
      "delimiter": "/",
      "isSelectable": true,
      "messageCount": 12,
      "unreadCount": 3,
      "countValid": true,
      "statusError": null
    },
    {
      "name": "Arşiv",
      "fullPath": "Arşiv",
      "delimiter": "/",
      "isSelectable": true,
      "messageCount": 450,
      "unreadCount": 0,
      "countValid": true,
      "statusError": null
    },
    {
      "name": "Sorunlu Klasör",
      "fullPath": "Sorunlu Klasör",
      "delimiter": "/",
      "isSelectable": true,
      "messageCount": null,
      "unreadCount": null,
      "countValid": false,
      "statusError": "Klasör ileti sayısı okunamadı."
    }
  ]
}
```

---

## 3. ImapClientService Correctness Fixes
1. **Count Discovery Fallback**:
   - Primary: `folder.Status(StatusItems.Count | StatusItems.Unread)`
   - Safe Fallback: Read-only `folder.Open(FolderAccess.ReadOnly)` then `folder.Close(false)`
   - Both Fail: Returns `CountValid = false`, `MessageCount = null`, and `StatusError = "Klasör ileti sayısı okunamadı."`. Never silently returns false zero (`messageCount = 0`).
2. **Subfolder Discovery Faults**:
   - `folder.GetSubfolders()` failures are never swallowed into partial success lists.
   - Throws `InvalidOperationException("IMAP alt klasörleri taranırken sunucu hatası oluştu.")`, producing a stable HTTP 400 error response.
3. **Represented Metadata**:
   - Full exact path (supports Turkish and Unicode folder names like `İstanbul`, `Arşiv`)
   - Hierarchy delimiter (e.g. `/` or `.`)
   - Selectable status (`IsSelectable`)
   - Explicit count validity (`CountValid`) blocking unverified preview.
