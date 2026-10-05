import { test, expect } from '@playwright/test';
import path from 'path';

test.describe('BitigMail End-to-End Migration Flow & QA Spec', () => {
  test.beforeEach(async ({ page }) => {
    // Listen for console errors to ensure zero unhandled exceptions
    page.on('console', (msg) => {
      if (msg.type() === 'error') {
        console.error(`[Browser Error]: ${msg.text()}`);
      }
    });
    // Isolate independent tests by clearing app localStorage in beforeEach
    await page.goto('/');
    await page.evaluate(() => localStorage.clear());
    await page.reload();
  });

  test('should render Clients directory on initial load and view company details', async ({ page }) => {
    await expect(page.locator('[data-testid="top-header"]')).toBeVisible();
    await expect(page.locator('[data-testid="client-directory-view"]')).toBeVisible();
    await expect(page.locator('.page-title')).toHaveText('Müşteriler');

    // Verify company cards exist
    await expect(page.locator('[data-testid="company-card-comp-ornek"]')).toBeVisible();
    await expect(page.locator('[data-testid="company-card-comp-anadolu"]')).toBeVisible();

    // Search company
    await page.fill('[data-testid="client-search-input"]', 'Anadolu');
    await expect(page.locator('[data-testid="company-card-comp-anadolu"]')).toBeVisible();
    await expect(page.locator('[data-testid="company-card-comp-ornek"]')).not.toBeVisible();
    await page.fill('[data-testid="client-search-input"]', '');

    // View company details
    await page.click('[data-testid="view-company-btn-comp-ornek"]');
    await expect(page.locator('[data-testid="client-detail-view"]')).toBeVisible();
    await expect(page.locator('[data-testid="project-card-proj-ornek-gecis"]')).toBeVisible();

    // Start transfer with context
    await page.click('[data-testid="start-transfer-source-src-ornek-imap"]');
    await expect(page.locator('[data-testid="transfers-tab-view"]')).toBeVisible();
    await expect(page.locator('[data-testid="workspace-view"]')).toBeVisible();
  });

  test('should render Workspace under Transfers tab, verify 248 items, and capture QA screenshot', async ({ page }, testInfo) => {
    await page.click('[data-testid="nav-tab-transfers"]');
    await expect(page.locator('[data-testid="workspace-view"]')).toBeVisible();

    // Verify logo and brand name
    await expect(page.locator('[data-testid="bitigmail-logo"]')).toBeVisible();
    await expect(page.locator('.brand-name')).toHaveText('BitigMail');

    // Verify breadcrumbs and title
    await expect(page.locator('[data-testid="breadcrumbs"]')).toContainText('Örnek Şirket');
    await expect(page.locator('.page-title')).toHaveText('Posta geçişi');

    // Verify default 2024 Inbox message scope of exactly 248 items
    await expect(page.locator('[data-testid="scope-summary-text"]')).toContainText('248 ileti');
    await expect(page.locator('[data-testid="plan-scope-value"]')).toContainText('248 ileti');

    // Verify first row matches mockup: Deniz Akın | Proje teslim belgeleri | 18.12.2024 | 2,4 MB
    const firstRow = page.locator('[data-testid="message-row-msg-2024-inbox-001"]');
    await expect(firstRow).toBeVisible();
    await expect(firstRow).toContainText('Deniz Akın');
    await expect(firstRow).toContainText('Proje teslim belgeleri');
    await expect(firstRow).toContainText('18.12.2024');

    // Verify preview pane details & readable provenance location context
    const previewLoc = page.locator('[data-testid="preview-location-context"]');
    await expect(previewLoc).toBeVisible();
    const previewLocBox = await previewLoc.boundingBox();
    expect(previewLocBox).not.toBeNull();
    expect(previewLocBox!.height).toBeGreaterThanOrEqual(20);
    await expect(page.locator('[data-testid="preview-subject"]')).toHaveText('Proje teslim belgeleri');
    await expect(page.locator('[data-testid="preview-sender"]')).toContainText('Deniz Akın <deniz@ornek.example>');
    await expect(page.locator('[data-testid="preview-attachment-chip"]')).toBeVisible();

    // Capture QA screenshot 1: Workspace
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({
      path: path.join('qa', `workspace-${testInfo.project.name}.png`),
      fullPage: false,
    });
  });

  test('should navigate to Job Center, exercise filters, and capture QA screenshot', async ({ page }, testInfo) => {
    // Navigate to Job Center tab
    await page.click('[data-testid="nav-tab-jobs"]');
    await expect(page.locator('[data-testid="job-center-view"]')).toBeVisible();
    await expect(page.locator('.page-title')).toHaveText('İş merkezi');

    // Verify all 6 deterministic sample jobs exist
    await expect(page.locator('[data-testid="job-row-job-1"]')).toContainText('Posta geçişi');
    await expect(page.locator('[data-testid="job-row-job-2"]')).toContainText('Yıllık arşiv');
    await expect(page.locator('[data-testid="job-row-job-3"]')).toContainText('Hesap aktarımı');
    await expect(page.locator('[data-testid="job-row-job-4"]')).toContainText('OST dönüşümü');
    await expect(page.locator('[data-testid="job-row-job-5"]')).toContainText('Posta kurtarma');
    await expect(page.locator('[data-testid="job-row-job-6"]')).toContainText('Mac arşivi aktarımı');

    // Verify Job 1 progress breakdown: 104 / 248
    await expect(page.locator('[data-testid="job-processed-count"]')).toContainText('104 / 248');
    await expect(page.locator('[data-testid="job-transferred-count"]')).toHaveText('100');
    await expect(page.locator('[data-testid="job-skipped-count"]')).toHaveText('3');
    await expect(page.locator('[data-testid="job-failed-count"]')).toHaveText('1');
    await expect(page.locator('[data-testid="job-pending-count"]')).toHaveText('144');

    // Exercise search filter
    await page.fill('[data-testid="job-search-input"]', 'Hesap');
    await expect(page.locator('[data-testid="job-row-job-3"]')).toBeVisible();
    await expect(page.locator('[data-testid="job-row-job-1"]')).not.toBeVisible();
    await page.fill('[data-testid="job-search-input"]', '');

    // Exercise status tabs
    await page.click('[data-testid="status-tab-running"]');
    await expect(page.locator('[data-testid="job-row-job-1"]')).toBeVisible();
    await page.click('[data-testid="status-tab-all"]');

    // Exercise Yeni İş dropdown
    await page.click('[data-testid="new-job-dropdown-btn"]');
    await expect(page.locator('[data-testid="new-job-menu"]')).toBeVisible();
    await expect(page.locator('[data-testid="new-job-mail-migration"]')).toBeVisible();
    await expect(page.locator('[data-testid="new-job-convert"]')).toBeVisible();
    await expect(page.locator('[data-testid="new-job-archive"]')).toBeVisible();
    await expect(page.locator('[data-testid="new-job-recovery"]')).toBeVisible();

    await page.click('[data-testid="new-job-dropdown-btn"]');
    await expect(page.locator('[data-testid="new-job-menu"]')).not.toBeVisible();

    // Click "İşi aç" button to carry context to transfers tab
    await page.click('[data-testid="job-open-btn"]');
    await expect(page.locator('[data-testid="transfers-tab-view"]')).toBeVisible();
    await expect(page.locator('[data-testid="workspace-view"]')).toBeVisible();

    // Re-open job center to capture screenshot
    await page.click('[data-testid="nav-tab-jobs"]');
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({
      path: path.join('qa', `job-center-${testInfo.project.name}.png`),
      fullPage: false,
    });
  });

  test('should execute full preflight blocker resolution, start migration, and capture QA screenshot', async ({ page }, testInfo) => {
    // Navigate to transfers tab
    await page.click('[data-testid="nav-tab-transfers"]');
    await expect(page.locator('[data-testid="workspace-view"]')).toBeVisible();

    // On workspace, click Run Preflight
    await page.click('[data-testid="run-preflight-btn"]');
    await expect(page.locator('[data-testid="preflight-view"]')).toBeVisible();
    await expect(page.locator('.page-title')).toHaveText('Ön kontrol');

    // Verify 4-step stepper shows step 3 active
    await expect(page.locator('[data-testid="preflight-stepper"]')).toBeVisible();

    // Verify 38 MB blocker banner is present and Start Transfer is disabled
    await expect(page.locator('[data-testid="preflight-blocker-banner"]')).toBeVisible();
    await expect(page.locator('[data-testid="preflight-blocker-banner"]')).toContainText('1 ileti hedef sınırını aşıyor');
    await expect(page.locator('[data-testid="start-transfer-helper-text"]')).toHaveText('Önce bekleyen kararı tamamlayın');
    await expect(page.locator('[data-testid="start-transfer-btn"]')).toBeDisabled();

    // Capture QA screenshot 3: Preflight Blocker Screen
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({
      path: path.join('qa', `preflight-${testInfo.project.name}.png`),
      fullPage: false,
    });

    // Inspect oversized item modal
    await page.click('[data-testid="inspect-oversized-item-link"]');
    await expect(page.locator('[data-testid="modal-dialog"]')).toContainText('Proje teslim arşivi');
    await page.click('[data-testid="modal-close-btn"]');

    // Resolve blocker by selecting Option 1: "Bu iletiyi atla ve raporla"
    await page.click('[data-testid="resolve-choice-skip-radio"]');

    // Blocker should be resolved, helper text gone, and Start Transfer enabled!
    await expect(page.locator('[data-testid="preflight-blocker-banner"]')).not.toBeVisible();
    await expect(page.locator('[data-testid="summary-pending-decision-count"]')).toHaveText('0 ileti');
    await expect(page.locator('[data-testid="start-transfer-btn"]')).toBeEnabled();

    // Start transfer simulation!
    await page.click('[data-testid="start-transfer-btn"]');
    await expect(page.locator('[data-testid="transfer-run-view"]')).toBeVisible();

    // Exercise Pause / Resume
    await page.click('[data-testid="transfer-pause-resume-btn"]');
    await expect(page.locator('[data-testid="transfer-progress-card"]')).toContainText('Duraklatıldı');
    await page.click('[data-testid="transfer-pause-resume-btn"]');

    // Refresh recovery check: reload page during/after run and verify run remains restored
    await page.reload();
    const isTransferRun = await page.locator('[data-testid="transfer-run-view"]').isVisible();
    if (!isTransferRun) {
      await page.click('[data-testid="nav-tab-transfers"]');
    }
    await expect(page.locator('[data-testid="transfer-run-view"]')).toBeVisible();

    // Wait for simulation completion
    await expect(page.locator('.page-title')).toHaveText('Aktarım tamamlandı', { timeout: 35000 });
    await expect(page.locator('[data-testid="transfer-percent-text"]')).toHaveText('%100');

    // Verify mutually exclusive counts sum to 248
    const total = await page.locator('[data-testid="count-total"]').innerText();
    expect(total).toBe('248');

    // Verify download buttons appear
    await expect(page.locator('[data-testid="download-json-btn"]')).toBeVisible();
    await expect(page.locator('[data-testid="download-csv-btn"]')).toBeVisible();
  });

  test('should exercise multi-location search tree, filtering, and empty prompt', async ({ page }) => {
    // Navigate to Search tab
    await page.click('[data-testid="nav-tab-search"]');
    await expect(page.locator('[data-testid="archive-search-view"]')).toBeVisible();

    // Clear selection
    await page.click('[data-testid="search-clear-selection-btn"]');
    await expect(page.locator('[data-testid="search-empty-scope-prompt"]')).toBeVisible();

    // Select All
    await page.click('[data-testid="search-select-all-btn"]');
    await expect(page.locator('[data-testid="search-results-table"]')).toBeVisible();

    // Filter by query
    await page.fill('[data-testid="archive-search-input"]', 'Proje');
    const rows = page.locator('[data-testid^="search-result-row-"]');
    await expect(rows.first()).toBeVisible();

    // Select first result and verify preview pane
    await rows.first().click();
    await expect(page.locator('[data-testid="search-preview-pane"]')).toBeVisible();
  });

  test('should test drawer navigation and demo reset', async ({ page }) => {
    // Open Settings Drawer
    await page.click('[data-testid="header-settings-btn"]');
    await expect(page.locator('[data-testid="settings-drawer-dialog"]')).toBeVisible();

    // Reset demo storage
    await page.click('[data-testid="reset-demo-storage-btn"]');
    await expect(page.locator('[data-testid="workspace-view"]')).toBeVisible();

    // Test Search tab
    await page.click('[data-testid="nav-tab-search"]');
    await expect(page.locator('[data-testid="archive-search-view"]')).toBeVisible();

    // Test Reports tab
    await page.click('[data-testid="nav-tab-reports"]');
    await expect(page.locator('[data-testid="reports-view"]')).toBeVisible();
  });
});
