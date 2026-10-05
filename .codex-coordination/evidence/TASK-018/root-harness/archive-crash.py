"""Real mid-ingest termination, pinned to a verified TestingHost process HANDLE."""
import ctypes, ctypes.wintypes as wt, hashlib, importlib.util, json, pathlib, threading, time, uuid, sys
spec=importlib.util.spec_from_file_location('recovery',pathlib.Path(__file__).with_name('archive-recovery.py'))
r=importlib.util.module_from_spec(spec);spec.loader.exec_module(r)
qa=r.qa
kernel=ctypes.WinDLL('kernel32',use_last_error=True)
kernel.OpenProcess.argtypes=[wt.DWORD,wt.BOOL,wt.DWORD];kernel.OpenProcess.restype=wt.HANDLE
kernel.GetProcessId.argtypes=[wt.HANDLE];kernel.GetProcessId.restype=wt.DWORD
kernel.TerminateProcess.argtypes=[wt.HANDLE,wt.UINT];kernel.TerminateProcess.restype=wt.BOOL
kernel.WaitForSingleObject.argtypes=[wt.HANDLE,wt.DWORD];kernel.WaitForSingleObject.restype=wt.DWORD
kernel.CloseHandle.argtypes=[wt.HANDLE];kernel.CloseHandle.restype=wt.BOOL
kernel.CreateFileW.argtypes=[wt.LPCWSTR,wt.DWORD,wt.DWORD,ctypes.c_void_p,wt.DWORD,wt.DWORD,wt.HANDLE];kernel.CreateFileW.restype=wt.HANDLE
kernel.GetFileSizeEx.argtypes=[wt.HANDLE,ctypes.POINTER(ctypes.c_longlong)];kernel.GetFileSizeEx.restype=wt.BOOL
kernel.ReadFile.argtypes=[wt.HANDLE,ctypes.c_void_p,wt.DWORD,ctypes.POINTER(wt.DWORD),ctypes.c_void_p];kernel.ReadFile.restype=wt.BOOL

def read_shared_job(path):
    # Diagnostic reads must allow Windows atomic job-file replacement.
    h=kernel.CreateFileW(str(path),0x80000000,7,None,3,128,None)
    if h==ctypes.c_void_p(-1).value: raise ctypes.WinError(ctypes.get_last_error())
    try:
        size=ctypes.c_longlong()
        if not kernel.GetFileSizeEx(h,ctypes.byref(size)): raise ctypes.WinError(ctypes.get_last_error())
        if size.value<1 or size.value>4*1024*1024: raise ValueError('Unexpected job record size')
        buffer=ctypes.create_string_buffer(size.value);read=wt.DWORD()
        if not kernel.ReadFile(h,buffer,size.value,ctypes.byref(read),None): raise ctypes.WinError(ctypes.get_last_error())
        return json.loads(buffer.raw[:read.value].decode('utf-8-sig'))
    finally: kernel.CloseHandle(h)

def identity():
    pid=int(r.pid_file.read_text().strip())
    data=json.loads(r.ps(f"$p=Get-CimInstance Win32_Process -Filter 'ProcessId={pid}'; $l=Get-NetTCPConnection -State Listen -LocalPort6175 -ErrorAction Stop; if ($null -eq $p -or $l.OwningProcess -ne {pid}) {{throw 'PID/listener mismatch'}}; $p | Select-Object ProcessId,ExecutablePath,CommandLine,@{{Name='CreatedUtc';Expression={{$_.CreationDate.ToUniversalTime().ToString('o')}}}} | ConvertTo-Json -Compress".replace('-LocalPort6175','-LocalPort 6175')))
    exe=pathlib.Path(data['ExecutablePath']).resolve()
    qa.check('exact TestingHost executable and command',exe==r.host_dll.with_suffix('.exe').resolve() or (exe==r.dotnet.resolve() and str(r.host_dll).lower() in data['CommandLine'].lower()))
    return data

def main():
    mbox='--mbox' in sys.argv
    total=12 if mbox else 72
    qa.token=qa.call('POST','/api/session',session=False)['token']
    qa.call('POST','/api/testing/set-mime-source',{'fixtureId':'corpus-mbox' if mbox else 'bridge-over50'})
    picked=qa.call('POST','/api/picker/archive-source',{'mode':'mbox' if mbox else 'eml-tree'})
    owner={'companyId':'root-archive-crash-'+uuid.uuid4().hex[:8],'projectId':'partial-ingest','companyName':'Kesinti Laboratuvarı','projectName':'Gerçek süreç kesintisi'}
    preview=qa.call('POST','/api/archive/ingest/preview',{**owner,'archiveName':str(total)+' ileti gerçek kesinti '+('MBOX' if mbox else 'EML'),'sourceHandle':picked['handle']})
    qa.check('closed synthetic fixture count',preview['canIngest'] and preview['totalItems']==total)
    plan=json.loads((qa.ROOT/'runtime/testing-engine/archive-plans'/ (preview['previewId']+'.json')).read_text(encoding='utf-8-sig'))
    qa.check('plan exact owner',plan['companyId']==owner['companyId'] and plan['previewId']==preview['previewId'])
    def source_raw():
        return [raw for _,raw in qa.oracle.mbox_records(pathlib.Path(plan['sourceRootPath']))] if mbox else [pathlib.Path(i['sourcePath']).read_bytes() for i in plan['items']]
    raw=source_raw()
    before=[hashlib.sha256(b).hexdigest() for b in raw]
    qa.check('every frozen source hash verified before test',before==[i['sourceSha256'] for i in plan['items']])
    ident=identity();handle=kernel.OpenProcess(0x101001,False,ident['ProcessId'])
    if not handle: raise ctypes.WinError(ctypes.get_last_error())
    first=threading.Event();holder={};events=[];errors=[]
    try:
        qa.check('stable process identity before arming',kernel.GetProcessId(handle)==ident['ProcessId'] and identity()==ident)
        qa.save('owned-process.json',ident)
        def watcher():
            try:
                if not first.wait(20): raise RuntimeError('Start response not received; no process killed')
                job_path=qa.ROOT/'runtime/testing-engine/jobs'/(holder['job']['jobId']+'.json')
                deadline=time.monotonic()+20
                while time.monotonic()<deadline:
                    try: state=read_shared_job(job_path)
                    except (OSError,ValueError): time.sleep(.001);continue
                    written=state.get('itemsWritten',0)
                    if state['status']=='converting' and 2<=written<total:
                        if state['archiveId']!=plan['archiveId'] or state['jobId']!=holder['job']['jobId']: raise RuntimeError('Job identity changed')
                        if not kernel.TerminateProcess(handle,137): raise ctypes.WinError(ctypes.get_last_error())
                        events.append({'action':'actual TerminateProcess on verified lifetime-bound handle','savedStateBeforeKill':state,'observedWritten':written})
                        return
                    if state['status'] in ('completed','failed'): raise RuntimeError('Job finished before interruption; no crash acceptance')
                    time.sleep(.001)
                raise RuntimeError('Partial progress not observed; no process killed')
            except BaseException as error: errors.append(str(error))
        thread=threading.Thread(target=watcher,daemon=True);thread.start()
        holder['job']=qa.call('POST','/api/archive/ingest/start',{'previewId':preview['previewId'],'idempotencyKey':uuid.uuid4().hex})
        first.set();thread.join(25)
        qa.save('crash-events.json',{'events':events,'errors':errors,'startResponse':holder.get('job'),'observer':'Win32 read share read+write+delete; does not block atomic record replacement'})
        qa.check('actual mid-job termination occurred',len(events)==1 and not errors and not thread.is_alive())
        qa.check('same process handle exited',kernel.WaitForSingleObject(handle,5000)==0)
    finally: kernel.CloseHandle(handle)
    for _ in range(100):
        if not r.live(): break
        time.sleep(.05)
    qa.check('old testing listener closed',not r.live())
    staging=r.archive_dir/'.staging'/plan['archiveId']
    partial=list((staging/'messages').glob('*.eml'))
    qa.check('real partial staging retained',1<=len(partial)<total)
    qa.check('partial archive not published',(r.archive_dir/plan['archiveId']/'manifest.json').exists() is False)
    qa.save('partial-staging.json',{'files':[{'name':p.name,'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in partial]})
    r.start()
    current=qa.call('GET','/api/jobs/'+holder['job']['jobId'])
    qa.check('startup marks real interrupted job',current['status']=='interrupted')
    resumed=qa.call('POST','/api/archive/ingest/resume',{'jobId':current['jobId']})
    qa.check('resume uses same job and archive',resumed['jobId']==current['jobId'] and resumed['archiveId']==plan['archiveId'])
    done=qa.wait_job(resumed)
    archive=next(a for a in qa.call('GET','/api/archive/catalog') if a['archiveId']==plan['archiveId'])
    qa.archives=[archive];qa.verify_raw(archive,raw);qa.search([archive],total)
    qa.check('no original fixture changes',[hashlib.sha256(b).hexdigest() for b in source_raw()]==before)
    qa.save('job-completed.json',done)

completed=False
try:
    main();completed=True
finally:
    qa.save('crash-report.json',{'completed':completed,'checks':qa.checks,'archives':qa.archives})
    print(json.dumps({'completed':completed,'passed':sum(c['pass'] for c in qa.checks),'failed':sum(not c['pass'] for c in qa.checks),'evidence':str(qa.RUN)}))
