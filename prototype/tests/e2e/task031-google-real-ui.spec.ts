import { test, expect } from '@playwright/test';
import path from 'path';

test('TASK-031 TestingHost Google add flow reaches durable connected state', async ({ page }) => {
  const owner = `TASK031 Gmail ${Date.now()}`;
  await page.addInitScript(() => { (window as any).__BITIGMAIL_ENGINE_URL__ = 'http://127.0.0.1:6175'; });
  await page.goto('/');
  await page.getByTestId('view-company-btn-comp-ornek').click();
  await page.locator('[data-testid^="add-account-btn-"]').first().click();
  await page.getByTestId('tab-google-oauth').click();
  await page.getByTestId('google-displayname-input').fill(owner);
  await page.getByTestId('google-email-input').fill('task031@gmail.example');
  await page.getByTestId('google-clientid-input').fill('123456789-task031.apps.googleusercontent.com');
  await page.getByTestId('google-clientsecret-input').fill('synthetic-render-only-secret');
  const connectedResponse = page.waitForResponse(async response => {
    if (!response.url().includes('/api/oauth/google/operations/') || response.request().method() !== 'GET') return false;
    try { return (await response.json()).status === 'connected'; } catch { return false; }
  });
  await page.getByTestId('submit-google-connect-btn').click();
  await expect(page.getByTestId('google-status-connected')).toBeVisible();
  const connected = await (await connectedResponse).json();
  await expect(page.getByTestId('google-clientsecret-input')).toHaveCount(0);
  await page.screenshot({ path: path.join('..', '.codex-coordination', 'evidence', 'TASK-031', 'google-connected-testinghost.png'), fullPage: false });
  await page.keyboard.press('Escape');
  const row = page.getByTestId(`account-row-${connected.accountId}`);
  await expect(row).toContainText(owner);
  await expect(row).toContainText('Gmail / XOAUTH2');
  await expect(row.getByText('Bağlantıyı Sına')).toBeVisible();
  await expect(row.getByText('Yeniden Bağlan')).toBeVisible();
  await row.scrollIntoViewIfNeeded();
  await page.screenshot({ path: path.join('..', '.codex-coordination', 'evidence', 'TASK-031', 'google-account-row-testinghost.png'), fullPage: false });
});
