import { test, expect } from '@playwright/test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { openDevelopmentSession, useDevelopmentTestingHost } from './support/testingHost';

useDevelopmentTestingHost();

// Starts its own current Release TestingHost on 6175 (support/testingHost).
// No production native picker or real user mailbox is automated.
const base = 'http://127.0.0.1:6175';
const evidence = path.join(os.tmpdir(), 'bitigmail-task013-qa', 'ui-final');
fs.mkdirSync(evidence, { recursive: true });

test('real MIME conversion, immutable selection, history and report', async ({ page }, info) => {
  const scenario = info.project.name === 'mobile-narrow' ? { mode: 'eml-tree', fixture: 'corpus-tree', count: 3, attachments: 1 }
    : info.project.name === 'desktop-compact' ? { mode: 'mbox', fixture: 'corpus-mbox', count: 12, attachments: 4 }
    : { mode: 'eml-files', fixture: 'corpus-eml', count: 12, attachments: 4 };
  const errors: string[] = [];
  page.on('pageerror', e => errors.push(e.message));
  page.on('console', entry => { if (entry.type() === 'error') errors.push(entry.text()); });
  await openDevelopmentSession(page);
  const headers: Record<string, string> = { Origin: 'http://127.0.0.1:5173', 'Content-Type': 'application/json' };
  const handshake = await page.request.post(base + '/api/session', { headers, data: {} });
  expect(handshake.status()).toBe(200);
  headers['X-BitigMail-Session'] = (await handshake.json()).token;
  const setup = await page.request.post(base + '/api/testing/set-mime-source', { headers, data: { fixtureId: scenario.fixture } });
  expect(setup.status()).toBe(200);
  await page.goto('http://127.0.0.1:5173/');
  await expect(page).toHaveTitle(/BitigMail/i);
  await page.getByTestId('nav-tab-transfers').click();
  await page.getByTestId('op-tab-convert').click();
  await page.getByTestId('convert-input-mime').click();
  await expect(page.getByTestId('mime-service-status')).toContainText('hazır');
  await page.getByTestId(`mime-mode-${scenario.mode}`).click();
  await page.getByTestId('mime-pick-source').click();
  await expect(page.getByTestId('mime-preview-selected')).toHaveText('12');
  await page.getByTestId('mime-clear-folders').click();
  await expect(page.getByTestId('mime-preview-selected')).toHaveText('0');
  await expect(page.getByTestId('mime-start')).toBeDisabled();
  if (scenario.mode === 'eml-tree') {
    await expect(page.getByTestId('mime-ignored-count')).toHaveText('1');
    await page.getByTestId('mime-folder').filter({ hasText: 'Projeler/İstanbul' }).locator('input').check();
    await page.getByTestId('mime-start-date').fill('2024-01-01');
    await page.getByTestId('mime-end-date').fill('2024-02-16');
    await expect(page.getByTestId('mime-preview-selected')).toHaveText('3');
  } else {
    await page.getByTestId('mime-select-all').click();
    await expect(page.getByTestId('mime-preview-selected')).toHaveText('12');
  }
  if (scenario.mode === 'eml-files') {
    // Return an obsolete response AFTER the latest preview and ensure it cannot win.
    await page.route('**/api/mime/selection/preview', async route => {
      const response = await route.fetch();
      if (route.request().postDataJSON().startDate === '2024-01-01') await new Promise(r => setTimeout(r, 1200));
      await route.fulfill({ response });
    });
    const oldRequest = page.waitForRequest(r => r.url().endsWith('/api/mime/selection/preview') && r.postDataJSON().startDate === '2024-01-01');
    await page.getByTestId('mime-start-date').fill('2024-01-01');
    await oldRequest;
    await page.getByTestId('mime-start-date').fill('2025-01-01');
    await expect(page.getByTestId('mime-preview-selected')).toHaveText('1');
    await page.waitForTimeout(1400); // Only the intentionally delayed obsolete response.
    await expect(page.getByTestId('mime-preview-selected')).toHaveText('1');
    await page.unroute('**/api/mime/selection/preview');
    const choices = await page.getByTestId('transfer-company-select').locator('option').evaluateAll(options => options.map(o => (o as HTMLOptionElement).value));
    const current = await page.getByTestId('transfer-company-select').inputValue();
    const different = choices.find(value => value !== current);
    expect(different).toBeTruthy();
    await page.getByTestId('transfer-company-select').selectOption(different!);
    await expect(page.getByTestId('mime-selection-card')).toHaveCount(0);
    await page.getByTestId('mime-pick-source').click();
    await expect(page.getByTestId('mime-preview-selected')).toHaveText('12');
  }
  await page.getByTestId('mime-pick-target').click();
  await expect(page.getByTestId('mime-start')).toBeEnabled();
  await page.getByTestId('local-mime-workflow').evaluate(el => { el.scrollTop = 0; });
  await page.screenshot({ path: path.join(evidence, `${info.project.name}-source.png`) });
  const responsePromise = page.waitForResponse(r => r.url().endsWith('/api/mime/start') && r.request().method() === 'POST');
  await page.getByTestId('mime-start').click();
  const response = await responsePromise;
  expect(response.status()).toBe(200);
  const started = await response.json();
  const requestContext = response.request().postDataJSON().clientContext;
  await expect(page.getByTestId('mime-job-id')).toHaveText(started.jobId);
  await expect(page.getByTestId('mime-result-count')).toHaveText(String(scenario.count), { timeout: 20000 });
  await expect(page.getByTestId('mime-result-qualification')).toContainText('Deneme işaretleri içeriyor');
  await expect(page.getByTestId('mime-job-context')).toContainText(requestContext.companyName);
  const downloadPromise = page.waitForEvent('download');
  await page.getByTestId('mime-download-report').click();
  const downloadPath = path.join(evidence, `${info.project.name}-report.json`);
  await (await downloadPromise).saveAs(downloadPath);
  const report = JSON.parse(fs.readFileSync(downloadPath, 'utf8'));
  expect(report.jobId).toBe(started.jobId);
  expect(report.mimeImport.overallQualification).toBe('DIFFERENCES');
  expect(report.reopenedPstVerification.totalPhysicalItemsFound).toBe(scenario.count);
  expect(report.reopenedPstVerification.totalAttachmentsVerified).toBe(scenario.attachments);
  expect(report.clientContext.companyId).toBe(requestContext.companyId);
  expect(fs.existsSync(report.outputPath)).toBe(true);
  await page.reload();
  await page.getByTestId('nav-tab-jobs').click();
  await page.getByTestId(`job-row-${started.jobId}`).click();
  await page.getByTestId('job-open-btn').click();
  await expect(page.getByTestId('local-mime-workflow')).toBeVisible();
  await expect(page.getByTestId('mime-job-id')).toHaveText(started.jobId);
  await expect(page.getByTestId('mime-result-count')).toHaveText(String(scenario.count));
  await page.getByTestId('mime-job-card').scrollIntoViewIfNeeded();
  await page.screenshot({ path: path.join(evidence, `${info.project.name}-result.png`) });
  const widths = await page.evaluate(() => ({ width: innerWidth, scroll: document.documentElement.scrollWidth, overlay: !!document.querySelector('vite-error-overlay') }));
  expect(widths.scroll).toBeLessThanOrEqual(widths.width);
  expect(widths.overlay).toBe(false);
  expect(errors).toEqual([]);
  fs.writeFileSync(path.join(evidence, `${info.project.name}-checks.json`), JSON.stringify({ jobId: started.jobId, scenario, widths, errors, outputPath: report.outputPath, clientContext: report.clientContext }, null, 2));
});
