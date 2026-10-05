import { test, expect } from '@playwright/test';

test.describe('Local PST Split & Archive Workflow E2E (Mocked Engine)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.evaluate(() => localStorage.clear());
    await page.reload();
    await page.getByTestId('nav-tab-transfers').click();
  });

  test('switches to Archive operation mode and displays genuine local split workflow UI', async ({ page }) => {
    // Click 'Arşivleme' operation tab
    await page.getByTestId('op-tab-archive').click();

    // Verify local archive workflow container is rendered
    const workflow = page.getByTestId('local-archive-workflow');
    await expect(workflow).toBeVisible();

    // Verify heading is 'Arşivleme ve bölümleme'
    await expect(page.getByRole('heading', { name: 'Arşivleme ve bölümleme' })).toBeVisible();

    // Verify demo source and target dropdowns are hidden in archive mode
    await expect(page.getByTestId('transfer-source-select')).toHaveCount(0);
    await expect(page.getByTestId('transfer-target-select')).toHaveCount(0);

    // Verify customer and project selectors remain visible
    await expect(page.getByTestId('transfer-company-select')).toBeVisible();
    await expect(page.getByTestId('transfer-project-select')).toBeVisible();

    // Verify Source selection card is present with PST / OST pick button
    await expect(page.getByTestId('source-selection-card')).toBeVisible();
    await expect(page.getByTestId('pick-source-btn')).toBeVisible();
    await expect(page.getByTestId('pick-source-btn')).toHaveText('PST veya OST dosyası seç');
  });

  test('displays service offline banner when local engine is unreachable', async ({ page }) => {
    await page.route('http://127.0.0.1:6174/**', route => route.abort('connectionrefused'));
    await page.getByTestId('op-tab-archive').click();

    const offlineBanner = page.getByTestId('service-offline-banner');
    await expect(offlineBanner).toBeVisible();
    await expect(offlineBanner).toContainText('127.0.0.1:6174');
    await expect(offlineBanner).toContainText('start-local-engine.ps1');

    const pickBtn = page.getByTestId('pick-source-btn');
    await expect(pickBtn).toBeDisabled();
  });

  test('completes full local PST split cycle by year with multipart report rendering', async ({ page }) => {
    // Mock endpoints
    await page.route('http://127.0.0.1:6174/api/session', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ token: 'mock_split_token', version: '0.1.0' }),
      });
    });

    await page.route('http://127.0.0.1:6174/api/session/status', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ status: 'ready', hasActiveSession: true }),
      });
    });

    await page.route('http://127.0.0.1:6174/api/jobs', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([]),
      });
    });

    await page.route('http://127.0.0.1:6174/api/picker/split-source', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          cancelled: false,
          handle: 'src_split_handle_1',
          fileName: 'genuine-full-converted-04.pst',
          displayPath: 'C:\\Fixtures\\genuine-full-converted-04.pst',
          sizeBytes: 16777216,
        }),
      });
    });

    await page.route('http://127.0.0.1:6174/api/split/source/analyze', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          sourceFileName: 'genuine-full-converted-04.pst',
          sourceSizeBytes: 16777216,
          sourceSha256: 'sha256_mock_full_pst',
          formatInfo: {
            isValidOutlookStorage: true,
            isOstSignature: false,
            isPstSignature: true,
            formatName: 'Outlook Personal Storage (.pst, !BDN)',
            authoritativeRuntimeValidation: 'PASS',
          },
          totalFolders: 4,
          activeFoldersCount: 3,
          emptyFoldersCount: 1,
          systemFoldersCount: 0,
          totalItems: 13,
          totalAttachments: 4,
          folders: [
            { folderId: 'fld_inbox', folderPath: 'IPM_SUBTREE/Gelen Kutusu', displayName: 'Gelen Kutusu', itemCount: 6, subFolderCount: 0, category: 'Active', isIpmFolder: true },
            { folderId: 'fld_sent', folderPath: 'IPM_SUBTREE/Gönderilenler', displayName: 'Gönderilenler', itemCount: 3, subFolderCount: 0, category: 'Active', isIpmFolder: true },
            { folderId: 'fld_istanbul', folderPath: 'IPM_SUBTREE/Projeler/İstanbul', displayName: 'İstanbul', itemCount: 4, subFolderCount: 0, category: 'Active', isIpmFolder: true },
          ],
          sampleMessages: [],
          preflight: {
            canConvert: true,
            hasTrialBlocker: false,
            trialBlockerReason: null,
            blockers: [],
            warnings: [],
            estimatedPstSizeBytes: 16777216,
            availableDiskSizeBytes: 1000000000,
          },
        }),
      });
    });

    await page.route('http://127.0.0.1:6174/api/source/selection/preview', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          selectionId: 'sel_split_preview_1',
          sourceHandle: 'src_split_handle_1',
          sourceSha256: 'sha256_mock_full_pst',
          filters: {
            folderIds: ['fld_inbox', 'fld_sent', 'fld_istanbul'],
            startDate: null,
            endDate: null,
            timeZone: 'Europe/Istanbul (UTC+03:00)',
            datePolicy: 'SubmissionDateThenDeliveryDate_UtcPlus3_Inclusive',
          },
          totalSourceMessages: 13,
          selectedMessagesCount: 13,
          excludedMessagesCount: 0,
          missingDateExcludedCount: 0,
          selectedAttachmentsCount: 4,
          folderBreakdown: [],
          canConvert: true,
          blockerReason: null,
        }),
      });
    });

    await page.route('http://127.0.0.1:6174/api/split/plan', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          planId: 'plan_year_test_1',
          sourceHandle: 'src_split_handle_1',
          sourceSha256: 'sha256_mock_full_pst',
          selectionId: 'sel_split_preview_1',
          splitMode: 'year',
          sizeCapBytes: null,
          totalSourceMessages: 13,
          selectedMessagesCount: 13,
          excludedMessagesCount: 0,
          selectedAttachmentsCount: 4,
          yearGroups: [
            { year: '2022', messageCount: 1, attachmentCount: 0, targetFileName: 'arsiv-2022.pst' },
            { year: '2023', messageCount: 2, attachmentCount: 1, targetFileName: 'arsiv-2023.pst' },
            { year: '2024', messageCount: 8, attachmentCount: 3, targetFileName: 'arsiv-2024.pst' },
            { year: '2025', messageCount: 1, attachmentCount: 0, targetFileName: 'arsiv-2025.pst' },
            { year: '2026', messageCount: 1, attachmentCount: 0, targetFileName: 'arsiv-2026.pst' },
          ],
          canSplit: true,
          blockerReason: null,
        }),
      });
    });

    await page.route('http://127.0.0.1:6174/api/picker/output-dir', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          cancelled: false,
          handle: 'outdir_handle_1',
          fileName: 'arsiv-cikti',
          displayPath: 'C:\\Output\\arsiv-cikti',
        }),
      });
    });

    await page.route('http://127.0.0.1:6174/api/split/start', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          jobId: 'split-job-001',
          jobKind: 'split',
          splitMode: 'year',
          sourceFileName: 'genuine-full-converted-04.pst',
          targetFileName: 'arsiv-cikti',
          outputDirectoryPath: 'C:\\Output\\arsiv-cikti\\arsiv-bundle-2024',
          status: 'converting',
          stage: 'Bölümleniyor',
          itemsRead: 13,
          itemsWritten: 13,
          failedItems: 0,
          totalItems: 13,
          currentFolder: 'Tamamlandı',
          percentComplete: 100,
          createdAt: new Date().toISOString(),
          clientContext: {
            companyId: 'comp-1',
            companyName: 'Örnek Müşteri',
            projectId: 'proj-1',
            projectName: 'Örnek Proje',
          },
        }),
      });
    });

    await page.route('http://127.0.0.1:6174/api/jobs/split-job-001', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          jobId: 'split-job-001',
          jobKind: 'split',
          splitMode: 'year',
          sourceFileName: 'genuine-full-converted-04.pst',
          targetFileName: 'arsiv-cikti',
          outputDirectoryPath: 'C:\\Output\\arsiv-cikti\\arsiv-bundle-2024',
          status: 'completed',
          stage: 'Tamamlandı',
          itemsRead: 13,
          itemsWritten: 13,
          failedItems: 0,
          totalItems: 13,
          currentFolder: 'Tamamlandı',
          percentComplete: 100,
          createdAt: new Date().toISOString(),
          completedAt: new Date().toISOString(),
          clientContext: {
            companyId: 'comp-1',
            companyName: 'Örnek Müşteri',
            projectId: 'proj-1',
            projectName: 'Örnek Proje',
          },
        }),
      });
    });

    await page.route('http://127.0.0.1:6174/api/jobs/split-job-001/report', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          jobId: 'split-job-001',
          jobKind: 'split',
          splitMode: 'year',
          sourceFileName: 'genuine-full-converted-04.pst',
          sourceSizeBytes: 16777216,
          sourceSha256Before: 'sha256_mock_full_pst',
          sourceSha256After: 'sha256_mock_full_pst',
          sourceHashMatch: true,
          outputDirectoryPath: 'C:\\Output\\arsiv-cikti\\arsiv-bundle-2024',
          conversionSuccess: true,
          itemsRead: 13,
          itemsWritten: 13,
          failedItems: 0,
          elapsedMilliseconds: 450,
          totalFoldersProcessed: 3,
          fidelityStatus: 'PASS',
          overallStatus: 'SUCCESS',
          isFiltered: true,
          totalSourceMessages: 13,
          selectedMessagesCount: 13,
          excludedMessagesCount: 0,
          missingDateExcludedCount: 0,
          selectedAttachmentsCount: 4,
          clientContext: {
            companyId: 'comp-1',
            companyName: 'Örnek Müşteri',
            projectId: 'proj-1',
            projectName: 'Örnek Proje',
          },
          reopenedPstVerification: {
            verifiedWith: 'Aspose.Email 24.8',
            totalFoldersFound: 3,
            totalPhysicalItemsFound: 13,
            itemCountMatch: true,
            totalAttachmentsVerified: 4,
            totalCidVerified: 1,
            verificationNotes: [],
          },
          parts: [
            {
              partFileName: 'arsiv-2022.pst',
              partFullPath: 'C:\\Output\\arsiv-cikti\\arsiv-bundle-2024\\arsiv-2022.pst',
              partSizeBytes: 524288,
              partSha256: 'sha_part_2022',
              itemsWritten: 1,
              totalAttachmentsVerified: 0,
              totalCidVerified: 0,
              groupKey: '2022',
              reopenedPstVerification: {
                verifiedWith: 'Aspose.Email 24.8',
                totalFoldersFound: 1,
                totalPhysicalItemsFound: 1,
                itemCountMatch: true,
                totalAttachmentsVerified: 0,
                totalCidVerified: 0,
                verificationNotes: [],
              },
            },
            {
              partFileName: 'arsiv-2023.pst',
              partFullPath: 'C:\\Output\\arsiv-cikti\\arsiv-bundle-2024\\arsiv-2023.pst',
              partSizeBytes: 1048576,
              partSha256: 'sha_part_2023',
              itemsWritten: 2,
              totalAttachmentsVerified: 1,
              totalCidVerified: 0,
              groupKey: '2023',
              reopenedPstVerification: {
                verifiedWith: 'Aspose.Email 24.8',
                totalFoldersFound: 2,
                totalPhysicalItemsFound: 2,
                itemCountMatch: true,
                totalAttachmentsVerified: 1,
                totalCidVerified: 0,
                verificationNotes: [],
              },
            },
            {
              partFileName: 'arsiv-2024.pst',
              partFullPath: 'C:\\Output\\arsiv-cikti\\arsiv-bundle-2024\\arsiv-2024.pst',
              partSizeBytes: 4194304,
              partSha256: 'sha_part_2024',
              itemsWritten: 8,
              totalAttachmentsVerified: 3,
              totalCidVerified: 1,
              groupKey: '2024',
              reopenedPstVerification: {
                verifiedWith: 'Aspose.Email 24.8',
                totalFoldersFound: 3,
                totalPhysicalItemsFound: 8,
                itemCountMatch: true,
                totalAttachmentsVerified: 3,
                totalCidVerified: 1,
                verificationNotes: [],
              },
            },
            {
              partFileName: 'arsiv-2025.pst',
              partFullPath: 'C:\\Output\\arsiv-cikti\\arsiv-bundle-2024\\arsiv-2025.pst',
              partSizeBytes: 524288,
              partSha256: 'sha_part_2025',
              itemsWritten: 1,
              totalAttachmentsVerified: 0,
              totalCidVerified: 0,
              groupKey: '2025',
              reopenedPstVerification: {
                verifiedWith: 'Aspose.Email 24.8',
                totalFoldersFound: 1,
                totalPhysicalItemsFound: 1,
                itemCountMatch: true,
                totalAttachmentsVerified: 0,
                totalCidVerified: 0,
                verificationNotes: [],
              },
            },
            {
              partFileName: 'arsiv-2026.pst',
              partFullPath: 'C:\\Output\\arsiv-cikti\\arsiv-bundle-2024\\arsiv-2026.pst',
              partSizeBytes: 524288,
              partSha256: 'sha_part_2026',
              itemsWritten: 1,
              totalAttachmentsVerified: 0,
              totalCidVerified: 0,
              groupKey: '2026',
              reopenedPstVerification: {
                verifiedWith: 'Aspose.Email 24.8',
                totalFoldersFound: 1,
                totalPhysicalItemsFound: 1,
                itemCountMatch: true,
                totalAttachmentsVerified: 0,
                totalCidVerified: 0,
                verificationNotes: [],
              },
            },
          ],
        }),
      });
    });

    // 1. Enter Archive mode
    await page.getByTestId('op-tab-archive').click();

    // 2. Pick Split Source
    await page.getByTestId('pick-source-btn').click();
    await expect(page.getByTestId('source-filename')).toHaveText('genuine-full-converted-04.pst');
    await expect(page.getByTestId('source-display-path')).toHaveText('C:\\Fixtures\\genuine-full-converted-04.pst');
    await expect(page.getByTestId('analysis-preflight-card')).toBeVisible();

    // 3. Selection preview card
    await expect(page.getByTestId('selection-preview-card')).toBeVisible();
    await expect(page.getByTestId('preview-selected-count')).toHaveText('13');

    // 4. Split options & year groups plan table
    await expect(page.getByTestId('split-options-card')).toBeVisible();
    await expect(page.getByTestId('split-year-groups-table')).toBeVisible();
    await expect(page.locator('text=arsiv-2024.pst')).toBeVisible();

    // 5. Pick Output Directory
    await page.getByTestId('pick-output-dir-btn').click();
    await expect(page.getByTestId('output-dir-path')).toHaveText('C:\\Output\\arsiv-cikti');

    // 6. Start Split
    const startBtn = page.getByTestId('start-split-btn');
    await expect(startBtn).toBeEnabled();
    await startBtn.click();

    // 7. Verify Multipart Report Card
    const reportCard = page.getByTestId('conversion-report-card');
    await expect(reportCard).toBeVisible({ timeout: 10000 });
    await expect(page.getByTestId('output-location-path')).toHaveText('C:\\Output\\arsiv-cikti\\arsiv-bundle-2024');

    // Verify 5 parts are in the table
    await expect(page.getByTestId('split-parts-table')).toBeVisible();
    await expect(page.getByTestId('split-part-row-0')).toContainText('arsiv-2022.pst');
    await expect(page.getByTestId('split-part-row-2')).toContainText('arsiv-2024.pst');

    // Verify copy output location button
    const copyLocBtn = page.getByTestId('copy-location-btn');
    await expect(copyLocBtn).toBeVisible();
    await copyLocBtn.click();
    await expect(copyLocBtn).toHaveText('Kopyalandı!');
  });

  test('honest size preview: displays dynamic measurement banner and disables start on zero selection', async ({ page }) => {
    await page.route('http://127.0.0.1:6174/api/session', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ token: 'tok_1', version: '0.1.0' }) });
    });
    await page.route('http://127.0.0.1:6174/api/session/status', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ status: 'ready', hasActiveSession: true }) });
    });
    await page.route('http://127.0.0.1:6174/api/jobs', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
    });
    await page.route('http://127.0.0.1:6174/api/picker/split-source', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ cancelled: false, handle: 'src_1', fileName: 'test.pst', displayPath: 'C:\\test.pst', sizeBytes: 5000 }),
      });
    });
    await page.route('http://127.0.0.1:6174/api/split/source/analyze', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          sourceFileName: 'test.pst',
          sourceSizeBytes: 5000,
          sourceSha256: 'hash_test',
          formatInfo: { isValidOutlookStorage: true, isOstSignature: false, isPstSignature: true, formatName: 'PST' },
          totalFolders: 1,
          activeFoldersCount: 1,
          emptyFoldersCount: 0,
          systemFoldersCount: 0,
          totalItems: 5,
          totalAttachments: 1,
          folders: [{ folderId: 'fld_1', folderPath: 'Gelen Kutusu', displayName: 'Gelen Kutusu', itemCount: 5, subFolderCount: 0, category: 'Active', isIpmFolder: true }],
          sampleMessages: [],
          preflight: { canConvert: true, hasTrialBlocker: false, trialBlockerReason: null, blockers: [], warnings: [], estimatedPstSizeBytes: 5000, availableDiskSizeBytes: 100000 },
        }),
      });
    });
    await page.route('http://127.0.0.1:6174/api/source/selection/preview', async (route) => {
      const req = route.request().postDataJSON();
      const count = !req.folderIds || req.folderIds.length === 0 ? 0 : 5;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          selectionId: 'sel_preview_dynamic',
          sourceHandle: 'src_1',
          sourceSha256: 'hash_test',
          filters: { folderIds: req.folderIds, startDate: null, endDate: null, timeZone: 'Europe/Istanbul (UTC+03:00)', datePolicy: 'policy' },
          totalSourceMessages: 5,
          selectedMessagesCount: count,
          excludedMessagesCount: 5 - count,
          missingDateExcludedCount: 0,
          selectedAttachmentsCount: count > 0 ? 1 : 0,
          folderBreakdown: [],
          canConvert: count > 0,
          blockerReason: count === 0 ? 'Seçim kriterlerinize uyan hiçbir ileti bulunamadı. Sıfır iletili bölme başlatılamaz.' : null,
        }),
      });
    });
    await page.route('http://127.0.0.1:6174/api/split/plan', async (route) => {
      const req = route.request().postDataJSON();
      const isYear = req.splitMode === 'year';
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          planId: 'plan_dynamic_1',
          sourceHandle: 'src_1',
          sourceSha256: 'hash_test',
          selectionId: 'sel_preview_dynamic',
          splitMode: req.splitMode,
          sizeCapBytes: req.sizeCapBytes,
          totalSourceMessages: 5,
          selectedMessagesCount: 5,
          excludedMessagesCount: 0,
          selectedAttachmentsCount: 1,
          yearGroups: isYear ? [{ year: '2024', messageCount: 5, attachmentCount: 1, targetFileName: 'arsiv-2024.pst' }] : [],
          canSplit: true,
          blockerReason: null,
        }),
      });
    });
    await page.route('http://127.0.0.1:6174/api/picker/output-dir', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ cancelled: false, handle: 'outdir_1', fileName: 'out', displayPath: 'C:\\out' }),
      });
    });

    await page.getByTestId('op-tab-archive').click();
    await page.getByTestId('pick-source-btn').click();
    await page.getByTestId('pick-output-dir-btn').click();

    // Switch to Size Mode
    await page.getByTestId('split-mode-size-radio').click();

    // Verify honest size preview note is visible
    const honestNote = page.getByTestId('split-size-honest-preview');
    await expect(honestNote).toBeVisible();
    await expect(honestNote).toContainText('Parça sayısı işlem sırasında belirlenir');

    // Verify size banner mentions dynamic determination
    const sizeBanner = page.getByTestId('split-size-info-banner');
    await expect(sizeBanner).toBeVisible();
    await expect(sizeBanner).toContainText('Parça sayısı işlem sırasında belirlenir');

    // Test zero selection invalidates plan and blocks start
    await page.getByTestId('clear-all-folders-btn').click();
    await expect(page.getByTestId('zero-match-alert')).toBeVisible();
    const startBtn = page.getByTestId('start-split-btn');
    if ((await startBtn.count()) > 0) {
      await expect(startBtn).toBeDisabled();
    }
  });

  test('mobile viewport 390px layout: zero horizontal body overflow in archive workflow', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.getByTestId('op-tab-archive').click();

    const isOverflowing = await page.evaluate(() => {
      return document.documentElement.scrollWidth > window.innerWidth;
    });
    expect(isOverflowing).toBe(false);
  });
});
