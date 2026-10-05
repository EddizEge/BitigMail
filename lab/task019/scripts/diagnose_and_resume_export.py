#!/usr/bin/env python3
"""TASK-019 Diagnostic & Bounded Resume Script.
Performs safe public resume of failed EML export job-a3d2e097b416.
Captures exact failing path, stack trace, and record using TestingHost FirstChanceException logger.
Preserves original failed evidence and measures recovery timing separately.
"""
import datetime
import json
import pathlib
import psutil
import subprocess
import sys
import time
import urllib.request
import urllib.error

ROOT = pathlib.Path(__file__).resolve().parents[3]
DOTNET_EXE = ROOT / '.tools/dotnet/dotnet.exe'
TESTING_HOST_DLL = ROOT / 'engine/BitigMail.TestingHost/bin/Release/net8.0-windows/BitigMail.TestingHost.dll'
PID_FILE = ROOT / 'runtime/testing-engine/testinghost.pid'

EVIDENCE_DIR = ROOT / '.codex-coordination/evidence/TASK-019/scale-22f1cb00'
DIAG_LOG = EVIDENCE_DIR / 'export-resume-diag.log'

JOB_ID = 'job-a3d2e097b416'
SCOPE = {
    'companyId': 'company-task019-scale',
    'projectId': 'project-task019-resilience'
}


def get_listening_pid(port=6175):
    cmd = f"Get-NetTCPConnection -LocalPort {port} -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess"
    proc = subprocess.run(['pwsh', '-NoProfile', '-Command', cmd], capture_output=True, text=True)
    out = proc.stdout.strip()
    if out and out.isdigit():
        return int(out)
    return None


def kill_pid(pid):
    subprocess.run(['pwsh', '-NoProfile', '-Command', f"Stop-Process -Id {pid} -Force -ErrorAction SilentlyContinue"], capture_output=True)


def main():
    print(f"=== TASK-019 Diagnostic Resume for {JOB_ID} ===")
    print(f"Evidence dir: {EVIDENCE_DIR}")

    # Ensure port 6175 is clear
    existing_pid = get_listening_pid(6175)
    if existing_pid:
        print(f"Cleaning up existing process on port 6175 (PID: {existing_pid})...")
        kill_pid(existing_pid)
        time.sleep(1)

    if PID_FILE.exists():
        try:
            PID_FILE.unlink()
        except Exception:
            pass

    # Launch TestingHost with output to DIAG_LOG
    diag_file = open(DIAG_LOG, 'w', encoding='utf-8')
    creationflags = 0x08000000 if sys.platform == 'win32' else 0
    proc = subprocess.Popen(
        [str(DOTNET_EXE), str(TESTING_HOST_DLL)],
        cwd=str(ROOT),
        stdout=diag_file,
        stderr=subprocess.STDOUT,
        creationflags=creationflags
    )
    print(f"TestingHost process spawned (PID: {proc.pid})")

    # Wait for /api/session
    t0_start = time.monotonic()
    token = None
    while time.monotonic() - t0_start < 30:
        try:
            headers = {'Origin': 'http://127.0.0.1:5173', 'Content-Type': 'application/json'}
            req = urllib.request.Request('http://127.0.0.1:6175/api/session', data=b'{}', headers=headers, method='POST')
            with urllib.request.urlopen(req, timeout=3) as resp:
                if resp.status == 200:
                    body = json.loads(resp.read().decode('utf-8'))
                    token = body.get('token')
                    break
        except Exception:
            time.sleep(0.5)

    if not token:
        print("[ERROR] TestingHost failed to start and respond to /api/session")
        proc.kill()
        return 1

    print(f"TestingHost session established (token: {token[:8]}...)")

    def api(method, path, body=None):
        headers = {
            'Origin': 'http://127.0.0.1:5173',
            'Content-Type': 'application/json',
            'X-BitigMail-Session': token
        }
        raw = None if body is None else json.dumps(body).encode('utf-8')
        if method == 'POST' and raw is None:
            raw = b'{}'
        req = urllib.request.Request('http://127.0.0.1:6175' + path, data=raw, headers=headers, method=method)
        try:
            with urllib.request.urlopen(req, timeout=30) as r:
                text = r.read().decode('utf-8', errors='replace')
                return r.status, json.loads(text) if text else None
        except urllib.error.HTTPError as he:
            text = he.read().decode('utf-8', errors='replace')
            return he.code, json.loads(text) if text else None

    # Check pre-resume state of job
    st_code, pre_job = api('GET', f"/api/jobs/{JOB_ID}")
    print(f"\n--- Pre-Resume Job State ---")
    print(f"HTTP Status: {st_code}")
    print(f"Job Status: {pre_job.get('status')}")
    print(f"Stage: {pre_job.get('stage')}")
    print(f"ItemsWritten: {pre_job.get('itemsWritten')}/{pre_job.get('totalItems')}")
    print(f"ErrorMessage: {pre_job.get('errorMessage')}")
    print(f"OutputPath: {pre_job.get('outputPath')}")

    # Call exact public resume route: POST /api/transfer/bridge/export/resume/{jobId}
    print(f"\n--- Triggering Public Resume Route ---")
    t0_rec = time.perf_counter()
    res_code, res_resp = api('POST', f"/api/transfer/bridge/export/resume/{JOB_ID}", SCOPE)
    print(f"Resume HTTP response: {res_code}, body: {res_resp}")

    if res_code != 200:
        print(f"[ERROR] Resume request failed with HTTP {res_code}: {res_resp}")

    # Poll until job finishes
    print(f"\n--- Polling Job Progress ---")
    last_written = pre_job.get('itemsWritten', 0)
    terminal_job = None
    poll_start = time.monotonic()
    while time.monotonic() - poll_start < 180:
        _, current_job = api('GET', f"/api/jobs/{JOB_ID}")
        st = current_job.get('status')
        w = current_job.get('itemsWritten', 0)
        if w != last_written:
            print(f"Progress: {w}/{current_job.get('totalItems', 1024)} (stage: {current_job.get('stage')})")
            last_written = w
        if st in ('completed', 'failed', 'interrupted', 'needs-attention'):
            terminal_job = current_job
            break
        time.sleep(0.3)

    t_rec_elapsed = time.perf_counter() - t0_rec

    if not terminal_job:
        print("[ERROR] Timed out waiting for job to reach terminal state.")
        _, terminal_job = api('GET', f"/api/jobs/{JOB_ID}")

    print(f"\n--- Post-Resume Result (Recovery Elapsed: {t_rec_elapsed:.2f}s) ---")
    print(f"Terminal Status: {terminal_job.get('status')}")
    print(f"Terminal Stage: {terminal_job.get('stage')}")
    print(f"Items Written: {terminal_job.get('itemsWritten')}/{terminal_job.get('totalItems')}")
    print(f"ErrorMessage: {terminal_job.get('errorMessage')}")

    # Check for report if completed
    report = None
    if terminal_job.get('status') == 'completed':
        _, report = api('GET', f"/api/jobs/{JOB_ID}/report")
        report_path = EVIDENCE_DIR / 'stage2a-export-eml-resumed-report.json'
        report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
        print(f"Saved resumed report to {report_path}")

    # Save diagnostic summary
    diag_summary = {
        'jobId': JOB_ID,
        'originalPreState': {
            'status': pre_job.get('status'),
            'itemsWritten': pre_job.get('itemsWritten'),
            'errorMessage': pre_job.get('errorMessage'),
            'outputPath': pre_job.get('outputPath')
        },
        'recoveryTiming': {
            'elapsed_sec': t_rec_elapsed,
            'items_completed': terminal_job.get('itemsWritten') - pre_job.get('itemsWritten', 0)
        },
        'terminalState': terminal_job,
        'reportSummary': {
            'totalPlanned': report['bridgeTransfer']['totalPlanned'] if report else None,
            'totalVerified': report['bridgeTransfer']['totalVerified'] if report else None,
            'totalFailed': report['bridgeTransfer']['totalFailed'] if report else None
        } if report else None
    }
    (EVIDENCE_DIR / 'export-resume-diag-summary.json').write_text(
        json.dumps(diag_summary, ensure_ascii=False, indent=2), encoding='utf-8'
    )

    # Stop TestingHost
    print("\nStopping TestingHost...")
    proc.terminate()
    try:
        proc.wait(timeout=5)
    except Exception:
        proc.kill()
    diag_file.close()

    # Read and inspect diagnostic log for FirstChanceException
    diag_content = DIAG_LOG.read_text(encoding='utf-8', errors='replace')
    fc_hits = [line for line in diag_content.splitlines() if 'DIAG_FIRST_CHANCE_UNAUTHORIZED_ACCESS' in line or 'DIAG_STACK_TRACE' in line]
    if fc_hits:
        print(f"\n[DIAGNOSTIC FIRST-CHANCE EXCEPTION CAUGHT ({len(fc_hits)} lines)]:")
        for l in fc_hits[:20]:
            print(f"  {l}")
    else:
        print("\n[DIAGNOSTIC]: No UnauthorizedAccessException was thrown during this resume attempt.")

    return 0 if terminal_job.get('status') == 'completed' else 2


if __name__ == '__main__':
    sys.exit(main())
