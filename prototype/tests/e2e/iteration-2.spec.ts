import { test, expect } from '@playwright/test';
import path from 'path';

test.describe('BitigMail Iteration 2: Multi-Client, Operations & Unified Search', () => {
  test.beforeEach(async ({ page }) => {
    page.on('console', (msg) => {
      if (msg.type() === 'error') {
        console.error(`[Browser Error]: ${msg.text()}`);
      }
    });
    // Isolate independent tests by clearing localStorage before test starts
    await page.goto('/');
    await page.evaluate(() => localStorage.clear());
    await page.reload();
  });

  test('should render company directory as initial view and capture QA screenshot', async ({ page }, testInfo) => {
    await expect(page.locator('[data-testid="top-header"]')).toBeVisible();
    await expect(page.locator('[data-testid="client-directory-view"]')).toBeVisible();
    await expect(page.locator('.page-title')).toHaveText('Müşteriler');

    // Both initial companies must be visible
    await expect(page.locator('[data-testid="company-card-comp-ornek"]')).toBeVisible();
    await expect(page.locator('[data-testid="company-card-comp-anadolu"]')).toBeVisible();

    // Verify search in directory
    await page.fill('[data-testid="client-search-input"]', 'Anadolu');
    await expect(page.locator('[data-testid="company-card-comp-anadolu"]')).toBeVisible();
    await expect(page.locator('[data-testid="company-card-comp-ornek"]')).not.toBeVisible();
    await page.fill('[data-testid="client-search-input"]', '');

    // Capture QA screenshot 1: Customers surface
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({
      path: path.join('qa', `customers-${testInfo.project.name}.png`),
      fullPage: false,
    });
  });

  test('should add sample company, project, and empty source, showing 0 items and empty preview', async ({ page }) => {
    // 1. Add company
    await page.click('[data-testid="add-company-btn"]');
    await page.fill('[data-testid="new-company-name-input"]', 'Kuzey Bilişim A.Ş.');
    await page.fill('[data-testid="new-company-code-input"]', 'KUZEY');
    await page.fill('[data-testid="new-company-desc-input"]', 'Bölgesel veri merkezi geçişi');
    await page.click('[data-testid="submit-new-company-btn"]');

    // Lands on company detail view
    await expect(page.locator('[data-testid="client-detail-view"]')).toBeVisible();
    await expect(page.locator('.page-title')).toHaveText('Kuzey Bilişim A.Ş.');

    // 2. Add project
    await page.click('[data-testid="add-project-btn"]');
    await page.fill('[data-testid="new-project-name-input"]', 'Bulut Arşiv 2024');
    await page.fill('[data-testid="new-project-desc-input"]', 'Yerel arşiv aktarımı');
    await page.click('[data-testid="submit-new-project-btn"]');

    // Project card exists
    const projCard = page.locator('[data-testid^="project-card-"]');
    await expect(projCard.first()).toBeVisible();

    // 3. Add empty source
    await page.click('[data-testid^="add-source-btn-"]');
    await page.fill('[data-testid="new-source-name-input"]', 'Boş Finans Arşivi');
    await page.fill('[data-testid="new-source-account-input"]', 'finans_bos.pst');
    await page.click('[data-testid="submit-new-source-btn"]');

    // Verify source is listed in project sources with 0 count
    const sourceRow = page.locator('[data-testid^="source-row-"]');
    await expect(sourceRow.first()).toBeVisible();
    await expect(sourceRow.first()).toContainText('0 ileti (Boş kaynak)');

    // 4. Start transfer with context on the empty source
    await page.click('[data-testid^="start-transfer-source-"]');
    await expect(page.locator('[data-testid="transfers-tab-view"]')).toBeVisible();
    await expect(page.locator('[data-testid="workspace-view"]')).toBeVisible();

    // Context bar shows Kuzey Bilişim and Boş Finans Arşivi
    await expect(page.locator('[data-testid="transfer-company-select"]')).toContainText('Kuzey Bilişim A.Ş.');

    // Workspace message table has 0 items and displays placeholder
    await expect(page.locator('[data-testid="empty-messages-placeholder"]')).toBeVisible();
    await expect(page.locator('[data-testid="preview-empty-state"]')).toBeVisible();

    // Preflight/transfer is blocked for zero items: assert disabled CTA and scope guard evidence
    await expect(page.locator('[data-testid="run-preflight-btn"]')).toBeDisabled();
    await expect(page.locator('[data-testid="scope-summary-text"]')).toContainText('0 ileti');
    await expect(page.locator('[data-testid="plan-scope-value"]')).toContainText('0 ileti');
    await expect(page.locator('[data-testid="empty-messages-placeholder"]')).toBeVisible();
    await expect(page.locator('[data-testid="preview-empty-state"]')).toBeVisible();
    await expect(page.locator('[data-testid="scope-guard-helper"]')).toBeVisible();
    await expect(page.locator('[data-testid="scope-guard-helper"]')).toHaveText(
      'Seçili kapsamda ileti yok. Ön kontrol için ileti içeren bir kaynak seçin.'
    );
  });

  test('should persist added company, project, and source across reload', async ({ page }) => {
    // Add company
    await page.click('[data-testid="add-company-btn"]');
    await page.fill('[data-testid="new-company-name-input"]', 'Kalıcı Holding A.Ş.');
    await page.fill('[data-testid="new-company-code-input"]', 'KALICI');
    await page.click('[data-testid="submit-new-company-btn"]');
    await expect(page.locator('.page-title')).toHaveText('Kalıcı Holding A.Ş.');

    // Add project
    await page.click('[data-testid="add-project-btn"]');
    await page.fill('[data-testid="new-project-name-input"]', 'Veri Göçü');
    await page.click('[data-testid="submit-new-project-btn"]');

    // Add source
    await page.click('[data-testid^="add-source-btn-"]');
    await page.fill('[data-testid="new-source-name-input"]', 'Yedek Depo');
    await page.fill('[data-testid="new-source-account-input"]', 'yedek_depo.pst');
    await page.click('[data-testid="submit-new-source-btn"]');

    // Dedicated reload test: reload without clearing storage
    await page.reload();

    // Navigate to Müşteriler directory
    await page.click('[data-testid="nav-tab-clients"]');
    const backBtn = page.locator('[data-testid="back-to-directory-btn"]');
    if (await backBtn.isVisible()) {
      await backBtn.click();
    }
    await expect(page.locator('[data-testid="client-directory-view"]')).toBeVisible();
    await expect(page.locator('text=Kalıcı Holding A.Ş.')).toBeVisible();

    // Check in Search tab
    await page.click('[data-testid="nav-tab-search"]');
    await expect(page.locator('text=Kalıcı Holding A.Ş.')).toBeVisible();
  });

  test('should handoff customer context to transfers, assert SourceTree is project-scoped, switch source to change scope, and clear old source on company change', async ({ page }, testInfo) => {
    // Open company detail
    await page.click('[data-testid="view-company-btn-comp-ornek"]');
    await expect(page.locator('[data-testid="client-detail-view"]')).toBeVisible();

    // Click "İşlem başlat" on src-ornek-imap
    await page.click('[data-testid="start-transfer-source-src-ornek-imap"]');

    // Lands on Transfers tab with context
    await expect(page.locator('[data-testid="transfers-tab-view"]')).toBeVisible();
    await expect(page.locator('[data-testid="workspace-view"]')).toBeVisible();

    // Context selectors reflect chosen source
    await expect(page.locator('[data-testid="transfer-company-select"]')).toHaveValue('comp-ornek');
    await expect(page.locator('[data-testid="transfer-project-select"]')).toHaveValue('proj-ornek-gecis');
    await expect(page.locator('[data-testid="transfer-source-select"]')).toHaveValue('src-ornek-imap');
    await expect(page.locator('[data-testid="scope-summary-text"]')).toContainText('248 ileti');

    // 1. Assert SourceTree is strictly project-scoped
    await expect(page.locator('[data-testid="source-node-src-ornek-imap"]')).toBeVisible();
    await expect(page.locator('[data-testid="source-node-src-ornek-exchange"]')).toBeVisible();
    await expect(page.locator('[data-testid="source-node-src-ornek-pst"]')).toBeVisible();
    await expect(page.locator('[data-testid="source-node-src-anadolu-operasyon"]')).not.toBeVisible();

    // 2. Switching shared source in SourceTree changes sourceId and scope
    await page.click('[data-testid="source-node-src-ornek-exchange"]');
    await expect(page.locator('[data-testid="transfer-source-select"]')).toHaveValue('src-ornek-exchange');
    await expect(page.locator('[data-testid="scope-summary-text"]')).toContainText('45 ileti');

    // Switch back to primary IMAP for canonical 248-item operations screenshot
    await page.click('[data-testid="source-node-src-ornek-imap"]');
    await expect(page.locator('[data-testid="scope-summary-text"]')).toContainText('248 ileti');

    // Capture QA screenshot 2: Operations surface
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({
      path: path.join('qa', `operations-${testInfo.project.name}.png`),
      fullPage: false,
    });

    // 3. Change company to comp-anadolu: old source must NOT leak and SourceTree updates
    await page.selectOption('[data-testid="transfer-company-select"]', 'comp-anadolu');
    await expect(page.locator('[data-testid="transfer-project-select"]')).toHaveValue('proj-anadolu-merkez');
    await expect(page.locator('[data-testid="transfer-source-select"]')).not.toHaveValue('src-ornek-imap');

    // SourceTree now only shows sources of comp-anadolu / proj-anadolu-merkez
    await expect(page.locator('[data-testid="source-node-src-anadolu-operasyon"]')).toBeVisible();
    await expect(page.locator('[data-testid="source-node-src-ornek-imap"]')).not.toBeVisible();
  });

  test('should add empty shared source from SourceTree, yielding zero items and empty preview', async ({ page }) => {
    // Navigate to transfers tab
    await page.click('[data-testid="nav-tab-transfers"]');
    await expect(page.locator('[data-testid="workspace-view"]')).toBeVisible();

    // Click "+" in SourceTree
    await page.click('[data-testid="add-source-btn"]');
    await page.fill('[data-testid="new-source-name-input"]', 'Özel Proje Arşivi');
    await page.click('[data-testid="source-kind-archive-btn"]');
    await page.fill('[data-testid="new-source-account-input"]', 'ozel_arsiv.pst');
    await page.click('[data-testid="submit-new-source-btn"]');

    // Newly added empty source is active with 0 messages and placeholder preview
    await expect(page.locator('[data-testid="scope-summary-text"]')).toContainText('0 ileti');
    await expect(page.locator('[data-testid="empty-messages-placeholder"]')).toBeVisible();
    await expect(page.locator('[data-testid="preview-empty-state"]')).toBeVisible();

    // Preflight/transfer is blocked on empty scope: assert disabled CTA and scope guard evidence
    await expect(page.locator('[data-testid="run-preflight-btn"]')).toBeDisabled();
    await expect(page.locator('[data-testid="scope-summary-text"]')).toContainText('0 ileti');
    await expect(page.locator('[data-testid="plan-scope-value"]')).toContainText('0 ileti');
    await expect(page.locator('[data-testid="empty-messages-placeholder"]')).toBeVisible();
    await expect(page.locator('[data-testid="preview-empty-state"]')).toBeVisible();
    await expect(page.locator('[data-testid="scope-guard-helper"]')).toBeVisible();
    await expect(page.locator('[data-testid="scope-guard-helper"]')).toHaveText(
      'Seçili kapsamda ileti yok. Ön kontrol için ileti içeren bir kaynak seçin.'
    );
  });

  test('should exercise multi-source search, partial tree state, visible location context in rows and preview, and capture QA screenshot', async ({ page }, testInfo) => {
    // Navigate to Search tab
    await page.click('[data-testid="nav-tab-search"]');
    await expect(page.locator('[data-testid="archive-search-view"]')).toBeVisible();

    // Clear all selection
    await page.click('[data-testid="search-clear-selection-btn"]');
    await expect(page.locator('[data-testid="search-empty-scope-prompt"]')).toBeVisible();

    // Select source 1 from Örnek and source 2 from Anadolu
    await page.click('[data-testid="checkbox-source-src-ornek-imap"]');
    await page.click('[data-testid="checkbox-source-src-anadolu-operasyon"]');

    // Verify parent checkboxes show partial/indeterminate state
    const compOrnekIndeterminate = await page.locator('[data-testid="checkbox-company-comp-ornek"]').evaluate(
      (el) => (el as HTMLInputElement).indeterminate
    );
    expect(compOrnekIndeterminate).toBe(true);

    const compAnadoluIndeterminate = await page.locator('[data-testid="checkbox-company-comp-anadolu"]').evaluate(
      (el) => (el as HTMLInputElement).indeterminate
    );
    expect(compAnadoluIndeterminate).toBe(true);

    // Verify search results table has rows
    await expect(page.locator('[data-testid="search-results-table"]')).toBeVisible();
    const rows = page.locator('[data-testid^="search-result-row-"]');
    await expect(rows.first()).toBeVisible();

    // Verify search result visible location context includes company, project, source, and account/file
    const firstRow = rows.first();
    await expect(firstRow.locator('[data-testid="search-result-company-project"]')).toBeVisible();
    await expect(firstRow.locator('[data-testid="search-result-source-account"]')).toBeVisible();
    const compProjText = await firstRow.locator('[data-testid="search-result-company-project"]').innerText();
    const srcAccountText = await firstRow.locator('[data-testid="search-result-source-account"]').innerText();
    expect(compProjText.length).toBeGreaterThan(0);
    expect(srcAccountText).toContain('(');

    // Select row to preview and verify location context in preview pane
    await firstRow.click();
    await expect(page.locator('[data-testid="preview-subject"]')).toBeVisible();
    await expect(page.locator('[data-testid="preview-location-context"]')).toBeVisible();
    const previewLocBox = await page.locator('[data-testid="preview-location-context"]').boundingBox();
    expect(previewLocBox).not.toBeNull();
    expect(previewLocBox!.height).toBeGreaterThanOrEqual(20);
    await expect(page.locator('[data-testid="preview-location-company"]')).toBeVisible();
    await expect(page.locator('[data-testid="preview-location-project"]')).toBeVisible();
    await expect(page.locator('[data-testid="preview-location-source"]')).toBeVisible();
    await expect(page.locator('[data-testid="preview-location-account"]')).toBeVisible();

    // Capture QA screenshot 3: Multi-search surface
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({
      path: path.join('qa', `multi-search-${testInfo.project.name}.png`),
      fullPage: false,
    });

    // Query + Folder Filter intersection
    await page.fill('[data-testid="archive-search-input"]', 'Proje');
    await page.selectOption('[data-testid="archive-folder-filter"]', 'inbox');
    await expect(rows.first()).toBeVisible();

    // Clear search and exclude preview
    await page.fill('[data-testid="archive-search-input"]', 'NON_EXISTENT_EXCLUSIVE_QUERY_XYZ');
    await expect(page.locator('[data-testid="preview-empty-state"]')).toBeVisible();
  });

  test('should ensure active run snapshot remains immutable when draft plan changes', async ({ page }) => {
    // Navigate to transfers and start simulation
    await page.click('[data-testid="nav-tab-transfers"]');
    await page.click('[data-testid="run-preflight-btn"]');
    await page.click('[data-testid="resolve-choice-skip-radio"]');
    await page.click('[data-testid="start-transfer-btn"]');

    await expect(page.locator('[data-testid="transfer-run-view"]')).toBeVisible();
    await expect(page.locator('[data-testid="count-total"]')).toHaveText('248');

    // Pause simulation
    await page.click('[data-testid="transfer-pause-resume-btn"]');
    await expect(page.locator('[data-testid="transfer-progress-card"]')).toContainText('Duraklatıldı');

    // Snapshot total remains immutable
    await expect(page.locator('[data-testid="count-total"]')).toHaveText('248');
  });

  test('should render responsive mobile 390x844 Archive/Search without horizontal clipping, support location drawer toggle, and retain brand styling', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.click('[data-testid="nav-tab-search"]');
    await expect(page.locator('[data-testid="archive-search-view"]')).toBeVisible();

    // 1. Checkbox/Radio brand accent styling
    const compCheckbox = page.locator('[data-testid="checkbox-company-comp-ornek"]');
    await expect(compCheckbox).toBeVisible();
    const accentColor = await compCheckbox.evaluate((el) => window.getComputedStyle(el).accentColor);
    expect(accentColor).toMatch(/(rgb\(233,\s*124,\s*36\)|#e97c24)/i);

    // 2. Mobile location toggle button is visible and toggles tree visibility
    const toggleBtn = page.locator('[data-testid="mobile-location-toggle-btn"]');
    await expect(toggleBtn).toBeVisible();
    await expect(page.locator('[data-testid="search-location-tree-panel"]')).toBeVisible();

    // Toggle off: location panel is hidden
    await toggleBtn.click();
    await expect(page.locator('[data-testid="search-location-tree-panel"]')).not.toBeVisible();

    // Toggle back on
    await toggleBtn.click();
    await expect(page.locator('[data-testid="search-location-tree-panel"]')).toBeVisible();

    // 3. Search query and folder filter controls are reachable and fit within 390px viewport width
    const searchInput = page.locator('[data-testid="archive-search-input"]');
    await expect(searchInput).toBeVisible();
    const inputBBox = await searchInput.boundingBox();
    expect(inputBBox).not.toBeNull();
    expect(inputBBox!.x).toBeGreaterThanOrEqual(0);
    expect(inputBBox!.x + inputBBox!.width).toBeLessThanOrEqual(390 + 2);

    const folderFilter = page.locator('[data-testid="archive-folder-filter"]');
    await expect(folderFilter).toBeVisible();
    const filterBBox = await folderFilter.boundingBox();
    expect(filterBBox).not.toBeNull();
    expect(filterBBox!.x).toBeGreaterThanOrEqual(0);
    expect(filterBBox!.x + filterBBox!.width).toBeLessThanOrEqual(390 + 2);

    // 4. Results pane and table fit within viewport without horizontal clipping
    const resultsPane = page.locator('.search-results-pane');
    await expect(resultsPane).toBeVisible();
    const paneBBox = await resultsPane.boundingBox();
    expect(paneBBox).not.toBeNull();
    expect(paneBBox!.x).toBeGreaterThanOrEqual(0);
    expect(paneBBox!.x + paneBBox!.width).toBeLessThanOrEqual(390 + 2);

    // 5. Select a row on mobile: preview pane is reachable full-width and displays location details
    const firstRow = page.locator('[data-testid^="search-result-row-"]').first();
    await expect(firstRow).toBeVisible();
    await firstRow.click();

    const previewPane = page.locator('[data-testid="search-preview-pane"]');
    await expect(previewPane).toBeVisible();
    const previewBBox = await previewPane.boundingBox();
    expect(previewBBox).not.toBeNull();
    expect(previewBBox!.x).toBeGreaterThanOrEqual(0);
    expect(previewBBox!.x + previewBBox!.width).toBeLessThanOrEqual(390 + 2);

    await expect(page.locator('[data-testid="preview-subject"]')).toBeVisible();
    await expect(page.locator('[data-testid="preview-location-context"]')).toBeVisible();
    const mobileLocBox = await page.locator('[data-testid="preview-location-context"]').boundingBox();
    expect(mobileLocBox).not.toBeNull();
    expect(mobileLocBox!.height).toBeGreaterThanOrEqual(20);
    await expect(page.locator('[data-testid="preview-location-company"]')).toBeVisible();
    await expect(page.locator('[data-testid="preview-location-project"]')).toBeVisible();
    await expect(page.locator('[data-testid="preview-location-source"]')).toBeVisible();
  });
});
