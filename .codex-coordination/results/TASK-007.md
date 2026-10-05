TASK_ID: TASK-007
STATUS: DONE
EXECUTOR: GEMINI
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- scripts/generate_mail_corpus.py
- fixtures/mail-corpus-v1/README.md
- fixtures/mail-corpus-v1/manifest.json
- fixtures/mail-corpus-v1/corpus.mbox
- fixtures/mail-corpus-v1/eml/msg-01.eml
- fixtures/mail-corpus-v1/eml/msg-02.eml
- fixtures/mail-corpus-v1/eml/msg-03.eml
- fixtures/mail-corpus-v1/eml/msg-04.eml
- fixtures/mail-corpus-v1/eml/msg-05.eml
- fixtures/mail-corpus-v1/eml/msg-06.eml
- fixtures/mail-corpus-v1/eml/msg-07.eml
- fixtures/mail-corpus-v1/eml/msg-08.eml
- fixtures/mail-corpus-v1/eml/msg-09.eml
- fixtures/mail-corpus-v1/eml/msg-10.eml
- fixtures/mail-corpus-v1/eml/msg-11.eml
- fixtures/mail-corpus-v1/eml/msg-12.eml
- .codex-coordination/results/TASK-007.md
SUMMARY:
- Gemini implemented a deterministic, Python-standard-library-only synthetic mail corpus generator and validator.
- The delivered corpus has exactly 12 EML files and 11 unique raw byte sequences: msg-01/msg-02 are byte-identical; msg-03/msg-04 share a Message-ID but have different content.
- It covers the three required Turkish logical folders, fixed 2022-2025 dates, three timezone offsets, Turkish Unicode, plain/HTML MIME, safe HTML, and four deterministic attachments including UTF-8 filename text, binary data, valid PNG, and inline CID PNG.
- Explicit mboxrd serialization preserves msg-08 body lines beginning with `From ` and `>From ` while keeping corpus.mbox parseable as exactly 12 messages.
- The UTC 2024 oracle contains msg-03, msg-04, msg-05, msg-06, msg-07, msg-08, and msg-12. The msg-11 local/UTC boundary case is correctly excluded.
VERIFICATION:
- Gemini execution: Antigravity CLI, model gemini-3.8-flash-high, conversation 79b860a2-ac04-48be-a84e-d18f6f472cb5; final implementation status SUCCESS.
- `python scripts\generate_mail_corpus.py --output-dir C:\Users\Eddiz\AppData\Local\Temp\bitigmail-task007-verification\run-a` -> exit 0; all 13 checks PASS.
- Independent run-b with the same command shape -> exit 0; all 13 checks PASS.
- Recursive SHA-256 comparison: 14 files versus 14 files, HASH_DIFFS=0.
- Official fixture regeneration `python scripts\generate_mail_corpus.py --output-dir fixtures\mail-corpus-v1` -> exit 0; all 13 checks PASS.
- Official generated artifacts versus run-a: 14 files, OFFICIAL_HASH_DIFFS=0.
- Independent controller checks: EML_COUNT=12; NON_EXAMPLE_ADDRESSES=0; real generated payload size=40,121 bytes; README preserved.
- Validator evidence: 12 parseable EML with zero MIME defects; 11 unique hashes; required duplicate and shared-ID pairs; folder distribution 5/3/4; four attachments; msg-11 UTC boundary; both hard-coded filter oracles; raw MBOX `>From `/`>>From ` escaping; exactly 12 parsed MBOX messages; msg-08 semantic round-trip; all four MBOX attachment hashes and sizes.
- LAB NOT RUN. Mail/Auth/API/Outlook NOT RUN. No account or external mail service was configured.
RISKS:
- The corpus is synthetic EML/MBOX, not a valid OST/PST, and does not prove OST conversion, damaged-file recovery, or 100GB-scale behavior.
- Third-party MBOX readers may implement a different From_ dialect; the included validator proves the documented stdlib/mboxrd behavior only.
UNCERTAINTIES:
- A real Outlook import/sync/closed-copy/orphan OST laboratory run remains necessary for OST/PST capability and scale evidence.
