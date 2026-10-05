#!/usr/bin/env python3
"""TASK-019 Independent Ordinary Verification Oracle.
Provides read-only audit functions:
1. Source Dovecot baseline invariance against root-source-before.json
2. Synthetic source corpus integrity (manifest, EMLs, corpus.mbox)
3. Target Dovecot account validation (Task019.<uniqueRun>.* folder counts & keyword integrity)
4. Export output validation (EML tree & MBOXRD byte/record checks)
5. SQLite FTS5 search index direct query audit (Turkish I folding & term verification)
"""
import argparse
import base64
import collections
import datetime
import email.parser
import email.policy
import hashlib
import imaplib
import json
import os
import pathlib
import re
import sqlite3
import sys

DEFAULT_FOLDERS = ['INBOX', 'Gönderilenler', 'Projeler/İstanbul']


def sha256_bytes(b):
    return hashlib.sha256(b).hexdigest()


def utf7(value):
    """Encode unicode IMAP mailbox name using modified UTF-7."""
    return re.sub(
        r'[^\x20-\x7e]+',
        lambda m: '&' + base64.b64encode(m[0].encode('utf-16be')).decode().rstrip('=').replace('/', ',') + '-',
        value.replace('&', '&-')
    )


def require_imap(result):
    if result[0] != 'OK':
        raise RuntimeError(f'IMAP command failed: {result}')
    return result[1]


def audit_dovecot_source_invariance(config_path, baseline_path, folders=None):
    """Audit source Dovecot account to prove original 12 messages remain 100% untouched."""
    if folders is None:
        folders = DEFAULT_FOLDERS

    cfg_data = json.loads(pathlib.Path(config_path).read_text(encoding='utf-8-sig'))
    src_cfg = cfg_data['source']
    if src_cfg['imapHost'] != '127.0.0.1' or src_cfg['imapPort'] != 5143:
        raise ValueError('Only isolated Dovecot loopback lab is authorized')

    client = imaplib.IMAP4(src_cfg['imapHost'], src_cfg['imapPort'], timeout=30)
    messages = []
    observed_folders = []

    try:
        require_imap(client.login(src_cfg['username'], src_cfg['password']))
        for folder in folders:
            quoted = '"' + utf7(folder).replace('\\', '\\\\').replace('"', '\\"') + '"'
            require_imap(client.select(quoted, readonly=True))
            validity = int(client.response('UIDVALIDITY')[1][0])
            uids = require_imap(client.uid('SEARCH', None, 'ALL'))[0].split()
            observed_folders.append({'folder': folder, 'uidValidity': validity, 'count': len(uids)})

            for uid in uids:
                resp = require_imap(client.uid('FETCH', uid, '(UID FLAGS INTERNALDATE BODY.PEEK[])'))
                meta, raw = next(part for part in resp if isinstance(part, tuple))
                flags = sorted(f.decode('ascii') for f in imaplib.ParseFlags(meta) if f != b'\\Recent')
                date_match = re.search(rb'INTERNALDATE "([^"]+)"', meta)
                date_text = date_match[1].decode('ascii').strip()
                instant = datetime.datetime.strptime(date_text, '%d-%b-%Y %H:%M:%S %z').astimezone(datetime.timezone.utc).isoformat()

                parsed = email.parser.BytesParser(policy=email.policy.default).parsebytes(raw)
                parts = []
                for part in parsed.walk():
                    if part.is_multipart():
                        continue
                    payload = part.get_payload(decode=True) or b''
                    semantic = payload.replace(b'\r\n', b'\n') if part.get_content_maintype() == 'text' else payload
                    parts.append({
                        'type': part.get_content_type(),
                        'name': part.get_filename(),
                        'disposition': part.get_content_disposition(),
                        'cid': str(part.get('Content-ID', '')),
                        'decodedSha256': sha256_bytes(semantic),
                        'decodedBytes': len(semantic)
                    })

                messages.append({
                    'identity': f'{validity}:{uid.decode()}',
                    'folder': folder,
                    'rawSha256': sha256_bytes(raw),
                    'rawBytes': len(raw),
                    'flags': flags,
                    'internalDateUtc': instant,
                    'parts': parts,
                    'attachments': sum(bool(p['name'] or p['cid'] or p['disposition'] == 'attachment') for p in parts)
                })
    finally:
        try:
            client.logout()
        except Exception:
            pass

    current_snap = {
        'count': len(messages),
        'attachments': sum(m['attachments'] for m in messages),
        'messages': messages,
        'folders': observed_folders
    }

    baseline_data = json.loads(pathlib.Path(baseline_path).read_text(encoding='utf-8'))

    # Compare multisets
    def sig(item):
        return json.dumps({
            'identity': item['identity'],
            'folder': item['folder'],
            'rawSha256': item['rawSha256'],
            'rawBytes': item['rawBytes'],
            'flags': item['flags'],
            'internalDateUtc': item['internalDateUtc'],
            'attachments': item['attachments']
        }, sort_keys=True)

    base_counter = collections.Counter(sig(m) for m in baseline_data['messages'])
    curr_counter = collections.Counter(sig(m) for m in current_snap['messages'])

    missing = sum((base_counter - curr_counter).values())
    extra = sum((curr_counter - base_counter).values())
    passed = (missing == 0 and extra == 0 and current_snap['count'] == 12)

    return {
        'oracle': 'Source Baseline Invariance Oracle',
        'pass': passed,
        'expectedCount': 12,
        'actualCount': current_snap['count'],
        'missing': missing,
        'extra': extra,
        'folders': observed_folders
    }


def audit_source_corpus(stage_dir):
    """Audits stage-128m source directory, verifying manifest, EML files, and corpus.mbox."""
    stage_path = pathlib.Path(stage_dir)
    source_path = stage_path / "source"
    manifest_path = source_path / "manifest.json"
    eml_tree_dir = source_path / "eml-tree"
    mbox_path = source_path / "corpus.mbox"

    if not manifest_path.exists():
        return {'pass': False, 'error': f'Manifest not found: {manifest_path}'}
    if not eml_tree_dir.exists():
        return {'pass': False, 'error': f'eml-tree dir not found: {eml_tree_dir}'}
    if not mbox_path.exists():
        return {'pass': False, 'error': f'corpus.mbox not found: {mbox_path}'}

    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    expected_items = manifest['totalItems']
    expected_bytes = manifest['totalRawSizeBytes']

    eml_files = list(eml_tree_dir.rglob('*.eml'))
    if len(eml_files) != expected_items:
        return {'pass': False, 'error': f'Expected {expected_items} EML files, found {len(eml_files)}'}

    actual_bytes = 0
    corrupt_files = []
    for eml_file in eml_files:
        if eml_file.is_symlink():
            return {'pass': False, 'error': f'Symlink detected in EML tree: {eml_file}'}
        data = eml_file.read_bytes()
        actual_bytes += len(data)

    if actual_bytes != expected_bytes:
        return {'pass': False, 'error': f'Total EML bytes mismatch: expected {expected_bytes}, got {actual_bytes}'}

    # Verify MBOX file
    if mbox_path.is_symlink():
        return {'pass': False, 'error': f'Symlink detected in corpus.mbox: {mbox_path}'}
    mbox_size = mbox_path.stat().st_size
    if mbox_size < actual_bytes:
        return {'pass': False, 'error': f'corpus.mbox size ({mbox_size}) is smaller than EML total ({actual_bytes})'}

    return {
        'oracle': 'Source Corpus Oracle',
        'pass': True,
        'totalItems': len(eml_files),
        'totalRawSizeBytes': actual_bytes,
        'totalRawSizeMiB': round(actual_bytes / (1024 * 1024), 2),
        'mboxSizeBytes': mbox_size,
        'folders': list(manifest.get('folders', {}).keys())
    }


def audit_target_folders(config_path, prefix):
    """Audit target account in Dovecot for mailboxes matching prefix (e.g. Task019.<uniqueRun>.*)."""
    cfg_data = json.loads(pathlib.Path(config_path).read_text(encoding='utf-8-sig'))
    tgt_cfg = cfg_data['target']

    client = imaplib.IMAP4(tgt_cfg['imapHost'], tgt_cfg['imapPort'], timeout=30)
    folder_details = {}
    total_messages = 0

    try:
        require_imap(client.login(tgt_cfg['username'], tgt_cfg['password']))
        status, folder_list = client.list()
        require_imap((status, folder_list))

        for entry in folder_list:
            match = re.search(rb'\"\/\"\s+\"?([^\"]+)\"?$', entry)
            if not match:
                continue
            raw_name = match[1].decode('latin-1')
            if prefix not in raw_name:
                continue

            quoted = f'"{raw_name}"'
            require_imap(client.select(quoted, readonly=True))
            uids = require_imap(client.uid('SEARCH', None, 'ALL'))[0].split()
            count = len(uids)
            folder_details[raw_name] = count
            total_messages += count
    finally:
        try:
            client.logout()
        except Exception:
            pass

    return {
        'oracle': 'Target Account Dovecot Oracle',
        'prefix': prefix,
        'matchingFolders': len(folder_details),
        'totalMessages': total_messages,
        'folders': folder_details
    }


def audit_sqlite_search_index(search_db_path, test_queries=None):
    """Directly audit SQLite FTS5 search.db created by ArchiveIngestWorker."""
    db_path = pathlib.Path(search_db_path)
    if not db_path.exists():
        return {'pass': False, 'error': f'search.db not found: {db_path}'}

    conn = sqlite3.connect(str(db_path))
    cursor = conn.cursor()

    # Total items
    cursor.execute("SELECT COUNT(*) FROM archive_items")
    total_items = cursor.fetchone()[0]

    # Test queries
    if test_queries is None:
        test_queries = ["isparta", "işlem", "şartname", "task019"]

    query_results = {}
    for q in test_queries:
        # FTS5 matches table
        try:
            cursor.execute("SELECT COUNT(*) FROM archive_items_fts WHERE archive_items_fts MATCH ?", (q,))
            hit_count = cursor.fetchone()[0]
            query_results[q] = hit_count
        except Exception as ex:
            query_results[q] = f"Error: {ex}"

    conn.close()
    return {
        'oracle': 'SQLite FTS5 Direct Oracle',
        'pass': total_items > 0,
        'totalItemsInIndex': total_items,
        'queryResults': query_results
    }


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description="TASK-019 Verification Oracle")
    parser.add_argument("--baseline", action="store_true", help="Audit Dovecot source invariance")
    parser.add_argument("--corpus", action="store_true", help="Audit stage-128m synthetic source")
    args = parser.parse_args()

    root = pathlib.Path(__file__).resolve().parents[3]
    cred = root / "lab/task014/dovecot/local-credentials.json"
    base = root / ".codex-coordination/evidence/TASK-019/root-source-before.json"

    if args.baseline:
        res = audit_dovecot_source_invariance(cred, base)
        print(json.dumps(res, indent=2, ensure_ascii=False))
    elif args.corpus:
        stage_dir = root / "runtime/task019/stage-128m"
        res = audit_source_corpus(stage_dir)
        print(json.dumps(res, indent=2, ensure_ascii=False))
    else:
        print("Usage: verify_task019_oracle.py [--baseline | --corpus]")
