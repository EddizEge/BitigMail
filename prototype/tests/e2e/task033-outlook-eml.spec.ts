import { test, expect } from '@playwright/test';
import path from 'node:path';

test('TASK-033 real UI completes qualified Outlook-to-EML extraction', async ({ page }) => {
  const browserErrors: string[] = [];
  page.on('pageerror', error => browserErrors.push(`pageerror: ${error.message}`));
  page.on('console', message => {
    if (message.type() === 'error') browserErrors.push(`console: ${message.text()}`);
  });
  await page.addInitScript(() => {
    (window as typeof window & { __BITIGMAIL_ENGINE_URL__?: string }).__BITIGMAIL_ENGINE_URL__ = 'http://127.0.0.1:6175';
  });

  await page.goto('/');
  await page.getByTestId('nav-tab-transfers').click();
  await page.getByTestId('op-tab-convert').click();
  await page.getByTestId('convert-input-outlook-eml').click();

  const view = page.getByTestId('outlook-eml-workflow');
  await expect(view).toContainText('PST / OST / OLM → EML klasörleri');
  await page.getByTestId('outlook-eml-pick').click();
  await expect(page.getByTestId('outlook-eml-preview')).toContainText('OLM');
  await page.getByTestId('outlook-eml-output').click();
  await expect(page.getByTestId('outlook-eml-start')).toBeEnabled();
  await page.getByTestId('outlook-eml-start').click();

  const result = page.getByTestId('outlook-eml-result');
  await expect(result).toContainText('22 ileti hazır', { timeout: 30_000 });
  await expect(result).toContainText('Tarih doğrulaması eksik');
  await expect(result).not.toContainText('DATE_FIDELITY_UNRESOLVED');
  await expect(result).toContainText('38 özgün ek doğrulandı');
  expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
  expect(browserErrors).toEqual([]);

  await page.screenshot({
    path: path.resolve('..', '.codex-coordination', 'evidence', 'TASK-033', `${test.info().project.name}-qualified.png`),
    fullPage: true,
  });
});
