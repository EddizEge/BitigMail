"""Persist an independent byte-level source audit without running any mail job."""
import argparse
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('source', type=Path)
parser.add_argument('baseline', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
source = args.source.resolve()
baseline = json.loads(args.baseline.read_text(encoding='utf-8-sig'))
checks = []
expected = set()
for message in baseline['messages']:
    file = (source / message['folder'] / message['identity']).resolve()
    if not file.is_relative_to(source):
        raise ValueError('Baseline path outside source')
    relative = file.relative_to(source).as_posix()
    expected.add(relative)
    with file.open('rb') as stream:
        actual_hash = hashlib.file_digest(stream, 'sha256').hexdigest()
    size = file.stat().st_size
    checks.append({'file': relative, 'bytes': size, 'sha256': actual_hash,
                   'pass': size == message['rawBytes'] and actual_hash == message['rawSha256']})
actual = {p.relative_to(source).as_posix() for p in source.rglob('*')
          if p.is_file() and p.suffix.lower() == '.eml'}
result = {'pass': actual == expected and len(expected) == len(checks) and all(c['pass'] for c in checks),
          'count': len(checks), 'rawBytes': sum(c['bytes'] for c in checks),
          'missing': sorted(expected - actual), 'extra': sorted(actual - expected), 'files': checks}
with args.output.open('x', encoding='utf-8') as stream:
    json.dump(result, stream, ensure_ascii=False, indent=2)
print(json.dumps({key: value for key, value in result.items() if key != 'files'}))
if not result['pass']:
    raise SystemExit('Source mismatch')
