import { expect, test } from '@playwright/test';

const engine = 'http://127.0.0.1:6175';

test('TASK-037B renders selected archive export and completes a verified real job', async ({ page }) => {
  const archiveName = `TASK037B UI ${Date.now()}`;
  const headers: Record<string, string> = { Origin: 'http://127.0.0.1:5173', 'Content-Type': 'application/json' };
  const session = await page.request.post(engine + '/api/session', { headers, data: {} });
  headers['X-BitigMail-Session'] = (await session.json()).token;
  const picked = await (await page.request.post(engine + '/api/picker/archive-source', { headers, data: { mode: 'eml-tree' } })).json();
  const preview = await (await page.request.post(engine + '/api/archive/ingest/preview', { headers, data: {
    sourceHandle: picked.handle, archiveName, companyId: 'comp-ornek', projectId: 'proj-ornek-gecis', companyName: 'Örnek Şirket', projectName: 'Örnek Proje',
  } })).json();
  const ingest = await (await page.request.post(engine + '/api/archive/ingest/start', { headers, data: {
    previewId: preview.previewId, idempotencyKey: `task037b-ui-${Date.now()}`, enqueueIfBusy: true,
  } })).json();
  await expect.poll(async () => (await (await page.request.get(`${engine}/api/jobs/${ingest.jobId}`, { headers })).json()).status, { timeout: 20_000 }).toBe('completed');

  await page.addInitScript(() => {
    (window as typeof window & { __BITIGMAIL_ENGINE_URL__?: string }).__BITIGMAIL_ENGINE_URL__ = 'http://127.0.0.1:6175';
  });
  await page.goto('/');
  await page.getByTestId('nav-tab-search').click();
  await expect(page.getByTestId('archive-search-view')).toBeVisible();
  await expect(page.getByText(archiveName, { exact: true })).toBeVisible();

  await page.getByTestId(`checkbox-archive-${ingest.archiveId}`).check();
  await expect(page.getByTestId('search-results-table')).toBeVisible();
  const firstRow = page.locator('[data-testid^="search-result-row-"]').first();
  await firstRow.locator('input[type="checkbox"]').check();
  const start = page.getByTestId('archive-selected-export-start');
  await expect(start).toContainText('(1)');
  await expect(start).toBeEnabled();
  await start.click();

  const status = page.getByTestId('archive-selected-export-job');
  await expect(status).toContainText('İş: job-', { timeout: 20_000 });
  const jobId = (await status.innerText()).match(/job-[a-z0-9]+/)?.[0];
  expect(jobId).toBeTruthy();

  await expect.poll(async () => {
    const response = await page.request.get(`${engine}/api/jobs/${jobId}`, { headers });
    return (await response.json()).status;
  }, { timeout: 20_000 }).toBe('completed');
  const report = await page.request.get(`${engine}/api/jobs/${jobId}/report`, { headers });
  expect(report.status()).toBe(200);
  const body = await report.json();
  expect(body.frozenSelectionFingerprint).toMatch(/^[a-f0-9]{64}$/);
  expect(body.folderMappingFingerprint).toMatch(/^[a-f0-9]{64}$/);
  expect(body.itemsWritten).toBe(1);
});
