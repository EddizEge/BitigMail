import { test, expect } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

const base = 'http://127.0.0.1:6175';
const corpus = path.join('..', 'fixtures', 'emlx-corpus-v1', 'complete');
function corpusHash() {
  const hash = crypto.createHash('sha256');
  for (const file of fs.readdirSync(corpus, { recursive: true }).map(String).sort()) {
    const full = path.join(corpus, file); if (fs.statSync(full).isFile()) hash.update(file).update(fs.readFileSync(full));
  }
  return hash.digest('hex');
}

test('TASK-032 TestingHost picker preview job report and Job Center flow', async ({ page }) => {
  const before = corpusHash();
  const headers: Record<string, string> = { Origin: 'http://127.0.0.1:5173', 'Content-Type': 'application/json' };
  const session = await page.request.post(base + '/api/session', { headers, data: {} });
  expect(session.status()).toBe(200); headers['X-BitigMail-Session'] = (await session.json()).token;
  const picked = await (await page.request.post(base + '/api/picker/emlx-source', { headers, data: { mode: 'tree' } })).json();
  const preview = await (await page.request.post(base + '/api/emlx/preview', { headers, data: { sourceHandle: picked.handle } })).json();
  expect(preview.totalItems).toBe(12);
  const output = await (await page.request.post(base + '/api/picker/output-dir', { headers, data: {} })).json();
  const startedResponse = await page.request.post(base + '/api/emlx/start', { headers, data: { sourceHandle: picked.handle, outputDirHandle: output.handle, expectedSourceFingerprint: preview.sourceFingerprint, idempotencyKey: crypto.randomUUID(), clientContext: { companyId: 'comp-ornek', companyName: 'Örnek Şirket', projectId: 'proj-mail', projectName: 'Mail Projesi' }, enqueueIfBusy: true } });
  expect(startedResponse.status()).toBe(200); const started = await startedResponse.json();
  let job: any;
  await expect.poll(async () => { job = await (await page.request.get(`${base}/api/jobs/${started.jobId}`, { headers })).json(); return job.status; }).toBe('completed');
  const report = await (await page.request.get(`${base}/api/jobs/${started.jobId}/report`, { headers })).json();
  expect(report.itemsWritten).toBe(12); expect(report.reopenedPstVerification.totalAttachmentsVerified).toBe(4);
  expect(corpusHash()).toBe(before);

  await page.addInitScript(() => { (window as any).__BITIGMAIL_ENGINE_URL__ = 'http://127.0.0.1:6175'; });
  await page.goto('/'); await page.getByTestId('nav-tab-jobs').click();
  await expect(page.getByTestId('job-center-view')).toBeVisible();
  await page.getByTestId('job-search-input').fill(started.jobId);
  await expect(page.getByTestId('jobs-table')).toContainText('12 Apple Mail EMLX');
  await expect(page.getByTestId('jobs-table')).toContainText('Tamamlandı');
});
