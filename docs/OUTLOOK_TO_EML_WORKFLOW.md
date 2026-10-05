# PST, OST and OLM to EML normalization

Healthy PST, OST and OLM files can be selected through a protected native handle and extracted into a new create-only EML tree. The source is opened read-only and hashed before and after extraction. Physical folder paths and ordinals keep duplicates distinct. Existing Archive, IMAP and EML-to-PST workflows can consume the resulting tree through explicit selection.

PST/OST items are extracted individually through the installed SDK; non-mail items are counted outside the mail result. MAPI-to-MIME output is reparsed with MimeKit and is qualified rather than claimed whole-message byte-exact. Vendor evaluation additions are retained.

OLM first validates a one-to-one join between the SDK and raw catalog using exact folder path plus Internet Message-ID. Subject-only or Message-ID-only matching is forbidden. Original attachment payloads, Content-ID metadata and source XML are retained. The public vendor fixture verifies 22 mail items, 3 excluded non-mail items and 38 original attachment payloads.

OLM source date literals remain in the XML provenance sidecars. A known installed-SDK noon parsing discrepancy means date fidelity is explicitly `DATE_FIDELITY_UNRESOLVED`; no timezone is inferred and no silent correction is performed.

Each emitted item now carries a generic fidelity matrix in the manifest. It compares subject, Message-ID, sender and recipient fields, body/date representation, and attachment payload hashes after reopening the persisted EML. Exact fields, qualified representation differences, and unmeasured MAPI-only fields are separate lists. Trial additions remain visible and are reported as differences rather than removed. For OLM, the original sent/received literals and the emitted SDK date are both recorded; the four known 12-hour diagnostic discrepancies remain qualified and are never treated as timezone evidence.

Source mutation, unsafe paths, folder collisions, and OLM identity cardinality failures abort the whole job without publication. A failure confined to extracting or writing one source item is recorded with its physical ordinal, folder, source identity, exception type, and message. Verified successes may then be atomically published with `partially_completed_with_qualification`; the job is exposed as `partially_completed`, never as full success. The manifest and API report retain the exact failed identities and counts.
