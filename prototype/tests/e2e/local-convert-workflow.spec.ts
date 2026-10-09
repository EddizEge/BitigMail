import { test, expect } from '@playwright/test';

test.describe('Local OST Convert Workflow E2E', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.evaluate(() => localStorage.clear());
    await page.reload();
    await page.getByTestId('nav-tab-transfers').click();
  });

  test('switches to Convert operation mode and displays real local workflow UI', async ({ page }) => {
    // Click 'Dönüştürme' operation tab
    await page.getByTestId('op-tab-convert').click();

    // Verify local convert workflow container is rendered
    const workflow = page.getByTestId('local-convert-workflow');
    await expect(workflow).toBeVisible();

    // Verify demo source and target dropdowns are hidden in convert mode
    await expect(page.getByTestId('transfer-source-select')).toHaveCount(0);
    await expect(page.getByTestId('transfer-target-select')).toHaveCount(0);

    // Verify customer and project selectors remain visible in convert mode
    await expect(page.getByTestId('transfer-company-select')).toBeVisible();
    await expect(page.getByTestId('transfer-project-select')).toBeVisible();

    // Verify Source selection card is present
    await expect(page.getByTestId('source-selection-card')).toBeVisible();
    await expect(page.getByTestId('pick-source-btn')).toBeVisible();
  });

  test('displays service offline banner when local engine is unreachable and does not simulate progress', async ({ page }) => {
    await page.route('http://127.0.0.1:6174/**', route => route.abort('connectionrefused'));
    await page.getByTestId('op-tab-convert').click();

    // Simulate the outage explicitly so this test is independent of the user's running service.
    const offlineBanner = page.getByTestId('service-offline-banner');
    await expect(offlineBanner).toBeVisible();
    // Kullanıcıya port veya betik adı değil, uygulamayı yeniden açma yönlendirmesi gösterilir.
    await expect(offlineBanner).toContainText('yeniden açın');
    await expect(offlineBanner).not.toContainText('127.0.0.1');
    await expect(offlineBanner).not.toContainText('.ps1');

    // Button should be disabled or prompt when offline
    const pickBtn = page.getByTestId('pick-source-btn');
    await expect(pickBtn).toBeDisabled();
  });

  test('completes full local OST conversion cycle when API adapter is active', async ({ page }) => {
    // Mock the local engine API responses to verify full client-side state machine
    await page.route('http://127.0.0.1:6174/api/session', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ token: 'mock_token_abc_123', version: '0.1.0' }),
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

    await page.route('http://127.0.0.1:6174/api/picker/source', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          cancelled: false,
          handle: 'src_mock_handle_1',
          fileName: 'bitigmail-lab-full.ost',
          displayPath: 'C:\\Fixtures\\bitigmail-lab-full.ost',
          sizeBytes: 15728640,
        }),
      });
    });

    await page.route('http://127.0.0.1:6174/api/source/analyze', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          sourceFileName: 'bitigmail-lab-full.ost',
          sourceSizeBytes: 15728640,
          sourceSha256: 'b0801758a2e61d4ce6e86799701a81a7a60c38401f73b13c993d94c03a2ee57a',
          formatInfo: {
            isValidOutlookStorage: true,
            isOstSignature: true,
            formatName: 'Outlook Offline Storage (.ost, !BDN)',
            authoritativeRuntimeValidation: 'PASS',
          },
          totalFolders: 6,
          activeFoldersCount: 3,
          emptyFoldersCount: 2,
          systemFoldersCount: 1,
          totalItems: 13,
          totalAttachments: 4,
          folders: [
            { folderId: 'fld_inbox', folderPath: 'IPM_SUBTREE/Gelen Kutusu', displayName: 'Gelen Kutusu', itemCount: 6, subFolderCount: 0, category: 'Active', isIpmFolder: true },
            { folderId: 'fld_sent', folderPath: 'IPM_SUBTREE/Gönderilenler', displayName: 'Gönderilenler', itemCount: 3, subFolderCount: 0, category: 'Active', isIpmFolder: true },
            { folderId: 'fld_istanbul', folderPath: 'IPM_SUBTREE/Projeler/İstanbul', displayName: 'İstanbul', itemCount: 4, subFolderCount: 0, category: 'Active', isIpmFolder: true },
          ],
          sampleMessages: [
            { entryId: 'e1', folderPath: 'IPM_SUBTREE/Gelen Kutusu', subject: 'Kurulum ve Entegrasyon', sender: 'destek@ornek.example', displayTo: 'lab@bitigmail.example', dateUtc: '2024-11-10 10:00:00', hasAttachments: false, attachmentCount: 0 },
            { entryId: 'e2', folderPath: 'IPM_SUBTREE/Projeler/İstanbul', subject: 'Proje Logosu ve Tasarım', sender: 'tasarim@ornek.example', displayTo: 'lab@bitigmail.example', dateUtc: '2024-11-12 14:30:00', hasAttachments: true, attachmentCount: 1 },
          ],
          preflight: {
            canConvert: true,
            hasTrialBlocker: false,
            trialBlockerReason: null,
            blockers: [],
            warnings: [],
            estimatedPstSizeBytes: 18000000,
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
          selectionId: 'sel_mock_preview_1',
          sourceHandle: 'src_mock_handle_1',
          sourceSha256: 'b0801758a2e61d4ce6e86799701a81a7a60c38401f73b13c993d94c03a2ee57a',
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
          folderBreakdown: [
            { folderId: 'fld_inbox', folderPath: 'IPM_SUBTREE/Gelen Kutusu', displayName: 'Gelen Kutusu', totalItems: 6, selectedItems: 6, isSelected: true },
            { folderId: 'fld_sent', folderPath: 'IPM_SUBTREE/Gönderilenler', displayName: 'Gönderilenler', totalItems: 3, selectedItems: 3, isSelected: true },
            { folderId: 'fld_istanbul', folderPath: 'IPM_SUBTREE/Projeler/İstanbul', displayName: 'İstanbul', totalItems: 4, selectedItems: 4, isSelected: true },
          ],
          canConvert: true,
          blockerReason: null,
          blockReason: null,
        }),
      });
    });

    await page.route('http://127.0.0.1:6174/api/picker/target', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          cancelled: false,
          handle: 'tgt_mock_handle_1',
          fileName: 'bitigmail-lab-full-donusturulen.pst',
          displayPath: 'C:\\Output\\bitigmail-lab-full-donusturulen.pst',
        }),
      });
    });

    await page.route('http://127.0.0.1:6174/api/jobs/start', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          jobId: 'job-real-001',
          sourceFileName: 'bitigmail-lab-full.ost',
          targetFileName: 'bitigmail-lab-full-donusturulen.pst',
          outputPath: 'C:\\Output\\bitigmail-lab-full-donusturulen.pst',
          status: 'converting',
          stage: 'Dönüştürülüyor',
          itemsRead: 13,
          itemsWritten: 13,
          failedItems: 0,
          totalItems: 13,
          currentFolder: 'IPM_SUBTREE/Projeler/İstanbul',
          percentComplete: 100,
          createdAt: new Date().toISOString(),
        }),
      });
    });

    await page.route('http://127.0.0.1:6174/api/jobs/job-real-001', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          jobId: 'job-real-001',
          sourceFileName: 'bitigmail-lab-full.ost',
          targetFileName: 'bitigmail-lab-full-donusturulen.pst',
          outputPath: 'C:\\Output\\bitigmail-lab-full-donusturulen.pst',
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
        }),
      });
    });

    await page.route('http://127.0.0.1:6174/api/jobs/job-real-001/report', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          jobId: 'job-real-001',
          evidenceLabel: 'GENUINE_OST_CONVERSION',
          sourceFileName: 'bitigmail-lab-full.ost',
          sourceSizeBytes: 15728640,
          sourceSha256Before: 'b0801758a2e61d4ce6e86799701a81a7a60c38401f73b13c993d94c03a2ee57a',
          sourceSha256After: 'b0801758a2e61d4ce6e86799701a81a7a60c38401f73b13c993d94c03a2ee57a',
          sourceHashMatch: true,
          outputPstFileName: 'bitigmail-lab-full-donusturulen.pst',
          outputPath: 'C:\\Output\\bitigmail-lab-full-donusturulen.pst',
          outputPstSizeBytes: 14680064,
          outputPstSha256: 'mock_output_sha256_pst',
          conversionSuccess: true,
          itemsRead: 13,
          itemsWritten: 13,
          failedItems: 0,
          elapsedMilliseconds: 342,
          totalFoldersProcessed: 3,
          fidelityStatus: 'PASS',
          overallStatus: 'SUCCESS',
          isFiltered: true,
          totalSourceMessages: 13,
          selectedMessagesCount: 13,
          excludedMessagesCount: 0,
          missingDateExcludedCount: 0,
          selectedAttachmentsCount: 4,
          selectionFilter: {
            folderIds: ['fld_inbox', 'fld_sent', 'fld_istanbul'],
            selectedFolders: [
              { folderId: 'fld_inbox', folderPath: 'Gelen Kutusu', displayName: 'Gelen Kutusu' },
              { folderId: 'fld_sent', folderPath: 'Gönderilmiş Öğeler', displayName: 'Gönderilmiş Öğeler' },
              { folderId: 'fld_istanbul', folderPath: 'Gelen Kutusu/Istanbul', displayName: 'Istanbul' },
            ],
            startDate: null,
            endDate: null,
            timeZone: 'Europe/Istanbul (UTC+03:00)',
            datePolicy: 'SubmissionDateThenDeliveryDate_UtcPlus3_Inclusive',
          },
          trialDifferences: {
            hasObservedTrialModifications: false,
            observedModificationsInThisRun: [],
            observedSummary: 'None observed',
            documentedTrialCapabilitiesAndLimits: [],
            watermarkPolicy: 'Preserved without stripping',
          },
          unmeasuredFields: { status: 'UNKNOWN', fields: [], note: 'Recorded as UNKNOWN' },
          reopenedPstVerification: {
            verifiedWith: 'Aspose.Email 24.8',
            totalFoldersFound: 3,
            totalPhysicalItemsFound: 13,
            itemCountMatch: true,
            totalAttachmentsVerified: 4,
            totalCidVerified: 1,
            verificationNotes: [],
          },
          errors: [],
          warnings: [],
        }),
      });
    });

    // 1. Enter Convert mode
    await page.getByTestId('op-tab-convert').click();

    // 2. Select Source
    await page.getByTestId('pick-source-btn').click();

    // 3. Verify Source card, display path, and Analysis preflight findings
    await expect(page.getByTestId('source-filename')).toHaveText('bitigmail-lab-full.ost');
    await expect(page.getByTestId('source-display-path')).toHaveText('C:\\Fixtures\\bitigmail-lab-full.ost');
    await expect(page.getByTestId('analysis-preflight-card')).toBeVisible();
    await expect(page.getByTestId('preflight-success-banner')).toBeVisible();

    // 4. Verify Filter & Selection Preview card
    await expect(page.getByTestId('filter-selection-card')).toBeVisible();
    await expect(page.getByTestId('selection-preview-card')).toBeVisible();
    await expect(page.getByTestId('preview-selected-count')).toHaveText('13');
    await expect(page.getByTestId('preview-attachments-count')).toHaveText('4');

    // 5. Select Target and verify target display path
    await page.getByTestId('pick-target-btn').click();
    await expect(page.getByTestId('target-filename')).toHaveText('bitigmail-lab-full-donusturulen.pst');
    await expect(page.getByTestId('target-display-path')).toHaveText('C:\\Output\\bitigmail-lab-full-donusturulen.pst');

    // 6. Start Conversion
    await page.getByTestId('start-conversion-btn').click();

    // 7. Verify Progress & Report card
    await expect(page.getByTestId('conversion-report-card')).toBeVisible();
    await expect(page.getByTestId('output-location-path')).toHaveText('C:\\Output\\bitigmail-lab-full-donusturulen.pst');
    await expect(page.getByTestId('download-report-btn')).toBeVisible();
    await expect(page.getByTestId('copy-report-btn')).toBeVisible();

    // 8. Verify Report filtered summary
    await expect(page.getByTestId('report-filter-summary')).toBeVisible();
    await expect(page.getByTestId('report-selected-messages')).toHaveText('13 / 13');
    await expect(page.getByTestId('report-selected-folders')).toBeVisible();
    await expect(page.getByTestId('report-selected-folders')).toContainText('Gelen Kutusu, Gönderilmiş Öğeler, Gelen Kutusu/Istanbul');

    // 9. Verify 'Konumu kopyala' button
    const copyLocationBtn = page.getByTestId('copy-location-btn');
    await expect(copyLocationBtn).toBeVisible();
    await expect(copyLocationBtn).toHaveText('Konumu kopyala');
    await copyLocationBtn.click();
    await expect(copyLocationBtn).toHaveText('Kopyalandı!');
  });

  test('selection filters: empty folder selection triggers zero-match alert and disables conversion button', async ({ page }) => {
    await page.route('http://127.0.0.1:6174/api/session', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ token: 'tok_1', version: '0.1.0' }) });
    });
    await page.route('http://127.0.0.1:6174/api/session/status', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ status: 'ready', hasActiveSession: true }) });
    });
    await page.route('http://127.0.0.1:6174/api/jobs', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
    });
    await page.route('http://127.0.0.1:6174/api/picker/source', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          cancelled: false,
          handle: 'src_1',
          fileName: 'test.ost',
          displayPath: 'C:\\test.ost',
          sizeBytes: 1000,
        }),
      });
    });
    await page.route('http://127.0.0.1:6174/api/source/analyze', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          sourceFileName: 'test.ost',
          sourceSizeBytes: 1000,
          sourceSha256: 'hash123',
          formatInfo: { isValidOutlookStorage: true, isOstSignature: true, formatName: 'OST', authoritativeRuntimeValidation: 'PASS' },
          totalFolders: 2,
          activeFoldersCount: 1,
          emptyFoldersCount: 1,
          systemFoldersCount: 0,
          totalItems: 5,
          totalAttachments: 1,
          folders: [
            { folderId: 'fld_1', folderPath: 'Gelen Kutusu', displayName: 'Gelen Kutusu', itemCount: 5, subFolderCount: 0, category: 'Active', isIpmFolder: true },
          ],
          sampleMessages: [],
          preflight: { canConvert: true, hasTrialBlocker: false, trialBlockerReason: null, blockers: [], warnings: [], estimatedPstSizeBytes: 1000, availableDiskSizeBytes: 100000 },
        }),
      });
    });
    await page.route('http://127.0.0.1:6174/api/picker/target', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ cancelled: false, handle: 'tgt_1', fileName: 'out.pst', displayPath: 'C:\\out.pst' }),
      });
    });

    let previewCallCount = 0;
    await page.route('http://127.0.0.1:6174/api/source/selection/preview', async (route) => {
      previewCallCount++;
      const req = route.request().postDataJSON();
      const folderIds = req.folderIds;

      if (!folderIds || folderIds.length === 0) {
        // Zero matches
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            selectionId: `sel_${previewCallCount}`,
            sourceHandle: 'src_1',
            sourceSha256: 'hash123',
            filters: { folderIds: [], startDate: null, endDate: null, timeZone: 'Europe/Istanbul (UTC+03:00)', datePolicy: 'SubmissionDateThenDeliveryDate_UtcPlus3_Inclusive' },
            totalSourceMessages: 5,
            selectedMessagesCount: 0,
            excludedMessagesCount: 5,
            missingDateExcludedCount: 0,
            selectedAttachmentsCount: 0,
            folderBreakdown: [],
            canConvert: false,
            blockerReason: 'Seçim kriterlerinize uyan hiçbir ileti bulunamadı. Sıfır iletili dönüştürme başlatılamaz.',
            blockReason: 'Seçim kriterlerinize uyan hiçbir ileti bulunamadı. Sıfır iletili dönüştürme başlatılamaz.',
          }),
        });
      } else {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            selectionId: `sel_${previewCallCount}`,
            sourceHandle: 'src_1',
            sourceSha256: 'hash123',
            filters: { folderIds: ['fld_1'], startDate: null, endDate: null, timeZone: 'Europe/Istanbul (UTC+03:00)', datePolicy: 'SubmissionDateThenDeliveryDate_UtcPlus3_Inclusive' },
            totalSourceMessages: 5,
            selectedMessagesCount: 5,
            excludedMessagesCount: 0,
            missingDateExcludedCount: 0,
            selectedAttachmentsCount: 1,
            folderBreakdown: [{ folderId: 'fld_1', folderPath: 'Gelen Kutusu', displayName: 'Gelen Kutusu', totalItems: 5, selectedItems: 5, isSelected: true }],
            canConvert: true,
            blockerReason: null,
            blockReason: null,
          }),
        });
      }
    });

    await page.getByTestId('op-tab-convert').click();
    await page.getByTestId('pick-source-btn').click();
    await page.getByTestId('pick-target-btn').click();

    // Initial preview: 5 selected
    await expect(page.getByTestId('preview-selected-count')).toHaveText('5');
    const startBtn = page.getByTestId('start-conversion-btn');
    await expect(startBtn).toBeEnabled();

    // Click "Tümünü kaldır"
    await page.getByTestId('clear-all-folders-btn').click();

    // Zero-match alert and preview-block-alert must be shown and start button absent OR disabled
    await expect(page.getByTestId('zero-match-alert')).toBeVisible();
    await expect(page.getByTestId('preview-block-alert')).toBeVisible();
    await expect(page.getByTestId('preview-block-alert')).toContainText('Seçim kriterlerinize uyan hiçbir ileti bulunamadı');
    await expect(page.getByTestId('preview-selected-count')).toHaveText('0');
    const startBtnZeroMatch = page.getByTestId('start-conversion-btn');
    if ((await startBtnZeroMatch.count()) > 0) {
      await expect(startBtnZeroMatch).toBeDisabled();
    } else {
      await expect(startBtnZeroMatch).toHaveCount(0);
    }

    // Click "Tümünü seç" to restore
    await page.getByTestId('select-all-folders-btn').click();
    await expect(page.getByTestId('preview-selected-count')).toHaveText('5');
    const restoredStartBtn = page.getByTestId('start-conversion-btn');
    await expect(restoredStartBtn).toBeVisible();
    await expect(restoredStartBtn).toBeEnabled();
  });

  test('mobile viewport 390px layout: no horizontal body overflow with filter controls', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.getByTestId('op-tab-convert').click();

    const isOverflowing = await page.evaluate(() => {
      return document.documentElement.scrollWidth > window.innerWidth;
    });
    expect(isOverflowing).toBe(false);
  });
});
