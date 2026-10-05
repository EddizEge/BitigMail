"""TASK018 independent protected HTTP acceptance. Synthetic data only; no deletes."""
import collections, hashlib, json, pathlib, sys, time, urllib.request, urllib.error, uuid

ROOT = pathlib.Path(r'C:\Users\Eddiz\Documents\ChatGPT\Mail Manager')
sys.path.insert(0, str(ROOT / 'lab/task017'))
import verify_bridge as oracle
RUN = ROOT / '.codex-coordination/evidence/TASK-018' / ('api-' + uuid.uuid4().hex[:10])
RUN.mkdir(parents=True)
checks, archives, token = [], [], None

def save(name, value):
    (RUN / name).write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')

def check(name, condition):
    checks.append({'name': name, 'pass': bool(condition)})
    if not condition:
        raise AssertionError(name)

def call(method, path, body=None, expected=(200,), session=True):
    headers = {'Origin': 'http://127.0.0.1:5173', 'Content-Type': 'application/json'}
    if token and session: headers['X-BitigMail-Session'] = token
    raw = json.dumps(body or {}).encode() if method == 'POST' else None
    req = urllib.request.Request('http://127.0.0.1:6175' + path, data=raw, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=45) as res:
            status, payload, cache = res.status, res.read(), res.headers.get('Cache-Control', '')
    except urllib.error.HTTPError as err:
        status, payload, cache = err.code, err.read(), err.headers.get('Cache-Control', '')
    try: data = json.loads(payload) if payload else None
    except ValueError: data = {'unparsedResponse': payload.decode(errors='replace')[:500]}
    if status not in expected:
        save('unexpected-response.json', {'path': path, 'status': status, 'response': data, 'request': body})
    check(path + ' HTTP ' + str(status), status in expected)
    check(path + ' no-store', 'no-store' in cache)
    return data

def wait_job(job):
    deadline = time.monotonic() + 110
    while time.monotonic() < deadline:
        result = call('GET', '/api/jobs/' + job['jobId'])
        if result['status'] in ('completed', 'failed', 'interrupted', 'cancelled', 'needs-attention'):
            break
        time.sleep(.2)
    save(job['jobId'] + '.json', result)
    check('job completed ' + job['jobId'], result['status'] == 'completed')
    return result

def selection(archive):
    return {k: archive[k] for k in ('companyId', 'projectId', 'archiveId')}

def request(scopes, **kw):
    return {'selectedScopes': [selection(s) for s in scopes], 'page': 1, 'pageSize': 100, **kw}

def search(scopes, expected_count, **kw):
    response = call('POST', '/api/archive/search', request(scopes, **kw))
    check('count ' + str(kw), response['totalCount'] == expected_count)
    check('bounded result count', len(response['items']) <= min(expected_count, kw.get('pageSize', 100)))
    check('scope membership', all(selection(item) in [selection(s) for s in scopes] for item in response['items']))
    return response

def preview(item, req, expected=(200,)):
    return call('POST', '/api/archive/message/preview', {'messageId': item['messageId'], 'searchRequest': req}, expected)

def ingest(fixture, mode, owner, count, name, source_job=None):
    req = {**owner, 'archiveName': name}
    if source_job: req['sourceJobId'] = source_job
    else:
        call('POST', '/api/testing/set-mime-source', {'fixtureId': fixture})
        picked = call('POST', '/api/picker/archive-source', {'mode': mode})
        check('source picked ' + fixture, bool(picked.get('handle')) and not picked.get('error'))
        req['sourceHandle'] = picked['handle']
    planned = call('POST', '/api/archive/ingest/preview', req)
    save(name + '-preview.json', planned)
    check('physical preview count ' + name, planned['totalItems'] == count and planned['canIngest'])
    start = {'previewId': planned['previewId'], 'idempotencyKey': uuid.uuid4().hex}
    job = call('POST', '/api/archive/ingest/start', start)
    same = call('POST', '/api/archive/ingest/start', start)
    check('same key same job', job['jobId'] == same['jobId'])
    other = call('POST', '/api/archive/ingest/start', {**start, 'idempotencyKey': uuid.uuid4().hex})
    check('same plan different key same job', job['jobId'] == other['jobId'])
    done = wait_job(job)
    check('archive job kind', done['jobKind'] == 'archive-ingest')
    catalog = call('GET', '/api/archive/catalog')
    archive = next(a for a in catalog if a['archiveId'] == done['archiveId'])
    check('catalog count/status', archive['totalItems'] == count and archive['status'] == 'ready')
    check('frozen ownership', all(archive[k] == owner[k] for k in ('companyId', 'projectId')))
    archives.append(archive)
    save('archives.json', archives)
    return archive

def verify_raw(archive, expected_raw):
    manifest = call('GET', '/api/archive/' + archive['archiveId'] + '/manifest')
    save(archive['archiveId'] + '-manifest.json', manifest)
    base = (ROOT / 'runtime/testing-engine/archives' / archive['archiveId']).resolve()
    actual = []
    for entry in manifest['items']:
        path = (base / entry['relativeEmlPath']).resolve(strict=True)
        check('stored path contained', path.is_relative_to(base))
        raw = path.read_bytes()
        digest = hashlib.sha256(raw).hexdigest()
        check('stored hash and length', digest == entry['sourceSha256'] == entry['storedSha256'] and len(raw) == entry['byteLength'])
        actual.append(digest)
    check('exact physical raw multiset', collections.Counter(actual) == collections.Counter(hashlib.sha256(raw).hexdigest() for raw in expected_raw))
    check('distinct physical IDs', len({i['itemId'] for i in manifest['items']}) == len(expected_raw))
    return manifest

def main():
    global token
    token = call('POST', '/api/session', session=False)['token']
    suffix = RUN.name
    owner_a = {'companyId': suffix + '-a', 'projectId': 'proje-a', 'companyName': 'A Şirketi ' + suffix, 'projectName': 'Arşiv Kontrolü'}
    owner_b = {'companyId': suffix + '-b', 'projectId': 'proje-b', 'companyName': 'B Şirketi ' + suffix, 'projectName': 'İstanbul'}
    corpus = ROOT / 'fixtures/mail-corpus-v1'
    eml_paths = sorted((corpus / 'eml').rglob('*.eml'))
    eml_raw = [p.read_bytes() for p in eml_paths]
    mbox_raw = [raw for _, raw in oracle.mbox_records(corpus / 'corpus.mbox')]
    edge_meta = json.loads((ROOT / '.codex-coordination/evidence/TASK-018/edge-fixture.json').read_text(encoding='utf-8-sig'))
    edge_dir = pathlib.Path(edge_meta['directory'])
    edge_raw = [(edge_dir / e['file']).read_bytes() for e in edge_meta['entries']]
    before = [hashlib.sha256(raw).hexdigest() for raw in eml_raw + mbox_raw + edge_raw]
    a = ingest('corpus-eml', 'eml-files', owner_a, 12, 'EML-' + suffix)
    b = ingest('corpus-mbox', 'mbox', owner_b, 12, 'MBOX-' + suffix)
    c = ingest('archive-edgecases', 'eml-tree', owner_a, 4, 'Sınırlar-' + suffix)
    verify_raw(a, eml_raw); verify_raw(b, mbox_raw); verify_raw(c, edge_raw)
    search([], 0); search([a], 12); search([b], 12); search([a, b], 24); search([a, b, c], 28)
    oracle_cases = json.loads((ROOT / '.codex-coordination/evidence/TASK-018/expected-fixture-search.json').read_text(encoding='utf-8-sig'))['cases']
    eml_hash = {p.name: hashlib.sha256(raw).hexdigest() for p, raw in zip(eml_paths, eml_raw)}
    for case in oracle_cases:
        kw = {('hasAttachment' if k == 'hasAttachments' else k): v for k, v in case.items() if k not in ('expectedPhysicalIds', 'expectedCount', 'timezone')}
        response = search([a], case['expectedCount'], **kw)
        if 'expectedPhysicalIds' in case:
            previews = [preview(item, request([a], **kw)) for item in response['items']]
            check('query physical identities ' + str(kw), collections.Counter(p['sha256'] for p in previews) == collections.Counter(eml_hash[i] for i in case['expectedPhysicalIds']))
    search([a, b], 8, query='istanbul', field='subject')
    page_ids = []
    for page in range(1, 6):
        result = search([a, b], 24, page=page, pageSize=5)
        page_ids += [i['messageId'] for i in result['items']]
    check('pagination all24 exactly once', len(page_ids) == len(set(page_ids)) == 24)
    for item in search([a,b,c],28)['items']:
        p = preview(item,request([a,b,c]))
        check('combined-scope preview exact physical identity',p['archiveId']==item['archiveId'] and p['messageId']==item['messageId'])
    one = search([a], 12)['items'][0]
    for req in [request([]), request([b]), request([a], query='NO_MATCH_SENTINEL_018'), request([a], startDate='2099-01-01')]:
        preview(one, req, (400, 404))
    bad_scope = {**selection(a), 'companyId': owner_b['companyId']}
    call('POST', '/api/archive/search', {'selectedScopes': [bad_scope]}, (400,))
    for kw in [{'field': 'unknown'}, {'page': 0}, {'pageSize': 101}, {'startDate': 'bad'}, {'startDate': '2025-02-01', 'endDate': '2024-01-01'}, {'query': 'x' * 513}, {'query': 'a\x00b'}]:
        call('POST', '/api/archive/search', request([a], **kw), (400,))
    call('POST', '/api/archive/search', {'selectedScopes': [selection(a), selection(a)]}, (400,))
    for text in ['istanbul OR bilgilendirme', 'subject:istanbul', '" OR *']:
        search([a], 0, query=text)
    search([c], 4); search([c], 3, startDate='2024-01-02', endDate='2024-01-02')
    search([c], 4, field='recipient', query='carbon')
    search([c], 1, field='attachmentName', query='şartname')
    for sentinel in ['HEAD_SENTINEL_018', 'STYLE_SENTINEL_018', 'SCRIPT_SENTINEL_018', 'ATTACHMENT_CONTENT_ONLY_018', 'AFTER_TRUNCATION_018']:
        search([c], 0, field='body', query=sentinel)
    html = search([c], 1, field='body', query='Özgün İstanbul')['items'][0]
    p = preview(html, request([c]))
    check('HTML text entities and blocks', all(text in p['bodyText'] for text in edge_meta['expected']['htmlVisibleText']))
    truncated = [i for i in search([c], 4)['items'] if i['isBodyTruncated']]
    check('one truncated body', len(truncated) == c['truncatedItemsCount'] == 1)
    p = preview(truncated[0], request([c]))
    check('bounded valid Unicode preview', len(p['bodyText'].encode('utf-8')) <= 512 * 1024 and '\ufffd' not in p['bodyText'] and 'AFTER_TRUNCATION_018' not in p['bodyText'])
    check('selected scope truncation warning', bool(search([c], 0, query='NO_MATCH_SENTINEL_018').get('warning')))
    stable_ids = {i['messageId'] for i in search([a],12)['items']}
    rebuilt = wait_job(call('POST', '/api/archive/' + a['archiveId'] + '/reindex', {}))
    check('reindex job kind', rebuilt['jobKind'] == 'archive-reindex')
    check('public physical IDs stable after reindex',stable_ids == {i['messageId'] for i in search([a],12)['items']})
    search([a, b, c], 28)
    after = [hashlib.sha256(raw).hexdigest() for raw in [p.read_bytes() for p in eml_paths] + [r for _, r in oracle.mbox_records(corpus / 'corpus.mbox')] + [(edge_dir / e['file']).read_bytes() for e in edge_meta['entries']]]
    check('all synthetic original raw unchanged', before == after)
    bridge_id = 'job-3b6cab7b2f7c'
    bridge = call('GET', '/api/jobs/' + bridge_id)
    bridge_owner = {k: bridge['clientContext'][k] for k in ('companyId', 'projectId', 'companyName', 'projectName')}
    bridge_root = pathlib.Path(bridge['outputPath'])
    export_manifest = json.loads((bridge_root / 'manifest.json').read_text(encoding='utf-8-sig'))
    bridge_source_before = {str(p.relative_to(bridge_root)): hashlib.sha256(p.read_bytes()).hexdigest() for p in bridge_root.rglob('*') if p.is_file()}
    save('bridge-source-before.json', bridge_source_before)
    check('completed original bridge export12', bridge['status'] == 'completed' and len(export_manifest['items']) == 12)
    call('POST', '/api/archive/ingest/preview', {**owner_a, 'archiveName': 'Wrong owner', 'sourceJobId': bridge_id}, (400,))
    d = ingest(None, None, bridge_owner, 12, 'Aktarım-' + suffix, source_job=bridge_id)
    check('completed bridge output and sidecar unchanged', bridge_source_before == {str(p.relative_to(bridge_root)): hashlib.sha256(p.read_bytes()).hexdigest() for p in bridge_root.rglob('*') if p.is_file()})
    if export_manifest['targetFormat'] == 'mboxrd':
        bridge_raw = [raw for name in sorted({i['relativeOutputPath'] for i in export_manifest['items']}) for _, raw in oracle.mbox_records(bridge_root / name)]
    else:
        bridge_raw = [(bridge_root / i['relativeOutputPath']).read_bytes() for i in export_manifest['items']]
    bridge_archive_manifest = verify_raw(d, bridge_raw)
    check('bridge original folder physical histogram', collections.Counter(i['originalFolder'] for i in bridge_archive_manifest['items']) == collections.Counter(i['originalFolder'] for i in export_manifest['items']))
    def archived_internal_date(item):
        return item.get('sourceInternalDateUtc', item.get('internalDateUtc', item.get('originalInternalDateUtc')))
    def instant(value):
        import datetime
        return None if value is None else datetime.datetime.fromisoformat(value.replace('Z', '+00:00')).isoformat()
    expected_dates = collections.Counter((i['storedSha256'], i['originalFolder'], instant(i.get('internalDateUtc'))) for i in export_manifest['items'])
    actual_dates = collections.Counter((i['storedSha256'], i['originalFolder'], instant(archived_internal_date(i))) for i in bridge_archive_manifest['items'])
    check('bridge INTERNALDATE distinct from MIME Date retained', actual_dates == expected_dates)
    search([a, b, c, d], 40)
    # Only the newly-created synthetic managed copy is changed, then restored exactly.
    req = request([a], field='attachmentName', query='şartname')
    item = search([a], 1, field='attachmentName', query='şartname')['items'][0]
    original_preview = preview(item, req)
    manifest = call('GET', '/api/archive/' + a['archiveId'] + '/manifest')
    entry = next(i for i in manifest['items'] if i['storedSha256'] == original_preview['sha256'])
    managed_base = (ROOT / 'runtime/testing-engine/archives' / a['archiveId']).resolve()
    managed_file = (managed_base / entry['relativeEmlPath']).resolve(strict=True)
    check('owned synthetic corruption target', a['companyId'] == owner_a['companyId'] and managed_file.is_relative_to(managed_base))
    original_raw = managed_file.read_bytes()
    try:
        managed_file.write_bytes(original_raw + b'\r\nTASK018_MANAGED_COPY_TAMPER\r\n')
        preview(item, req, (400, 409, 422))
    finally:
        managed_file.write_bytes(original_raw)
    check('managed copy restored after integrity probe', hashlib.sha256(managed_file.read_bytes()).hexdigest() == entry['storedSha256'])

if __name__ == '__main__':
    completed = False
    try:
        main(); completed = True
    finally:
        save('report.json', {'completed': completed, 'checks': checks, 'archives': archives})
        print(json.dumps({'completed': completed, 'passed': sum(c['pass'] for c in checks), 'failed': sum(not c['pass'] for c in checks), 'evidence': str(RUN)}, ensure_ascii=False))
