# TASK043 root transport verification

Status: backend transport changes accepted locally; UI/package acceptance pending.

- Password IMAP/POP explicit account-level `allowUnencryptedConnection`; false default. No automatic downgrade or certificate bypass.
- IMAP updated endpoint requires renewed consent; switching to TLS clears stored consent; account version increments.
- Persisted record, public DTO, resolver, connection test, folder listing and transfer worker credentials carry consent. OAuth fixedTLS boundaries unchanged.
- Actual local IMAP socket checks: opted-in none authenticates, nonconsented none refuses beforeauth, unsupported STARTTLS refuses withoutauth.
- Actual local POP socket: production policy with explicitconsent downloads12, attachments4, hashesexact, DELE0.
- Full backend suite891/891 PASS. Evidence root043-backend.trx. New focused consenttests8/8.
- First broad run caught a testlistener start/cleanup race in the new fixture (1failure/891); fixture now starts pendingaccept before schedulingserver. Full rerun passed.
- No real mail server or usercredentials used; installed userprofile untouched.

Catalog integration follow-up: added real admin-only POST /api/catalog/companies/{companyId}/projects; durable new project within existing company, duplicate-name rejection, no automatic extension of operator grants. Route inventory delta independently verified against previous production/development hashes before adding this single route. Focused catalog/security/transport48 tests pass after this change; previous full891 preceded the new one-test addition.
Final backend after real project endpoint: full892/892 PASS; evidence root043-backend-final.trx.
