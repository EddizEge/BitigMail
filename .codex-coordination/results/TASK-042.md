TASK_ID: TASK-042
STATUS: DONE
EXECUTOR: SOL DIRECT (AUTHORIZED EXCEPTION; ASTRA HOST PROTOCOL CORE)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.Desktop/Program.cs
- engine/BitigMail.LocalHost/Program.cs
- engine/BitigMail.LocalHost/IdentityApiEndpoints.cs
- engine/BitigMail.LocalHost/Security/DesktopSetupProofRefresh.cs
- engine/BitigMail.Engine.Tests/DesktopSetupProofRefreshTests.cs
- prototype/src/components/auth/IdentityGate.tsx
- prototype/src/api/localEngineClient.ts
- artifacts/BitigMail-Internal-0.9.2/
- .codex-coordination/evidence/STAGE9/sol042-native-webview-admin.json
SUMMARY:
- First-admin submission now requests a fresh 32-byte setup proof over the owned native WebView2/private stdin channel immediately before every production submit.
- The shell admits messages only from the trusted top-level loopback document, serializes refresh and exit writes, verifies the engine's HMAC acknowledgement, and never logs the proof.
- Refresh is rejected while exiting, while another refresh is active, after initialization, or while operation drain is active; failures return bounded, actionable public messages.
- The frontend preserves the TestingHost injected-proof fallback, prevents duplicate submits, requires a 12-character first-admin password, and clearly explains expired/unavailable native proof failures.
- Capture-only synthetic smoke is restricted to an explicit profile under the OS temporary root; it cannot run against the default desktop profile.
VERIFICATION:
- Actual installed 0.9.2 WebView2 flow after an explicit page reload: form became ready, inputs were filled, `Yönetici oluştur` was clicked, native refresh was requested, MAC matched and acknowledged, first administrator was created, identity gate disappeared, admin bar rendered, capture completed, and engine exited cleanly.
- Actual WebView evidence: `C:/Users/Eddiz/AppData/Local/Temp/BitigMail-SetupProof-Reload-7ebd24d8ca40493f8a4020fe003d5178/admin-after-reload.png` (58,821 bytes), no error file, 0 remaining fixture engines.
- Astra proof/catalog suite: PASS 18/18; actual private-protocol process checks: PASS 7/7 (`.codex-coordination/evidence/TASK-042/root042-native-refresh.json`).
- SOL focused backend suite: PASS 54/54 (`engine/BitigMail.Engine.Tests/TestResults/task042-focused.trx`).
- Frontend: typecheck PASS, lint PASS, build PASS, tests PASS 151/151.
- Desktop Release build: PASS, 0 warnings / 0 errors.
- Release hashes: PASS 1118/1118; package remains intentionally `NotSigned`.
RISKS:
- Commercial signature and clean-Windows gates remain open as documented for TASK-041.
- Synthetic UI credentials are fixed test-only values and are admitted only when both a capture path and explicit temporary profile are supplied.
UNCERTAINTIES:
- No real user credentials or personal mailbox data were used. Stage 10 remains excluded.
