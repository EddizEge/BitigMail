import {expect,test} from '@playwright/test';
import {spawn,ChildProcessWithoutNullStreams} from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

test('TASK-038 production first-run, login and admin catalog UI',async({page})=>{
 const capture=process.env.BITIGMAIL_TASK043_CAPTURE;const browserErrors:string[]=[];
 if(capture){const [width,height]=capture.split('x').map(Number);await page.setViewportSize({width,height});page.on('console',message=>{if(message.type()==='error')browserErrors.push(message.text());});page.on('pageerror',error=>browserErrors.push(error.message));}
 const root=path.resolve('..');const runtime=path.join(root,'runtime','testing-engine-secure');if(path.relative(root,path.resolve(runtime)).split('\\').join('/')!=='runtime/testing-engine-secure')throw new Error('Unexpected test directory');fs.rmSync(runtime,{recursive:true,force:true});
 const proof=crypto.randomBytes(32);const dotnet=path.join(root,'.tools','dotnet','dotnet.exe');const dll=path.join(root,'engine','BitigMail.TestingHost','bin','Release','net8.0-windows','BitigMail.TestingHost.dll');
 const child=spawn(dotnet,[dll],{cwd:root,env:{...process.env,BITIGMAIL_TEST_PRODUCTION_SECURITY:'1'},windowsHide:true,stdio:['pipe','pipe','pipe']}) as ChildProcessWithoutNullStreams;child.stdin.write(proof);child.stdin.end();
 try{
  await expect.poll(async()=>fetch('http://127.0.0.1:6175/api/setup/status',{headers:{Host:'127.0.0.1:6175'}}).then(r=>r.status).catch(()=>0),{timeout:20_000}).toBe(200);
  await page.addInitScript(value=>{(window as any).__BITIGMAIL_ENGINE_URL__='http://127.0.0.1:6175';(window as any).__BITIGMAIL_SETUP_PROOF__=value;},proof.toString('hex'));
  await page.goto('/');await expect(page.getByTestId('identity-gate')).toContainText('İlk yönetici kurulumu');await page.getByLabel('Kullanıcı adı').fill('local-admin');await page.getByLabel('Parola').fill('correct horse battery');await page.getByRole('button',{name:'Yönetici oluştur'}).click();
  await expect(page.getByTestId('header-account-control')).toBeVisible();await page.getByTestId('header-account-control').click();await expect(page.getByTestId('identity-management-drawer')).toBeVisible();await page.getByLabel('Müşteri adı').fill('Acme');await page.getByLabel('İlk proje').fill('Migration');await page.getByRole('button',{name:'Çalışma alanı oluştur'}).click();
  await page.getByLabel('Proje eklenecek müşteri').selectOption({label:'Acme'});await page.getByLabel('Yeni proje adı').fill('Phase Two');await page.getByRole('button',{name:'Projeyi ekle'}).click();await expect(page.getByText(/Migration · Phase Two/)).toBeVisible();
  if(capture){const evidence=path.join(process.env.TEMP||root,'BitigMail-TASK043');fs.mkdirSync(evidence,{recursive:true});await page.screenshot({path:path.join(evidence,`management-${capture}.png`),fullPage:true});await page.getByRole('button',{name:'Yönetimi kapat'}).click();await page.getByTestId('nav-tab-transfers').click();await page.screenshot({path:path.join(evidence,`transfer-${capture}.png`),fullPage:true});const layout=await page.evaluate(()=>({viewport:{width:innerWidth,height:innerHeight},document:{clientWidth:document.documentElement.clientWidth,scrollWidth:document.documentElement.scrollWidth},body:{clientWidth:document.body.clientWidth,scrollWidth:document.body.scrollWidth},nav:[...document.querySelectorAll<HTMLElement>('[data-testid^="nav-tab-"]')].map(element=>({name:element.textContent?.trim(),rect:element.getBoundingClientRect().toJSON()})),account:document.querySelector<HTMLElement>('[data-testid="header-account-control"]')?.getBoundingClientRect().toJSON()}));fs.writeFileSync(path.join(evidence,`layout-${capture}.json`),JSON.stringify({...layout,browserErrors},null,2));await page.getByTestId('header-account-control').click();}
  await page.getByLabel('Kullanıcı adı',{exact:true}).fill('operator');await page.getByLabel('Geçici parola',{exact:true}).fill('operator secure password');await page.getByLabel('Rol',{exact:true}).selectOption('Operator');await page.getByLabel('Müşteri',{exact:true}).selectOption({label:'Acme'});await page.getByLabel('Proje',{exact:true}).selectOption({label:'Migration'});await page.getByRole('button',{name:'Kullanıcı oluştur'}).click();
  await page.getByRole('button',{name:'Bu cihazda oturumu kapat'}).click();await expect(page.getByTestId('identity-gate')).toContainText('BitigMail oturumu');await page.getByLabel('Kullanıcı adı').fill('operator');await page.getByLabel('Parola').fill('operator secure password');await page.getByRole('button',{name:'Oturum aç'}).click();await expect(page.getByTestId('header-account-control')).toBeVisible();await expect(page.getByTestId('app-shell')).toBeVisible();await page.getByTestId('nav-tab-clients').click();await page.locator('[data-testid^="company-card-"]').click();await page.locator('[data-testid^="add-account-btn-"]').click();await page.getByTestId('create-account-name-input').fill('Yetkili IMAP');await page.getByTestId('create-account-email-input').fill('operator@example.test');await page.getByTestId('create-account-host-input').fill('imap.example.test');await page.getByTestId('create-account-username-input').fill('operator@example.test');await page.getByTestId('create-account-password-input').fill('synthetic-password');await page.getByTestId('submit-new-account-btn').click();await expect(page.getByText('Yetkili IMAP')).toBeVisible();await page.getByTestId('header-account-control').click();await expect(page.getByText('Yeni çalışma alanı')).toHaveCount(0);await page.getByRole('button',{name:'Bu cihazda oturumu kapat'}).click();await expect(page.getByTestId('identity-gate')).toContainText('BitigMail oturumu');
 }finally{if(child.exitCode===null){const exited=new Promise(resolve=>child.once('exit',resolve));child.kill();await exited;}}
});

test('TASK-038 secure HTTP ownership matrix',async({request},testInfo)=>{
 test.skip(testInfo.project.name!=='desktop-reference','Tek secure host matrisi yeterlidir.');
 const root=path.resolve('..');const runtime=path.join(root,'runtime','testing-engine-secure');if(path.relative(root,path.resolve(runtime)).split('\\').join('/')!=='runtime/testing-engine-secure')throw new Error('Unexpected test directory');fs.rmSync(runtime,{recursive:true,force:true});
 const proof=crypto.randomBytes(32);const child=spawn(path.join(root,'.tools','dotnet','dotnet.exe'),[path.join(root,'engine','BitigMail.TestingHost','bin','Release','net8.0-windows','BitigMail.TestingHost.dll')],{cwd:root,env:{...process.env,BITIGMAIL_TEST_PRODUCTION_SECURITY:'1',BITIGMAIL_TEST_SECURE_MIME_FIXTURE:'corpus-eml'},windowsHide:true,stdio:['pipe','pipe','pipe']}) as ChildProcessWithoutNullStreams;child.stdin.end(proof);
 const base='http://127.0.0.1:6175',headers=(token?:string)=>({Host:'127.0.0.1:6175',Origin:'http://127.0.0.1:5173','Content-Type':'application/json',...(token?{'X-BitigMail-Session':token}:{})});
 try{
  await expect.poll(async()=>fetch(base+'/api/setup/status',{headers:{Host:'127.0.0.1:6175'}}).then(r=>r.status).catch(()=>0),{timeout:20_000}).toBe(200);
  const setup=await request.post(base+'/api/setup/first-admin',{headers:headers(),data:{userName:'admin',password:'correct horse battery',proof:proof.toString('hex')}});const adminToken=(await setup.json()).token;
  const company=await (await request.post(base+'/api/catalog/companies',{headers:headers(adminToken),data:{name:'Acme',projectName:'Migration'}})).json();
  await request.post(base+'/api/admin/users',{headers:headers(adminToken),data:{userName:'operator',password:'operator secure password',role:'Operator',grants:[{companyId:company.companyId,projectIds:[company.projects[0].projectId]}]}});
  const operatorToken=(await (await request.post(base+'/api/auth/login',{headers:headers(),data:{userName:'operator',password:'operator secure password'}})).json()).token;
  const newAdminToken=(await (await request.post(base+'/api/auth/login',{headers:headers(),data:{userName:'admin',password:'correct horse battery'}})).json()).token;
  const picked=await (await request.post(base+'/api/picker/mime-source',{headers:headers(adminToken),data:{mode:'eml-files'}})).json();const sourceHandle=picked.handle;
  expect((await request.post(base+'/api/mime/source/analyze',{headers:headers(adminToken),data:{sourceHandle}})).status()).toBe(200);
  expect((await request.post(base+'/api/mime/source/analyze',{headers:headers(operatorToken),data:{sourceHandle}})).status()).toBe(404);
  expect((await request.post(base+'/api/mime/source/analyze',{headers:headers(newAdminToken),data:{sourceHandle}})).status()).toBe(404);
  const preview=await request.post(base+'/api/mime/selection/preview',{headers:headers(adminToken),data:{sourceHandle,folderIds:[]}});expect(preview.status()).toBe(200);const selectionId=(await preview.json()).selectionId;expect(selectionId).toBeTruthy();
  for(const foreignToken of [operatorToken,newAdminToken]){const foreignSource=(await (await request.post(base+'/api/picker/mime-source',{headers:headers(foreignToken),data:{mode:'eml-files'}})).json()).handle;expect((await request.post(base+'/api/mime/source/analyze',{headers:headers(foreignToken),data:{sourceHandle:foreignSource}})).status()).toBe(200);const foreignTarget=(await (await request.post(base+'/api/picker/target',{headers:headers(foreignToken),data:{sourceHandle:foreignSource}})).json()).handle;expect((await request.post(base+'/api/mime/start',{headers:headers(foreignToken),data:{sourceHandle:foreignSource,targetHandle:foreignTarget,selectionId,clientContext:{companyId:company.companyId,projectId:company.projects[0].projectId}}})).status()).toBe(404);}
  expect((await request.get(base+'/api/admin/users',{headers:{Host:'127.0.0.1:6175','X-BitigMail-Session':operatorToken}})).status()).toBe(404);
  const other=await (await request.post(base+'/api/catalog/companies',{headers:headers(adminToken),data:{name:'Other',projectName:'Secret'}})).json();
  const account=await (await request.post(base+'/api/accounts',{headers:headers(adminToken),data:{companyId:other.companyId,projectId:other.projects[0].projectId,displayName:'Other mailbox',email:'other@example.test',host:'imap.example.test',port:993,tlsMode:'ssl',username:'other',password:'not-used-in-test'}})).json();
  expect((await request.get(base+`/api/accounts/${account.accountId}?companyId=${company.companyId}&projectId=${company.projects[0].projectId}`,{headers:{Host:'127.0.0.1:6175','X-BitigMail-Session':operatorToken}})).status()).toBe(404);
  const archives=[];
  for(const owner of [company,other]){
   const previewResponse=await request.post(base+'/api/archive/ingest/preview',{headers:headers(adminToken),data:{sourceHandle,archiveName:'Security fixture '+owner.name,companyId:owner.companyId,projectId:owner.projects[0].projectId}});
   expect(previewResponse.status(),await previewResponse.text()).toBe(200);const archivePreview=await previewResponse.json();
   for(const foreignToken of [operatorToken,newAdminToken]){
    expect((await request.get(base+'/api/archive/ingest/preview/'+archivePreview.previewId,{headers:headers(foreignToken)})).status()).toBe(404);
    expect((await request.post(base+'/api/archive/ingest/start',{headers:headers(foreignToken),data:{previewId:archivePreview.previewId,idempotencyKey:crypto.randomUUID()}})).status()).toBe(404);
   }
   const start=await request.post(base+'/api/archive/ingest/start',{headers:headers(adminToken),data:{previewId:archivePreview.previewId,idempotencyKey:crypto.randomUUID()}});
   expect(start.status(),await start.text()).toBe(200);const job=await start.json();
   await expect.poll(async()=>{const response=await request.get(base+'/api/jobs/'+job.jobId,{headers:headers(adminToken)});return (await response.json()).status;},{timeout:20_000}).toBe('completed');
   archives.push({companyId:owner.companyId,projectId:owner.projects[0].projectId,archiveId:job.archiveId});
  }
  const own=await request.post(base+'/api/archive/search',{headers:headers(operatorToken),data:{selectedScopes:[archives[0]],page:1,pageSize:20}});
  expect(own.status(),await own.text()).toBe(200);expect((await own.json()).totalCount).toBeGreaterThan(0);
  const mixed=await request.post(base+'/api/archive/search',{headers:headers(operatorToken),data:{selectedScopes:archives,page:1,pageSize:20}});
  expect(mixed.status()).toBe(404);expect(await mixed.text()).not.toContain('Security fixture');
  const substituted=await request.post(base+'/api/archive/search',{headers:headers(operatorToken),data:{selectedScopes:[{...archives[0],archiveId:archives[1].archiveId}],page:1,pageSize:20}});
  expect(substituted.status()).toBe(404);
  const catalog=await request.get(base+'/api/archive/catalog',{headers:headers(operatorToken)});expect(catalog.status()).toBe(200);
  const visible=await catalog.json();expect(visible.map((a:any)=>a.archiveId)).toEqual([archives[0].archiveId]);

 }finally{if(child.exitCode===null){const exited=new Promise(resolve=>child.once('exit',resolve));child.kill();await exited;}}
});
