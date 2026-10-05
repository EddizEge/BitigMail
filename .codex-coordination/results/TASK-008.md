TASK_ID: TASK-008
STATUS: DONE_WITH_DIFFERENCES
EXECUTOR: GEMINI
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- .gitignore
- lab/ost-spike/README.md
- lab/ost-spike/OUTLOOK_SETUP.md
- lab/ost-spike/OST_EXPERIMENT_RESULT.md
- lab/ost-spike/local-credentials.example.json
- lab/ost-spike/harness/BitigMail.OstHarness.csproj
- lab/ost-spike/harness/Program.cs
- lab/ost-spike/scripts/install_local_dotnet.ps1
- lab/ost-spike/scripts/build_harness.ps1
- lab/ost-spike/scripts/run_harness.ps1
- lab/ost-spike/scripts/lab_mail_start.ps1
- lab/ost-spike/scripts/lab_mail_status.ps1
- lab/ost-spike/scripts/lab_mail_stop.ps1
- lab/ost-spike/scripts/seed_and_verify_mailbox.py
- lab/ost-spike/output/genuine-full-converted-04.pst
- lab/ost-spike/output/genuine-full-converted-04-report.json
- lab/ost-spike/output/clean-container-full-04.pst
- lab/ost-spike/output/clean-container-full-04-report.json
- lab/ost-spike/output/independent-libpff-full-04-report.json
- .codex-coordination/results/TASK-008.md
SUMMARY:
- A genuine Classic Outlook OST generated from the isolated synthetic lab was converted item-by-item into a new Unicode PST. The source was never overwritten or modified.
- Final source snapshot: 16,818,176 bytes; SHA-256 `B0801758A2E61D4CE6E86799701A81A7A60C38401F73B13C993D94C03A2EE57A`.
- Header inspection: `!BDN`, OST client magic `0x4F53` at offset 8, version word `0x0024` at offset 10. Aspose independently reported `PersonalStorage.FileFormat=Ost`; libpff reported `64-bit with 4k page OST`, encryption none.
- Source inventory contains 13 physical messages: all 12 baseline fixtures plus one separately preserved Outlook test message. Active folder counts are 6 Gelen Kutusu, 3 Gönderilenler, and 4 Projeler/İstanbul.
- Final Windows conversion read 13, wrote 13, failed 0. Output PST is 271,360 bytes with SHA-256 `98C1971DBED4F504865B32922147244EDE2AD669AD3694E0DCE0997270D6AC44`.
VERIFICATION:
- Project-local .NET SDK 8.0.425; Aspose.Email 24.8.0 pinned. Final Release build: exit 0, 0 warnings, 0 errors.
- Source SHA-256 before and after final conversion: identical; source mutation check PASS.
- Format negative control: a genuine PST input sharing `!BDN` magic was identified as `FileFormat=Pst` and rejected from `ost-to-pst` mode with exit 2 before destination creation; destination existence false.
- Same-SDK read-only reopen: 13/13 physical items, source count match true, 12/12 baseline coverage `COMPLETE_WITH_EXTRA`, duplicate msg-01/msg-02 consumed as distinct physical items, msg-03/msg-04 retained as distinct same-Message-ID items, ordered To addresses 12/12 matched.
- Same-SDK measured differences: exact decoded body match 8/12; four bodies have documented Outlook representation/normalization differences. The inline `logo.png` name/hash/size survived, but its MAPI Content-ID was null and is recorded as LOST/UNKNOWN/DIFFERENCES. No evaluation watermark was observed in this genuine run; documented trial limits are separate from observed changes.
- Independent reader: Debian `pff-tools` 20180714-3+b2 in a locally built image based on official `debian:bookworm-slim@sha256:88200866...4171`; source and PST `pffinfo`/`pffexport` all exit 0 under `--network none` with read-only source mount and no profile/credential/socket mounts.
- Independent source/PST comparison: 13 vs 13 messages; folder distribution 6/3/4 vs 6/3/4; header identity multiset MATCH; duplicate and shared-ID multiplicities MATCH; all four attachment name/size/SHA-256 values MATCH; every body representation common to source and PST has matching normalized SHA-256. PST added nine plain-text representations alongside preserved source representations. libpff does not expose enough evidence to accept inline CID preservation, so CID remains UNKNOWN independently.
- Clean-container proof: official `mcr.microsoft.com/dotnet/runtime:8.0.31-bookworm-slim@sha256:9cfa8aaf...1524`, network none, only harness/runtime/manifest/read-only snapshot/output mounts; exit 0, 13/13 written, 0 failures, source hash unchanged. This proves extraction without Outlook installation/profile/account access in that container.
- Phase A remains verified: GreenMail loopback-only lab; 12 baseline plus one Outlook test extra on server; three mapped folders; four attachments; semantic headers/bodies verified.
RISKS:
- Fidelity is not fully accepted: four source-body projections differ from the original EML text representation. The measured inline ContentId is null in BOTH source OST and reopened PST, so conversion-stage CID loss is not established; acquisition-stage change or reader/property interpretation remains unresolved. The independent export does not expose CID. Overall fidelity status is DIFFERENCES, not PASS.
- Aspose trial documents a 50-item-per-folder limit; this 13-item experiment does not establish behavior beyond that limit or licensed behavior.
- Output PST bytes are not deterministic between Windows and Linux runs even when semantic inventory matches; raw PST hash equality is not an acceptance criterion.
UNCERTAINTIES:
- Unmeasured MAPI named properties, raw transport header streams, compressed RTF synchronization, and storage-specific EntryID/RecordKey fields remain UNKNOWN.
- No 100GB, corrupt-file recovery, resumability, or licensed large-folder experiment was performed.
INDEPENDENT_READER_STATUS: PASS_WITH_LIMITATIONS
GENUINE_OST_CONVERSION_STATUS: SUCCESS_13_OF_13
FIDELITY_STATUS: DIFFERENCES
