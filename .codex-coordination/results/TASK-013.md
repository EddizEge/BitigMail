TASK_ID: TASK-013
STATUS: DONE
EXECUTOR: GEMINI + ASTRA HIGH FALLBACK
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.Engine/Storage/MimeFidelityPolicy.cs
- THIRD-PARTY-NOTICES.md
- engine/README.md
- engine/BitigMail.Engine/BitigMail.Engine.csproj
- engine/BitigMail.Engine/Models/{ConversionReport,MimeImportModels,SelectionPreviewResult}.cs
- engine/BitigMail.Engine/Storage/{MboxrdRecordReader,MimeSelectionEngine,MimeSourceInspector,MimeToPstConverter,MimeVendorCalibrator}.cs
- engine/BitigMail.LocalHost/Dialogs/{FileHandleRegistry,IFilePickerService,WinFormsFilePickerService}.cs
- engine/BitigMail.LocalHost/Jobs/{JobManager,LocalJobRecord}.cs
- engine/BitigMail.LocalHost/{LocalEngineApiEndpoints,Program}.cs
- engine/BitigMail.TestingHost/{Program,TestingFilePickerService}.cs
- engine/BitigMail.Engine.Tests/{MimeImportWorkflowTests,MimeCriticalBoundaryTests}.cs
- engine/BitigMail.Engine.Tests/GateB/GateBExperimentTests.cs
- prototype/src/api/localEngineClient.ts
- prototype/src/components/convert/{ConversionWorkspace,LocalMimeWorkflow}.tsx
- prototype/src/components/convert/mimeWorkflow.css
- prototype/src/components/jobs/JobCenter.tsx
- prototype/src/components/transfer/TransfersTabView.tsx
- prototype/src/hooks/{useLocalEngine,useMimeImport}.ts
- prototype/src/types/localEngine.ts
- prototype/src/tests/mimeImportClient.test.ts
- prototype/tests/e2e/{local-archive-workflow,local-convert-workflow,testinghost-real-mime}.spec.ts
SUMMARY:
- Added typed opaque EML file-set/tree and explicit mboxrd import through immutable server analysis/preview, new verified PST publication, shared durable job management, report restoration and existing archive-flow reuse.
- Preserved same raw MIME bytes for MimeKitLite/Aspose, exact one-pass mboxrd dequote, qualified evaluation additions as DIFFERENCES, exact content/metadata/attachment/CID verification and fail-closed mutation/limit/path/collision behavior.
- Added the real React MIME workflow, job/report history, frozen customer/project context, exact folder/date filtering, amber trial-difference disclosure and responsive desktop/mobile behavior.
- Gemini produced the initial backend/API implementation. Astra used the documented critical-integrity exception to close fidelity boundaries and the Gemini quota/unavailable exception to complete frontend/tree-fixture work. SOL performed deterministic verification.
VERIFICATION:
- Release backend: 152/152 passed, 0 failed/skipped; targeted MIME/null boundary: 29/29 passed.
- Dependency audit: no known vulnerable packages; System.Formats.Asn1 pinned/resolved at 8.0.1, MimeKitLite 4.17.0 and Aspose.Email 24.8.0 unchanged.
- Frontend: typecheck PASS; lint PASS; production build PASS; Vitest 56/56 PASS.
- Real TestingHost MIME Playwright: 3/3 PASS at 1660px, 1366px and 390px; screenshots/checks under C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task013-qa/ui-final/.
- Existing mocked OST/archive Playwright: 30/30 accepted (24 functional plus 6 isolated offline-banner cases).
- Actual HTTP guards/filter/tree checks: 25/25 PASS. Full EML and MBOX each produced 12 messages/4 exact attachments; filtered tree produced 3 messages/1 attachment in exact Projeler/İstanbul folder with ignored non-EML count 1.
- Independent libpff/source-oracle: EML independent-89d238ad, MBOX independent-4b95ee42, tree independent-9e89de5b, imported-PST-to-four-year-parts independent-81497beb; missing/extra/body differences 0 and source unchanged.
- Normal services: Vite 5173 PID 40012 and LocalHost 6174 PID 78768 listening; TestingHost 6175 stopped.
RISKS:
- Aspose.Email remains unlicensed evaluation mode. Every successful MIME import is deliberately labelled DIFFERENCES / deneme işaretleri içeriyor; no lossless claim is made.
- MBOX support is explicitly mboxrd only. Unmeasured MIME/MAPI properties remain unknown rather than implicitly verified.
UNCERTAINTIES:
- Browser plugin was unavailable; authorized installed headless Playwright supplied DOM, console, interaction, screenshot and responsive evidence.
