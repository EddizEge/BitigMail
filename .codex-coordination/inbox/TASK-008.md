TASK_ID: TASK-008
STATUS: READY
FROM: ASTRA HIGH
TO: SOL 5.6 LIMITED

USER AUTHORIZATION:
Kullanıcı 'tamamdır yapalım' diyerek yapay corpus'tan geçerli OST oluşturma ve gerçek dönüşüm denemesine başlamayı onayladı. Eski hesap/orijinal Outlook profili olmayacak; müşteri posta verisi kullanılmayacak. Kurulum/deney artık bu sınırda yetkili, önceki araştırma görevlerindeki install-yok sınırı bu yeni görevin kapsamı değildir. Ticari satın alma/harici hesap açma yok.

KNOWN ENVIRONMENT (Astra read-only checks):
- Classic Outlook C:\Program Files\Microsoft Office\root\Office16\OUTLOOK.EXE version16.0.20326.20132 installed; no OUTLOOK process observed.
- Docker CLI/daemon works, server29.5.3. Python3.12 available. dotnet host installed but `dotnet --list-sdks` empty.
- Synthetic fixtures/mail-corpus-v1:12EML,3folders,4attachments,manifest; all verified TASK007.
- docs/OST_ENGINE_DECISION.md is authoritative. Candidate A: SDK per-item extraction + newPST; directSaveAs restriction isn't blanketread prohibition. CandidateB libpff+writer fallback; none proven.

GOAL:
Prepare actual isolated mail lab and bounded conversion harness, then genuine OST->PST experiment if a valid OST becomes available. New output under lab/ost-spike/, scripts as needed, .tools/ for project-local dev dependencies and ignored artifacts. Do not modify prototype or user's personal Outlook accounts/profile/defaults.

PHASE A — LOCAL TEST MAILBOX (do first, report readiness early):
- Use an official maintained test IMAP/SMTP Docker image (GreenMail standalone is a candidate; verify official repo/docs and exact image/version before use) or similarly bounded trustworthy testserver. Docker already installed; no new engine/system services/Windows features.
- Publish IMAP/SMTP only to 127.0.0.1 on free nonprivileged ports. Internal container bind can be0.0.0.0 but host ports MUST loopback. Dedicated named BitigMail labcontainer, explicit image tag/digest, no privileged/hostnetwork/docker-socketmount.
- Dedicated synthetic mailbox e.g. lab@bitigmail.example, random lab-only password saved in ignored local config. No existing credentials/accounts. SMTP must be mail capture only; no external delivery. Loopback plaintext is acceptable for this synthetic lab; no global TLS/trust/antivirus changes.
- Gemini implements reproducible start/status/stop+seed scripts/docs using repo conventions, SOL executes. Start/restart doesn't duplicate data silently; only explicitly owned isolated container/mailbox may be reseeded, never personal servers. Avoid logging password or Docker env dumps; provide local credential file location plus username/ports for user later.
- Seed12EML into3manifestfolders with Python imaplib APPEND, preserve raw payload/time semantics wherepossible; verify counts and FETCH attachment hashes/fields againstmanifest. Save server UIDs/UIDVALIDITY/seed manifest so duplicate Message-ID pair preserved as2physicalitems. Record server normalization vs original corpus if any. Verify Docker publishes only127.0.0.1, IMAP login/list/fetch, total12, fourattachments; do not markOSTproof.
- Write short lab/ost-spike/OUTLOOK_SETUP.md with exact host/ports/username/encryption settings and credential file, separate named lab profile instructions based official Microsoftdocs. NO GUI/browser/desktop automation, no Outlook launch, no modifying registry/default/profile or silently accessing user's existing mailbox. If profile setup requires user action, report USER_STEP_REQUIRED with concrete working lab details. Astra will coordinate user while you continue PhaseB.

PHASE B — REPEATABLE SDK HARNESS:
- Gemini creates minimal console experiment (C# preferred for Aspose.Email). Install .NET SDK project-locally into .tools/ if needed using official Microsoft distribution pinned supportedversion; no machine-wide setup/PATH changes. Normal official NuGet package restore permitted. Version pin dependencies.
- Aspose.Email trial allowed; do not obtainpaidlicense/submitform/contactvendor. Don't hide trialwatermarks/limits. Probe capability and record trial changes as differences, not PASSforfidelity. Support optional license path supplied later; no secrets committed/logged.
- Harness accepts explicit input .ost and new output .pst; original source read-only/copy, never overwriteexistinginput/output. Inspect actualformat/signature notextensionrename. Extract peritem, write newUnicodePST, counters/lossreport, sourceSHA256before/after, memory/time/outputmetrics. Wholefolder/items notloadintoRAM; small currentexperiment, no100GBclaim.
- Provide syntheticEML->PST smoke if noOSTyet (strictly NOT OSTconversion evidence); genuine OST path stays NOT RUN until user isolatedfixtureexists. Use knownmanifest for core fields/folders/attachmentsha comparisons. Consider MAPIproperties/RTF losses per decisiondoc.
- Independent PST reader ideally libpff/pffexport via isolatedofficial-source-built tool/container (do not fetch untrustedbinary). If not yet available, SDK roundtrip is SAME_SDK_ONLY and notindependentacceptance. Keep capabilityreport honest. No bespoke giant universalframework.
- Min meaningful tests: source/output safeguards, count/attachment/field outcomes on12fixtureitems, clearlicenseimpacts. Existingprototypechecksnotrelevant. Don't broadretestUI.

Genuine OST acquisition:
Only exact task-owned lab OST path supplied/created via lab profile may be processed. No search/reading user's personalOST directory. Once user provides exactlabcopy location, first ensure Outlook writing stopped normally beforecopy; noforcekill. .OST copied to labinput; recordhashandwriterformat/version; run isolatedread->PST no originalprofile access. No uploading mail files.

WORKFLOW:
Existing SOL->officialagyheadlessGemini; no newCodexthreads/subagents or CUA. SOL CLI installs/executes/verifies, Gemini normalcode. Batch boundedwork; use directfiletools ifagyshelldenied. PhaseAready response early, thenB; don't waitforuserbefore independentBpreparation. ROADMAP/coord PROJECT_STATE Astraowns. General behavior no new security approvals; routineprojectlocalsetup allowed. Any material blocker showconcreteevidence/actionneeded, notpermissionspeculation.

DELIVERY:
.codex-coordination/results/TASK-008.md statuses perphase, genuineOSTpresence, exact commands/exits, mailserver image/version/ports/fixtureverification, harnessbuild/results, triallimitations, independentreaderstatus. LabREADME, Outlooksetup, fixturesresultsreport. Do not markwholeexperimentDONE if genuineOST/userstepmissing. Leave loopbacklabaccessible; recordstopcommand. May finish boundedprep with PARTIAL/USER_STEP_REQUIRED whileOSTrequired; don'tfabricatefile.

RESUME 2026-09-12 — GENUINE OST SUPPLIED (continue TASK-008, no new task):
USER: "test ostsi hazır".
EXACT AUTHORIZED INPUT: C:\Users\Eddiz\Documents\ChatGPT\Mail Manager\lab\ost-spike\input\lab@bitigmail.example - BitigMail-Lab.ost
ROOT PREFLIGHT: 16,818,176 bytes; no OUTLOOK process; exclusive read open succeeded; first24bytes 21-42-44-4E-A8-1D-AE-BF-53-4F-24-00-0C-00-01-01-78-00-00-00-58-10-FD-04; SHA256 61ABD7989323E053ACC47635E978E5EFB3F625D5B5B26A59E8B2EB05E6465FA9.

EXECUTION / ACCEPTANCE:
1. Use existing harness on this exact read-only input with NEW output/report paths. No rename/reseed/profile access or original mailbox changes. Confirm true OST clientmagic/version via current format inspection. Source hash before/after must match root baseline.
2. Capture real source folder/message inventory and per-item extraction/failures. Preserve extra Outlook system folders and any client test messages; separate baseline12 from extras. Actual OST folder display paths can differ from corpus names; document evidence-based mapping rather than silently renaming source or assuming 12 is totalOSTcount.
3. Known corpus comparison must cover full decoded subject, addresses, UTCdate, complete body text (not just first line), all4 attachment hashes/names/inlineCID, two physical duplicates and sameID/differentbody pair. Track fixture->OST->PST distinctions and trial changes at each SDK read/write stage. Field not measured = UNKNOWN; content changes = DIFFERENCES, never exactfidelityPASS due to stripwatermark or loose substring.
4. Reopen generated PST, compare whole-source physical counts and baselinefields, empty/system folders separately. SameSDK result explicitly SAME_SDK_ONLY.
5. Attempt independent source/output inspection using libpff pffinfo/pffexport from official source or trusted distro package in isolated Docker. Source task file readonly bind mount, output only lab output subdirectory, no userhome/profile/credentials mounts, no privilege/socket/hostnetwork. Dependency install/download allowed as previous scope. Parse fixture count/headers/fulltext/attachments independently where exported. No untrustedbinary/download/uploadmail. Record packageversion/commands/exits and limitations.
6. Prove account/profile independence where feasible by rerunning same extraction in a clean container with taskinput readonly and ONLY harness/runtime/tool assets mounted, network none during conversion. Prefer existing .NET8 runtime/image official; never claim Windows run with Outlook closed proves Outlook-uninstalled or no profile dependency. If platform prevents cleancontainer run, state exact limit without substituting claims.
7. Gemini implements missing bounded verification/adapter tooling via official agy CLI; SOL runs commands/tests and handles normal compile/runtime failures. No frontend retest, no 100GB or corruption/resume work yet, no license purchase/forms. Source/output guard test only if changed/relevant.
8. Deliver lab/ost-spike/OST_EXPERIMENT_RESULT.md user-readable Turkish result plus machine-readable report. Update existing TASK008 result preserving prior phase evidence. Overall experiment executed can be DONE_WITH_DIFFERENCES if source unchanged and measured scope completed, but candidate acceptance explicitly inconclusive/limited where trial/independentfield check fails. Report genuineconversion success separate from fidelity. ROADMAP and OST_ENGINE_DECISION/PROJECT_STATE root owns.

Report first real OST inventory/conversion result promptly, then finish independent verification. Do not wait for further user confirmation. Input is explicitly task-owned synthetic test data. No CUA/OutlookUI or personal profile access.
