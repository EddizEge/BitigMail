import { expect, test } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';

const makeRecord = (index: number) => ({
  jobId: `task028-${String(index).padStart(5, '0')}`,
  jobKind: 'convert',
  sourceFileName: `source-${index}.ost`,
  targetFileName: `target-${index}.pst`,
  status: 'completed',
  stage: 'Tamamlandı',
  percentComplete: 100,
  itemsRead: 1,
  itemsWritten: 1,
  failedItems: 0,
  totalItems: 1,
  clientContext: { companyId: 'task028-company', companyName: 'Kuyruk Testi', projectId: 'task028-project', projectName: 'Sayfalama' },
  createdAt: '2026-09-15T12:00:00Z',
});

test('TASK-028 renders a bounded accessible page from a 10000-job history', async ({ page }, testInfo) => {
  const requestedPages: number[] = [];
  let priorityPayload:unknown=null;
  await page.route('**/api/jobs/task028-00041/priority',async route=>{priorityPayload=route.request().postDataJSON();await route.fulfill({contentType:'application/json',body:JSON.stringify({...makeRecord(41),status:'queued',waitingPriority:10})});});
  await page.route('**/api/jobs/page?**', async route => {
    const url = new URL(route.request().url());
    const pageNumber = Number(url.searchParams.get('page') || '1');
    const status = url.searchParams.get('status');
    requestedPages.push(pageNumber);
    const queuedItems = [makeRecord(41), makeRecord(17)].map(item => ({
      ...item,
      status: 'queued', stage: 'Sırada', percentComplete: 0,
      itemsRead: 0, itemsWritten: 0, startedAt: null,
      waitingAtShutdown: true, neverStartedQueued: true,
    }));
    const items = status === 'queued'
      ? queuedItems
      : Array.from({ length: 50 }, (_, offset) => makeRecord(10_000 - ((pageNumber - 1) * 50) - offset));
    await route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        items,
        page: pageNumber,
        pageSize: 50,
        totalCount: status === 'queued' ? queuedItems.length : 10_000,
      }),
    });
  });

  await page.goto('/');
  await page.getByTestId('nav-tab-jobs').click();
  await expect(page.getByTestId('job-center-view')).toBeVisible();
  await expect(page.getByRole('button', { name: /sonraki/i })).toBeVisible();
  await expect(page.locator('[data-testid^="job-row-task028-"]')).toHaveCount(50);
  await page.getByRole('button', { name: /sonraki/i }).click();
  await expect.poll(() => requestedPages.includes(2)).toBe(true);
  await expect(page.locator('[data-testid^="job-row-task028-"]')).toHaveCount(50);

  const dimensions = await page.evaluate(() => ({ width: window.innerWidth, body: document.body.scrollWidth }));
  expect(dimensions.body).toBeLessThanOrEqual(dimensions.width);
  await expect(page.getByTestId('job-search-input')).toHaveAttribute('placeholder', 'İş veya müşteri ara');

  const evidenceDir = path.resolve('..', '.codex-coordination', 'evidence', 'TASK-028', 'ui');
  await mkdir(evidenceDir, { recursive: true });
  await page.screenshot({ path: path.join(evidenceDir, `jobcenter-all-${testInfo.project.name}.png`), fullPage: true, animations: 'disabled' });
  await page.getByTestId('status-tab-queued').click();
  await expect.poll(() => requestedPages.at(-1)).toBe(1);
  const queuedRows = page.locator('[data-testid^="job-row-task028-"]');
  await expect(queuedRows).toHaveCount(2);
  await expect(queuedRows.nth(0)).toContainText('Sırada');
  await expect(queuedRows.nth(1)).toContainText('Sırada');
  await expect(page.getByTestId('status-tab-queued')).toHaveClass(/active/);
  await queuedRows.nth(0).click();
  await page.getByTestId('job-waiting-priority').selectOption('10');
  await expect.poll(()=>priorityPayload).toEqual({priority:10,companyId:'task028-company',projectId:'task028-project'});
  await expect(page.getByTestId('job-progress-bar')).toHaveAttribute('style', /width: 0%;/);
  await page.screenshot({ path: path.join(evidenceDir, `jobcenter-queued-${testInfo.project.name}.png`), fullPage: true, animations: 'disabled' });
  await writeFile(path.join(evidenceDir, `jobcenter-${testInfo.project.name}.json`), JSON.stringify({
    project: testInfo.project.name,
    viewport: page.viewportSize(),
    renderedAllRows: 50,
    renderedQueuedRows: await page.locator('[data-testid^="job-row-task028-"]').count(),
    requestedPages,
    dimensions,
    passed: true,
  }, null, 2));
});
