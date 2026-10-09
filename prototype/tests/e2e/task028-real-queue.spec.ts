import { expect, test } from '@playwright/test';
import { openDevelopmentSession, useDevelopmentTestingHost } from './support/testingHost';

useDevelopmentTestingHost();

test('TASK-028 real queued jobs are visible with frozen owner and completed results', async ({ page }) => {
  await openDevelopmentSession(page);
  await page.goto('/');
  await page.getByTestId('nav-tab-jobs').click();
  await page.getByTestId('job-search-input').fill('TASK028 Queue');
  const first = page.getByTestId('job-row-job-582f3e6c5ad0');
  const second = page.getByTestId('job-row-job-7e3670848f2f');
  await expect(first).toBeVisible();
  await expect(second).toBeVisible();
  await expect(first).toContainText('Tamamlandı');
  await expect(second).toContainText('Tamamlandı');
  await expect(first).toContainText('TASK028 Queue');
  await second.click();
  await expect(page.getByTestId('job-details-pane')).toContainText('13 / 13 ileti');
  await expect(page.getByTestId('job-details-pane')).toContainText('TASK028 Queue');
});
