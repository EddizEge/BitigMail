"""Independent stdlib oracle for TASK017. Read-only files and loopback lab IMAP only.

Snapshots retain each physical message. Comparing counters catches missing copies,
unexpected copies, changed headers, decoded body/attachments and inline Content-ID.
No production parser is imported; no mailbox content/credentials printed to stdout.
"""
import argparse
import base64
import collections
import datetime
import email.policy
import email.parser
import hashlib
import imaplib
import json
import pathlib
import re
import sys


def sha(data):
    return hashlib.sha256(data).hexdigest()


def canonical(raw):
    if b'\r' in raw.replace(b'\r\n', b''):
        raise ValueError('Bare CR in canonical comparison input')
    result = raw.replace(b'\r\n', b'\n').replace(b'\n', b'\r\n')
    return result if result.endswith(b'\r\n') else result + b'\r\n'


def observe(raw, identity, folder='', **extra):
    message = email.parser.BytesParser(policy=email.policy.default).parsebytes(raw)
    parts = []
    for part in message.walk():
        if part.is_multipart():
            continue
        payload = part.get_payload(decode=True) or b''
        # Comparing decoded text permits only wire newline normalization.
        semantic = payload.replace(b'\r\n', b'\n') if part.get_content_maintype() == 'text' else payload
        parts.append({'type': part.get_content_type(), 'name': part.get_filename(),
                      'disposition': part.get_content_disposition(), 'cid': str(part.get('Content-ID', '')),
                      'decodedSha256': sha(semantic), 'decodedBytes': len(semantic)})
    item = {'identity': identity, 'folder': folder, 'rawSha256': sha(raw), 'rawBytes': len(raw),
            'canonicalSha256': sha(canonical(raw)), 'headers': [(k, str(v)) for k, v in message.items()],
            'parts': parts, 'attachments': sum(bool(p['name'] or p['cid'] or p['disposition'] == 'attachment') for p in parts)}
    item.update(extra)
    return item


def mbox_records(path):
    """mboxrd: byte-preserving record framing; strip exactly one body escape."""
    current = None
    in_body = False
    ordinal = 0
    with path.open('rb') as source:
        for line in source:
            if line.startswith(b'From '):
                if current is not None:
                    if not in_body:
                        raise ValueError('MBOX message has no header/body separator')
                    ordinal += 1
                    yield ordinal, b''.join(current)
                current, in_body = [], False
            elif current is None:
                raise ValueError('MBOX has unframed leading bytes')
            else:
                if in_body and re.match(br'^>+From ', line):
                    line = line[1:]
                current.append(line)
                if line in (b'\n', b'\r\n'):
                    in_body = True
    if current is not None:
        if not in_body:
            raise ValueError('MBOX final message has no header/body separator')
        yield ordinal + 1, b''.join(current)


def files_snapshot(root, kind):
    root = pathlib.Path(root).resolve(strict=True)
    paths = [root] if root.is_file() else sorted(root.rglob('*.eml' if kind == 'eml' else '*.mbox'))
    messages, files = [], []
    for path in paths:
        if path.is_symlink():
            raise ValueError('Oracle rejects symlinks')
        before = sha(path.read_bytes())
        if kind == 'eml':
            messages.append(observe(path.read_bytes(), path.name, str(path.parent.relative_to(root)) if root.is_dir() else ''))
        else:
            for ordinal, raw in mbox_records(path):
                messages.append(observe(raw, f'{path.name}:{ordinal}', path.name))
        after = sha(path.read_bytes())
        if before != after:
            raise ValueError('Source file changed during oracle read')
        files.append({'path': str(path), 'sha256': before})
    if not messages:
        raise ValueError('No messages observed')
    return {'oracle': 'Python stdlib independent file/MIME parser', 'messages': messages, 'files': files,
            'count': len(messages), 'attachments': sum(m['attachments'] for m in messages)}


def utf7(value):
    return re.sub(r'[^\x20-\x7e]+', lambda m: '&' + base64.b64encode(m[0].encode('utf-16be')).decode().rstrip('=').replace('/', ',') + '-', value.replace('&', '&-'))


def require(result):
    if result[0] != 'OK':
        raise RuntimeError('Read-only lab IMAP command failed')
    return result[1]


def imap_snapshot(config_path, account, folders):
    cfg = json.loads(pathlib.Path(config_path).read_text(encoding='utf-8-sig'))[account]
    if cfg['imapHost'] != '127.0.0.1' or cfg['imapPort'] != 5143:
        raise ValueError('Only isolated Dovecot loopback lab is allowed')
    client = imaplib.IMAP4('127.0.0.1', 5143, timeout=30)
    messages, observed_folders = [], []
    try:
        require(client.login(cfg['username'], cfg['password']))
        for folder in folders:
            quoted = '"' + utf7(folder).replace('\\', '\\\\').replace('"', '\\"') + '"'
            require(client.select(quoted, readonly=True))
            validity = int(client.response('UIDVALIDITY')[1][0])
            uids = require(client.uid('SEARCH', None, 'ALL'))[0].split()
            observed_folders.append({'folder': folder, 'uidValidity': validity, 'count': len(uids)})
            for uid in uids:
                response = require(client.uid('FETCH', uid, '(UID FLAGS INTERNALDATE BODY.PEEK[])'))
                meta, raw = next(part for part in response if isinstance(part, tuple))
                flags = sorted(flag.decode('ascii') for flag in imaplib.ParseFlags(meta) if flag != b'\\Recent')
                date_text = re.search(rb'INTERNALDATE "([^"]+)"', meta)[1].decode('ascii').strip()
                instant = datetime.datetime.strptime(date_text, '%d-%b-%Y %H:%M:%S %z').astimezone(datetime.timezone.utc).isoformat()
                messages.append(observe(raw, f'{validity}:{uid.decode()}', folder, flags=flags, internalDateUtc=instant))
    finally:
        try:
            client.logout()
        except Exception:
            pass
    return {'oracle': 'Python stdlib read-only IMAP BODY.PEEK/MIME', 'messages': messages, 'folders': observed_folders,
            'count': len(messages), 'attachments': sum(m['attachments'] for m in messages)}


def compare(left, right, exact, metadata):
    fields = ['rawSha256', 'rawBytes'] if exact else ['canonicalSha256']
    fields += ['headers', 'parts']
    if metadata:
        fields += ['flags', 'internalDateUtc', 'identity', 'folder']
    def signature(item):
        return json.dumps({key: item[key] for key in fields}, sort_keys=True)
    a = collections.Counter(signature(m) for m in left['messages'])
    b = collections.Counter(signature(m) for m in right['messages'])
    missing, extra = sum((a - b).values()), sum((b - a).values())
    return {'oracle': 'Independent physical-message multiset', 'pass': missing == extra == 0,
            'exactRaw': exact, 'metadataCompared': metadata, 'sourceCount': left['count'], 'targetCount': right['count'],
            'missing': missing, 'extra': extra, 'sourceAttachments': left['attachments'], 'targetAttachments': right['attachments']}


def metadata_flags_match(system_flags, keywords, observed):
    # Sidecars encode MailKit MessageFlags names; None means the zero enum value.
    # Keep actual IMAP keywords named None/Recent/Seen distinct from system flags.
    expected_system = {f.lstrip('\\').lower() for f in system_flags
                       if f.lstrip('\\').lower() not in ('none', 'recent')}
    actual_system = {f[1:].lower() for f in observed if f.startswith('\\') and f.lower() != '\\recent'}
    expected_keywords = {k.lower() for k in keywords}
    actual_keywords = {k.lower() for k in observed if not k.startswith('\\')}
    return expected_system == actual_system and expected_keywords == actual_keywords


def verify_manifest(source, manifest, output_root):
    """Verify metadata and original-folder attribution independently of product reports."""
    root = pathlib.Path(output_root).resolve(strict=True)
    originals = {(m['folder'], m['identity']): m for m in source['messages']}
    observed = set()
    results = []
    instant = lambda text: datetime.datetime.fromisoformat(text.replace('Z', '+00:00')).astimezone(datetime.timezone.utc)
    for item in manifest['items']:
        key = (item['originalFolder'], f"{item['sourceUidValidity']}:{item['sourceUid']}")
        expected = originals.get(key)
        ok = expected is not None and key not in observed
        observed.add(key)
        path = (root / item['relativeOutputPath']).resolve(strict=True)
        ok = ok and path.is_relative_to(root) and not path.is_symlink()
        if expected:
            ok = ok and item['sourceSha256'].lower() == expected['rawSha256'] and item['originalLength'] == expected['rawBytes']
            ok = ok and instant(item['internalDateUtc']) == instant(expected['internalDateUtc'])
            ok = ok and metadata_flags_match(item['flags'], item['keywords'], expected['flags'])
        if manifest['targetFormat'] == 'eml-tree':
            raw = path.read_bytes()
            ok = ok and sha(raw) == item['storedSha256'].lower() == item['sourceSha256'].lower()
        results.append({'identity': key, 'pass': bool(ok)})
    folder_counts = collections.Counter(m['folder'] for m in source['messages'])
    mapped_counts = {folder['originalFolder']: folder['messageCount'] for folder in manifest['folders']}
    mapping_ok = len(mapped_counts) == len(manifest['folders']) and dict(folder_counts) == mapped_counts
    return {'oracle': 'Independent export sidecar metadata and folder mapping',
            'pass': all(r['pass'] for r in results) and set(originals) == observed and mapping_ok,
            'count': len(results), 'mappingPass': mapping_ok, 'items': results}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=['eml', 'mbox', 'imap', 'compare'])
    parser.add_argument('--path')
    parser.add_argument('--config')
    parser.add_argument('--account', choices=['source', 'target'])
    parser.add_argument('--folders', help='JSON file containing exact Unicode folder paths')
    parser.add_argument('--source')
    parser.add_argument('--target')
    parser.add_argument('--exact', action='store_true')
    parser.add_argument('--metadata', action='store_true')
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    load = lambda path: json.loads(pathlib.Path(path).read_text(encoding='utf-8-sig'))
    try:
        if args.mode in ('eml', 'mbox'):
            result = files_snapshot(args.path, args.mode)
        elif args.mode == 'imap':
            result = imap_snapshot(args.config, args.account, load(args.folders))
        else:
            result = compare(load(args.source), load(args.target), args.exact, args.metadata)
        output = pathlib.Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
        print(json.dumps({k: v for k, v in result.items() if k not in ['messages', 'files', 'folders']}, ensure_ascii=False))
        sys.exit(0 if result.get('pass', True) else 1)
    except Exception as exc:
        print(json.dumps({'pass': False, 'errorType': type(exc).__name__}))
        sys.exit(2)
