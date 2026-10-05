"""Independent XML field comparison for the pinned vendor OLM probe; no body text in output."""
import collections
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile

root = Path(__file__).resolve().parents[2]
probe = json.loads((root / '.codex-coordination/evidence/STAGE4/olm-fields-probe.json').read_text())
observed = {(folder['folderPath'], item['identityHash']): item for folder in probe['folders'] for item in folder['mailFields']}
counts = collections.Counter()
details = []
def digest(value):
    return hashlib.sha256(value.encode()).hexdigest().upper()

with zipfile.ZipFile(root / 'fixtures/vendor-olm/SampleOLM.olm') as archive:
    for name in archive.namelist():
        if not name.endswith('.xml') or '/com.microsoft.__Messages/' not in name or '/com.microsoft.__Attachments/' in name:
            continue
        folder = '/' + name.split('/com.microsoft.__Messages/', 1)[1].rsplit('/', 1)[0]
        for ordinal, email in enumerate(ET.fromstring(archive.read(name)).iter('email'), 1):
            key = folder, digest(email.findtext('OPFMessageCopyMessageID', ''))
            item = observed.get(key)
            if item is None:
                counts['missingBinding'] += 1
                continue
            checks = {}
            for source, target in [('OPFMessageCopySubject', 'subjectHash'), ('OPFMessageCopyBody', 'plainHash'), ('OPFMessageCopyHTMLBody', 'htmlHash')]:
                value = email.findtext(source, '')
                checks[target] = digest(value) == item[target]
                counts[target + ('Equal' if checks[target] else 'Different')] += 1
                if target != 'subjectHash':
                    normalized = digest(value.replace('\r\n', '\n').replace('\r', '\n')) == item[target.replace('Hash', 'LfHash')]
                    checks[target.replace('Hash', 'LfHash')] = normalized
                    counts[target.replace('Hash', 'LfHash') + ('Equal' if normalized else 'Different')] += 1
            checks['submitLiteralEqual'] = item['submitTime'][:19] == email.findtext('OPFMessageCopySentTime', '')
            checks['deliveryLiteralEqual'] = item['deliveryTime'][:19] == email.findtext('OPFMessageCopyReceivedTime', '')
            counts['submitLiteralEqual' if checks['submitLiteralEqual'] else 'submitLiteralDifferent'] += 1
            counts['deliveryLiteralEqual' if checks['deliveryLiteralEqual'] else 'deliveryLiteralDifferent'] += 1
            details.append({'sourceEntry': name, 'ordinal': ordinal, 'checks': checks,
                            'sdkSubmitKind': item['submitKind'], 'sdkDeliveryKind': item['deliveryKind']})
output = {'status': 'MEASURED_NOT_CONVERSION_ACCEPTANCE', 'items': len(details), 'counts': dict(counts), 'details': details,
          'qualification': 'Direct OLM-to-SDK field comparison. XML date literals have no offset; matching literals is not a timezone semantics proof. No MIME conversion or UI acceptance.'}
with (root / '.codex-coordination/evidence/STAGE4/olm-field-audit.json').open('x', encoding='utf-8') as target:
    json.dump(output, target, indent=2)
print(json.dumps({'items': len(details), 'counts': dict(counts), 'dateKinds': sorted({d['sdkSubmitKind'] for d in details})}))
