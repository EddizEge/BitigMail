import { test, expect } from '@playwright/test';
import * as path from 'path';
import * as fs from 'fs';
import { openDevelopmentSession, REPO_ROOT, startTestingHost, TESTING_HOST_PORT, TESTING_HOST_URL, type TestingHost } from './support/testingHost';

const QA_SCREENSHOT_DIR = path.join(process.env.LOCALAPPDATA ?? process.env.TEMP ?? REPO_ROOT, 'Temp', 'bitigmail-task010-qa');

// TestingHost derlenmiş DLL'den doğrudan başlatılır (dotnet run yok); afterAll yalnız bu dosyanın başlattığı süreç ağacını kapatır.
let testingHost: TestingHost | null = null;

test.describe('TestingHost Real OST Conversion & Security E2E', () => {
  test.beforeAll(async () => {
    testingHost = await startTestingHost();
  });

  test.afterAll(async () => {
    await testingHost?.stop();
    testingHost = null;
  });

  test('HTTP security negative controls: strictly enforces Origin, Host, Token, and Media Type', async ({ request }) => {
    // A. Reject foreign Origin
    const foreignOriginRes = await request.post(`${TESTING_HOST_URL}/api/session`, {
      headers: {
        Host: `127.0.0.1:${TESTING_HOST_PORT}`,
        Origin: 'http://malicious-site.example',
        'Content-Type': 'application/json',
      },
      data: {},
    });
    expect(foreignOriginRes.status()).toBe(403);
    const foreignOriginBody = await foreignOriginRes.json();
    expect(foreignOriginBody.error).toContain('Forbidden');

    // B. Reject missing Origin
    const missingOriginRes = await request.post(`${TESTING_HOST_URL}/api/session`, {
      headers: {
        Host: `127.0.0.1:${TESTING_HOST_PORT}`,
        'Content-Type': 'application/json',
      },
      data: {},
    });
    expect(missingOriginRes.status()).toBe(403);

    // C. Reject incorrect Host (e.g. attempting to use production port 6174 on 6175)
    const invalidHostRes = await request.post(`${TESTING_HOST_URL}/api/session`, {
      headers: {
        Host: '127.0.0.1:6174',
        Origin: 'http://127.0.0.1:5173',
        'Content-Type': 'application/json',
      },
      data: {},
    });
    expect(invalidHostRes.status()).toBe(400);

    // D. Reject protected API without session token
    const missingTokenRes = await request.post(`${TESTING_HOST_URL}/api/jobs/start`, {
      headers: {
        Host: `127.0.0.1:${TESTING_HOST_PORT}`,
        Origin: 'http://127.0.0.1:5173',
        'Content-Type': 'application/json',
      },
      data: { sourceHandle: 'h1', targetHandle: 'h2' },
    });
    expect(missingTokenRes.status()).toBe(401);

    // E. Reject protected API with invalid session token
    const invalidTokenRes = await request.post(`${TESTING_HOST_URL}/api/jobs/start`, {
      headers: {
        Host: `127.0.0.1:${TESTING_HOST_PORT}`,
        Origin: 'http://127.0.0.1:5173',
        'X-BitigMail-Session': 'invalid_secret_token_123',
        'Content-Type': 'application/json',
      },
      data: { sourceHandle: 'h1', targetHandle: 'h2' },
    });
    expect(invalidTokenRes.status()).toBe(401);

    // F. Reject non-JSON Content-Type on mutating requests
    const invalidCtRes = await request.post(`${TESTING_HOST_URL}/api/session`, {
      headers: {
        Host: `127.0.0.1:${TESTING_HOST_PORT}`,
        Origin: 'http://127.0.0.1:5173',
        'Content-Type': 'text/plain',
      },
      data: 'plain text body',
    });
    expect(invalidCtRes.status()).toBe(415);
  });

  test('Direct HTTP E2E: selects real 13-item OST fixture, analyzes, converts, verifies reopening, and validates produced PST on disk', async ({ request }) => {
    const validHeaders: Record<string, string> = {
      Host: `127.0.0.1:${TESTING_HOST_PORT}`,
      Origin: 'http://127.0.0.1:5173',
      'Content-Type': 'application/json',
    };

    // 1. Initialize session
    const sessionRes = await request.post(`${TESTING_HOST_URL}/api/session`, {
      headers: validHeaders,
      data: {},
    });
    expect(sessionRes.status()).toBe(200);
    const sessionData = await sessionRes.json();
    expect(sessionData.token).toBeTruthy();
    const token = sessionData.token;
    validHeaders['X-BitigMail-Session'] = token;

    // 2. Pick source OST (TestingHost adapter selects approved bitigmail-lab-full.ost fixture)
    const pickSourceRes = await request.post(`${TESTING_HOST_URL}/api/picker/source`, {
      headers: validHeaders,
      data: {},
    });
    expect(pickSourceRes.status()).toBe(200);
    const sourcePick = await pickSourceRes.json();
    expect(sourcePick.cancelled).toBe(false);
    expect(sourcePick.fileName).toBe('bitigmail-lab-full.ost');
    expect(sourcePick.handle).toMatch(/^src_/);
    expect(sourcePick.displayPath).toContain('bitigmail-lab-full.ost');
    expect(sourcePick.sizeBytes).toBeGreaterThan(0);
    const sourceHandle = sourcePick.handle;

    // 3. Analyze source OST
    const analyzeRes = await request.post(`${TESTING_HOST_URL}/api/source/analyze`, {
      headers: validHeaders,
      data: { sourceHandle },
    });
    expect(analyzeRes.status()).toBe(200);
    const analysis = await analyzeRes.json();
    expect(analysis.totalItems).toBe(13);
    expect(analysis.totalAttachments).toBe(4);
    expect(analysis.activeFoldersCount).toBe(3);
    expect(analysis.formatInfo.isValidOutlookStorage).toBe(true);
    expect(analysis.formatInfo.isOstSignature).toBe(true);
    expect(analysis.preflight.canConvert).toBe(true);
    expect(analysis.preflight.hasTrialBlocker).toBe(false);
    expect(analysis.sourceSha256).toBeTruthy();

    // 4. Pick target PST
    const pickTargetRes = await request.post(`${TESTING_HOST_URL}/api/picker/target`, {
      headers: validHeaders,
      data: { sourceHandle },
    });
    expect(pickTargetRes.status()).toBe(200);
    const targetPick = await pickTargetRes.json();
    expect(targetPick.cancelled).toBe(false);
    expect(targetPick.fileName).toMatch(/\.pst$/);
    expect(targetPick.handle).toMatch(/^tgt_/);
    expect(targetPick.displayPath).toMatch(/\.pst$/);
    const targetHandle = targetPick.handle;

    // 5. Start Conversion Job
    const startJobRes = await request.post(`${TESTING_HOST_URL}/api/jobs/start`, {
      headers: validHeaders,
      data: {
        sourceHandle,
        targetHandle,
        clientContext: {
          clientId: 'comp-ornek',
          clientName: 'Örnek Şirket',
          projectId: 'proj-ornek-gecis',
          projectName: 'Sistem Geçişi 2024',
        },
      },
    });
    expect(startJobRes.status()).toBe(200);
    const jobRecord = await startJobRes.json();
    expect(jobRecord.jobId).toBeTruthy();
    const jobId = jobRecord.jobId;

    // 6. Poll until completed
    let pollCount = 0;
    let completedJob: any = null;
    while (pollCount < 40) {
      await new Promise((r) => setTimeout(r, 500));
      const getJobRes = await request.get(`${TESTING_HOST_URL}/api/jobs/${jobId}`, {
        headers: {
          Host: `127.0.0.1:${TESTING_HOST_PORT}`,
          Origin: 'http://127.0.0.1:5173',
          'X-BitigMail-Session': token,
        },
      });
      expect(getJobRes.status()).toBe(200);
      const jobStatus = await getJobRes.json();
      if (jobStatus.status === 'completed' || jobStatus.status === 'failed') {
        completedJob = jobStatus;
        break;
      }
      pollCount++;
    }

    expect(completedJob).not.toBeNull();
    expect(completedJob.status).toBe('completed');
    expect(completedJob.itemsRead).toBe(13);
    expect(completedJob.itemsWritten).toBe(13);
    expect(completedJob.failedItems).toBe(0);
    expect(completedJob.outputPath).toBeTruthy();

    // 7. Retrieve authoritative conversion report
    const reportRes = await request.get(`${TESTING_HOST_URL}/api/jobs/${jobId}/report`, {
      headers: {
        Host: `127.0.0.1:${TESTING_HOST_PORT}`,
        Origin: 'http://127.0.0.1:5173',
        'X-BitigMail-Session': token,
      },
    });
    expect(reportRes.status()).toBe(200);
    const report = await reportRes.json();

    expect(report.conversionSuccess).toBe(true);
    expect(report.itemsRead).toBe(13);
    expect(report.itemsWritten).toBe(13);
    expect(report.failedItems).toBe(0);
    expect(report.sourceHashMatch).toBe(true);
    expect(report.sourceSha256Before).toBe(report.sourceSha256After);
    expect(report.outputPath).toBe(completedJob.outputPath);

    // Assert Aspose reopened PST verification
    expect(report.reopenedPstVerification.itemCountMatch).toBe(true);
    expect(report.reopenedPstVerification.totalPhysicalItemsFound).toBe(13);
    expect(report.reopenedPstVerification.totalAttachmentsVerified).toBe(4);
    expect(report.reopenedPstVerification.totalCidVerified).toBeGreaterThanOrEqual(1);

    // 8. Assert produced PST file physically exists on disk and is non-empty
    expect(fs.existsSync(report.outputPath)).toBe(true);
    const pstStat = fs.statSync(report.outputPath);
    expect(pstStat.size).toBeGreaterThan(0);
  });

  test('UI E2E: connects to real TestingHost, runs full OST convert flow, hides demo counters, and renders complete verification card', async ({ page }) => {
    // Inject runtime engine URL to point browser to real TestingHost (127.0.0.1:6175)
    await openDevelopmentSession(page);

    await page.goto('/');
    await page.evaluate(() => localStorage.clear());
    await page.reload();

    // 1. Navigate to Transfers tab
    await page.getByTestId('nav-tab-transfers').click();

    // 2. Switch to Convert operation
    await page.getByTestId('op-tab-convert').click();

    // 3. Verify real local workflow container is displayed
    const workflow = page.getByTestId('local-convert-workflow');
    await expect(workflow).toBeVisible();

    // 4. Verify '.NET 8 / Aspose 24.8' badge was removed
    await expect(page.locator('text=.NET 8 / Aspose 24.8')).toHaveCount(0);

    // 5. Verify heading is 'OST → PST dönüştürme'
    await expect(page.getByRole('heading', { name: 'OST → PST dönüştürme' })).toBeVisible();

    // 6. Verify status bar demo counter ('248 ileti filtreye uyuyor') is HIDDEN in Convert mode
    await expect(page.getByTestId('status-bar-count')).toHaveCount(0);

    // 7. Verify online status shows Motor hazır
    await expect(page.locator('text=Motor hazır')).toBeVisible();

    // 8. Verify Pick Source button has exact copy 'OST dosyası seç'
    const pickSourceBtn = page.getByTestId('pick-source-btn');
    await expect(pickSourceBtn).toBeVisible();
    await expect(pickSourceBtn).toHaveText('OST dosyası seç');

    // 9. Click 'OST dosyası seç' to trigger real fixture pick and analysis
    await pickSourceBtn.click();

    // 10. Verify Source Filename, full Display Path, and Analysis Preflight findings
    await expect(page.getByTestId('source-filename')).toHaveText('bitigmail-lab-full.ost', { timeout: 15000 });
    await expect(page.getByTestId('source-display-path')).toBeVisible();
    await expect(page.getByTestId('source-display-path')).toContainText('bitigmail-lab-full.ost');
    await expect(page.getByTestId('analysis-preflight-card')).toBeVisible();
    await expect(page.getByTestId('preflight-success-banner')).toBeVisible();
    fs.mkdirSync(QA_SCREENSHOT_DIR, { recursive: true });
    await page.screenshot({
      path: path.join(QA_SCREENSHOT_DIR, `sol-real-analysis-${test.info().project.name}.png`),
      fullPage: true,
    });

    // 11. Select Target PST and verify Target Display Path
    const pickTargetBtn = page.getByTestId('pick-target-btn');
    await expect(pickTargetBtn).toBeVisible();
    await pickTargetBtn.click();
    await expect(page.getByTestId('target-filename')).toBeVisible();
    await expect(page.getByTestId('target-display-path')).toBeVisible();
    await expect(page.getByTestId('target-display-path')).toContainText('.pst');

    // 12. Start Conversion (waits for authoritative preview to be ready)
    const startConversionBtn = page.getByTestId('start-conversion-btn');
    await expect(startConversionBtn).toBeVisible();
    await expect(startConversionBtn).toBeEnabled({ timeout: 20000 });
    await startConversionBtn.click();

    // 13. Wait for full real conversion to complete and report card to render
    const reportCard = page.getByTestId('conversion-report-card');
    await expect(reportCard).toBeVisible({ timeout: 20000 });

    // 14. Assert verification summary details and Output Location in the UI
    await expect(page.locator('text=13 / 13 öğe')).toBeVisible();
    await expect(page.locator('text=Bit düzeyinde eşleşti')).toBeVisible();
    await expect(page.locator('text=4 Ek, 1 CID')).toBeVisible();
    const pageWidth = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth,
    }));
    expect(pageWidth.scrollWidth).toBe(pageWidth.innerWidth);
    await page.screenshot({
      path: path.join(QA_SCREENSHOT_DIR, `sol-real-result-${test.info().project.name}.png`),
      fullPage: true,
    });
    await reportCard.screenshot({
      path: path.join(QA_SCREENSHOT_DIR, `sol-real-result-card-${test.info().project.name}.png`),
    });

    const outputLocationElement = page.getByTestId('output-location-path');
    await expect(outputLocationElement).toBeVisible();
    const outputLocationText = (await outputLocationElement.innerText()).trim();
    expect(outputLocationText).toContain('.pst');

    // 15. Verify Report and Location action buttons are available
    await expect(page.getByTestId('download-report-btn')).toBeVisible();
    await expect(page.getByTestId('copy-report-btn')).toBeVisible();

    const copyLocationBtn = page.getByTestId('copy-location-btn');
    await expect(copyLocationBtn).toBeVisible();
    await expect(copyLocationBtn).toHaveText('Konumu kopyala');
    await copyLocationBtn.click();
    await expect(copyLocationBtn).toHaveText('Kopyalandı!');

    // 16. Refresh page and verify report card and completed output location persist across refresh
    await page.reload();
    await page.getByTestId('nav-tab-transfers').click();
    await page.getByTestId('op-tab-convert').click();

    const reloadedReportCard = page.getByTestId('conversion-report-card');
    await expect(reloadedReportCard).toBeVisible({ timeout: 15000 });
    await expect(page.getByTestId('output-location-path')).toHaveText(outputLocationText);
    const reloadedCopyLocationBtn = page.getByTestId('copy-location-btn');
    await expect(reloadedCopyLocationBtn).toBeVisible();
    await expect(reloadedCopyLocationBtn).toHaveText('Konumu kopyala');
    await reloadedCopyLocationBtn.click();
    await expect(reloadedCopyLocationBtn).toHaveText('Kopyalandı!');
  });

  test('JobCenter & Multi-Job Navigation: handles two distinct persisted real jobs, opens older job via Job Center -> İşi aç, shows older frozen context, hides pause control, and downloads correct report', async ({ request, page }) => {
    // 1. Establish session with TestingHost
    const sessionRes = await request.post(`${TESTING_HOST_URL}/api/session`, {
      headers: {
        Host: `127.0.0.1:${TESTING_HOST_PORT}`,
        Origin: 'http://127.0.0.1:5173',
        'Content-Type': 'application/json',
      },
      data: {},
    });
    expect(sessionRes.status()).toBe(200);
    const { token } = await sessionRes.json();
    const headers = {
      Host: `127.0.0.1:${TESTING_HOST_PORT}`,
      Origin: 'http://127.0.0.1:5173',
      'X-BitigMail-Session': token,
      'Content-Type': 'application/json',
    };

    // Helper to run conversion via API
    async function runConversion(clientContext: { companyId: string; companyName: string; projectId: string; projectName: string; clientId?: string; clientName?: string }) {
      const pickSrc = await (await request.post(`${TESTING_HOST_URL}/api/picker/source`, { headers, data: {} })).json();
      await request.post(`${TESTING_HOST_URL}/api/source/analyze`, { headers, data: { sourceHandle: pickSrc.handle } });
      const pickTgt = await (await request.post(`${TESTING_HOST_URL}/api/picker/target`, { headers, data: { sourceHandle: pickSrc.handle } })).json();
      const startRes = await (await request.post(`${TESTING_HOST_URL}/api/jobs/start`, {
        headers,
        data: {
          sourceHandle: pickSrc.handle,
          targetHandle: pickTgt.handle,
          clientContext,
        },
      })).json();

      const jobId = startRes.jobId;
      let completed: any = null;
      for (let i = 0; i < 40; i++) {
        await new Promise((r) => setTimeout(r, 400));
        const res = await (await request.get(`${TESTING_HOST_URL}/api/jobs/${jobId}`, {
          headers: {
            Host: `127.0.0.1:${TESTING_HOST_PORT}`,
            Origin: 'http://127.0.0.1:5173',
            'X-BitigMail-Session': token,
          },
        })).json();
        if (res.status === 'completed' || res.status === 'failed') {
          completed = res;
          break;
        }
      }
      expect(completed).not.toBeNull();
      expect(completed.status).toBe('completed');
      return completed;
    }

    // 2. Create Job 1 (older job) for "Müşteri Alfa / Proje Alfa"
    const job1 = await runConversion({
      companyId: 'comp-alfa',
      companyName: 'Müşteri Alfa',
      clientId: 'comp-alfa',
      clientName: 'Müşteri Alfa',
      projectId: 'proj-alfa',
      projectName: 'Proje Alfa',
    });
    const job1Id = job1.jobId;
    const job1OutputPath = job1.outputPath;

    // 3. Create Job 2 (newer job) for "Müşteri Beta / Proje Beta"
    const job2 = await runConversion({
      companyId: 'comp-beta',
      companyName: 'Müşteri Beta',
      clientId: 'comp-beta',
      clientName: 'Müşteri Beta',
      projectId: 'proj-beta',
      projectName: 'Proje Beta',
    });
    const job2Id = job2.jobId;

    // 4. Navigate to Web UI with TestingHost URL injected
    await openDevelopmentSession(page);

    await page.goto('/');
    await page.evaluate(() => localStorage.clear());
    await page.reload();

    // 5. Navigate to Job Center
    await page.getByTestId('nav-tab-jobs').click();
    await expect(page.getByTestId('jobs-table')).toBeVisible();

    // Assert both jobs exist in the jobs list
    const job1Row = page.getByTestId(`job-row-${job1Id}`);
    const job2Row = page.getByTestId(`job-row-${job2Id}`);
    await expect(job1Row).toBeVisible({ timeout: 10000 });
    await expect(job2Row).toBeVisible({ timeout: 10000 });

    // 6. Select older job (Job 1) in Job Center
    await job1Row.click();
    const detailsPane = page.getByTestId('job-details-pane');
    await expect(detailsPane).toBeVisible();

    // Assert NO Pause/Resume button exists for the real job
    await expect(page.getByTestId('job-pause-btn')).toHaveCount(0);

    // 7. Click 'İşi aç' to navigate to workspace with the older job
    const openJobBtn = page.getByTestId('job-open-btn');
    await expect(openJobBtn).toBeVisible();
    await openJobBtn.click();

    // 8. Assert navigation to Convert screen and exact older job report/context is loaded
    await expect(page.getByTestId('local-convert-workflow')).toBeVisible();

    // Header client context matches Job 1's frozen context
    await expect(page.getByTestId('context-company-name')).toHaveText('Müşteri Alfa');
    await expect(page.getByTestId('context-project-name')).toHaveText('Proje Alfa');

    // Report card displays Job 1's exact details
    const reportCard = page.getByTestId('conversion-report-card');
    await expect(reportCard).toBeVisible({ timeout: 10000 });
    await expect(page.getByTestId('report-client-context')).toHaveText('Müşteri Alfa / Proje Alfa');
    await expect(page.getByTestId('report-job-id')).toHaveText(job1Id);
    await expect(page.getByTestId('output-location-path')).toHaveText(job1OutputPath);

    // Assert no fake pause button exists anywhere on convert screen
    await expect(page.getByTestId('job-pause-btn')).toHaveCount(0);

    // 9. Navigate to Reports View and verify download works for the correct older jobId
    await page.getByTestId('nav-tab-reports').click();
    await expect(page.getByTestId('reports-table')).toBeVisible();

    const report1Row = page.getByTestId(`report-row-${job1Id}`);
    const report2Row = page.getByTestId(`report-row-${job2Id}`);
    await expect(report1Row).toBeVisible({ timeout: 10000 });
    await expect(report2Row).toBeVisible({ timeout: 10000 });

    const download1Btn = page.getByTestId(`download-json-${job1Id}`);
    await expect(download1Btn).toBeVisible();

    const [download] = await Promise.all([
      page.waitForEvent('download'),
      download1Btn.click(),
    ]);
    expect(download.suggestedFilename()).toBe(`rapor-${job1Id}.json`);
  });

  test('Direct HTTP E2E: filtered conversion of Projeler/İstanbul (2024-01-01 to 2024-02-16) exercises exact 3/1/10 independent oracle, verifies reopening and physical PST', async ({ request }) => {
    // 1. Establish session
    const sessionRes = await request.post(`${TESTING_HOST_URL}/api/session`, {
      headers: {
        Host: `127.0.0.1:${TESTING_HOST_PORT}`,
        Origin: 'http://127.0.0.1:5173',
        'Content-Type': 'application/json',
      },
      data: {},
    });
    expect(sessionRes.status()).toBe(200);
    const { token } = await sessionRes.json();
    const headers = {
      Host: `127.0.0.1:${TESTING_HOST_PORT}`,
      Origin: 'http://127.0.0.1:5173',
      'Content-Type': 'application/json',
      'X-BitigMail-Session': token,
    };

    // 2. Pick Source
    const pickSourceRes = await request.post(`${TESTING_HOST_URL}/api/picker/source`, { headers, data: {} });
    expect(pickSourceRes.status()).toBe(200);
    const { handle: sourceHandle } = await pickSourceRes.json();

    // 3. Analyze Source
    const analyzeRes = await request.post(`${TESTING_HOST_URL}/api/source/analyze`, {
      headers,
      data: { sourceHandle },
    });
    expect(analyzeRes.status()).toBe(200);
    const analysis = await analyzeRes.json();
    expect(analysis.totalItems).toBe(13);

    // Find Projeler/İstanbul folder reliably without ASCII lowercase assumptions
    const istanbulFolder = analysis.folders.find((f: any) =>
      f.folderPath.includes('Projeler/İstanbul') ||
      f.folderPath.includes('İstanbul') ||
      f.displayName === 'İstanbul' ||
      f.folderPath.endsWith('stanbul')
    );
    expect(istanbulFolder).toBeDefined();
    expect(istanbulFolder.itemCount).toBe(4);
    const istanbulFolderId = istanbulFolder.folderId;
    expect(istanbulFolderId).toMatch(/^fld_/);

    // 4. Negative control: Empty folder selection returns zero matches and blocks conversion
    const emptyPreviewRes = await request.post(`${TESTING_HOST_URL}/api/source/selection/preview`, {
      headers,
      data: {
        sourceHandle,
        folderIds: [],
        startDate: null,
        endDate: null,
      },
    });
    expect(emptyPreviewRes.status()).toBe(200);
    const emptyPreview = await emptyPreviewRes.json();
    expect(emptyPreview.selectedMessagesCount).toBe(0);
    expect(emptyPreview.canConvert).toBe(false);
    expect(emptyPreview.blockerReason).toBeTruthy();

    // 5. Request Authoritative Selection Preview for Projeler/İstanbul with 2024-01-01..2024-02-16
    const previewRes = await request.post(`${TESTING_HOST_URL}/api/source/selection/preview`, {
      headers,
      data: {
        sourceHandle,
        folderIds: [istanbulFolderId],
        startDate: '2024-01-01',
        endDate: '2024-02-16',
      },
    });
    expect(previewRes.status()).toBe(200);
    const preview = await previewRes.json();
    expect(preview.selectionId).toMatch(/^sel_/);
    expect(preview.totalSourceMessages).toBe(13);
    expect(preview.selectedMessagesCount).toBe(3);
    expect(preview.excludedMessagesCount).toBe(10);
    expect(preview.selectedAttachmentsCount).toBe(1);
    expect(preview.missingDateExcludedCount).toBe(0);
    expect(preview.canConvert).toBe(true);

    // 6. Pick Target
    const pickTargetRes = await request.post(`${TESTING_HOST_URL}/api/picker/target`, {
      headers,
      data: { sourceHandle },
    });
    expect(pickTargetRes.status()).toBe(200);
    const { handle: targetHandle } = await pickTargetRes.json();

    // 7. Start Conversion with selectionId
    const startJobRes = await request.post(`${TESTING_HOST_URL}/api/jobs/start`, {
      headers,
      data: {
        sourceHandle,
        targetHandle,
        selectionId: preview.selectionId,
        clientContext: {
          companyId: 'comp-filter-oracle',
          companyName: 'Oracle Test Şirketi',
          projectId: 'proj-filter-oracle',
          projectName: 'Oracle Bağımsız Filtre Testi',
        },
      },
    });
    expect(startJobRes.status()).toBe(200);
    const job = await startJobRes.json();
    expect(job.jobId).toBeTruthy();

    // 8. Poll until completed
    let completedJob: any = null;
    for (let i = 0; i < 40; i++) {
      await new Promise((r) => setTimeout(r, 500));
      const getJobRes = await request.get(`${TESTING_HOST_URL}/api/jobs/${job.jobId}`, {
        headers: {
          Host: `127.0.0.1:${TESTING_HOST_PORT}`,
          Origin: 'http://127.0.0.1:5173',
          'X-BitigMail-Session': token,
        },
      });
      const current = await getJobRes.json();
      if (current.status === 'completed' || current.status === 'failed') {
        completedJob = current;
        break;
      }
    }
    expect(completedJob).not.toBeNull();
    expect(completedJob.status).toBe('completed');
    expect(completedJob.itemsWritten).toBe(3);
    expect(completedJob.failedItems).toBe(0);
    expect(completedJob.selectedMessagesCount).toBe(3);
    expect(completedJob.totalSourceMessages).toBe(13);
    expect(completedJob.outputPath).toBeTruthy();

    // 9. Assert authoritative report and reopened PST verification
    const reportRes = await request.get(`${TESTING_HOST_URL}/api/jobs/${job.jobId}/report`, {
      headers: {
        Host: `127.0.0.1:${TESTING_HOST_PORT}`,
        Origin: 'http://127.0.0.1:5173',
        'X-BitigMail-Session': token,
      },
    });
    expect(reportRes.status()).toBe(200);
    const report = await reportRes.json();
    expect(report.conversionSuccess).toBe(true);
    expect(report.isFiltered).toBe(true);
    expect(report.selectedMessagesCount).toBe(3);
    expect(report.totalSourceMessages).toBe(13);
    expect(report.excludedMessagesCount).toBe(10);
    expect(report.itemsWritten).toBe(3);
    expect(report.failedItems).toBe(0);
    expect(report.sourceHashMatch).toBe(true);
    expect(report.sourceSha256Before).toBe(report.sourceSha256After);
    expect(report.outputPath).toBe(completedJob.outputPath);

    // Exactly 3 physical messages, 1 attachment and CID preserved in reopened PST
    expect(report.reopenedPstVerification.itemCountMatch).toBe(true);
    expect(report.reopenedPstVerification.totalPhysicalItemsFound).toBe(3);
    expect(report.reopenedPstVerification.totalAttachmentsVerified).toBe(1);
    expect(report.reopenedPstVerification.totalCidVerified).toBeGreaterThanOrEqual(1);

    // Full output path retained and physically exists on disk
    expect(fs.existsSync(report.outputPath)).toBe(true);
    expect(fs.statSync(report.outputPath).size).toBeGreaterThan(0);
  });

  test('UI E2E: filtered OST conversion of Projeler/İstanbul with 2024-01-01..2024-02-16 dates, blocks on empty selection, verifies 3/1/10 preview and frozen report persistence, and enforces zero 390px overflow', async ({ page }) => {
    // 1. Point browser to real TestingHost runtime
    await openDevelopmentSession(page);

    await page.goto('/');
    await page.evaluate(() => localStorage.clear());
    await page.reload();

    // 2. Navigate to Transfers tab and switch to Convert
    await page.getByTestId('nav-tab-transfers').click();
    await page.getByTestId('op-tab-convert').click();

    const workflow = page.getByTestId('local-convert-workflow');
    await expect(workflow).toBeVisible();
    await expect(page.locator('text=Motor hazır')).toBeVisible({ timeout: 10000 });

    // 3. Pick real source OST fixture
    const pickSourceBtn = page.getByTestId('pick-source-btn');
    await expect(pickSourceBtn).toBeVisible();
    await pickSourceBtn.click();

    await expect(page.getByTestId('source-filename')).toHaveText('bitigmail-lab-full.ost', { timeout: 15000 });
    await expect(page.getByTestId('analysis-preflight-card')).toBeVisible();
    await expect(page.getByTestId('preflight-success-banner')).toBeVisible();
    await expect(page.getByTestId('filter-selection-card')).toBeVisible();

    // 4. Pick target PST
    const pickTargetBtn = page.getByTestId('pick-target-btn');
    await expect(pickTargetBtn).toBeVisible();
    await pickTargetBtn.click();
    await expect(page.getByTestId('target-filename')).toBeVisible();
    await expect(page.getByTestId('target-display-path')).toContainText('.pst');

    // 5. Exercise explicit empty folder selection zero-match with conversion blocked
    const clearAllFoldersBtn = page.getByTestId('clear-all-folders-btn');
    await expect(clearAllFoldersBtn).toBeVisible();
    await clearAllFoldersBtn.click();

    // Observe zero-match alert and conversion blocked: start control is absent OR present and disabled
    await expect(page.getByTestId('zero-match-alert')).toBeVisible({ timeout: 10000 });
    await expect(page.getByTestId('preview-selected-count')).toHaveText('0');
    const startBtnBlocked = page.getByTestId('start-conversion-btn');
    if ((await startBtnBlocked.count()) > 0) {
      await expect(startBtnBlocked).toBeDisabled();
    } else {
      await expect(startBtnBlocked).toHaveCount(0);
    }

    // 6. Restore selection to Projeler/İstanbul only (reliable Turkish dotted İ match)
    const istanbulLabel = page.locator('[data-testid^="folder-label-"]').filter({ hasText: 'İstanbul' });
    await expect(istanbulLabel).toBeVisible();
    const istanbulCheckbox = istanbulLabel.locator('input[type="checkbox"]');
    await istanbulCheckbox.check();
    await expect(istanbulCheckbox).toBeChecked();

    // 7. Enter inclusive Türkiye date filter (2024-01-01 to 2024-02-16)
    const startDateInput = page.getByTestId('filter-start-date');
    const endDateInput = page.getByTestId('filter-end-date');
    await startDateInput.fill('2024-01-01');
    await endDateInput.fill('2024-02-16');

    // 8. Observe authoritative 3/1/10 preview
    await expect(page.getByTestId('preview-selected-count')).toHaveText('3', { timeout: 10000 });
    await expect(page.getByTestId('preview-attachments-count')).toHaveText('1');
    await expect(page.getByTestId('preview-excluded-count')).toHaveText('10');
    await expect(page.getByTestId('preview-total-count')).toHaveText('13');
    await expect(page.getByTestId('zero-match-alert')).toHaveCount(0);

    // 9. Start conversion with authoritative selection (reacquire locator and require visible/enabled)
    const startConversionBtn = page.getByTestId('start-conversion-btn');
    await expect(startConversionBtn).toBeVisible({ timeout: 10000 });
    await expect(startConversionBtn).toBeEnabled({ timeout: 10000 });
    await startConversionBtn.click();

    // 10. Wait for conversion to complete and report card to render
    const reportCard = page.getByTestId('conversion-report-card');
    await expect(reportCard).toBeVisible({ timeout: 25000 });

    // 11. Assert exact frozen counts, selected folder path, date range, and timezone
    await expect(page.getByTestId('report-selected-messages')).toHaveText('3 / 13');
    await expect(page.getByTestId('report-excluded-messages')).toHaveText('10');
    await expect(page.locator('text=3 / 3 öğe')).toBeVisible();
    await expect(page.locator('text=Bit düzeyinde eşleşti')).toBeVisible();
    await expect(page.locator('text=1 Ek, 1 CID')).toBeVisible();
    await expect(page.locator('text=PR_ATTACH_CONTENT_ID korundu')).toBeVisible();

    const selectedFoldersElem = page.getByTestId('report-selected-folders');
    await expect(selectedFoldersElem).toBeVisible();
    await expect(selectedFoldersElem).toContainText('İstanbul');

    const filterSummaryElem = page.getByTestId('report-filter-summary');
    await expect(filterSummaryElem).toBeVisible();
    await expect(filterSummaryElem).toContainText('Europe/Istanbul (UTC+03:00)');
    await expect(filterSummaryElem).toContainText('2024-01-01 / 2024-02-16');

    const outputLocationElement = page.getByTestId('output-location-path');
    await expect(outputLocationElement).toBeVisible();
    const outputLocationText = (await outputLocationElement.innerText()).trim();
    expect(outputLocationText).toContain('.pst');
    expect(fs.existsSync(outputLocationText)).toBe(true);

    const copyLocationBtn = page.getByTestId('copy-location-btn');
    await expect(copyLocationBtn).toBeVisible();
    await copyLocationBtn.click();
    await expect(copyLocationBtn).toHaveText('Kopyalandı!');

    // 12. Refresh page and verify report card, frozen filter info, and output location persist
    await page.reload();
    await page.getByTestId('nav-tab-transfers').click();
    await page.getByTestId('op-tab-convert').click();

    const reloadedReportCard = page.getByTestId('conversion-report-card');
    await expect(reloadedReportCard).toBeVisible({ timeout: 15000 });
    await expect(page.getByTestId('report-selected-messages')).toHaveText('3 / 13');
    await expect(page.getByTestId('report-excluded-messages')).toHaveText('10');
    await expect(page.locator('text=3 / 3 öğe')).toBeVisible();
    await expect(page.getByTestId('report-selected-folders')).toContainText('İstanbul');
    await expect(page.getByTestId('output-location-path')).toHaveText(outputLocationText);

    const reloadedCopyLocationBtn = page.getByTestId('copy-location-btn');
    await expect(reloadedCopyLocationBtn).toBeVisible();
    await reloadedCopyLocationBtn.click();
    await expect(reloadedCopyLocationBtn).toHaveText('Kopyalandı!');

    // 13. Verify no body horizontal overflow on current project viewport
    const currentDims = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth,
    }));
    expect(currentDims.scrollWidth).toBe(currentDims.innerWidth);

    // 14. Explicitly test 390px narrow-screen responsiveness to ensure zero horizontal overflow
    await page.setViewportSize({ width: 390, height: 844 });
    const mobileDims = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth,
    }));
    expect(mobileDims.scrollWidth).toBe(mobileDims.innerWidth);

    // Save screenshot for audit evidence
    fs.mkdirSync(QA_SCREENSHOT_DIR, { recursive: true });
    await page.screenshot({
      path: path.join(QA_SCREENSHOT_DIR, `sol-filtered-real-result-${test.info().project.name}.png`),
      fullPage: true,
    });
  });
});
