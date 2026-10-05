import fs from 'node:fs';
const headers={Origin:'http://127.0.0.1:5173','Content-Type':'application/json'};
async function api(method,path,data){const r=await fetch('http://127.0.0.1:6175'+path,{method,headers,body:method==='POST'?JSON.stringify(data??{}):undefined});if(!r.ok)throw Error(path+' '+r.status+' '+await r.text());return r.json()}
headers['X-BitigMail-Session']=(await api('POST','/api/session')).token;
await api('POST','/api/testing/set-mime-source',{fixtureId:'corpus-tree'});
const picked=await api('POST','/api/picker/mime-source',{mode:'eml-tree'});
const scope={companyId:'comp-ornek',projectId:'proj-ornek-gecis'};const prefix='TASK017-TASK021-'+Date.now();
const mappings=Object.fromEntries(['Gelen Kutusu','Gönderilenler','Projeler/İstanbul'].map((f,i)=>[f,prefix+'-'+i]));
const prev=await api('POST','/api/transfer/bridge/import/preview',{...scope,sourceHandle:picked.handle,targetAccountId:'acc_9d9f72fb33984e3dbf6986866532d4ba',selectedFolders:Object.keys(mappings),targetFolderMappings:mappings});
if(!prev.canTransfer)throw Error('Preview not ready');
await api('POST','/api/testing/bridge-fault',{fault:'LostResponseAfterAppend',targetOrdinal:2});
const job=await api('POST','/api/transfer/bridge/import/start',{...scope,previewId:prev.previewId,idempotencyKey:crypto.randomUUID()});
let current;for(let i=0;i<120;i++){current=await api('GET','/api/jobs/'+job.jobId);if(current.status==='interrupted')break;await new Promise(r=>setTimeout(r,250))}
if(current.status!=='interrupted')throw Error('Not interrupted');
await api('POST','/api/testing/bridge-fault/reset');
fs.mkdirSync('.codex-coordination/evidence/TASK-021',{recursive:true});fs.writeFileSync('.codex-coordination/evidence/TASK-021/recovery-job.json',JSON.stringify({job:current,mappings,scope},null,2));console.log(JSON.stringify({jobId:job.jobId,status:current.status,items:current.itemsWritten}));