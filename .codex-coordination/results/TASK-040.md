TASK_ID: TASK-040
STATUS: DONE
EXECUTOR: SOL DIRECT (AUTHORIZED EXCEPTION; ASTRA CRITICAL CORES)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.Desktop/BitigMail.Desktop.csproj
- engine/BitigMail.Desktop/Program.cs
- engine/BitigMail.LocalHost/Program.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.DesktopLifecycle.cs
- engine/BitigMail.LocalHost/Security/SecurityConfig.cs
- engine/BitigMail.Engine/Distribution/DesktopInstanceLease.cs
- engine/BitigMail.Engine/Distribution/DesktopNavigationPolicy.cs
- engine/BitigMail.Engine/Distribution/DesktopProtocolReader.cs
- engine/BitigMail.Engine/Distribution/DesktopReadyProof.cs
- engine/BitigMail.LocalHost/Security/DesktopOperationGate.cs
- engine/BitigMail.Engine.Tests/DesktopInstanceLeaseTests.cs
- engine/BitigMail.Engine.Tests/DesktopNavigationPolicyTests.cs
- engine/BitigMail.Engine.Tests/DesktopProtocolReaderTests.cs
- engine/BitigMail.Engine.Tests/DesktopReadyProofTests.cs
- engine/BitigMail.Engine.Tests/DesktopOriginSecurityTests.cs
- engine/BitigMail.Engine.Tests/DesktopOperationGateTests.cs
- engine/BitigMail.Engine.Tests/DesktopShutdownTests.cs
- prototype/src/components/auth/IdentityGate.tsx
- prototype/src/styles/index.css
- scripts/build-desktop.ps1
SUMMARY:
- Added a real WinForms/WebView2 desktop shell that owns a dynamically bound loopback LocalHost process through an authenticated private stdin/stdout protocol.
- Packaged the production React build and self-contained win-x64 desktop/engine binaries with no Node/Vite runtime dependency.
- Enforced exact loopback Host/Origin, safe navigation/new-window behavior, guarded first-run proof injection, per-user profile/WebView2 storage, same-profile exclusion, close-to-tray, and drain-before-exit semantics.
- Shutdown persistence/unresolved-worker failures now remain visible and retryable; concurrent exit requests are serialized and startup cleanup cannot hide the original failure or falsely claim the child stopped.
- Added a branded, accessible first-run/login/loading/error experience and preserved the unsigned internal-build/WebView2/SDK disclosures.
- Hardened packaging against native-command false success and stale/nested UI or engine output.
VERIFICATION:
- Desktop and LocalHost Release builds: PASS, 0 warnings / 0 errors.
- Desktop-focused backend suite: PASS 56/56 (`engine/BitigMail.Engine.Tests/TestResults/task040-final-desktop.trx`).
- Full backend suite: PASS 866/866 (`engine/BitigMail.Engine.Tests/TestResults/task040-full.trx`).
- Frontend typecheck: PASS; lint: PASS; build: PASS; tests: PASS 151/151 (an unrelated timer-sensitive test failed once, then its focused rerun and the complete rerun passed).
- Production host boundary probe: PASS 10/10 (`.codex-coordination/evidence/STAGE9/root040-native-host-boundary.json`).
- External self-contained WebView2 smoke from a temporary directory with child PATH limited to System32: PASS; process exit 0, styled branded screenshot 189,836 bytes, 186 CSS rules loaded, no error file, no surviving engine, and no nested stale publish directories. This capture preceded the final bounded startup-cleanup/concurrent-exit guard; that final guard was verified by a clean Release build and the 56-test desktop suite.
- Final capture: `C:/Users/Eddiz/AppData/Local/Temp/BitigMail-Desktop-Final-f47ae271716d439aa14b585ef7696850/desktop-render.png`.
RISKS:
- Package is intentionally unsigned and for internal distribution; no clean-Windows or trust-prompt claim is made.
- WebView2 Runtime remains a documented prerequisite.
- The preliminary TASK-040 package manifest is evidence only; installer-grade manifest/signature/rollback integrity belongs to TASK-041.
UNCERTAINTIES:
- Close-to-tray and blocked-shutdown recovery are covered by deterministic lifecycle/protocol tests and code paths; the final automated visible smoke exercises successful capture and clean exit, not a deliberately corrupted live profile.
