import {createRequire} from 'node:module';
import fs from 'node:fs';
import path from 'node:path';
const root='C:/Users/Eddiz/Documents/ChatGPT/Mail Manager';
const require=createRequire(path.join(root,'prototype/package.json'));
const {chromium,expect}=require('@playwright/test');
const acceptance=JSON.parse(fs.readFileSync(process.argv[2],'utf8').replace(/^\uFEFF/,''));
if(!acceptance.completed || acceptance.checks.some(c=>!c.pass)) throw new Error('Accepted API report required');
const [a,b,c]=acceptance.archives;
const output=path.join('C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task017-qa','archive-ui-'+Date.now());
fs.mkdirSync(output,{recursive:true});
const browser=await chromium.launch({headless:true});
const results=[];
let completed=false;
try {
 for(const width of (process.argv.includes('--mobile-only')?[390]:[1660,390])) {
  const context=await browser.newContext({viewport:{width,height:width===390?844:948}});
  const page=await context.newPage();
  const errors=[],beacons=[],issues=[];
  page.on('pageerror',e=>errors.push(e.message));
  page.on('console',e=>{if(['error','warning'].includes(e.type())) errors.push(e.text());});
  page.on('request',r=>{if(r.url().includes('example.invalid/task018')) beacons.push(r.url());});
  await page.addInitScript(()=>{window.__BITIGMAIL_ENGINE_URL__='http://127.0.0.1:6175';});
  const base='http://127.0.0.1:6175';
  const headers={Origin:'http://127.0.0.1:5173','Content-Type':'application/json'};
  const session=await page.request.post(base+'/api/session',{headers,data:{}});
  expect(session.status()).toBe(200);headers['X-BitigMail-Session']=(await session.json()).token;
  const post=async(url,data={})=>{const r=await page.request.post(base+url,{headers,data});expect(r.status(),url).toBe(200);return r.json();};
  const showTree=async()=>{if(!await page.getByTestId('search-location-tree-panel').isVisible()) await page.getByTestId('mobile-location-toggle-btn').click();};
  const hideTree=async()=>{if(width===390&&await page.getByTestId('search-location-tree-panel').isVisible()) await page.getByTestId('mobile-location-toggle-btn').click();};
  const expectCount=async n=>expect(page.getByTestId('search-status-bar')).toContainText(n+' sonuç');
  const screenshot=async name=>page.screenshot({path:path.join(output,width+'-'+name+'.png'),fullPage:true});
  try {
   await page.goto('http://127.0.0.1:5173/');await expect(page).toHaveTitle(/BitigMail/i);
   await page.getByTestId('nav-tab-search').click();await page.getByTestId('archive-mode-real-btn').click();
   await expect(page.getByTestId('search-empty-scope-prompt')).toBeVisible();
   await showTree();
   await expect(page.getByTestId('tree-company-'+a.companyId)).toContainText(a.companyName);
   await expect(page.getByTestId('tree-company-'+b.companyId)).toContainText(b.companyName);
   // Fresh browser has no saved company records for these server-registered archive owners.
   const company=page.getByTestId('tree-company-'+a.companyId);
   await company.getByText(a.companyName,{exact:true}).click();
   await expect(page.getByTestId('checkbox-archive-'+a.archiveId)).toHaveCount(0);
   await company.getByText(a.companyName,{exact:true}).click();
   await page.getByTestId('checkbox-company-'+a.companyId).check();await expectCount(16);
   await page.getByTestId('checkbox-company-'+b.companyId).check();await expectCount(28);
   await page.getByTestId('checkbox-archive-'+c.archiveId).uncheck();await expectCount(24);
   expect(await page.getByTestId('checkbox-company-'+a.companyId).evaluate(e=>e.indeterminate)).toBe(true);
   await hideTree();
   await page.getByTestId('archive-search-field-select').selectOption('subject');
   await page.getByTestId('archive-search-input').fill('istanbul');await expectCount(8);
   await expect(page.locator('footer')).not.toContainText('Örnek proje');
   await expect(page.locator('footer')).toContainText('2 arşiv seçili');
   if(width===390) {
    const q=await page.getByTestId('archive-search-input').boundingBox(),f=await page.getByTestId('archive-search-field-select').boundingBox();
    expect(f.y-q.y-q.height,'mobile search controls stay adjacent').toBeLessThan(60);
   }
   await page.getByTestId('archive-folder-filter').selectOption('Corpus');await expectCount(4);
   await page.getByTestId('archive-folder-filter').selectOption('all');await expectCount(8);
   await page.getByTestId('archive-search-start-date').fill('2024-01-01');
   await page.getByTestId('archive-search-end-date').fill('2024-02-16');await expectCount(6);
   await page.getByTestId('archive-search-start-date').fill('');await page.getByTestId('archive-search-end-date').fill('');await expectCount(8);
   const allAttachmentValue=await page.getByTestId('archive-search-attachment-filter').inputValue();
   await page.getByTestId('archive-search-attachment-filter').selectOption({label:'Yalnızca Ekli'});await expectCount(2);
   await page.getByTestId('archive-search-attachment-filter').selectOption(allAttachmentValue);await expectCount(8);
   const expectedRows=await post('/api/archive/search',{selectedScopes:[a,b].map(({companyId,projectId,archiveId})=>({companyId,projectId,archiveId})),query:'istanbul',field:'subject',page:1,pageSize:100});
   const selected=expectedRows.items.find(i=>i.archiveId===b.archiveId);
   expect(selected).toBeTruthy();
   const previewPromise=page.waitForResponse(r=>r.url().endsWith('/api/archive/message/preview'));
   await page.getByTestId('search-result-row-'+selected.messageId).click();
   const previewResponse=await previewPromise;expect(previewResponse.status()).toBe(200);
   expect((await previewResponse.json()).archiveId).toBe(b.archiveId);
   await expect(page.getByTestId('preview-subject')).toHaveText(selected.subject);
   try { await expect(page.getByTestId('preview-location-company')).toHaveText(b.companyName); }
   catch(error) { issues.push({case:'authoritative preview owner display name',error:String(error)}); }
   await screenshot('two-company-search');
   await page.getByTestId('archive-search-input').fill('NO_MATCH_SENTINEL_018');await expectCount(0);
   await expect(page.getByTestId('preview-empty-state')).toBeVisible();
   await showTree();await page.getByTestId('search-clear-selection-btn').click();
   await page.getByTestId('checkbox-archive-'+c.archiveId).check();await hideTree();
   await page.getByTestId('archive-search-field-select').selectOption('body');
   await page.getByTestId('archive-search-input').fill('Özgün İstanbul');await expectCount(1);
   await expect(page.getByTestId('archive-truncation-warning')).toBeVisible();
   const htmlRow=page.getByTestId('search-results-table').locator('tbody tr').first();await htmlRow.click();
   await expect(page.getByTestId('preview-body')).toContainText('Özgün İstanbul & INVOICE');
   await expect(page.getByTestId('preview-body')).toContainText('Güvenli gövde sonu');
   await expect(page.getByTestId('message-preview-pane').locator('script,img,iframe,object,embed')).toHaveCount(0);
   expect(await page.evaluate(()=>window.TASK018_SCRIPT_EXECUTED===true)).toBe(false);expect(beacons).toEqual([]);
   await page.getByTestId('preview-body').scrollIntoViewIfNeeded();await screenshot('safe-html-preview');
   await showTree();await page.getByTestId('search-clear-selection-btn').click();await hideTree();
   await expect(page.getByTestId('search-empty-scope-prompt')).toBeVisible();await expect(page.getByTestId('preview-empty-state')).toBeVisible();
   // Real native-picker abstraction bound to root's synthetic named fixture only.
   await post('/api/testing/set-mime-source',{fixtureId:'archive-edgecases'});
   await page.getByTestId('archive-open-add-modal-btn').click();
   await expect(page.getByTestId('archive-add-modal')).toBeVisible();
   await page.getByTestId('archive-add-mime-mode').selectOption('eml-tree');
   await page.getByTestId('archive-pick-source-btn').click();
   await expect(page.getByTestId('archive-picked-source-info')).toBeVisible();
   await page.getByTestId('archive-add-name-input').fill('Arayüz denemesi '+width+' '+Date.now());
   await page.getByTestId('archive-preview-btn').click();await expect(page.getByTestId('archive-preview-summary')).toBeVisible();
   await screenshot('add-preview');
   const startedPromise=page.waitForResponse(r=>r.url().endsWith('/api/archive/ingest/start')&&r.request().method()==='POST');
   await page.getByTestId('archive-start-btn').click();const startResponse=await startedPromise;
   expect(startResponse.status()).toBe(200);const job=await startResponse.json();
   await expect.poll(async()=>{const r=await page.request.get(base+'/api/jobs/'+job.jobId,{headers});return(await r.json()).status;},{timeout:30000}).toBe('completed');
   await expect(page.getByTestId('archive-add-modal')).toHaveCount(0);await showTree();
   await expect(page.getByTestId('tree-archive-'+job.archiveId)).toBeVisible({timeout:15000});
   await screenshot('ingested');
   await page.getByTestId('nav-tab-jobs').click();
   await page.getByTestId('job-row-'+job.jobId).click();
   await page.getByTestId('job-open-btn').click();
   await expect(page.getByTestId('archive-search-view')).toBeVisible();
   await expect(page.getByTestId('archive-frozen-context')).toContainText(job.archiveName);
   await expect(page.getByTestId('archive-frozen-context')).toContainText(job.clientContext.companyName);
   await screenshot('job-restored');
   const geometry=await page.evaluate(()=>({viewport:innerWidth,document:document.documentElement.scrollWidth,body:document.body.scrollWidth}));
   expect(geometry.document).toBeLessThanOrEqual(width+1);expect(geometry.body).toBeLessThanOrEqual(width+1);expect(errors).toEqual([]);
   results.push({width,jobId:job.jobId,archiveId:job.archiveId,geometry,errors,issues,remoteFixtureRequests:beacons});
  } catch(error) { await screenshot('failure');results.push({width,error:String(error),errors,issues,beacons});throw error; }
  finally { await context.close(); }
 }
 completed=results.every(r=>!r.error&&r.errors.length===0&&r.issues.length===0);
 if(!completed) process.exitCode=1;
} finally {
 await browser.close();fs.writeFileSync(path.join(output,'report.json'),JSON.stringify({completed,results,sourceApiReport:process.argv[2]},null,2));
 console.log(JSON.stringify({completed,output,results}));
}
