#!/usr/bin/env python3
"""TASK-020 1GiB Synthetic Source Corpus Generator.
Generates exactly 8 physical copies of each of the accepted 1,024 source records from stage-128m,
yielding exactly 8,192 physical RFC822 messages (~1.00 GiB) across the 5 canonical folders.

Preserves stage-128m source unchanged.
Closed token: task019-tree-1g (resolves to runtime/task019/stage-1g/source/eml-tree).
Closed token: task019-mbox-1g (resolves to runtime/task019/stage-1g/source/corpus.mbox).

Outputs:
- runtime/task019/stage-1g/source/eml-tree/ (8,192 physical EML files)
- runtime/task019/stage-1g/source/corpus.mbox (unified 8,192 records)
- runtime/task019/stage-1g/source/manifest.json (detailed metadata and duplicate tracking)
"""
import copy
import datetime
import hashlib
import json
import os
import pathlib
import re
import sys

ROOT = pathlib.Path(r'C:\Users\Eddiz\Documents\ChatGPT\Mail Manager').resolve()
STAGE128_DIR = ROOT / 'runtime/task019/stage-128m/source'
STAGE1G_DIR = ROOT / 'runtime/task019/stage-1g/source'

PHYSICAL_COPIES = 8
ACCEPTED_ITEMS = 1024
TOTAL_ITEMS = ACCEPTED_ITEMS * PHYSICAL_COPIES  # 8,192 items

MAX_SINGLE_MESSAGE_BYTES = 64 * 1024 * 1024  # 64 MiB limit


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def format_mboxrd_record(raw_bytes: bytes, envelope_date_str: str) -> bytes:
    try:
        dt = datetime.datetime.fromisoformat(envelope_date_str.replace('Z', '+00:00'))
    except Exception:
        dt = datetime.datetime(2026, 9, 14, 12, 0, 0, tzinfo=datetime.timezone.utc)
    from_line = f"From sender@task020.local {dt.strftime('%a %b %d %H:%M:%S %Y')}\r\n".encode('ascii')
    lines = raw_bytes.splitlines(keepends=True)
    escaped = []
    for line in lines:
        if re.match(br'^>*From ', line):
            escaped.append(b'>' + line)
        else:
            escaped.append(line)
    body = b''.join(escaped)
    if not body.endswith(b'\r\n'):
        body += b'\r\n'
    return from_line + body + b'\r\n'


def generate_1g_corpus(force_regenerate=False):
    print("=======================================================================")
    print(f"TASK-020 1GiB SYNTHETIC CORPUS GENERATOR ({TOTAL_ITEMS} items)")
    print("=======================================================================")

    # 1. Verify stage-128m preservation
    if not STAGE128_DIR.exists():
        raise FileNotFoundError(f"stage-128m source directory not found at {STAGE128_DIR}")

    manifest128_path = STAGE128_DIR / 'manifest.json'
    if not manifest128_path.exists():
        raise FileNotFoundError(f"stage-128m manifest not found at {manifest128_path}")

    manifest128 = json.loads(manifest128_path.read_text(encoding='utf-8-sig'))
    src_messages = manifest128.get('messages', [])
    if len(src_messages) != ACCEPTED_ITEMS:
        raise ValueError(f"Expected {ACCEPTED_ITEMS} messages in stage-128m, got {len(src_messages)}")

    eml_tree_128 = STAGE128_DIR / 'eml-tree'
    src_emls = list(eml_tree_128.glob('**/*.eml'))
    if len(src_emls) != ACCEPTED_ITEMS:
        raise ValueError(f"Expected {ACCEPTED_ITEMS} EML files in stage-128m, found {len(src_emls)}")

    total_128_bytes = sum(f.stat().st_size for f in src_emls)
    print(f"Verified preserved stage-128m: {len(src_emls)} items, {total_128_bytes} bytes ({total_128_bytes / (1024*1024):.2f} MiB)")

    # 2. Check if stage-1g already generated and valid
    eml_tree_1g = STAGE1G_DIR / 'eml-tree'
    manifest_1g_path = STAGE1G_DIR / 'manifest.json'
    mbox_1g_path = STAGE1G_DIR / 'corpus.mbox'

    if not force_regenerate and manifest_1g_path.exists() and eml_tree_1g.exists():
        try:
            m1g = json.loads(manifest_1g_path.read_text(encoding='utf-8-sig'))
            if m1g.get('totalItems') == TOTAL_ITEMS:
                actual_emls = list(eml_tree_1g.glob('**/*.eml'))
                if len(actual_emls) == TOTAL_ITEMS:
                    print(f"stage-1g already generated with {TOTAL_ITEMS} valid items. Skipping regeneration.")
                    return m1g
        except Exception as ex:
            print(f"Existing stage-1g verification failed ({ex}); regenerating...")

    # 3. Create destination directory
    STAGE1G_DIR.mkdir(parents=True, exist_ok=True)
    eml_tree_1g.mkdir(parents=True, exist_ok=True)

    # 4. Generate 8 physical copies of each accepted message
    print(f"Generating {PHYSICAL_COPIES} physical copies of {ACCEPTED_ITEMS} source records -> {TOTAL_ITEMS} total records...")
    
    # Cache source raw bytes to avoid re-reading
    src_raw_cache = {}
    for msg in src_messages:
        ord_num = msg['ordinal']
        rel_path = msg['relativePath']
        src_path = eml_tree_128 / rel_path
        if not src_path.exists():
            raise FileNotFoundError(f"Source file {src_path} does not exist")
        raw = src_path.read_bytes()
        if len(raw) >= MAX_SINGLE_MESSAGE_BYTES:
            raise ValueError(f"Message {ord_num} exceeds 64 MiB limit: {len(raw)} bytes")
        src_raw_cache[ord_num] = {
            'raw': raw,
            'meta': msg,
            'sha256': sha256_bytes(raw),
            'folder': msg['folder'],
            'date': msg.get('dateUtc', '2026-09-14T00:00:00+00:00')
        }

    # Generate records
    messages_1g = []
    folder_stats = {f_name: {'itemCount': 0, 'rawSizeBytes': 0} for f_name in manifest128.get('folders', {})}
    mbox_records = []
    item_hashes = []
    total_physical_raw_bytes = 0
    item_ordinal = 0

    for copy_idx in range(PHYSICAL_COPIES):
        for ord_num in range(ACCEPTED_ITEMS):
            item = src_raw_cache[ord_num]
            folder_name = item['folder']
            folder_disk_dir = eml_tree_1g / pathlib.Path(folder_name)
            folder_disk_dir.mkdir(parents=True, exist_ok=True)

            eml_filename = f"msg_{ord_num:04d}_c{copy_idx:02d}.eml"
            eml_path = folder_disk_dir / eml_filename

            raw_bytes = item['raw']
            eml_path.write_bytes(raw_bytes)

            file_sha = item['sha256']
            item_hashes.append(file_sha)
            total_physical_raw_bytes += len(raw_bytes)

            folder_stats[folder_name]['itemCount'] += 1
            folder_stats[folder_name]['rawSizeBytes'] += len(raw_bytes)

            # Build metadata record
            orig_meta = item['meta']
            meta_rec = {
                'ordinal': item_ordinal,
                'sourceOrdinal': ord_num,
                'copyIndex': copy_idx,
                'folder': folder_name,
                'senderAddress': orig_meta.get('senderAddress', ''),
                'senderDisplay': orig_meta.get('senderDisplay', ''),
                'subject': orig_meta.get('subject', ''),
                'dateUtc': orig_meta.get('dateUtc', ''),
                'messageIdHeader': orig_meta.get('messageIdHeader', ''),
                'attachmentCount': orig_meta.get('attachmentCount', 0),
                'attachments': orig_meta.get('attachments', []),
                'isBodyTruncated': orig_meta.get('isBodyTruncated', False),
                'expectedSearchMatches': orig_meta.get('expectedSearchMatches', []),
                'relativePath': f"{folder_name}/{eml_filename}",
                'rawSizeBytes': len(raw_bytes),
                'rawSha256': file_sha
            }
            messages_1g.append(meta_rec)

            # Build MBOX record
            mbox_rec = format_mboxrd_record(raw_bytes, item['date'])
            mbox_records.append(mbox_rec)

            item_ordinal += 1

    # 5. Write unified corpus.mbox
    print(f"Writing unified corpus.mbox ({len(mbox_records)} records)...")
    with mbox_1g_path.open('wb') as f_mbox:
        for rec in mbox_records:
            f_mbox.write(rec)
    mbox_size = mbox_1g_path.stat().st_size
    print(f"Written corpus.mbox: {mbox_size} bytes ({mbox_size / (1024*1024):.2f} MiB)")

    # 6. Aggregate fingerprint
    aggregate_fingerprint = hashlib.sha256("".join(item_hashes).encode('ascii')).hexdigest()

    # 7. Search query oracles accounting for 8 copies
    src_search_oracles = manifest128.get('searchOracles', {})
    turkish_queries_1g = []
    for q_spec in src_search_oracles.get('turkishIQueries', []):
        q_term = q_spec['query']
        orig_matching = set(q_spec.get('expectedMatchingOrdinals', []))
        matching_1g = [m['ordinal'] for m in messages_1g if m['sourceOrdinal'] in orig_matching]
        turkish_queries_1g.append({
            'query': q_term,
            'sourceMatchCountPerCopy': len(orig_matching),
            'expectedTotalMatches': len(matching_1g),
            'expectedMatchingOrdinals': matching_1g
        })

    # 8. Duplicate mapping
    exact_duplicates_1g = []
    for ord_num in range(ACCEPTED_ITEMS):
        copies_of_ord = [m['ordinal'] for m in messages_1g if m['sourceOrdinal'] == ord_num]
        exact_duplicates_1g.append({
            'sourceOrdinal': ord_num,
            'physicalCopies': copies_of_ord
        })

    manifest_1g = {
        'generator': 'TASK-020 1GiB Synthetic Scale Corpus Generator',
        'generatedAtUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'sourceSeed': manifest128.get('generatorSeed', 20260914),
        'physicalCopiesPerRecord': PHYSICAL_COPIES,
        'acceptedSourceRecords': ACCEPTED_ITEMS,
        'totalItems': TOTAL_ITEMS,
        'totalPhysicalRawSizeBytes': total_physical_raw_bytes,
        'totalPhysicalRawSizeMiB': round(total_physical_raw_bytes / (1024 * 1024), 2),
        'totalPhysicalRawSizeGiB': round(total_physical_raw_bytes / (1024 * 1024 * 1024), 4),
        'nominalRawSizeBytes': 1070775680,
        'nominalRawSizeNote': '8 * 133,846,960 bytes; 1248-byte delta reflects 156 bytes per 1024 messages wire CRLF formatting variance documented in SCALE_AND_RESILIENCE_VALIDATION.md',
        'maxIndividualMessageSizeBytes': max(m['rawSizeBytes'] for m in messages_1g),
        'maxMessageUnder64MiBLimit': all(m['rawSizeBytes'] < MAX_SINGLE_MESSAGE_BYTES for m in messages_1g),
        'aggregateFingerprint': aggregate_fingerprint,
        'mboxSizeBytes': mbox_size,
        'folders': folder_stats,
        'messages': messages_1g,
        'searchOracles': {
            'turkishIQueries': turkish_queries_1g,
            'exactPhysicalDuplicates': exact_duplicates_1g,
            'replicatedDuplicatePairs': [
                {'pair': pair, 'replicatedAcrossCopies': 8}
                for pair in src_search_oracles.get('exactDuplicatePairs', [])
            ]
        }
    }

    manifest_1g_path.write_text(json.dumps(manifest_1g, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f"Generation complete! Total items: {TOTAL_ITEMS}, Physical raw bytes: {total_physical_raw_bytes} ({total_physical_raw_bytes / (1024*1024):.2f} MiB)")
    return manifest_1g


if __name__ == '__main__':
    generate_1g_corpus(force_regenerate='--force' in sys.argv)
