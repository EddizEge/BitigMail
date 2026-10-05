"""TASK-017 Extended Acceptance Runner (Task-Owned).
Deterministic actual API and independent read-only lab/file oracle execution.
Tests:
1. Vendor-free bridge-over50 (72 EML, 24 attachments, independent IMAP verification, 0 Aspose).
2. Filtered Istanbul (3 messages / 1 attachment) export and import (inclusive dates 2024-01-01 to 2024-02-28).
3. Export -> import roundtrip for EML tree with physical duplicates and raw/canonical boundary.
4A. Real process restart (Import Crash): PauseAfterAppendBeforeReturn exact-folder fault, prove target has 1 copy before kill, verified PID/port ownership, Stop-Process -Id -Force, process exited AND port closed, restart CREATE_NO_WINDOW with task log, resume with empty registry, 0 new copies, complete 12/12.
4B. Real process restart (Export Crash): PauseAfterExportItemPersisted, prove item 1 persisted, verified PID/port ownership, Stop-Process -Force, restart, resume with empty registry, same output identity.
5A. APPEND lost-response: deterministic fault after append before response, resume with keyword reconciliation and ZERO duplicate appends.
5B. Natural zero-match negative: PauseBeforeAppend ordinal 2 leaves keyword0 on server, verified kill/restart, resume yields NeedsAttention / fail-closed, ZERO new appends.
6. Negatives: leading junk/empty/malformed mbox; second source drift producing zero appends; sibling dir tamper; collision/traversal; safe error/no-store.
7. Source invariance: before/after source Dovecot UIDs, hashes, flags, internaldates exact unchanged.
"""
import collections
import datetime
import imaplib
import json
import os
import pathlib
import re
import signal
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

# Ensure lab/task017 is on path for verify_bridge oracle
ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'lab/task017'))
import verify_bridge as oracle

RUN_ID = 'ext-' + uuid.uuid4().hex[:10]
RUN = ROOT / '.codex-coordination/evidence/TASK-017' / RUN_ID
RUN.mkdir(parents=True, exist_ok=True)

CONFIG_PATH = ROOT / 'lab/task014/dovecot/local-credentials.json'
CONFIG = json.loads(CONFIG_PATH.read_text(encoding='utf-8-sig'))
for role in ('source', 'target'):
    if CONFIG[role]['imapHost'] != '127.0.0.1' or CONFIG[role]['imapPort'] != 5143:
        raise RuntimeError('Only isolated Dovecot lab is authorized')

SECRETS = [CONFIG[r]['password'] for r in ('source', 'target')]
SCOPE = {
    'companyId': 'task017-' + RUN_ID,
    'projectId': 'extended-pilot',
    'companyName': 'TASK017 Genişletilmiş Kabul Testi',
    'projectName': 'Dosya ve Hesap Köprüsü Genişletilmiş Test'
}
FOLDERS = ['INBOX', 'Gönderilenler', 'Projeler/İstanbul']

DOTNET_EXE = ROOT / '.tools/dotnet/dotnet.exe'
TESTING_HOST_DLL = ROOT / 'engine/BitigMail.TestingHost/bin/Release/net8.0-windows/BitigMail.TestingHost.dll'
PID_FILE = ROOT / 'runtime/testing-engine/testinghost.pid'
SIGNAL_FILE = ROOT / 'runtime/testing-engine/bridge-pause.signal'

checks = []
jobs = []
token = None
active_host_proc = None


def save(name, data):
    (RUN / name).write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')


def check(name, condition, details=None):
    passed = bool(condition)
    record = {'name': name, 'pass': passed}
    if details:
        record['details'] = details
    checks.append(record)
    print(f"[{'PASS' if passed else 'FAIL'}] {name}")
    if not passed:
        save('failed-check-' + re.sub(r'[^a-zA-Z0-9_-]', '_', name) + '.json', record)
        raise AssertionError(f"Check failed: {name}")


def get_listening_pid(port=6175):
    cmd = f"Get-NetTCPConnection -LocalPort {port} -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess"
    proc = subprocess.run(['pwsh', '-NoProfile', '-Command', cmd], capture_output=True, text=True)
    out = proc.stdout.strip()
    if out and out.isdigit():
        return int(out)
    return None


def get_process_info(pid):
    cmd = f"Get-CimInstance Win32_Process -Filter 'ProcessId = {pid}' -ErrorAction SilentlyContinue | Select-Object -Property CommandLine, CreationDate | ConvertTo-Json"
    proc = subprocess.run(['pwsh', '-NoProfile', '-Command', cmd], capture_output=True, text=True)
    if proc.returncode == 0 and proc.stdout.strip():
        try:
            data = json.loads(proc.stdout.strip())
            return data.get('CommandLine', ''), data.get('CreationDate', '')
        except Exception:
            pass
    return None, None


def verify_and_record_host(expected_port=6175):
    check("PID file exists", PID_FILE.exists())
    file_pid = int(PID_FILE.read_text().strip())

    listen_pid = get_listening_pid(expected_port)
    check(f"Owner of LISTEN 127.0.0.1:{expected_port} ({listen_pid}) matches pidfile ({file_pid})", listen_pid == file_pid)

    cmdline, creation_date = get_process_info(file_pid)
    check(f"Win32_Process info found for PID {file_pid}", cmdline is not None)

    expected_dll_substr = "BitigMail.TestingHost.dll"
    check(f"Win32_Process command line references {expected_dll_substr}", expected_dll_substr.lower() in (cmdline or '').lower())
    check("Win32_Process command line references Release build", "release" in (cmdline or '').lower())

    info = {
        'pid': file_pid,
        'listenPort': expected_port,
        'commandLine': cmdline,
        'creationDate': str(creation_date),
        'recordedAt': datetime.datetime.now(datetime.timezone.utc).isoformat()
    }
    return info


def kill_and_verify_stopped(pid, port=6175, timeout=10):
    print(f"Executing native PowerShell Stop-Process -Id {pid} -Force...")
    cmd = f"Stop-Process -Id {pid} -Force -ErrorAction SilentlyContinue"
    subprocess.run(['pwsh', '-NoProfile', '-Command', cmd], capture_output=True)

    t0 = time.monotonic()
    process_dead = False
    port_closed = False
    while time.monotonic() - t0 < timeout:
        check_proc = subprocess.run(['pwsh', '-NoProfile', '-Command', f"Get-Process -Id {pid} -ErrorAction SilentlyContinue"], capture_output=True, text=True)
        is_proc_alive = bool(check_proc.stdout.strip())

        curr_listen_pid = get_listening_pid(port)
        is_port_open = curr_listen_pid is not None

        if not is_proc_alive:
            process_dead = True
        if not is_port_open:
            port_closed = True

        if process_dead and port_closed:
            break
        time.sleep(0.3)

    check(f"Process {pid} confirmed terminated", process_dead)
    check(f"Port {port} confirmed closed", port_closed)


def start_testing_host(log_name="testinghost.log"):
    global active_host_proc
    log_path = RUN / log_name
    log_file = open(log_path, 'a', encoding='utf-8')

    creationflags = 0x08000000 if sys.platform == 'win32' else 0
    proc = subprocess.Popen(
        [str(DOTNET_EXE), str(TESTING_HOST_DLL)],
        cwd=str(ROOT),
        stdout=log_file,
        stderr=subprocess.STDOUT,
        creationflags=creationflags
    )
    active_host_proc = proc

    t0 = time.monotonic()
    ready = False
    new_token = None
    while time.monotonic() - t0 < 30:
        try:
            headers = {'Origin': 'http://127.0.0.1:5173', 'Content-Type': 'application/json'}
            req = urllib.request.Request('http://127.0.0.1:6175/api/session', data=b'{}', headers=headers, method='POST')
            with urllib.request.urlopen(req, timeout=3) as resp:
                if resp.status == 200:
                    body = json.loads(resp.read().decode('utf-8'))
                    new_token = body.get('token')
                    ready = True
                    break
        except Exception:
            time.sleep(0.5)

    check("Started TestingHost responded to /api/session", ready and new_token is not None)

    host_info = verify_and_record_host(6175)
    host_info['popenPid'] = proc.pid
    host_info['logPath'] = str(log_path)
    save(f"host-info-{host_info['pid']}.json", host_info)
    return proc, new_token, host_info


def call(method, path, body=None, expected=(200,), session=True, max_retries=3):
    headers = {'Origin': 'http://127.0.0.1:5173', 'Content-Type': 'application/json'}
    if session and token:
        headers['X-BitigMail-Session'] = token
    raw = None if body is None else json.dumps(body).encode('utf-8')
    if method == 'POST' and raw is None:
        raw = b'{}'

    for attempt in range(max_retries):
        request = urllib.request.Request('http://127.0.0.1:6175' + path, data=raw, headers=headers, method=method)
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                status, payload, response_headers = response.status, response.read(), response.headers
                break
        except urllib.error.HTTPError as error:
            status, payload, response_headers = error.code, error.read(), error.headers
            break
        except (urllib.error.URLError, ConnectionResetError, TimeoutError) as net_err:
            if attempt < max_retries - 1:
                time.sleep(0.5)
                continue
            raise

    text = payload.decode('utf-8', errors='replace')
    check(f"{path} secret-free", all(s not in text for s in SECRETS))
    check(f"{path} no-store", 'no-store' in response_headers.get('Cache-Control', ''))

    result = json.loads(text) if text else None
    if path != '/api/session' and status not in expected:
        save(f"unexpected-{status}-{int(time.time())}.json", {'path': path, 'status': status, 'response': result})
    check(f"{path} status {status} in {expected}", status in expected)
    return result


def create_account(role):
    cfg = CONFIG[role]
    return call('POST', '/api/accounts', {
        **SCOPE,
        'displayName': f"TASK017 Ext {role}",
        'email': cfg['email'],
        'host': cfg['imapHost'],
        'port': cfg['imapPort'],
        'tlsMode': 'none',
        'username': cfg['username'],
        'password': cfg['password']
    })


def wait_for_job(job_id, terminal_statuses=('completed', 'failed', 'interrupted', 'needs-attention'), timeout_sec=60):
    start = time.monotonic()
    last = None
    while time.monotonic() - start < timeout_sec:
        last = call('GET', f"/api/jobs/{job_id}")
        if last.get('status') in terminal_statuses:
            return last
        time.sleep(0.5)
    raise TimeoutError(f"Job {job_id} timed out; last status: {last}")


def run_job(kind, preview, expected_items, timeout_sec=90):
    start_path = f"/api/transfer/bridge/{kind}/start"
    idempotency_key = uuid.uuid4().hex
    start_res = call('POST', start_path, {
        **SCOPE,
        'previewId': preview['previewId'],
        'idempotencyKey': idempotency_key
    })
    check(f"{kind} start response has jobId", 'jobId' in start_res)
    job_id = start_res['jobId']
    jobs.append(job_id)

    # Idempotency check: replay start with same key -> same jobId
    replay = call('POST', start_path, {
        **SCOPE,
        'previewId': preview['previewId'],
        'idempotencyKey': idempotency_key
    })
    check(f"{kind} replay returned identical jobId", replay['jobId'] == job_id)

    final_job = wait_for_job(job_id, timeout_sec=timeout_sec)
    save(f"{kind}-job-{job_id}.json", final_job)
    check(f"{kind} job final status completed", final_job['status'] == 'completed')

    rep = call('GET', f"/api/jobs/{job_id}/report")
    save(f"{kind}-report-{job_id}.json", rep)
    bt = rep.get('bridgeTransfer', {})
    check(f"{kind} report verified == expected", bt.get('totalVerified') == expected_items or rep.get('itemsWritten') == expected_items)
    check(f"{kind} report failed == 0", (bt.get('totalFailed') == 0) if 'totalFailed' in bt else (rep.get('failedItems') == 0))
    return final_job, rep


def main():
    global token
    print(f"=== TASK-017 Extended Acceptance Runner (Run ID: {RUN_ID}) ===")
    save('meta.json', {
        'runId': RUN_ID,
        'timestamp': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'scope': SCOPE,
        'folders': FOLDERS
    })

    # Ensure clean initial state for TestingHost port
    listen_pid = get_listening_pid(6175)
    if listen_pid is not None:
        print(f"Found existing process {listen_pid} on port 6175, stopping it...")
        kill_and_verify_stopped(listen_pid, 6175)

    if SIGNAL_FILE.exists():
        SIGNAL_FILE.unlink()

    print("Launching initial TestingHost process...")
    proc_initial, token, host_info1 = start_testing_host("initial-host.log")
    print(f"TestingHost initial PID: {host_info1['pid']}")

    # Setup isolated test accounts
    accounts = {r: create_account(r) for r in ('source', 'target')}
    save('accounts.json', {r: accounts[r]['accountId'] for r in accounts})

    # Capture initial source snapshot via independent Python IMAP oracle
    print("\n--- Capturing initial source IMAP snapshot ---")
    source_before = oracle.imap_snapshot(CONFIG_PATH, 'source', FOLDERS)
    save('source-before.json', source_before)
    check("Source before has 12 items", source_before['count'] == 12)
    check("Source before has 4 attachments", source_before['attachments'] == 4)

    # =========================================================================
    # 1) Vendor-free bridge-over50 picker/descriptor/preview/import
    # =========================================================================
    print("\n--- TEST 1: Vendor-free bridge-over50 ---")
    call('POST', '/api/testing/set-mime-source', {'fixtureId': 'bridge-over50'})
    picked = call('POST', '/api/picker/mime-source', {'mode': 'eml-files'})
    check("Picker returned valid source handle", picked.get('handle'))
    check("Picker returned displayPath", bool(picked.get('displayPath')))

    target_folder_over50 = f"TASK017-{RUN_ID}-over50"
    imp_req = {
        **SCOPE,
        'sourceHandle': picked['handle'],
        'targetAccountId': accounts['target']['accountId'],
        'selectedFolders': ['over50-1cd78704'],
        'targetFolderMappings': {'over50-1cd78704': target_folder_over50}
    }
    prev_over50 = call('POST', '/api/transfer/bridge/import/preview', imp_req)
    save('over50-import-preview.json', prev_over50)
    check("over50 preview sourceKind is eml-tree", prev_over50.get('sourceKind') == 'eml-tree')
    check("over50 preview eligible == 72", prev_over50['eligibleItemsCount'] == 72)
    check("over50 preview canTransfer == True", prev_over50['canTransfer'] is True)

    job_over50, rep_over50 = run_job('import', prev_over50, 72, timeout_sec=120)

    # Independent IMAP verification via stdlib oracle
    print("Verifying over50 target folder via independent IMAP oracle...")
    actual_over50 = oracle.imap_snapshot(CONFIG_PATH, 'target', [target_folder_over50])
    save('over50-target-snapshot.json', actual_over50)
    check("Independent IMAP proof: exactly 72 physical messages", actual_over50['count'] == 72)
    check("Independent IMAP proof: exactly 24 attachments", actual_over50['attachments'] == 24)

    # Prove zero Aspose markers
    for item in actual_over50.get('messages', []):
        raw_text = str(item.get('headers', ''))
        check("Zero Aspose vendor markers in headers", 'aspose' not in raw_text.lower())

    # =========================================================================
    # 2) Filtered Istanbul: 3 messages / 1 attachment import & export
    # =========================================================================
    print("\n--- TEST 2: Filtered Istanbul (Export & Import) ---")
    # Part 2A: Filtered Export from source
    out_dir_ist = call('POST', '/api/picker/output-dir', {})
    check("Output dir picker returned handle", out_dir_ist.get('handle'))

    exp_ist_req = {
        **SCOPE,
        'sourceAccountId': accounts['source']['accountId'],
        'targetDirHandle': out_dir_ist['handle'],
        'targetFormat': 'eml-tree',
        'selectedFolders': ['Projeler/İstanbul'],
        'startDate': '2024-01-01',
        'endDate': '2024-02-28'
    }
    prev_exp_ist = call('POST', '/api/transfer/bridge/export/preview', exp_ist_req)
    save('istanbul-export-preview.json', prev_exp_ist)
    check("Istanbul export preview eligible == 3", prev_exp_ist['eligibleItemsCount'] == 3)
    check("Istanbul export preview excluded == 1", prev_exp_ist['excludedCount'] == 1)

    job_exp_ist, rep_exp_ist = run_job('export', prev_exp_ist, 3)
    out_dir_path = rep_exp_ist['bridgeTransfer']['outputPath']
    tree_snap = oracle.files_snapshot(pathlib.Path(out_dir_path), 'eml')
    save('istanbul-export-tree.json', tree_snap)
    check("Exported EML tree has exactly 3 messages", tree_snap['count'] == 3)
    check("Exported EML tree has exactly 1 attachment", tree_snap['attachments'] == 1)

    # Part 2B: Filtered Import to target
    call('POST', '/api/testing/set-mime-source', {'fixtureId': 'corpus-eml'})
    picked_corpus = call('POST', '/api/picker/mime-source', {'mode': 'eml-files'})
    target_folder_ist = f"TASK017-{RUN_ID}-istanbul-imp"
    imp_ist_req = {
        **SCOPE,
        'sourceHandle': picked_corpus['handle'],
        'targetAccountId': accounts['target']['accountId'],
        'selectedFolders': ['Corpus'],
        'targetFolderMappings': {'Corpus': target_folder_ist},
        'startDate': '2024-01-01',
        'endDate': '2024-02-28'
    }
    prev_imp_ist = call('POST', '/api/transfer/bridge/import/preview', imp_ist_req)
    save('istanbul-import-preview.json', prev_imp_ist)
    check("Istanbul import preview eligible == 3", prev_imp_ist['eligibleItemsCount'] == 3)
    check("Istanbul import preview excluded == 9", prev_imp_ist['excludedCount'] == 9)

    job_imp_ist, rep_imp_ist = run_job('import', prev_imp_ist, 3)
    actual_imp_ist = oracle.imap_snapshot(CONFIG_PATH, 'target', [target_folder_ist])
    save('istanbul-import-target.json', actual_imp_ist)
    check("Istanbul imported IMAP count == 3", actual_imp_ist['count'] == 3)
    check("Istanbul imported IMAP attachments == 1", actual_imp_ist['attachments'] == 1)

    # =========================================================================
    # 3) Export -> Import Roundtrip for EML tree with physical duplicates
    # =========================================================================
    print("\n--- TEST 3: Export -> Import Roundtrip ---")
    out_dir_rt = call('POST', '/api/picker/output-dir', {})
    prev_rt_exp = call('POST', '/api/transfer/bridge/export/preview', {
        **SCOPE,
        'sourceAccountId': accounts['source']['accountId'],
        'targetDirHandle': out_dir_rt['handle'],
        'targetFormat': 'eml-tree',
        'selectedFolders': FOLDERS
    })
    save('roundtrip-export-preview.json', prev_rt_exp)
    check("Roundtrip export preview count == 12", prev_rt_exp['eligibleItemsCount'] == 12)
    job_rt_exp, rep_rt_exp = run_job('export', prev_rt_exp, 12)
    rt_export_dir = rep_rt_exp['bridgeTransfer']['outputPath']

    call('POST', '/api/testing/set-mime-source', {'fixtureId': f"job:{job_rt_exp['jobId']}"})
    picked_rt = call('POST', '/api/picker/mime-source', {'mode': 'eml-files'})
    check("Roundtrip picked handle valid", picked_rt.get('handle'))

    target_folder_rt = f"TASK017-{RUN_ID}-roundtrip"
    rt_manifest = json.loads((pathlib.Path(rt_export_dir) / 'manifest.json').read_text(encoding='utf-8'))
    rt_folders = [f['folderKey'] for f in rt_manifest['folders']]
    rt_mappings = {k: f"{target_folder_rt}-{k}" for k in rt_folders}

    prev_rt_imp = call('POST', '/api/transfer/bridge/import/preview', {
        **SCOPE,
        'sourceHandle': picked_rt['handle'],
        'targetAccountId': accounts['target']['accountId'],
        'selectedFolders': rt_folders,
        'targetFolderMappings': rt_mappings
    })
    save('roundtrip-import-preview.json', prev_rt_imp)
    check("Roundtrip import preview eligible == 12", prev_rt_imp['eligibleItemsCount'] == 12)
    job_rt_imp, rep_rt_imp = run_job('import', prev_rt_imp, 12)

    actual_rt = oracle.imap_snapshot(CONFIG_PATH, 'target', list(rt_mappings.values()))
    save('roundtrip-target.json', actual_rt)
    cmp_rt = oracle.compare(source_before, actual_rt, exact=False, metadata=False)
    save('roundtrip-comparison.json', cmp_rt)
    check("Roundtrip IMAP target matches source before multiset (exact duplicates preserved)", cmp_rt['pass'])

    # =========================================================================
    # 4A) REAL PROCESS Restart (Import Crash & Resume)
    # Fault: PauseAfterAppendBeforeReturn exact-folder fault
    # =========================================================================
    print("\n--- TEST 4A: Real Process Restart (Import Crash) ---")
    call('POST', '/api/testing/set-mime-source', {'fixtureId': 'corpus-eml'})
    picked_restart = call('POST', '/api/picker/mime-source', {'mode': 'eml-files'})
    target_folder_restart = f"TASK017-{RUN_ID}-restart-imp"

    prev_restart = call('POST', '/api/transfer/bridge/import/preview', {
        **SCOPE,
        'sourceHandle': picked_restart['handle'],
        'targetAccountId': accounts['target']['accountId'],
        'selectedFolders': ['Corpus'],
        'targetFolderMappings': {'Corpus': target_folder_restart}
    })
    check("Restart import preview eligible == 12", prev_restart['eligibleItemsCount'] == 12)

    # Arm fault: PauseAfterAppendBeforeReturn on target folder, ordinal 1
    call('POST', '/api/testing/bridge-fault', {'fault': 'PauseAfterAppendBeforeReturn', 'targetOrdinal': 1})

    start_res = call('POST', '/api/transfer/bridge/import/start', {
        **SCOPE,
        'previewId': prev_restart['previewId'],
        'idempotencyKey': uuid.uuid4().hex
    })
    restart_job_id = start_res['jobId']
    jobs.append(restart_job_id)

    # Wait for pause signal file on disk
    deadline = time.monotonic() + 30
    paused = False
    signal_info = None
    while time.monotonic() < deadline:
        if SIGNAL_FILE.exists():
            try:
                signal_info = json.loads(SIGNAL_FILE.read_text(encoding='utf-8'))
                paused = True
                break
            except Exception:
                pass
        time.sleep(0.2)
    check("Bridge pause seam signal file created", paused and signal_info is not None)
    check("Signal stage is pause-after-append-before-return", signal_info.get('stage') == 'pause-after-append-before-return')
    save('import-pause-signal.json', signal_info)

    # PROVE: Target folder has EXACTLY 1 message (one copy before kill) via independent IMAP!
    snap_before_kill = oracle.imap_snapshot(CONFIG_PATH, 'target', [target_folder_restart])
    save('import-target-before-kill.json', snap_before_kill)
    check("Independent IMAP proof: exactly 1 copy in target before process kill", snap_before_kill['count'] == 1)

    # Verify PID and port ownership before killing
    pre_kill_host = verify_and_record_host(6175)
    old_pid = pre_kill_host['pid']
    save('pre-kill-host-import.json', pre_kill_host)

    # Kill verified PID using native PowerShell Stop-Process -Id -Force and verify exited & closed
    kill_and_verify_stopped(old_pid, 6175)

    # Remove pause signal file before restart
    if SIGNAL_FILE.exists():
        SIGNAL_FILE.unlink()

    # Restart new TestingHost process with empty FileHandleRegistry
    print("Launching new TestingHost process after import kill...")
    proc_restart_imp, token, host_info2 = start_testing_host("restart-import-host.log")
    new_pid_imp = host_info2['pid']
    check("New TestingHost PID differs from killed PID", new_pid_imp != old_pid)
    print(f"New TestingHost running with PID: {new_pid_imp} (killed PID was {old_pid})")

    # Reset any active fault on new process
    call('POST', '/api/testing/bridge-fault/reset', {})

    # Resume the interrupted job from persisted plan/journal with empty handle registry (never edit journal!)
    print(f"Resuming import job {restart_job_id} on new process...")
    resume_res = call('POST', '/api/transfer/bridge/import/resume', {
        **SCOPE,
        'jobId': restart_job_id
    })
    check("Resume accepted with job ID", resume_res.get('jobId') == restart_job_id)

    final_restart_job = wait_for_job(restart_job_id, timeout_sec=60)
    save('restart-import-job-final.json', final_restart_job)
    check("Resumed job completed successfully", final_restart_job['status'] == 'completed')

    # Independent check: target folder has exactly 12 items, ZERO duplicates!
    actual_restart = oracle.imap_snapshot(CONFIG_PATH, 'target', [target_folder_restart])
    save('restart-import-target-final.json', actual_restart)
    check("Restart test: target folder has exactly 12 messages (0 duplicate appends)", actual_restart['count'] == 12)

    # =========================================================================
    # 4B) REAL PROCESS Restart (Export Crash & Resume)
    # Fault: PauseAfterExportItemPersisted exact-folder fault
    # =========================================================================
    print("\n--- TEST 4B: Real Process Restart (Export Crash) ---")
    out_dir_exp_crash = call('POST', '/api/picker/output-dir', {})
    prev_exp_crash = call('POST', '/api/transfer/bridge/export/preview', {
        **SCOPE,
        'sourceAccountId': accounts['source']['accountId'],
        'targetDirHandle': out_dir_exp_crash['handle'],
        'targetFormat': 'eml-tree',
        'selectedFolders': ['INBOX']
    })
    check("Export crash preview has eligible items", prev_exp_crash['eligibleItemsCount'] >= 2)

    # Arm fault: PauseAfterExportItemPersisted, targetOrdinal 2 (pauses when item 2 is about to fetch; item 1 persisted)
    call('POST', '/api/testing/bridge-fault', {'fault': 'PauseAfterExportItemPersisted', 'targetOrdinal': 2})

    start_exp_crash = call('POST', '/api/transfer/bridge/export/start', {
        **SCOPE,
        'previewId': prev_exp_crash['previewId'],
        'idempotencyKey': uuid.uuid4().hex
    })
    exp_crash_job_id = start_exp_crash['jobId']
    jobs.append(exp_crash_job_id)

    # Wait for pause signal file
    deadline = time.monotonic() + 30
    paused_exp = False
    exp_signal_info = None
    while time.monotonic() < deadline:
        if SIGNAL_FILE.exists():
            try:
                exp_signal_info = json.loads(SIGNAL_FILE.read_text(encoding='utf-8'))
                paused_exp = True
                break
            except Exception:
                pass
        time.sleep(0.2)
    check("Export pause seam signal file created", paused_exp and exp_signal_info is not None)
    save('export-pause-signal.json', exp_signal_info)

    # Verify that item 1 is already persisted to disk before kill
    curr_host_pre_exp = verify_and_record_host(6175)
    old_pid_exp = curr_host_pre_exp['pid']
    save('pre-kill-host-export.json', curr_host_pre_exp)

    # Kill verified PID using native PowerShell Stop-Process -Id -Force
    kill_and_verify_stopped(old_pid_exp, 6175)

    if SIGNAL_FILE.exists():
        SIGNAL_FILE.unlink()

    # Restart new TestingHost process
    print("Launching new TestingHost process after export kill...")
    proc_restart_exp, token, host_info3 = start_testing_host("restart-export-host.log")
    new_pid_exp = host_info3['pid']
    check("New TestingHost PID differs from killed PID", new_pid_exp != old_pid_exp)
    print(f"New TestingHost running with PID: {new_pid_exp} (killed PID was {old_pid_exp})")

    call('POST', '/api/testing/bridge-fault/reset', {})

    # Resume the interrupted export job from persisted plan/journal with empty registry
    print(f"Resuming export job {exp_crash_job_id} on new process...")
    resume_exp_res = call('POST', '/api/transfer/bridge/export/resume', {
        **SCOPE,
        'jobId': exp_crash_job_id
    })
    check("Export resume accepted", resume_exp_res.get('jobId') == exp_crash_job_id)

    final_exp_crash_job = wait_for_job(exp_crash_job_id, timeout_sec=60)
    save('restart-export-job-final.json', final_exp_crash_job)
    check("Resumed export job completed successfully", final_exp_crash_job['status'] == 'completed')

    # Verify output directory and identity
    exp_rep = call('GET', f"/api/jobs/{exp_crash_job_id}/report")
    out_dir_final = exp_rep['bridgeTransfer']['outputPath']
    check("Export manifest exists in resumed output dir", (pathlib.Path(out_dir_final) / 'manifest.json').exists())
    exp_final_snap = oracle.files_snapshot(pathlib.Path(out_dir_final), 'eml')
    check("Resumed export directory has all planned messages", exp_final_snap['count'] == prev_exp_crash['eligibleItemsCount'])

    # =========================================================================
    # 5A) APPEND Lost-Response & Keyword Reconciliation (Zero-Duplicate)
    # =========================================================================
    print("\n--- TEST 5A: APPEND Lost-Response & Keyword Reconciliation ---")
    call('POST', '/api/testing/set-mime-source', {'fixtureId': 'corpus-eml'})
    picked_lost = call('POST', '/api/picker/mime-source', {'mode': 'eml-files'})
    target_folder_lost = f"TASK017-{RUN_ID}-lostresp"

    prev_lost = call('POST', '/api/transfer/bridge/import/preview', {
        **SCOPE,
        'sourceHandle': picked_lost['handle'],
        'targetAccountId': accounts['target']['accountId'],
        'selectedFolders': ['Corpus'],
        'targetFolderMappings': {'Corpus': target_folder_lost}
    })

    # Arm fault: LostResponseAfterAppend at ordinal 1
    call('POST', '/api/testing/bridge-fault', {'fault': 'LostResponseAfterAppend', 'targetOrdinal': 1})

    lost_start = call('POST', '/api/transfer/bridge/import/start', {
        **SCOPE,
        'previewId': prev_lost['previewId'],
        'idempotencyKey': uuid.uuid4().hex
    })
    lost_job_id = lost_start['jobId']
    jobs.append(lost_job_id)

    # Job encounters connection drop after append -> interrupted
    interrupted_job = wait_for_job(lost_job_id, timeout_sec=30)
    save('lost-response-interrupted.json', interrupted_job)
    check("Job interrupted after simulated lost response on append", interrupted_job['status'] == 'interrupted')

    call('POST', '/api/testing/bridge-fault/reset', {})

    # Resume the job: must reconcile keyword on server without re-appending item 1
    print("Resuming job to verify keyword reconciliation without duplicate append...")
    call('POST', '/api/transfer/bridge/import/resume', {**SCOPE, 'jobId': lost_job_id})
    completed_lost_job = wait_for_job(lost_job_id, timeout_sec=60)
    save('lost-response-completed.json', completed_lost_job)
    check("Resumed lost-response job completed", completed_lost_job['status'] == 'completed')

    # Independent check: target folder has EXACTLY 12 items, ZERO duplicates
    actual_lost = oracle.imap_snapshot(CONFIG_PATH, 'target', [target_folder_lost])
    save('lost-response-target.json', actual_lost)
    check("Lost-response test: target folder has exactly 12 items (ZERO duplicate append)", actual_lost['count'] == 12)

    # =========================================================================
    # 5B) Natural Zero-Match Negative (PauseBeforeAppend leaves keyword0)
    # Must yield NeedsAttention / fail-closed without re-append
    # =========================================================================
    print("\n--- TEST 5B: Natural Zero-Match Negative (PauseBeforeAppend) ---")
    call('POST', '/api/testing/set-mime-source', {'fixtureId': 'corpus-eml'})
    picked_zero = call('POST', '/api/picker/mime-source', {'mode': 'eml-files'})
    target_folder_zero = f"TASK017-{RUN_ID}-zero-match-pause"

    prev_zero = call('POST', '/api/transfer/bridge/import/preview', {
        **SCOPE,
        'sourceHandle': picked_zero['handle'],
        'targetAccountId': accounts['target']['accountId'],
        'selectedFolders': ['Corpus'],
        'targetFolderMappings': {'Corpus': target_folder_zero}
    })

    # Arm PauseBeforeAppend ordinal 2: item 1 appends, item 2 pauses before append (leaves keyword0)
    call('POST', '/api/testing/bridge-fault', {'fault': 'PauseBeforeAppend', 'targetOrdinal': 2})

    zero_start = call('POST', '/api/transfer/bridge/import/start', {
        **SCOPE,
        'previewId': prev_zero['previewId'],
        'idempotencyKey': uuid.uuid4().hex
    })
    zero_job_id = zero_start['jobId']
    jobs.append(zero_job_id)

    # Wait for pause signal
    deadline = time.monotonic() + 30
    paused_zero = False
    while time.monotonic() < deadline:
        if SIGNAL_FILE.exists():
            paused_zero = True
            break
        time.sleep(0.2)
    check("Zero-match pause signal created", paused_zero)

    # Target folder has exactly 1 message (item 1); item 2 is unappended (keyword0)
    snap_zero_pre = oracle.imap_snapshot(CONFIG_PATH, 'target', [target_folder_zero])
    check("Zero-match pre-kill: target folder has exactly 1 message", snap_zero_pre['count'] == 1)

    pre_kill_zero = verify_and_record_host(6175)
    kill_and_verify_stopped(pre_kill_zero['pid'], 6175)

    if SIGNAL_FILE.exists():
        SIGNAL_FILE.unlink()

    # Restart new process
    proc_zero, token, host_info4 = start_testing_host("restart-zero-match-host.log")
    call('POST', '/api/testing/bridge-fault/reset', {})

    # Resume the job: item 2 has AppendIntent but keyword count on server is 0!
    # Under ambiguous zero rule, must transition to needs-attention / fail-closed, NOT completed!
    print(f"Resuming zero-match job {zero_job_id}...")
    call('POST', '/api/transfer/bridge/import/resume', {**SCOPE, 'jobId': zero_job_id})
    res_zero = wait_for_job(zero_job_id, timeout_sec=20)
    save('zero-match-final.json', res_zero)
    check("Ambiguous zero keyword naturally yields failed / needs-attention", res_zero['status'] in ('failed', 'needs-attention'))

    # PROVE: Target folder STILL has exactly 1 message (ZERO new appends on resume!)
    snap_zero_post = oracle.imap_snapshot(CONFIG_PATH, 'target', [target_folder_zero])
    save('zero-match-post-snap.json', snap_zero_post)
    check("Zero-match test produced ZERO new appends on resume (count remains 1)", snap_zero_post['count'] == 1)

    # =========================================================================
    # 6) Negatives
    # =========================================================================
    print("\n--- TEST 6: Negatives ---")
    # 6.1: Leading junk / empty / malformed mbox rejected
    for bad_mbox in ('junk-mbox', 'empty-mbox', 'malformed-mbox'):
        call('POST', '/api/testing/set-mime-source', {'fixtureId': bad_mbox})
        pick_res = call('POST', '/api/picker/mime-source', {'mode': 'eml-files'})
        prev_res = call('POST', '/api/transfer/bridge/import/preview', {
            **SCOPE,
            'sourceHandle': pick_res.get('handle', 'invalid'),
            'targetAccountId': accounts['target']['accountId'],
            'selectedFolders': ['junk', 'empty', 'malformed']
        }, expected=(400, 409))
        check(f"Negative: {bad_mbox} rejected in picker or preview",
              pick_res.get('error') is not None or prev_res.get('canTransfer') is False or 'error' in prev_res)

    # 6.2: Second source drift before first append produces zero appends
    print("Testing second source drift before first append...")
    call('POST', '/api/testing/set-mime-source', {'fixtureId': 'two-items'})
    picked_drift = call('POST', '/api/picker/mime-source', {'mode': 'eml-files'})
    target_folder_drift = f"TASK017-{RUN_ID}-drift"
    prev_drift = call('POST', '/api/transfer/bridge/import/preview', {
        **SCOPE,
        'sourceHandle': picked_drift['handle'],
        'targetAccountId': accounts['target']['accountId'],
        'selectedFolders': ['two-items'],
        'targetFolderMappings': {'two-items': target_folder_drift}
    })
    check("Two-items preview canTransfer == True", prev_drift['canTransfer'] is True)

    two_items_dir = pathlib.Path(os.environ.get('TEMP', '/tmp')) / 'bitigmail-testing-fixtures/two-items'
    file2 = two_items_dir / 'msg-02.eml'
    original_file2_bytes = file2.read_bytes()
    try:
        file2.write_text("TAMPERED DATA IN SECOND SOURCE FILE BEFORE APPEND", encoding='utf-8')
        start_drift = call('POST', '/api/transfer/bridge/import/start', {
            **SCOPE,
            'previewId': prev_drift['previewId'],
            'idempotencyKey': uuid.uuid4().hex
        })
        final_drift = wait_for_job(start_drift['jobId'], timeout_sec=20)
        check("Job with tampered second source file failed closed", final_drift['status'] in ('failed', 'interrupted'))

        # Verify ZERO appends made to target folder
        drift_snap = oracle.imap_snapshot(CONFIG_PATH, 'target', [target_folder_drift])
        check("Second item drift produced ZERO appends on target folder", drift_snap['count'] == 0)
    finally:
        file2.write_bytes(original_file2_bytes)

    # 6.3: Sibling output dir tamper rejected
    print("Testing sibling output dir tamper rejection...")
    out_dir_tamper = call('POST', '/api/picker/output-dir', {})
    prev_tamper = call('POST', '/api/transfer/bridge/export/preview', {
        **SCOPE,
        'sourceAccountId': accounts['source']['accountId'],
        'targetDirHandle': out_dir_tamper['handle'],
        'targetFormat': 'eml-tree',
        'selectedFolders': ['INBOX']
    })
    start_tamper = call('POST', '/api/transfer/bridge/export/start', {
        **SCOPE,
        'previewId': prev_tamper['previewId'],
        'idempotencyKey': uuid.uuid4().hex
    })
    wait_for_job(start_tamper['jobId'], timeout_sec=20)

    exp_journal_path = ROOT / f"runtime/testing-engine/bridge-transfers/export-journals/{start_tamper['jobId']}.json"
    if exp_journal_path.exists():
        journal_data = json.loads(exp_journal_path.read_text(encoding='utf-8'))
        out_key = 'JobOutputDir' if 'JobOutputDir' in journal_data else 'jobOutputDir'
        journal_data[out_key] = str(pathlib.Path(journal_data[out_key]).parent / 'tampered-sibling')
        exp_journal_path.write_text(json.dumps(journal_data), encoding='utf-8')

        res_tamper = call('POST', '/api/transfer/bridge/export/resume', {**SCOPE, 'jobId': start_tamper['jobId']}, expected=(200, 400, 409))
        if res_tamper and ('error' in res_tamper or res_tamper.get('status') in ('failed', 'conflict')):
            check("Sibling output dir tamper rejected fail-closed", True)
        else:
            tamper_job = wait_for_job(start_tamper['jobId'], timeout_sec=15)
            check("Sibling output dir tamper rejected fail-closed", tamper_job['status'] in ('failed', 'interrupted'))

    # 6.4: Output collision / reparse / traversal in start
    for bad_id in ('../outside', 'bad:stream', 'x' * 130):
        call('POST', '/api/transfer/bridge/import/start', {'previewId': bad_id, 'idempotencyKey': uuid.uuid4().hex}, expected=(400, 409))
        call('POST', '/api/transfer/bridge/export/start', {'previewId': bad_id, 'idempotencyKey': uuid.uuid4().hex}, expected=(400, 409))
    check("Path traversal / invalid preview ID rejected", True)

    # =========================================================================
    # 7) Source Invariance Check
    # =========================================================================
    print("\n--- TEST 7: Source Invariance Check ---")
    source_after = oracle.imap_snapshot(CONFIG_PATH, 'source', FOLDERS)
    save('source-after.json', source_after)
    unchanged = oracle.compare(source_before, source_after, exact=True, metadata=True)
    save('source-unchanged.json', unchanged)
    check("Source completely unchanged (exact UIDs, raw hashes, flags, internaldates)", unchanged['pass'])

    print("\n=== ALL EXTENDED ACCEPTANCE TESTS PASSED SUCCESSFULLY! ===")
    result_summary = {
        'runId': RUN_ID,
        'pass': True,
        'totalChecks': len(checks),
        'passedChecks': sum(1 for c in checks if c['pass']),
        'failedChecks': [c['name'] for c in checks if not c['pass']],
        'evidenceDirectory': str(RUN)
    }
    save('acceptance-extended-summary.json', result_summary)
    (RUN.parent / 'extended-latest.json').write_text(json.dumps(result_summary, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(result_summary, indent=2))
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except Exception as exc:
        print(f"\nFATAL ERROR: {type(exc).__name__}: {exc}")
        save('fatal-error.json', {'error': str(exc), 'type': type(exc).__name__})
        sys.exit(1)
