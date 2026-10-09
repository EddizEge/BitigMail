import { test, expect } from '@playwright/test';
import net from 'node:net';

test('TASK-034 POP source-only constraint is visible and responsive', async ({ page }) => {
  await page.route('**/api/pop/accounts?*', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await page.goto('/'); await page.getByTestId('nav-tab-transfers').click(); await page.getByTestId('op-tab-migration').click(); await page.getByTestId('tab-direction-pop-to-file').click();
  const workflow = page.getByTestId('pop-workflow'); await expect(workflow).toContainText('POP kaynağından al'); await expect(workflow).toContainText('Postalar sunucuda kalır'); await expect(workflow).toContainText('POP hedefi ve silme işlemi yoktur');
  await expect(page.getByTestId('pop-filter-guidance')).toContainText('indirmeden önce tam gövde filtresi desteklenmez');
  expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
});

test('TASK-034 actual TestingHost POP job fails on UIDL change and resumes from JobCenter', async ({ page }) => {
  test.skip(test.info().project.name !== 'desktop-reference');
  const messages = [1,2,3].map(i => `From: a@example.test\r\nTo: b@example.test\r\nSubject: POP ${i}\r\nMessage-Id: <same@example.test>\r\nDate: Mon, 1 Jan 2024 00:00:00 +0000\r\n\r\nbody-${i}\r\n`);
  let reorder = true, retrievals = 0;
  const server = net.createServer(socket => { socket.setEncoding('ascii'); socket.write('+OK local POP\r\n'); let pending=''; socket.on('data', chunk => { pending += chunk; let at; while((at=pending.indexOf('\r\n'))>=0){const line=pending.slice(0,at);pending=pending.slice(at+2);const [raw,...rest]=line.split(' '),cmd=raw.toUpperCase(),arg=rest.join(' ');if(cmd==='CAPA')socket.write('+OK\r\nUSER\r\nUIDL\r\n.\r\n');else if(cmd==='USER'||cmd==='PASS'||cmd==='NOOP')socket.write('+OK\r\n');else if(cmd==='STAT')socket.write(`+OK 3 ${messages.reduce((n,m)=>n+Buffer.byteLength(m),0)}\r\n`);else if(cmd==='UIDL'){const ids=(reorder&&retrievals>0?['uid-3','uid-2','uid-1']:['uid-1','uid-2','uid-3']);socket.write('+OK\r\n'+ids.map((id,i)=>`${i+1} ${id}\r\n`).join('')+'.\r\n')}else if(cmd==='LIST')socket.write('+OK\r\n'+messages.map((m,i)=>`${i+1} ${Buffer.byteLength(m)}\r\n`).join('')+'.\r\n');else if(cmd==='RETR'){retrievals++;const m=messages[Number(arg)-1];socket.write(`+OK ${Buffer.byteLength(m)} octets\r\n${m}.\r\n`)}else if(cmd==='DELE')socket.write('-ERR forbidden\r\n');else if(cmd==='QUIT'){socket.write('+OK bye\r\n');socket.end()}else socket.write('-ERR\r\n')}}); });
  await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',()=>resolve())); const port=(server.address() as net.AddressInfo).port;
  try {
    await page.addInitScript(()=>{(window as any).__BITIGMAIL_ENGINE_URL__='http://127.0.0.1:6175'}); const headers:Record<string,string>={Origin:'http://127.0.0.1:5173','Content-Type':'application/json'};
    const session=await page.request.post('http://127.0.0.1:6175/api/session',{headers,data:{}});headers['X-BitigMail-Session']=(await session.json()).token;
    const seeded=await page.request.post('http://127.0.0.1:6175/api/testing/seed-pop-account',{headers,data:{companyId:'comp-ornek',projectId:'proj-ornek-gecis',port,username:'user',password:'pass'}});const seededAccount=await seeded.json();
    await page.goto('/');await page.getByTestId('nav-tab-transfers').click();await page.getByTestId('op-tab-migration').click();await page.getByTestId('tab-direction-pop-to-file').click();await page.getByLabel('POP hesabı').selectOption(seededAccount.accountId);await expect(page.getByLabel('POP hesabı')).toHaveValue(seededAccount.accountId);
    await page.getByTestId('pop-preview').click();await expect(page.getByText('3 ileti listelendi', { exact: false })).toBeVisible();await page.getByText('Çıktı klasörü seç').click();await page.getByTestId('pop-start').click();await expect(page.getByTestId('pop-job-status')).toContainText('başarısız',{timeout:20000});
    reorder=false;await page.getByTestId('nav-tab-jobs').click();await expect(page.getByTestId('job-resume-pop-btn')).toBeVisible({timeout:10000});await page.getByTestId('job-resume-pop-btn').click();
    await expect.poll(async()=>{const res=await page.request.get('http://127.0.0.1:6175/api/jobs',{headers});const jobs=await res.json();return jobs.filter((j:any)=>j.jobKind==='pop-snapshot'&&j.status==='completed').length},{timeout:20000}).toBeGreaterThan(0);
  } finally { await new Promise<void>(resolve=>server.close(()=>resolve())); }
});
