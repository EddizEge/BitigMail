TASK_ID: TASK-043
STATUS: IN_PROGRESS
EXECUTOR: SOL DIRECT
COMPLETED:
- Replaced the global raw identity/admin toolbar with a compact branded account control and role-gated management drawer.
- Added a four-choice operation-first transfer hub while preserving the existing real workflow components and saved-job routing.
- Added authenticated catalog precedence in the transfer screen so real account operations no longer submit demo company/project IDs.
- Added explicit IMAP and POP unencrypted transport choice with default-safe TLS, port defaults, visible risk consent, consent reset on endpoint/security changes, and DTO propagation.
- Updated the production identity Playwright flow and native synthetic smoke selector for the new management UI.
- Frontend typecheck, lint, build, 151/151 unit tests, production first-run UI, and secure ownership checks pass (one unrelated timer test was flaky once and passed on full rerun).
- Root backend verification is complete: 891/891 plus real IMAP TLS/no-downgrade and POP no-delete socket scenarios.
REMAINING:
- Propagate the authenticated catalog through the whole app state/client directory, not only the transfer surface, without persisting authorized IDs into demo storage.
- Add focused TASK-043 UI tests for management, operation switching, mobile layout, and plaintext-consent behavior.
- Capture desktop 1440x900 and mobile 390 evidence, then build and inspect the 0.9.3 native package with an authenticated native screenshot.
- Root acceptance, safe installed-app update, and final DONE result.
BLOCKERS: None.
