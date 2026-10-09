// @vitest-environment jsdom
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { renderHook } from '@testing-library/react';
import { BridgeTransferWorkflow, getAccountBadgeText } from '../components/transfer/BridgeTransferWorkflow';
import { TransfersTabView } from '../components/transfer/TransfersTabView';
import { JobCenter } from '../components/jobs/JobCenter';
import { useBridgeTransfer } from '../hooks/useBridgeTransfer';
import { LocalEngineClient, localEngineClient } from '../api/localEngineClient';
import {
  ImapAccountPublicDto,
  ImapFolderDto,
  BridgeSourceDescriptorResponse,
  BridgeImportPreviewResponse,
  BridgeExportPreviewResponse,
  LocalJobRecord,
} from '../types/localEngine';

describe('TASK-017 Bridge Transfer Workflow & UI Integration Tests', () => {
  let mockClient: LocalEngineClient;

  const sampleAccounts: ImapAccountPublicDto[] = [
    {
      accountId: 'acc-corp-m365',
      companyId: 'comp-1',
      projectId: 'proj-1',
      displayName: 'Kurumsal Exchange',
      email: 'corp@sirket.com',
      host: 'outlook.office365.com',
      port: 993,
      tlsMode: 'ssl',
      username: 'corp@sirket.com',
      authKind: 'microsoft365',
      tenantId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      version: 2,
      createdAtUtc: '2026-09-13T12:00:00Z',
      updatedAtUtc: '2026-09-13T12:00:00Z',
    },
    {
      accountId: 'acc-personal-m365',
      companyId: 'comp-1',
      projectId: 'proj-1',
      displayName: 'Kişisel Outlook',
      email: 'ali@customdomain.com',
      host: 'outlook.office365.com',
      port: 993,
      tlsMode: 'ssl',
      username: 'ali@customdomain.com',
      authKind: 'microsoft365',
      tenantId: 'consumers',
      version: 1,
      createdAtUtc: '2026-09-13T12:00:00Z',
      updatedAtUtc: '2026-09-13T12:00:00Z',
    },
    {
      accountId: 'acc-generic-imap',
      companyId: 'comp-1',
      projectId: 'proj-1',
      displayName: 'Sunucu Postası',
      email: 'posta@gmail.com', // Even with gmail domain, must NOT infer Google!
      host: 'mail.yerel.test',
      port: 993,
      tlsMode: 'ssl',
      username: 'posta@gmail.com',
      authKind: 'password',
      version: 1,
      createdAtUtc: '2026-09-13T12:00:00Z',
      updatedAtUtc: '2026-09-13T12:00:00Z',
    },
  ];

  const sampleDescriptor: BridgeSourceDescriptorResponse = {
    sourceHandle: 'msrc_test_001',
    sourceKind: 'eml-tree',
    dialect: 'rfc822',
    displayPath: 'C:\\Arsiv\\Corpus',
    totalFiles: 15,
    totalItems: 15,
    totalSizeBytes: 1048576,
    ignoredNonEmlFilesCount: 0,
    folders: [
      { folderName: 'INBOX', itemCount: 10, totalSizeBytes: 700000 },
      { folderName: 'Sent', itemCount: 5, totalSizeBytes: 348576 },
    ],
  };

  const sampleImapFolders: ImapFolderDto[] = [
    { name: 'INBOX', fullPath: 'INBOX', delimiter: '/', isSelectable: true, messageCount: 20, countValid: true },
    { name: 'Sent', fullPath: 'Sent', delimiter: '/', isSelectable: true, messageCount: 10, countValid: true },
  ];

  const sampleImportPreview: BridgeImportPreviewResponse = {
    previewId: 'bprev_imp_001',
    companyId: 'comp-1',
    projectId: 'proj-1',
    sourceHandle: 'msrc_test_001',
    sourceFingerprint: 'fp_imp_123',
    sourceKind: 'eml-tree',
    targetAccountId: 'acc-corp-m365',
    targetAccountVersion: 2,
    totalSourceItems: 15,
    eligibleItemsCount: 15,
    excludedCount: 0,
    missingDateExcludedCount: 0,
    folders: [
      { sourceFolder: 'INBOX', targetFolder: 'INBOX', totalItems: 10, eligibleItems: 10, excludedCount: 0, missingDateExcludedCount: 0, deletedExcludedCount: 0 },
      { sourceFolder: 'Sent', targetFolder: 'Giden', totalItems: 5, eligibleItems: 5, excludedCount: 0, missingDateExcludedCount: 0, deletedExcludedCount: 0 },
    ],
    createdAtUtc: '2026-09-14T00:00:00Z',
    canTransfer: true,
  };

  const sampleExportPreview: BridgeExportPreviewResponse = {
    previewId: 'bprev_exp_002',
    companyId: 'comp-1',
    projectId: 'proj-1',
    sourceAccountId: 'acc-corp-m365',
    sourceAccountVersion: 2,
    targetDirHandle: 'dir_out_001',
    targetFormat: 'eml-tree',
    totalSourceItems: 30,
    eligibleItemsCount: 30,
    excludedCount: 0,
    missingDateExcludedCount: 0,
    deletedExcludedCount: 0,
    folders: [
      { sourceFolder: 'INBOX', targetFolder: 'INBOX', totalItems: 20, eligibleItems: 20, excludedCount: 0, missingDateExcludedCount: 0, deletedExcludedCount: 0 },
      { sourceFolder: 'Sent', targetFolder: 'Sent', totalItems: 10, eligibleItems: 10, excludedCount: 0, missingDateExcludedCount: 0, deletedExcludedCount: 0 },
    ],
    createdAtUtc: '2026-09-14T00:00:00Z',
    canTransfer: true,
    estimatedRequiredBytes: 314572800,
    availableFreeBytes: 1073741824,
  };

  beforeEach(() => {
    vi.restoreAllMocks();
    mockClient = new LocalEngineClient('http://127.0.0.1:6174');
    mockClient.listAccounts = vi.fn().mockResolvedValue(sampleAccounts);
    mockClient.listAccountFolders = vi.fn().mockResolvedValue({ accountId: 'acc-corp-m365', folders: sampleImapFolders });
    mockClient.describeMimeSource = vi.fn().mockResolvedValue(sampleDescriptor);
    mockClient.createBridgeImportPreview = vi.fn().mockResolvedValue(sampleImportPreview);
    mockClient.createBridgeExportPreview = vi.fn().mockResolvedValue(sampleExportPreview);
    mockClient.startBridgeImport = vi.fn().mockResolvedValue({
      jobId: 'job_bimp_001',
      jobKind: 'bridge-import',
      status: 'converting',
      stage: 'Aktarılıyor',
      percentComplete: 10,
      itemsRead: 15,
      itemsWritten: 2,
      failedItems: 0,
      totalItems: 15,
      sourceFileName: 'C:\\Arsiv\\Corpus',
      targetFileName: 'corp@sirket.com',
      createdAt: '2026-09-14T00:00:00Z',
      clientContext: { companyId: 'comp-1', companyName: 'Acme', projectId: 'proj-1', projectName: 'P1' },
    } as LocalJobRecord);
    mockClient.startBridgeExport = vi.fn().mockResolvedValue({
      jobId: 'job_bexp_002',
      jobKind: 'bridge-export',
      status: 'converting',
      stage: 'İndiriliyor',
      percentComplete: 5,
      itemsRead: 30,
      itemsWritten: 1,
      failedItems: 0,
      totalItems: 30,
      sourceFileName: 'corp@sirket.com',
      targetFileName: 'dir_out_001',
      createdAt: '2026-09-14T00:00:00Z',
      clientContext: { companyId: 'comp-1', companyName: 'Acme', projectId: 'proj-1', projectName: 'P1' },
    } as LocalJobRecord);
    mockClient.resumeBridgeImport = vi.fn().mockResolvedValue({
      jobId: 'job_bimp_001',
      jobKind: 'bridge-import',
      status: 'converting',
      stage: 'Yeniden başlatıldı',
      percentComplete: 20,
      itemsRead: 15,
      itemsWritten: 3,
      failedItems: 0,
      totalItems: 15,
      sourceFileName: 'C:\\Arsiv\\Corpus',
      targetFileName: 'corp@sirket.com',
      createdAt: '2026-09-14T00:00:00Z',
      clientContext: { companyId: 'comp-1', companyName: 'Acme', projectId: 'proj-1', projectName: 'P1' },
    } as LocalJobRecord);
    mockClient.resumeBridgeExport = vi.fn().mockResolvedValue({
      jobId: 'job_bexp_002',
      jobKind: 'bridge-export',
      status: 'converting',
      stage: 'Yeniden başlatıldı',
      percentComplete: 10,
      itemsRead: 30,
      itemsWritten: 3,
      failedItems: 0,
      totalItems: 30,
      sourceFileName: 'corp@sirket.com',
      targetFileName: 'dir_out_001',
      createdAt: '2026-09-14T00:00:00Z',
      clientContext: { companyId: 'comp-1', companyName: 'Acme', projectId: 'proj-1', projectName: 'P1' },
    } as LocalJobRecord);
    mockClient.pickMimeSource = vi.fn().mockResolvedValue({
      cancelled: false,
      handle: 'msrc_test_001',
      fileName: 'Corpus',
      displayPath: 'C:\\Arsiv\\Corpus',
      sizeBytes: 1048576,
    });
    mockClient.pickOutputDir = vi.fn().mockResolvedValue({
      cancelled: false,
      handle: 'dir_out_001',
      fileName: 'HedefKlasor',
      displayPath: 'C:\\ExportTarget',
    });
  });

  describe('1. Account Badge Determination Without Email Domain Inference', () => {
    it('accurately distinguishes corporate vs personal M365 and generic IMAP without domain inference', () => {
      // 1. M365 with tenantId === 'consumers' -> personal Outlook
      expect(getAccountBadgeText(sampleAccounts[1])).toBe('Bireysel Outlook');

      // 2. M365 with GUID or other tenant -> corporate
      expect(getAccountBadgeText(sampleAccounts[0])).toBe('Kurumsal Microsoft 365');

      // 3. Generic IMAP -> Standart IMAP (even with gmail.com domain)
      expect(getAccountBadgeText(sampleAccounts[2])).toBe('Standart IMAP');
    });
  });

  describe('2. Vendor-Free Descriptor Endpoint & Hook State Contract', () => {
    it('fetches folder descriptor via /api/transfer/bridge/source/describe and never calls /api/mime/analyze', async () => {
      const { result } = renderHook(() =>
        useBridgeTransfer({ companyId: 'comp-1', projectId: 'proj-1', client: mockClient, autoFetchAccounts: false })
      );

      await act(async () => {
        const desc = await result.current.describeMimeSource('msrc_test_001');
        expect(desc).toEqual(sampleDescriptor);
      });

      expect(mockClient.describeMimeSource).toHaveBeenCalledWith('msrc_test_001');
      // Assert analyzeOst / analyzeMime were NEVER called
      expect((mockClient as any).analyzeOst).toBeUndefined();
      expect((mockClient as any).analyzeMime).toBeUndefined();

      expect(result.current.selectedSourceFolders).toEqual(['INBOX', 'Sent']);
      expect(result.current.targetFolderMappings).toEqual({ INBOX: 'INBOX', Sent: 'Sent' });
    });

    it('enforces required parameters and date validation for File->IMAP preview', async () => {
      const { result } = renderHook(() =>
        useBridgeTransfer({ companyId: 'comp-1', projectId: 'proj-1', client: mockClient, autoFetchAccounts: false })
      );

      // 1. Missing source handle
      await expect(result.current.createPreview()).rejects.toThrow('En az bir kaynak dosya seçilmelidir.');

      // 2. With handle but missing target account
      await act(async () => {
        await result.current.describeMimeSource('msrc_test_001');
      });
      expect(result.current.sourceHandle).toBe('msrc_test_001');
      await expect(result.current.createPreview()).rejects.toThrow('Hedef IMAP hesabı zorunludur.');

      // 3. With target account but empty folders
      act(() => {
        result.current.setTargetAccountId('acc-corp-m365');
        result.current.deselectAllSourceFolders();
      });
      expect(result.current.targetAccountId).toBe('acc-corp-m365');
      expect(result.current.selectedSourceFolders).toEqual([]);
      await expect(result.current.createPreview()).rejects.toThrow('En az bir klasör seçilmelidir.');

      // 4. Inverted date range
      act(() => {
        result.current.selectAllSourceFolders();
        result.current.setStartDate('2026-09-15');
        result.current.setEndDate('2026-09-10');
      });
      expect(result.current.selectedSourceFolders.length).toBeGreaterThan(0);
      expect(result.current.startDate).toBe('2026-09-15');
      expect(result.current.endDate).toBe('2026-09-10');
      await expect(result.current.createPreview()).rejects.toThrow('Tarih filtresi başlangıcı bitişinden büyük olamaz.');

      // 5. Valid preview request with mapping and date
      act(() => {
        result.current.setStartDate('2026-09-01');
        result.current.setEndDate('2026-09-14');
        result.current.setFolderMapping('Sent', 'Giden');
      });
      expect(result.current.startDate).toBe('2026-09-01');
      expect(result.current.endDate).toBe('2026-09-14');

      let prev: any;
      await act(async () => {
        prev = await result.current.createPreview();
      });
      expect(prev).toEqual(sampleImportPreview);

      expect(mockClient.createBridgeImportPreview).toHaveBeenCalledWith({
        companyId: 'comp-1',
        projectId: 'proj-1',
        companyName: undefined,
        projectName: undefined,
        sourceHandle: 'msrc_test_001',
        targetAccountId: 'acc-corp-m365',
        selectedFolders: ['INBOX', 'Sent'],
        targetFolderMappings: { INBOX: 'INBOX', Sent: 'Giden' },
        startDate: '2026-09-01',
        endDate: '2026-09-14',
      });
    });

    it('enforces required parameters and date validation for IMAP->File export preview', async () => {
      const { result } = renderHook(() =>
        useBridgeTransfer({ companyId: 'comp-1', projectId: 'proj-1', client: mockClient, autoFetchAccounts: false })
      );

      act(() => {
        result.current.setDirection('imap-to-file');
      });
      expect(result.current.direction).toBe('imap-to-file');

      // 1. Missing source account
      await expect(result.current.createPreview()).rejects.toThrow('Kaynak IMAP hesabı zorunludur.');

      // 2. Set source account, but missing target directory
      await act(async () => {
        await result.current.setSourceAccountId('acc-corp-m365');
      });
      expect(result.current.sourceAccountId).toBe('acc-corp-m365');
      expect(result.current.targetDirHandle).toBe('');
      await expect(result.current.createPreview()).rejects.toThrow('Hedef klasör seçilmelidir.');

      // 3. Set target directory, but deselect all folders -> missing folders
      await act(async () => {
        await result.current.pickOutputDir();
      });
      expect(result.current.targetDirHandle).toBe('dir_out_001');

      act(() => {
        result.current.deselectAllImapFolders();
      });
      expect(result.current.selectedImapFolders).toEqual([]);
      await expect(result.current.createPreview()).rejects.toThrow('En az bir klasör seçilmelidir.');

      // 4. Select folders, but set inverted date range
      act(() => {
        result.current.selectAllImapFolders();
        result.current.setStartDate('2026-09-15');
        result.current.setEndDate('2026-09-10');
      });
      expect(result.current.selectedImapFolders.length).toBeGreaterThan(0);
      expect(result.current.startDate).toBe('2026-09-15');
      expect(result.current.endDate).toBe('2026-09-10');
      await expect(result.current.createPreview()).rejects.toThrow('Tarih filtresi başlangıcı bitişinden büyük olamaz.');

      // 5. Valid preview request with reset dates and format
      act(() => {
        result.current.setStartDate('');
        result.current.setEndDate('');
        result.current.setTargetFormat('mboxrd');
      });
      expect(result.current.targetFormat).toBe('mboxrd');

      let prev: any;
      await act(async () => {
        prev = await result.current.createPreview();
      });
      expect(prev).toEqual(sampleExportPreview);

      expect(mockClient.createBridgeExportPreview).toHaveBeenCalledWith({
        companyId: 'comp-1',
        projectId: 'proj-1',
        companyName: undefined,
        projectName: undefined,
        sourceAccountId: 'acc-corp-m365',
        targetDirHandle: 'dir_out_001',
        targetFormat: 'mboxrd',
        selectedFolders: ['INBOX', 'Sent'],
        startDate: null,
        endDate: null,
      });
    });
  });

  describe('3. Wire Payloads: Start and Resume Exact Routes', () => {
    it('executes start and resume for File->IMAP with exact route and idempotency', async () => {
      const { result } = renderHook(() =>
        useBridgeTransfer({ companyId: 'comp-1', projectId: 'proj-1', client: mockClient, autoFetchAccounts: false })
      );

      await act(async () => {
        await result.current.describeMimeSource('msrc_test_001');
      });
      expect(result.current.sourceHandle).toBe('msrc_test_001');

      act(() => {
        result.current.setTargetAccountId('acc-corp-m365');
      });
      expect(result.current.targetAccountId).toBe('acc-corp-m365');

      await act(async () => {
        await result.current.createPreview();
      });
      expect(result.current.preview).toEqual(sampleImportPreview);

      // Start import
      let job: any;
      await act(async () => {
        job = await result.current.startTransfer();
      });
      expect(job?.jobId).toBe('job_bimp_001');

      expect(mockClient.startBridgeImport).toHaveBeenCalledWith({
        enqueueIfBusy: true,
        previewId: 'bprev_imp_001',
        idempotencyKey: expect.any(String),
        companyId: 'comp-1',
        projectId: 'proj-1',
      });

      // Resume import
      let resumed: any;
      await act(async () => {
        resumed = await result.current.resumeTransfer('job_bimp_001');
      });
      expect(resumed?.jobId).toBe('job_bimp_001');

      expect(mockClient.resumeBridgeImport).toHaveBeenCalledWith('job_bimp_001', 'comp-1', 'proj-1', true);
    });

    it('executes start and resume for IMAP->File with exact route and idempotency', async () => {
      const { result } = renderHook(() =>
        useBridgeTransfer({ companyId: 'comp-1', projectId: 'proj-1', client: mockClient, autoFetchAccounts: false })
      );

      act(() => {
        result.current.setDirection('imap-to-file');
      });
      expect(result.current.direction).toBe('imap-to-file');

      await act(async () => {
        await result.current.setSourceAccountId('acc-corp-m365');
      });
      expect(result.current.sourceAccountId).toBe('acc-corp-m365');

      await act(async () => {
        await result.current.pickOutputDir();
      });
      expect(result.current.targetDirHandle).toBe('dir_out_001');

      act(() => {
        result.current.selectAllImapFolders();
      });
      expect(result.current.selectedImapFolders.length).toBeGreaterThan(0);

      await act(async () => {
        await result.current.createPreview();
      });
      expect(result.current.preview).toEqual(sampleExportPreview);

      // Start export
      let job: any;
      await act(async () => {
        job = await result.current.startTransfer();
      });
      expect(job?.jobId).toBe('job_bexp_002');

      expect(mockClient.startBridgeExport).toHaveBeenCalledWith({
        enqueueIfBusy: true,
        previewId: 'bprev_exp_002',
        idempotencyKey: expect.any(String),
        companyId: 'comp-1',
        projectId: 'proj-1',
      });

      // Resume export
      let resumed: any;
      await act(async () => {
        resumed = await result.current.resumeTransfer('job_bexp_002');
      });
      expect(resumed?.jobId).toBe('job_bexp_002');

      expect(mockClient.resumeBridgeExport).toHaveBeenCalledWith('job_bexp_002', 'comp-1', 'proj-1', true);
    });
  });

  describe('4. State Isolation & Stale Response Discarding', () => {
    it('invalidates preview when switching direction, source handle, target account, or format', async () => {
      const { result } = renderHook(() =>
        useBridgeTransfer({ companyId: 'comp-1', projectId: 'proj-1', client: mockClient, autoFetchAccounts: false })
      );

      await act(async () => {
        await result.current.describeMimeSource('msrc_test_001');
      });
      expect(result.current.sourceHandle).toBe('msrc_test_001');

      act(() => {
        result.current.setTargetAccountId('acc-corp-m365');
      });
      expect(result.current.targetAccountId).toBe('acc-corp-m365');

      await act(async () => {
        await result.current.createPreview();
      });
      expect(result.current.preview).not.toBeNull();

      // 1. Changing target account invalidates preview
      act(() => {
        result.current.setTargetAccountId('acc-personal-m365');
      });
      expect(result.current.preview).toBeNull();

      // Regenerate preview
      await act(async () => {
        await result.current.createPreview();
      });
      expect(result.current.preview).not.toBeNull();

      // 2. Changing direction invalidates preview
      act(() => {
        result.current.setDirection('imap-to-file');
      });
      expect(result.current.preview).toBeNull();
    });

    it('discards stale async in-flight preview response if selection changed during request', async () => {
      let delayedResolve: (val: BridgeImportPreviewResponse) => void = () => {};
      mockClient.createBridgeImportPreview = vi.fn().mockImplementation(
        () => new Promise((resolve) => { delayedResolve = resolve; })
      );

      const { result } = renderHook(() =>
        useBridgeTransfer({ companyId: 'comp-1', projectId: 'proj-1', client: mockClient, autoFetchAccounts: false })
      );

      await act(async () => {
        await result.current.describeMimeSource('msrc_test_001');
      });
      expect(result.current.sourceHandle).toBe('msrc_test_001');

      act(() => {
        result.current.setTargetAccountId('acc-corp-m365');
      });
      expect(result.current.targetAccountId).toBe('acc-corp-m365');

      // Trigger first preview (in-flight)
      let p1: Promise<any>;
      act(() => {
        p1 = result.current.createPreview();
      });

      // User modifies folder selection while preview is in-flight
      act(() => {
        result.current.toggleSourceFolder('Sent');
      });

      // Resolve delayed promise from first call
      await act(async () => {
        delayedResolve(sampleImportPreview);
        try {
          await p1;
        } catch {
          // Stale call may reject or settle
        }
      });

      // Stale preview MUST NOT be committed because signature/sequence diverged
      expect(result.current.preview).toBeNull();
    });
  });

  describe('5. BridgeTransferWorkflow Component Rendering & Direction Switching', () => {
    it('renders clean header without visible (TASK-017) and displays safety notices', async () => {
      render(
        <BridgeTransferWorkflow
          companyId="comp-1"
          companyName="Acme Corp"
          projectId="proj-1"
          projectName="Proje 1"
          client={mockClient}
        />
      );

      // Verify product title lacks TASK-017
      expect(screen.getByText('Dosya ve posta hesabı arasında aktarım')).toBeDefined();
      expect(screen.queryByText(/TASK-017/)).toBeNull();

      // Safety notices
      expect(screen.getByTestId('bridge-safety-notices')).toBeDefined();
      expect(screen.getByText(/Kaynak korunur/)).toBeDefined();
      expect(screen.getByText(/Standart biçim sınırları/)).toBeDefined();
      expect(screen.getByText(/Mboxrd kuralı/)).toBeDefined();
    });

    it('switches between File->IMAP and IMAP->File forms and preserves IMAP<->IMAP', async () => {
      const onSelectImapToImap = vi.fn();

      render(
        <BridgeTransferWorkflow
          companyId="comp-1"
          companyName="Acme Corp"
          projectId="proj-1"
          projectName="Proje 1"
          client={mockClient}
          onSelectImapToImap={onSelectImapToImap}
        />
      );

      // Default is file-to-imap
      expect(screen.getByTestId('bridge-import-form')).toBeDefined();
      expect(screen.queryByTestId('bridge-export-form')).toBeNull();

      // Switch to IMAP->File
      fireEvent.click(screen.getByTestId('direction-imap-to-file-btn'));
      expect(screen.getByTestId('bridge-export-form')).toBeDefined();
      expect(screen.queryByTestId('bridge-import-form')).toBeNull();

      // Switch to IMAP<->IMAP calls callback
      fireEvent.click(screen.getByTestId('direction-imap-to-imap-btn'));
      expect(onSelectImapToImap).toHaveBeenCalledTimes(1);
    });

    it('File->IMAP flow: picks source, discovers folders with camelCase, and maps target folders', async () => {
      render(
        <BridgeTransferWorkflow
          companyId="comp-1"
          companyName="Acme Corp"
          projectId="proj-1"
          projectName="Proje 1"
          client={mockClient}
        />
      );

      // Click pick EML Tree
      await act(async () => {
        fireEvent.click(screen.getByTestId('pick-eml-tree-btn'));
      });

      // Verify source badge appears
      await waitFor(() => {
        expect(screen.getByTestId('selected-source-handle-badge')).toBeDefined();
      });
      expect(screen.getByTestId('selected-source-handle-badge').textContent).toContain('msrc_test_001');

      // Verify folder table rendered with camelCase items
      await waitFor(() => {
        expect(screen.getByTestId('source-folder-row-INBOX')).toBeDefined();
      });
      expect(screen.getByText('INBOX')).toBeDefined();
      expect(screen.getByText('(10 ileti)')).toBeDefined();
      expect(screen.getByText('Sent')).toBeDefined();
      expect(screen.getByText('(5 ileti)')).toBeDefined();

      // Check target account dropdown contains non-inferred badges
      const select = screen.getByTestId('bridge-target-account-select') as HTMLSelectElement;
      expect(select.textContent).toContain('(Kurumsal Microsoft 365)');
      expect(select.textContent).toContain('(Bireysel Outlook)');
      expect(select.textContent).toContain('(Standart IMAP)');

      // Select target account
      fireEvent.change(select, { target: { value: 'acc-corp-m365' } });

      // Generate preview
      await act(async () => {
        fireEvent.click(screen.getByTestId('create-bridge-import-preview-btn'));
      });

      // Preview details breakdown
      await waitFor(() => {
        expect(screen.getByTestId('bridge-preview-card')).toBeDefined();
      });
      expect(screen.getByTestId('preview-eligible-items').textContent).toContain('15');

      // Start transfer
      await act(async () => {
        fireEvent.click(screen.getByTestId('start-bridge-transfer-btn'));
      });

      await waitFor(() => {
        expect(screen.getByTestId('bridge-job-monitor')).toBeDefined();
      });
      expect(screen.getByTestId('bridge-job-written-count').textContent).toBe('2');
    });

    it('renders error state and triggers resume for interrupted/failed bridge jobs', async () => {
      const interruptedJob: LocalJobRecord = {
        jobId: 'job_interrupted_99',
        jobKind: 'bridge-import',
        status: 'interrupted',
        stage: 'Ağ hatası nedeniyle durdu',
        errorMessage: 'Bağlantı kesildi, devam edilebilir.',
        percentComplete: 45,
        itemsRead: 20,
        itemsWritten: 9,
        failedItems: 1,
        totalItems: 20,
        currentFolder: 'INBOX',
        sourceFileName: 'Corpus',
        targetFileName: 'corp@sirket.com',
        createdAt: '2026-09-14T00:00:00Z',
        clientContext: { companyId: 'comp-1', companyName: 'Acme', projectId: 'proj-1', projectName: 'P1' },
      };

      render(
        <BridgeTransferWorkflow
          companyId="comp-1"
          companyName="Acme Corp"
          projectId="proj-1"
          projectName="Proje 1"
          client={mockClient}
          initialJob={interruptedJob}
        />
      );

      expect(screen.getByTestId('bridge-job-error-state')).toBeDefined();
      expect(screen.getByText('Bağlantı kesildi, devam edilebilir.')).toBeDefined();

      const resumeBtn = screen.getByTestId('bridge-job-resume-btn');
      expect(resumeBtn.textContent).toContain('Kesintiden Devam Et');

      await act(async () => {
        fireEvent.click(resumeBtn);
      });

      expect(mockClient.resumeBridgeImport).toHaveBeenCalledWith('job_interrupted_99', 'comp-1', 'proj-1', true);
    });

    it('switches to export form when parent/prop direction changes and suppresses completed import job', async () => {
      const completedImportJob: LocalJobRecord = {
        jobId: 'job-4c868b2fc7ac',
        jobKind: 'bridge-import',
        status: 'completed',
        stage: 'Tamamlandı',
        percentComplete: 100,
        itemsRead: 12,
        itemsWritten: 12,
        failedItems: 0,
        totalItems: 12,
        currentFolder: 'INBOX',
        sourceFileName: 'Corpus',
        targetFileName: 'corp@sirket.com',
        createdAt: '2026-09-14T00:00:00Z',
        clientContext: { companyId: 'comp-1', companyName: 'Acme', projectId: 'proj-1', projectName: 'P1' },
      };

      // 1. Initial render with file-to-imap and completed import job
      const { rerender } = render(
        <BridgeTransferWorkflow
          companyId="comp-1"
          companyName="Acme Corp"
          projectId="proj-1"
          projectName="Proje 1"
          client={mockClient}
          initialJob={completedImportJob}
          initialDirection="file-to-imap"
        />
      );

      // Verify file-to-imap form and import job monitor are visible
      expect(screen.getByTestId('bridge-import-form')).toBeDefined();
      expect(screen.queryByTestId('bridge-export-form')).toBeNull();
      expect(screen.getByTestId('bridge-job-monitor')).toBeDefined();
      expect(screen.getByText(/job-4c868b2fc7ac/)).toBeDefined();

      // 2. Parent switches direction to imap-to-file (e.g. via top tab-direction-imap-to-file)
      rerender(
        <BridgeTransferWorkflow
          companyId="comp-1"
          companyName="Acme Corp"
          projectId="proj-1"
          projectName="Proje 1"
          client={mockClient}
          initialJob={completedImportJob}
          initialDirection="imap-to-file"
        />
      );

      // Verify bridge-export-form appears, import form is removed, and completed import job no longer controls direction
      await waitFor(() => {
        expect(screen.getByTestId('bridge-export-form')).toBeDefined();
      });
      expect(screen.queryByTestId('bridge-import-form')).toBeNull();
      expect(screen.queryByTestId('bridge-job-monitor')).toBeNull();

      // Verify clicking direction button switches back cleanly
      fireEvent.click(screen.getByTestId('direction-file-to-imap-btn'));
      await waitFor(() => {
        expect(screen.getByTestId('bridge-import-form')).toBeDefined();
      });
      expect(screen.queryByTestId('bridge-export-form')).toBeNull();
    });
  });

  describe('6. TransfersTabView Direction Switching Integration', () => {
    it('switches between Dosya->IMAP, IMAP->Dosya, and IMAP<->IMAP preserving real mode', async () => {
      const mockState: any = {
        plan: { companyId: 'comp-1', projectId: 'proj-1', operationType: 'migration' },
        companies: [{ id: 'comp-1', name: 'Müşteri 1', code: 'M1' }],
        projects: [{ id: 'proj-1', companyId: 'comp-1', name: 'Proje 1' }],
        sources: [],
        currentView: 'workspace',
        updatePlan: vi.fn(),
        setCurrentView: vi.fn(),
        setCurrentTab: vi.fn(),
      };

      render(<TransfersTabView state={mockState} />);

      // Direction switcher buttons in mode switcher
      expect(screen.getByTestId('tab-direction-file-to-imap')).toBeDefined();
      expect(screen.getByTestId('tab-direction-imap-to-file')).toBeDefined();
      expect(screen.getByTestId('tab-direction-imap-to-imap')).toBeDefined();

      // Click IMAP <-> IMAP switches to ImapTransferWorkflow
      fireEvent.click(screen.getByTestId('tab-direction-imap-to-imap'));
      await waitFor(() => {
        expect(screen.getByTestId('imap-transfer-workflow')).toBeDefined();
      });

      // Switch back to Dosya -> IMAP
      fireEvent.click(screen.getByTestId('tab-direction-file-to-imap'));
      await waitFor(() => {
        expect(screen.getByTestId('bridge-transfer-workflow')).toBeDefined();
      });
    });
  });

  describe('7. JobCenter Recognition of Bridge Jobs & Verification Note', () => {
    it('maps bridge jobs to informative titles, needs_attention state, and suppresses PST fields', async () => {
      const bridgeImportRecord: LocalJobRecord = {
        jobId: 'job_bimp_44',
        jobKind: 'bridge-import',
        status: 'interrupted',
        stage: 'Yarıda kesildi',
        errorMessage: 'Hedef sunucu zaman aşımı',
        percentComplete: 60,
        itemsRead: 50,
        itemsWritten: 30,
        failedItems: 2,
        totalItems: 50,
        currentFolder: 'INBOX',
        sourceFileName: 'Corpus.mbox',
        targetFileName: 'arsiv@hedef.com',
        createdAt: '2026-09-14T00:00:00Z',
        clientContext: { companyId: 'comp-1', companyName: 'Acme', projectId: 'proj-1', projectName: 'P1' },
      };

      localEngineClient.getAllJobs = vi.fn().mockResolvedValue([bridgeImportRecord]);
      localEngineClient.resumeBridgeImport = vi.fn().mockResolvedValue(bridgeImportRecord);

      render(
        <JobCenter
          jobs={[]}
          onToggleJobPause={vi.fn()}
          onNavigateToWorkspace={vi.fn()}
          onAddNewJob={vi.fn()}
        />
      );

      // Verify title mapped accurately on both surfaces (table row and details pane)
      await waitFor(() => {
        const titles = screen.getAllByText('Dosya → IMAP Aktarımı (Corpus.mbox → arsiv@hedef.com)');
        expect(titles.length).toBeGreaterThanOrEqual(1);
      });

      const titles = screen.getAllByText('Dosya → IMAP Aktarımı (Corpus.mbox → arsiv@hedef.com)');
      expect(titles.length).toBe(2);

      // Verify table row contains title
      const table = screen.getByTestId('jobs-table');
      expect(table.textContent).toContain('Dosya → IMAP Aktarımı (Corpus.mbox → arsiv@hedef.com)');

      // Verify details pane contains title
      const detailsPane = screen.getByTestId('job-details-pane');
      expect(detailsPane.textContent).toContain('Dosya → IMAP Aktarımı (Corpus.mbox → arsiv@hedef.com)');

      // Verify status mapped to needs_attention on both surfaces
      expect(screen.getAllByText('Müdahale bekliyor').length).toBe(2);

      // Verify bridge verification note suppresses PST success fields
      const verificationNote = screen.getByTestId('job-verification-note');
      expect(verificationNote.textContent).toContain('Hedef sunucuda RFC822 SHA-256 ve BitigMail keyword ile doğrulanır');
      expect(verificationNote.textContent).toContain('PST başarı alanları uygulanmaz');

      // Verify resume bridge button exists and executes resumeBridgeImport
      const resumeBtn = screen.getByTestId('job-resume-bridge-btn');
      expect(resumeBtn.textContent).toContain('Kesintiden Devam Et');

      await act(async () => {
        fireEvent.click(resumeBtn);
      });

      expect(localEngineClient.resumeBridgeImport).toHaveBeenCalledWith('job_bimp_44', 'comp-1', 'proj-1', true);
    });
  });

  describe('8. Responsive Mobile Viewport (390px) Layout', () => {
    it('renders cleanly in a 390px mobile viewport without throwing or clipped critical actions', () => {
      const { container } = render(
        <div style={{ width: '390px', maxWidth: '390px', overflowX: 'hidden' }}>
          <BridgeTransferWorkflow
            companyId="comp-1"
            companyName="Acme Corp"
            projectId="proj-1"
            projectName="Proje 1"
            client={mockClient}
          />
        </div>
      );

      expect(container.querySelector('.bridge-transfer-workflow')).toBeDefined();
      expect(screen.getByTestId('direction-file-to-imap-btn')).toBeDefined();
      expect(screen.getByTestId('direction-imap-to-file-btn')).toBeDefined();
    });
  });

  describe('9. JobCenter Rehydration, Frozen Context & Direction Clearing (TASK-017)', () => {
    it('rehydrates completed bridge-export job to export form, monitor, and report download', async () => {
      const completedExportJob: LocalJobRecord = {
        jobId: 'job_bexp_completed_99',
        jobKind: 'bridge-export',
        status: 'completed',
        stage: 'Tamamlandı',
        percentComplete: 100,
        itemsRead: 40,
        itemsWritten: 40,
        failedItems: 0,
        totalItems: 40,
        sourceFileName: 'corp@sirket.com',
        targetFileName: 'ExportDir',
        currentFolder: 'INBOX',
        createdAt: '2026-09-14T00:00:00Z',
        clientContext: {
          companyId: 'comp-1',
          companyName: 'Acme Corp',
          projectId: 'proj-1',
          projectName: 'Proje 1',
        },
        bridgeTransfer: {
          jobId: 'job_bexp_completed_99',
          planId: 'plan_bexp_99',
          direction: 'export',
          sourceIdentifier: 'acc-corp-m365',
          targetIdentifier: 'dir_out_001',
          totalPlanned: 40,
          totalVerified: 40,
          totalFailed: 0,
          totalNeedsAttention: 0,
          folders: [],
          items: [],
        },
      };
      const mockReport = {
        jobId: 'job_bexp_completed_99',
        conversionSuccess: true,
        itemsRead: 40,
        itemsWritten: 40,
        failedItems: 0,
        elapsedMilliseconds: 1200,
      } as any;

      mockClient.getJob = vi.fn().mockResolvedValue(completedExportJob);
      mockClient.getJobReport = vi.fn().mockResolvedValue(mockReport);

      const mockState: any = {
        plan: { companyId: 'comp-1', projectId: 'proj-1', operationType: 'migration' },
        companies: [{ id: 'comp-1', name: 'Acme Corp', code: 'AC' }],
        projects: [{ id: 'proj-1', companyId: 'comp-1', name: 'Proje 1' }],
        sources: [],
        currentView: 'workspace',
        selectedLocalJobId: 'job_bexp_completed_99',
        setSelectedLocalJobId: vi.fn(),
        updatePlan: vi.fn(),
        setCurrentView: vi.fn(),
        setCurrentTab: vi.fn(),
      };

      render(<TransfersTabView state={mockState} client={mockClient} />);

      await waitFor(() => {
        expect(mockClient.getJob).toHaveBeenCalledWith('job_bexp_completed_99');
      });

      // Direction rehydrated to imap-to-file
      await waitFor(() => {
        expect(screen.getByTestId('bridge-export-form')).toBeDefined();
      });
      expect(screen.queryByTestId('bridge-import-form')).toBeNull();

      // Monitor and report
      await waitFor(() => {
        expect(screen.getByTestId('bridge-job-monitor')).toBeDefined();
      });
      expect(screen.getByText('Tamamlandı')).toBeDefined();
      expect(screen.getByText('%100')).toBeDefined();
      expect(screen.getByTestId('bridge-download-report-btn')).toBeDefined();
    });

    it('rehydrates interrupted bridge-import job to import form, error state, and executes actual resume endpoint', async () => {
      const interruptedImportJob: LocalJobRecord = {
        jobId: 'job_bimp_interrupted_77',
        jobKind: 'bridge-import',
        status: 'interrupted',
        stage: 'Kesintiye uğradı',
        errorMessage: 'Ağ bağlantısı koptu',
        percentComplete: 45,
        itemsRead: 50,
        itemsWritten: 20,
        failedItems: 1,
        totalItems: 50,
        sourceFileName: 'Corpus.mbox',
        targetFileName: 'corp@sirket.com',
        currentFolder: 'INBOX',
        createdAt: '2026-09-14T00:00:00Z',
        clientContext: {
          companyId: 'comp-1',
          companyName: 'Acme Corp',
          projectId: 'proj-1',
          projectName: 'Proje 1',
        },
        bridgeTransfer: {
          jobId: 'job_bimp_interrupted_77',
          planId: 'plan_bimp_77',
          direction: 'import',
          sourceIdentifier: 'msrc_mbox_01',
          targetIdentifier: 'acc-corp-m365',
          totalPlanned: 50,
          totalVerified: 20,
          totalFailed: 1,
          totalNeedsAttention: 29,
          folders: [],
          items: [],
        },
      };

      mockClient.getJob = vi.fn().mockResolvedValue(interruptedImportJob);
      mockClient.resumeBridgeImport = vi.fn().mockResolvedValue({
        ...interruptedImportJob,
        status: 'converting',
        stage: 'Yeniden başlatıldı',
      });

      const mockState: any = {
        plan: { companyId: 'comp-1', projectId: 'proj-1', operationType: 'migration' },
        companies: [{ id: 'comp-1', name: 'Acme Corp', code: 'AC' }],
        projects: [{ id: 'proj-1', companyId: 'comp-1', name: 'Proje 1' }],
        sources: [],
        currentView: 'workspace',
        selectedLocalJobId: 'job_bimp_interrupted_77',
        setSelectedLocalJobId: vi.fn(),
        updatePlan: vi.fn(),
        setCurrentView: vi.fn(),
        setCurrentTab: vi.fn(),
      };

      render(<TransfersTabView state={mockState} client={mockClient} />);

      await waitFor(() => {
        expect(mockClient.getJob).toHaveBeenCalledWith('job_bimp_interrupted_77');
      });

      // Direction rehydrated to file-to-imap
      await waitFor(() => {
        expect(screen.getByTestId('bridge-import-form')).toBeDefined();
      });
      expect(screen.queryByTestId('bridge-export-form')).toBeNull();

      // Monitor shows interrupted error and resume button
      await waitFor(() => {
        expect(screen.getByTestId('bridge-job-error-state')).toBeDefined();
      });
      expect(screen.getByText('Ağ bağlantısı koptu')).toBeDefined();
      const resumeBtn = screen.getByTestId('bridge-job-resume-btn');
      expect(resumeBtn).toBeDefined();

      await act(async () => {
        fireEvent.click(resumeBtn);
      });

      expect(mockClient.resumeBridgeImport).toHaveBeenCalledWith('job_bimp_interrupted_77', 'comp-1', 'proj-1', true);
    });

    it('preserves frozen job company/project context over differing currently selected dropdown context', async () => {
      const frozenJob: LocalJobRecord = {
        jobId: 'job_frozen_ctx_88',
        jobKind: 'bridge-import',
        status: 'interrupted',
        stage: 'Beklemede',
        errorMessage: 'Erişim yetkisi yenilenmeli',
        percentComplete: 10,
        itemsRead: 10,
        itemsWritten: 1,
        failedItems: 0,
        totalItems: 10,
        sourceFileName: 'Arsiv.mbox',
        targetFileName: 'arsiv@frozen.com',
        currentFolder: 'INBOX',
        createdAt: '2026-09-14T00:00:00Z',
        clientContext: {
          companyId: 'comp-frozen-999',
          companyName: 'Dondurulmuş Holding',
          projectId: 'proj-frozen-888',
          projectName: 'Özel Arşiv Projesi',
        },
      };

      mockClient.getJob = vi.fn().mockResolvedValue(frozenJob);
      mockClient.resumeBridgeImport = vi.fn().mockResolvedValue({
        ...frozenJob,
        status: 'converting',
      });

      // App state has a totally different selected company/project
      const mockState: any = {
        plan: { companyId: 'comp-current-111', projectId: 'proj-current-222', operationType: 'migration' },
        companies: [{ id: 'comp-current-111', name: 'Mevcut Şirket A.Ş.', code: 'MS' }],
        projects: [{ id: 'proj-current-222', companyId: 'comp-current-111', name: 'Mevcut Proje' }],
        sources: [],
        currentView: 'workspace',
        selectedLocalJobId: 'job_frozen_ctx_88',
        setSelectedLocalJobId: vi.fn(),
        updatePlan: vi.fn(),
        setCurrentView: vi.fn(),
        setCurrentTab: vi.fn(),
      };

      render(<TransfersTabView state={mockState} client={mockClient} />);

      // Banner must display the frozen job's companyName / projectName, NOT the app state's
      await waitFor(() => {
        expect(screen.getByText(/Dondurulmuş Holding \/ Özel Arşiv Projesi/)).toBeDefined();
      });
      expect(screen.queryByText(/Mevcut Şirket A\.Ş\. \/ Mevcut Proje/)).toBeNull();

      // Resume action must use the frozen context's IDs
      const resumeBtn = await screen.findByTestId('bridge-job-resume-btn');
      await act(async () => {
        fireEvent.click(resumeBtn);
      });

      expect(mockClient.resumeBridgeImport).toHaveBeenCalledWith('job_frozen_ctx_88', 'comp-frozen-999', 'proj-frozen-888', true);
    });

    it('clears rehydrated job and exits selected-job lock on explicit direction switch so stale job cannot return', async () => {
      const completedExportJob: LocalJobRecord = {
        jobId: 'job_bexp_stale_check',
        jobKind: 'bridge-export',
        status: 'completed',
        stage: 'Tamamlandı',
        percentComplete: 100,
        itemsRead: 25,
        itemsWritten: 25,
        failedItems: 0,
        totalItems: 25,
        sourceFileName: 'corp@sirket.com',
        targetFileName: 'ExportDir',
        currentFolder: 'INBOX',
        createdAt: '2026-09-14T00:00:00Z',
        clientContext: {
          companyId: 'comp-1',
          companyName: 'Acme Corp',
          projectId: 'proj-1',
          projectName: 'Proje 1',
        },
      };

      mockClient.getJob = vi.fn().mockResolvedValue(completedExportJob);
      mockClient.getJobReport = vi.fn().mockResolvedValue({ jobId: 'job_bexp_stale_check' } as any);

      let currentSelectedId: string | null = 'job_bexp_stale_check';
      const setSelectedLocalJobId = vi.fn((id: string | null) => {
        currentSelectedId = id;
      });

      const mockState: any = {
        plan: { companyId: 'comp-1', projectId: 'proj-1', operationType: 'migration' },
        companies: [{ id: 'comp-1', name: 'Acme Corp', code: 'AC' }],
        projects: [{ id: 'proj-1', companyId: 'comp-1', name: 'Proje 1' }],
        sources: [],
        currentView: 'workspace',
        get selectedLocalJobId() {
          return currentSelectedId;
        },
        setSelectedLocalJobId,
        updatePlan: vi.fn(),
        setCurrentView: vi.fn(),
        setCurrentTab: vi.fn(),
      };

      const { rerender } = render(<TransfersTabView state={mockState} client={mockClient} />);

      await waitFor(() => {
        expect(screen.getByTestId('bridge-export-form')).toBeDefined();
        expect(screen.getByTestId('bridge-job-monitor')).toBeDefined();
      });

      // User explicitly clicks top direction button to switch to Dosya -> IMAP
      const topImportBtn = screen.getByTestId('tab-direction-file-to-imap');
      fireEvent.click(topImportBtn);

      expect(setSelectedLocalJobId).toHaveBeenCalledWith(null);

      // Re-render with cleared selectedLocalJobId
      rerender(<TransfersTabView state={mockState} client={mockClient} />);

      await waitFor(() => {
        expect(screen.getByTestId('bridge-import-form')).toBeDefined();
      });
      expect(screen.queryByTestId('bridge-export-form')).toBeNull();
      expect(screen.queryByTestId('bridge-job-monitor')).toBeNull();

      // Now switch the single top-level direction to IMAP -> Dosya explicitly: stale job must still not return!
      const innerExportBtn = screen.getByTestId('tab-direction-imap-to-file');
      fireEvent.click(innerExportBtn);

      await waitFor(() => {
        expect(screen.getByTestId('bridge-export-form')).toBeDefined();
      });
      // The old completed job monitor must NEVER force itself back
      expect(screen.queryByTestId('bridge-job-monitor')).toBeNull();
      expect(screen.queryByText('job_bexp_stale_check')).toBeNull();
    });

    it('shows estimated and available disk capacity on export preview', async () => {
      render(
        <BridgeTransferWorkflow
          companyId="comp-1"
          companyName="Acme Corp"
          projectId="proj-1"
          projectName="Proje 1"
          client={mockClient}
          initialDirection="imap-to-file"
        />
      );

      const account = await screen.findByTestId('bridge-source-account-select');
      fireEvent.change(account, { target: { value: 'acc-corp-m365' } });
      await screen.findByTestId('imap-folders-table');
      fireEvent.click(screen.getByTestId('select-all-imap-folders-btn'));
      await act(async () => fireEvent.click(screen.getByTestId('pick-output-dir-btn')));
      await act(async () => fireEvent.click(screen.getByTestId('create-bridge-export-preview-btn')));

      expect((await screen.findByTestId('preview-estimated-required-bytes')).textContent).toContain('300.00 MiB');
      expect(screen.getByTestId('preview-available-free-bytes').textContent).toContain('1024.00 MiB');
    });
  });
});
