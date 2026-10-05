# SDK license and scale acceptance

At process start BitigMail reads only the explicitly configured `BITIGMAIL_ASPOSE_LICENSE_PATH`. License bytes are never copied into the repository, API response, logs or templates. A successful `SetLicense` records `licensed_loaded`; missing configuration remains `unlicensed`; read or load failure is `configuration_error` and conversion startup is refused. License changes require a process restart. No watermark or trial-limit bypass exists.

Immediately after the one-time license attempt and before HTTP requests, one serial synthetic `MapiMessage.SetProperty` warmup initializes the SDK property tables. The bounded cold-process probe in `docs/SDK_INITIALIZATION_VALIDATION.md` observed two failures in three cold parallel processes without warmup and zero in three with warmup. This does not establish general SDK thread safety.

`ScaleAcceptanceHarness` requires an explicit existing target directory and explicit source files, hashes every source, calculates conservative capacity, and reports `PLANNED_NOT_EXECUTED`; its small EML self-check independently counts MIME messages, attachments and usable dates. It does not manufacture sparse data or convert planning into execution evidence.

Current state is `unlicensed / initialized / LICENSED_OUTPUT_ACCEPTANCE_PENDING`. No commercial license, licensed-output canary, real 10/25/50/100 GB store, or clean-Windows provider run was available. Those remain external acceptance gates.
