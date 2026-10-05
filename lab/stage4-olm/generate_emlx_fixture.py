"""Create a separate synthetic Apple Mail wrapper around accepted EML bytes; never alters sources."""
from pathlib import Path
import hashlib
import json
import plistlib

root = Path(__file__).resolve().parents[2]
source = root / 'fixtures/mail-corpus-v1/eml'
target = root / 'fixtures/emlx-corpus-v1'
target.mkdir(exist_ok=False)
entries = []
for ordinal, path in enumerate(sorted(source.glob('*.eml')), 1):
    raw = path.read_bytes()
    folder = 'Gelen Kutusu' if ordinal <= 5 else 'Gönderilenler' if ordinal <= 8 else 'Projeler/İstanbul'
    relative = Path('complete') / folder / f'{ordinal:04d}.emlx'
    destination = target / relative
    destination.parent.mkdir(parents=True, exist_ok=True)
    metadata = plistlib.dumps({'flags': ordinal, 'synthetic-fixture': True, 'source-id': path.stem})
    wrapper = str(len(raw)).encode('ascii') + (b'\r\n' if ordinal % 2 else b'\n') + raw + metadata
    destination.write_bytes(wrapper)
    entries.append({'sourceFile': path.name, 'relativePath': relative.as_posix(),
                    'sourceSha256': hashlib.sha256(raw).hexdigest().upper(),
                    'wrapperSha256': hashlib.sha256(wrapper).hexdigest().upper(),
                    'metadataSha256': hashlib.sha256(metadata).hexdigest().upper(),
                    'messageBytes': len(raw)})
negative = target / 'negative'
negative.mkdir()
(negative / 'missing-body.partial.emlx').write_bytes(b'100\nSubject: Incomplete\n\n')
(negative / 'truncated.emlx').write_bytes(b'1000\nShort')
(negative / 'invalid-count.emlx').write_bytes(b'-1\nInvalid')
(target / 'manifest.json').write_text(json.dumps({'qualification': 'Synthetic EMLX wrappers, not exported by a Mac. '
    'Contains accepted physical duplicate EMLs. Metadata remains opaque and not mapped to destination flags.',
    'messages': entries, 'negativeCases': ['partial', 'truncated', 'invalid-count']}, ensure_ascii=False, indent=2), encoding='utf-8')
print(f'Created {len(entries)} complete EMLX files and 3 negative fixtures, source unchanged.')
