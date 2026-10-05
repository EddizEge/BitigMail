"""Explicitly coordinated TestingHost-only restart/index repair acceptance."""
import importlib.util, json, os, pathlib, socket, subprocess, sys, time, uuid

spec = importlib.util.spec_from_file_location('qa', pathlib.Path(__file__).with_name('archive-api.py'))
qa = importlib.util.module_from_spec(spec); spec.loader.exec_module(qa)
root = qa.ROOT
archive_dir = (root / 'runtime/testing-engine/archives').resolve()
db = archive_dir / 'archive-search.db'
pid_file = root / 'runtime/testing-engine/testinghost.pid'
host_dll = root / 'engine/BitigMail.TestingHost/bin/Release/net8.0-windows/BitigMail.TestingHost.dll'
dotnet = root / '.tools/dotnet/dotnet.exe'
processes = []

def ps_literal(text): return "'" + str(text).replace("'", "''") + "'"

def ps(script):
    res = subprocess.run(['powershell.exe', '-NoProfile', '-NonInteractive', '-Command', script], text=True, capture_output=True, timeout=25, creationflags=subprocess.CREATE_NO_WINDOW)
    if res.returncode: raise RuntimeError(res.stderr[:800])
    return res.stdout.strip()

def live():
    with socket.socket() as connection:
        connection.settimeout(.3)
        return connection.connect_ex(('127.0.0.1', 6175)) == 0

def stop_owned():
    pid = int(pid_file.read_text().strip())
    script = f"$p = Get-CimInstance Win32_Process -Filter 'ProcessId={pid}'; $l = Get-NetTCPConnection -State Listen -LocalPort 6175 -ErrorAction Stop; if ($null -eq $p -or $l.OwningProcess -ne {pid}) {{throw 'Listener ownership mismatch'}}; $p | Select-Object ProcessId,ExecutablePath,CommandLine,@{{Name='CreatedUtc';Expression={{$_.CreationDate.ToUniversalTime().ToString('o')}}}} | ConvertTo-Json -Compress"
    identity = json.loads(ps(script))
    executable = pathlib.Path(identity['ExecutablePath']).resolve()
    command = identity['CommandLine']
    accepted_exe = host_dll.with_suffix('.exe').resolve()
    qa.check('exact owned TestingHost process', executable == accepted_exe or (executable == dotnet.resolve() and str(host_dll).lower() in command.lower()))
    qa.check('PID evidence agrees', identity['ProcessId'] == pid)
    # Fresh exact command comparison and port check in the same stop operation.
    ps(f"$p = Get-CimInstance Win32_Process -Filter 'ProcessId={pid}'; $l = Get-NetTCPConnection -State Listen -LocalPort 6175 -ErrorAction Stop; if ($p.CommandLine -cne {ps_literal(command)} -or $p.CreationDate.ToUniversalTime().ToString('o') -cne {ps_literal(identity['CreatedUtc'])} -or $l.OwningProcess -ne {pid}) {{throw 'Ownership changed'}}; Stop-Process -Id {pid} -Force -ErrorAction Stop")
    for _ in range(100):
        if not live(): break
        time.sleep(.05)
    qa.check('old listener stopped', not live())
    processes.append(identity)
    qa.save('processes.json', processes)
    return pid

def start():
    qa.check('testing listener free before restart', not live())
    log = (qa.RUN / ('host-' + uuid.uuid4().hex[:6] + '.log')).open('wb')
    process = subprocess.Popen([str(dotnet), str(host_dll)], cwd=str(root), stdout=log, stderr=subprocess.STDOUT, creationflags=subprocess.CREATE_NO_WINDOW)
    log.close()
    for _ in range(200):
        if live(): break
        if process.poll() is not None: raise RuntimeError('TestingHost startup failed')
        time.sleep(.1)
    qa.check('new listener running', live())
    qa.check('new PID published', int(pid_file.read_text().strip()) == process.pid)
    qa.token = qa.call('POST', '/api/session', session=False)['token']
    return process.pid

def safe_rename(source, target):
    source, target = source.resolve(), target.resolve()
    qa.check('index paths remain inside owned archive directory', source.parent == archive_dir and target.parent == archive_dir and not target.exists())
    qa.check('index source not reparse point', not (source.stat().st_file_attributes & 0x400))
    source.rename(target)

def managed_hashes(archives):
    import hashlib
    return {str(p.relative_to(archive_dir)): hashlib.sha256(p.read_bytes()).hexdigest() for a in archives for p in (archive_dir / a['archiveId']).rglob('*') if p.is_file()}

def main():
    report = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding='utf-8-sig'))
    base_accepted = report['completed'] and all(c['pass'] for c in report['checks'])
    diagnostic = '--diagnostic' in sys.argv
    qa.save('run-mode.json', {'diagnosticOnly': diagnostic, 'baseApiAccepted': base_accepted})
    qa.check('accepted base or explicitly separate diagnostic run', base_accepted or diagnostic)
    archives = report['archives']
    qa.archives = archives
    total = sum(a['totalItems'] for a in archives)
    qa.token = qa.call('POST', '/api/session', session=False)['token']
    initial = qa.search(archives, total)
    stable_ids = {(i['archiveId'],i['messageId']) for i in initial['items']}
    raw_before = managed_hashes(archives)
    old_pid = stop_owned(); new_pid = start()
    qa.check('actual new process after restart', old_pid != new_pid)
    after_restart = qa.search(archives, total)
    qa.check('physical public IDs stable after actual restart',stable_ids=={(i['archiveId'],i['messageId']) for i in after_restart['items']})
    for mode in ('missing', 'corrupt'):
        stop_owned()
        backup_tag = '.root-task018-' + mode + '-' + uuid.uuid4().hex[:8]
        originals = []
        for suffix in ('', '-wal', '-shm'):
            source = pathlib.Path(str(db) + suffix)
            if source.exists():
                target = pathlib.Path(str(source) + backup_tag)
                safe_rename(source, target); originals.append((source, target))
        qa.check('existing database preserved', any(s == db for s, _ in originals))
        if mode == 'corrupt': db.write_bytes(b'TASK018 deliberate synthetic index corruption\x00')
        try:
            start()
            catalog = qa.call('GET', '/api/archive/catalog')
            qa.save(mode + '-catalog.json', catalog)
            qa.check('all managed owners visible despite ' + mode + ' index', all(any(i['archiveId'] == a['archiveId'] and i['companyId'] == a['companyId'] and i['projectId'] == a['projectId'] for i in catalog) for a in archives))
            response = qa.call('POST', '/api/archive/search', qa.request(archives), expected=(200, 400, 409, 500, 503))
            qa.save(mode + '-unrepaired-search.json', response)
            qa.check('no silent healthy empty search after ' + mode, not ('totalCount' in response and response['totalCount'] == 0 and response.get('indexHealthy', True)))
            qa.call('POST', '/api/archive/reindex-all')
            after_rebuild = qa.search(archives, total)
            qa.check('physical public IDs stable after '+mode+' index rebuild',stable_ids=={(i['archiveId'],i['messageId']) for i in after_rebuild['items']})
            qa.check('managed MIME and manifests unchanged', managed_hashes(archives) == raw_before)
        except BaseException:
            if live(): stop_owned()
            for suffix in ('', '-wal', '-shm'):
                source = pathlib.Path(str(db) + suffix)
                if source.exists(): safe_rename(source, pathlib.Path(str(source) + '.failed-' + uuid.uuid4().hex[:8]))
            for source, backup in originals:
                safe_rename(backup, source)
            start()
            raise
    qa.save('raw-hashes.json', raw_before)

if __name__ == '__main__':
    finished = False
    try:
        main(); finished = True
    finally:
        qa.save('recovery-report.json', {'completed': finished, 'checks': qa.checks, 'archives': qa.archives})
        print(json.dumps({'completed': finished, 'passed': sum(c['pass'] for c in qa.checks), 'failed': sum(not c['pass'] for c in qa.checks), 'evidence': str(qa.RUN)}))
