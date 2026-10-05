# Damaged PST/OST recovery — local acceptance boundary

The recovery path opens the selected PST/OST read-only and writes MIME files only into a new per-job output directory. It never repairs in place, invokes Outlook repair tools, scans soft-deleted blocks, or promises undelete/block carving.

Normal folder/message enumeration is attempted first. If a boundary fails, the worker records it and may use the supported `FindSubfolders`/`FindMessages` identifiers. Folder IDs are cycle-guarded and message IDs are deduplicated within one store. Folder, item, depth and deadline limits are enforced. Each message extraction is isolated and failures remain in the result.

The parent owns the exact worker process and accepts output only after successful exit and confirmed termination. Every result is bound to schema version, invocation ID, job ID, source hash and private output root. It rejects stale manifests, changed sources, invalid paths, orphan files and content hash/length mismatches before publishing the new directory.

`healthy_extraction`, `partial_recovered`, `unreadable_source`, `cancelled` and `failed` are separate outcomes. Unknown original totals remain unknown, so an interrupted or unreadable region never receives a fabricated recovery percentage. Trial-license restrictions remain applicable.

Local tests use a known generated fixture and a tiny truncated derivative retained beside an unchanged original. This proves lifecycle and reporting behavior, not broad production recovery. Representative real damaged corpora, licensed-output acceptance and clean-machine acceptance remain pending.
