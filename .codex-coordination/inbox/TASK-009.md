TASK_ID: TASK-009
STATUS: READY
FROM: ASTRA HIGH
TO: SOL 5.6 LIMITED
GOAL: Close TASK008 inline CID uncertainty and define body representation acceptance before connecting real OST conversion to UI.
USER: 'harika devam' approves proposed sequence: remaining CID/body verification -> real file selection/preflight -> real PST job/progress/report. This is first bounded subtask, TASK010 integration contract follows from Astra.
INPUT: task-owned lab/ost-spike/input/bitigmail-lab-full.ost, SHA256 B0801758A2E61D4CE6E86799701A81A7A60C38401F73B13C993D94C03A2EE57A. No Outlook/profile/account access. Existingsource/output untouched.
KNOWN: TASK00813/13copy+independentlibpff+networknonecleancontainer,4attachmentsidentical. CID null BOTH source andPST SDK views; notprovenconversionloss. Suspect property accessor/tag Unicode/ANSI distinction or sourceacquisition: investigate exactstoredproperties first, notgeneric reconstruction.
ACCEPTANCE:
- Inspect msg11 source and finalPST attachment property bags (PR_ATTACH_CONTENT_ID ANSI/Unicode, contentlocation, hidden/rendering flags, HTML cid references). Use actual Aspose24.8 API metadata; do not hallucinate API names. Read-only dump only targeted synthetic fields.
- Check if currentextractor wrong type/tag/projection. Fix narrow reader if applicable; preserve original storedbytes. No watermark removal/licensebypass. No claimingunknown=loss.
- Establish HTML cid reference resolves to intended73byte logo.png before/after, fullHTML body not firstline. If reference can'tresolve, implement proven safe preservationonly ifrealconversiondefect; don't inventattachmentmetadata frommanifest. If sourcealreadymissing, report preciseSOURCE_INCOMPLETE and blockthatpathinpreflight ratherthanmanufacture.
- Re-run boundedfull13 known fixture test afterfix withNEW paths; verify4 hashes/duplicates/headers/HTML andbeforeafterinputhash. Existingruntime/independenttoolreuse; don'tnewbigdownload.
- Body policy: primarysource representations preserved (normalizednewlines only); additiveplaintext allowed+reported; EML->OST acquisition changes distinct from conversion losses.
- Document concise docs/CID_BODY_ACCEPTANCE.md + .codex-coordination/results/TASK-009.md withexactoutcome/evidence andusable capability status for integration.
CONSTRAINTS: existingSOL->officialagyGemini no newtasks/subagents/CUA. Gemini normalcode, SOL executes meaningful tests. Root owns overallROADMAP/newintegrationarchitecture. Don'tchangeprototype/serviceyet. Source/customer files untouched. Read-only targetedrawproperty diagnostic before largecode. Avoidprose/researchrunoverhead; closeboundedissue thenacceptTASK010.
