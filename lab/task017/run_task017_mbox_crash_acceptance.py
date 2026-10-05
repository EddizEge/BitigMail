"""TASK-017 MBOX Export Crash/Restart Acceptance Runner.
Dedicated test for targetFormat=mboxrd actual crash and resume:
- 5 INBOX messages exported to mboxrd
- Pause after first item staging persisted
- Verify exact TestingHost PID / listener / command line / creation time
- Kill exact PID with Stop-Process -Id -Force, verify process dead and port closed
- Restart hidden new PID with empty FileHandleRegistry
- Resume same job/output identity without editing journal
- Final mbox contains exactly 5 raw messages
- Independent stdlib / approved reader compare exact own source snapshot, attachment multiset, and manifest
- Preserve source UID/raw/flags/internaldate before/after
"""
import collections
import datetime
import json
import os
import pathlib
import re
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'lab/task017'))
import verify_bridge as oracle

RUN_ID = 'mbox-' + uuid.uuid4().hex[:10]
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
    'projectId': 'mbox-crash-pilot',
    'companyName': 'TASK017 MBOX Export Crash Test',
    'projectName': 'MBOX Export Crash and Resume'
}

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


def wait_for_job(job_id, terminal_statuses=('completed', 'failed', 'interrupted', 'needs-attention'), timeout_sec=60):
    start = time.monotonic()
    last = None
    while time.monotonic() - start < timeout_sec:
        last = call('GET', f"/api/jobs/{job_id}")
        if last.get('status') in terminal_statuses:
            return last
        time.sleep(0.5)
    raise TimeoutError(f"Job {job_id} timed out; last status: {last}")


def main():
    global token
    print(f"=== TASK-017 MBOX Export Crash & Resume Acceptance (Run ID: {RUN_ID}) ===")
    save('meta.json', {
        'runId': RUN_ID,
        'timestamp': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'scope': SCOPE
    })

    listen_pid = get_listening_pid(6175)
    if listen_pid is not None:
        print(f"Found existing process {listen_pid} on port 6175, stopping it...")
        kill_and_verify_stopped(listen_pid, 6175)

    if SIGNAL_FILE.exists():
        SIGNAL_FILE.unlink()

    print("Launching initial TestingHost process...")
    proc_initial, token, host_info1 = start_testing_host("initial-host.log")
    pid1 = host_info1['pid']
    print(f"TestingHost initial PID: {pid1}")

    # Create isolated source account
    cfg = CONFIG['source']
    acc_src = call('POST', '/api/accounts', {
        **SCOPE,
        'displayName': f"TASK017 Mbox Source",
        'email': cfg['email'],
        'host': cfg['imapHost'],
        'port': cfg['imapPort'],
        'tlsMode': 'none',
        'username': cfg['username'],
        'password': cfg['password']
    })
    save('account.json', acc_src)

    # 1. Capture initial source snapshot for INBOX (5 messages)
    print("\n--- Capturing source INBOX IMAP snapshot ---")
    source_inbox_before = oracle.imap_snapshot(CONFIG_PATH, 'source', ['INBOX'])
    save('source-inbox-before.json', source_inbox_before)
    check("Source INBOX before has exactly 5 messages", source_inbox_before['count'] == 5)

    # 2. Pick output directory
    out_dir_res = call('POST', '/api/picker/output-dir', {})
    check("Output dir picker returned handle", out_dir_res.get('handle'))

    # 3. Request MBOXRD Export Preview for INBOX
    prev_req = {
        **SCOPE,
        'sourceAccountId': acc_src['accountId'],
        'targetDirHandle': out_dir_res['handle'],
        'targetFormat': 'mboxrd',
        'selectedFolders': ['INBOX']
    }
    prev_res = call('POST', '/api/transfer/bridge/export/preview', prev_req)
    save('mbox-export-preview.json', prev_res)
    check("MBOX export preview targetFormat is mboxrd", prev_res.get('targetFormat') == 'mboxrd')
    check("MBOX export preview eligibleItemsCount == 5", prev_res.get('eligibleItemsCount') == 5)
    check("MBOX export preview canTransfer == True", prev_res.get('canTransfer') is True)

    # 4. Arm fault: PauseAfterExportItemPersisted at ordinal 2 (pauses when item 2 is about to fetch; item 1 is staged)
    call('POST', '/api/testing/bridge-fault', {'fault': 'PauseAfterExportItemPersisted', 'targetOrdinal': 2})

    # 5. Start MBOX export job
    start_res = call('POST', '/api/transfer/bridge/export/start', {
        **SCOPE,
        'previewId': prev_res['previewId'],
        'idempotencyKey': uuid.uuid4().hex
    })
    job_id = start_res['jobId']
    jobs.append(job_id)

    # 6. Wait for pause signal file on disk
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
    check("Export pause seam signal file created", paused and signal_info is not None)
    check("Signal stage is pause-after-export-item-persisted", signal_info.get('stage') == 'pause-after-export-item-persisted')
    save('mbox-pause-signal.json', signal_info)

    # 7. PROVE: Item 1 staging file is persisted to disk in job output directory before kill
    exp_journal_path = ROOT / f"runtime/testing-engine/bridge-transfers/export-journals/{job_id}.json"
    check("Export journal exists on disk before kill", exp_journal_path.exists())
    journal_data_pre = json.loads(exp_journal_path.read_text(encoding='utf-8'))
    out_dir_pre = pathlib.Path(journal_data_pre['JobOutputDir'])
    staging_dir_pre = out_dir_pre / '.staging' / 'fld_001'
    staged_item1 = staging_dir_pre / 'msg_00000001.raw'
    check("Item 1 staging directory exists", staging_dir_pre.exists())
    check("Item 1 staging raw file exists before kill", staged_item1.exists())
    check("Item 1 staging raw file length > 0", staged_item1.stat().st_size > 0)
    save('pre-kill-staging-check.json', {
        'stagingDir': str(staging_dir_pre),
        'stagedItem1': str(staged_item1),
        'sizeBytes': staged_item1.stat().st_size
    })

    # 8. Verify host process identity before kill
    pre_kill_host = verify_and_record_host(6175)
    check(f"Pre-kill host PID matches initial PID {pid1}", pre_kill_host['pid'] == pid1)
    save('pre-kill-host-info.json', pre_kill_host)

    # 9. Kill exact verified PID using native PowerShell Stop-Process -Id -Force
    kill_and_verify_stopped(pid1, 6175)

    if SIGNAL_FILE.exists():
        SIGNAL_FILE.unlink()

    # 10. Restart new TestingHost process with empty FileHandleRegistry
    print("Launching new TestingHost process after MBOX export crash...")
    proc_restart, token, host_info2 = start_testing_host("restart-mbox-host.log")
    pid2 = host_info2['pid']
    check("New TestingHost PID differs from killed PID", pid2 != pid1)
    print(f"New TestingHost running with PID: {pid2} (killed PID was {pid1})")

    call('POST', '/api/testing/bridge-fault/reset', {})

    # 11. Resume the interrupted MBOX export job from persisted plan/journal with empty registry (no journal edits!)
    print(f"Resuming MBOX export job {job_id} on new process...")
    resume_res = call('POST', '/api/transfer/bridge/export/resume', {
        **SCOPE,
        'jobId': job_id
    })
    check("Resume accepted with jobId", resume_res.get('jobId') == job_id)

    # 12. Wait for completed status
    final_job = wait_for_job(job_id, timeout_sec=60)
    save('mbox-export-job-final.json', final_job)
    check("Resumed MBOX export job completed successfully", final_job['status'] == 'completed')

    # 13. Check job report
    rep = call('GET', f"/api/jobs/{job_id}/report")
    save('mbox-export-report-final.json', rep)
    bt = rep.get('bridgeTransfer', {})
    check("Report totalVerified == 5", bt.get('totalVerified') == 5)
    check("Report totalFailed == 0", bt.get('totalFailed') == 0)

    # 14. Independent verification of final MBOX output directory
    final_output_path = pathlib.Path(bt['outputPath'])
    check("Final output directory exists", final_output_path.exists())
    check("Final output directory has manifest.json", (final_output_path / 'manifest.json').exists())
    manifest = json.loads((final_output_path / 'manifest.json').read_text(encoding='utf-8'))

    # Staging directory must be cleaned up
    check("Folder staging directory cleaned up after completion", not staging_dir_pre.exists())
    check("No raw staged files left in output directory", len(list(final_output_path.rglob('*.raw'))) == 0)

    # Final folder mbox contains exactly 5 raw messages
    mbox_files = list(final_output_path.glob('*.mbox'))
    check("Exactly 1 MBOX file produced for single folder", len(mbox_files) == 1)
    mbox_file = mbox_files[0]
    print(f"Inspecting final MBOX file: {mbox_file.name} ({mbox_file.stat().st_size} bytes)...")

    mbox_snap = oracle.files_snapshot(final_output_path, 'mbox')
    save('mbox-output-snapshot.json', mbox_snap)
    check("Final folder MBOX contains exactly 5 raw messages", mbox_snap['count'] == 5)
    check("MBOX attachments match source INBOX attachments", mbox_snap['attachments'] == source_inbox_before['attachments'])

    # Independent stdlib comparison with source INBOX
    cmp_res = oracle.compare(source_inbox_before, mbox_snap, exact=False, metadata=False)
    save('mbox-comparison.json', cmp_res)
    check("Independent stdlib compare confirms exact message multiset match", cmp_res['pass'])

    # Verify manifest integrity
    manifest_results = oracle.verify_manifest(source_inbox_before, manifest, final_output_path)
    save('manifest-verification.json', manifest_results)
    check("All manifest items pass independent verification", manifest_results['pass'])
    check("Manifest items count matches 5", manifest_results['count'] == 5)
    check("Manifest mappingPass is True", manifest_results['mappingPass'] is True)

    # 15. Source invariance check for INBOX
    print("\n--- Source Invariance Check ---")
    source_inbox_after = oracle.imap_snapshot(CONFIG_PATH, 'source', ['INBOX'])
    save('source-inbox-after.json', source_inbox_after)
    unchanged = oracle.compare(source_inbox_before, source_inbox_after, exact=True, metadata=True)
    save('source-inbox-unchanged.json', unchanged)
    check("Source INBOX completely unchanged (exact UIDs, raw hashes, flags, internaldates)", unchanged['pass'])

    print("\n=== MBOX EXPORT CRASH ACCEPTANCE TEST PASSED! ===")
    summary = {
        'runId': RUN_ID,
        'pass': True,
        'totalChecks': len(checks),
        'passedChecks': sum(1 for c in checks if c['pass']),
        'failedChecks': [c['name'] for c in checks if not c['pass']],
        'evidenceDirectory': str(RUN),
        'jobId': job_id,
        'pidBeforeKill': pid1,
        'pidAfterRestart': pid2,
        'mboxFile': str(mbox_file),
        'mboxSizeBytes': mbox_file.stat().st_size,
        'messageCount': mbox_snap['count']
    }
    save('acceptance-mbox-summary.json', summary)
    (RUN.parent / 'mbox-crash-latest.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(summary, indent=2))
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except Exception as exc:
        print(f"\nFATAL ERROR: {type(exc).__name__}: {exc}")
        save('fatal-error.json', {'error': str(exc), 'type': type(exc).__name__})
        sys.exit(1)
