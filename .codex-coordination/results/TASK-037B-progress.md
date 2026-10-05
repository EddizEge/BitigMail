TASK_ID: TASK-037B
STATUS: IN_PROGRESS
EXECUTOR: SOL 5.6 LIMITED (AUTHORIZED ANTIGRAVITY FAILURE EXCEPTION)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.LocalHost/Archive/ArchiveSelectedJobService.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.Stage7.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.Recovery.cs
- engine/BitigMail.LocalHost/Jobs/LocalJobRecord.cs
- engine/BitigMail.LocalHost/Jobs/JobManager.cs
SUMMARY:
- Added selected archive-result preview and real queued EML export endpoints.
- Preview resolves every selected result through the existing scoped server search verifier, freezes physical IDs/raw hashes/owner/query, requires explicit mapping for every selected logical folder and snapshots the duplicate policy.
- Start revalidates owner and every selected raw hash, dispatches through the shared global queue, applies content/metadata duplicate policy, writes new EML files, verifies their hashes and persists mapping/dedup/selection fingerprints plus skipped physical IDs.
VERIFICATION:
- Current full backend regression: 656/656 PASS (`task037b-current-full.trx`).
RISKS:
- Dedicated real archive fixture/output oracle, result report endpoint shape and rendered Archive Search selection UI are not complete; TASK037B is not DONE.
UNCERTAINTIES:
- Mapping/dedup execution has compiled and preserved regression but has not yet received its required end-to-end file/UI acceptance.
