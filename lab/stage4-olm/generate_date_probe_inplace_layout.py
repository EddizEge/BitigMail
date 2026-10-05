"""New fixture copy: change equal-length date text, retain ZIP layout and update standard CRCs."""
import binascii
import hashlib
import io
import json
from pathlib import Path
import re
import struct
import zipfile

root = Path(__file__).resolve().parents[2]
source = root / 'fixtures/vendor-olm/SampleOLM.olm'
original = source.read_bytes()
data = bytearray(original)
rows = []
crc_by_name = {}
with zipfile.ZipFile(io.BytesIO(original)) as archive:
    central_start = archive.start_dir
    central_entries = len(archive.infolist())
    for info in archive.infolist():
        if not info.filename.endswith('.xml') or '/com.microsoft.__Messages/' not in info.filename or '/com.microsoft.__Attachments/' in info.filename:
            continue
        assert info.compress_type == zipfile.ZIP_STORED and not info.flag_bits & 8
        before = archive.read(info)
        hour = len(rows) % 24
        literal = f'2018-10-17T{hour:02d}:16:53'.encode()
        pattern = rb'(<OPFMessageCopy(?:SentTime|ReceivedTime)[^>]*>)[^<]+(</OPFMessageCopy(?:SentTime|ReceivedTime)>)'
        after, count = re.subn(pattern, lambda match: match[1] + literal + match[2], before)
        assert count == 2 and len(after) == len(before)
        offset = info.header_offset
        assert data[offset:offset + 4] == b'PK\x03\x04'
        name_len, extra_len = struct.unpack_from('<HH', data, offset + 26)
        payload_offset = offset + 30 + name_len + extra_len
        assert data[payload_offset:payload_offset + len(before)] == before
        data[payload_offset:payload_offset + len(after)] = after
        crc = binascii.crc32(after) & 0xffffffff
        struct.pack_into('<I', data, offset + 14, crc)
        crc_by_name[info.filename] = crc
        rows.append({'entry': info.filename, 'literal': literal.decode()})

pos = central_start
for _ in range(central_entries):
    assert data[pos:pos + 4] == b'PK\x01\x02'
    name_len, extra_len, comment_len = struct.unpack_from('<HHH', data, pos + 28)
    name = bytes(data[pos + 46:pos + 46 + name_len]).decode('utf-8')
    if name in crc_by_name:
        struct.pack_into('<I', data, pos + 16, crc_by_name[name])
    pos += 46 + name_len + extra_len + comment_len
with zipfile.ZipFile(io.BytesIO(data)) as check:
    assert check.testzip() is None
out = root / 'runtime/root-olm-date-layout-probe'
out.mkdir(exist_ok=False)
with (out / 'hours.olm').open('xb') as target:
    target.write(data)
with (out / 'oracle.json').open('x', encoding='utf-8') as target:
    json.dump({'originalSha256': hashlib.sha256(original).hexdigest(), 'synthetic': True, 'rows': rows}, target, indent=2)
assert source.read_bytes() == original
print(f'Created layout-preserving derivative with {len(rows)} date pairs; ZIP CRCs valid, original unchanged.')
