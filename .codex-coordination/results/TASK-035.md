TASK_ID: TASK-035
STATUS: DONE
EXECUTOR: SOL 5.6 LIMITED (AUTHORIZED ANTIGRAVITY FAILURE EXCEPTION)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.Engine/Storage/AsposeSdkStartupService.cs
- engine/BitigMail.Engine/Storage/ScaleAcceptanceHarness.cs
- engine/BitigMail.LocalHost/Program.cs
- engine/BitigMail.LocalHost/Security/AsposeLicenseConfigurationStore.cs
- engine/BitigMail.LocalHost/Dialogs/IFilePickerService.cs
- engine/BitigMail.LocalHost/Dialogs/WinFormsFilePickerService.cs
- lab/stage5-sdk-initialization/Program.cs
- lab/stage5-sdk-initialization/Stage5SdkInitialization.csproj
- engine/BitigMail.TestingHost/Program.cs
- engine/BitigMail.Engine.Tests/Task035SdkAndScaleTests.cs
- prototype/src/types/localEngine.ts
- prototype/src/api/localEngineClient.ts
- prototype/src/components/settings/SettingsDrawer.tsx
- prototype/src/tests/task035SdkStatus.test.tsx
- docs/SDK_LICENSE_AND_SCALE_ACCEPTANCE.md
SUMMARY:
- Added one process-start Aspose license/bootstrap service. It reads only the explicitly configured local `BITIGMAIL_ASPOSE_LICENSE_PATH`, calls official SetLicense when present, never emits license bytes, distinguishes unlicensed/licensed_loaded/configuration_error, and refuses readiness on configuration or initialization failure.
- Added the proven one-time serial synthetic MAPI property warmup after license loading and before HTTP request acceptance in production and TestingHost processes.
- Added authenticated SDK status and rendered Settings status with restart/acceptance qualification. Current status remains unlicensed / initialized / LICENSED_OUTPUT_ACCEPTANCE_PENDING.
- Added native `.lic` picker and CurrentUser-DPAPI protected persistent license bytes. Neither selected path nor bytes enter DTOs; selection only returns restartRequired and never hot-loads the SDK. Explicit environment configuration remains an administrator override.
- Preview, immutable selection/job state and MIME→PST report snapshot the startup SDK qualification while retaining existing measured fidelity canaries.
- Added bounded scale planning/self-check with explicit target directory, source hashes, capacity preflight, and independent MIME message/attachment/date counts. Planning reports PLANNED_NOT_EXECUTED and makes no 100 GB execution claim.
VERIFICATION:
- TASK-035 final focused plus root distribution validator: 22/22 PASS (`task035-gap-final.trx`).
- Settings rendered status: 1/1 PASS.
- Post-integration cold-process probe using actual `AsposeSdkStartupService`: 3 fresh processes x 32 concurrent MAPI property calls, 0 failures in every process.
- Full backend: 607/607 PASS (`task035-acceptance-final-full.trx`).
- Frontend: 147/147 PASS; typecheck/lint/build PASS.
- Final bounded license-store guard: 9/9 PASS (`task035-store-guard.trx`, Release). Selected plaintext is capped at 1 MiB, protected state at 2 MiB, reads remain bounded while streaming, reparse/directory targets are rejected, saves use serialized unique temporary files plus atomic replacement, picker failures are sanitized, and startup buffers are zeroed.
RISKS:
- No commercial Aspose license was available, so successful SetLicense and licensed-output canary behavior remain externally pending.
- The warmup evidence addresses the reproduced KnownPropertyList first-use race only; it is not a blanket SDK thread-safety claim.
- Build retains the existing non-fatal >500 kB bundle advisory.
UNCERTAINTIES:
- Real 10/25/50/100 GB PST/OST execution, licensed output acceptance, provider interoperability and clean-Windows acceptance were not run.
- No sparse/zero-filled fake large stores or unrelated user files were used.

ROOT ACCEPTANCE 2026-09-20: Core criteria and607/607 backend,147/147 frontend accepted for local Stage5. Final bounded license file read/atomic save guard requested; targeted passing evidence closes conditional local acceptance. Commercial license and real large-store acceptance remain pending. TASK036 authorized immediately after guard passes.
