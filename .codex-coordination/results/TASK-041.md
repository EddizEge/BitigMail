TASK_ID: TASK-041
STATUS: DONE
EXECUTOR: SOL DIRECT (AUTHORIZED EXCEPTION; ASTRA CRITICAL CORES)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.Setup/BitigMail.Setup.csproj
- engine/BitigMail.Setup/Program.cs
- engine/BitigMail.Setup/InstallCoordinator.cs
- engine/BitigMail.Desktop/Program.cs
- engine/BitigMail.LocalHost/Program.cs
- engine/BitigMail.Engine/Distribution/PackagePayloadValidator.cs
- engine/BitigMail.Engine/Distribution/PackageManifestReader.cs
- engine/BitigMail.Engine/Distribution/DesktopProfileSchema.cs
- engine/BitigMail.Engine.Tests/PackagePayloadValidatorTests.cs
- engine/BitigMail.Engine.Tests/PackageManifestReaderTests.cs
- engine/BitigMail.Engine.Tests/DesktopProfileSchemaTests.cs
- scripts/build-windows-release.ps1
- docs/WINDOWS_QUICK_START_TR.md
- docs/WINDOWS_RELEASE_ACCEPTANCE.md
- artifacts/BitigMail-Internal-0.9.1/
- .codex-coordination/evidence/STAGE9/sol041-release-lifecycle.json
SUMMARY:
- Produced a real self-contained per-user Windows setup executable with a branded native UI for install/update, launch, rollback, and uninstall.
- Added strict package/receipt admission, exact payload hashing, guarded versioned staging, durable atomic active/previous pointers, schema-aware update/rollback, install-operation/profile leases, and installed-shell activation handoff.
- Added a durable installed setup launcher and verified Start Menu shortcut ownership rules; isolated test roots never alter the user's shortcut.
- Uninstall validates every receipt before mutation, removes only unchanged receipt-owned files, refuses reparse traversal, preserves modified/unknown files, and always preserves user data.
- The installed shell now infers canonical `versions/<version>` layout even on direct double-click, takes the install activation lease, and rejects obsolete versions before starting the engine.
- Release includes strict external manifest, SHA256SUMS, Turkish quick start, acceptance record, explicit unsigned-internal labeling, and WebView2 prerequisite disclosure.
VERIFICATION:
- Release candidate: `artifacts/BitigMail-Internal-0.9.1` (589,211,058 bytes); setup SHA-256 `ce5ec12b881ed0a592fcdfc1f9cfdae7ee73539c2f9650ab39e36ed51f4d31ba`.
- SHA256SUMS: PASS 1118/1118; Authenticode: `NotSigned` (intentional internal deliverable, not publisher authentication).
- Actual isolated RC lifecycle with PATH limited to System32: install, durable-launcher launch, real WebView2 capture, clean engine exit, and uninstall all exit 0; data schema preserved; 0 version files remain.
- Actual upgrade/rollback: 0.9.0 install -> 0.9.1 update -> active 0.9.1 -> rollback -> active 0.9.0 -> uninstall; all exit 0 and profile preserved.
- Direct installed `BitigMail.exe` launch without `--install-root`: current version PASS with real WebView capture and clean exit; copied obsolete-version fixture exits 1, writes the inactive-version error, and starts no engine. This proves canonical install-root inference and obsolete-shell refusal.
- Installer adversarial boundary suite: PASS 13/13 (`.codex-coordination/probes/InstallerBoundaryTests/TestResults/sol041-final13.trx`).
- Production desktop host boundary: PASS 11/11 (`.codex-coordination/evidence/STAGE9/root040-native-host-boundary.json`).
- Full backend regression: PASS 877/877 (`engine/BitigMail.Engine.Tests/TestResults/task041-full.trx`).
- Frontend: typecheck PASS, lint PASS, build PASS, tests PASS 151/151.
- Durable evidence: `.codex-coordination/evidence/STAGE9/sol041-release-lifecycle.json`.
RISKS:
- Commercial publisher-signature gate remains OPEN: the executable is intentionally unsigned and Windows will not identify a trusted publisher.
- Clean-Windows gate remains OPEN: Windows Sandbox is unavailable on this host, so no clean-machine acceptance claim is made.
- WebView2 Runtime is an explicit prerequisite rather than bundled.
- The setup executable is intentionally retained after uninstall as the durable launcher/installer; application version payloads are removed and user data remains.
- Stage 9 has a qualified internal deliverable, but is NOT fully complete against commercial signature and clean-Windows acceptance gates.
UNCERTAINTIES:
- No real code-signing certificate or clean Windows VM was available.
- Stage 10 Azure setup and real personal Outlook non-deleting pilot remain excluded and are the next milestone.

## Root acceptance — 21 September 2026
Qualified internal software deliverable accepted. Actual default-root install exited 0, active version 0.9.1 and Start Menu shortcut verified. Installed Windows application opened for user review and remains running; evidence `evidence/STAGE9/root041-default-install-open.json`. No Stage10 work performed. Stage9 commercial signature and clean-Windows gates remain explicitly OPEN. Latest quota71%remaining. Root authored acceptance/quickstart/roadmap records; release contains quickstart, while detailed acceptance remains in workspace docs. Do not close user's review window.
