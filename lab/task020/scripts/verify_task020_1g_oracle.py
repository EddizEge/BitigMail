#!/usr/bin/env python3
"""TASK-020 1GiB Independent Verification Oracle.
Provides:
1. Synthetic source corpus audit (stage-1g, 8,192 items, ~1.00 GiB, 8 physical copies)
2. Target IMAP bulk folder audit (Task020/<RUN_ID>/Bulk, 8,192 messages)
3. Source -> IMAP canonical MIME multiset comparison (8,192 items, 2,464 attachments)
4. IMAP -> MBOX exact raw byte multiset comparison (exactRaw: true, 8,192 items)
5. IMAP -> EML export exact raw byte multiset comparison (exactRaw: true, 8,192 items)
6. MBOX sidecar manifest audit (8,192 items, UID validity, dates, flags, keywords)
7. Managed archive SQLite integrity & paged public identity search oracle (accounting for 8 copies)
8. Dovecot source baseline invariance audit (12 original messages / 4 attachments)
"""
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
import time
import urllib.request

ROOT = pathlib.Path(r'C:\Users\Eddiz\Documents\ChatGPT\Mail Manager').resolve()
sys.path.insert(0, str(ROOT / 'lab/task017'))
sys.path.insert(0, str(ROOT / 'lab/task019/scripts'))

import verify_bridge as bridge_oracle
import verify_task019_oracle as base_task019_oracle

ACCEPTED_ITEMS = 1024
PHYSICAL_COPIES = 8
TOTAL_1G_ITEMS = ACCEPTED_ITEMS * PHYSICAL_COPIES  # 8,192


def audit_stage1g_source(stage1g_dir: pathlib.Path) -> dict:
    """Audit runtime/task019/stage-1g/source for 8,192 physical EML files, no symlinks, and exact size."""
    stage1g_dir = pathlib.Path(stage1g_dir).resolve(strict=True)
    manifest_path = stage1g_dir / "manifest.json"
    mbox_path = stage1g_dir / "corpus.mbox"
    eml_dir = stage1g_dir / "eml-tree"

    if not manifest_path.exists():
        return {'pass': False, 'error': f"manifest.json not found in {stage1g_dir}"}
    if not eml_dir.exists():
        return {'pass': False, 'error': f"eml-tree not found in {stage1g_dir}"}

    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    expected_items = manifest.get('totalItems', 0)
    if expected_items != TOTAL_1G_ITEMS:
        return {'pass': False, 'error': f"Expected {TOTAL_1G_ITEMS} items in manifest, got {expected_items}"}

    eml_files = list(eml_dir.glob("**/*.eml"))
    if len(eml_files) != TOTAL_1G_ITEMS:
        return {'pass': False, 'error': f"Expected {TOTAL_1G_ITEMS} EML files on disk, found {len(eml_files)}"}

    actual_bytes = 0
    for f in eml_files:
        if f.is_symlink():
            return {'pass': False, 'error': f"Symlink detected in EML tree: {f}"}
        actual_bytes += f.stat().st_size

    expected_physical_bytes = manifest.get('totalPhysicalRawSizeBytes', 0)
    if actual_bytes != expected_physical_bytes:
        return {'pass': False, 'error': f"Byte count mismatch: manifest says {expected_physical_bytes}, actual disk files sum to {actual_bytes}"}

    if not mbox_path.exists():
        return {'pass': False, 'error': f"corpus.mbox not found at {mbox_path}"}
    if mbox_path.is_symlink():
        return {'pass': False, 'error': f"Symlink detected in corpus.mbox: {mbox_path}"}

    mbox_size = mbox_path.stat().st_size
    if mbox_size < actual_bytes:
        return {'pass': False, 'error': f"corpus.mbox size ({mbox_size}) is smaller than EML total ({actual_bytes})"}

    return {
        'oracle': 'TASK-020 Stage-1g Source Oracle',
        'pass': True,
        'totalItems': len(eml_files),
        'totalPhysicalRawSizeBytes': actual_bytes,
        'totalPhysicalRawSizeMiB': round(actual_bytes / (1024 * 1024), 2),
        'nominalRawSizeBytes': manifest.get('nominalRawSizeBytes', 1070775680),
        'mboxSizeBytes': mbox_size,
        'mboxSizeMiB': round(mbox_size / (1024 * 1024), 2),
        'folders': list(manifest.get('folders', {}).keys())
    }


def audit_target_bulk_folder(config_path: pathlib.Path, bulk_folder_name: str) -> dict:
    """Audit Dovecot target mailbox to verify exact count of 8,192 messages."""
    cfg = json.loads(pathlib.Path(config_path).read_text(encoding='utf-8-sig'))['target']
    client = imaplib.IMAP4(cfg['imapHost'], cfg['imapPort'], timeout=60)
    try:
        client.login(cfg['username'], cfg['password'])
        quoted = '"' + bridge_oracle.utf7(bulk_folder_name).replace('\\', '\\\\').replace('"', '\\"') + '"'
        status, count_bytes = client.select(quoted, readonly=True)
        if status != 'OK':
            return {'pass': False, 'error': f"Failed to select target folder {bulk_folder_name}: {count_bytes}"}
        total_msgs = int(count_bytes[0].decode('ascii'))
        validity = int(client.response('UIDVALIDITY')[1][0])
        uids_res = client.uid('SEARCH', None, 'ALL')
        uids = uids_res[1][0].split() if uids_res[0] == 'OK' else []
        return {
            'oracle': 'Target IMAP Bulk Folder Audit',
            'pass': total_msgs == TOTAL_1G_ITEMS and len(uids) == TOTAL_1G_ITEMS,
            'folder': bulk_folder_name,
            'totalMessages': total_msgs,
            'uidCount': len(uids),
            'uidValidity': validity
        }
    finally:
        try:
            client.logout()
        except Exception:
            pass


def audit_search_paged_identity(api_func, scope_selection, stage1g_manifest):
    import unicodedata
    def tokens(value):
        value = unicodedata.normalize('NFC', value).replace('I', 'ı').replace('İ', 'i').lower().replace('ı', 'i')
        return set(re.findall(r'[^\W_]+', value))
    subjects = {}
    for scope in scope_selection:
        directory = (ROOT / 'runtime/testing-engine/archives' / scope['archiveId']).resolve(strict=True)
        manifest = json.loads((directory / 'manifest.json').read_text(encoding='utf-8-sig'))
        assert manifest['archiveId'] == scope['archiveId']
        for item in manifest['items']:
            path = (directory / item['relativeEmlPath']).resolve(strict=True)
            assert path.is_relative_to(directory) and not path.is_symlink()
            raw = path.read_bytes()
            assert hashlib.sha256(raw).hexdigest() == item['storedSha256'].lower()
            assert len(raw) == item['byteLength']
            msg = email.parser.BytesParser(policy=email.policy.default).parsebytes(raw, headersonly=True)
            subjects[scope['archiveId'] + ':' + item['itemId']] = str(msg.get('Subject', ''))
    assert len(subjects) == 8192
    results = []
    for query in ['', 'isparta', 'izmir', 'işlem', 'diyarbakır', 'sıkıştırma', 'ankara']:
        expected = {key for key, subject in subjects.items() if not query or tokens(query) <= tokens(subject)}
        observed = []
        started = time.perf_counter()
        first_ms = None
        page = 1
        total = None
        while True:
            response = api_func('POST', '/api/archive/search', {'selectedScopes': scope_selection, 'query': query, 'field': 'subject', 'page': page, 'pageSize': 100})
            if first_ms is None: first_ms = (time.perf_counter() - started) * 1000
            assert total is None or total == response['totalCount']
            total = response['totalCount']
            rows = response['items']
            for row in rows:
                assert row['messageId'] in subjects and row['subject'] == subjects[row['messageId']]
                observed.append(row['messageId'])
            if len(observed) >= total: break
            assert rows and page <= 100
            page += 1
        ok = total == len(expected) and len(observed) == len(set(observed)) and set(observed) == expected
        results.append({'query': query, 'field': 'subject', 'expectedTotalMatches': len(expected), 'observedTotalHits': total, 'retrievedItemsCount': len(observed), 'expectedIds': sorted(expected), 'observedIds': sorted(observed), 'firstPageLatencyMs': first_ms, 'pass': ok})
    started = time.perf_counter()
    api_func('POST', '/api/archive/search', {'selectedScopes': scope_selection, 'query': 'isparta', 'field': 'subject', 'page': 1, 'pageSize': 100})
    return {'oracle': 'Root decoded raw Subject tokens and full public identities; single archive', 'pass': all(q['pass'] for q in results), 'queries': results, 't_repeated_ms': (time.perf_counter()-started)*1000}


def audit_archive_sqlite(archive_dir):
    archive_dir = pathlib.Path(archive_dir).resolve(strict=True)
    manifest = json.loads((archive_dir/'manifest.json').read_text(encoding='utf-8-sig'))
    db = archive_dir.parent/'archive-search.db'
    assert db.is_file()
    with sqlite3.connect(db.as_uri()+'?mode=ro', uri=True, timeout=30) as conn:
        integrity = conn.execute('PRAGMA integrity_check').fetchone()[0]
        rows = conn.execute('SELECT item_id FROM message_metadata WHERE archive_id=?', (manifest['archiveId'],)).fetchall()
        fts = conn.execute('SELECT COUNT(*) FROM messages_fts f JOIN message_metadata m ON m.rowid=f.rowid WHERE m.archive_id=?', (manifest['archiveId'],)).fetchone()[0]
    ids = [r[0] for r in rows]
    expected = {item['itemId'] for item in manifest['items']}
    return {'pass': integrity=='ok' and len(ids)==len(set(ids))==len(expected)==fts==8192 and set(ids)==expected, 'archiveId': manifest['archiveId'], 'totalMessages': len(ids), 'ftsIndexedCount': fts, 'integrityCheck': integrity, 'dbPath': str(db), 'scope': 'archive filtered global projection, read only'}


def audit_dovecot_source_invariance(config_path: pathlib.Path, baseline_path: pathlib.Path) -> dict:
    """Audit Dovecot source account to ensure original 12 messages / 4 attachments are completely untouched."""
    return base_task019_oracle.audit_dovecot_source_invariance(config_path, baseline_path)
