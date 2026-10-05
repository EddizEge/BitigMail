import { test, expect } from '@playwright/test';
import path from 'path';

test('TASK-031 Gmail/Workspace connection form renders safely', async ({ page }, testInfo) => {
  await page.goto('/');
  await page.getByTestId('view-company-btn-comp-ornek').click();
  await page.locator('[data-testid^="add-account-btn-"]').first().click();
  await page.getByTestId('tab-google-oauth').click();
  await expect(page.getByTestId('google-oauth-panel')).toContainText('imap.gmail.com:993');
  await expect(page.getByTestId('google-clientsecret-input')).toHaveAttribute('type', 'password');
  await expect(page.getByTestId('submit-google-connect-btn')).toBeVisible();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
  await page.screenshot({ path: path.join('..', '.codex-coordination', 'evidence', 'TASK-031', `google-connect-${testInfo.project.name}.png`), fullPage: false });
});
