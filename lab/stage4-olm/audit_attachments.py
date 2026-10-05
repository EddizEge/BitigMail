"""Read-only independent check against physical public OLM ZIP attachment payloads."""
import collections
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
import zipfile

source, probe_path, output_path = map(Path, sys.argv[1:])
source_hash = hashlib.sha256(source.read_bytes()).hexdigest().upper()
with zipfile.ZipFile(source) as archive:
    names = archive.namelist()
    # This is a pinned, <1MB public vendor fixture, not a general untrusted OLM reader.
    roots = [ET.fromstring(archive.read(n)) for n in names if n.endswith('.xml')]
    references = [e.attrib for root in roots for e in root.iter('messageAttachment')]
    missing = sum(1 for ref in references if ref.get('OPFAttachmentURL') not in names)
    expected = collections.Counter(
        hashlib.sha256(archive.read(ref['OPFAttachmentURL'])).hexdigest().upper()
        for ref in references if ref.get('OPFAttachmentURL') in names)
probe = json.loads(probe_path.read_text(encoding='utf-8-sig'))
observed = collections.Counter(h for folder in probe['folders'] for h in folder['attachmentHashes']
                               if h != 'UNRESOLVED_BINARY_DATA')
result = {
    'status': 'PASS' if expected == observed and missing == 0 else 'FIDELITY_GAP',
    'sourceSha256': source_hash,
    'sourceMatchesProbe': source_hash == probe['before'] == probe['after'],
    'xmlEmailElements': sum(1 for root in roots for _ in root.iter('email')),
    'attachmentReferences': len(references), 'missingPhysicalPayloads': missing,
    'matchingPhysicalHashes': sum((expected & observed).values()),
    'expectedUnmatched': sum((expected-observed).values()),
    'observedUnmatched': sum((observed-expected).values()),
    'sdkNullBinaryAttachments': probe['unresolvedAttachments'],
    'qualification': 'BinaryData extraction alone is not accepted for OLM preservation. '
                     'Does not establish whether a different SDK attachment extraction path preserves the content.'
}
with output_path.open('x', encoding='utf-8') as out:
    json.dump(result, out, indent=2)
print(json.dumps(result))
