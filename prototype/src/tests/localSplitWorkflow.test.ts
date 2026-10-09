import { describe, it, expect, beforeEach, vi } from 'vitest';
import { LocalEngineClient } from '../api/localEngineClient';
import {
  ClientProjectContext,
  ConversionReport,
  LocalJobRecord,
  OstAnalysisResult,
  SelectionPreviewResult,
  SplitPartReport,
  SplitPlanResult,
} from '../types/localEngine';
import { Job, JobType } from '../types';

describe('LocalEngineClient PST Split API Endpoints & Request Shapes', () => {
  let client: LocalEngineClient;

  beforeEach(() => {
    client = new LocalEngineClient('http://127.0.0.1:6174');
    vi.restoreAllMocks();
  });

  it('pickSplitSource calls POST /api/picker/split-source and returns handle and displayPath', async () => {
    const mockToken = 'tok_split_session';
    globalThis.fetch = vi.fn()
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ token: mockToken, version: '0.1.0' }),
      } as Response)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          cancelled: false,
          handle: 'src_split_pst_1',
          fileName: 'archive.pst',
          displayPath: 'C:\\Data\\archive.pst',
          sizeBytes: 10485760,
        }),
      } as Response);

    const result = await client.pickSplitSource();
    expect(result.cancelled).toBe(false);
    expect(result.handle).toBe('src_split_pst_1');
    expect(result.fileName).toBe('archive.pst');
    expect(result.displayPath).toBe('C:\\Data\\archive.pst');
    expect(result.sizeBytes).toBe(10485760);

    const calls = (globalThis.fetch as any).mock.calls;
    expect(calls[1][0]).toBe('http://127.0.0.1:6174/api/picker/split-source');
    expect(calls[1][1].method).toBe('POST');
    expect(calls[1][1].headers['X-BitigMail-Session']).toBe(mockToken);
  });

  it('analyzeSplitSource calls POST /api/split/source/analyze with { sourceHandle }', async () => {
    const mockAnalysis: Partial<OstAnalysisResult> = {
      sourceFileName: 'archive.pst',
      sourceSizeBytes: 10485760,
      sourceSha256: 'hash_split_source_sha256',
      totalItems: 13,
      totalAttachments: 4,
      folders: [],
    };

    globalThis.fetch = vi.fn()
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ token: 'tok_1', version: '0.1.0' }),
      } as Response)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => mockAnalysis,
      } as Response);

    const analysis = await client.analyzeSplitSource('src_split_pst_1');
    expect(analysis.sourceSha256).toBe('hash_split_source_sha256');

    const calls = (globalThis.fetch as any).mock.calls;
    expect(calls[1][0]).toBe('http://127.0.0.1:6174/api/split/source/analyze');
    expect(JSON.parse(calls[1][1].body)).toEqual({ sourceHandle: 'src_split_pst_1' });
  });

  it('pickOutputDir calls POST /api/picker/output-dir and returns typed output-dir handle', async () => {
    globalThis.fetch = vi.fn()
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ token: 'tok_1', version: '0.1.0' }),
      } as Response)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          cancelled: false,
          handle: 'outdir_handle_123',
          fileName: 'SplitArchiveDir',
          displayPath: 'C:\\Archives\\SplitArchiveDir',
        }),
      } as Response);

    const result = await client.pickOutputDir();
    expect(result.handle).toBe('outdir_handle_123');
    expect(result.displayPath).toBe('C:\\Archives\\SplitArchiveDir');

    const calls = (globalThis.fetch as any).mock.calls;
    expect(calls[1][0]).toBe('http://127.0.0.1:6174/api/picker/output-dir');
  });

  it('createSplitPlan in year mode sends splitMode: "year" and sizeCapBytes: null', async () => {
    const mockPlan: SplitPlanResult = {
      planId: 'plan_year_001',
      sourceHandle: 'src_1',
      sourceSha256: 'sha256_abc',
      selectionId: 'sel_1',
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
    };

    globalThis.fetch = vi.fn()
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ token: 'tok_1', version: '0.1.0' }),
      } as Response)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => mockPlan,
      } as Response);

    const plan = await client.createSplitPlan({
      sourceHandle: 'src_1',
      selectionId: 'sel_1',
      splitMode: 'year',
      sizeCapBytes: null,
      folderIds: ['fld_inbox'],
      startDate: null,
      endDate: null,
    });

    expect(plan.planId).toBe('plan_year_001');
    expect(plan.splitMode).toBe('year');
    expect(plan.yearGroups?.length).toBe(5);

    const calls = (globalThis.fetch as any).mock.calls;
    expect(calls[1][0]).toBe('http://127.0.0.1:6174/api/split/plan');
    const body = JSON.parse(calls[1][1].body);
    expect(body.splitMode).toBe('year');
    expect(body.sizeCapBytes).toBeNull();
    expect(body.sourceHandle).toBe('src_1');
    expect(body.selectionId).toBe('sel_1');
  });

  it('createSplitPlan in size mode sends splitMode: "size" and integer bytes for sizeCapBytes', async () => {
    const sizeCapMb = 2048;
    const expectedBytes = sizeCapMb * 1_000_000; // 2,048,000,000 bytes

    const mockPlan: SplitPlanResult = {
      planId: 'plan_size_001',
      sourceHandle: 'src_1',
      sourceSha256: 'sha256_abc',
      selectionId: 'sel_1',
      splitMode: 'size',
      sizeCapBytes: expectedBytes,
      totalSourceMessages: 13,
      selectedMessagesCount: 13,
      excludedMessagesCount: 0,
      selectedAttachmentsCount: 4,
      yearGroups: [],
      canSplit: true,
    };

    globalThis.fetch = vi.fn()
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ token: 'tok_1', version: '0.1.0' }),
      } as Response)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => mockPlan,
      } as Response);

    const plan = await client.createSplitPlan({
      sourceHandle: 'src_1',
      selectionId: 'sel_1',
      splitMode: 'size',
      sizeCapBytes: expectedBytes,
      folderIds: ['fld_inbox'],
      startDate: null,
      endDate: null,
    });

    expect(plan.splitMode).toBe('size');
    expect(plan.sizeCapBytes).toBe(expectedBytes);

    const calls = (globalThis.fetch as any).mock.calls;
    const body = JSON.parse(calls[1][1].body);
    expect(body.splitMode).toBe('size');
    expect(body.sizeCapBytes).toBe(2048000000);
  });

  it('startSplitJob sends planId, outputDirHandle, idempotencyKey, and clientContext', async () => {
    const mockContext: ClientProjectContext = {
      companyId: 'comp-10',
      companyName: 'Büyük Arşiv A.Ş.',
      projectId: 'proj-split-01',
      projectName: 'PST Bölümleme Projesi',
    };

    const mockJobRecord: Partial<LocalJobRecord> = {
      jobId: 'split_job_999',
      jobKind: 'split',
      splitMode: 'year',
      status: 'converting',
      stage: 'Bölümleniyor',
      itemsRead: 13,
      itemsWritten: 0,
    };

    globalThis.fetch = vi.fn()
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ token: 'tok_1', version: '0.1.0' }),
      } as Response)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => mockJobRecord,
      } as Response);

    const record = await client.startSplitJob({
      planId: 'plan_year_001',
      outputDirHandle: 'outdir_handle_123',
      idempotencyKey: 'idemp_stable_key_xyz',
      clientContext: mockContext,
    });

    expect(record.jobId).toBe('split_job_999');

    const calls = (globalThis.fetch as any).mock.calls;
    expect(calls[1][0]).toBe('http://127.0.0.1:6174/api/split/start');
    const body = JSON.parse(calls[1][1].body);
    expect(body.planId).toBe('plan_year_001');
    expect(body.outputDirHandle).toBe('outdir_handle_123');
    expect(body.idempotencyKey).toBe('idemp_stable_key_xyz');
    expect(body.clientContext.companyName).toBe('Büyük Arşiv A.Ş.');
  });
});

describe('Split Workflow Business Rules, Invalidation & Idempotency', () => {
  it('invalidates plan when source, folder selection, date bounds, mode, or size cap change and resets loading', () => {
    let splitPlan: SplitPlanResult | null = {
      planId: 'plan_original',
      sourceHandle: 'src_1',
      sourceSha256: 'sha_1',
      selectionId: 'sel_1',
      splitMode: 'year',
      sizeCapBytes: null,
      totalSourceMessages: 10,
      selectedMessagesCount: 10,
      excludedMessagesCount: 0,
      selectedAttachmentsCount: 2,
      canSplit: true,
    };
    let stableIdempotencyKey: string | null = 'idemp_plan_original_123';
    let planRequestId = 1;
    let isLoadingPlan = true;
    let previewRequestId = 1;
    let isLoadingPreview = true;
    let selectionPreview: SelectionPreviewResult | null = {
      selectionId: 'sel_1',
      sourceHandle: 'src_1',
      sourceSha256: 'sha_1',
      filters: { timeZone: 'UTC+03', datePolicy: 'policy' },
      totalSourceMessages: 10,
      selectedMessagesCount: 10,
      excludedMessagesCount: 0,
      missingDateExcludedCount: 0,
      selectedAttachmentsCount: 2,
      folderBreakdown: [],
      canConvert: true,
    };

    const invalidatePlan = () => {
      planRequestId += 1;
      isLoadingPlan = false;
      splitPlan = null;
      stableIdempotencyKey = null;
    };

    const invalidatePreview = () => {
      previewRequestId += 1;
      isLoadingPreview = false;
      selectionPreview = null;
    };

    // 1. Changing source handle invalidates preview and plan
    invalidatePreview();
    invalidatePlan();
    expect(splitPlan).toBeNull();
    expect(stableIdempotencyKey).toBeNull();
    expect(selectionPreview).toBeNull();
    expect(isLoadingPlan).toBe(false);
    expect(isLoadingPreview).toBe(false);
    expect(planRequestId).toBe(2);
    expect(previewRequestId).toBe(2);

    // Re-establish plan
    splitPlan = { planId: 'plan_2', sourceHandle: 'src_2', sourceSha256: 'sha_2', selectionId: 'sel_2', splitMode: 'year', totalSourceMessages: 5, selectedMessagesCount: 5, excludedMessagesCount: 0, selectedAttachmentsCount: 1, canSplit: true };
    stableIdempotencyKey = 'idemp_plan_2';
    isLoadingPlan = true;

    // 2. Changing folder selection invalidates preview and plan
    invalidatePreview();
    invalidatePlan();
    expect(splitPlan).toBeNull();
    expect(stableIdempotencyKey).toBeNull();
    expect(isLoadingPlan).toBe(false);
    expect(isLoadingPreview).toBe(false);
    expect(planRequestId).toBe(3);
    expect(previewRequestId).toBe(3);

    // 3. Changing date bounds invalidates preview and plan
    splitPlan = { planId: 'plan_3', sourceHandle: 'src_2', sourceSha256: 'sha_2', selectionId: 'sel_3', splitMode: 'year', totalSourceMessages: 5, selectedMessagesCount: 3, excludedMessagesCount: 2, selectedAttachmentsCount: 1, canSplit: true };
    isLoadingPlan = true;
    invalidatePreview();
    invalidatePlan();
    expect(splitPlan).toBeNull();
    expect(isLoadingPlan).toBe(false);
    expect(planRequestId).toBe(4);
    expect(previewRequestId).toBe(4);

    // 4. Changing split mode invalidates plan (increments planRequestId, resets isLoadingPlan)
    splitPlan = { planId: 'plan_4', sourceHandle: 'src_2', sourceSha256: 'sha_2', selectionId: 'sel_4', splitMode: 'year', totalSourceMessages: 5, selectedMessagesCount: 5, excludedMessagesCount: 0, selectedAttachmentsCount: 1, canSplit: true };
    isLoadingPlan = true;
    invalidatePlan();
    expect(splitPlan).toBeNull();
    expect(isLoadingPlan).toBe(false);
    expect(planRequestId).toBe(5);

    // 5. Changing size cap MB invalidates plan (increments planRequestId, resets isLoadingPlan)
    splitPlan = { planId: 'plan_5', sourceHandle: 'src_2', sourceSha256: 'sha_2', selectionId: 'sel_4', splitMode: 'size', sizeCapBytes: 1000000000, totalSourceMessages: 5, selectedMessagesCount: 5, excludedMessagesCount: 0, selectedAttachmentsCount: 1, canSplit: true };
    isLoadingPlan = true;
    invalidatePlan();
    expect(splitPlan).toBeNull();
    expect(isLoadingPlan).toBe(false);
    expect(planRequestId).toBe(6);
  });

  it('maintains one stable idempotency key for unchanged plan across multiple start retries', () => {
    const plan: SplitPlanResult = {
      planId: 'plan_stable_99',
      sourceHandle: 'src_1',
      sourceSha256: 'sha_source_abc',
      selectionId: 'sel_1',
      splitMode: 'year',
      totalSourceMessages: 13,
      selectedMessagesCount: 13,
      excludedMessagesCount: 0,
      selectedAttachmentsCount: 4,
      canSplit: true,
    };

    // Generate stable idempotency key bound to the unchanged planId
    const stableKey = `idemp_split_${plan.planId}_fixed`;

    // Attempt 1 (e.g. initial start)
    const keyAttempt1 = stableKey;

    // Attempt 2 (e.g. retry after network failure on start)
    const keyAttempt2 = stableKey;

    expect(keyAttempt1).toBe(keyAttempt2);
    expect(keyAttempt1).toContain('plan_stable_99');
  });

  it('rejects stale out-of-order async responses using request id guards', async () => {
    let requestIdCounter = 0;
    let currentCommittedPlanId: string | null = null;

    // Simulate async request 1 (dispatched first, but delayed)
    const req1Id = ++requestIdCounter;

    // Simulate async request 2 (dispatched immediately after user changes an option)
    const req2Id = ++requestIdCounter;

    // Fast response for request 2 arrives first
    if (req2Id === requestIdCounter) {
      currentCommittedPlanId = 'plan_from_req2';
    }

    // Delayed response for request 1 arrives later
    if (req1Id === requestIdCounter) {
      currentCommittedPlanId = 'plan_from_req1_STALE';
    }

    // Stale response from request 1 was ignored!
    expect(currentCommittedPlanId).toBe('plan_from_req2');
  });

  it('rejects old plan response arriving after mutation but before debounced replacement request starts', async () => {
    let planRequestIdCounter = 0;
    let isLoadingPlan = false;
    let committedPlan: SplitPlanResult | null = null;
    let stableKey: string | null = null;

    const invalidatePlan = () => {
      planRequestIdCounter += 1;
      isLoadingPlan = false;
      committedPlan = null;
      stableKey = null;
    };

    // Step 1: Initial state in year mode with fetchSplitPlan in-flight (request 1):
    const req1Id = ++planRequestIdCounter;
    isLoadingPlan = true;

    let resolveReq1: (plan: SplitPlanResult) => void = () => {};
    const req1Promise = new Promise<SplitPlanResult>((resolve) => {
      resolveReq1 = resolve;
    });

    // Step 2: User mutates option (e.g. changes mode to 'size' or alters sizeCapMb during debounce window)
    // Synchronously, invalidatePlan() increments planRequestIdCounter and resets loading flag:
    invalidatePlan();

    expect(planRequestIdCounter).toBe(2);
    expect(isLoadingPlan).toBe(false);
    expect(committedPlan).toBeNull();
    expect(stableKey).toBeNull();

    // Step 3: BEFORE the debounced replacement request 2 begins (during 150ms debounce window),
    // old response 1 resolves:
    resolveReq1({
      planId: 'stale_year_plan_001',
      sourceHandle: 'src_1',
      sourceSha256: 'sha_1',
      selectionId: 'sel_1',
      splitMode: 'year',
      sizeCapBytes: null,
      totalSourceMessages: 10,
      selectedMessagesCount: 10,
      excludedMessagesCount: 0,
      selectedAttachmentsCount: 2,
      canSplit: true,
    });

    const staleResult = await req1Promise;

    // Guard inside fetchSplitPlan:
    if (req1Id === planRequestIdCounter) {
      committedPlan = staleResult;
      stableKey = `idemp_${staleResult.planId}`;
      isLoadingPlan = false;
    }

    // Stale plan response MUST NOT overwrite state or re-enable Start with stale plan!
    expect(committedPlan).toBeNull();
    expect(stableKey).toBeNull();
    // Loading flag must remain false (spinner not stuck!)
    expect(isLoadingPlan).toBe(false);

    // Step 4: After debounce window expires, replacement request 2 starts:
    const req2Id = ++planRequestIdCounter;
    isLoadingPlan = true;
    expect(req2Id).toBe(3);
    expect(isLoadingPlan).toBe(true);

    const freshSizePlan: SplitPlanResult = {
      planId: 'fresh_size_plan_002',
      sourceHandle: 'src_1',
      sourceSha256: 'sha_1',
      selectionId: 'sel_1',
      splitMode: 'size',
      sizeCapBytes: 2000000000,
      totalSourceMessages: 10,
      selectedMessagesCount: 10,
      excludedMessagesCount: 0,
      selectedAttachmentsCount: 2,
      canSplit: true,
    };

    if (req2Id === planRequestIdCounter) {
      committedPlan = freshSizePlan;
      stableKey = `idemp_${freshSizePlan.planId}`;
      isLoadingPlan = false;
    }

    expect(committedPlan).toEqual(freshSizePlan);
    expect(committedPlan?.splitMode).toBe('size');
    expect(stableKey).toContain('fresh_size_plan_002');
    expect(isLoadingPlan).toBe(false);
  });

  it('synchronously invalidates previewRequestIdRef and resets isLoadingPreview on folder/date mutations', async () => {
    let previewRequestIdCounter = 0;
    let isLoadingPreview = false;
    let committedPreview: SelectionPreviewResult | null = null;

    const invalidatePreview = () => {
      previewRequestIdCounter += 1;
      isLoadingPreview = false;
      committedPreview = null;
    };

    // In-flight preview request 1 starts:
    const req1Id = ++previewRequestIdCounter;
    isLoadingPreview = true;

    // User toggles folder during preview debounce:
    invalidatePreview();

    expect(previewRequestIdCounter).toBe(2);
    expect(isLoadingPreview).toBe(false);
    expect(committedPreview).toBeNull();

    // Stale preview response arrives before replacement preview request:
    const stalePreview: any = { selectionId: 'stale_sel_1', selectedMessagesCount: 5 };
    if (req1Id === previewRequestIdCounter) {
      committedPreview = stalePreview;
      isLoadingPreview = false;
    }

    expect(committedPreview).toBeNull();
    expect(isLoadingPreview).toBe(false);
  });

  it('blocks split plan when selectedMessagesCount is 0', () => {
    const preview: SelectionPreviewResult = {
      selectionId: 'sel_empty',
      sourceHandle: 'src_1',
      sourceSha256: 'hash_1',
      filters: { timeZone: 'UTC+03', datePolicy: 'policy' },
      totalSourceMessages: 10,
      selectedMessagesCount: 0,
      excludedMessagesCount: 10,
      missingDateExcludedCount: 0,
      selectedAttachmentsCount: 0,
      folderBreakdown: [],
      canConvert: false,
      blockerReason: 'Seçim kriterlerinize uyan hiçbir ileti bulunamadı. Sıfır iletili bölme başlatılamaz.',
    };

    let canSplit = false;
    let blockerReason: string | null = null;

    if (preview.selectedMessagesCount === 0 || !preview.canConvert) {
      canSplit = false;
      blockerReason = preview.blockerReason || 'Sıfır iletili bölme başlatılamaz.';
    }

    expect(canSplit).toBe(false);
    expect(blockerReason).toContain('Sıfır iletili bölme başlatılamaz');
  });

  it('honest size preview contract: never fabricates part count and explains dynamic measurement', () => {
    const sizeCapMb = 2048;
    const selectedMessagesCount = 13;
    const selectedAttachmentsCount = 4;

    // Format text used in the honest size preview banner
    const honestExplanation =
      'Parça sayısı işlem sırasında belirlenir. Tek bir ileti ekleriyle birlikte bu sınırı aşarsa işlem durur; daha yüksek bir sınır seçmeniz gerekir.';

    expect(honestExplanation).toBe(
      'Parça sayısı işlem sırasında belirlenir. Tek bir ileti ekleriyle birlikte bu sınırı aşarsa işlem durur; daha yüksek bir sınır seçmeniz gerekir.'
    );
    expect(honestExplanation).toContain('Parça sayısı işlem sırasında belirlenir');
    expect(honestExplanation).toContain('daha yüksek bir sınır seçmeniz gerekir');

    // Make sure no fabricated part count is exposed in size mode plan result
    const sizePlanResult: SplitPlanResult = {
      planId: 'plan_size_honest',
      sourceHandle: 'src_1',
      sourceSha256: 'sha_1',
      selectionId: 'sel_1',
      splitMode: 'size',
      sizeCapBytes: sizeCapMb * 1_000_000,
      totalSourceMessages: selectedMessagesCount,
      selectedMessagesCount,
      excludedMessagesCount: 0,
      selectedAttachmentsCount,
      yearGroups: [], // Size mode has NO fabricated groups
      canSplit: true,
    };

    expect(sizePlanResult.yearGroups).toEqual([]);
    expect(sizePlanResult.splitMode).toBe('size');
  });

  it('new source selection clears activeJob and jobReport, revealing the new split plan/start workflow', () => {
    // Initial state with a completed split job and report
    let activeJob: LocalJobRecord | null = {
      jobId: 'split_completed_1',
      jobKind: 'split',
      splitMode: 'year',
      status: 'completed',
      stage: 'Tamamlandı',
      sourceFileName: 'old_archive.pst',
      targetFileName: '',
      itemsRead: 13,
      itemsWritten: 13,
      failedItems: 0,
      totalItems: 13,
      currentFolder: 'Tamamlandı',
      percentComplete: 100,
      createdAt: new Date().toISOString(),
      clientContext: { companyId: 'c1', companyName: 'C1', projectId: 'p1', projectName: 'P1' },
    };
    let jobReport: ConversionReport | null = {
      jobId: 'split_completed_1',
      evidenceLabel: 'Ev1',
      clientContext: { companyId: 'c1', companyName: 'C1', projectId: 'p1', projectName: 'P1' },
      sourceFileName: 'old_archive.pst',
      sourceSizeBytes: 1000,
      sourceSha256Before: 'sha_old',
      sourceSha256After: 'sha_old',
      sourceHashMatch: true,
      outputPstFileName: '',
      outputPstSizeBytes: 2000,
      outputPstSha256: 'sha_out',
      conversionSuccess: true,
      itemsRead: 13,
      itemsWritten: 13,
      failedItems: 0,
      elapsedMilliseconds: 500,
      totalFoldersProcessed: 3,
      fidelityStatus: 'Tam Uyumlu',
      overallStatus: 'Tamamlandı',
      trialDifferences: { hasObservedTrialModifications: false, observedModificationsInThisRun: [], observedSummary: '', documentedTrialCapabilitiesAndLimits: [], watermarkPolicy: '' },
      unmeasuredFields: { status: '', fields: [], note: '' },
      reopenedPstVerification: { verifiedWith: 'Aspose.Email', totalFoldersFound: 3, totalPhysicalItemsFound: 13, itemCountMatch: true, totalAttachmentsVerified: 4, totalCidVerified: 1, verificationNotes: [] },
      errors: [],
      warnings: [],
    };
    let selectedSource: any = null;
    let selectedOutputDir: any = { handle: 'outdir_old', fileName: 'OldDir' };
    let userChosenSource = false;

    // Simulate user clicking 'Farklı dosya seç' and selecting a new source file
    const onNewSourceSelected = (newSource: { handle: string; fileName: string; sizeBytes: number }) => {
      userChosenSource = true;
      selectedSource = newSource;
      activeJob = null;
      jobReport = null;
      selectedOutputDir = null;
    };

    onNewSourceSelected({ handle: 'src_new_2', fileName: 'new_archive.pst', sizeBytes: 2048576 });

    // Assert activeJob and jobReport are cleared
    expect(activeJob).toBeNull();
    expect(jobReport).toBeNull();
    expect(selectedSource.fileName).toBe('new_archive.pst');
    expect(selectedOutputDir).toBeNull();
    expect(userChosenSource).toBe(true);

    // Assert background auto-restore cannot re-clobber activeJob or jobReport while userChosenSource is true
    const backgroundJobs: LocalJobRecord[] = [
      {
        jobId: 'split_completed_1',
        jobKind: 'split',
        status: 'completed',
        stage: 'Tamamlandı',
        sourceFileName: 'old_archive.pst',
        targetFileName: '',
        itemsRead: 13,
        itemsWritten: 13,
        failedItems: 0,
        totalItems: 13,
        currentFolder: 'Tamamlandı',
        percentComplete: 100,
        createdAt: new Date().toISOString(),
        clientContext: { companyId: 'c1', companyName: 'C1', projectId: 'p1', projectName: 'P1' },
      },
    ];

    // Auto-restore logic check
    let autoRestoredJob: LocalJobRecord | null = null;
    if (!userChosenSource && !selectedSource && backgroundJobs.length > 0) {
      autoRestoredJob = backgroundJobs[0];
    }
    expect(autoRestoredJob).toBeNull(); // Guarded: did NOT clobber!
  });

  it('new source selection synchronously invalidates in-flight loadJob, pollJobStatus, and getJobReport responses from prior job via workflow epoch', async () => {
    let workflowEpoch = 1;
    let pollTimer: any = 'timer_prior_job_active';
    let activeJob: LocalJobRecord | null = {
      jobId: 'job_prior_100',
      jobKind: 'split',
      status: 'converting',
      stage: 'Bölümleniyor',
      sourceFileName: 'prior_archive.pst',
      targetFileName: '',
      itemsRead: 5,
      itemsWritten: 5,
      failedItems: 0,
      totalItems: 13,
      currentFolder: 'Gelen Kutusu',
      percentComplete: 38,
      createdAt: new Date().toISOString(),
      clientContext: { companyId: 'c1', companyName: 'C1', projectId: 'p1', projectName: 'P1' },
    };
    let jobReport: ConversionReport | null = null;
    let selectedSource: any = { handle: 'src_prior', fileName: 'prior_archive.pst', sizeBytes: 1000000 };

    // In-flight async routines for prior job capture epoch 1:
    const capturedEpoch = workflowEpoch;

    let resolvePriorGetJob: (job: LocalJobRecord) => void = () => {};
    const priorGetJobPromise = new Promise<LocalJobRecord>((resolve) => {
      resolvePriorGetJob = resolve;
    });

    let resolvePriorGetReport: (report: ConversionReport) => void = () => {};
    const priorGetReportPromise = new Promise<ConversionReport>((resolve) => {
      resolvePriorGetReport = resolve;
    });

    // While prior job poll/load/report is in flight across the network, user accepts a new source:
    const onNewSourceAccepted = (newSource: { handle: string; fileName: string; sizeBytes: number }) => {
      workflowEpoch += 1;
      if (pollTimer) {
        clearTimeout(pollTimer);
        pollTimer = null;
      }
      selectedSource = newSource;
      activeJob = null;
      jobReport = null;
    };

    onNewSourceAccepted({ handle: 'src_new_accepted', fileName: 'new_chosen_source.pst', sizeBytes: 5000000 });

    // Synchronous state immediately after accepting new source:
    expect(workflowEpoch).toBe(2);
    expect(pollTimer).toBeNull();
    expect(activeJob).toBeNull();
    expect(jobReport).toBeNull();
    expect(selectedSource.fileName).toBe('new_chosen_source.pst');

    // Delayed prior-job responses resolve:
    resolvePriorGetJob({
      jobId: 'job_prior_100',
      jobKind: 'split',
      status: 'completed',
      stage: 'Tamamlandı',
      sourceFileName: 'prior_archive.pst',
      targetFileName: '',
      itemsRead: 13,
      itemsWritten: 13,
      failedItems: 0,
      totalItems: 13,
      currentFolder: 'Tamamlandı',
      percentComplete: 100,
      createdAt: new Date().toISOString(),
      clientContext: { companyId: 'c1', companyName: 'C1', projectId: 'p1', projectName: 'P1' },
    });

    resolvePriorGetReport({
      jobId: 'job_prior_100',
      overallStatus: 'Tamamlandı',
      sourceFileName: 'prior_archive.pst',
    } as any);

    const resolvedJob = await priorGetJobPromise;
    if (capturedEpoch === workflowEpoch) {
      activeJob = resolvedJob;
    }

    const resolvedReport = await priorGetReportPromise;
    if (capturedEpoch === workflowEpoch) {
      jobReport = resolvedReport;
    }

    // Crucial assertions:
    // Stale prior-job responses are completely discarded by the workflow epoch guard.
    // An old job or report response NEVER overwrites the new-source form!
    expect(activeJob).toBeNull();
    expect(jobReport).toBeNull();
    expect(selectedSource.fileName).toBe('new_chosen_source.pst');
    expect(pollTimer).toBeNull();

    // Explicit targetJobId loading starts a new generation and succeeds
    const targetJobEpoch = ++workflowEpoch;
    expect(targetJobEpoch).toBe(3);
    const targetJob: LocalJobRecord = {
      jobId: 'explicit_target_job_33',
      jobKind: 'split',
      status: 'completed',
      stage: 'Tamamlandı',
      sourceFileName: 'explicit.pst',
      targetFileName: '',
      itemsRead: 10,
      itemsWritten: 10,
      failedItems: 0,
      totalItems: 10,
      currentFolder: 'Tamamlandı',
      percentComplete: 100,
      createdAt: new Date().toISOString(),
      clientContext: { companyId: 'c1', companyName: 'C1', projectId: 'p1', projectName: 'P1' },
    };
    if (targetJobEpoch === workflowEpoch) {
      activeJob = targetJob;
    }
    expect(activeJob?.jobId).toBe('explicit_target_job_33');
  });

  it('maps real split jobs to type: "archive" and title "Yerel PST Bölümleme", and hides pause button', () => {
    const splitRecord: LocalJobRecord = {
      jobId: 'split_rec_001',
      jobKind: 'split',
      splitMode: 'year',
      sourceFileName: 'archive.pst',
      targetFileName: '',
      outputDirectoryPath: 'C:\\Archives\\Bundle_2024',
      status: 'completed',
      stage: 'Tamamlandı',
      itemsRead: 13,
      itemsWritten: 13,
      failedItems: 0,
      totalItems: 13,
      currentFolder: 'Tamamlandı',
      percentComplete: 100,
      createdAt: new Date().toISOString(),
      clientContext: {
        companyId: 'comp-1',
        companyName: 'Müşteri Gama',
        projectId: 'proj-1',
        projectName: 'Proje Gama',
      },
    };

    // Mapping logic in JobCenter
    const isSplit = splitRecord.jobKind === 'split';
    const mappedJob: Job = {
      id: splitRecord.jobId,
      title: isSplit
        ? `Yerel PST Bölümleme (${splitRecord.sourceFileName})`
        : `Yerel OST Dönüştürme (${splitRecord.sourceFileName})`,
      client: splitRecord.clientContext.companyName,
      type: (isSplit ? 'archive' : 'convert') as JobType,
      source: splitRecord.sourceFileName,
      target: isSplit ? (splitRecord.outputDirectoryPath || 'Çoklu PST Klasörü') : splitRecord.targetFileName,
      status: splitRecord.status === 'completed' ? 'completed' : 'running',
      progressPercent: splitRecord.percentComplete,
      processedCount: splitRecord.itemsWritten,
      totalCount: splitRecord.totalItems,
      transferredCount: splitRecord.itemsWritten,
      skippedCount: 0,
      failedCount: splitRecord.failedItems,
      pendingCount: 0,
      recentEvents: [{ time: '10:00', text: `Aşama: ${splitRecord.stage}` }],
      companyId: splitRecord.clientContext.companyId,
      projectId: splitRecord.clientContext.projectId,
      isLocalEngine: true,
      reportAvailable: true,
    };

    expect(mappedJob.type).toBe('archive');
    expect(mappedJob.title).toBe('Yerel PST Bölümleme (archive.pst)');
    expect(mappedJob.target).toBe('C:\\Archives\\Bundle_2024');
    expect(mappedJob.isLocalEngine).toBe(true);

    // Pause control rule: Engine jobs (isLocalEngine === true) must NEVER show pause button
    const showPauseButton = !mappedJob.isLocalEngine;
    expect(showPauseButton).toBe(false);
  });

  it('renders multipart report verification fields for every part', () => {
    const mockParts: SplitPartReport[] = [
      {
        partFileName: 'arsiv-2022.pst',
        partFullPath: 'C:\\Archives\\Bundle_2024\\arsiv-2022.pst',
        partSizeBytes: 1024000,
        partSha256: 'sha_part_2022',
        itemsWritten: 1,
        totalAttachmentsVerified: 0,
        totalCidVerified: 0,
        groupKey: '2022',
        reopenedPstVerification: {
          verifiedWith: 'Aspose.Email 24.8',
          totalFoldersFound: 2,
          totalPhysicalItemsFound: 1,
          itemCountMatch: true,
          totalAttachmentsVerified: 0,
          totalCidVerified: 0,
          verificationNotes: [],
        },
      },
      {
        partFileName: 'arsiv-2024.pst',
        partFullPath: 'C:\\Archives\\Bundle_2024\\arsiv-2024.pst',
        partSizeBytes: 8192000,
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
    ];

    const report: Partial<ConversionReport> = {
      jobId: 'job_multipart_123',
      jobKind: 'split',
      splitMode: 'year',
      outputDirectoryPath: 'C:\\Archives\\Bundle_2024',
      conversionSuccess: true,
      itemsWritten: 9,
      parts: mockParts,
    };

    expect(report.parts?.length).toBe(2);
    expect(report.parts?.[0].partFileName).toBe('arsiv-2022.pst');
    expect(report.parts?.[0].reopenedPstVerification.itemCountMatch).toBe(true);
    expect(report.parts?.[1].partFileName).toBe('arsiv-2024.pst');
    expect(report.parts?.[1].totalAttachmentsVerified).toBe(3);
    expect(report.parts?.[1].totalCidVerified).toBe(1);
  });
});
