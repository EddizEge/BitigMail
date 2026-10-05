#!/usr/bin/env python3
"""TASK-023 isolated 12-message real export capacity smoke."""
import importlib.util
import json
import pathlib
import sys
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.argv = ['benchmark_preview_export.py', 'after']
spec = importlib.util.spec_from_file_location('task022_harness', ROOT / 'lab/task022/scripts/benchmark_preview_export.py')
h = importlib.util.module_from_spec(spec)
spec.loader.exec_module(h)
# Importing the reusable harness allocates its unique run directory; remove that
# still-empty directory before redirecting all evidence to TASK-023.
h.EVIDENCE_DIR.rmdir()

run_id = 'smoke-' + uuid.uuid4().hex[:8]
evidence = ROOT / '.codex-coordination/evidence/TASK-023' / run_id
evidence.mkdir(parents=True)
h.RUN_ID = run_id
h.EVIDENCE_DIR = evidence
h.base_pipeline.RUN_ID = run_id
h.base_pipeline.EVIDENCE_DIR = evidence
h.base_pipeline.save_json = h.save_json
h.checks.clear()

scope = {'companyId': 'root-bridge-83f6a9e7', 'projectId': 'local-pilot'}
account_id = 'acc_2fe8eb7af4f1498d960ba324603be4d7'
folders = ['INBOX', 'Gönderilenler', 'Projeler/İstanbul']
host_pid = None
protected = {p: h.process_identity(h.get_listening_pid(p)) for p in (5173,) if h.get_listening_pid(p)}
summary = {'taskId': 'TASK-023', 'runId': run_id}

try:
    h.check('6175 free', h.get_listening_pid(6175) is None)
    before = h.bridge_oracle.imap_snapshot(h.CONFIG_PATH, 'source', folders)
    h.check('original source is 12/4', before['count'] == 12 and before['attachments'] == 4)
    h.save_json('source-before.json', before)
    h.base_pipeline.TESTING_HOST_DLL = h.TESTING_HOST_DLL
    proc, host_pid = h.start_testing_host('testinghost.log')
    h.check('owned TestingHost child', proc.pid == host_pid)
    account = h.api_call('GET', f"/api/accounts/{account_id}?companyId={scope['companyId']}&projectId={scope['projectId']}")
    h.check('preserved source account', account.get('accountId') == account_id)
    output = h.api_call('POST', '/api/picker/output-dir', {}, max_retries=1)
    request = {**scope, 'sourceAccountId': account_id, 'targetDirHandle': output['handle'],
               'targetFormat': 'eml-tree', 'selectedFolders': folders}
    h.save_json('preview-request.json', request)
    preview = h.api_call('POST', '/api/transfer/bridge/export/preview', request, max_retries=1)
    h.save_json('preview-response.json', preview)
    h.check('preview eligible 12 and transferable', preview.get('eligibleItemsCount') == 12 and preview.get('canTransfer') is True)
    h.check('estimated required bytes positive', isinstance(preview.get('estimatedRequiredBytes'), int) and preview['estimatedRequiredBytes'] > 0)
    h.check('available bytes sufficient', isinstance(preview.get('availableFreeBytes'), int) and preview['availableFreeBytes'] >= preview['estimatedRequiredBytes'])
    start_request = {**scope, 'previewId': preview['previewId'], 'idempotencyKey': uuid.uuid4().hex}
    h.save_json('start-request.json', start_request)
    started = h.api_call('POST', '/api/transfer/bridge/export/start', start_request, max_retries=1)
    h.save_json('start-response.json', started)
    job = h.wait_for_job(started['jobId'], timeout_sec=120)
    h.check('small export completed', job.get('status') == 'completed')
    report = h.api_call('GET', f"/api/jobs/{started['jobId']}/report")
    h.save_json('report.json', report)
    path = pathlib.Path(report['bridgeTransfer']['outputPath'])
    files = h.bridge_oracle.files_snapshot(path, 'eml')
    comparison = h.bridge_oracle.compare(before, files, exact=True, metadata=False)
    h.save_json('files.json', files)
    h.save_json('source-to-files.json', comparison)
    h.check('12-message exact raw output', comparison['pass'] and comparison['missing'] == 0 and comparison['extra'] == 0)
    after = h.bridge_oracle.imap_snapshot(h.CONFIG_PATH, 'source', folders)
    unchanged = h.bridge_oracle.compare(before, after, exact=True, metadata=True)
    h.save_json('source-after.json', after)
    h.save_json('source-unchanged.json', unchanged)
    h.check('source unchanged', unchanged['pass'])
    summary.update({'status': 'PASS', 'jobId': started['jobId'], 'estimatedRequiredBytes': preview['estimatedRequiredBytes'],
                    'availableFreeBytes': preview['availableFreeBytes'], 'checks': h.checks})
    h.save_json('summary.json', summary)
finally:
    if host_pid and h.get_listening_pid(6175) == host_pid:
        h.kill_and_verify_stopped(host_pid, 6175)
    h.check('6175 off', h.get_listening_pid(6175) is None)
    for port, identity in protected.items():
        h.check(f'{port} identity preserved', h.process_identity(h.get_listening_pid(port)) == identity)

print(json.dumps(summary, ensure_ascii=False))
