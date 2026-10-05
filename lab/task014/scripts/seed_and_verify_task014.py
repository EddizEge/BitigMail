#!/usr/bin/env python3
"""
seed_and_verify_task014.py — BitigMail TASK-014 Idempotent Source Seeder & Verifier

Uses ONLY Python standard library to:
1. Connect to loopback IMAP (127.0.0.1:4143) with authenticated credentials.
2. Target account protection: TARGET MAILBOX IS NEVER CLEARED, RESEEDED, OR WRITTEN BY THIS SCRIPT.
3. Source seeding is idempotent and non-destructive:
   - If empty (0 items): seeds all 12 fixtures and verifies.
   - If complete (12 fixtures present and verified): verification-only no-op, succeeds.
   - If partial or conflicting: FAILS CLOSED with exit code 2. NEVER deletes or purges any message.
4. Detect server hierarchy delimiter via IMAP LIST.
5. Explicit logical-to-IMAP mapping:
   - 'Gelen Kutusu' -> 'INBOX'
   - 'Gönderilenler' -> 'Gönderilenler' (RFC 3501 modified UTF-7)
   - 'Projeler/İstanbul' -> 'Projeler/İstanbul' (RFC 3501 modified UTF-7)
6. Preserve all 12 physical fixtures (including duplicates msg-01/02 and shared-ID msg-03/04).
7. Persist UID, UIDVALIDITY, hierarchy delimiter, folder mapping, and verification data to lab/task014/seed-manifest.json.
8. Verify message counts, headers, semantic bodies, and 4 attachment hashes via FETCH.
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
import sys
from typing import Any, Dict, List, Optional, Tuple


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
    """Finds repository root containing fixtures/mail-corpus-v1/manifest.json."""
    candidate = Path.cwd().resolve()
    for parent in [candidate, *candidate.parents]:
        if (parent / "fixtures" / "mail-corpus-v1" / "manifest.json").exists():
            return parent
    return candidate


def load_credentials(cred_path: Path, account: str) -> Tuple[str, str, str, int]:
    """Loads credentials for the specified role ('source' or 'target') from local-credentials.json."""
    if not cred_path.exists():
        raise FileNotFoundError(f"Credential file {cred_path} not found.")

    with cred_path.open("r", encoding="utf-8-sig") as f:
        data = json.load(f)

    if account not in data:
        raise KeyError(f"Account '{account}' not found in {cred_path}.")

    acc_info = data[account]
    username = acc_info.get("username", account)
    password = acc_info.get("password")
    if not password:
        raise ValueError(f"Account '{account}' in {cred_path} has empty password.")

    host = acc_info.get("imapHost", "127.0.0.1")
    port = int(acc_info.get("imapPort", 4143))

    return username, password, host, port


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
    """
    dt = email.utils.parsedate_to_datetime(date_str)
    months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]
    month_name = months[dt.month - 1]

    zone = dt.strftime("%z")
    if not zone:
        zone = "+0000"

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
    parser = argparse.ArgumentParser(description="BitigMail TASK-014 Idempotent Source Seeder & Verifier")
    parser.add_argument("--account", choices=["source", "target"], default="source",
                        help="Account to operate on (default: source; target is verify-only)")
    parser.add_argument("--manifest", type=Path, help="Path to fixtures manifest.json")
    parser.add_argument("--credentials", type=Path, help="Path to local-credentials.json")
    parser.add_argument("--host", default="127.0.0.1", help="IMAP host (must be loopback)")
    parser.add_argument("--port", type=int, default=4143, help="IMAP port (default 4143)")
    parser.add_argument("--output-seed-manifest", type=Path, help="Path to write seed-manifest.json")
    parser.add_argument("--verify-only", action="store_true", help="Only verify without appending")
    args = parser.parse_args()

    repo_root = find_repo_root()
    manifest_path = (args.manifest or (repo_root / "fixtures" / "mail-corpus-v1" / "manifest.json")).resolve()
    cred_path = (args.credentials or (repo_root / "lab" / "task014" / "local-credentials.json")).resolve()
    seed_manifest_path = (args.output_seed_manifest or (repo_root / "lab" / "task014" / "seed-manifest.json")).resolve()

    if not manifest_path.exists():
        print(f"[ERROR] Manifest not found: {manifest_path}", file=sys.stderr)
        return 1

    with manifest_path.open("r", encoding="utf-8-sig") as f:
        manifest = json.load(f)

    # Security check: loopback only
    if args.host not in ("127.0.0.1", "localhost"):
        print(f"[SECURITY ERROR] Host {args.host} is not loopback. Refusing to connect.", file=sys.stderr)
        return 1

    # Strict target protection: Target mailbox must NEVER be seeded, modified, or cleared!
    if args.account == "target" and not args.verify_only:
        print("[PROTECTION ERROR] Target mailbox can NEVER be seeded or modified by this script. Run with --verify-only.", file=sys.stderr)
        return 1

    try:
        user, password, cred_host, cred_port = load_credentials(cred_path, args.account)
    except Exception as e:
        print(f"[ERROR] Failed to load credentials for account '{args.account}': {e}", file=sys.stderr)
        return 1

    host = args.host or cred_host
    port = args.port or cred_port

    print(f"=== BitigMail TASK-014 IMAP Seeder & Verifier ===")
    print(f"Account Role  : {args.account.upper()}")
    print(f"IMAP Host     : {host}:{port}")
    print(f"Login User    : {user}")
    print(f"Manifest      : {manifest_path}")
    print(f"Mode          : {'VERIFY ONLY' if args.verify_only else 'IDEMPOTENT SEED & VERIFY'}")

    try:
        imap = imaplib.IMAP4(host, port)
        typ, resp = imap.login(user, password)
        if typ != "OK":
            print(f"[ERROR] IMAP login failed: {resp}", file=sys.stderr)
            return 1
    except Exception as e:
        print(f"[ERROR] Failed to connect/authenticate to IMAP server: {e}", file=sys.stderr)
        return 1

    # Detect server delimiter
    delim = get_imap_delimiter(imap)
    print(f"IMAP Delimiter: '{delim}'")

    expected_logical_folders = list(manifest.get("folderDistribution", {}).keys())

    # Resolve IMAP mailbox mappings
    resolved_folders: Dict[str, Tuple[str, str]] = {}
    for lf in expected_logical_folders:
        resolved_folders[lf] = resolve_imap_mailbox(lf, delim)
        unenc, enc = resolved_folders[lf]
        print(f"  Mapping: '{lf}' -> IMAP '{unenc}' (encoded: '{enc}')")

    # Check existing message counts across mapped folders
    existing_counts: Dict[str, int] = {}
    for lf in expected_logical_folders:
        _, enc = resolved_folders[lf]
        mailbox_arg = enc if enc.upper() == "INBOX" else f'"{enc}"'
        try:
            typ, data = imap.select(mailbox_arg, readonly=True)
            if typ == "OK":
                existing_counts[lf] = int(data[0].decode())
            else:
                existing_counts[lf] = 0
        except Exception:
            existing_counts[lf] = 0

    total_existing = sum(existing_counts.values())
    expected_total = manifest["totalMessages"]

    print(f"\nCurrent message counts across mapped folders: {existing_counts} (Total: {total_existing})")

    # Idempotent State Machine:
    # 1. Total == 0: Empty mailbox -> Proceed with seeding (if not verify-only)
    # 2. Total == 12: Complete mailbox -> Skip seeding (NO-OP), proceed to verify
    # 3. Otherwise (0 < Total < 12 or Total > 12): Partial or conflicting -> FAIL CLOSED!
    need_seeding = False

    if args.verify_only:
        print("[MODE] Verify-only mode requested. No messages will be appended.")
    elif total_existing == 0:
        print("[IDEMPOTENT CHECK] Mailbox is empty (0 messages). Safe to seed 12 fixtures.")
        need_seeding = True
    elif total_existing == expected_total and all(existing_counts.get(lf) == manifest["folderDistribution"][lf] for lf in expected_logical_folders):
        print(f"[IDEMPOTENT CHECK] Mailbox already contains {expected_total} messages matching folder distribution. Seeding skipped (NO-OP).")
        need_seeding = False
    else:
        print(f"\n[FAIL CLOSED] Mailbox contains unexpected/partial state ({total_existing} messages vs expected 0 or {expected_total}): {existing_counts}", file=sys.stderr)
        print("[FAIL CLOSED] Non-destructive policy: Will NOT delete, purge, or overwrite messages.", file=sys.stderr)
        imap.logout()
        return 2

    # Seeding Phase
    if need_seeding:
        print("\n[SEED] Ensuring folders exist and appending 12 fixtures...")
        # Create and subscribe folders
        for lf in expected_logical_folders:
            unenc, enc = resolved_folders[lf]
            if enc.upper() == "INBOX":
                if imap.subscribe("INBOX")[0] != "OK":
                    raise RuntimeError("Could not subscribe to lab INBOX")
                continue

            parts = enc.split(delim)
            accum = ""
            for p in parts:
                accum = f"{accum}{delim}{p}" if accum else p
                try:
                    imap.create(f'"{accum}"')
                except Exception:
                    pass
                if imap.subscribe(f'"{accum}"')[0] != "OK":
                    raise RuntimeError(f"Could not subscribe to lab mailbox '{accum}'")

        # Append all 12 fixtures
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
    actual_total = 0

    for logical_folder, exp_count in manifest.get("folderDistribution", {}).items():
        unenc_name, enc_name = resolved_folders[logical_folder]
        mailbox_arg = enc_name if enc_name.upper() == "INBOX" else f'"{enc_name}"'

        try:
            typ, sel_data = imap.select(mailbox_arg, readonly=True)
            if typ != "OK":
                count = 0
            else:
                count = int(sel_data[0].decode())
        except Exception:
            count = 0
        actual_total += count

        # Query UIDVALIDITY
        typ, status_data = imap.status(mailbox_arg, "(UIDVALIDITY UIDNEXT)")
        status_str = status_data[0].decode() if status_data and status_data[0] else ""
        uv_match = re.search(r"UIDVALIDITY\s+(\d+)", status_str)
        uid_validity = int(uv_match.group(1)) if uv_match else None

        folder_meta[logical_folder] = {
            "imapMailbox": unenc_name,
            "imapEncodedName": enc_name,
            "expectedCount": exp_count,
            "actualCount": count,
            "uidValidity": uid_validity,
            "countMatches": (count == exp_count),
        }

        if count == 0:
            continue

        # Fetch UID and full message body for all items in folder
        typ, fetch_data = imap.uid("FETCH", "1:*", "(UID RFC822.SIZE INTERNALDATE BODY.PEEK[])")
        if typ != "OK":
            raise RuntimeError(f"FETCH failed for mailbox {mailbox_arg}: {fetch_data}")

        unmatched_fixtures = [m for m in manifest["messages"] if m["folder"] == logical_folder]

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

                # Multiset candidate matching
                candidates = [m for m in unmatched_fixtures if m["messageId"] == msg_id]
                matching_manifest_item = None

                if len(candidates) == 1:
                    matching_manifest_item = candidates[0]
                elif len(candidates) > 1:
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
                            norm_note = f"Unclassified byte difference: original {len(original_bytes)}B vs fetched {len(body_bytes)}B"
                        normalization_notes.append(f"{fid}: {norm_note}")

                    headers_match = (
                        subj_decoded == matching_manifest_item["subject"]
                        and decode_mime_header(parsed.get("From", "")) == matching_manifest_item["from"]
                        and decode_mime_header(parsed.get("To", "")) == matching_manifest_item["to"]
                        and email.utils.parsedate_to_datetime(date_hdr) == email.utils.parsedate_to_datetime(matching_manifest_item["dateRfc"])
                    )

                    expected_body_text = matching_manifest_item["bodyText"].replace("\r\n", "\n").strip()
                    body_matches = (body_text == expected_body_text)

                    # Attachment verification (including inline CID logo.png)
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
                            att_verified_for_msg = False
                        else:
                            for exp_att in expected_attachments:
                                matched_att = next((a for a in actual_attachments if a["sha256"] == exp_att["sha256"]), None)
                                if not matched_att:
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
                    client_extra_items.append({
                        "logicalFolder": logical_folder,
                        "imapMailbox": unenc_name,
                        "uid": uid,
                        "messageId": msg_id,
                        "subject": subj_decoded,
                        "dateRfc": date_hdr,
                    })
            i += 1

    imap.logout()

    verified_items.sort(key=lambda x: x["fixtureId"])

    unique_uids = {(v["imapMailbox"], v["uidValidity"], v["uid"]) for v in verified_items
                   if v.get("uid") is not None and v.get("uidValidity") is not None}
    all_fixtures_verified = (len(verified_items) == expected_total)
    all_uids_distinct = (len(unique_uids) == expected_total)
    all_bodies_match = all(item["bodyMatches"] for item in verified_items)
    all_headers_match = all(item["headersMatch"] for item in verified_items)
    all_attachments_match = (attachments_verified_count == 4) and all(item["attachmentsVerified"] for item in verified_items)

    overall_pass = all_fixtures_verified and all_uids_distinct and all_bodies_match and all_headers_match and all_attachments_match

    report = {
        "account": args.account,
        "loginUsername": user,
        "host": host,
        "imapPort": port,
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

    if args.account == "source":
        seed_manifest_path.parent.mkdir(parents=True, exist_ok=True)
        with seed_manifest_path.open("w", encoding="utf-8") as f:
            json.dump(report, f, indent=2, ensure_ascii=False)
        print(f"\n[MANIFEST] Saved verification report to {seed_manifest_path}")

    print("\n=== Verification Summary ===")
    print(f"Account           : {args.account.upper()}")
    print(f"Baseline Messages : {len(verified_items)} / {expected_total} verified")
    print(f"Attachments       : {attachments_verified_count} / 4 verified")
    print(f"Bodies Match      : {all_bodies_match}")
    print(f"Headers Match     : {all_headers_match}")
    print(f"UIDs Distinct     : {all_uids_distinct}")
    print(f"Final Status      : {'PASS' if overall_pass else 'FAIL'}")

    return 0 if overall_pass else 1


if __name__ == "__main__":
    sys.exit(main())
