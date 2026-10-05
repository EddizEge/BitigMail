#!/usr/bin/env python3
"""
seed_and_verify_mailbox.py — BitigMail Test Mailbox Seeder and IMAP Verifier (TASK-008 Phase A)

Uses ONLY Python standard library to:
1. Connect to loopback IMAP (127.0.0.1:3143) with authenticated credentials (login 'lab').
2. Guard against silent duplication on restart (requires explicit --reseed to clear/re-populate).
3. Detect server hierarchy delimiter via IMAP LIST.
4. Enforce explicit logical-to-IMAP mapping:
   - 'Gelen Kutusu' -> 'INBOX' (no separate 'Gelen Kutusu' mailbox is created)
   - 'Gönderilenler' -> 'Gönderilenler' (modified UTF-7: 'G&APY-nderilenler')
   - 'Projeler/İstanbul' -> 'Projeler/İstanbul' (modified UTF-7: 'Projeler<delim>&ATA-stanbul')
5. Seed exactly 12 synthetic fixtures from fixtures/mail-corpus-v1 using IMAP APPEND with preserved dates.
6. Preserve all 12 physical items including byte-duplicate pair (msg-01/02) and shared-ID pair (msg-03/04).
7. Persist UID, UIDVALIDITY, hierarchy delimiter, and folder mapping to lab/ost-spike/seed-manifest.json.
8. Verify message counts, headers, semantic bodies, and 4 attachment hashes via FETCH.
9. Ignore unrelated/empty client folders in the 3-source-folder/12-message oracle.
10. Record server normalization differences if any.
"""

from __future__ import annotations

import argparse
import base64
import email
from email import policy
import email.header
import email.utils
import hashlib
import imaplib
import json
import os
from pathlib import Path
import re
import secrets
import sys
from typing import Any, Dict, List, Optional, Tuple


# Explicit mapping from corpus logical folders to IMAP mailbox names
# Astra requirement: Gelen Kutusu -> INBOX; do NOT create a separate Gelen Kutusu mailbox
LOGICAL_TO_IMAP_MAP: Dict[str, str] = {
    "Gelen Kutusu": "INBOX",
    "Gönderilenler": "Gönderilenler",
    "Projeler/İstanbul": "Projeler/İstanbul",
}


def encode_imap_utf7(s: str) -> str:
    """Encodes a folder name according to RFC 3501 Section 5.1.3 (modified UTF-7)."""
    res: List[str] = []
    in_b64 = False
    b64_buffer: List[str] = []

    for char in s:
        code = ord(char)
        # Direct characters: ASCII 0x20 to 0x7E except '&'
        if 0x20 <= code <= 0x7E:
            if in_b64:
                raw_bytes = "".join(b64_buffer).encode("utf-16be")
                b64_str = base64.b64encode(raw_bytes).decode("ascii").rstrip("=").replace("/", ",")
                res.append(f"&{b64_str}-")
                in_b64 = False
                b64_buffer = []
            if char == "&":
                res.append("&-")
            else:
                res.append(char)
        else:
            in_b64 = True
            b64_buffer.append(char)

    if in_b64:
        raw_bytes = "".join(b64_buffer).encode("utf-16be")
        b64_str = base64.b64encode(raw_bytes).decode("ascii").rstrip("=").replace("/", ",")
        res.append(f"&{b64_str}-")

    return "".join(res)


def decode_imap_utf7(s: str) -> str:
    """Decodes an RFC 3501 modified UTF-7 encoded mailbox name."""
    res: List[str] = []
    parts = re.split(r"(&[^-]*-)", s)
    for part in parts:
        if part.startswith("&") and part.endswith("-"):
            if part == "&-":
                res.append("&")
            else:
                b64_str = part[1:-1].replace(",", "/")
                padding = "=" * ((4 - len(b64_str) % 4) % 4)
                raw_bytes = base64.b64decode((b64_str + padding).encode("ascii"))
                res.append(raw_bytes.decode("utf-16be"))
        else:
            res.append(part)
    return "".join(res)


def decode_mime_header(val: str) -> str:
    """Decodes RFC 2047 encoded MIME header to plain string."""
    if not val:
        return ""
    try:
        decoded_parts = email.header.decode_header(val)
        res = []
        for part, encoding in decoded_parts:
            if isinstance(part, bytes):
                enc = encoding or "utf-8"
                res.append(part.decode(enc, errors="replace"))
            else:
                res.append(str(part))
        return "".join(res).strip()
    except Exception:
        return str(val).strip()


def find_repo_root() -> Path:
    """Finds the repository root containing fixtures/mail-corpus-v1/manifest.json."""
    candidate = Path.cwd().resolve()
    for parent in [candidate, *candidate.parents]:
        if (parent / "fixtures" / "mail-corpus-v1" / "manifest.json").exists():
            return parent
    return candidate


def load_or_create_credentials(cred_path: Path) -> Dict[str, Any]:
    """Loads credentials or generates random local secret without echoing."""
    if cred_path.exists():
        with cred_path.open("r", encoding="utf-8") as f:
            data = json.load(f)
            if not data.get("password"):
                raise ValueError(f"Credential file {cred_path} has empty password.")
            return data

    # Generate new random secret
    secret = secrets.token_urlsafe(24)
    data = {
        "email": "lab@bitigmail.example",
        "username": "lab",
        "password": secret,
        "smtpHost": "127.0.0.1",
        "smtpPort": 3025,
        "imapHost": "127.0.0.1",
        "imapPort": 3143,
        "containerName": "bitigmail-lab-mail",
    }
    cred_path.parent.mkdir(parents=True, exist_ok=True)
    with cred_path.open("w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
    return data


def get_imap_delimiter(imap: imaplib.IMAP4) -> str:
    """Queries IMAP LIST to discover the server's hierarchy delimiter."""
    try:
        typ, data = imap.list('""', '""')
        if typ == "OK" and data and data[0]:
            raw_str = data[0].decode("ascii", errors="replace") if isinstance(data[0], bytes) else str(data[0])
            m = re.search(r'\(([^)]*)\)\s+"?([^"\s]+)"?\s+', raw_str)
            if m and m.group(2) != 'NIL':
                return m.group(2)
    except Exception:
        pass

    try:
        typ, data = imap.list()
        if typ == "OK" and data:
            for item in data:
                if isinstance(item, bytes):
                    raw_str = item.decode("ascii", errors="replace")
                    m = re.search(r'\(([^)]*)\)\s+"?([^"\s]+)"?\s+', raw_str)
                    if m and m.group(2) != 'NIL':
                        return m.group(2)
    except Exception:
        pass

    return "/"


def resolve_imap_mailbox(logical_folder: str, delim: str) -> Tuple[str, str]:
    """
    Resolves logical corpus folder to (unencoded_imap_name, encoded_imap_name).
    - 'Gelen Kutusu' -> ('INBOX', 'INBOX')
    - 'Gönderilenler' -> ('Gönderilenler', 'G&APY-nderilenler')
    - 'Projeler/İstanbul' -> ('Projeler<delim>İstanbul', 'Projeler<delim>&ATA-stanbul')
    """
    target = LOGICAL_TO_IMAP_MAP.get(logical_folder, logical_folder)
    if target.upper() == "INBOX":
        return "INBOX", "INBOX"

    parts = target.split("/")
    server_unencoded = delim.join(parts)
    encoded_parts = [encode_imap_utf7(p) for p in parts]
    server_encoded = delim.join(encoded_parts)

    return server_unencoded, server_encoded


def rfc_date_to_internaldate(date_str: str) -> str:
    """
    Converts an RFC 2822 date string into RFC 3501 date-time format for IMAP INTERNALDATE.
    Format: "DD-Mon-YYYY HH:MM:SS +ZZZZ"
    Preserves exact fixture instant and timezone offset without losing gmtoff semantics.
    Avoids Python 3.12 imaplib.Time2Internaldate(dt.timetuple()) TypeError.
    """
    dt = email.utils.parsedate_to_datetime(date_str)
    months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]
    month_name = months[dt.month - 1]

    # Timezone offset formatted as +HHMM or -HHMM
    zone = dt.strftime("%z")
    if not zone:
        zone = "+0000"

    # RFC 3501 Section 9 date-day-fixed: 2DIGIT or (SP DIGIT)
    return f'"{dt.day:02d}-{month_name}-{dt.year:04d} {dt.hour:02d}:{dt.minute:02d}:{dt.second:02d} {zone}"'


def extract_body_text(msg: email.message.EmailMessage) -> str:
    """Extracts plain text body from EmailMessage, normalizing line endings."""
    if msg.is_multipart():
        for part in msg.walk():
            ctype = part.get_content_type()
            cdisp = str(part.get("Content-Disposition", ""))
            if ctype == "text/plain" and "attachment" not in cdisp:
                payload = part.get_payload(decode=True)
                charset = part.get_content_charset() or "utf-8"
                text = payload.decode(charset, errors="replace")
                return text.replace("\r\n", "\n").strip()
    else:
        payload = msg.get_payload(decode=True)
        charset = msg.get_content_charset() or "utf-8"
        text = payload.decode(charset, errors="replace")
        return text.replace("\r\n", "\n").strip()
    return ""


def main() -> int:
    parser = argparse.ArgumentParser(description="BitigMail IMAP Seeder & Verifier")
    parser.add_argument("--manifest", type=Path, help="Path to fixtures manifest.json")
    parser.add_argument("--credentials", type=Path, help="Path to local-credentials.json")
    parser.add_argument("--host", default="127.0.0.1", help="IMAP host (must be loopback)")
    parser.add_argument("--port", type=int, default=3143, help="IMAP port (default 3143)")
    parser.add_argument("--output-seed-manifest", type=Path, help="Path to write seed-manifest.json")
    parser.add_argument("--verify-only", action="store_true", help="Only verify without appending")
    parser.add_argument("--reseed", action="store_true", help="Explicitly clear and re-seed owned mailbox")
    args = parser.parse_args()
    if args.verify_only and args.reseed:
        parser.error("--verify-only and --reseed cannot be combined")

    repo_root = find_repo_root()
    manifest_path = (args.manifest or (repo_root / "fixtures" / "mail-corpus-v1" / "manifest.json")).resolve()
    cred_path = (args.credentials or (repo_root / "lab" / "ost-spike" / "local-credentials.json")).resolve()
    seed_manifest_path = (args.output_seed_manifest or (repo_root / "lab" / "ost-spike" / "seed-manifest.json")).resolve()

    if not manifest_path.exists():
        print(f"[ERROR] Manifest not found: {manifest_path}", file=sys.stderr)
        return 1

    with manifest_path.open("r", encoding="utf-8") as f:
        manifest = json.load(f)

    # Security check: loopback only
    if args.host not in ("127.0.0.1", "localhost"):
        print(f"[SECURITY ERROR] Host {args.host} is not loopback. Refusing to connect.", file=sys.stderr)
        return 1

    creds = load_or_create_credentials(cred_path)
    raw_user = creds.get("username", "lab")
    # In GreenMail, user syntax is login:password@domain; login identifier is 'lab'
    user = raw_user.split("@")[0] if "@" in raw_user else raw_user
    email_addr = creds.get("email", "lab@bitigmail.example")
    password = creds["password"]

    print("=== BitigMail IMAP Seeder & Verifier ===")
    print(f"IMAP Host     : {args.host}:{args.port}")
    print(f"Login User    : {user} (Email: {email_addr})")
    print(f"Manifest      : {manifest_path}")
    print(f"Mode          : {'VERIFY ONLY' if args.verify_only else ('RESEED' if args.reseed else 'SEED')}")

    try:
        imap = imaplib.IMAP4(args.host, args.port)
        imap.login(user, password)
    except Exception as e:
        print(f"[ERROR] Failed to connect/authenticate to IMAP server: {e}", file=sys.stderr)
        return 1

    # Detect server delimiter
    delim = get_imap_delimiter(imap)
    print(f"IMAP Delimiter: '{delim}'")

    expected_logical_folders = list(manifest.get("folderDistribution", {}).keys())
    # Expected: ["Gelen Kutusu", "Gönderilenler", "Projeler/İstanbul"]

    # Resolve IMAP mailbox mappings
    resolved_folders: Dict[str, Tuple[str, str]] = {}
    for lf in expected_logical_folders:
        resolved_folders[lf] = resolve_imap_mailbox(lf, delim)
        unenc, enc = resolved_folders[lf]
        print(f"  Mapping: '{lf}' -> IMAP '{unenc}' (encoded: '{enc}')")

    # Check existing folder messages for anti-duplication safeguard
    existing_counts: Dict[str, int] = {}
    for lf in expected_logical_folders:
        _, enc = resolved_folders[lf]
        mailbox_arg = enc if enc.upper() == "INBOX" else f'"{enc}"'
        typ, data = imap.select(mailbox_arg, readonly=args.verify_only)
        if typ == "OK":
            existing_counts[lf] = int(data[0].decode())
        else:
            existing_counts[lf] = 0

    total_existing = sum(existing_counts.values())

    if total_existing > 0 and not args.verify_only and not args.reseed:
        print(f"\n[SAFEGUARD REJECTION] Mailbox already contains {total_existing} messages across mapped folders: {existing_counts}")
        print("Restart cannot silently duplicate data. Run with --verify-only or pass --reseed to clear and re-populate the owned lab mailbox.")
        imap.logout()
        return 2

    if total_existing == 0 and not args.verify_only:
        print(f"\n[SAFEGUARD PASS] Mailbox contains 0 messages across mapped folders ({existing_counts}).")
        print("Safe to proceed with seeding without requiring --reseed (rerun after folder creation is supported).")

    # If --reseed is specified, purge messages in the 3 mapped lab mailboxes only
    if args.reseed:
        print(f"\n[RESEED] Clearing {total_existing} existing messages from owned lab mailboxes...")
        for lf in expected_logical_folders:
            _, enc = resolved_folders[lf]
            mailbox_arg = enc if enc.upper() == "INBOX" else f'"{enc}"'
            typ, _ = imap.select(mailbox_arg)
            if typ == "OK":
                typ, search_data = imap.search(None, "ALL")
                if typ == "OK" and search_data[0]:
                    msg_nums = search_data[0].split()
                    for num in msg_nums:
                        imap.store(num, "+FLAGS", "\\Deleted")
                    imap.expunge()
        print("[RESEED] Mailboxes cleared.")

    # Seeding Phase (if not verify-only)
    if not args.verify_only:
        print("\n[SEED] Ensuring folders exist and appending 12 fixtures...")
        # Create non-INBOX folders
        for lf in expected_logical_folders:
            unenc, enc = resolved_folders[lf]
            if enc.upper() == "INBOX":
                if imap.subscribe("INBOX")[0] != "OK":
                    raise RuntimeError("Could not subscribe to lab INBOX")
                continue  # INBOX always exists

            # For hierarchical folders like Projeler/İstanbul, ensure parent exists first
            parts = enc.split(delim)
            accum = ""
            for p in parts:
                accum = f"{accum}{delim}{p}" if accum else p
                try:
                    typ, res = imap.create(f'"{accum}"')
                    if typ == "OK":
                        print(f"  Created IMAP mailbox: '{accum}'")
                    else:
                        print(f"  IMAP mailbox already exists or verified: '{accum}'")
                except Exception:
                    pass
                # Outlook can show subscribed folders only. Include each lab
                # parent and leaf so nested fixtures are discoverable on first sync.
                if imap.subscribe(f'"{accum}"')[0] != "OK":
                    raise RuntimeError(f"Could not subscribe to lab mailbox '{accum}'")

        # Append messages
        for msg_info in manifest["messages"]:
            fid = msg_info["fixtureId"]
            logical_folder = msg_info["folder"]
            eml_rel = msg_info["emlPath"]
            eml_path = (repo_root / "fixtures" / "mail-corpus-v1" / eml_rel).resolve()

            if not eml_path.exists():
                raise FileNotFoundError(f"Fixture file missing: {eml_path}")

            with eml_path.open("rb") as ef:
                raw_bytes = ef.read()

            date_str = msg_info["dateRfc"]
            internal_date = rfc_date_to_internaldate(date_str)
            _, enc_mailbox = resolved_folders[logical_folder]
            mailbox_arg = enc_mailbox if enc_mailbox.upper() == "INBOX" else f'"{enc_mailbox}"'

            typ, res = imap.append(mailbox_arg, "(\\Seen)", internal_date, raw_bytes)
            if typ != "OK":
                raise RuntimeError(f"Failed to APPEND {fid} to {logical_folder} ({mailbox_arg}): {res}")

        print("[SEED] Successfully appended all 12 fixtures.")

    # Verification Phase
    print("\n[VERIFY] Fetching and verifying server state against manifest...")
    folder_meta: Dict[str, Any] = {}
    verified_items: List[Dict[str, Any]] = []
    client_extra_items: List[Dict[str, Any]] = []
    normalization_notes: List[str] = []
    attachments_verified_count = 0

    expected_total = manifest["totalMessages"]
    actual_total = 0

    # Oracle strictly checks the 3 mapped folders. Unrelated client/Outlook folders are ignored.
    for logical_folder, expected_count in manifest.get("folderDistribution", {}).items():
        unenc_name, enc_name = resolved_folders[logical_folder]
        mailbox_arg = enc_name if enc_name.upper() == "INBOX" else f'"{enc_name}"'

        typ, sel_data = imap.select(mailbox_arg, readonly=True)
        if typ != "OK":
            raise RuntimeError(f"Could not SELECT mailbox {mailbox_arg} for '{logical_folder}': {sel_data}")

        count = int(sel_data[0].decode())
        actual_total += count

        # Get UIDVALIDITY
        typ, status_data = imap.status(mailbox_arg, "(UIDVALIDITY UIDNEXT)")
        status_str = status_data[0].decode() if status_data and status_data[0] else ""
        uv_match = re.search(r"UIDVALIDITY\s+(\d+)", status_str)
        uid_validity = int(uv_match.group(1)) if uv_match else None

        folder_meta[logical_folder] = {
            "imapMailbox": unenc_name,
            "imapEncodedName": enc_name,
            "expectedCount": expected_count,
            "actualCount": count,
            "uidValidity": uid_validity,
            "countMatches": (count == expected_count),
        }

        if count != expected_count:
            print(f"[COUNT DIFFERENCE] Logical folder '{logical_folder}' (IMAP '{unenc_name}'): expected baseline {expected_count}, got {count}; checking baseline and extras separately")

        # Fetch UID and full message body for all items in folder
        typ, fetch_data = imap.uid("FETCH", "1:*", "(UID RFC822.SIZE INTERNALDATE BODY.PEEK[])")
        if typ != "OK":
            raise RuntimeError(f"FETCH failed for mailbox {mailbox_arg}: {fetch_data}")

        # Prepare multiset matching pool of expected manifest fixtures for this logical folder
        unmatched_fixtures = [m for m in manifest["messages"] if m["folder"] == logical_folder]

        # Parse fetched items
        i = 0
        while i < len(fetch_data):
            item = fetch_data[i]
            if isinstance(item, tuple) and len(item) == 2:
                header_bytes = item[0]
                body_bytes = item[1]

                header_str = header_bytes.decode("ascii", errors="replace")
                uid_match = re.search(r"UID\s+(\d+)", header_str)
                uid = int(uid_match.group(1)) if uid_match else None

                parsed = email.message_from_bytes(body_bytes, policy=policy.compat32)
                msg_id = parsed.get("Message-ID", "").strip()
                subj_raw = parsed.get("Subject", "").strip()
                subj_decoded = decode_mime_header(subj_raw)
                date_hdr = parsed.get("Date", "").strip()
                body_text = extract_body_text(parsed)

                # Physical multiset matching:
                # Find all candidate fixtures in this folder sharing this Message-ID
                candidates = [m for m in unmatched_fixtures if m["messageId"] == msg_id]
                matching_manifest_item = None

                if len(candidates) == 1:
                    matching_manifest_item = candidates[0]
                elif len(candidates) > 1:
                    # Disambiguate by content / subject / body (e.g. msg-03 vs msg-04)
                    for cand in candidates:
                        cand_subj = cand.get("subject", "").strip()
                        if cand_subj and (cand_subj == subj_decoded or cand_subj in subj_decoded or subj_decoded in cand_subj):
                            matching_manifest_item = cand
                            break
                    if not matching_manifest_item:
                        for cand in candidates:
                            cand_body = cand.get("bodyText", "").strip()
                            if cand_body and (cand_body in body_text or body_text in cand_body):
                                matching_manifest_item = cand
                                break
                    # If still not disambiguated (e.g. byte-identical duplicates msg-01 vs msg-02):
                    # Deterministically assign the first item from the multimap queue
                    if not matching_manifest_item:
                        matching_manifest_item = candidates[0]

                if matching_manifest_item:
                    unmatched_fixtures.remove(matching_manifest_item)
                    fid = matching_manifest_item["fixtureId"]
                    fetched_sha256 = hashlib.sha256(body_bytes).hexdigest()
                    orig_sha256 = matching_manifest_item["rawSha256"]
                    is_byte_identical = (fetched_sha256 == orig_sha256)

                    norm_note = ""
                    if not is_byte_identical:
                        original_bytes = (manifest_path.parent / matching_manifest_item["emlPath"]).read_bytes()
                        if original_bytes.replace(b"\r\n", b"\n") == body_bytes.replace(b"\r\n", b"\n"):
                            norm_note = "Verified line-ending-only difference (LF/CRLF)"
                        else:
                            norm_note = f"Unclassified byte difference: original {len(original_bytes)}B vs fetched {len(body_bytes)}B; semantic checks reported separately"
                        normalization_notes.append(f"{fid}: {norm_note}")

                    headers_match = (
                        subj_decoded == matching_manifest_item["subject"]
                        and decode_mime_header(parsed.get("From", "")) == matching_manifest_item["from"]
                        and decode_mime_header(parsed.get("To", "")) == matching_manifest_item["to"]
                        and email.utils.parsedate_to_datetime(date_hdr) == email.utils.parsedate_to_datetime(matching_manifest_item["dateRfc"])
                    )

                    # Verify semantic body
                    expected_body_text = matching_manifest_item["bodyText"].replace("\r\n", "\n").strip()
                    body_matches = (body_text == expected_body_text)
                    if not body_matches:
                        print(f"[FAIL] Semantic body mismatch on {fid}!")

                    # Verify attachments (4 total across corpus including inline CID PNG)
                    att_verified_for_msg = True
                    expected_attachments = matching_manifest_item.get("attachments", [])
                    actual_attachments = []
                    for part in parsed.walk():
                        cdisp = str(part.get("Content-Disposition", ""))
                        filename = part.get_filename()
                        cid = part.get("Content-Id")
                        if filename or "attachment" in cdisp or (cid and "image" in part.get_content_type()):
                            payload = part.get_payload(decode=True)
                            if payload is not None:
                                att_hash = hashlib.sha256(payload).hexdigest()
                                actual_attachments.append({
                                    "filename": filename,
                                    "size": len(payload),
                                    "sha256": att_hash,
                                    "isInline": bool(cid and "attachment" not in cdisp),
                                })

                    if expected_attachments or actual_attachments:
                        if len(actual_attachments) != len(expected_attachments):
                            print(f"[FAIL] Attachment count mismatch on {fid}: expected {len(expected_attachments)}, got {len(actual_attachments)}")
                            att_verified_for_msg = False
                        else:
                            for exp_att in expected_attachments:
                                matched_att = next((a for a in actual_attachments if a["sha256"] == exp_att["sha256"]), None)
                                if not matched_att:
                                    print(f"[FAIL] Attachment hash missing on {fid}: {exp_att['filename']}")
                                    att_verified_for_msg = False
                                else:
                                    attachments_verified_count += 1

                    verified_items.append({
                        "fixtureId": fid,
                        "logicalFolder": logical_folder,
                        "imapMailbox": unenc_name,
                        "uid": uid,
                        "uidValidity": uid_validity,
                        "messageId": msg_id,
                        "subject": subj_decoded,
                        "dateRfc": date_hdr,
                        "originalSha256": orig_sha256,
                        "fetchedSha256": fetched_sha256,
                        "isByteIdentical": is_byte_identical,
                        "normalizationNote": norm_note,
                        "bodyMatches": body_matches,
                        "headersMatch": headers_match,
                        "attachmentsVerified": att_verified_for_msg,
                    })
                else:
                    # Post-baseline extra items (e.g. automated test email from Outlook)
                    # Astra requirement: report extras separately, do not hide/delete or call loss
                    client_extra_items.append({
                        "logicalFolder": logical_folder,
                        "imapMailbox": unenc_name,
                        "uid": uid,
                        "messageId": msg_id,
                        "subject": subj_decoded,
                        "dateRfc": date_hdr,
                    })
                    print(f"  [CLIENT-EXTRA] Post-baseline item detected: UID {uid}, ID {msg_id}, Subj '{subj_decoded}'")

            i += 1

    imap.logout()

    # Sort verified items by fixtureId
    verified_items.sort(key=lambda x: x["fixtureId"])

    unique_uids = {(v["imapMailbox"], v["uidValidity"], v["uid"]) for v in verified_items
                   if v.get("uid") is not None and v.get("uidValidity") is not None}
    all_fixtures_verified = (len(verified_items) == expected_total)
    all_uids_distinct = (len(unique_uids) == expected_total)
    all_bodies_match = all(item["bodyMatches"] for item in verified_items)
    all_headers_match = all(item["headersMatch"] for item in verified_items)
    all_attachments_match = (attachments_verified_count == 4) and all(item["attachmentsVerified"] for item in verified_items)

    overall_pass = all_fixtures_verified and all_uids_distinct and all_bodies_match and all_headers_match and all_attachments_match

    # Output seed manifest
    seed_report = {
        "labMailbox": email_addr,
        "loginUsername": user,
        "host": args.host,
        "imapPort": args.port,
        "hierarchyDelimiter": delim,
        "logicalToImapMapping": LOGICAL_TO_IMAP_MAP,
        "baselineMessagesVerified": len(verified_items),
        "expectedTotal": expected_total,
        "folderMeta": folder_meta,
        "attachmentsVerifiedCount": attachments_verified_count,
        "allFixturesVerified": all_fixtures_verified,
        "allUidsDistinct": all_uids_distinct,
        "allBodiesMatch": all_bodies_match,
        "allHeadersMatch": all_headers_match,
        "allAttachmentsMatch": all_attachments_match,
        "clientExtraCount": len(client_extra_items),
        "clientExtraItems": client_extra_items,
        "normalizationNotes": normalization_notes,
        "verifiedItems": verified_items,
        "status": "PASS" if overall_pass else "FAIL",
    }

    seed_manifest_path.parent.mkdir(parents=True, exist_ok=True)
    with seed_manifest_path.open("w", encoding="utf-8") as f:
        json.dump(seed_report, f, indent=2, ensure_ascii=False)

    print("\n=== Verification Summary ===")
    print(f"Baseline Messages : {len(verified_items)} / {expected_total} verified across 3 mapped folders")
    for lf, meta in folder_meta.items():
        print(f"  {lf} -> IMAP '{meta['imapMailbox']}': actual {meta['actualCount']} (expected baseline {meta['expectedCount']}, UIDVALIDITY: {meta['uidValidity']})")
    print(f"Mailbox UID identities: {len(unique_uids)} / {expected_total} unique (mailbox + UIDVALIDITY + UID)")
    print(f"Duplicates (01/02): Mapped to distinct UIDs {[i['uid'] for i in verified_items if i['fixtureId'] in ('msg-01', 'msg-02')]}")
    print(f"Shared ID (03/04) : Mapped to distinct UIDs {[i['uid'] for i in verified_items if i['fixtureId'] in ('msg-03', 'msg-04')]}")
    print(f"Attachments       : {attachments_verified_count} / 4 verified (including inline CID PNG)")
    print(f"Semantic Bodies   : {'ALL MATCH' if all_bodies_match else 'MISMATCH'}")
    if client_extra_items:
        print(f"Client Extras     : {len(client_extra_items)} post-baseline extra items recorded separately (not loss)")
    else:
        print("Client Extras     : 0 post-baseline items")
    print(f"Normalizations    : {len(normalization_notes)} items recorded with server normalization")
    print(f"Saved Manifest    : {seed_manifest_path}")
    print(f"Final Status      : {'PASS' if overall_pass else 'FAIL'}")

    return 0 if overall_pass else 1



if __name__ == "__main__":
    sys.exit(main())
