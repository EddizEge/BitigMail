"""Isolated, checkpointed Stage 2 scale measurement. Does not regenerate sources.

Run only after the controller has reserved port 6175 and supplied a tested snapshot.
Use --small for a 12-message harness smoke. A unique run keeps all phase evidence.
"""
import argparse
import datetime
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import sqlite3
import sys
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('--snapshot', required=True, type=Path)
parser.add_argument('--run-id', default='scale-' + uuid.uuid4().hex[:8])
parser.add_argument('--small', action='store_true')
args = parser.parse_args()
if not re.fullmatch(r'[a-z0-9-]+', args.run_id):
    raise SystemExit('Invalid run id')
RUN = ROOT / '.codex-coordination/evidence/TASK-029' / args.run_id
RUN.mkdir(parents=True, exist_ok=True)
ATTEMPT = 'attempt-' + uuid.uuid4().hex[:8]
OUT = RUN / ATTEMPT
OUT.mkdir()
old_argv = sys.argv
sys.argv = [str(__file__), 'baseline']
spec = importlib.util.spec_from_file_location('accepted_benchmark', ROOT / 'lab/task022/scripts/benchmark_preview_export.py')
b = importlib.util.module_from_spec(spec)
spec.loader.exec_module(b)
sys.argv = old_argv
COUNT = 12 if args.small else 8192
SOURCE = ROOT / ('fixtures/mail-corpus-v1/eml' if args.small else 'runtime/task019/stage-1g/source/eml-tree')
SCOPE = {'companyId': 'company-task020-scale', 'projectId': 'project-task020-1g-gate',
         'companyName': 'TASK029 Measurement', 'projectName': args.run_id}
ACCOUNT = 'acc_ad07dbaf54b44cd58bf64e88e037f811'
FOLDER = 'Task029/' + args.run_id + '/Bulk'
STATE_PATH = RUN / 'checkpoint.json'
state = json.loads(STATE_PATH.read_text(encoding='utf-8')) if STATE_PATH.exists() else {'runId': args.run_id, 'small': args.small, 'phases': {}}
if state['small'] != args.small:
    raise SystemExit('Run mode differs from checkpoint')


def utc():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def save(name, value):
    with (OUT / name).open('x', encoding='utf-8') as f:
        json.dump(value, f, ensure_ascii=False, indent=2)
        f.flush()
        os.fsync(f.fileno())


def atomic(path, value):
    tmp = path.with_name(path.name + '.' + uuid.uuid4().hex + '.tmp')
    with tmp.open('x', encoding='utf-8') as f:
        json.dump(value, f, ensure_ascii=False, indent=2)
        f.flush()
        os.fsync(f.fileno())
    os.replace(tmp, path)


def checkpoint():
    state['updatedAtUtc'] = utc()
    atomic(STATE_PATH, state)


def check(name, passed, detail=None):
    if not passed:
        raise AssertionError(name + (': ' + str(detail) if detail else ''))


b.EVIDENCE_DIR = OUT
b.save_json = save
b.check = check
b.base_pipeline.EVIDENCE_DIR = OUT
b.base_pipeline.save_json = save
b.base_pipeline.check = check
b.TESTING_HOST_DLL = args.snapshot.resolve() / 'BitigMail.TestingHost.dll'
b.base_pipeline.TESTING_HOST_DLL = b.TESTING_HOST_DLL
if not b.TESTING_HOST_DLL.is_file():
    raise SystemExit('Tested snapshot DLL missing')


def api(method, path, body=None):
    # No blind retries for side-effecting calls. Requests/keys are checkpointed first.
    return b.api_call(method, path, body, max_retries=2 if method == 'GET' else 1, timeout=1800 if 'preview' in path else 60)


def measured_call(label, method, path, body):
    save(label + '-request.json', {'path': path, 'body': body, 'utc': utc()})
    telemetry.set_phase(label)
    started = time.perf_counter()
    cpu = process.cpu_times()
    result = api(method, path, body)
    ended = time.perf_counter()
    cpu_end = process.cpu_times()
    telemetry.set_phase('idle-' + label)
    save(label + '-response.json', result)
    save(label + '-time.json', {'elapsedSec': ended - started, 'cpuSec': cpu_end.user + cpu_end.system - cpu.user - cpu.system})
    return result


def run_job(name, preview, start_path, resume_path=None):
    phase = state['phases'].setdefault(name, {})
    if phase.get('complete'):
        job = api('GET', '/api/jobs/' + phase['jobId'])
        check(name + ' restored completed job', job['status'] == 'completed')
        return phase
    started = time.perf_counter()
    cpu = process.cpu_times()
    telemetry.set_phase(name + '-starting')
    if 'jobId' not in phase:
        # Retain the logical request if the previous response was lost after POST.
        # The server's idempotency check can then recover the already-created job.
        if 'request' not in phase:
            phase['request'] = {**SCOPE, 'previewId': preview['previewId'], 'idempotencyKey': uuid.uuid4().hex}
            phase['startedAtUtc'] = utc()
        checkpoint()
        response = api('POST', start_path, phase['request'])
        phase.update({'jobId': response['jobId'], 'archiveId': response.get('archiveId')})
        checkpoint()  # durable before waiting for this job
        save(name + '-start-response.json', response)
    else:
        job = api('GET', '/api/jobs/' + phase['jobId'])
        if job['status'] == 'interrupted':
            check(name + ' resume supported', resume_path is not None)
            phase['recovered'] = True
            checkpoint()
            api('POST', resume_path + phase['jobId'], SCOPE)
        else:
            check(name + ' is active/completed', job['status'] in ('queued', 'converting', 'verifying', 'completed'))
    previous = None
    last_progress = 0.0
    while True:
        job = api('GET', '/api/jobs/' + phase['jobId'])
        status = job['status']
        label = name + '-' + ('verifying' if status == 'verifying' else 'transfer')
        terminal = status in ('completed', 'failed', 'interrupted', 'cancelled', 'needs-attention')
        if not terminal and label != previous:
            telemetry.set_phase(label)
            previous = label
        now = time.perf_counter()
        if now - last_progress >= 5 or terminal:
            progress = {'utc': utc(), 'phase': name, 'jobId': phase['jobId'], 'status': status,
                        'itemsWritten': job.get('itemsWritten'), 'totalItems': job.get('totalItems'),
                        'phaseCompleted': job.get('phaseCompleted'), 'phaseTotal': job.get('phaseTotal'),
                        'segmentElapsedSec': now - started}
            atomic(RUN / 'progress.json', progress)
            last_progress = now
        if terminal:
            break
        check(name + ' bounded 4h watchdog', now - started < 14400)
        time.sleep(0.5)
    telemetry.set_phase('idle-after-' + name)
    cpu_end = process.cpu_times()
    segment = {'attempt': ATTEMPT, 'elapsedSec': time.perf_counter() - started,
               'cpuSec': cpu_end.user + cpu_end.system - cpu.user - cpu.system, 'terminalStatus': status}
    phase.setdefault('segments', []).append(segment)
    save(name + '-terminal.json', job)
    checkpoint()
    check(name + ' completed', status == 'completed', job.get('errorMessage'))
    check(name + ' count', job.get('itemsWritten') == COUNT)
    if name != 'archive':
        report = api('GET', '/api/jobs/' + phase['jobId'] + '/report')
        save(name + '-report.json', report)
        phase['outputPath'] = report.get('bridgeTransfer', {}).get('outputPath')
    phase['complete'] = True
    phase['completedAtUtc'] = utc()
    checkpoint()
    return phase


host_pid = None
telemetry = None
try:
    check('6175 is unowned/free', b.get_listening_pid(6175) is None)
    check('disk reserve >= 26GiB', b.psutil.disk_usage(str(ROOT.anchor)).free >= 26 * 1024**3)
    check('available RAM >= 2GiB', b.psutil.virtual_memory().available >= 2 * 1024**3)
    save('snapshot.json', {'dll': str(b.TESTING_HOST_DLL), 'dllSha256': hashlib.sha256(b.TESTING_HOST_DLL.read_bytes()).hexdigest(), 'attempt': ATTEMPT})
    original = b.task019_oracle.audit_dovecot_source_invariance(b.CONFIG_PATH, b.BASELINE_PATH)
    check('original source12/4', original['pass'])
    save('original-before.json', original)
    source = b.bridge_oracle.files_snapshot(SOURCE, 'eml')
    check('physical source count', len(source['messages']) == COUNT)
    raw_bytes = sum(m['rawBytes'] for m in source['messages'])
    if not args.small:
        check('physical source bytes', raw_bytes == 1070776928)
    save('source-before.json', source)
    state['rawSourceBytes'] = raw_bytes
    checkpoint()
    child, host_pid = b.start_testing_host('testinghost.log')
    process = b.psutil.Process(host_pid)
    telemetry = b.PhaseTelemetryCollector(host_pid, OUT / 'telemetry.csv', 0.1)
    telemetry.start()
    telemetry.set_phase('idle-preflight')
    account = api('GET', '/api/accounts/' + ACCOUNT + '?companyId=' + SCOPE['companyId'] + '&projectId=' + SCOPE['projectId'])
    check('expected lab account', account['accountId'] == ACCOUNT)

    if not state['phases'].get('import', {}).get('jobId'):
        api('POST', '/api/testing/set-mime-source', {'fixtureId': 'corpus-tree' if args.small else 'task019-tree-1g'})
        picked = api('POST', '/api/picker/mime-source', {'mode': 'eml-tree'})
        desc = measured_call('source-describe', 'POST', '/api/transfer/bridge/source/describe', {'sourceHandle': picked['handle']})
        check('described count', desc['totalItems'] == COUNT)
        folders = [f['folderName'] for f in desc['folders']]
        imp = measured_call('import-preview', 'POST', '/api/transfer/bridge/import/preview',
                            {**SCOPE, 'sourceHandle': picked['handle'], 'targetAccountId': ACCOUNT,
                             'selectedFolders': folders, 'targetFolderMappings': {f: FOLDER for f in folders}})
        check('import eligible', imp['canTransfer'] and imp['eligibleItemsCount'] == COUNT)
    else:
        imp = {}
    run_job('import', imp, '/api/transfer/bridge/import/start', '/api/transfer/bridge/import/resume/')
    for phase_name, fmt in [('mbox', 'mboxrd'), ('eml', 'eml-tree')]:
        if not state['phases'].get(phase_name, {}).get('jobId'):
            target = api('POST', '/api/picker/output-dir', {})
            preview = measured_call(phase_name + '-preview', 'POST', '/api/transfer/bridge/export/preview',
                                    {**SCOPE, 'sourceAccountId': ACCOUNT, 'targetDirHandle': target['handle'],
                                     'selectedFolders': [FOLDER], 'targetFormat': fmt})
            check(phase_name + ' eligible', preview['canTransfer'] and preview['eligibleItemsCount'] == COUNT)
        else:
            preview = {}
        run_job(phase_name, preview, '/api/transfer/bridge/export/start', '/api/transfer/bridge/export/resume/')
    if not state['phases'].get('archive', {}).get('jobId'):
        preview = measured_call('archive-preview', 'POST', '/api/archive/ingest/preview',
                                {**SCOPE, 'sourceJobId': state['phases']['eml']['jobId'], 'archiveName': 'Task029-' + args.run_id})
        check('archive eligible', preview['canIngest'] and preview['totalItems'] == COUNT)
    else:
        preview = {}
    archive = run_job('archive', preview, '/api/archive/ingest/start', '/api/archive/ingest/resume/')
    telemetry.set_phase('independent-oracle')
    target = b.bridge_oracle.imap_snapshot(b.CONFIG_PATH, 'target', [FOLDER])
    save('imap-snapshot.json', target)
    comparison = b.bridge_oracle.compare(source, target, exact=False, metadata=False)
    save('source-to-imap.json', comparison)
    check('source->IMAP multiset', comparison['pass'] and comparison['missing'] == comparison['extra'] == 0)
    for phase_name, fmt in [('mbox', 'mbox'), ('eml', 'eml')]:
        output = Path(state['phases'][phase_name]['outputPath'])
        snap = b.bridge_oracle.files_snapshot(output, fmt)
        comparison = b.bridge_oracle.compare(target, snap, exact=True, metadata=False)
        save(phase_name + '-snapshot.json', snap)
        save(phase_name + '-comparison.json', comparison)
        check(phase_name + ' exactraw', comparison['pass'] and comparison['missing'] == comparison['extra'] == 0)
        sidecar = b.bridge_oracle.verify_manifest(target, json.loads((output / 'manifest.json').read_text(encoding='utf-8-sig')), output)
        save(phase_name + '-sidecar.json', sidecar)
        check(phase_name + ' metadata', sidecar['pass'])
    archive_dir = ROOT / 'runtime/testing-engine/archives' / archive['archiveId']
    archived = b.bridge_oracle.files_snapshot(archive_dir, 'eml')
    archive_comparison = b.bridge_oracle.compare(target, archived, exact=True, metadata=False)
    save('archive-comparison.json', archive_comparison)
    check('archive exact raw multiset', archive_comparison['pass'] and archive_comparison['missing'] == archive_comparison['extra'] == 0)
    sys.path.insert(0, str(ROOT / 'lab/task020/scripts'))
    import verify_task020_1g_oracle as oracle
    archive_manifest = json.loads((archive_dir / 'manifest.json').read_text(encoding='utf-8-sig'))
    db = archive_dir.parent / 'archive-search.db'
    with sqlite3.connect(db.resolve().as_uri() + '?mode=ro', uri=True, timeout=30) as connection:
        integrity = connection.execute('PRAGMA integrity_check').fetchone()[0]
        ids = [row[0] for row in connection.execute('SELECT item_id FROM message_metadata WHERE archive_id=?', (archive['archiveId'],))]
        fts = connection.execute('SELECT COUNT(*) FROM messages_fts f JOIN message_metadata m ON m.rowid=f.rowid WHERE m.archive_id=?', (archive['archiveId'],)).fetchone()[0]
    expected_ids = {item['itemId'] for item in archive_manifest['items']}
    sqlite_audit = {'pass': integrity == 'ok' and len(ids) == len(set(ids)) == len(expected_ids) == fts == COUNT and set(ids) == expected_ids,
                    'integrity': integrity, 'rows': len(ids), 'ftsRows': fts, 'archiveId': archive['archiveId']}
    save('archive-sqlite.json', sqlite_audit)
    check('archive SQLite identities/integrity', sqlite_audit['pass'])
    if not args.small:
        telemetry.set_phase('archive-search')
        search = oracle.audit_search_paged_identity(api, [{**SCOPE, 'archiveId': archive['archiveId']}],
                   json.loads((ROOT / 'runtime/task019/stage-1g/source/manifest.json').read_text(encoding='utf-8-sig')))
        save('archive-search.json', search)
        check('archive search exact identities', search['pass'])
    after = b.bridge_oracle.files_snapshot(SOURCE, 'eml')
    save('source-after.json', after)
    check('physical source unchanged', source == after)
    original = b.task019_oracle.audit_dovecot_source_invariance(b.CONFIG_PATH, b.BASELINE_PATH)
    save('original-after.json', original)
    check('original source unchanged', original['pass'])
    state['integrityPass'] = True
    state['oracleAttempt'] = ATTEMPT
    checkpoint()
    save('summary.json', state)
except Exception as exc:
    save('failure.json', {'type': type(exc).__name__, 'error': str(exc), 'utc': utc()})
    raise
finally:
    if telemetry is not None:
        telemetry.stop()
        save('telemetry-summary.json', b.telemetry_result(telemetry))
    if host_pid is not None and b.base_pipeline.active_host_proc is not None and b.base_pipeline.active_host_proc.poll() is None:
        b.kill_and_verify_stopped(host_pid)
