# Apple Mail EMLX normalization

BitigMail accepts native-selected `.emlx` files or a directory tree through an in-memory protected handle. API callers cannot submit arbitrary filesystem paths.

The explicit normalization job produces a fresh EML directory tree. Every physical source file receives a stable six-digit ordinal in its output name, so duplicate messages and case-aliased names remain separate. Source folder hierarchy is retained with filesystem-safe names. Existing source and output files are never overwritten.

Before writing and again before atomic publication, the job verifies each source file's size and SHA-256 against the immutable preview manifest. Each EML output is the exact byte segment declared by the EMLX prefix, is parsed again with MimeKit, and has its attachment count compared. Apple plist bytes are written unchanged as `.apple.plist` sidecars. A JSON manifest records source paths, source/output/metadata hashes, physical ordinals and qualification.

## Supported direction

| Source | Output | Status |
|---|---|---|
| Complete `.emlx` files/tree | Verified EML tree | Supported |
| `.partial.emlx` | — | Explicit blocker |
| Apple external attachment stores (`Attachments`, `.emlxpart`) | — | Not supported; explicit blocker |
| EML tree | Archive / IMAP / PST workflows | Select the generated output in the existing workflow |

## Metadata qualification

Raw MIME is preserved byte-for-byte. Apple plist metadata is preserved as opaque sidecar bytes only. Flags, internal dates, Apple-specific identifiers, and external attachment references are not interpreted or silently propagated into IMAP/PST fields.
