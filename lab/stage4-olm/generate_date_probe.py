"""Create a new public-fixture derivative for hour parsing diagnostics; never alter source."""
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile

root = Path(__file__).resolve().parents[2]
out = root / 'runtime/root-olm-date-probe'
out.mkdir(exist_ok=False)
rows = []
with zipfile.ZipFile(root / 'fixtures/vendor-olm/SampleOLM.olm') as source, zipfile.ZipFile(out / 'hours.olm', 'x', compression=zipfile.ZIP_DEFLATED) as target:
    for info in source.infolist():
        data = source.read(info)
        if info.filename.endswith('.xml') and '/com.microsoft.__Messages/' in info.filename and '/com.microsoft.__Attachments/' not in info.filename:
            tree = ET.fromstring(data)
            for email in tree.iter('email'):
                hour = len(rows) % 24
                literal = f'2018-10-17T{hour:02d}:16:53'
                for field in ('OPFMessageCopySentTime', 'OPFMessageCopyReceivedTime'):
                    node = email.find(field)
                    if node is not None:
                        node.text = literal
                rows.append({'entry': info.filename, 'messageId': email.findtext('OPFMessageCopyMessageID'), 'literal': literal})
            data = ET.tostring(tree, encoding='utf-8', xml_declaration=True)
        target.writestr(info, data)
with (out / 'oracle.json').open('x', encoding='utf-8') as f:
    json.dump(rows, f, indent=2)
print(f'Created new synthetic date derivative: {len(rows)} messages, source unchanged.')
