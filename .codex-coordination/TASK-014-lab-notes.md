# TASK-014 Lab Architecture & Preparation Findings

**Date**: 2026-09-13  
**Target Audience**: Astra  
**Scope**: TASK-014 Independent Lab Preparation Only  
**Product Status**: No product code changes made. No lab scripts implemented yet.

---

## 1. Executive Summary & Preparation Scope

This document details the architecture, design findings, and preparation requirements for the independent **TASK-014** GreenMail lab environment. The lab provides a dedicated, isolated test harness supporting two authenticated accounts for end-to-end migration and synchronization workflows without impacting the previous spike lab (`lab/ost-spike`) or any existing test fixtures and product code.

---

## 2. Deterministic Host Evidence

The following deterministic host state and verification metrics were established:

* **Docker Environment**:
  * Docker client and engine version: `29.5.3`.
  * Active context: `desktop-linux`.
* **Container Image**:
  * Pinned image: `greenmail/standalone:2.1.12@sha256:9f32971b4f25d32b4de6fa2e297423768441c65e4541f6aecd7631c890a229a7`.
  * Image status: Verified present in local Docker image cache. `docker image inspect` resolves deterministically to this exact digest.
* **Old Lab State (`lab/ost-spike`)**:
  * Container `bitigmail-lab-mail` is stopped.
  * Port bindings for the old container were mapped strictly to `127.0.0.1:3025` (SMTP) and `127.0.0.1:3143` (IMAP).
* **Candidate Host Ports for TASK-014**:
  * Loopback port `127.0.0.1:4025` (SMTP) is free and available.
  * Loopback port `127.0.0.1:4143` (IMAP) is free and available.
* **Gemini CLI Tooling Canary**:
  * Execution time: 16.6 seconds.
  * Result: Exit code `0`, exact file `TASK014_ACCESS_OK` present, JSON response indicates success, execution log present.

---

## 3. Recommended Lab Architecture

To maintain strict isolation and prevent regressions against prior lab configurations:

1. **Strictly Separate Container & Namespace**:
   * Deploy a dedicated container named specifically for TASK-014 (e.g., `bitigmail-task014-mail` or `task014-lab-mail`).
   * Do not reuse or share container names, state directories, credential files, or seed manifests with `lab/ost-spike`.
   * Pinned image must remain identical: `greenmail/standalone:2.1.12@sha256:9f32971b4f25d32b4de6fa2e297423768441c65e4541f6aecd7631c890a229a7`.
2. **Two Authenticated Accounts**:
   * GreenMail standalone must be configured with two discrete user accounts (e.g., a source account `source@bitigmail.example` and a target account `target@bitigmail.example`).
   * Passwords must be generated cryptographically at initialization and stored strictly in a gitignored local file (e.g., `lab/task014/task014-credentials.json`).
3. **Loopback-Only Network Binding**:
   * Published host ports must be restricted strictly to loopback IP `127.0.0.1`:
     * Host `127.0.0.1:4025` -> Container `3025` (SMTP capture-only).
     * Host `127.0.0.1:4143` -> Container `3143` (IMAP service).
   * Status scripts must enforce loopback binding validation (`HostIp == "127.0.0.1"`), aborting on wildcard (`0.0.0.0`) or missing bindings.
4. **Hard Security & Isolation Boundaries**:
   * Disallow privileged mode (`--privileged`).
   * Disallow host networking (`--net=host`).
   * Disallow mounting Docker daemon sockets (`/var/run/docker.sock` or `//./pipe/docker_engine`).
   * SMTP service must remain capture-only (no external relay).

---

## 4. Corpus, Folders & Fixture Integrity

The lab must preserve complete fidelity with the canonical test corpus defined in `fixtures/mail-corpus-v1/manifest.json`:

* **12 Physical Fixture Messages**:
  * Preserve all 12 items, including boundary edge cases:
    * `msg-01` and `msg-02`: Byte-identical duplicate messages.
    * `msg-03` and `msg-04`: Distinct messages sharing an identical `Message-ID`.
* **Attachment Validations**:
  * Preserve validation of all 4 attachments across the corpus, specifically including the inline CID image (`image/png`, `logo.png` referenced via `cid:`).
* **Turkish Folder Mappings**:
  * `Gelen Kutusu` -> maps to standard `INBOX`.
  * `Gönderilenler` -> maps to RFC 3501 modified UTF-7 `Gönderilenler` (`G&APY-nderilenler`).
  * `Projeler/İstanbul` -> maps to hierarchical folder with server delimiter, UTF-7 encoded `Projeler<delim>&ATA-stanbul`.
  * Subscriptions: Parent and leaf folders must be explicitly subscribed via IMAP `SUBSCRIBE` for client discovery.

---

## 5. Idempotent Seeding & Non-Destructive Operations

### Why the Old `--reseed` Flag is Unsuitable
The implementation in `lab/ost-spike/scripts/seed_and_verify_mailbox.py` handled reseeding via:
```python
imap.store(num, "+FLAGS", "\\Deleted")
imap.expunge()
```
This marked all messages as `\Deleted` and immediately issued an IMAP `EXPUNGE`, permanently purging the mailbox. In a migration scenario involving source and target mailboxes, expunging existing mailboxes introduces severe data-loss risks, invalidates target verification, and breaks idempotent pipelines.

### New Seeding Design Principles
Seeding must operate idempotently based on **fixture identity** rather than brute-force clearing:
1. **Empty Source Mailbox**:
   * If the source mailbox has 0 items across the expected folders, seed all 12 fixtures once and verify.
2. **Complete Source Mailbox**:
   * If the source mailbox already contains the expected 12 fixtures (verified by headers, semantic body, attachments, and UIDVALIDITY), the seeding script performs a verification-only **no-op** and succeeds.
3. **Partial or Conflicting Source Mailbox**:
   * If the source mailbox contains an unexpected message count, partial fixtures, or conflicting UID sets, the seeder must **fail closed** with a diagnostic error.
   * Under no circumstance may it delete, wipe, or overwrite existing source messages.
4. **Target Mailbox Protection**:
   * The target mailbox is **never cleared, reseeded, or deleted**.
   * It must remain untouched prior to migration runs and evaluated strictly via non-destructive verification tools.

---

## 6. GreenMail Two-User Configuration & Smoke Test Requirement

In GreenMail Standalone, user configuration is traditionally passed via the Java property `greenmail.users`. However:
* Syntax variations exist across releases and container run configurations (e.g., comma-delimited strings `-Dgreenmail.users=user1:pass1@domain,user2:pass2@domain` versus whitespace or repeated parameters).
* Login identifier behavior (login by short username `user1` vs full address `user1@domain`) must be strictly verified.

> [!IMPORTANT]
> **Isolated Smoke Test Requirement**:  
> Before finalizing the implementation scripts or locking the contract spec, a short, isolated smoke test must be executed against the standalone container to verify the exact multi-user syntax and confirm successful IMAP authentication for both users. We must not invent certainty regarding GreenMail's exact parser behavior without empirical verification.

---

## 7. Proposed Scripts, Files & Directory Structure

To maintain a clean separation from `lab/ost-spike/`, the recommended namespace is `lab/task014/`:

```
lab/
└── task014/
    ├── scripts/
    │   ├── lab_mail_start.ps1        # Starts dedicated container (ports 4025/4143, 2 users)
    │   ├── lab_mail_status.ps1       # Probes loopback bindings & IMAP/SMTP protocol banners
    │   ├── lab_mail_stop.ps1         # Stops/removes TASK-014 container
    │   └── seed_and_verify_task014.py# Idempotent, non-destructive source seeder & verifier
    ├── local-credentials.json        # Gitignored two-user credentials
    └── seed-manifest.json            # Generated verification state, UIDs, and folder metadata
```

### Script Specifications:
1. **`lab_mail_start.ps1`**:
   * Resolves repository root.
   * Generates or loads two cryptographically random passwords in `local-credentials.json`.
   * Starts container `bitigmail-task014-mail` on `127.0.0.1:4025:3025` and `127.0.0.1:4143:3143`.
   * Validates SMTP (`220`) and IMAP (`* OK`) banners within a 45-second bounded loop.
2. **`lab_mail_status.ps1`**:
   * Inspects `bitigmail-task014-mail`.
   * Verifies port bindings are strictly `127.0.0.1` (exits with error code 2 if exposed externally).
   * Tests socket connection and non-blank GreenMail protocol banners.
   * Confirms local credential file presence without printing secrets.
3. **`lab_mail_stop.ps1`**:
   * Stops container `bitigmail-task014-mail`.
   * Accepts `-Remove` switch to remove container.
4. **`seed_and_verify_task014.py`**:
   * Standard library only (no external pip dependencies).
   * Supports targeting specific account (source vs target).
   * Implements the idempotent fail-closed logic detailed in Section 5.
   * Writes detailed verification manifest with UIDs, folder counts, attachment hashes, and client extra logs.

---

## 8. Verification Gates

Prior to declaring TASK-014 lab readiness, the following gates must pass:

* **Gate 1: Container Security & Port Isolation**  
  Container starts with pinned digest; inspect confirms `HostIp == "127.0.0.1"` for ports 4025 and 4143; banner probes return `220` and `* OK`.
* **Gate 2: Two-Account IMAP Authentication**  
  IMAP `LOGIN` succeeds independently on port 4143 for both Account A (Source) and Account B (Target).
* **Gate 3: Source Seeding Idempotency**  
  First run against empty source seeds 12 fixtures; second run verifies and cleanly no-ops; running against a manually altered folder triggers fail-closed error without data deletion.
* **Gate 4: Corpus & Attachment Integrity**  
  All 12 items verified (including duplicates and shared IDs); 4 attachments verified by SHA-256; Turkish folder hierarchies correctly mapped and subscribed.
* **Gate 5: Target Protection Verification**  
  Ensures no script or verification routine attempts `EXPUNGE` or deletion on the target mailbox.

---

## 9. Risks and Uncertainties

1. **GreenMail Standalone Multiple Users Syntax**:
   * Risk: The `-Dgreenmail.users` parameter parser might handle separators (commas/spaces) inconsistently or have edge cases with complex generated passwords.
   * Mitigation: Run the smoke test immediately during lab implementation before locking configuration.
2. **Port Collisions**:
   * Risk: Host ports 4025 or 4143 could be claimed by another local service before container spin-up.
   * Mitigation: Start and status scripts must verify port availability prior to execution and fail early if occupied.
3. **Hierarchy Delimiter Differences**:
   * Risk: While GreenMail standalone default is `/`, client or server configuration shifts could affect folder creation.
   * Mitigation: Dynamically query server hierarchy delimiter via `IMAP4.list('""', '""')` as done in `seed_and_verify_mailbox.py`.

---

## 10. Status Declaration

* **Product Code Status**: No product files, profiles, or runtime code have been altered.
* **Existing Lab Status**: `lab/ost-spike/` scripts, configuration, and result files remain completely untouched.
* **Current Step Complete**: Architecture and preparation findings recorded. Ready for review and execution planning.
