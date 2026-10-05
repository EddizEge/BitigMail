import { test, expect } from '@playwright/test';
import path from 'node:path';

test('TASK-032 EMLX normalization surface is responsive and qualified', async ({ page }, info) => {
  await page.goto('/');
  await page.getByTestId('nav-tab-transfers').click();
  await page.getByTestId('op-tab-convert').click();
  await page.getByTestId('convert-input-emlx').click();
  const workflow = page.getByTestId('emlx-workflow');
  await expect(workflow).toBeVisible();
  await expect(workflow).toContainText('EMLX → doğrulanmış EML klasörleri');
  await expect(workflow).toContainText('Apple ek bilgileri ayrı dosyada saklanır');
  await expect(workflow.getByText('Harici ek depoları açık engeldir')).not.toBeVisible();
  await expect(page.getByTestId('emlx-pick')).toBeVisible();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join('..', '.codex-coordination', 'evidence', 'TASK-032', `emlx-${info.project.name}.png`), fullPage: false });
});
