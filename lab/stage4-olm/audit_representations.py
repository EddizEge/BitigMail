"""Read-only public OLM oracle; representation comparisons are not full MIME fidelity."""
import collections
import datetime
import hashlib
from html.parser import HTMLParser
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]


class VisibleText(HTMLParser):
    def __init__(self):
        super().__init__()
        self.parts = []
        self.skip = 0

    def handle_starttag(self, tag, attrs):
        if tag in ('script', 'style'):
            self.skip += 1

    def handle_endtag(self, tag):
        if tag in ('script', 'style'):
            self.skip = max(0, self.skip - 1)

    def handle_data(self, data):
        if not self.skip:
            self.parts.append(data)


def visible(value):
    parser = VisibleText()
    parser.feed(value)
    return ' '.join(' '.join(parser.parts).split())


probe = json.loads((ROOT / '.codex-coordination/evidence/STAGE4/olm-representation-probe.json').read_text())
observed = {(f['folderPath'], m['identityHash']): m for f in probe['folders'] for m in f['mailFields']}
counts = collections.Counter()
rows = []
with zipfile.ZipFile(ROOT / 'fixtures/vendor-olm/SampleOLM.olm') as archive:
    for name in archive.namelist():
        if not name.endswith('.xml') or '/com.microsoft.__Messages/' not in name or '/com.microsoft.__Attachments/' in name:
            continue
        folder = '/' + name.split('/com.microsoft.__Messages/', 1)[1].rsplit('/', 1)[0]
        for ordinal, email in enumerate(ET.fromstring(archive.read(name)).iter('email'), 1):
            identity = hashlib.sha256(email.findtext('OPFMessageCopyMessageID', '').encode()).hexdigest().upper()
            item = observed[(folder, identity)]
            original_html = email.findtext('OPFMessageCopyHTMLBody', '')
            original_visible = visible(original_html)
            sdk_visible = visible(item['sdkHtml'] or '')
            counts['sourceBodyEqualsHtml' if email.findtext('OPFMessageCopyBody', '') == original_html else 'sourceBodyDiffersFromHtml'] += 1
            counts['subjectOnlyEvaluationSuffix' if item['sdkSubject'] == email.findtext('OPFMessageCopySubject', '') + '(Aspose.Email Evaluation)' else 'subjectOtherDifference'] += 1
            counts['htmlVisibleSourceContained' if original_visible in sdk_visible else 'htmlVisibleDifferent'] += 1
            # This is a diagnostic hypothesis, not a verified OLM timezone contract.
            literal = email.findtext('OPFMessageCopySentTime')
            hypothetical_utc = datetime.datetime.fromisoformat(literal).replace(tzinfo=datetime.timezone.utc)
            sdk_utc = datetime.datetime.fromisoformat(item['submitTime']).astimezone(datetime.timezone.utc)
            delta = int((sdk_utc - hypothetical_utc).total_seconds())
            counts['submitDeltaSeconds:' + str(delta)] += 1
            rows.append({'entry': name, 'ordinal': ordinal, 'sourceSubmitLiteral': literal,
                         'sdkSubmitTime': item['submitTime'], 'hypotheticalUtcDeltaSeconds': delta})

result = {'status': 'QUALIFIED_REPRESENTATION_AUDIT_NOT_CONVERSION_ACCEPTANCE', 'items': len(rows),
          'counts': dict(counts), 'details': rows,
          'qualification': 'All source Body fields in this fixture contain HTML. Visible-text containment does not prove HTML markup/CID/address fidelity. Evaluation additions remain untouched. Source date literals have no offset; UTC comparison is diagnostic only. Four physical copies show a 12-hour difference, not a DST-sized difference. No automatic timestamp correction is authorized.'}
with (ROOT / '.codex-coordination/evidence/STAGE4/olm-representation-audit.json').open('x', encoding='utf-8') as target:
    json.dump(result, target, indent=2)
print(json.dumps({'items': len(rows), 'counts': dict(counts)}))
