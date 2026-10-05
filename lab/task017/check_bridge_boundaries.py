"""Protected API error-boundary checks; no mailbox operations or existing record edits."""
import json
import pathlib
import urllib.error
import urllib.request
import uuid

root = pathlib.Path(__file__).resolve().parents[2]
checks = []
token = None


def call(path, body, expected):
    headers = {'Origin': 'http://127.0.0.1:5173', 'Content-Type': 'application/json'}
    if token:
        headers['X-BitigMail-Session'] = token
    request = urllib.request.Request('http://127.0.0.1:6175' + path, data=json.dumps(body).encode(), headers=headers, method='POST')
    try:
        with urllib.request.urlopen(request, timeout=15) as response:
            status, raw, response_headers = response.status, response.read().decode(), response.headers
    except urllib.error.HTTPError as error:
        status, raw, response_headers = error.code, error.read().decode(), error.headers
    safe = not any(text in raw for text in ('System.ArgumentException', 'System.InvalidOperationException', ' at BitigMail.', 'X-BitigMail-Session'))
    safe = safe and (not token or token not in raw)
    ok = status in expected and safe and 'no-store' in response_headers.get('Cache-Control', '')
    checks.append({'path': path, 'status': status, 'expected': expected, 'safeError': safe, 'pass': ok})
    # Never write a leaked token or raw exception to evidence.
    return json.loads(raw) if safe else {}


token = call('/api/session', {}, [200])['token']
for direction in ('import', 'export'):
    for identifier in ('../outside', 'bad:stream', 'x' * 129, None):
        call('/api/transfer/bridge/' + direction + '/start',
             {'previewId': identifier, 'idempotencyKey': uuid.uuid4().hex}, [400, 409])
    # A new deliberately invalid task-owned plan, never an existing user's record.
    plan_id = 'bprev_root_invalid_' + uuid.uuid4().hex
    directory = root / 'runtime/testing-engine/bridge-transfers' / (direction + '-plans')
    if not directory.is_dir():
        raise RuntimeError('Expected isolated TestingHost journal directory is missing')
    with (directory / (plan_id + '.json')).open('x', encoding='utf-8') as output:
        output.write('{invalid synthetic root fixture}')
    call('/api/transfer/bridge/' + direction + '/start',
         {'previewId': plan_id, 'idempotencyKey': uuid.uuid4().hex}, [400, 409])

result = {'pass': all(c['pass'] for c in checks), 'checks': checks}
path = root / '.codex-coordination/evidence/TASK-017/root-api-boundaries.json'
path.write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps({'pass': result['pass'], 'checks': len(checks), 'failed': [c for c in checks if not c['pass']]}))
raise SystemExit(0 if result['pass'] else 1)
