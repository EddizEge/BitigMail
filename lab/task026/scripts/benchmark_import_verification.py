#!/usr/bin/env python3
"""One isolated 12-message import with final-verification timing and oracle."""
import json, pathlib, sys, time, urllib.request, urllib.error, uuid

ROOT = pathlib.Path(r'C:\Users\Eddiz\Documents\ChatGPT\Mail Manager')
sys.path.insert(0, str(ROOT / 'lab/task019/scripts'))
sys.path.insert(0, str(ROOT / 'lab/task017'))
sys.path.insert(0, str(ROOT / 'lab/task022/scripts'))
import run_task019_scale_pipeline as life
import verify_task019_oracle as source_oracle
import verify_bridge as oracle
from benchmark_preview_export import PhaseTelemetryCollector, telemetry_result

mode = sys.argv[1] if len(sys.argv) > 1 else 'baseline'
if mode not in ('baseline', 'after'): raise SystemExit('baseline|after')
run_id = mode + '-' + uuid.uuid4().hex[:8]
evidence = ROOT / '.codex-coordination/evidence/TASK-026' / run_id
evidence.mkdir(parents=True)
config_path = ROOT / 'lab/task014/dovecot/local-credentials.json'
config = json.loads(config_path.read_text(encoding='utf-8-sig'))
scope = {'companyId': 'task026-' + uuid.uuid4().hex[:8], 'projectId': 'composite-verification',
         'companyName': 'TASK-026', 'projectName': 'Final verification benchmark'}
token = None
checks = []

def save(name, value):
    (evidence / name).write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')

def check(name, condition):
    checks.append({'name': name, 'pass': bool(condition)})
    if not condition: raise AssertionError(name)

def call(method, path, body=None, timeout=90):
    headers = {'Origin':'http://127.0.0.1:5173','Content-Type':'application/json'}
    if token: headers['X-BitigMail-Session'] = token
    raw = None if body is None else json.dumps(body).encode()
    if method == 'POST' and raw is None: raw = b'{}'
    req = urllib.request.Request('http://127.0.0.1:6175'+path, data=raw, headers=headers, method=method)
    with urllib.request.urlopen(req, timeout=timeout) as response:
        return json.loads(response.read())

def account(role):
    c=config[role]
    return call('POST','/api/accounts',{**scope,'displayName':'TASK026 '+role,'email':c['email'],
        'host':c['imapHost'],'port':c['imapPort'],'tlsMode':'none','username':c['username'],'password':c['password']})

host_pid = None
collector = None
protected = {p: life.get_listening_pid(p) for p in (6174,5173)}
try:
    check('6175 free', life.get_listening_pid(6175) is None)
    check('protected hosts present', all(protected.values()))
    source_before = source_oracle.audit_dovecot_source_invariance(config_path, ROOT/'.codex-coordination/evidence/TASK-019/root-source-before.json')
    check('source 12/4 before', source_before['pass']); save('source-before.json',source_before)
    life.RUN_ID=run_id; life.EVIDENCE_DIR=evidence; life.ROOT=ROOT
    life.DOTNET_EXE=ROOT/'.tools/dotnet/dotnet.exe'
    life.TESTING_HOST_DLL=ROOT/'engine/BitigMail.TestingHost/bin/Release/net8.0-windows/BitigMail.TestingHost.dll'
    life.PID_FILE=ROOT/'runtime/testing-engine/testinghost.pid'
    life.save_json=save; life.check=lambda n,c,d=None: check(n,c)
    proc, host_pid = life.start_testing_host('testinghost.log')
    token = call('POST','/api/session')['token']
    target = account('target')
    call('POST','/api/testing/set-mime-source',{'fixtureId':'corpus-eml'})
    picked = call('POST','/api/picker/mime-source',{'mode':'eml-files'})
    folder = 'TASK026/' + run_id
    request={**scope,'sourceHandle':picked['handle'],'targetAccountId':target['accountId'],
             'selectedFolders':['Corpus'],'targetFolderMappings':{'Corpus':folder}}
    preview=call('POST','/api/transfer/bridge/import/preview',request); save('preview.json',preview)
    check('preview 12',preview['canTransfer'] and preview['eligibleItemsCount']==12)
    started=call('POST','/api/transfer/bridge/import/start',{**scope,'previewId':preview['previewId'],'idempotencyKey':uuid.uuid4().hex})
    collector=PhaseTelemetryCollector(host_pid,evidence/'telemetry.csv',0.1); collector.start(); collector.set_phase('transfer')
    phase_started=None; phase_ended=None; last_phase=None; t0=time.perf_counter()
    while time.perf_counter()-t0 < 180:
        job=call('GET','/api/jobs/'+started['jobId'])
        phase=job.get('progressPhase')
        if phase != last_phase:
            collector.set_phase(phase or 'unknown'); last_phase=phase
        if phase=='verifying' and phase_started is None: phase_started=time.perf_counter()
        if phase_started is not None and phase!='verifying' and phase_ended is None: phase_ended=time.perf_counter()
        if job['status'] in ('completed','failed','cancelled','interrupted','needs-attention'): break
        time.sleep(0.02)
    if phase_started is not None and phase_ended is None: phase_ended=time.perf_counter()
    collector.stop(); telem=telemetry_result(collector); collector=None
    save('job.json',job); save('telemetry-summary.json',telem)
    check('job completed',job['status']=='completed')
    actual=oracle.imap_snapshot(config_path,'target',[folder])
    expected=oracle.files_snapshot(ROOT/'fixtures/mail-corpus-v1/eml','eml')
    comparison=oracle.compare(expected,actual,exact=False,metadata=False)
    save('target.json',actual); save('oracle.json',comparison); check('independent oracle',comparison['pass'])
    source_after=source_oracle.audit_dovecot_source_invariance(config_path, ROOT/'.codex-coordination/evidence/TASK-019/root-source-before.json')
    check('source 12/4 after',source_after['pass']); save('source-after.json',source_after)
    summary={'taskId':'TASK-026','mode':mode,'runId':run_id,'status':'PASS','jobId':started['jobId'],
      'targetFolder':folder,'elapsedSec':time.perf_counter()-t0,
      'verifyingObservedSec':None if phase_started is None else phase_ended-phase_started,
      'verifyingTelemetry':telem['phases'].get('verifying'),'checks':checks}
    save('summary.json',summary); print(json.dumps(summary))
finally:
    if collector: collector.stop()
    if host_pid and life.get_listening_pid(6175)==host_pid: life.kill_and_verify_stopped(host_pid,6175)
    check('6175 off',life.get_listening_pid(6175) is None)
    check('6174 preserved',life.get_listening_pid(6174)==protected[6174])
    check('5173 preserved',life.get_listening_pid(5173)==protected[5173])
