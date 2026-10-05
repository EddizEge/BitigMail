import { test, expect, type Locator, type Page } from '@playwright/test';

async function expectInsideWorkbench(locator: Locator, page: Page) {
  const box = await locator.boundingBox();
  const viewport = page.viewportSize();
  expect(box).not.toBeNull();
  expect(viewport).not.toBeNull();
  expect(box!.y).toBeGreaterThanOrEqual(56);
  expect(box!.y + box!.height).toBeLessThanOrEqual(viewport!.height - 31);
  expect(box!.x).toBeGreaterThanOrEqual(0);
  expect(box!.x + box!.width).toBeLessThanOrEqual(viewport!.width + 1);
}

test.describe('Desktop workbench geometry', () => {
  test.beforeEach(async ({ page }, testInfo) => {
    test.skip(testInfo.project.name === 'mobile-narrow', 'Narrow layout uses document scrolling.');
    await page.goto('/');
    await page.evaluate(() => localStorage.clear());
    await page.reload();
    await page.getByTestId('nav-tab-transfers').click();
  });

  test('keeps preview and preflight action visible while the message list scrolls', async ({ page }) => {
    const list = page.getByTestId('messages-table-container');
    await expect(page.getByTestId('scope-summary-text')).toContainText('248 ileti');
    await expectInsideWorkbench(page.locator('.preview-pane'), page);
    const locationBadge = page.getByTestId('preview-location-context');
    await expect(locationBadge).toBeVisible();
    await expectInsideWorkbench(locationBadge, page);
    const badgeBox = await locationBadge.boundingBox();
    expect(badgeBox).not.toBeNull();
    expect(badgeBox!.height).toBeGreaterThanOrEqual(20);
    await expectInsideWorkbench(page.getByTestId('run-preflight-btn'), page);
    const dimensions = await list.evaluate((element) => ({
      visible: element.clientHeight,
      content: element.scrollHeight,
    }));
    expect(dimensions.visible).toBeGreaterThan(100);
    expect(dimensions.content).toBeGreaterThan(dimensions.visible);
    await list.evaluate((element) => { element.scrollTop = element.scrollHeight; });
    await expectInsideWorkbench(page.locator('.preview-pane'), page);
    await expectInsideWorkbench(page.getByTestId('run-preflight-btn'), page);
    expect(await page.evaluate(() => window.scrollY)).toBe(0);
  });

  test('keeps both preflight footer actions visible without scrolling the document', async ({ page }) => {
    await page.getByTestId('run-preflight-btn').click();
    await expect(page.getByTestId('start-transfer-btn')).toBeDisabled();
    await expectInsideWorkbench(page.getByTestId('start-transfer-btn'), page);
    await expectInsideWorkbench(page.getByTestId('back-to-plan-btn'), page);
    await page.locator('.preflight-grid').evaluate((element) => { element.scrollTop = element.scrollHeight; });
    await page.getByTestId('resolve-choice-skip-radio').check();
    await expect(page.getByTestId('start-transfer-btn')).toBeEnabled();
    await expectInsideWorkbench(page.getByTestId('start-transfer-btn'), page);
    expect(await page.evaluate(() => window.scrollY)).toBe(0);
  });
});
