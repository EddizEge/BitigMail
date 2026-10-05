#!/usr/bin/env python3
"""TASK-022 bounded preview/export benchmark for the preserved 128 MiB target."""
import collections
import datetime
import hashlib
import json
import os
import pathlib
import psutil
import re
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
import uuid

ROOT = pathlib.Path(r'C:\Users\Eddiz\Documents\ChatGPT\Mail Manager').resolve()
sys.path.insert(0, str(ROOT / 'lab/task019/scripts'))
sys.path.insert(0, str(ROOT / 'lab/task017'))

import run_task019_scale_pipeline as base_pipeline
import verify_task019_oracle as task019_oracle
import verify_bridge as bridge_oracle

DOTNET_EXE = ROOT / '.tools/dotnet/dotnet.exe'
TESTING_HOST_DLL = ROOT / 'engine/BitigMail.TestingHost/bin/Release/net8.0-windows/BitigMail.TestingHost.dll'
PID_FILE = ROOT / 'runtime/testing-engine/testinghost.pid'
CONFIG_PATH = ROOT / 'lab/task014/dovecot/local-credentials.json'
BASELINE_PATH = ROOT / '.codex-coordination/evidence/TASK-019/root-source-before.json'
CONFIG = json.loads(CONFIG_PATH.read_text(encoding='utf-8-sig'))

SCOPE = {
    'companyId': 'company-task020-scale',
    'projectId': 'project-task020-mbox-bounded',
    'companyName': 'TASK-020 Bounded MBOX Validation',
    'projectName': '128MiB MBOX Retention and Recovery Audit'
}

BULK_FOLDER = 'Task019/scale-cont-ff44e15b/Bulk'

MODE = sys.argv[1] if len(sys.argv) > 1 else 'baseline'
if MODE not in ('baseline', 'after'):
    raise SystemExit('usage: benchmark_preview_export.py [baseline|after]')
RUN_ID = MODE + '-' + uuid.uuid4().hex[:8]
EVIDENCE_DIR = ROOT / '.codex-coordination/evidence/TASK-022' / RUN_ID
EVIDENCE_DIR.mkdir(parents=True, exist_ok=True)

# Point base_pipeline to our run context
base_pipeline.RUN_ID = RUN_ID
base_pipeline.EVIDENCE_DIR = EVIDENCE_DIR
base_pipeline.ROOT = ROOT
base_pipeline.DOTNET_EXE = DOTNET_EXE
base_pipeline.TESTING_HOST_DLL = TESTING_HOST_DLL
base_pipeline.PID_FILE = PID_FILE

checks = []

def save_json(name, data):
    path = EVIDENCE_DIR / name
    with path.open('x', encoding='utf-8') as stream:
        json.dump(data, stream, ensure_ascii=False, indent=2)

base_pipeline.save_json = save_json

def check(name, condition, details=None):
    passed = bool(condition)
    rec = {'name': name, 'pass': passed}
    if details:
        rec['details'] = details
    checks.append(rec)
    print(f"[{'PASS' if passed else 'FAIL'}] {name}")
    if not passed:
        save_json('failed-check-' + re.sub(r'[^a-zA-Z0-9_-]', '_', name) + '.json', rec)
        raise AssertionError(f"Check failed: {name}")

base_pipeline.checks = checks
base_pipeline.check = check

# Use root-owned lifecycle helpers from base_pipeline unchanged
get_listening_pid = base_pipeline.get_listening_pid
get_process_info = base_pipeline.get_process_info
process_identity = base_pipeline.process_identity
kill_and_verify_stopped = base_pipeline.kill_and_verify_stopped
start_testing_host = base_pipeline.start_testing_host
wait_for_job = base_pipeline.wait_for_job

def api_call(method, path, body=None, expected=(200,), max_retries=4, timeout=90):
    headers = {'Origin': 'http://127.0.0.1:5173', 'Content-Type': 'application/json'}
    if base_pipeline.token:
        headers['X-BitigMail-Session'] = base_pipeline.token
    raw = None if body is None else json.dumps(body).encode('utf-8')
    if method == 'POST' and raw is None:
        raw = b'{}'

    payload = None
    status = None
    for attempt in range(max_retries):
        req = urllib.request.Request('http://127.0.0.1:6175' + path, data=raw, headers=headers, method=method)
        try:
            with urllib.request.urlopen(req, timeout=timeout) as response:
                status, payload = response.status, response.read()
                break
        except urllib.error.HTTPError as error:
            status, payload = error.code, error.read()
            break
        except (urllib.error.URLError, ConnectionResetError, TimeoutError, OSError):
            if attempt < max_retries - 1:
                time.sleep(0.5)
                continue
            raise

    text = payload.decode('utf-8', errors='replace') if payload else ''
    res = json.loads(text) if text else None
    if status not in expected:
        save_json(f"api-error-{status}.json", {'path': path, 'status': status, 'body': res})
    check(f"{method} {path} returned {status} (expected {expected})", status in expected)
    return res

base_pipeline.api_call = api_call


class PhaseTelemetryCollector:
    """100ms CPU/WS/Private telemetry sampler with tagged phase boundaries."""
    def __init__(self, pid, csv_path, interval_sec=0.1):
        self.pid = pid
        self.csv_path = csv_path
        self.interval_sec = interval_sec
        self._stop = threading.Event()
        self._thread = None
        self.current_phase = "init"
        self.phase_stats = collections.defaultdict(lambda: {
            'samples': 0,
            'peak_ws': 0,
            'min_ws': float('inf'),
            'peak_private': 0,
            'min_private': float('inf'),
            'peak_cpu': 0.0,
            'start_time': None,
            'end_time': None
        })
        self.peak_ws = 0
        self.peak_private = 0

    def set_phase(self, phase_name):
        now_str = datetime.datetime.now(datetime.timezone.utc).isoformat()
        if self.current_phase in self.phase_stats and self.phase_stats[self.current_phase]['start_time'] is not None:
            self.phase_stats[self.current_phase]['end_time'] = now_str
        self.current_phase = phase_name
        self.phase_stats[phase_name]['start_time'] = now_str
        print(f"  [Telemetry] Phase changed -> '{phase_name}' at {now_str}")

    def start(self):
        self._thread = threading.Thread(target=self._run, daemon=True)
        self._thread.start()

    def stop(self):
        now_str = datetime.datetime.now(datetime.timezone.utc).isoformat()
        if self.current_phase in self.phase_stats:
            self.phase_stats[self.current_phase]['end_time'] = now_str
        self._stop.set()
        if self._thread:
            self._thread.join(timeout=3)

    def _run(self):
        try:
            p = psutil.Process(self.pid)
        except Exception:
            return
        p.cpu_percent(interval=None)  # prime
        with open(self.csv_path, 'x', encoding='utf-8') as f:
            f.write("timestamp_utc,phase,cpu_pct,working_set_bytes,private_bytes\n")
            while not self._stop.is_set():
                try:
                    if not p.is_running():
                        break
                    mem = p.memory_info()
                    cpu = p.cpu_percent(interval=None)
                    ws = mem.rss
                    priv = getattr(mem, 'private', getattr(mem, 'vms', 0))
                    phase = self.current_phase
                    stats = self.phase_stats[phase]
                    stats['samples'] += 1
                    if ws > stats['peak_ws']: stats['peak_ws'] = ws
                    if ws < stats['min_ws']: stats['min_ws'] = ws
                    if priv > stats['peak_private']: stats['peak_private'] = priv
                    if priv < stats['min_private']: stats['min_private'] = priv
                    if cpu > stats['peak_cpu']: stats['peak_cpu'] = cpu
                    if ws > self.peak_ws: self.peak_ws = ws
                    if priv > self.peak_private: self.peak_private = priv
                    now_str = datetime.datetime.now(datetime.timezone.utc).isoformat()
                    f.write(f"{now_str},{phase},{cpu:.1f},{ws},{priv}\n")
                    f.flush()
                except Exception:
                    break
                self._stop.wait(self.interval_sec)


def telemetry_result(collector):
    phases = {}
    for name, raw in collector.phase_stats.items():
        samples = raw['samples']
        phases[name] = {
            'samples': samples,
            'peakWsBytes': raw['peak_ws'] if samples else None,
            'minWsBytes': raw['min_ws'] if samples else None,
            'peakPrivateBytes': raw['peak_private'] if samples else None,
            'minPrivateBytes': raw['min_private'] if samples else None,
            'peakCpuPct': raw['peak_cpu'] if samples else None,
            'startTime': raw['start_time'],
            'endTime': raw['end_time'],
        }
    return {
        'overallPeakWsBytes': collector.peak_ws or None,
        'overallPeakPrivateBytes': collector.peak_private or None,
        'phases': phases,
    }


def benchmark_main():
    """One preserved-target preview/export benchmark; never imports or injects faults."""
    host_pid = None
    preview_telemetry = None
    export_telemetry = None
    protected_pids = {p: get_listening_pid(p) for p in (6174, 5173)}
    protected = {p: process_identity(pid) if pid else None for p, pid in protected_pids.items()}
    summary = {'taskId': 'TASK-022', 'mode': MODE, 'runId': RUN_ID, 'protectedBefore': protected}
    try:
        check('6175 free before launch', get_listening_pid(6175) is None)
        check('6174 listener present', protected_pids[6174] is not None)
        check('5173 listener present', protected_pids[5173] is not None)
        disk_gib = psutil.disk_usage('C:').free / 1024**3
        ram_gib = psutil.virtual_memory().available / 1024**3
        check('disk reserve >= 11.2 GiB', disk_gib >= 11.2, {'freeGiB': disk_gib})
        check('RAM available >= 2 GiB', ram_gib >= 2, {'availableGiB': ram_gib})
        source_before = task019_oracle.audit_dovecot_source_invariance(CONFIG_PATH, BASELINE_PATH)
        check('source baseline preflight 12/4', source_before['pass'], source_before)
        save_json('source-before.json', source_before)
        bulk = task019_oracle.audit_target_folders(CONFIG_PATH, BULK_FOLDER)
        check('preserved bulk is exactly 1024', bulk.get('totalMessages') == 1024, bulk)
        save_json('bulk-before.json', bulk)

        base_pipeline.RUN_ID = RUN_ID
        base_pipeline.EVIDENCE_DIR = EVIDENCE_DIR
        base_pipeline.ROOT = ROOT
        base_pipeline.DOTNET_EXE = DOTNET_EXE
        base_pipeline.TESTING_HOST_DLL = TESTING_HOST_DLL
        base_pipeline.PID_FILE = PID_FILE
        base_pipeline.save_json = save_json
        base_pipeline.checks = checks
        base_pipeline.check = check
        host_proc, host_pid = start_testing_host('testinghost.log')
        check('fresh TestingHost exact child started', host_pid == host_proc.pid)

        account_id = 'acc_8f7863114fd1430ebc68cf1f2e90c1db'
        account = api_call(
            'GET',
            f"/api/accounts/{account_id}?companyId={SCOPE['companyId']}&projectId={SCOPE['projectId']}")
        check('preserved account available', account is not None)
        out_dir = api_call('POST', '/api/picker/output-dir', {}, max_retries=1)
        request = {**SCOPE, 'sourceAccountId': account['accountId'], 'targetDirHandle': out_dir['handle'],
                   'selectedFolders': [BULK_FOLDER], 'targetFormat': 'mboxrd'}
        save_json('preview-request.json', request)

        preview_telemetry = PhaseTelemetryCollector(host_pid, EVIDENCE_DIR / 'telemetry-preview.csv', 0.1)
        preview_telemetry.start(); preview_telemetry.set_phase('idle_pre_preview'); time.sleep(2)
        preview_telemetry.set_phase('preview_active'); t0 = time.perf_counter()
        preview = api_call('POST', '/api/transfer/bridge/export/preview', request, max_retries=1, timeout=600)
        preview_elapsed = time.perf_counter() - t0
        save_json('preview-response.json', preview)
        save_json('preview-id.json', {'previewId': preview.get('previewId')})
        preview_telemetry.set_phase('idle_post_preview'); time.sleep(2); preview_telemetry.stop()
        preview_telemetry_summary = telemetry_result(preview_telemetry)
        save_json('preview-telemetry-summary.json', preview_telemetry_summary)
        check('preview active telemetry has samples', preview_telemetry_summary['phases'].get('preview_active', {}).get('samples', 0) > 0)
        preview_telemetry = None
        check('preview eligible 1024', preview.get('eligibleItemsCount') == 1024)
        save_json('preview-metrics.json', {'elapsedSec': preview_elapsed})

        start_request = {**SCOPE, 'previewId': preview['previewId'], 'idempotencyKey': uuid.uuid4().hex}
        save_json('export-start-request.json', start_request)
        export_telemetry = PhaseTelemetryCollector(host_pid, EVIDENCE_DIR / 'telemetry-export.csv', 0.1)
        export_telemetry.start(); export_telemetry.set_phase('idle_pre_export'); time.sleep(2)
        export_telemetry.set_phase('export_active'); t1 = time.perf_counter()
        started = api_call('POST', '/api/transfer/bridge/export/start', start_request, max_retries=1)
        save_json('export-start-response.json', started)
        job = wait_for_job(started['jobId'], timeout_sec=600)
        export_elapsed = time.perf_counter() - t1
        export_telemetry.set_phase('idle_post_export'); time.sleep(2); export_telemetry.stop()
        telemetry_summary = telemetry_result(export_telemetry)
        check('export active telemetry has samples', telemetry_summary['phases'].get('export_active', {}).get('samples', 0) > 0)
        export_telemetry = None
        save_json('export-telemetry-summary.json', telemetry_summary)
        check('export completed', job.get('status') == 'completed', job)
        report = api_call('GET', f"/api/jobs/{started['jobId']}/report")
        save_json('export-report.json', report)
        output = pathlib.Path(report['bridgeTransfer']['outputPath']).resolve(strict=True)
        source_snap = bridge_oracle.files_snapshot(ROOT / 'runtime/task019/stage-128m/source/eml-tree', 'eml')
        imap_snap = bridge_oracle.imap_snapshot(CONFIG_PATH, 'target', [BULK_FOLDER])
        mbox_snap = bridge_oracle.files_snapshot(output, 'mbox')
        save_json('source-snapshot.json', source_snap); save_json('imap-snapshot.json', imap_snap); save_json('mbox-snapshot.json', mbox_snap)
        source_imap = bridge_oracle.compare(source_snap, imap_snap, exact=False, metadata=False)
        imap_mbox = bridge_oracle.compare(imap_snap, mbox_snap, exact=True, metadata=False)
        sidecar = bridge_oracle.verify_manifest(imap_snap, json.loads((output/'manifest.json').read_text(encoding='utf-8-sig')), output)
        save_json('source-to-imap.json', source_imap); save_json('imap-to-mbox.json', imap_mbox); save_json('sidecar.json', sidecar)
        check('source to IMAP canonical match', source_imap['pass'])
        check('IMAP to MBOX exact raw match', imap_mbox['pass'] and imap_mbox.get('missing') == 0 and imap_mbox.get('extra') == 0)
        check('sidecar match', sidecar['pass'])
        source_after = task019_oracle.audit_dovecot_source_invariance(CONFIG_PATH, BASELINE_PATH)
        check('source baseline postflight 12/4', source_after['pass'], source_after)
        save_json('source-after.json', source_after)
        summary.update({'status': 'PASS', 'previewElapsedSec': preview_elapsed, 'exportElapsedSec': export_elapsed,
                        'jobId': started['jobId'], 'outputPath': str(output),
                        'previewTelemetry': preview_telemetry_summary, 'exportTelemetry': telemetry_summary})
        save_json('summary.json', summary)
    except Exception as exc:
        save_json('failure.json', {'status': 'FAIL', 'type': type(exc).__name__, 'error': str(exc), 'checks': checks})
        raise
    finally:
        if preview_telemetry: preview_telemetry.stop()
        if export_telemetry: export_telemetry.stop()
        if host_pid and get_listening_pid(6175) == host_pid:
            kill_and_verify_stopped(host_pid, 6175)
        check('6175 off after run', get_listening_pid(6175) is None)
        current_6174 = get_listening_pid(6174)
        current_5173 = get_listening_pid(5173)
        check('6174 identity preserved', current_6174 == protected_pids[6174] and process_identity(current_6174) == protected[6174])
        check('5173 identity preserved', current_5173 == protected_pids[5173] and process_identity(current_5173) == protected[5173])


if __name__ == '__main__':
    benchmark_main()

