"""Actual protected TestingHost API and independent read-only lab/file oracle.
Creates only isolated local account records and new TASK017 target folders.
No DELETE, EXPUNGE, MOVE, original source seed or real-provider operations.
"""
import datetime
import json
import pathlib
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
import verify_bridge as oracle

ROOT = pathlib.Path(__file__).resolve().parents[2]
RUN = ROOT / '.codex-coordination/evidence/TASK-017' / ('api-' + uuid.uuid4().hex[:10])
RUN.mkdir(parents=True)
CONFIG_PATH = ROOT / 'lab/task014/dovecot/local-credentials.json'
CONFIG = json.loads(CONFIG_PATH.read_text(encoding='utf-8-sig'))
for role in ('source', 'target'):
    if CONFIG[role]['imapHost'] != '127.0.0.1' or CONFIG[role]['imapPort'] != 5143:
        raise RuntimeError('Only isolated Dovecot lab is authorized')
SECRETS = [CONFIG[r]['password'] for r in ('source', 'target')]
SCOPE = {'companyId': 'root-bridge-' + uuid.uuid4().hex[:8], 'projectId': 'local-pilot',
         'companyName': 'BitigMail Bağımsız Test', 'projectName': 'Dosya ve hesap köprüsü'}
FOLDERS = ['INBOX', 'Gönderilenler', 'Projeler/İstanbul']
checks, jobs = [], []
token = None


def save(name, data):
    (RUN / name).write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')


def check(name, condition):
    checks.append({'name': name, 'pass': bool(condition)})
    if not condition:
        raise AssertionError(name)


def call(method, path, body=None, expected=(200,), session=True):
    headers = {'Origin': 'http://127.0.0.1:5173', 'Content-Type': 'application/json'}
    if session and token:
        headers['X-BitigMail-Session'] = token
    raw = None if body is None else json.dumps(body).encode('utf-8')
    if method == 'POST' and raw is None:
        raw = b'{}'
    request = urllib.request.Request('http://127.0.0.1:6175' + path, data=raw, headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=50) as response:
            status, payload, response_headers = response.status, response.read(), response.headers
    except urllib.error.HTTPError as error:
        status, payload, response_headers = error.code, error.read(), error.headers
    text = payload.decode('utf-8')
    check(path + ' secret-free', all(secret not in text for secret in SECRETS))
    check(path + ' no-store', 'no-store' in response_headers.get('Cache-Control', ''))
    result = json.loads(text) if text else None
    if path != '/api/session' and status not in expected:
        save('unexpected-response.json', {'path': path, 'status': status, 'response': result})
    check(path + ' status', status in expected)
    return result


def create_account(role):
    cfg = CONFIG[role]
    return call('POST', '/api/accounts', {**SCOPE, 'displayName': 'TASK017 ' + role,
        'email': cfg['email'], 'host': cfg['imapHost'], 'port': cfg['imapPort'],
        'tlsMode': 'none', 'username': cfg['username'], 'password': cfg['password']})


def run_job(direction, preview, count):
    start_path = '/api/transfer/bridge/' + direction + '/start'
    key = uuid.uuid4().hex
    body = {**SCOPE, 'previewId': preview['previewId'], 'idempotencyKey': key}
    job = call('POST', start_path, body)
    jobs.append(job['jobId'])
    same = call('POST', start_path, body)
    check(direction + ' same key returns same job', same['jobId'] == job['jobId'])
    other_key = call('POST', start_path, {**body, 'idempotencyKey': uuid.uuid4().hex}, expected=(200, 409))
    if 'jobId' in other_key:
        check(direction + ' same plan never starts new job', other_key['jobId'] == job['jobId'])
    deadline = time.monotonic() + 100
    while time.monotonic() < deadline:
        job = call('GET', '/api/jobs/' + job['jobId'])
        if job['status'] in ('completed', 'failed', 'interrupted', 'needs-attention', 'cancelled'):
            break
        time.sleep(0.25)
    save(job['jobId'] + '.json', job)
    check(direction + ' completed', job['status'] == 'completed')
    report = call('GET', '/api/jobs/' + job['jobId'] + '/report')
    save(job['jobId'] + '-report.json', report)
    detail = report['bridgeTransfer']
    check(direction + ' verified physical count', detail['totalVerified'] == count and detail['totalPlanned'] == count)
    check(direction + ' no failed or ambiguous items', detail['totalFailed'] == 0 and detail['totalNeedsAttention'] == 0)
    return job, report


finished = False
try:
    token = call('POST', '/api/session', {}, session=False)['token']
    accounts = {role: create_account(role) for role in ('source', 'target')}
    save('accounts.json', accounts)
    before = oracle.imap_snapshot(CONFIG_PATH, 'source', FOLDERS)
    save('source-before.json', before)
    check('original source baseline12/4', before['count'] == 12 and before['attachments'] == 4)
    for fixture, mode, source_folder, baseline_kind, baseline_path in [
        ('corpus-eml', 'eml-files', 'Corpus', 'eml', ROOT / 'fixtures/mail-corpus-v1/eml'),
        ('corpus-mbox', 'mbox', 'corpus', 'mbox', ROOT / 'fixtures/mail-corpus-v1/corpus.mbox')]:
        call('POST', '/api/testing/set-mime-source', {'fixtureId': fixture})
        selected = call('POST', '/api/picker/mime-source', {'mode': mode})
        check(fixture + ' picker accepted', selected.get('handle') and not selected.get('error'))
        target_folder = 'TASK017-' + RUN.name + '-' + fixture
        request = {**SCOPE, 'sourceHandle': selected['handle'], 'targetAccountId': accounts['target']['accountId'],
                   'selectedFolders': [source_folder], 'targetFolderMappings': {source_folder: target_folder}}
        call('POST', '/api/transfer/bridge/import/preview', {**request, 'selectedFolders': []}, expected=(400, 409))
        preview = call('POST', '/api/transfer/bridge/import/preview', request)
        save(fixture + '-preview.json', preview)
        check(fixture + ' preview12', preview['canTransfer'] and preview['eligibleItemsCount'] == 12)
        run_job('import', preview, 12)
        expected = oracle.files_snapshot(baseline_path, baseline_kind)
        actual = oracle.imap_snapshot(CONFIG_PATH, 'target', [target_folder])
        save(fixture + '-target.json', actual)
        comparison = oracle.compare(expected, actual, exact=False, metadata=False)
        save(fixture + '-independent.json', comparison)
        check(fixture + ' independent byte/header/decoded multiset', comparison['pass'])
        check(fixture + ' unread default', all('\\Seen' not in message['flags'] and '\\Deleted' not in message['flags'] for message in actual['messages']))
    for target_format in ('eml-tree', 'mboxrd'):
        output = call('POST', '/api/picker/output-dir', {})
        preview = call('POST', '/api/transfer/bridge/export/preview', {**SCOPE,
            'sourceAccountId': accounts['source']['accountId'], 'targetDirHandle': output['handle'],
            'targetFormat': target_format, 'selectedFolders': FOLDERS})
        save(target_format + '-preview.json', preview)
        check(target_format + ' preview12', preview['canTransfer'] and preview['eligibleItemsCount'] == 12)
        job, report = run_job('export', preview, 12)
        path = report['bridgeTransfer']['outputPath']
        actual = oracle.files_snapshot(path, 'eml' if target_format == 'eml-tree' else 'mbox')
        save(target_format + '-files.json', actual)
        comparison = oracle.compare(before, actual, exact=True, metadata=False)
        save(target_format + '-independent.json', comparison)
        check(target_format + ' independent exact raw/header/parts', comparison['pass'])
    after = oracle.imap_snapshot(CONFIG_PATH, 'source', FOLDERS)
    save('source-after.json', after)
    unchanged = oracle.compare(before, after, exact=True, metadata=True)
    save('source-unchanged.json', unchanged)
    check('source unchanged including flags/dates/UIDs', unchanged['pass'])
    finished = True
finally:
    result = {'pass': finished and all(c['pass'] for c in checks), 'finished': finished, 'checks': checks,
              'jobs': jobs, 'evidenceDirectory': str(RUN)}
    save('acceptance.json', result)
    (RUN.parent / 'api-root-latest.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'finished': finished, 'checks': len(checks), 'passed': sum(c['pass'] for c in checks),
                      'failed': [c['name'] for c in checks if not c['pass']], 'evidenceDirectory': str(RUN)}))
