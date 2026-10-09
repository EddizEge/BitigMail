import { test, expect } from '@playwright/test';
import { spawn, execSync } from 'child_process';
import * as path from 'path';
import * as fs from 'fs';
import * as http from 'http';
import * as crypto from 'crypto';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const REPO_ROOT = path.resolve(__dirname, '../../..');
const DOTNET_EXE = path.join(REPO_ROOT, '.tools', 'dotnet', 'dotnet.exe');
const PROJECT_FILE = path.join(REPO_ROOT, 'engine', 'BitigMail.TestingHost', 'BitigMail.TestingHost.csproj');
const STOP_SCRIPT = path.join(REPO_ROOT, 'scripts', 'stop-testing-engine.ps1');
const TESTING_HOST_PORT = 6175;
const TESTING_HOST_URL = `http://127.0.0.1:${TESTING_HOST_PORT}`;
const QA_SCREENSHOT_DIR = path.join(process.env.LOCALAPPDATA ?? process.env.TEMP ?? REPO_ROOT, 'Temp', 'bitigmail-task012-qa');

let testingHostProcess: any = null;

async function isPortListening(port: number): Promise<boolean> {
  return new Promise((resolve) => {
    const req = http.request(
      {
        host: '127.0.0.1',
        port,
        path: '/api/session',
        method: 'POST',
        headers: {
          Host: `127.0.0.1:${port}`,
          Origin: 'http://127.0.0.1:5173',
          'Content-Type': 'application/json',
          'Content-Length': '2',
        },
        timeout: 1000,
      },
      (res) => {
        res.resume();
        resolve(res.statusCode === 200);
      }
    );
    req.on('error', () => resolve(false));
    req.on('timeout', () => {
      req.destroy();
      resolve(false);
    });
    req.end('{}');
  });
}

async function waitForHostReady(timeoutMs: number = 30000): Promise<void> {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    if (await isPortListening(TESTING_HOST_PORT)) {
      return;
    }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(`BitigMail.TestingHost 127.0.0.1:${TESTING_HOST_PORT} adresinde dinlemeye başlayamadı (Zaman aşımı).`);
}

function stopTestingHost() {
  try {
    execSync(`powershell -ExecutionPolicy Bypass -File "${STOP_SCRIPT}"`, {
      stdio: 'pipe',
      timeout: 10000,
    });
  } catch {
    // Ignore script stop error and kill direct child process if alive
  }

  if (testingHostProcess && !testingHostProcess.killed) {
    try {
      testingHostProcess.kill('SIGKILL');
    } catch { }
    testingHostProcess = null;
  }
}

test.describe('TestingHost Real PST/OST Split & Archive E2E (TASK-012)', () => {
  test.beforeAll(async () => {
    // 1. Ensure testing host is not currently running from an earlier dirty state
    stopTestingHost();

    // 2. Spawn BitigMail.TestingHost strictly on port 6175 (server history is preserved)
    testingHostProcess = spawn(DOTNET_EXE, ['run', '--project', PROJECT_FILE], {
      cwd: REPO_ROOT,
      stdio: 'pipe',
      windowsHide: true,
      env: {
        ...process.env,
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE: '1',
        DOTNET_CLI_TELEMETRY_OPTOUT: '1',
        DOTNET_NOLOGO: '1',
        DOTNET_MULTILEVEL_LOOKUP: '0',
      },
    });

    // 3. Wait until the host is verified listening on port 6175
    await waitForHostReady(30000);
  });

  test.afterAll(async () => {
    // Cleanly stop TestingHost using official stop script
    stopTestingHost();
  });

  test('HTTP split security & parameter negative controls: enforces session auth, validates split mode and size cap constraints', async ({ request }) => {
    const validHeaders: Record<string, string> = {
      Host: `127.0.0.1:${TESTING_HOST_PORT}`,
      Origin: 'http://127.0.0.1:5173',
      'Content-Type': 'application/json',
    };

    // A. Reject split endpoints without session token
    const noTokenPlanRes = await request.post(`${TESTING_HOST_URL}/api/split/plan`, {
      headers: validHeaders,
      data: { sourceHandle: 'src_test', splitMode: 'year' },
    });
    expect(noTokenPlanRes.status()).toBe(401);

    const noTokenStartRes = await request.post(`${TESTING_HOST_URL}/api/split/start`, {
      headers: validHeaders,
      data: { planId: 'plan_test', outputDirHandle: 'out_test' },
    });
    expect(noTokenStartRes.status()).toBe(401);

    // B. Create valid session
    const sessionRes = await request.post(`${TESTING_HOST_URL}/api/session`, {
      headers: validHeaders,
      data: {},
    });
    expect(sessionRes.status()).toBe(200);
    const { token } = await sessionRes.json();
    validHeaders['X-BitigMail-Session'] = token;

    // C. Set split source to genuine-pst fixture
    const setFixtureRes = await request.post(`${TESTING_HOST_URL}/api/testing/set-split-source`, {
      headers: validHeaders,
      data: { fixtureId: 'genuine-pst' },
    });
    expect(setFixtureRes.status()).toBe(200);

    // D. Pick split source
    const pickRes = await request.post(`${TESTING_HOST_URL}/api/picker/split-source`, {
      headers: validHeaders,
      data: {},
    });
    expect(pickRes.status()).toBe(200);
    const { handle: sourceHandle } = await pickRes.json();
    expect(sourceHandle).toMatch(/^src_[0-9a-f]{32}$/);

    // E. Analyze split source
    const analyzeRes = await request.post(`${TESTING_HOST_URL}/api/split/source/analyze`, {
      headers: validHeaders,
      data: { sourceHandle },
    });
    expect(analyzeRes.status()).toBe(200);

    // F. Negative control: invalid split mode rejected
    const invalidModeRes = await request.post(`${TESTING_HOST_URL}/api/split/plan`, {
      headers: validHeaders,
      data: {
        sourceHandle,
        splitMode: 'arbitrary_mode',
      },
    });
    expect(invalidModeRes.status()).toBe(400);

    // G. Negative control: size mode with non-positive or missing size cap rejected
    const invalidCapRes = await request.post(`${TESTING_HOST_URL}/api/split/plan`, {
      headers: validHeaders,
      data: {
        sourceHandle,
        splitMode: 'size',
        sizeCapBytes: 0,
      },
    });
    expect(invalidCapRes.status()).toBe(400);

    // H. Negative control: start split with non-existent planId rejected
    const invalidPlanStartRes = await request.post(`${TESTING_HOST_URL}/api/split/start`, {
      headers: validHeaders,
      data: {
        planId: 'plan_non_existent',
        outputDirHandle: 'out_test',
      },
    });
    expect(invalidPlanStartRes.status()).toBe(400);
  });

  test('Direct HTTP E2E: OST input supported for splitting path, verifying dual-format capability', async ({ request }) => {
    const headers: Record<string, string> = {
      Host: `127.0.0.1:${TESTING_HOST_PORT}`,
      Origin: 'http://127.0.0.1:5173',
      'Content-Type': 'application/json',
    };

    // 1. Establish session
    const sessionRes = await request.post(`${TESTING_HOST_URL}/api/session`, { headers, data: {} });
    expect(sessionRes.status()).toBe(200);
    const { token } = await sessionRes.json();
    headers['X-BitigMail-Session'] = token;

    // 2. Set split fixture to genuine-ost
    const setSourceRes = await request.post(`${TESTING_HOST_URL}/api/testing/set-split-source`, {
      headers,
      data: { fixtureId: 'genuine-ost' },
    });
    expect(setSourceRes.status()).toBe(200);

    // 3. Pick split source
    const pickSourceRes = await request.post(`${TESTING_HOST_URL}/api/picker/split-source`, { headers, data: {} });
    expect(pickSourceRes.status()).toBe(200);
    const sourceData = await pickSourceRes.json();
    expect(sourceData.fileName).toMatch(/\.ost$/);
    const sourceHandle = sourceData.handle;

    // 4. Analyze split source (OST signature accepted for splitting)
    const analyzeRes = await request.post(`${TESTING_HOST_URL}/api/split/source/analyze`, {
      headers,
      data: { sourceHandle },
    });
    expect(analyzeRes.status()).toBe(200);
    const analysis = await analyzeRes.json();
    expect(analysis.formatInfo.isValidOutlookStorage).toBe(true);
    expect(analysis.formatInfo.isOstSignature).toBe(true);
    expect(analysis.totalItems).toBe(13);

    // Find Istanbul folder
    const istanbulFolder = analysis.folders.find((f: any) =>
      f.folderPath.includes('İstanbul') || f.displayName === 'İstanbul'
    );
    expect(istanbulFolder).toBeDefined();

    // 5. Authoritative preview for Istanbul folder
    const previewRes = await request.post(`${TESTING_HOST_URL}/api/source/selection/preview`, {
      headers,
      data: {
        sourceHandle,
        folderIds: [istanbulFolder.folderId],
        startDate: '2024-01-01',
        endDate: '2024-02-16',
      },
    });
    expect(previewRes.status()).toBe(200);
    const preview = await previewRes.json();
    expect(preview.selectedMessagesCount).toBe(3);

    // 6. Split plan for OST filtered selection
    const planRes = await request.post(`${TESTING_HOST_URL}/api/split/plan`, {
      headers,
      data: {
        sourceHandle,
        selectionId: preview.selectionId,
        splitMode: 'year',
      },
    });
    expect(planRes.status()).toBe(200);
    const plan = await planRes.json();
    expect(plan.canSplit).toBe(true);
    expect(plan.yearGroups.length).toBe(1);
    expect(plan.yearGroups[0].year).toBe('2024');
    expect(plan.yearGroups[0].messageCount).toBe(3);
  });

  test('Direct HTTP E2E: genuine PST input split by Year (UTC+03) produces verified multipart PST bundle on disk', async ({ request }) => {
    const headers: Record<string, string> = {
      Host: `127.0.0.1:${TESTING_HOST_PORT}`,
      Origin: 'http://127.0.0.1:5173',
      'Content-Type': 'application/json',
    };

    // 1. Establish session
    const sessionRes = await request.post(`${TESTING_HOST_URL}/api/session`, { headers, data: {} });
    expect(sessionRes.status()).toBe(200);
    const { token } = await sessionRes.json();
    headers['X-BitigMail-Session'] = token;

    // 2. Set split fixture to genuine-pst
    const setSourceRes = await request.post(`${TESTING_HOST_URL}/api/testing/set-split-source`, {
      headers,
      data: { fixtureId: 'genuine-pst' },
    });
    expect(setSourceRes.status()).toBe(200);

    // 3. Pick split source
    const pickSourceRes = await request.post(`${TESTING_HOST_URL}/api/picker/split-source`, { headers, data: {} });
    expect(pickSourceRes.status()).toBe(200);
    const sourceData = await pickSourceRes.json();
    expect(sourceData.cancelled).toBe(false);
    expect(sourceData.fileName).toMatch(/\.pst$/);
    const sourceHandle = sourceData.handle;

    // 4. Analyze split source
    const analyzeRes = await request.post(`${TESTING_HOST_URL}/api/split/source/analyze`, {
      headers,
      data: { sourceHandle },
    });
    expect(analyzeRes.status()).toBe(200);
    const analysis = await analyzeRes.json();
    expect(analysis.formatInfo.isValidOutlookStorage).toBe(true);
    expect(analysis.totalItems).toBe(13);
    expect(analysis.totalAttachments).toBe(4);
    expect(analysis.preflight.hasTrialBlocker).toBe(false);

    // 5. Create split plan in Year mode
    const planRes = await request.post(`${TESTING_HOST_URL}/api/split/plan`, {
      headers,
      data: {
        sourceHandle,
        splitMode: 'year',
      },
    });
    expect(planRes.status()).toBe(200);
    const plan = await planRes.json();
    expect(plan.canSplit).toBe(true);
    expect(plan.planId).toMatch(/^plan_/);
    expect(plan.splitMode).toBe('year');
    expect(plan.yearGroups).toBeDefined();
    expect(plan.yearGroups.length).toBeGreaterThanOrEqual(4);

    // Verify expected years in 13-item oracle: 2022, 2023, 2024, 2025, 2026
    const years = plan.yearGroups.map((g: any) => g.year);
    expect(years).toContain('2022');
    expect(years).toContain('2023');
    expect(years).toContain('2024');

    // 6. Pick output directory - verify real typed server contract /^dir_[0-9a-f]{32}$/
    const pickDirRes = await request.post(`${TESTING_HOST_URL}/api/picker/output-dir`, { headers, data: {} });
    expect(pickDirRes.status()).toBe(200);
    const dirData = await pickDirRes.json();
    expect(dirData.handle).toMatch(/^dir_[0-9a-f]{32}$/);
    expect(dirData.displayPath).toBeTruthy();
    const outputDirHandle = dirData.handle;

    // 7. Start split job
    const startRes = await request.post(`${TESTING_HOST_URL}/api/split/start`, {
      headers,
      data: {
        planId: plan.planId,
        outputDirHandle,
        idempotencyKey: `idemp_pst_year_${Date.now()}`,
        clientContext: {
          companyId: 'comp-acme',
          companyName: 'Acme Holding',
          projectId: 'proj-split-year',
          projectName: 'PST Yıllık Arşivleme',
        },
      },
    });
    expect(startRes.status()).toBe(200);
    const jobRecord = await startRes.json();
    expect(jobRecord.jobId).toBeTruthy();
    expect(jobRecord.jobKind).toBe('split');
    const jobId = jobRecord.jobId;

    // 8. Poll until completed
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
      const cur = await getJobRes.json();
      if (cur.status === 'completed' || cur.status === 'failed') {
        completedJob = cur;
        break;
      }
      pollCount++;
    }

    expect(completedJob).not.toBeNull();
    expect(completedJob.status).toBe('completed');
    expect(completedJob.itemsWritten).toBe(13);
    expect(completedJob.failedItems).toBe(0);
    expect(completedJob.parts).toBeDefined();
    expect(completedJob.parts.length).toBe(plan.yearGroups.length);

    // 9. Retrieve authoritative report
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
    expect(report.jobKind).toBe('split');
    expect(report.splitMode).toBe('year');
    expect(report.sourceHashMatch).toBe(true);
    expect(report.outputDirectoryPath).toBeTruthy();

    // 10. Validate physical existence of output bundle and every PST part on disk using authoritative partFullPath
    expect(fs.existsSync(report.outputDirectoryPath)).toBe(true);
    let totalItemsFoundInParts = 0;
    for (const part of report.parts) {
      expect(fs.existsSync(part.partFullPath)).toBe(true);
      const stat = fs.statSync(part.partFullPath);
      expect(stat.size).toBe(part.partSizeBytes);
      expect(stat.size).toBeGreaterThan(0);
      expect(part.partSha256).toMatch(/^[0-9a-f]{64}$/);
      const computedSha = crypto.createHash('sha256').update(fs.readFileSync(part.partFullPath)).digest('hex');
      expect(computedSha).toBe(part.partSha256);
      expect(part.partFileName).toBeTruthy();
      expect(path.basename(part.partFullPath)).toBe(part.partFileName);
      expect(part.reopenedPstVerification.verificationSuccess).toBe(true);
      expect(part.reopenedPstVerification.itemCountMatch).toBe(true);
      expect(part.itemsWritten).toBe(part.reopenedPstVerification.totalPhysicalItemsFound);
      totalItemsFoundInParts += part.itemsWritten;
    }
    expect(totalItemsFoundInParts).toBe(13);
  });

  test('Direct HTTP E2E: synthetic PST input split by Size (hard cap) partitions messages into compliant PST parts <= cap', async ({ request }) => {
    const headers: Record<string, string> = {
      Host: `127.0.0.1:${TESTING_HOST_PORT}`,
      Origin: 'http://127.0.0.1:5173',
      'Content-Type': 'application/json',
    };

    // 1. Establish session
    const sessionRes = await request.post(`${TESTING_HOST_URL}/api/session`, { headers, data: {} });
    expect(sessionRes.status()).toBe(200);
    const { token } = await sessionRes.json();
    headers['X-BitigMail-Session'] = token;

    // 2. Set split fixture to synthetic-size (9 messages with 256 KiB attachments)
    const setSourceRes = await request.post(`${TESTING_HOST_URL}/api/testing/set-split-source`, {
      headers,
      data: { fixtureId: 'synthetic-size' },
    });
    expect(setSourceRes.status()).toBe(200);

    // 3. Pick split source
    const pickSourceRes = await request.post(`${TESTING_HOST_URL}/api/picker/split-source`, { headers, data: {} });
    expect(pickSourceRes.status()).toBe(200);
    const { handle: sourceHandle } = await pickSourceRes.json();
    expect(sourceHandle).toMatch(/^src_[0-9a-f]{32}$/);

    // 4. Analyze split source
    const analyzeRes = await request.post(`${TESTING_HOST_URL}/api/split/source/analyze`, {
      headers,
      data: { sourceHandle },
    });
    expect(analyzeRes.status()).toBe(200);
    const analysis = await analyzeRes.json();
    expect(analysis.totalItems).toBe(9);

    // 5. Create split plan with Size Mode hard cap of 1,200,000 bytes (~1.2 MB)
    const sizeCapBytes = 1_200_000;
    const planRes = await request.post(`${TESTING_HOST_URL}/api/split/plan`, {
      headers,
      data: {
        sourceHandle,
        splitMode: 'size',
        sizeCapBytes,
      },
    });
    expect(planRes.status()).toBe(200);
    const plan = await planRes.json();
    expect(plan.canSplit).toBe(true);
    expect(plan.splitMode).toBe('size');
    expect(plan.sizeCapBytes).toBe(sizeCapBytes);

    // 6. Pick output directory
    const pickDirRes = await request.post(`${TESTING_HOST_URL}/api/picker/output-dir`, { headers, data: {} });
    expect(pickDirRes.status()).toBe(200);
    const { handle: outputDirHandle } = await pickDirRes.json();
    expect(outputDirHandle).toMatch(/^dir_[0-9a-f]{32}$/);

    // 7. Start split job
    const startRes = await request.post(`${TESTING_HOST_URL}/api/split/start`, {
      headers,
      data: {
        planId: plan.planId,
        outputDirHandle,
        idempotencyKey: `idemp_pst_size_${Date.now()}`,
        clientContext: {
          companyId: 'comp-size-test',
          companyName: 'Boyut Test Müşterisi',
          projectId: 'proj-size-test',
          projectName: 'Sabit Boyut Bölümleme',
        },
      },
    });
    expect(startRes.status()).toBe(200);
    const { jobId } = await startRes.json();

    // 8. Poll until completed
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
      const cur = await getJobRes.json();
      if (cur.status === 'completed' || cur.status === 'failed') {
        completedJob = cur;
        break;
      }
      pollCount++;
    }

    expect(completedJob).not.toBeNull();
    expect(completedJob.status).toBe('completed');
    expect(completedJob.itemsWritten).toBe(9);

    // 9. Retrieve report and verify EVERY part strictly complies with the hard cap and authoritatively exists via partFullPath
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
    expect(report.parts.length).toBeGreaterThanOrEqual(2);

    let sumWritten = 0;
    for (const part of report.parts) {
      expect(fs.existsSync(part.partFullPath)).toBe(true);
      const stat = fs.statSync(part.partFullPath);
      // Hard cap verification: EVERY single closed file MUST be <= configured cap
      expect(stat.size).toBe(part.partSizeBytes);
      expect(stat.size).toBeGreaterThan(0);
      expect(stat.size).toBeLessThanOrEqual(sizeCapBytes);
      expect(part.partSha256).toMatch(/^[0-9a-f]{64}$/);
      const computedSha = crypto.createHash('sha256').update(fs.readFileSync(part.partFullPath)).digest('hex');
      expect(computedSha).toBe(part.partSha256);
      expect(part.partFileName).toBeTruthy();
      expect(path.basename(part.partFullPath)).toBe(part.partFileName);
      expect(part.reopenedPstVerification.verificationSuccess).toBe(true);
      expect(part.reopenedPstVerification.itemCountMatch).toBe(true);
      expect(part.itemsWritten).toBe(part.reopenedPstVerification.totalPhysicalItemsFound);
      sumWritten += part.itemsWritten;
    }
    expect(sumWritten).toBe(9);
  });

  test('UI E2E: connects to real TestingHost, executes full Year split workflow beginning from an existing completed split, verifies new source flow, multipart report, reload restoration, and Job Center selection', async ({ page, request }) => {
    // 1. Point browser to real TestingHost runtime (port 6175)
    await page.addInitScript((url) => {
      (window as any).__BITIGMAIL_ENGINE_URL__ = url;
    }, TESTING_HOST_URL);

    const sessionRes = await request.post(`${TESTING_HOST_URL}/api/session`, {
      headers: {
        Host: `127.0.0.1:${TESTING_HOST_PORT}`,
        Origin: 'http://127.0.0.1:5173',
        'Content-Type': 'application/json',
      },
      data: {},
    });
    const { token } = await sessionRes.json();
    const headers: Record<string, string> = {
      Host: `127.0.0.1:${TESTING_HOST_PORT}`,
      Origin: 'http://127.0.0.1:5173',
      'X-BitigMail-Session': token,
      'Content-Type': 'application/json',
    };

    // 2. Ensure an existing completed split job exists on the server
    const allJobsRes = await request.get(`${TESTING_HOST_URL}/api/jobs`, { headers });
    const allJobs = await allJobsRes.json();
    const hasCompleted = allJobs.some((j: any) => j.jobKind === 'split' && j.status === 'completed');
    if (!hasCompleted) {
      await request.post(`${TESTING_HOST_URL}/api/testing/set-split-source`, { headers, data: { fixtureId: 'genuine-pst' } });
      const pickRes = await (await request.post(`${TESTING_HOST_URL}/api/picker/split-source`, { headers, data: {} })).json();
      await request.post(`${TESTING_HOST_URL}/api/split/source/analyze`, { headers, data: { sourceHandle: pickRes.handle } });
      const planRes = await (await request.post(`${TESTING_HOST_URL}/api/split/plan`, { headers, data: { sourceHandle: pickRes.handle, splitMode: 'year' } })).json();
      const dirRes = await (await request.post(`${TESTING_HOST_URL}/api/picker/output-dir`, { headers, data: {} })).json();
      const startRes = await (await request.post(`${TESTING_HOST_URL}/api/split/start`, {
        headers,
        data: {
          planId: planRes.planId,
          outputDirHandle: dirRes.handle,
        },
      })).json();
      for (let i = 0; i < 40; i++) {
        await new Promise((r) => setTimeout(r, 400));
        const j = await (await request.get(`${TESTING_HOST_URL}/api/jobs/${startRes.jobId}`, { headers })).json();
        if (j.status === 'completed' || j.status === 'failed') break;
      }
    }

    await page.goto('/');
    await page.evaluate(() => localStorage.clear());
    await page.reload();

    // 3. Navigate to Transfers -> Archive
    await page.getByTestId('nav-tab-transfers').click();
    await page.getByTestId('op-tab-archive').click();

    // 4. Verify local archive workflow container is displayed
    const workflow = page.getByTestId('local-archive-workflow');
    await expect(workflow).toBeVisible();

    // 5. Verify demo controls and counters are HIDDEN in Archive mode
    await expect(page.getByTestId('source-account-select')).toHaveCount(0);
    await expect(page.getByTestId('target-account-select')).toHaveCount(0);
    await expect(page.getByTestId('status-bar-count')).toHaveCount(0);

    // 6. Verify engine status shows Motor hazır
    await expect(page.locator('text=Motor hazır')).toBeVisible({ timeout: 10000 });

    // 7. Deliberately begin with existing completed split: assert restored report card and 'Farklı dosya seç' button
    const preExistingReportCard = page.getByTestId('conversion-report-card');
    await expect(preExistingReportCard).toBeVisible({ timeout: 15000 });
    const preExistingJobId = (await page.getByTestId('report-job-id').innerText()).trim();
    expect(preExistingJobId).toBeTruthy();

    const reselectBtn = page.getByTestId('reselect-source-btn');
    await expect(reselectBtn).toBeVisible();
    await expect(reselectBtn).toHaveText('Farklı dosya seç');

    // 8. Ensure test-source fixture is set for the new split flow
    await request.post(`${TESTING_HOST_URL}/api/testing/set-split-source`, {
      headers,
      data: { fixtureId: 'genuine-pst' },
    });

    // 9. Click 'Farklı dosya seç' - new source selection must clear activeJob/jobReport and reveal the new plan/start workflow
    await reselectBtn.click();

    // Pre-existing completed report MUST be cleared from the DOM
    await expect(page.getByTestId('conversion-report-card')).toHaveCount(0);

    // 10. Verify newly chosen source and analysis preflight findings
    await expect(page.getByTestId('source-filename')).toBeVisible({ timeout: 15000 });
    await expect(page.getByTestId('source-display-path')).toBeVisible();
    await expect(page.getByTestId('analysis-preflight-card')).toBeVisible();
    await expect(page.getByTestId('preflight-success-banner')).toBeVisible();

    // 11. Year mode is selected by default; verify planned year groups table renders
    const yearRadio = page.getByTestId('split-mode-year-radio');
    await expect(yearRadio).toBeChecked();
    await expect(page.getByTestId('split-year-groups-table')).toBeVisible({ timeout: 15000 });

    // 12. PROVE start-split-btn becomes available and is disabled until output dir is selected
    const startSplitBtn = page.getByTestId('start-split-btn');
    await expect(startSplitBtn).toBeVisible({ timeout: 10000 });
    await expect(startSplitBtn).toBeDisabled();

    // 13. Pick output directory
    const pickDirBtn = page.getByTestId('pick-output-dir-btn');
    await expect(pickDirBtn).toBeVisible();
    await pickDirBtn.click();
    await expect(page.getByTestId('output-dir-path')).toBeVisible();

    // 14. PROVE start-split-btn is now enabled and start split
    await expect(startSplitBtn).toBeEnabled({ timeout: 10000 });
    await startSplitBtn.click();

    // 15. Wait for split job to complete and multipart report card to render
    const reportCard = page.getByTestId('conversion-report-card');
    await expect(reportCard).toBeVisible({ timeout: 35000 });

    const newJobId = (await page.getByTestId('report-job-id').innerText()).trim();
    expect(newJobId).toBeTruthy();
    expect(newJobId).not.toBe(preExistingJobId);

    // 16. Verify multipart report details
    const outputLocationElement = page.getByTestId('output-location-path');
    await expect(outputLocationElement).toBeVisible();
    const outputLocationText = (await outputLocationElement.innerText()).trim();
    expect(fs.existsSync(outputLocationText)).toBe(true);

    const partsTable = page.getByTestId('split-parts-table');
    await expect(partsTable).toBeVisible();
    const partRows = partsTable.locator('tbody tr');
    expect(await partRows.count()).toBeGreaterThanOrEqual(1);

    // Copy location test
    const copyLocationBtn = page.getByTestId('copy-location-btn');
    await expect(copyLocationBtn).toBeVisible();
    await copyLocationBtn.click();
    await expect(copyLocationBtn).toHaveText('Kopyalandı!');

    // Download report button is present
    await expect(page.getByTestId('download-report-btn')).toBeVisible();

    // 17. Reload page and assert exactly the newly completed job and report persist
    await page.reload();
    await page.getByTestId('nav-tab-transfers').click();
    await page.getByTestId('op-tab-archive').click();

    const reloadedReportCard = page.getByTestId('conversion-report-card');
    await expect(reloadedReportCard).toBeVisible({ timeout: 15000 });
    const reloadedJobId = (await page.getByTestId('report-job-id').innerText()).trim();
    expect(reloadedJobId).toBe(newJobId);
    await expect(page.getByTestId('output-location-path')).toHaveText(outputLocationText);

    // 18. Assert Job Center selection works and opens this exact newly completed job
    await page.getByTestId('nav-tab-jobs').click();
    await expect(page.getByTestId('jobs-table')).toBeVisible();
    const jobRow = page.getByTestId(`job-row-${newJobId}`);
    await expect(jobRow).toBeVisible({ timeout: 10000 });
    await jobRow.click();

    const detailsPane = page.getByTestId('job-details-pane');
    await expect(detailsPane).toBeVisible();
    await expect(page.getByTestId('job-pause-btn')).toHaveCount(0);

    const openJobBtn = page.getByTestId('job-open-btn');
    await expect(openJobBtn).toBeVisible();
    await openJobBtn.click();

    await expect(page.getByTestId('local-archive-workflow')).toBeVisible();
    await expect(page.getByTestId('conversion-report-card')).toBeVisible({ timeout: 10000 });
    expect((await page.getByTestId('report-job-id').innerText()).trim()).toBe(newJobId);

    // 19. Verify desktop responsiveness (no horizontal overflow)
    const desktopDims = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth,
    }));
    expect(desktopDims.scrollWidth).toBe(desktopDims.innerWidth);

    // 20. Verify 390px mobile responsiveness (zero horizontal overflow)
    await page.setViewportSize({ width: 390, height: 844 });
    const mobileDims = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth,
    }));
    expect(mobileDims.scrollWidth).toBe(mobileDims.innerWidth);

    // Save screenshot for audit evidence
    fs.mkdirSync(QA_SCREENSHOT_DIR, { recursive: true });
    await page.screenshot({
      path: path.join(QA_SCREENSHOT_DIR, `split-real-result-${test.info().project.name}.png`),
      fullPage: true,
    });
  });

  test('UI E2E: Size mode shows honest preview banner, blocks zero-match filter, and reflects real size cap', async ({ page, request }) => {
    // 1. Point browser to real TestingHost runtime
    await page.addInitScript((url) => {
      (window as any).__BITIGMAIL_ENGINE_URL__ = url;
    }, TESTING_HOST_URL);

    // Ensure fixture is reset to genuine-pst
    const sessionRes = await request.post(`${TESTING_HOST_URL}/api/session`, {
      headers: {
        Host: `127.0.0.1:${TESTING_HOST_PORT}`,
        Origin: 'http://127.0.0.1:5173',
        'Content-Type': 'application/json',
      },
      data: {},
    });
    const { token } = await sessionRes.json();
    await request.post(`${TESTING_HOST_URL}/api/testing/set-split-source`, {
      headers: {
        Host: `127.0.0.1:${TESTING_HOST_PORT}`,
        Origin: 'http://127.0.0.1:5173',
        'X-BitigMail-Session': token,
        'Content-Type': 'application/json',
      },
      data: { fixtureId: 'genuine-pst' },
    });

    await page.goto('/');
    await page.evaluate(() => localStorage.clear());
    await page.reload();

    // 2. Navigate to Transfers -> Archive
    await page.getByTestId('nav-tab-transfers').click();
    await page.getByTestId('op-tab-archive').click();

    // 3. Pick source fixture (bounded retry over whichever of reselect-source-btn / pick-source-btn is currently actionable)
    const pickSourceBtn = page.locator('[data-testid="reselect-source-btn"], [data-testid="pick-source-btn"]').first();
    await expect(async () => {
      await pickSourceBtn.click();
    }).toPass({ timeout: 15000 });
    await expect(page.getByTestId('source-filename')).toBeVisible({ timeout: 15000 });
    await expect(page.getByTestId('analysis-preflight-card')).toBeVisible();

    // 4. Switch to Size mode
    const sizeRadio = page.getByTestId('split-mode-size-radio');
    await sizeRadio.click();
    await expect(sizeRadio).toBeChecked();

    // 5. Verify honest size preview notice and size cap input
    await expect(page.getByTestId('split-size-honest-preview')).toBeVisible();
    await expect(page.getByTestId('split-size-honest-preview')).toHaveText(
      'Parça sayısı işlem sırasında belirlenir. Tek bir ileti ekleriyle birlikte bu sınırı aşarsa işlem durur; daha yüksek bir sınır seçmeniz gerekir.'
    );
    const capInput = page.getByTestId('split-size-cap-input');
    await expect(capInput).toBeVisible();
    await expect(capInput).toHaveValue('2048');

    // Change cap to 1024
    await capInput.fill('1024');
    await expect(capInput).toHaveValue('1024');

    // 6. Test zero-match blocking: deselect all folders
    const clearAllBtn = page.getByTestId('clear-all-folders-btn');
    await expect(clearAllBtn).toBeVisible();
    await clearAllBtn.click();

    // Verify zero-match alert appears and start split is blocked
    await expect(page.getByTestId('zero-match-alert')).toBeVisible({ timeout: 10000 });
    await expect(page.getByTestId('preview-selected-count')).toHaveText('0');

    const startBtn = page.getByTestId('start-split-btn');
    if ((await startBtn.count()) > 0) {
      await expect(startBtn).toBeDisabled();
    }
  });

  test('JobCenter & Split Job Navigation: real completed split job appears with type archive, no pause controls, and opens into Archive view with preserved report', async ({ request, page }) => {
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
      'X-BitigMail-Session': token,
      'Content-Type': 'application/json',
    };

    // 2. Run a real split job via API
    await request.post(`${TESTING_HOST_URL}/api/testing/set-split-source`, { headers, data: { fixtureId: 'genuine-pst' } });
    const pickSrc = await (await request.post(`${TESTING_HOST_URL}/api/picker/split-source`, { headers, data: {} })).json();
    await request.post(`${TESTING_HOST_URL}/api/split/source/analyze`, { headers, data: { sourceHandle: pickSrc.handle } });
    const plan = await (await request.post(`${TESTING_HOST_URL}/api/split/plan`, { headers, data: { sourceHandle: pickSrc.handle, splitMode: 'year' } })).json();
    const pickDir = await (await request.post(`${TESTING_HOST_URL}/api/picker/output-dir`, { headers, data: {} })).json();
    expect(pickDir.handle).toMatch(/^dir_[0-9a-f]{32}$/);

    const startRes = await (await request.post(`${TESTING_HOST_URL}/api/split/start`, {
      headers,
      data: {
        planId: plan.planId,
        outputDirHandle: pickDir.handle,
        clientContext: {
          companyId: 'comp-jobcenter-split',
          companyName: 'Bölümleme Holding',
          projectId: 'proj-jobcenter-split',
          projectName: 'JobCenter Bölümleme Testi',
        },
      },
    })).json();

    const splitJobId = startRes.jobId;
    let completed: any = null;
    for (let i = 0; i < 40; i++) {
      await new Promise((r) => setTimeout(r, 400));
      const res = await (await request.get(`${TESTING_HOST_URL}/api/jobs/${splitJobId}`, {
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

    // 3. Open UI and navigate to Job Center
    await page.addInitScript((url) => {
      (window as any).__BITIGMAIL_ENGINE_URL__ = url;
    }, TESTING_HOST_URL);

    await page.goto('/');
    await page.evaluate(() => localStorage.clear());
    await page.reload();

    await page.getByTestId('nav-tab-jobs').click();
    await expect(page.getByTestId('jobs-table')).toBeVisible();

    // 4. Assert split job row exists in Job Center
    const jobRow = page.getByTestId(`job-row-${splitJobId}`);
    await expect(jobRow).toBeVisible({ timeout: 10000 });

    // 5. Select job row and verify details pane
    await jobRow.click();
    const detailsPane = page.getByTestId('job-details-pane');
    await expect(detailsPane).toBeVisible();

    // Assert NO pause control for real split job
    await expect(page.getByTestId('job-pause-btn')).toHaveCount(0);

    // 6. Click 'İşi aç' to navigate to Archive workflow
    const openJobBtn = page.getByTestId('job-open-btn');
    await expect(openJobBtn).toBeVisible();
    await openJobBtn.click();

    // 7. Assert navigation to Archive view and frozen context loaded
    await expect(page.getByTestId('local-archive-workflow')).toBeVisible();
    await expect(page.getByTestId('context-company-name')).toHaveText('Bölümleme Holding');
    await expect(page.getByTestId('context-project-name')).toHaveText('JobCenter Bölümleme Testi');

    // 8. Assert multipart report card is displayed with split parts table
    const reportCard = page.getByTestId('conversion-report-card');
    await expect(reportCard).toBeVisible({ timeout: 10000 });
    await expect(page.getByTestId('report-job-id')).toHaveText(splitJobId);
    await expect(page.getByTestId('split-parts-table')).toBeVisible();

    // 9. Navigate to Reports View and verify JSON report download
    await page.getByTestId('nav-tab-reports').click();
    await expect(page.getByTestId('reports-table')).toBeVisible();

    const reportRow = page.getByTestId(`report-row-${splitJobId}`);
    await expect(reportRow).toBeVisible({ timeout: 10000 });

    const downloadBtn = page.getByTestId(`download-json-${splitJobId}`);
    await expect(downloadBtn).toBeVisible();

    const [download] = await Promise.all([
      page.waitForEvent('download'),
      downloadBtn.click(),
    ]);
    expect(download.suggestedFilename()).toBe(`rapor-${splitJobId}.json`);
  });
});
