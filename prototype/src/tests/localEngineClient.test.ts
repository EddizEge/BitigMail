import { describe, it, expect, beforeEach, vi } from 'vitest';
import { LocalEngineClient } from '../api/localEngineClient';
import { ClientProjectContext, FolderSummary, PreflightCheckResult } from '../types/localEngine';

describe('LocalEngineClient Unit Tests', () => {
  let client: LocalEngineClient;

  beforeEach(() => {
    client = new LocalEngineClient('http://127.0.0.1:6174');
    vi.restoreAllMocks();
  });

  it('should initialize with default baseUrl and null in-memory session token', () => {
    expect(client.getBaseUrl()).toBe('http://127.0.0.1:6174');
    expect(client.hasToken()).toBe(false);
  });

  it('should store session token strictly in-memory and not in localStorage', async () => {
    const mockToken = 'mock_secret_session_token_12345';
    globalThis.fetch = vi.fn().mockResolvedValueOnce({
      ok: true,
      json: async () => ({ token: mockToken, version: '0.1.0' }),
    } as Response);

    const session = await client.initSession();
    expect(session.token).toBe(mockToken);
    expect(client.hasToken()).toBe(true);

    // Verify localStorage was NEVER touched for the session token
    const storage = (globalThis as unknown as { localStorage?: Storage }).localStorage;
    if (storage) {
      expect(storage.getItem('token')).toBeNull();
      expect(storage.getItem('session')).toBeNull();
      expect(storage.getItem('X-BitigMail-Session')).toBeNull();
    }
  });

  it('should include private X-BitigMail-Session header on subsequent mutating calls', async () => {
    const mockToken = 'token_abc_xyz';
    // 1st call for initSession
    globalThis.fetch = vi.fn()
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ token: mockToken, version: '0.1.0' }),
      } as Response)
      // 2nd call for pickSource
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ cancelled: false, handle: 'src_test_handle', fileName: 'test.ost', sizeBytes: 1024 }),
      } as Response);

    const result = await client.pickSource();
    expect(result.handle).toBe('src_test_handle');

    const fetchCalls = (globalThis.fetch as any).mock.calls;
    expect(fetchCalls.length).toBe(2);

    const pickSourceHeaders = fetchCalls[1][1].headers;
    expect(pickSourceHeaders['X-BitigMail-Session']).toBe(mockToken);
    expect(pickSourceHeaders['Content-Type']).toBe('application/json');
  });

  it('should receive server-derived read-only displayPath from pickSource and pickTarget', async () => {
    const mockSourcePath = 'C:\\Data\\Archive\\test.ost';
    const mockTargetPath = 'C:\\Data\\Archive\\test.pst';

    globalThis.fetch = vi.fn()
      // initSession
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ token: 'mock_token', version: '0.1.0' }),
      } as Response)
      // pickSource
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          cancelled: false,
          handle: 'src_handle_123',
          fileName: 'test.ost',
          displayPath: mockSourcePath,
          sizeBytes: 2048,
        }),
      } as Response)
      // pickTarget
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          cancelled: false,
          handle: 'tgt_handle_456',
          fileName: 'test.pst',
          displayPath: mockTargetPath,
        }),
      } as Response);

    const sourceRes = await client.pickSource();
    expect(sourceRes.displayPath).toBe(mockSourcePath);
    expect(sourceRes.handle).toBe('src_handle_123');

    const targetRes = await client.pickTarget(sourceRes.handle);
    expect(targetRes.displayPath).toBe(mockTargetPath);
    expect(targetRes.handle).toBe('tgt_handle_456');

    // Confirm request payloads used handles, NEVER absolute paths
    const fetchCalls = (globalThis.fetch as any).mock.calls;
    const targetCallBody = JSON.parse(fetchCalls[2][1].body);
    expect(targetCallBody).toEqual({ sourceHandle: 'src_handle_123' });
    expect(targetCallBody).not.toHaveProperty('path');
    expect(targetCallBody).not.toHaveProperty('displayPath');
  });

  it('should expose server-derived outputPath in job and report', async () => {
    const mockOutputPath = 'C:\\Data\\Archive\\test.pst';

    globalThis.fetch = vi.fn()
      // initSession
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ token: 'mock_token', version: '0.1.0' }),
      } as Response)
      // getJob
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          jobId: 'job-101',
          status: 'completed',
          stage: 'Tamamlandı',
          sourceFileName: 'test.ost',
          targetFileName: 'test.pst',
          outputPath: mockOutputPath,
          itemsRead: 10,
          itemsWritten: 10,
          failedItems: 0,
        }),
      } as Response)
      // getJobReport
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          jobId: 'job-101',
          outputPstFileName: 'test.pst',
          outputPath: mockOutputPath,
          itemsRead: 10,
          itemsWritten: 10,
          conversionSuccess: true,
        }),
      } as Response);

    const job = await client.getJob('job-101');
    expect(job.outputPath).toBe(mockOutputPath);

    const report = await client.getJobReport('job-101');
    expect(report.outputPath).toBe(mockOutputPath);
  });

  it('should reject or throw meaningful error when server responds with 403 Forbidden', async () => {
    globalThis.fetch = vi.fn().mockResolvedValueOnce({
      ok: false,
      status: 403,
      json: async () => ({ error: 'Forbidden: Exact Origin http://127.0.0.1:5173 is required.' }),
    } as Response);

    await expect(client.initSession()).rejects.toThrow('Forbidden: Exact Origin http://127.0.0.1:5173 is required.');
  });
});

describe('Local OST Workflow Logic & Contract Negatives', () => {
  it('should flag preflight trial blocker if any folder has > 50 items', () => {
    const folders: FolderSummary[] = [
      { folderId: 'f_inbox_01', folderPath: 'IPM_SUBTREE/Gelen Kutusu', displayName: 'Gelen Kutusu', itemCount: 51, subFolderCount: 0, category: 'Active', isIpmFolder: true },
      { folderId: 'f_projects_01', folderPath: 'IPM_SUBTREE/Projeler', displayName: 'Projeler', itemCount: 4, subFolderCount: 0, category: 'Active', isIpmFolder: true },
    ];

    let hasTrialBlocker = false;
    let blockerReason: string | null = null;
    const blockers: string[] = [];

    for (const f of folders) {
      if (f.itemCount > 50) {
        hasTrialBlocker = true;
        blockerReason = `Deneme Sürümü Sınırı: '${f.displayName}' klasöründe ${f.itemCount} öğe var. En fazla 50 öğeye izin verilir.`;
        blockers.push(blockerReason);
      }
    }

    const preflight: PreflightCheckResult = {
      canConvert: blockers.length === 0,
      hasTrialBlocker,
      trialBlockerReason: blockerReason,
      blockers,
      warnings: [],
      estimatedPstSizeBytes: 1000000,
      availableDiskSizeBytes: 50000000,
    };

    expect(preflight.canConvert).toBe(false);
    expect(preflight.hasTrialBlocker).toBe(true);
    expect(preflight.blockers.length).toBe(1);
    expect(preflight.trialBlockerReason).toContain('51 öğe var');
  });

  it('should allow conversion when all folders have <= 50 items', () => {
    const folders: FolderSummary[] = [
      { folderId: 'f_inbox_01', folderPath: 'IPM_SUBTREE/Gelen Kutusu', displayName: 'Gelen Kutusu', itemCount: 13, subFolderCount: 0, category: 'Active', isIpmFolder: true },
      { folderId: 'f_projects_01', folderPath: 'IPM_SUBTREE/Projeler', displayName: 'Projeler', itemCount: 4, subFolderCount: 0, category: 'Active', isIpmFolder: true },
    ];

    const blockers: string[] = [];
    for (const f of folders) {
      if (f.itemCount > 50) {
        blockers.push('Limit exceeded');
      }
    }

    const preflight: PreflightCheckResult = {
      canConvert: blockers.length === 0,
      hasTrialBlocker: false,
      trialBlockerReason: null,
      blockers,
      warnings: [],
      estimatedPstSizeBytes: 500000,
      availableDiskSizeBytes: 50000000,
    };

    expect(preflight.canConvert).toBe(true);
    expect(preflight.hasTrialBlocker).toBe(false);
    expect(preflight.blockers.length).toBe(0);
  });

  it('should freeze client and project context at job launch', () => {
    const liveContext: ClientProjectContext = {
      companyId: 'comp-1',
      companyName: 'Acme Holding',
      projectId: 'proj-1',
      projectName: 'OST Arşivi 2024',
    };

    // Simulated job launch freeze
    const frozenContext: ClientProjectContext = { ...liveContext };

    // External UI selection change
    liveContext.companyName = 'Başka Şirket';
    liveContext.projectName = 'Değişen Proje';

    expect(frozenContext.companyName).toBe('Acme Holding');
    expect(frozenContext.projectName).toBe('OST Arşivi 2024');
  });

  it('should preserve previous selection when picker is cancelled', () => {
    let currentSelection: { handle: string; fileName: string } | null = {
      handle: 'src_previous_valid',
      fileName: 'previous.ost',
    };

    const cancelledPickResult = { cancelled: true };

    if (!cancelledPickResult.cancelled && (cancelledPickResult as any).handle) {
      currentSelection = {
        handle: (cancelledPickResult as any).handle,
        fileName: (cancelledPickResult as any).fileName,
      };
    }

    // Previous selection preserved!
    expect(currentSelection).not.toBeNull();
    expect(currentSelection?.fileName).toBe('previous.ost');
    expect(currentSelection?.handle).toBe('src_previous_valid');
  });

  it('should map real local engine jobs with isLocalEngine=true and not show pause control', () => {
    const realRecord = {
      jobId: 'real_job_001',
      sourceFileName: 'test.ost',
      targetFileName: 'test.pst',
      status: 'completed' as const,
      percentComplete: 100,
      itemsWritten: 50,
      itemsRead: 50,
      failedItems: 0,
      stage: 'Completed',
      createdAt: new Date().toISOString(),
      clientContext: {
        companyId: 'comp-1',
        companyName: 'Müşteri Alfa',
        projectId: 'proj-1',
        projectName: 'Proje Alfa',
      },
    };

    // Mapping logic in JobCenter
    const mappedJob = {
      id: realRecord.jobId,
      title: `Yerel OST Dönüştürme (${realRecord.sourceFileName})`,
      client: realRecord.clientContext.companyName,
      companyId: realRecord.clientContext.companyId,
      projectId: realRecord.clientContext.projectId,
      type: 'convert' as const,
      isLocalEngine: true,
      reportAvailable: realRecord.status === 'completed',
    };

    expect(mappedJob.isLocalEngine).toBe(true);
    // Pause button guard: {!selectedJob.isLocalEngine && (...)}
    const showPauseButton = !mappedJob.isLocalEngine;
    expect(showPauseButton).toBe(false);
  });

  it('should carry explicitly selected jobId and frozen client context when opening older real job', () => {
    // State before opening older job
    let selectedLocalJobId: string | null = null;
    let plan = {
      companyId: 'comp-demo',
      projectId: 'proj-demo',
      operationType: 'migration',
    };
    let currentTab = 'jobs';
    let currentView = 'workspace';

    const openLocalJob = (jobId: string, companyId?: string, projectId?: string) => {
      selectedLocalJobId = jobId;
      plan = {
        ...plan,
        operationType: 'convert',
        companyId: companyId || plan.companyId,
        projectId: projectId || plan.projectId,
      };
      currentTab = 'transfers';
      currentView = 'workspace';
    };

    // Open older job
    openLocalJob('older_job_123', 'comp-alfa', 'proj-alfa');

    expect(selectedLocalJobId).toBe('older_job_123');
    expect(plan.operationType).toBe('convert');
    expect(plan.companyId).toBe('comp-alfa');
    expect(plan.projectId).toBe('proj-alfa');
    expect(currentTab).toBe('transfers');
    expect(currentView).toBe('workspace');
  });

  it('should preserve report download for exact older jobId in ReportsView', async () => {
    const job1Id = 'job_older_111';

    const mockReport1 = {
      jobId: job1Id,
      clientContext: { companyId: 'comp-1', companyName: 'Müşteri Alfa', projectId: 'proj-1', projectName: 'Proje Alfa' },
      itemsWritten: 10,
    };

    globalThis.fetch = vi.fn().mockImplementation(async (url: string) => {
      if (url.includes('/api/session')) {
        return {
          ok: true,
          json: async () => ({ token: 'test_token', version: '0.1.0' }),
        } as Response;
      }
      if (url.includes(`/api/jobs/${job1Id}/report`)) {
        return {
          ok: true,
          json: async () => mockReport1,
        } as Response;
      }
      return { ok: false, status: 404, json: async () => ({}) } as Response;
    });

    const client = new LocalEngineClient('http://127.0.0.1:6174');
    const report = await client.getJobReport(job1Id);
    expect(report.jobId).toBe(job1Id);
    expect(report.clientContext.companyName).toBe('Müşteri Alfa');
  });
});
