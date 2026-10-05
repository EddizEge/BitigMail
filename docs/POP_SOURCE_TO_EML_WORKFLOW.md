# POP source-only snapshot to EML

POP is a separate source-only adapter. Its account IDs, encrypted account envelopes, endpoints and UI are distinct from IMAP; IMAP OAuth authorization is never reused or represented as POP authorization. Production accounts require TLS-on-connect or STARTTLS. Plain transport is accepted only by the owned loopback testing profile.

Preview records the account metadata version and an ordered UIDL + LIST-size snapshot. Missing or duplicate UIDLs, count mismatch, reorder, addition, removal or size change block the run. The message count is bounded before requesting the bulk UIDL/LIST data. Every RETR is preceded by a fresh snapshot comparison and a final comparison occurs before publication. The worker has no DELE operation; QUIT only disconnects the session, so messages remain on the server.

Each message is handled independently and is rejected above the 64 MiB LIST/stream guard. The retrieved stream is parsed as MIME, written create-only, reopened, and SHA-256 compared. The hash describes bytes returned by RETR after POP transport handling; it is not claimed to be the provider's mailbox-on-disk representation. The current MailKit `GetStreamAsync` implementation may buffer internally before the local bounded copy, so this is not a proven hard allocation ceiling; the production-scale allocation benchmark remains a later gate.

Verified output is held under a deterministic hidden partial directory with an atomic UIDL-to-file journal. The immutable plan is persisted. After interruption or process restart, resuming reloads both, rechecks every saved file hash, reconnects, requires the original UIDL/LIST mapping, and continues. Final publication is an atomic directory move into a new target. Unsafe/reparse output chains and overwrites are rejected.

POP has one synthetic source folder. RFC `Date` is preserved as a message field, while server internal received date, read flag and folder hierarchy are explicitly unavailable and never invented. The responsive UI labels the operation “POP kaynağından al” and states that messages stay on the server.
