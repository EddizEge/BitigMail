TASK_ID: TASK-009
STATUS: DONE
EXECUTOR: GEMINI
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- lab/ost-spike/harness/Program.cs
- docs/CID_BODY_ACCEPTANCE.md
- lab/ost-spike/output/genuine-full-converted-09.pst
- lab/ost-spike/output/genuine-full-converted-09-report.json
- .codex-coordination/results/TASK-009.md
SUMMARY:
- Closed the inline CID uncertainty. Aspose.Email 24.8 `MapiAttachment` has no CLR `ContentId` property; the prior reflection accessor always returned null.
- Gemini replaced both defective reflection sites with one narrow helper that reads `PR_ATTACH_CONTENT_ID_W` (`0x3712001F`) from `MapiAttachment.Properties`, with ANSI `0x3712001E` fallback. Angle brackets are normalized only during comparison; stored metadata is neither changed nor synthesized.
- Source OST and output PST both store CID `proje_logo_cid`. Full HTML SHA-256 is identical before/after (`6ACA1CE092B1B914EDC317C2EA01AAB85FCAF161376AEF78173B55C241127AE4`), contains `cid:proje_logo_cid`, and resolves to `logo.png` (73 bytes; SHA-256 `411E320C1D42FE6857FDF02EB7EE4AE020FE6522782476C81D083014FD56C61F`). CID loss is refuted.
- Body acceptance policy now distinguishes EML-to-OST acquisition normalization from OST-to-PST loss. Primary source HTML/text representations must remain hash-identical after newline normalization; additive plaintext is allowed and reported.
VERIFICATION:
- Final Release build: exit 0; 0 warnings; 0 errors.
- Run09 genuine OST conversion: exit 0; 13 read, 13 written, 0 failed; 12/12 baseline plus one Outlook test message; coverage `COMPLETE_WITH_EXTRA`.
- Source snapshot SHA-256 before/after: `B0801758A2E61D4CE6E86799701A81A7A60C38401F73B13C993D94C03A2EE57A`; unchanged.
- Same-SDK msg-11 source: ContentId `proje_logo_cid`, `logo.png` 73 bytes, expected SHA-256, `AttachmentsVerified=true`.
- Same-SDK msg-11 reopened PST: same ContentId/name/size/hash; item differences empty.
- Independent libpff run09 export under `--network none`: exit 0; source/PST message counts 13/13; folder counts 6/3/4 vs 6/3/4; all four attachment name/size/SHA-256 values match.
- Read-only targeted raw-property evidence: both OST/PST tag `0x3712001F` values match; full HTML and logo hashes match.
RISKS:
- Four decoded plain-body projections still differ from original EML representations due to Outlook acquisition/rendering behavior. They are documented differences, not OST-to-PST loss where the primary stored representation remains identical.
UNCERTAINTIES:
- libpff export does not expose inline CID property tags, so independent CID-property confirmation relies on the targeted read-only Aspose property-bag diagnostic plus independent HTML/attachment preservation evidence.
CAPABILITY_STATUS: READY_FOR_LOCAL_OST_WORKFLOW_INTEGRATION_WITH_DOCUMENTED_BODY_POLICY
