"""Root independent read-only scale acceptance, not production implementation.
No writes to mailbox/source/output. Only reports and ephemeral HTTP session creation.
"""
import argparse, collections, email.policy, email.parser, hashlib, json, pathlib
import sys, time, unicodedata, urllib.request, uuid, re

ROOT = pathlib.Path(r'C:\Users\Eddiz\Documents\ChatGPT\Mail Manager')
sys.path.insert(0, str(ROOT / 'lab/task017'))
import verify_bridge as oracle

parser = argparse.ArgumentParser()
parser.add_argument('--stage', choices=['128m', '1g'], required=True)
parser.add_argument('--export-job', action='append', required=True)
parser.add_argument('--archive-id', action='append', default=[])
args = parser.parse_args()
OUT = ROOT / '.codex-coordination/evidence/TASK-019' / ('root-audit-' + uuid.uuid4().hex[:10])
OUT.mkdir(parents=True)
checks, exports, archives, timings = [], [], [], []
headers = {'Origin': 'http://127.0.0.1:5173', 'Content-Type': 'application/json'}

def save(name, value):
    (OUT / name).write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')

def check(name, ok):
    checks.append({'name': name, 'pass': bool(ok)})
    if not ok: raise AssertionError(name)

def api(method, route, payload=None):
    data = json.dumps(payload or {}).encode() if method == 'POST' else None
    req = urllib.request.Request('http://127.0.0.1:6175' + route, data=data, headers=headers, method=method)
    start = time.perf_counter()
    with urllib.request.urlopen(req, timeout=120) as res:
        obj = json.load(res)
        check(route + ' no-store', 'no-store' in res.headers.get('Cache-Control', ''))
    timings.append({'route': route, 'httpAndJsonMs': (time.perf_counter()-start)*1000})
    return obj

def raw_counter(snapshot):
    return collections.Counter((m['rawSha256'], m['rawBytes']) for m in snapshot['messages'])

def norm(text):
    return unicodedata.normalize('NFC', text).replace('I', 'ı').replace('İ', 'i').lower().replace('ı', 'i')

completed = False
try:
    headers['X-BitigMail-Session'] = api('POST', '/api/session')['token']
    source_path = ROOT / 'runtime/task019' / ('stage-' + args.stage) / 'source/eml-tree'
    source = oracle.files_snapshot(source_path, 'eml')
    save('independent-source.json', source)
    size = sum(m['rawBytes'] for m in source['messages'])
    check('at least1024 physical source messages', source['count'] >= 1024)
    target_bytes = (128 if args.stage == '128m' else 1024) * 1024 * 1024
    check('source size within2percent target', abs(size-target_bytes) <= target_bytes*.02)
    for job_id in args.export_job:
        job = api('GET', '/api/jobs/' + job_id)
        check('completed owned export ' + job_id, job['status']=='completed' and job['jobKind']=='bridge-export'
              and job['clientContext']['companyId']=='company-task019-scale')
        report = api('GET', '/api/jobs/' + job_id + '/report')
        output = pathlib.Path(report['bridgeTransfer']['outputPath']).resolve(strict=True)
        manifest = json.loads((output/'manifest.json').read_text(encoding='utf-8-sig'))
        folders = [f['originalFolder'] for f in manifest['folders']]
        check('only owned target folders', bool(folders) and all(f.startswith('Task019.') for f in folders))
        mailbox = oracle.imap_snapshot(ROOT/'lab/task014/dovecot/local-credentials.json', 'target', folders)
        actual = oracle.files_snapshot(output, 'eml' if manifest['targetFormat']=='eml-tree' else 'mbox')
        source_to_imap = oracle.compare(source, mailbox, exact=False, metadata=False)
        imap_to_file = oracle.compare(mailbox, actual, exact=True, metadata=False)
        sidecar = oracle.verify_manifest(mailbox, manifest, output)
        check('source to IMAP canonical/header/MIME physical multiset', source_to_imap['pass'])
        check('IMAP to export exact raw/header/MIME physical multiset', imap_to_file['pass'])
        check('export folder/date/flags/UID sidecar', sidecar['pass'])
        check('target not deleted', all('\\Deleted' not in m['flags'] for m in mailbox['messages']))
        save(job_id+'-mailbox.json', mailbox)
        save(job_id+'-files.json', actual)
        save(job_id+'-sidecar.json', sidecar)
        exports.append({'jobId': job_id, 'format': manifest['targetFormat'], 'output': str(output),
                        'sourceToImap': source_to_imap, 'imapToFile': imap_to_file, 'sidecarPass': sidecar['pass']})

    catalog = api('GET', '/api/archive/catalog')
    all_scopes, all_expected_ids = [], set()
    for archive_id in args.archive_id:
        item = next(a for a in catalog if a['archiveId']==archive_id)
        check('owned ready archive', item['companyId']=='company-task019-scale' and item['status']=='ready')
        directory = ROOT / 'runtime/testing-engine/archives' / archive_id
        manifest = json.loads((directory/'manifest.json').read_text(encoding='utf-8-sig'))
        observed, subjects = collections.Counter(), {}
        for record in manifest['items']:
            file = (directory/record['relativeEmlPath']).resolve(strict=True)
            check('contained managed file', file.is_relative_to(directory.resolve()) and not file.is_symlink())
            raw = file.read_bytes()
            sha = hashlib.sha256(raw).hexdigest()
            check('managed physical bytes match manifest', sha==record['storedSha256'].lower() and len(raw)==record['byteLength'])
            observed[(sha,len(raw))] += 1
            mime = email.parser.BytesParser(policy=email.policy.default).parsebytes(raw, headersonly=True)
            subjects[archive_id+':'+record['itemId']] = str(mime.get('Subject',''))
        # Archives created from exports must match one independently read export byte multiset.
        export_snapshots = [json.loads((OUT/(e['jobId']+'-files.json')).read_text(encoding='utf-8')) for e in exports]
        check('archive exact physical export multiset', any(observed==raw_counter(s) for s in export_snapshots))
        scope = {k:item[k] for k in ('companyId','projectId','archiveId')}
        all_scopes.append(scope)
        all_expected_ids.update(subjects)
        request = {'selectedScopes':[scope], 'query':'', 'field':'subject','page':1,'pageSize':100}
        got = []
        while True:
            result = api('POST','/api/archive/search',request)
            check('archive exact total', result['totalCount']==len(subjects))
            for row in result['items']:
                check('row identity and decoded subject', row['messageId'] in subjects and row['subject']==subjects[row['messageId']])
                got.append(row['messageId'])
            if len(got)>=result['totalCount']: break
            check('pagination progresses', bool(result['items']))
            request['page'] += 1
        check('no missing/extra/duplicate search identities', len(got)==len(set(got)) and set(got)==set(subjects))
        # Independent header-derived token oracle, positive and negative cases.
        for query in ['istanbul','iğdir','rootnomatch019cafebabe']:
            expected = {k for k,v in subjects.items() if query in re.findall(r'[^\W_]+', norm(v))}
            q = {**request,'query':query,'page':1}
            result = api('POST','/api/archive/search',q)
            check('subject token count '+query, result['totalCount']==len(expected))
            check('subject query identity membership '+query, all(r['messageId'] in expected for r in result['items']))
        # Repeat same count query to separately observe warm HTTP+JSON latency.
        for _ in range(3): api('POST','/api/archive/search',{**request,'page':1})
        archives.append({'archiveId':archive_id,'physicalMessages':len(subjects),'rawBytes':sum(k[1]*n for k,n in observed.items())})
    if len(all_scopes)>1:
        got, page = [], 1
        while True:
            res = api('POST','/api/archive/search',{'selectedScopes':all_scopes,'page':page,'pageSize':100})
            check('combined scope total',res['totalCount']==len(all_expected_ids))
            got.extend(r['messageId'] for r in res['items'])
            if len(got)>=res['totalCount']: break
            check('combined pagination progress',bool(res['items']))
            page += 1
        check('combined unique physical identities',len(got)==len(set(got)) and set(got)==all_expected_ids)
    source_after = oracle.files_snapshot(source_path,'eml')
    check('source immutable throughout read-only audit',oracle.compare(source,source_after,exact=True,metadata=False)['pass'])
    original_after = oracle.imap_snapshot(ROOT/'lab/task014/dovecot/local-credentials.json','source',['INBOX','Gönderilenler','Projeler/İstanbul'])
    original_before = json.loads((ROOT/'.codex-coordination/evidence/TASK-019/root-source-before.json').read_text(encoding='utf-8'))
    unchanged = oracle.compare(original_before,original_after,exact=True,metadata=True)
    check('original source12/4 unchanged',unchanged['pass'] and original_after['count']==12 and original_after['attachments']==4)
    save('original-source-after.json',original_after)
    save('original-source-unchanged.json',unchanged)
    completed = True
finally:
    save('report.json',{'completed':completed,'stage':args.stage,'checks':checks,'exports':exports,'archives':archives,'timings':timings})
    print(json.dumps({'completed':completed,'output':str(OUT),'checks':len(checks),'failed':sum(not c['pass'] for c in checks)}))
