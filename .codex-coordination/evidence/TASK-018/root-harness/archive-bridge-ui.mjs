import {createRequire} from 'node:module';
import fs from 'node:fs';
import path from 'node:path';
const root='C:/Users/Eddiz/Documents/ChatGPT/Mail Manager';
const require=createRequire(path.join(root,'prototype/package.json'));
const {chromium,expect}=require('@playwright/test');
const output=path.join('C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task017-qa','archive-bridge-ui-'+Date.now());
fs.mkdirSync(output,{recursive:true});
const browser=await chromium.launch({headless:true});
const results=[];
let completed=false;
try {
 for(const width of [1660,390]) {
  const context=await browser.newContext({viewport:{width,height:width===390?844:948}});
  const page=await context.newPage(),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  page.on('console',e=>{if(['warning','error'].includes(e.type())) errors.push(e.text());});
  await page.addInitScript(()=>{window.__BITIGMAIL_ENGINE_URL__='http://127.0.0.1:6175';});
  const base='http://127.0.0.1:6175',headers={Origin:'http://127.0.0.1:5173','Content-Type':'application/json'};
  const session=await page.request.post(base+'/api/session',{headers,data:{}});expect(session.status()).toBe(200);
  headers['X-BitigMail-Session']=(await session.json()).token;
  try {
   await page.goto('http://127.0.0.1:5173/');await expect(page).toHaveTitle(/BitigMail/i);
   await page.getByTestId('nav-tab-jobs').click();
   await page.getByTestId('job-row-job-3b6cab7b2f7c').click();
   if(width===1660) await page.getByTestId('job-add-to-archive-btn').click();
   else {
    await page.getByTestId('job-open-btn').click();
    await expect(page.getByTestId('bridge-export-form')).toBeVisible();
    await page.getByTestId('bridge-export-add-to-archive-btn').click();
   }
   await expect(page.getByTestId('archive-add-modal')).toBeVisible();
   await expect(page.getByTestId('archive-frozen-owner-context')).toContainText('Örnek Şirket');
   await expect(page.getByTestId('archive-frozen-owner-context')).toContainText('Sistem Geçişi 2024');
   await expect(page.getByTestId('archive-source-job-id-input')).toHaveCount(0);
   await expect(page.getByTestId('archive-completed-bridge-jobs-select')).toHaveValue('job-3b6cab7b2f7c');
   await page.getByTestId('archive-add-name-input').fill('Aktarımdan arşiv '+width+' '+Date.now());
   const previewPromise=page.waitForResponse(r=>r.url().endsWith('/api/archive/ingest/preview')&&r.request().method()==='POST');
   await page.getByTestId('archive-preview-btn').click();
   const response=await previewPromise;expect(response.status()).toBe(200);const preview=await response.json();
   expect(preview.totalItems).toBe(12);expect(preview.companyId).toBe('comp-ornek');expect(preview.projectId).toBe('proj-ornek-gecis');
   await page.screenshot({path:path.join(output,width+'-preview.png'),fullPage:true});
   const startPromise=page.waitForResponse(r=>r.url().endsWith('/api/archive/ingest/start')&&r.request().method()==='POST');
   await page.getByTestId('archive-start-btn').click();const started=await startPromise;expect(started.status()).toBe(200);const job=await started.json();
   await expect.poll(async()=>{const r=await page.request.get(base+'/api/jobs/'+job.jobId,{headers});return(await r.json()).status;},{timeout:30000}).toBe('completed');
   await expect(page.getByTestId('archive-add-modal')).toHaveCount(0);
   if(!await page.getByTestId('search-location-tree-panel').isVisible()) await page.getByTestId('mobile-location-toggle-btn').click();
   await expect(page.getByTestId('tree-archive-'+job.archiveId)).toBeVisible({timeout:15000});
   await page.screenshot({path:path.join(output,width+'-completed.png'),fullPage:true});
   expect(errors).toEqual([]);
   results.push({width,entry:width===1660?'job-center':'bridge-workflow',jobId:job.jobId,archiveId:job.archiveId,physicalMessages:12,errors});
  } catch(error) {await page.screenshot({path:path.join(output,width+'-failure.png'),fullPage:true});results.push({width,error:String(error),errors});throw error;}
  finally {await context.close();}
 }
 completed=true;
} finally {
 await browser.close();fs.writeFileSync(path.join(output,'report.json'),JSON.stringify({completed,results},null,2));console.log(JSON.stringify({completed,output,results}));
}
