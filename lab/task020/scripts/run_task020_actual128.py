#!/usr/bin/env python3
"""TASK-020 Actual 128MiB MBOX Export & Interruption/Resume Harness.
Executes:
1. Preflight system integrity (ports 6174/5173 protected, 6175 free, disk/RAM checks)
2. Source baseline invariance preflight (12 messages / 4 attachments intact)
3. Fresh exact TestingHost launch & ownership verification
4. 100ms CPU/WS/Private telemetry collection with precise phase boundaries
5. Job 1: New single-folder MBOX export (1,024 messages, ~128 MiB) from preserved bulk target
6. Full physical raw/MIME/sidecar comparison and source baseline invariance
7. Job 2: Separate new single-folder MBOX export on changed path
8. Termination of exact owned TestingHost mid-export at observed durable partial position
9. Restart of exact TestingHost, verification of interrupted & persisted state
10. One explicit same-job resume to completion (1024/1024 verified, 0 missing/extra/duplicates)
11. Verification of unchanged prior published/staged bytes and final output integrity
12. Postflight Dovecot source baseline invariance audit
13. Stop and release port 6175
"""
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

RUN_ID = 'act128-' + uuid.uuid4().hex[:8]
EVIDENCE_DIR = ROOT / '.codex-coordination/evidence/TASK-020' / RUN_ID
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
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')

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

def api_call(method, path, body=None, expected=(200,), max_retries=4):
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
            with urllib.request.urlopen(req, timeout=90) as response:
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
        with open(self.csv_path, 'w', encoding='utf-8') as f:
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


def main():
    print(f"=======================================================================")
    print(f"TASK-020 ACTUAL 128MiB HARNESS RUNNER (Run ID: {RUN_ID})")
    print(f"=======================================================================")

    results_summary = {}

    try:
        # -----------------------------------------------------------------
        # 1. Preflight System Integrity
        # -----------------------------------------------------------------
        print("\n--- Step 1: Preflight System & Port Integrity ---")
        cur_6175 = get_listening_pid(6175)
        check("Port 6175 is free before test launch", cur_6175 is None)

        cur_6174 = get_listening_pid(6174)
        check("Normal service port 6174 is running (PID 49876 protected)", cur_6174 == 49876)

        cur_5173 = get_listening_pid(5173)
        check("Vite preview service port 5173 is running", cur_5173 is not None)

        free_c_gb = psutil.disk_usage('C:').free / (1024 ** 3)
        check("Free disk on C: >= 11.2 GiB", free_c_gb >= 11.2, {'free_c_gb': free_c_gb})

        free_ram_gb = psutil.virtual_memory().available / (1024 ** 3)
        check("Available physical RAM >= 2.0 GiB", free_ram_gb >= 2.0, {'free_ram_gb': free_ram_gb})

        # -----------------------------------------------------------------
        # 2. Preflight Dovecot Source Baseline Invariance
        # -----------------------------------------------------------------
        print("\n--- Step 2: Dovecot Source Baseline Invariance Preflight ---")
        base_pre = task019_oracle.audit_dovecot_source_invariance(CONFIG_PATH, BASELINE_PATH)
        check("Source baseline invariance preflight passed (12 items / 4 attachments)", base_pre['pass'], base_pre)
        save_json('source-baseline-preflight.json', base_pre)

        # -----------------------------------------------------------------
        # 3. Verify Preserved Bulk Target from job-19ed705a3aad
        # -----------------------------------------------------------------
        print(f"\n--- Step 3: Verify Preserved Bulk Target ({BULK_FOLDER}) ---")
        bulk_audit = task019_oracle.audit_target_folders(CONFIG_PATH, BULK_FOLDER)
        check(f"Preserved bulk folder {BULK_FOLDER} exists with exactly 1024 messages",
              bulk_audit.get('totalMessages') == 1024, bulk_audit)
        save_json('preserved-bulk-target-check.json', bulk_audit)

        # -----------------------------------------------------------------
        # 4. Fresh Exact TestingHost Launch
        # -----------------------------------------------------------------
        print("\n--- Step 4: Fresh Exact TestingHost Launch ---")
        host_proc1, host_pid1 = start_testing_host("task020-host1.log")
        check(f"TestingHost 1 running with verified PID {host_pid1}", host_pid1 is not None and host_pid1 > 0)
        results_summary['host1_pid'] = host_pid1

        # -----------------------------------------------------------------
        # 5. Register Target Account & Picker Setup
        # -----------------------------------------------------------------
        print("\n--- Step 5: Register Target Account in TestingHost ---")
        tgt_cfg = CONFIG['target']
        acc = api_call('POST', '/api/accounts', {
            **SCOPE,
            'displayName': f"TASK020 Target {RUN_ID}",
            'email': tgt_cfg['email'],
            'host': tgt_cfg['imapHost'],
            'port': tgt_cfg['imapPort'],
            'tlsMode': 'none',
            'username': tgt_cfg['username'],
            'password': tgt_cfg['password']
        })
        target_acc_id = acc['accountId']
        check("Target account registered in TestingHost", bool(target_acc_id))
        save_json('target-account.json', acc)

        # -----------------------------------------------------------------
        # 6. JOB 1: New Single-Folder MBOX Export (1024 items) with Telemetry
        # -----------------------------------------------------------------
        print("\n--- Step 6: Job 1 Single-Folder MBOX Export (1024 items) ---")
        out_dir1 = api_call('POST', '/api/picker/output-dir', {})
        check("Output dir handle obtained for Job 1", bool(out_dir1.get('handle')))

        exp_prev1 = api_call('POST', '/api/transfer/bridge/export/preview', {
            **SCOPE,
            'sourceAccountId': target_acc_id,
            'targetDirHandle': out_dir1['handle'],
            'selectedFolders': [BULK_FOLDER],
            'targetFormat': 'mboxrd'
        })
        check("Job 1 preview eligibleItemsCount == 1024", exp_prev1.get('eligibleItemsCount') == 1024)
        check("Job 1 preview canTransfer == True", exp_prev1.get('canTransfer') is True)
        save_json('job1-export-preview.json', exp_prev1)

        # Start 100ms phase telemetry sampler
        telemetry_csv1 = EVIDENCE_DIR / "telemetry-task020-job1.csv"
        telemetry1 = PhaseTelemetryCollector(host_pid1, telemetry_csv1, interval_sec=0.1)
        telemetry1.start()

        # Phase 1: Idle Pre-Export
        telemetry1.set_phase("idle_pre_export")
        time.sleep(2.0)

        # Phase 2: Active Export
        telemetry1.set_phase("export_active")
        t0_job1 = time.perf_counter()
        exp_start1 = api_call('POST', '/api/transfer/bridge/export/start', {
            **SCOPE,
            'previewId': exp_prev1['previewId'],
            'idempotencyKey': uuid.uuid4().hex
        })
        job1_id = exp_start1['jobId']
        print(f"Job 1 started with ID: {job1_id}")

        job1_record = wait_for_job(job1_id, timeout_sec=240)
        t_job1_elapsed = time.perf_counter() - t0_job1

        # Phase 3: Idle Post-Export
        telemetry1.set_phase("idle_post_export")
        time.sleep(2.0)
        telemetry1.stop()

        check("Job 1 completed successfully", job1_record.get('status') == 'completed')
        rep1 = api_call('GET', f"/api/jobs/{job1_id}/report")
        save_json('job1-export-report.json', rep1)
        bt1 = rep1.get('bridgeTransfer', {})
        check("Job 1 totalVerified == 1024", bt1.get('totalVerified') == 1024)
        check("Job 1 totalFailed == 0", bt1.get('totalFailed') == 0)

        job1_output_dir = pathlib.Path(bt1['outputPath']).resolve(strict=True)
        print(f"Job 1 completed 1024 items in {t_job1_elapsed:.2f}s (Rate: {1024/t_job1_elapsed:.1f} msg/s)")
        print(f"  Output directory: {job1_output_dir}")

        # Telemetry stats
        p_idle_pre = telemetry1.phase_stats['idle_pre_export']
        p_active = telemetry1.phase_stats['export_active']
        p_idle_post = telemetry1.phase_stats['idle_post_export']

        print(f"  Telemetry Phase Stats (Job 1):")
        print(f"    idle_pre_export:  samples={p_idle_pre['samples']}, Peak WS={p_idle_pre['peak_ws']/(1024*1024):.1f} MiB, Peak Private={p_idle_pre['peak_private']/(1024*1024):.1f} MiB")
        print(f"    export_active:    samples={p_active['samples']}, Peak WS={p_active['peak_ws']/(1024*1024):.1f} MiB, Peak Private={p_active['peak_private']/(1024*1024):.1f} MiB, Max CPU={p_active['peak_cpu']:.1f}%")
        print(f"    idle_post_export: samples={p_idle_post['samples']}, Peak WS={p_idle_post['peak_ws']/(1024*1024):.1f} MiB, Peak Private={p_idle_post['peak_private']/(1024*1024):.1f} MiB")

        telemetry1_summary = {
            'jobId': job1_id,
            'elapsed_sec': t_job1_elapsed,
            'items': 1024,
            'items_per_sec': 1024 / t_job1_elapsed,
            'overall_peak_ws_bytes': telemetry1.peak_ws,
            'overall_peak_ws_mib': telemetry1.peak_ws / (1024 * 1024),
            'overall_peak_private_bytes': telemetry1.peak_private,
            'overall_peak_private_mib': telemetry1.peak_private / (1024 * 1024),
            'phases': dict(telemetry1.phase_stats)
        }
        save_json('job1-telemetry-summary.json', telemetry1_summary)
        results_summary['job1'] = telemetry1_summary

        # -----------------------------------------------------------------
        # 7. Physical Raw/MIME/Sidecar Comparison for Job 1
        # -----------------------------------------------------------------
        print("\n--- Step 7: Independent Physical Raw/MIME/Sidecar Audit (Job 1) ---")
        source_tree_dir = ROOT / 'runtime/task019/stage-128m/source/eml-tree'
        source_snap = bridge_oracle.files_snapshot(source_tree_dir, 'eml')
        save_json('job1-source-snapshot.json', source_snap)

        imap_snap = bridge_oracle.imap_snapshot(CONFIG_PATH, 'target', [BULK_FOLDER])
        save_json('job1-imap-snapshot.json', imap_snap)

        mbox_snap1 = bridge_oracle.files_snapshot(job1_output_dir, 'mbox')
        save_json('job1-mbox-snapshot.json', mbox_snap1)

        source_to_imap1 = bridge_oracle.compare(source_snap, imap_snap, exact=False, metadata=False)
        save_json('job1-source-to-imap.json', source_to_imap1)
        check("Job 1 source to IMAP canonical/MIME multiset matches (1024 items, 308 attachments)",
              source_to_imap1['pass'] and source_to_imap1['missing'] == 0 and source_to_imap1['extra'] == 0)

        imap_to_file1 = bridge_oracle.compare(imap_snap, mbox_snap1, exact=True, metadata=False)
        save_json('job1-imap-to-file.json', imap_to_file1)
        check("Job 1 IMAP to MBOX exact raw/header/MIME multiset matches (1024 items, 0 missing, 0 extra)",
              imap_to_file1['pass'] and imap_to_file1['missing'] == 0 and imap_to_file1['extra'] == 0)

        manifest1 = json.loads((job1_output_dir / 'manifest.json').read_text(encoding='utf-8-sig'))
        sidecar1 = bridge_oracle.verify_manifest(imap_snap, manifest1, job1_output_dir)
        save_json('job1-sidecar-audit.json', sidecar1)
        check("Job 1 sidecar manifest metadata, UID validity, and folder mapping PASS", sidecar1['pass'])

        # -----------------------------------------------------------------
        # 8. Source Baseline Invariance Check after Job 1
        # -----------------------------------------------------------------
        print("\n--- Step 8: Dovecot Source Baseline Invariance Check (Post Job 1) ---")
        base_mid = task019_oracle.audit_dovecot_source_invariance(CONFIG_PATH, BASELINE_PATH)
        check("Source baseline invariance maintained after Job 1 (12 items / 4 attachments)", base_mid['pass'], base_mid)

        # -----------------------------------------------------------------
        # 9. JOB 2: Separate New MBOX Export Job on Changed Path (Interruption Test)
        # -----------------------------------------------------------------
        print("\n--- Step 9: Job 2 Separate MBOX Export on Changed Path ---")
        out_dir2 = api_call('POST', '/api/picker/output-dir', {})
        check("Output dir handle obtained for Job 2 (changed path)", bool(out_dir2.get('handle')))

        exp_prev2 = api_call('POST', '/api/transfer/bridge/export/preview', {
            **SCOPE,
            'sourceAccountId': target_acc_id,
            'targetDirHandle': out_dir2['handle'],
            'selectedFolders': [BULK_FOLDER],
            'targetFormat': 'mboxrd'
        })
        check("Job 2 preview eligibleItemsCount == 1024", exp_prev2.get('eligibleItemsCount') == 1024)
        save_json('job2-export-preview.json', exp_prev2)

        # Start Job 2
        exp_start2 = api_call('POST', '/api/transfer/bridge/export/start', {
            **SCOPE,
            'previewId': exp_prev2['previewId'],
            'idempotencyKey': uuid.uuid4().hex
        })
        job2_id = exp_start2['jobId']
        print(f"Job 2 started with ID: {job2_id}")

        # Locate Job 2 output directory and staging path
        job2_journal_path = ROOT / f"runtime/testing-engine/bridge-transfers/export-journals/{job2_id}.json"
        t0_j2 = time.monotonic()
        while time.monotonic() - t0_j2 < 5:
            if job2_journal_path.exists():
                break
            time.sleep(0.05)
        check("Job 2 export journal initialized on disk", job2_journal_path.exists())
        journal2_init = json.loads(job2_journal_path.read_text(encoding='utf-8'))
        job2_output_dir = pathlib.Path(journal2_init['JobOutputDir'])
        staging_dir2 = job2_output_dir / '.staging' / 'fld_001'
        print(f"  Job 2 output dir: {job2_output_dir}")
        print(f"  Job 2 staging dir: {staging_dir2}")

        # -----------------------------------------------------------------
        # 10. Advance to Observed Durable Partial Position and Kill Host
        # -----------------------------------------------------------------
        print("\n--- Step 10: Observing Durable Partial Position & Process Interruption ---")
        t0_observe = time.monotonic()
        observed_pos = 0
        pre_kill_staged_files = {}

        # Target durable position: between 50 and 200 items (out of 1024)
        while time.monotonic() - t0_observe < 40:
            if staging_dir2.exists():
                try:
                    staged = [f for f in staging_dir2.glob('*.raw') if f.is_file() and f.stat().st_size > 0]
                    if len(staged) >= 60:
                        observed_pos = len(staged)
                        for f in staged:
                            data = f.read_bytes()
                            pre_kill_staged_files[f.name] = {
                                'size': len(data),
                                'sha256': hashlib.sha256(data).hexdigest()
                            }
                        break
                except Exception:
                    pass
            time.sleep(0.02)

        check(f"Observed durable partial position ({observed_pos} staged items) is genuinely partial (50 <= pos < 1024)",
              50 <= observed_pos < 1024)
        print(f"Observed durable position reached: {observed_pos} staged items on disk before termination")

        save_json('job2-pre-kill-staging.json', {
            'jobId': job2_id,
            'observedDurablePosition': observed_pos,
            'stagedCount': len(pre_kill_staged_files),
            'stagedFiles': pre_kill_staged_files
        })

        # Record exact process identity before kill
        crashed_pid = base_pipeline.active_host_proc.pid
        crashed_identity = process_identity(crashed_pid)
        save_json(f'owned-process-termination-{crashed_pid}.json', crashed_identity)
        check("Target process PID matches active TestingHost PID", crashed_pid == host_pid1)

        # Terminate exact owned TestingHost
        print(f"Terminating exact owned TestingHost PID {crashed_pid}...")
        kill_and_verify_stopped(crashed_pid, 6175)
        print("TestingHost successfully terminated.")

        # -----------------------------------------------------------------
        # 11. Verify Interrupted/Persisted State on Disk
        # -----------------------------------------------------------------
        print("\n--- Step 11: Verify Persisted State on Disk Before Host Restart ---")
        check("Export journal remains intact on disk across termination", job2_journal_path.exists())
        check("Staging directory remains intact on disk", staging_dir2.exists())
        staged_post_kill = list(staging_dir2.glob('*.raw'))
        check(f"Staged files count on disk ({len(staged_post_kill)}) >= observed position ({observed_pos})",
              len(staged_post_kill) >= observed_pos)

        # Verify final MBOX and manifest do NOT exist yet
        final_mbox2_path = job2_output_dir / 'fld_001.mbox'
        final_manifest2_path = job2_output_dir / 'manifest.json'
        check("Final MBOX file does not exist before resume", not final_mbox2_path.exists())
        check("Final manifest.json does not exist before resume", not final_manifest2_path.exists())

        # Verify staged byte hashes match pre-kill observation
        for name, info in pre_kill_staged_files.items():
            fpath = staging_dir2 / name
            check(f"Pre-kill staged file {name} exists on disk", fpath.exists())
            actual_bytes = fpath.read_bytes()
            check(f"Pre-kill staged file {name} hash unchanged",
                  hashlib.sha256(actual_bytes).hexdigest() == info['sha256'])

        # -----------------------------------------------------------------
        # 12. Restart Exact Host & Verify Interrupted State via API
        # -----------------------------------------------------------------
        print("\n--- Step 12: Restart Exact TestingHost & Verify Interrupted Status ---")
        host_proc2, host_pid2 = start_testing_host("task020-host2-resume.log")
        check("New TestingHost PID differs from crashed PID", host_pid2 != crashed_pid)
        print(f"TestingHost restarted at PID {host_pid2}")
        results_summary['host2_resumed_pid'] = host_pid2

        rec_job2 = api_call('GET', f"/api/jobs/{job2_id}")
        save_json('job2-recovered-interrupted.json', rec_job2)
        check("Job 2 recovered status is 'interrupted'", rec_job2.get('status') == 'interrupted')
        print(f"Job 2 recovered status confirmed: {rec_job2.get('status')}, Stage: '{rec_job2.get('stage')}'")

        # -----------------------------------------------------------------
        # 13. One Explicit Same-Job Resume
        # -----------------------------------------------------------------
        print(f"\n--- Step 13: One Explicit Same-Job Resume for {job2_id} ---")
        t0_resume = time.perf_counter()
        resume_res = api_call('POST', f"/api/transfer/bridge/export/resume/{job2_id}?companyId={SCOPE['companyId']}&projectId={SCOPE['projectId']}", {
            'companyId': SCOPE['companyId'],
            'projectId': SCOPE['projectId'],
            'jobId': job2_id
        })
        check("Resume request accepted", resume_res.get('status') in ('running', 'resuming', 'converting', 'queued', 'completed'))
        save_json('job2-resume-response.json', resume_res)

        final_job2 = wait_for_job(job2_id, timeout_sec=240)
        t_resume_elapsed = time.perf_counter() - t0_resume
        check("Job 2 completed after resume", final_job2.get('status') == 'completed')

        rep2 = api_call('GET', f"/api/jobs/{job2_id}/report")
        save_json('job2-export-report-final.json', rep2)
        bt2 = rep2.get('bridgeTransfer', {})
        check("Job 2 final totalVerified == 1024", bt2.get('totalVerified') == 1024)
        check("Job 2 final totalFailed == 0", bt2.get('totalFailed') == 0)
        print(f"Job 2 resumed and completed in {t_resume_elapsed:.2f}s (1024/1024 verified)")

        # -----------------------------------------------------------------
        # 14. Full 1024 Final Output Verification for Job 2
        # -----------------------------------------------------------------
        print("\n--- Step 14: Final Output Integrity & Verification (Job 2) ---")
        check("Final MBOX file exists", final_mbox2_path.exists())
        check("Final manifest.json exists", final_manifest2_path.exists())

        mbox_snap2 = bridge_oracle.files_snapshot(job2_output_dir, 'mbox')
        save_json('job2-mbox-snapshot-final.json', mbox_snap2)
        check("Job 2 final MBOX physical count == 1024", mbox_snap2['count'] == 1024)
        check("Job 2 final MBOX attachment count == 308", mbox_snap2['attachments'] == 308)

        imap_to_file2 = bridge_oracle.compare(imap_snap, mbox_snap2, exact=True, metadata=False)
        save_json('job2-imap-to-file.json', imap_to_file2)
        check("Job 2 IMAP to final MBOX multiset match (0 missing, 0 extra, 0 duplicates)",
              imap_to_file2['pass'] and imap_to_file2['missing'] == 0 and imap_to_file2['extra'] == 0)

        manifest2 = json.loads(final_manifest2_path.read_text(encoding='utf-8-sig'))
        sidecar2 = bridge_oracle.verify_manifest(imap_snap, manifest2, job2_output_dir)
        save_json('job2-sidecar-audit.json', sidecar2)
        check("Job 2 final sidecar manifest verification PASS", sidecar2['pass'])

        # Verify unchanged prior published bytes:
        # Check that every pre-kill staged item's hash matches the corresponding item in final manifest
        manifest_items_by_filename = {
            f"msg_{item['sourceUid']:08d}.raw": item['sourceSha256'].lower()
            for item in manifest2['items']
        }
        for name, info in pre_kill_staged_files.items():
            check(f"Prior staged item {name} found in final manifest", name in manifest_items_by_filename)
            check(f"Prior staged item {name} byte hash unchanged in final manifest",
                  manifest_items_by_filename[name] == info['sha256'])

        # -----------------------------------------------------------------
        # 15. Postflight Dovecot Source Baseline Invariance Check
        # -----------------------------------------------------------------
        print("\n--- Step 15: Postflight Dovecot Source Baseline Invariance Check ---")
        base_post = task019_oracle.audit_dovecot_source_invariance(CONFIG_PATH, BASELINE_PATH)
        check("Postflight source baseline invariance passed (12 items / 4 attachments untouched)",
              base_post['pass'], base_post)
        save_json('source-baseline-postflight.json', base_post)

        # -----------------------------------------------------------------
        # 16. Stop and Release Port 6175
        # -----------------------------------------------------------------
        print("\n--- Step 16: Stop and Release Port 6175 ---")
        kill_and_verify_stopped(host_pid2, 6175)
        check("Port 6175 is confirmed released", get_listening_pid(6175) is None)

        # Verify protected services
        check("Normal service port 6174 PID 49876 still running", get_listening_pid(6174) == 49876)
        check("Vite preview service port 5173 still running", get_listening_pid(5173) is not None)

        print("\n=======================================================================")
        print(f"TASK-020 ACTUAL 128MiB RUN COMPLETED SUCCESSFULLY! ({len(checks)}/{len(checks)} PASS)")
        print(f"=======================================================================")

        results_summary['all_checks_passed'] = True
        results_summary['total_checks'] = len(checks)
        results_summary['job1_id'] = job1_id
        results_summary['job2_id'] = job2_id
        results_summary['observed_durable_position'] = observed_pos
        results_summary['crashed_pid'] = crashed_pid
        results_summary['resumed_pid'] = host_pid2
        results_summary['evidence_dir'] = str(EVIDENCE_DIR)

        save_json('task020-execution-summary.json', results_summary)

    except Exception as ex:
        print(f"\n[FATAL EXCEPTION] {ex}")
        save_json('execution-fatal-error.json', {
            'error': str(ex),
            'type': type(ex).__name__
        })
        # Try to clean up port 6175 if owned
        try:
            cur_pid = get_listening_pid(6175)
            if cur_pid and base_pipeline.active_host_proc and base_pipeline.active_host_proc.pid == cur_pid:
                kill_and_verify_stopped(cur_pid, 6175)
        except Exception:
            pass
        raise


if __name__ == '__main__':
    main()
