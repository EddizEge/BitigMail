// @vitest-environment jsdom
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useImapTransfer } from '../hooks/useImapTransfer';
import { LocalEngineClient } from '../api/localEngineClient';
import {
  ImapAccountPublicDto,
  ImapFolderDto,
  ImapTransferPreviewResponse,
  LocalJobRecord,
} from '../types/localEngine';

describe('IMAP Transfer Workflow Unit Tests', () => {
  let mockClient: LocalEngineClient;

  const sampleAccounts: ImapAccountPublicDto[] = [
    {
      accountId: 'acc-src-1',
      companyId: 'comp-1',
      projectId: 'proj-1',
      displayName: 'Kaynak Posta',
      email: 'kaynak@sirket.com',
      host: 'imap.kaynak.com',
      port: 993,
      tlsMode: 'ssl',
      username: 'kaynak@sirket.com',
      version: 1,
      createdAtUtc: '2026-09-13T12:00:00Z',
      updatedAtUtc: '2026-09-13T12:00:00Z',
    },
    {
      accountId: 'acc-tgt-2',
      companyId: 'comp-1',
      projectId: 'proj-1',
      displayName: 'Hedef Posta',
      email: 'hedef@sirket.com',
      host: 'imap.hedef.com',
      port: 993,
      tlsMode: 'ssl',
      username: 'hedef@sirket.com',
      version: 1,
      createdAtUtc: '2026-09-13T12:00:00Z',
      updatedAtUtc: '2026-09-13T12:00:00Z',
    },
  ];

  const sampleFolders: ImapFolderDto[] = [
    {
      name: 'INBOX',
      fullPath: 'INBOX',
      delimiter: '/',
      isSelectable: true,
      messageCount: 12,
      countValid: true,
    },
    {
      name: 'İstanbul',
      fullPath: 'İstanbul/2026',
      delimiter: '/',
      isSelectable: true,
      messageCount: 5,
      countValid: true,
    },
  ];

  beforeEach(() => {
    vi.restoreAllMocks();
    mockClient = new LocalEngineClient('http://127.0.0.1:6174');
    mockClient.listAccounts = vi.fn().mockResolvedValue(sampleAccounts);
    mockClient.listAccountFolders = vi.fn().mockResolvedValue({ accountId: 'acc-src-1', folders: sampleFolders });
  });

  it('enforces preview payload exactness and non-empty folder selection', async () => {
    const { result } = renderHook(() =>
      useImapTransfer({ companyId: 'comp-1', projectId: 'proj-1', client: mockClient, autoFetchAccounts: false })
    );

    await act(async () => {
      await result.current.loadAccounts();
    });

    // 1. Trying preview with no accounts selected must throw
    await expect(result.current.createPreview()).rejects.toThrow('Kaynak ve hedef IMAP hesapları seçilmelidir.');

    // 2. Selecting same account for source and target must throw
    act(() => {
      result.current.setSourceAccountId('acc-src-1');
      result.current.setTargetAccountId('acc-src-1');
    });
    await expect(result.current.createPreview()).rejects.toThrow('Kaynak ve hedef hesap aynı olamaz.');

    // 3. Different accounts but zero selected folders must throw
    act(() => {
      result.current.setTargetAccountId('acc-tgt-2');
      result.current.deselectAllFolders();
    });
    await expect(result.current.createPreview()).rejects.toThrow('En az bir klasör seçilmelidir.');

    // 4. Valid selection sends exact preview payload
    act(() => {
      result.current.toggleFolderSelection('INBOX');
      result.current.toggleFolderSelection('İstanbul/2026');
      result.current.setFolderMapping('İstanbul/2026', 'Istanbul_Arsiv');
      result.current.setStartDate('2024-01-01');
      result.current.setEndDate('2024-02-16');
    });

    const mockPreviewResponse: ImapTransferPreviewResponse = {
      previewId: 'prev-12345',
      companyId: 'comp-1',
      projectId: 'proj-1',
      sourceAccountId: 'acc-src-1',
      sourceAccountVersion: 1,
      targetAccountId: 'acc-tgt-2',
      targetAccountVersion: 1,
      totalSourceItems: 17,
      eligibleItemsCount: 3,
      excludedCount: 14,
      missingDateExcludedCount: 1,
      deletedExcludedCount: 0,
      folders: [
        { sourceFolder: 'INBOX', targetFolder: 'INBOX', sourceUidValidity: 100, totalItems: 12, eligibleItems: 2, excludedCount: 10 },
        { sourceFolder: 'İstanbul/2026', targetFolder: 'Istanbul_Arsiv', sourceUidValidity: 200, totalItems: 5, eligibleItems: 1, excludedCount: 4 },
      ],
      createdAtUtc: '2026-09-13T12:00:00Z',
      canTransfer: true,
      blockerReason: null,
    };

    mockClient.createImapTransferPreview = vi.fn().mockResolvedValue(mockPreviewResponse);

    await act(async () => {
      const res = await result.current.createPreview();
      expect(res.previewId).toBe('prev-12345');
    });

    expect(mockClient.createImapTransferPreview).toHaveBeenCalledWith({
      companyId: 'comp-1',
      projectId: 'proj-1',
      sourceAccountId: 'acc-src-1',
      targetAccountId: 'acc-tgt-2',
      selectedFolders: [
        { sourceFolderPath: 'INBOX', targetFolderPath: 'INBOX' },
        { sourceFolderPath: 'İstanbul/2026', targetFolderPath: 'Istanbul_Arsiv' },
      ],
      startDate: '2024-01-01',
      endDate: '2024-02-16',
    });

    expect(result.current.preview).toEqual(mockPreviewResponse);
  });

  it('starts transfer with strictly previewId and fresh idempotencyKey without counts or host', async () => {
    const mockPreviewResponse: ImapTransferPreviewResponse = {
      previewId: 'prev-frozen-777',
      companyId: 'comp-1',
      projectId: 'proj-1',
      sourceAccountId: 'acc-src-1',
      sourceAccountVersion: 1,
      targetAccountId: 'acc-tgt-2',
      targetAccountVersion: 1,
      totalSourceItems: 12,
      eligibleItemsCount: 3,
      excludedCount: 9,
      missingDateExcludedCount: 0,
      deletedExcludedCount: 0,
      folders: [
        { sourceFolder: 'INBOX', targetFolder: 'INBOX', sourceUidValidity: 1, totalItems: 12, eligibleItems: 3, excludedCount: 9 },
      ],
      createdAtUtc: '2026-09-13T12:00:00Z',
      canTransfer: true,
      blockerReason: null,
    };

    const mockStartedJob: LocalJobRecord = {
      jobId: 'job-imap-999',
      jobKind: 'imap-transfer',
      idempotencyKey: 'imap-xfer-prev-frozen-777-mock',
      clientContext: { companyId: 'comp-1', companyName: 'Müşteri', projectId: 'proj-1', projectName: 'Proje' },
      sourceFileName: 'imap://acc-src-1',
      targetFileName: 'imap://acc-tgt-2',
      status: 'queued',
      stage: 'Sırada',
      itemsRead: 0,
      itemsWritten: 0,
      failedItems: 0,
      totalItems: 3,
      currentFolder: 'INBOX',
      percentComplete: 0,
      createdAt: '2026-09-13T12:00:00Z',
    };

    mockClient.createImapTransferPreview = vi.fn().mockResolvedValue(mockPreviewResponse);
    mockClient.startImapTransfer = vi.fn().mockResolvedValue(mockStartedJob);

    const { result } = renderHook(() =>
      useImapTransfer({ companyId: 'comp-1', projectId: 'proj-1', client: mockClient, autoFetchAccounts: false })
    );

    act(() => {
      result.current.setSourceAccountId('acc-src-1');
      result.current.setTargetAccountId('acc-tgt-2');
      result.current.toggleFolderSelection('INBOX');
    });

    await act(async () => { await Promise.resolve(); });

    await act(async () => {
      await result.current.createPreview();
    });

    await act(async () => {
      const job = await result.current.startTransfer();
      expect(job.jobId).toBe('job-imap-999');
    });

    // Check payload sent to startImapTransfer
    const startCalls = (mockClient.startImapTransfer as any).mock.calls;
    expect(startCalls.length).toBe(1);
    const sentPayload = startCalls[0][0];

    // Payload MUST contain previewId and fresh idempotencyKey
    expect(sentPayload.previewId).toBe('prev-frozen-777');
    expect(sentPayload.idempotencyKey).toMatch(/^imap-xfer-prev-frozen-777-/);

    // Payload MUST NOT contain client counts, host, or UIDs
    expect(sentPayload.totalItems).toBeUndefined();
    expect(sentPayload.count).toBeUndefined();
    expect(sentPayload.uids).toBeUndefined();
    expect(sentPayload.host).toBeUndefined();
    expect(Object.keys(sentPayload).sort()).toEqual(['enqueueIfBusy', 'idempotencyKey', 'previewId']);
    expect(sentPayload.enqueueIfBusy).toBe(true);
    await act(async () => { await result.current.startTransfer(); });
    expect((mockClient.startImapTransfer as any).mock.calls[1][0].idempotencyKey).toBe(sentPayload.idempotencyKey);
  });

  it('prevents start when preview contains blockers', async () => {
    const blockedPreview: ImapTransferPreviewResponse = {
      previewId: 'prev-blocked-1',
      companyId: 'comp-1',
      projectId: 'proj-1',
      sourceAccountId: 'acc-src-1',
      sourceAccountVersion: 1,
      targetAccountId: 'acc-tgt-2',
      targetAccountVersion: 1,
      totalSourceItems: 5,
      eligibleItemsCount: 0,
      excludedCount: 5,
      missingDateExcludedCount: 0,
      deletedExcludedCount: 2,
      folders: [],
      createdAtUtc: '2026-09-13T12:00:00Z',
      canTransfer: false,
      blockerReason: 'Seçili iletiler arasında Deleted bayrağı içeren öğeler bulunmaktadır.',
    };

    mockClient.createImapTransferPreview = vi.fn().mockResolvedValue(blockedPreview);

    const { result } = renderHook(() =>
      useImapTransfer({ companyId: 'comp-1', projectId: 'proj-1', client: mockClient, autoFetchAccounts: false })
    );

    act(() => {
      result.current.setSourceAccountId('acc-src-1');
      result.current.setTargetAccountId('acc-tgt-2');
      result.current.toggleFolderSelection('INBOX');
    });

    await act(async () => { await Promise.resolve(); });

    await act(async () => {
      await result.current.createPreview();
    });

    await expect(result.current.startTransfer()).rejects.toThrow(
      'Seçili iletiler arasında Deleted bayrağı içeren öğeler bulunmaktadır.'
    );
    let release!: (value: ImapTransferPreviewResponse) => void;
    mockClient.createImapTransferPreview = vi.fn().mockImplementation(() => new Promise(resolve => { release = resolve; }));
    let pending!: Promise<ImapTransferPreviewResponse>;
    act(() => { pending = result.current.createPreview(); });
    act(() => { result.current.setEndDate('2024-02-16'); });
    await act(async () => { release(blockedPreview); await pending; });
    expect(result.current.preview).toBeNull();
  });

  it('handles resume action for interrupted/NeedsAttention jobs', async () => {
    const mockResumedJob: LocalJobRecord = {
      jobId: 'job-interrupted-1',
      jobKind: 'imap-transfer',
      clientContext: { companyId: 'comp-1', companyName: 'Müşteri', projectId: 'proj-1', projectName: 'Proje' },
      sourceFileName: 'imap://acc-src-1',
      targetFileName: 'imap://acc-tgt-2',
      status: 'queued',
      stage: 'Yeniden Başlatılıyor (Keyword Uzlaştırması)',
      itemsRead: 1,
      itemsWritten: 1,
      failedItems: 0,
      totalItems: 3,
      currentFolder: 'INBOX',
      percentComplete: 33,
      createdAt: '2026-09-13T12:00:00Z',
    };

    mockClient.resumeImapTransfer = vi.fn().mockResolvedValue(mockResumedJob);

    const { result } = renderHook(() =>
      useImapTransfer({ companyId: 'comp-1', projectId: 'proj-1', client: mockClient, autoFetchAccounts: false })
    );

    await act(async () => {
      const resumed = await result.current.resumeTransfer('job-interrupted-1');
      expect(resumed.status).toBe('queued');
      expect(resumed.stage).toContain('Keyword Uzlaştırması');
    });

    expect(mockClient.resumeImapTransfer).toHaveBeenCalledWith('job-interrupted-1', 'comp-1', 'proj-1', true);
  });

  it('protects against stale responses across customer/project scope switches', async () => {
    let resolveFirstPreview: (res: ImapTransferPreviewResponse) => void;
    const firstPreviewPromise = new Promise<ImapTransferPreviewResponse>((resolve) => {
      resolveFirstPreview = resolve;
    });

    mockClient.createImapTransferPreview = vi.fn().mockImplementation((req) => {
      if (req.companyId === 'comp-1') {
        return firstPreviewPromise;
      }
      return Promise.resolve({
        previewId: 'prev-scope-2',
        companyId: 'comp-2',
        projectId: 'proj-2',
        sourceAccountId: 'acc-src-2',
        sourceAccountVersion: 1,
        targetAccountId: 'acc-tgt-2',
        targetAccountVersion: 1,
        totalSourceItems: 20,
        eligibleItemsCount: 20,
        excludedCount: 0,
        missingDateExcludedCount: 0,
        deletedExcludedCount: 0,
        folders: [],
        createdAtUtc: '2026-09-13T12:00:00Z',
        canTransfer: true,
        blockerReason: null,
      });
    });

    const { result, rerender } = renderHook(
      ({ comp, proj }) => useImapTransfer({ companyId: comp, projectId: proj, client: mockClient, autoFetchAccounts: false }),
      { initialProps: { comp: 'comp-1', proj: 'proj-1' } }
    );

    act(() => {
      result.current.setSourceAccountId('acc-src-1');
      result.current.setTargetAccountId('acc-tgt-2');
      result.current.toggleFolderSelection('INBOX');
    });

    // Start preview in scope 1
    const p1 = act(async () => {
      result.current.createPreview().catch(() => {});
    });

    // Switch scope to comp-2 / proj-2 while p1 is in flight
    rerender({ comp: 'comp-2', proj: 'proj-2' });

    // In the new scope, preview must be reset and not retain stale data
    expect(result.current.preview).toBeNull();

    // Late resolution of Scope 1
    await act(async () => {
      resolveFirstPreview!({
        previewId: 'stale-preview-1',
        companyId: 'comp-1',
        projectId: 'proj-1',
        sourceAccountId: 'acc-src-1',
        sourceAccountVersion: 1,
        targetAccountId: 'acc-tgt-2',
        targetAccountVersion: 1,
        totalSourceItems: 12,
        eligibleItemsCount: 12,
        excludedCount: 0,
        missingDateExcludedCount: 0,
        deletedExcludedCount: 0,
        folders: [],
        createdAtUtc: '2026-09-13T12:00:00Z',
        canTransfer: true,
        blockerReason: null,
      });
      await p1;
    });

    // Current scope (comp-2) must NOT be populated with stale Scope 1 preview
    expect(result.current.preview).toBeNull();
  });
});
