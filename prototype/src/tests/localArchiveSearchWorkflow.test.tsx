// @vitest-environment jsdom
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { ArchiveSearchView } from '../components/search/ArchiveSearchView';
import { MessagePreview } from '../components/workspace/MessagePreview';
import { JobCenter } from '../components/jobs/JobCenter';
import { BridgeTransferWorkflow } from '../components/transfer/BridgeTransferWorkflow';
import { LocalEngineClient } from '../api/localEngineClient';
import {
  ArchiveCatalogItemDto,
  ArchiveMessagePreviewResponse,
  ArchiveSearchResultItem,
  ArchiveSearchResponse,
  ArchiveIngestPreviewResponse,
  LocalJobRecord,
} from '../types/localEngine';
import { AppState } from '../state/useAppState';

describe('TASK-018 Local Archive and Search Frontend Tests', () => {
  let mockClient: LocalEngineClient;

  const sampleCatalog: ArchiveCatalogItemDto[] = [
    {
      archiveId: 'arch-1',
      archiveName: 'Arşiv 2024',
      companyId: 'comp-alpha',
      projectId: 'proj-finance',
      companyName: 'Alpha Holding',
      projectName: 'Mali Denetim',
      sourceKind: 'eml-tree',
      dialect: 'rfc822',
      sourceFingerprint: 'fp-111',
      totalItems: 12,
      totalSizeBytes: 1048576,
      truncatedItemsCount: 1,
      status: 'ready',
      createdAtUtc: '2026-09-14T00:00:00Z',
      indexedAtUtc: '2026-09-14T00:05:00Z',
      indexGeneration: 1,
    },
    {
      archiveId: 'arch-2',
      archiveName: 'MBOX Eski Postalar',
      companyId: 'comp-alpha',
      projectId: 'proj-legal',
      companyName: 'Alpha Holding',
      projectName: 'Hukuk Arşivi',
      sourceKind: 'mbox',
      dialect: 'mboxrd',
      sourceFingerprint: 'fp-222',
      totalItems: 8,
      totalSizeBytes: 524288,
      truncatedItemsCount: 0,
      status: 'index_failed',
      createdAtUtc: '2026-09-14T00:00:00Z',
      indexedAtUtc: null,
      indexGeneration: 0,
    },
    {
      archiveId: 'arch-3',
      archiveName: 'Bridge Aktarım Arşivi',
      companyId: 'comp-beta',
      projectId: 'proj-migration',
      companyName: 'Beta Teknoloji',
      projectName: 'Sistem Geçişi',
      sourceKind: 'eml-files',
      dialect: 'rfc822',
      sourceFingerprint: 'fp-333',
      totalItems: 20,
      totalSizeBytes: 2097152,
      truncatedItemsCount: 0,
      status: 'ready',
      createdAtUtc: '2026-09-14T00:00:00Z',
      indexedAtUtc: '2026-09-14T00:10:00Z',
      indexGeneration: 1,
    },
  ];

  const sampleSearchItems: ArchiveSearchResultItem[] = [
    {
      messageId: 'msg-001',
      archiveId: 'arch-1',
      companyId: 'comp-alpha',
      projectId: 'proj-finance',
      archiveName: 'Arşiv 2024',
      originalFolder: 'INBOX',
      sender: 'Ahmet Yılmaz',
      senderEmail: 'ahmet@alpha.com',
      recipients: 'Mehmet Öz <mehmet@alpha.com>',
      subject: '2024 Yılı Bütçe Raporu',
      snippet: 'Bütçe detayları ekteki tabloda sunulmuştur...',
      originalMimeDateUtc: '2024-03-15T10:30:00Z',
      formattedDate: '15.03.2024 13:30',
      hasAttachments: true,
      attachmentNames: ['butce.xlsx'],
      sizeBytes: 15420,
      isBodyTruncated: true,
    },
    {
      messageId: 'msg-002',
      archiveId: 'arch-1',
      companyId: 'comp-alpha',
      projectId: 'proj-finance',
      archiveName: 'Arşiv 2024',
      originalFolder: 'Sent',
      sender: 'Mehmet Öz',
      senderEmail: 'mehmet@alpha.com',
      recipients: 'Ahmet Yılmaz <ahmet@alpha.com>',
      subject: 'Bütçe Onayı',
      snippet: 'Teşekkürler, bütçe onaylanmıştır.',
      originalMimeDateUtc: '2024-03-15T11:00:00Z',
      formattedDate: '15.03.2024 14:00',
      hasAttachments: false,
      attachmentNames: [],
      sizeBytes: 3200,
      isBodyTruncated: false,
    },
  ];

  const sampleSearchResponse: ArchiveSearchResponse = {
    totalCount: 2,
    page: 1,
    pageSize: 20,
    items: sampleSearchItems,
    indexHealthy: true,
    warning: 'Bazı ileti gövdeleri 512 KiB sınırını aşmıştır.',
  };

  const samplePreviewResponse: ArchiveMessagePreviewResponse = {
    messageId: 'msg-001',
    archiveId: 'arch-1',
    subject: '2024 Yılı Bütçe Raporu',
    from: 'Ahmet Yılmaz <ahmet@alpha.com>',
    to: 'Mehmet Öz <mehmet@alpha.com>',
    cc: 'ayse@alpha.com',
    bcc: null,
    dateUtc: '2024-03-15T10:30:00Z',
    messageIdHeader: '<20240315-msg-001@alpha.com>',
    bodyText: 'Merhaba,\n\nBütçe detayları ekteki tabloda sunulmuştur.\nSaygılarımla.',
    isBodyTruncated: true,
    attachments: [
      {
        fileName: 'butce.xlsx',
        contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        sizeBytes: 12400,
        isInline: false,
        contentId: null,
      },
    ],
    rawSizeBytes: 15420,
    sha256: 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
  };

  const createMockAppState = (overrides?: Partial<AppState>): AppState => {
    return {
      currentTab: 'search',
      setCurrentTab: vi.fn(),
      currentView: 'workspace',
      setCurrentView: vi.fn(),
      companies: [],
      projects: [],
      sources: [],
      selectedCompanyId: null,
      setSelectedCompanyId: vi.fn(),
      selectedProjectId: null,
      setSelectedProjectId: vi.fn(),
      addCompany: vi.fn(),
      addProject: vi.fn(),
      addSource: vi.fn(),
      startTransferWithContext: vi.fn(),
      searchSelectedSourceIds: [],
      updateSearchSelectedSourceIds: vi.fn(),
      plan: {
        version: 1,
        id: 'plan-1',
        name: 'Test Plan',
        companyId: 'comp-1',
        projectId: 'proj-1',
        sourceId: 'src-1',
        client: 'Müşteri',
        sourceType: 'server',
        sourceAccount: 'user@test.com',
        sourceFolderId: 'inbox',
        sourceFolderName: 'Gelen kutusu',
        targetType: 'pst',
        targetAccount: 'archive.pst',
        targetFolderName: 'Archive',
        filters: {
          searchTerm: '',
          year: 'all',
          attachment: 'all',
          sender: '',
          minSizeBytes: null,
          maxSizeBytes: null,
        },
        manualSelectionMode: false,
        selectedMessageIds: [],
        duplicatePolicy: 'skip',
        preserveSource: true,
        oversizedResolution: 'none',
        planHash: 'hash-1',
        updatedAt: '2026-09-14T00:00:00Z',
      },
      updatePlan: vi.fn(),
      updateFilters: vi.fn(),
      jobs: [],
      setJobs: vi.fn(),
      toggleJobPause: vi.fn(),
      sourceMessages: [],
      filteredMessages: [],
      selectedMessage: undefined,
      selectedMessageId: 'msg-1',
      setSelectedMessageId: vi.fn(),
      preflightState: null,
      isPreflightStale: false,
      runPreflightCheck: vi.fn(),
      resolveOversizedItem: vi.fn(),
      toggleManualSelection: vi.fn(),
      toggleMessageSelected: vi.fn(),
      selectAllFiltered: vi.fn(),
      deselectAll: vi.fn(),
      handleStartTransfer: vi.fn(),
      handleResetDemoData: vi.fn(),
      simulation: {} as any,
      setSelectedLocalJobId: vi.fn(),
      openLocalJob: vi.fn(),
      setSelectedArchiveJobId: vi.fn(),
      openArchiveSearchJob: vi.fn(),
      setArchiveAddInitialJobId: vi.fn(),
      openArchiveSearchAdd: vi.fn(),
      settingsOpen: false,
      setSettingsOpen: vi.fn(),
      newJobMenuOpen: false,
      setNewJobMenuOpen: vi.fn(),
      draftJobModalType: null,
      setDraftJobModalType: vi.fn(),
      folderMappingModalOpen: false,
      setFolderMappingModalOpen: vi.fn(),
      addSourceModalOpen: false,
      setAddSourceModalOpen: vi.fn(),
      filterDrawerOpen: false,
      setFilterDrawerOpen: vi.fn(),
      blockerDetailModalOpen: false,
      setBlockerDetailModalOpen: vi.fn(),
      reportModalOpen: false,
      setReportModalOpen: vi.fn(),
      ...overrides,
      selectedLocalJobId: overrides?.selectedLocalJobId ?? null,
      selectedArchiveJobId: overrides?.selectedArchiveJobId ?? null,
      archiveAddInitialJobId: overrides?.archiveAddInitialJobId ?? null,
      archiveSearchScopeCount: overrides?.archiveSearchScopeCount ?? 0,
      setArchiveSearchScopeCount: overrides?.setArchiveSearchScopeCount ?? vi.fn(),
    };
  };

  beforeEach(() => {
    vi.clearAllMocks();
    mockClient = new LocalEngineClient('http://127.0.0.1:6174');
    mockClient.listAccounts = vi.fn().mockResolvedValue([]);
    mockClient.listAccountFolders = vi.fn().mockResolvedValue({ accountId: 'acc-test', folders: [] });
    mockClient.getArchiveCatalog = vi.fn().mockResolvedValue(sampleCatalog);
    mockClient.getArchiveManifest = vi.fn().mockResolvedValue({ folders: [{ folderName: 'INBOX' }] });
    mockClient.searchArchive = vi.fn().mockResolvedValue(sampleSearchResponse);
    mockClient.previewArchiveMessage = vi.fn().mockResolvedValue(samplePreviewResponse);
    mockClient.reindexArchive = vi.fn().mockResolvedValue({ jobId: 'job-reindex-1', status: 'queued' } as any);
    mockClient.reindexAllArchives = vi.fn().mockResolvedValue({ success: true, message: 'OK' });
    mockClient.pickArchiveSource = vi.fn().mockResolvedValue({
      cancelled: false,
      handle: 'handle-eml-001',
      fileName: 'YeniGelenler.eml',
      displayPath: 'C:\\Postalar\\YeniGelenler.eml',
      sizeBytes: 1048576,
    });
    mockClient.createArchivePreview = vi.fn().mockResolvedValue({
      previewId: 'prev-001',
      archiveName: 'Yeni Arşiv',
      companyId: 'comp-alpha',
      projectId: 'proj-finance',
      sourceKind: 'eml-files',
      dialect: 'rfc822',
      sourceFingerprint: 'fp-new',
      totalFiles: 5,
      totalItems: 5,
      totalSizeBytes: 512000,
      folders: [{ folderName: 'INBOX', itemCount: 5, totalSizeBytes: 512000 }],
      canIngest: true,
      blockerReason: null,
      estimatedRequiredBytes: 270483456,
      availableFreeBytes: 1073741824,
      estimateBasis: 'Aynı hacimde ham içerik ve indeks geçici alan toplamı',
    } as ArchiveIngestPreviewResponse);
    mockClient.startArchiveIngest = vi.fn().mockResolvedValue({
      jobId: 'job-ingest-001',
      jobKind: 'archive-ingest',
      status: 'queued',
    } as LocalJobRecord);
    mockClient.resumeArchiveIngest = vi.fn().mockResolvedValue({
      jobId: 'job-resume-001',
      jobKind: 'archive-ingest',
      status: 'queued',
    } as LocalJobRecord);
    mockClient.getAllJobs = vi.fn().mockResolvedValue([]);
    mockClient.getJob = vi.fn().mockResolvedValue({
      jobId: 'job-arch-nav',
      jobKind: 'archive-ingest',
      archiveId: 'arch-1',
      archiveName: 'Arşiv 2024',
      clientContext: {
        companyId: 'comp-alpha',
        companyName: 'Alpha Holding',
        projectId: 'proj-finance',
        projectName: 'Mali Denetim',
      },
      status: 'completed',
    } as LocalJobRecord);
  });

  // 1. catalog-without-localStorage
  it('renders location tree grouped authoritatively from catalog when localStorage is empty', async () => {
    const emptyState = createMockAppState({ companies: [], projects: [] });

    render(<ArchiveSearchView state={emptyState} client={mockClient} />);

    await waitFor(() => {
      expect(mockClient.getArchiveCatalog).toHaveBeenCalledTimes(1);
    });

    // Authoritative frozen snapshot names rendered without localStorage records
    expect(await screen.findByText('Alpha Holding')).toBeDefined();
    expect(await screen.findByText('Beta Teknoloji')).toBeDefined();
    expect(await screen.findByText('Mali Denetim')).toBeDefined();
    expect(await screen.findByText('Hukuk Arşivi')).toBeDefined();
    expect(await screen.findByText('Sistem Geçişi')).toBeDefined();
    expect(await screen.findByText('Arşiv 2024')).toBeDefined();
    expect(await screen.findByText('MBOX Eski Postalar')).toBeDefined();
    expect(await screen.findByText('Bridge Aktarım Arşivi')).toBeDefined();
  });

  // 2. empty scope
  it('displays 0 results and empty scope prompt when no archives are selected', async () => {
    const state = createMockAppState();

    render(<ArchiveSearchView state={state} client={mockClient} />);

    await waitFor(() => {
      expect(screen.getByTestId('search-empty-scope-prompt')).toBeDefined();
    });

    expect(screen.getByTestId('search-scope-summary-badge').textContent).toContain('Konum seçilmedi');
    expect(screen.getByTestId('search-status-bar').textContent).toContain('Konum seçilmedi · 0 sonuç');
    expect(mockClient.searchArchive).not.toHaveBeenCalled();
  });

  // 3. selection tree (tri-state selection & clear/select-all)
  it('supports tri-state company/project/archive multi-selection and select all / clear actions', async () => {
    const state = createMockAppState();

    render(<ArchiveSearchView state={state} client={mockClient} />);

    await screen.findByText('Alpha Holding');

    const compAlphaCheckbox = screen.getByTestId('checkbox-company-comp-alpha') as HTMLInputElement;
    const projFinanceCheckbox = screen.getByTestId('checkbox-project-proj-finance') as HTMLInputElement;
    const arch1Checkbox = screen.getByTestId('checkbox-archive-arch-1') as HTMLInputElement;
    const arch2Checkbox = screen.getByTestId('checkbox-archive-arch-2') as HTMLInputElement;

    // Initially none selected
    expect(compAlphaCheckbox.checked).toBe(false);
    expect(compAlphaCheckbox.indeterminate).toBe(false);

    // 1. Select company comp-alpha: selects arch-1 and arch-2
    fireEvent.click(compAlphaCheckbox);
    expect(arch1Checkbox.checked).toBe(true);
    expect(arch2Checkbox.checked).toBe(true);
    expect(projFinanceCheckbox.checked).toBe(true);
    expect(compAlphaCheckbox.checked).toBe(true);
    expect(compAlphaCheckbox.indeterminate).toBe(false);

    // 2. Deselect single archive arch-2: comp-alpha becomes indeterminate!
    fireEvent.click(arch2Checkbox);
    expect(arch1Checkbox.checked).toBe(true);
    expect(arch2Checkbox.checked).toBe(false);
    expect(compAlphaCheckbox.indeterminate).toBe(true);

    // 3. Select all button selects all archives in catalog
    const selectAllBtn = screen.getByTestId('search-select-all-btn');
    fireEvent.click(selectAllBtn);
    expect(arch1Checkbox.checked).toBe(true);
    expect(arch2Checkbox.checked).toBe(true);
    expect(compAlphaCheckbox.checked).toBe(true);
    expect(compAlphaCheckbox.indeterminate).toBe(false);

    // 4. Clear selection button clears all
    const clearBtn = screen.getByTestId('search-clear-selection-btn');
    fireEvent.click(clearBtn);
    expect(arch1Checkbox.checked).toBe(false);
    expect(arch2Checkbox.checked).toBe(false);
    expect(compAlphaCheckbox.checked).toBe(false);
    expect(screen.getByTestId('search-empty-scope-prompt')).toBeDefined();
  });

  // 4. request payloads
  it('dispatches search request with exact scope triples, literal text, closed field, and UTC+03 dates', async () => {
    const state = createMockAppState();

    render(<ArchiveSearchView state={state} client={mockClient} />);

    await screen.findByText('Arşiv 2024');

    // Select arch-1
    fireEvent.click(screen.getByTestId('checkbox-archive-arch-1'));

    // Set search parameters
    const searchInput = screen.getByTestId('archive-search-input');
    fireEvent.change(searchInput, { target: { value: 'Bütçe Raporu' } });

    const fieldSelect = screen.getByTestId('archive-search-field-select');
    fireEvent.change(fieldSelect, { target: { value: 'subject' } });

    const startDateInput = screen.getByTestId('archive-search-start-date');
    fireEvent.change(startDateInput, { target: { value: '2024-01-01' } });

    const endDateInput = screen.getByTestId('archive-search-end-date');
    fireEvent.change(endDateInput, { target: { value: '2024-12-31' } });

    const attachmentFilter = screen.getByTestId('archive-search-attachment-filter');
    fireEvent.change(attachmentFilter, { target: { value: 'with' } });

    const folderFilter = screen.getByTestId('archive-folder-filter');
    await screen.findByRole('option', { name: 'INBOX' });
    fireEvent.change(folderFilter, { target: { value: 'INBOX' } });

    await waitFor(() => {
      expect(mockClient.searchArchive).toHaveBeenCalledWith(
        expect.objectContaining({
          selectedScopes: [
            { companyId: 'comp-alpha', projectId: 'proj-finance', archiveId: 'arch-1' },
          ],
          query: 'Bütçe Raporu',
          field: 'subject',
          startDate: '2024-01-01',
          endDate: '2024-12-31',
          hasAttachment: true,
          folder: 'INBOX',
          page: 1,
          pageSize: 20,
        }),
        expect.anything()
      );
    });
  });

  // 5. stale search and preview response handling
  it('aborts and ignores stale search and preview responses when filters or scope change', async () => {
    let resolveFirstSearch: (res: ArchiveSearchResponse) => void = () => {};
    mockClient.searchArchive = vi.fn().mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveFirstSearch = resolve;
        })
    );

    const state = createMockAppState();
    render(<ArchiveSearchView state={state} client={mockClient} />);

    await screen.findByText('Arşiv 2024');
    fireEvent.click(screen.getByTestId('checkbox-archive-arch-1'));

    // First search is triggered and pending
    expect(mockClient.searchArchive).toHaveBeenCalledTimes(1);

    // User types new query before first search resolves
    fireEvent.change(screen.getByTestId('archive-search-input'), { target: { value: 'Yeni' } });
    expect(mockClient.searchArchive).toHaveBeenCalledTimes(2);

    // Resolve first (stale) search with old item
    act(() => {
      resolveFirstSearch({
        totalCount: 1,
        page: 1,
        pageSize: 20,
        items: [
          {
            ...sampleSearchItems[0],
            subject: 'Eski Bayat Sonuç',
          },
        ],
        indexHealthy: true,
      });
    });

    // Stale result should NOT be rendered in DOM!
    expect(screen.queryByText('Eski Bayat Sonuç')).toBeNull();
  });

  // 6. full-filter preview
  it('sends MessageId PLUS full current ArchiveSearchRequest for message preview and renders details', async () => {
    const state = createMockAppState();
    render(<ArchiveSearchView state={state} client={mockClient} />);

    await screen.findByText('Arşiv 2024');
    fireEvent.click(screen.getByTestId('checkbox-archive-arch-1'));

    // Results table appears
    const resultRow = await screen.findByTestId('search-result-row-msg-001');
    fireEvent.click(resultRow);

    await waitFor(() => {
      expect(mockClient.previewArchiveMessage).toHaveBeenCalledWith(
        expect.objectContaining({
          messageId: 'msg-001',
          searchRequest: expect.objectContaining({
            selectedScopes: [
              { companyId: 'comp-alpha', projectId: 'proj-finance', archiveId: 'arch-1' },
            ],
            page: 1,
          }),
        }),
        expect.anything()
      );
    });

    // Preview rendered with metadata and truncation alert
    expect((await screen.findByTestId('preview-subject')).textContent).toContain('2024 Yılı Bütçe Raporu');
    expect((await screen.findByTestId('preview-sender')).textContent).toContain('Ahmet Yılmaz <ahmet@alpha.com>');
    expect((await screen.findByTestId('preview-to')).textContent).toContain('Mehmet Öz <mehmet@alpha.com>');
    expect((await screen.findByTestId('preview-cc')).textContent).toContain('ayse@alpha.com');
    expect(await screen.findByTestId('preview-body-truncated-alert')).toBeDefined();
    expect((await screen.findByTestId('preview-sha256')).textContent).toContain('e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855');
  });

  // 7. add flows (MIME picker & bridge export preview/start)
  it('completes archive add flow via native picker and bridge export with preview then start', async () => {
    const state = createMockAppState({
      companies: [{ id: 'comp-alpha', name: 'Alpha Holding', code: 'ALP', projectIds: ['proj-finance'] }],
      projects: [{ id: 'proj-finance', companyId: 'comp-alpha', name: 'Mali Denetim', sourceIds: [] }],
    });

    render(<ArchiveSearchView state={state} client={mockClient} />);

    // Open add modal
    const openAddBtn = await screen.findByTestId('archive-open-add-modal-btn');
    fireEvent.click(openAddBtn);

    expect(screen.getByTestId('archive-add-modal')).toBeDefined();

    // 1. Pick source
    const pickSourceBtn = screen.getByTestId('archive-pick-source-btn');
    fireEvent.click(pickSourceBtn);

    await waitFor(() => {
      expect(mockClient.pickArchiveSource).toHaveBeenCalledWith('eml-files');
    });
    expect(await screen.findByTestId('archive-picked-source-info')).toBeDefined();

    // 2. Preview
    const previewBtn = screen.getByTestId('archive-preview-btn');
    fireEvent.click(previewBtn);

    await waitFor(() => {
      expect(mockClient.createArchivePreview).toHaveBeenCalledWith(
        expect.objectContaining({
          archiveName: 'YeniGelenler',
          companyId: 'comp-alpha',
          projectId: 'proj-finance',
          sourceHandle: 'handle-eml-001',
        })
      );
    });
    expect(await screen.findByTestId('archive-preview-summary')).toBeDefined();
    expect(screen.getByTestId('archive-estimated-required-bytes').textContent).toContain('258 MB');
    expect(screen.getByTestId('archive-available-free-bytes').textContent).toContain('1 GB');

    // 3. Start
    const startBtn = screen.getByTestId('archive-start-btn');
    fireEvent.click(startBtn);

    await waitFor(() => {
      expect(mockClient.startArchiveIngest).toHaveBeenCalledWith(
        expect.objectContaining({
          previewId: 'prev-001',
          idempotencyKey: expect.stringMatching(/^arch-start-/),
        })
      );
    });

    // Catalog refreshed after start
    expect(mockClient.getArchiveCatalog).toHaveBeenCalledTimes(2);
  });

  // 8. resume / reindex actions
  it('triggers archive reindex and reindex-all actions from catalog tree', async () => {
    const state = createMockAppState();

    render(<ArchiveSearchView state={state} client={mockClient} />);

    await screen.findByText('MBOX Eski Postalar');

    // Archive 2 has status index_failed, shows Onar button
    const reindexBtn = screen.getByTestId('reindex-archive-btn-arch-2');
    fireEvent.click(reindexBtn);

    await waitFor(() => {
      expect(mockClient.reindexArchive).toHaveBeenCalledWith('arch-2', true);
    });

    // Reindex all button
    const reindexAllBtn = screen.getByTestId('reindex-all-btn');
    fireEvent.click(reindexAllBtn);

    await waitFor(() => {
      expect(mockClient.reindexAllArchives).toHaveBeenCalledTimes(1);
    });
  });

  // 9. JobCenter routing and frozen context
  it('routes archive-ingest jobs to Archive Search instead of PST split and displays frozen context', async () => {
    const archiveJob: LocalJobRecord = {
      jobId: 'job-arch-001',
      jobKind: 'archive-ingest',
      archiveId: 'arch-1',
      archiveName: 'Arşiv 2024',
      clientContext: {
        companyId: 'comp-alpha',
        companyName: 'Alpha Holding',
        projectId: 'proj-finance',
        projectName: 'Mali Denetim',
      },
      sourceFileName: 'corpus.mbox',
      targetFileName: 'Arşiv 2024',
      status: 'completed',
      stage: 'Tamamlandı',
      itemsRead: 12,
      itemsWritten: 12,
      failedItems: 0,
      totalItems: 12,
      currentFolder: 'INBOX',
      percentComplete: 100,
      createdAt: '2026-09-14T00:00:00Z',
    };

    mockClient.getAllJobs = vi.fn().mockResolvedValue([archiveJob]);
    mockClient.getJob = vi.fn().mockResolvedValue(archiveJob);

    let navigatedJob: any = null;
    const state = createMockAppState({
      currentTab: 'jobs',
      openArchiveSearchJob: vi.fn((jobId, compId, projId, archId) => {
        navigatedJob = { jobId, compId, projId, archId };
      }),
    });

    render(
      <JobCenter
        jobs={[]}
        onToggleJobPause={vi.fn()}
        onNavigateToWorkspace={(job) => {
          if (job?.jobKind === 'archive-ingest' || job?.jobKind === 'archive-reindex') {
            state.openArchiveSearchJob(job.id, job.companyId, job.projectId, job.archiveId);
          }
        }}
        onAddNewJob={vi.fn()}
        client={mockClient}
      />
    );

    // Job title mapped to Arşiv Alma
    const titleElements = await screen.findAllByText(/Arşiv Alma/);
    expect(titleElements.length).toBeGreaterThan(0);
    expect((await screen.findByTestId('job-verification-note')).textContent).toContain('Yerel Arşiv Alma');

    // Click open job
    const openBtn = screen.getByTestId('job-open-btn');
    fireEvent.click(openBtn);

    expect(state.openArchiveSearchJob).toHaveBeenCalledWith(
      'job-arch-001',
      'comp-alpha',
      'proj-finance',
      'arch-1'
    );
    expect(navigatedJob?.jobId).toBe('job-arch-001');

    // Now render ArchiveSearchView with selectedArchiveJobId to verify frozen context banner
    const searchState = createMockAppState({
      selectedArchiveJobId: 'job-arch-001',
    });

    render(<ArchiveSearchView state={searchState} client={mockClient} />);

    expect((await screen.findByTestId('archive-frozen-context')).textContent).toContain('Alpha Holding');
    expect((await screen.findByTestId('archive-frozen-context')).textContent).toContain('Mali Denetim');
  });

  // 10. safe HTML-looking text through React escaping
  it('safely renders HTML-looking text as plain escaped text without script execution or image loading', () => {
    const untrustedPreview: ArchiveMessagePreviewResponse = {
      messageId: 'msg-untrusted',
      archiveId: 'arch-1',
      subject: '<script>alert("xss")</script>',
      from: 'hacker <hacker@test.com>',
      to: 'victim <victim@test.com>',
      dateUtc: '2026-09-14T00:00:00Z',
      bodyText: '<div id="malicious"><script>window.__pwned__ = true;</script><img src="http://evil.test/pixel.png" onerror="alert(1)" />Zararsız metin</div>',
      isBodyTruncated: false,
      attachments: [],
      rawSizeBytes: 100,
      sha256: 'abc123hash',
    };

    render(<MessagePreview archiveMessage={untrustedPreview} />);

    const bodyEl = screen.getByTestId('preview-body');

    // Text content matches the literal raw string
    expect(bodyEl.textContent).toContain('<script>window.__pwned__ = true;</script>');
    expect(bodyEl.textContent).toContain('<img src="http://evil.test/pixel.png" onerror="alert(1)" />');

    // No <script> element or <img> element created inside the preview body!
    expect(bodyEl.querySelector('script')).toBeNull();
    expect(bodyEl.querySelector('img')).toBeNull();
    expect(bodyEl.querySelector('#malicious')).toBeNull();
    expect((window as any).__pwned__).toBeUndefined();
  });

  // 11. mobile-friendly layout and toggle
  it('toggles mobile location panel visibility via mobile toggle button', async () => {
    const state = createMockAppState();
    render(<ArchiveSearchView state={state} client={mockClient} />);

    const toggleBtn = screen.getByTestId('mobile-location-toggle-btn');
    const panel = screen.getByTestId('search-location-tree-panel');

    // Initially open (not mobile-hidden)
    expect(panel.className).not.toContain('mobile-hidden');

    // Click toggle: adds mobile-hidden
    fireEvent.click(toggleBtn);
    expect(panel.className).toContain('mobile-hidden');

    // Click toggle again: removes mobile-hidden
    fireEvent.click(toggleBtn);
    expect(panel.className).not.toContain('mobile-hidden');
  });

  // 12. AddArchiveModal: no user-editable sourceJobId textbox; readable completed bridge-export selection hides internal ID and preserves frozen company/project
  it('AddArchiveModal has no user-editable sourceJobId textbox, presents readable bridge-export selection, and enforces frozen company/project', async () => {
    const completedExportJob: LocalJobRecord = {
      jobId: 'job-bridge-exp-77',
      jobKind: 'bridge-export',
      clientContext: {
        companyId: 'comp-alpha',
        companyName: 'Alpha Holding',
        projectId: 'proj-finance',
        projectName: 'Mali Denetim',
      },
      sourceFileName: 'user@imap.com (INBOX)',
      targetFileName: 'Alpha-Export-2024.mbox',
      status: 'completed',
      stage: 'Tamamlandı',
      itemsRead: 45,
      itemsWritten: 45,
      failedItems: 0,
      totalItems: 45,
      currentFolder: 'INBOX',
      percentComplete: 100,
      createdAt: '2026-09-14T00:00:00Z',
    };

    mockClient.getAllJobs = vi.fn().mockResolvedValue([completedExportJob]);

    const state = createMockAppState({
      companies: [
        { id: 'comp-alpha', name: 'Alpha Holding', code: 'ALP', projectIds: ['proj-finance'] },
        { id: 'comp-beta', name: 'Beta Teknoloji', code: 'BET', projectIds: ['proj-other'] },
      ],
      projects: [
        { id: 'proj-finance', companyId: 'comp-alpha', name: 'Mali Denetim', sourceIds: [] },
        { id: 'proj-other', companyId: 'comp-beta', name: 'Diğer Proje', sourceIds: [] },
      ],
    });

    render(<ArchiveSearchView state={state} client={mockClient} />);

    // Open add modal
    const openAddBtn = await screen.findByTestId('archive-open-add-modal-btn');
    fireEvent.click(openAddBtn);

    expect(screen.getByTestId('archive-add-modal')).toBeDefined();

    // In initial MIME mode, company/project selects are visible and editable
    expect(screen.getByTestId('archive-add-company-select')).toBeDefined();
    expect(screen.getByTestId('archive-add-project-select')).toBeDefined();

    // Switch to Bridge Export mode
    const bridgeModeBtn = screen.getByTestId('source-type-bridge-btn');
    fireEvent.click(bridgeModeBtn);

    // 1. Verify NO user-editable sourceJobId textbox exists anywhere in the modal
    expect(screen.queryByTestId('archive-source-job-id-input')).toBeNull();
    expect(screen.queryByPlaceholderText(/iş numarası|job id/i)).toBeNull();

    // 2. Verify readable completed bridge-export selection is rendered
    const jobSelect = (await screen.findByTestId('archive-completed-bridge-jobs-select')) as HTMLSelectElement;
    expect(jobSelect).toBeDefined();

    // Option value is internal jobId, but option text is readable
    const option = jobSelect.querySelector('option[value="job-bridge-exp-77"]') as HTMLOptionElement;
    expect(option).toBeDefined();
    expect(option.textContent).toContain('Alpha Holding / Mali Denetim');
    expect(option.textContent).toContain('user@imap.com (INBOX) → Alpha-Export-2024.mbox');

    // 3. Verify frozen company/project context is displayed and company/project dropdowns are hidden (cannot be overwritten)
    const frozenOwnerBanner = screen.getByTestId('archive-frozen-owner-context');
    expect(frozenOwnerBanner.textContent).toContain('Alpha Holding > Mali Denetim');
    expect(screen.queryByTestId('archive-add-company-select')).toBeNull();
    expect(screen.queryByTestId('archive-add-project-select')).toBeNull();

    // Switch back to MIME mode: separate file selection restores company/project selectors
    const mimeModeBtn = screen.getByTestId('source-type-mime-btn');
    fireEvent.click(mimeModeBtn);
    expect(screen.getByTestId('archive-add-company-select')).toBeDefined();
    expect(screen.getByTestId('archive-add-project-select')).toBeDefined();
    expect(screen.queryByTestId('archive-frozen-owner-context')).toBeNull();
  });

  // 13. One-click "Arşive ekle" from JobCenter and BridgeTransferWorkflow routes and prefills AddArchiveModal
  it('completed bridge-export provides one-click Arşive ekle navigation and prefills AddArchiveModal with frozen context', async () => {
    const completedExportJob: LocalJobRecord = {
      jobId: 'job-bridge-exp-88',
      jobKind: 'bridge-export',
      clientContext: {
        companyId: 'comp-alpha',
        companyName: 'Alpha Holding',
        projectId: 'proj-finance',
        projectName: 'Mali Denetim',
      },
      sourceFileName: 'export-src',
      targetFileName: 'export-target.mbox',
      status: 'completed',
      stage: 'Tamamlandı',
      itemsRead: 30,
      itemsWritten: 30,
      failedItems: 0,
      totalItems: 30,
      currentFolder: 'INBOX',
      percentComplete: 100,
      createdAt: '2026-09-14T00:00:00Z',
    };

    mockClient.getAllJobs = vi.fn().mockResolvedValue([completedExportJob]);
    mockClient.getJob = vi.fn().mockResolvedValue(completedExportJob);
    mockClient.getJobReport = vi.fn().mockResolvedValue({ jobId: 'job-bridge-exp-88', summary: {} } as any);

    let archiveAddJobId: string | null = null;
    let archiveAddCompId: string | undefined = undefined;
    let archiveAddProjId: string | undefined = undefined;

    const onAddToArchiveJobCenter = vi.fn((job: any) => {
      archiveAddJobId = job.id;
      archiveAddCompId = job.companyId;
      archiveAddProjId = job.projectId;
    });

    // 1. Verify JobCenter renders Arşive ekle for completed bridge-export
    const { unmount: unmountJobCenter } = render(
      <JobCenter
        jobs={[]}
        onToggleJobPause={vi.fn()}
        onNavigateToWorkspace={vi.fn()}
        onAddNewJob={vi.fn()}
        client={mockClient}
        onAddToArchive={onAddToArchiveJobCenter}
      />
    );

    const jobCenterAddBtn = await screen.findByTestId('job-add-to-archive-btn');
    expect(jobCenterAddBtn.textContent).toContain('Arşive ekle');
    fireEvent.click(jobCenterAddBtn);

    expect(onAddToArchiveJobCenter).toHaveBeenCalledTimes(1);
    expect(archiveAddJobId).toBe('job-bridge-exp-88');
    expect(archiveAddCompId).toBe('comp-alpha');
    expect(archiveAddProjId).toBe('proj-finance');
    unmountJobCenter();

    // 2. Verify BridgeTransferWorkflow renders Arşive ekle for completed bridge-export
    const onAddToArchiveBridge = vi.fn();
    const { unmount: unmountBridge } = render(
      <BridgeTransferWorkflow
        companyId="comp-alpha"
        companyName="Alpha Holding"
        projectId="proj-finance"
        projectName="Mali Denetim"
        initialDirection="imap-to-file"
        initialJob={completedExportJob}
        client={mockClient}
        onAddToArchive={onAddToArchiveBridge}
      />
    );

    const bridgeAddBtn = await screen.findByTestId('bridge-export-add-to-archive-btn');
    expect(bridgeAddBtn.textContent).toContain('Arşive ekle');
    fireEvent.click(bridgeAddBtn);

    expect(onAddToArchiveBridge).toHaveBeenCalledWith(completedExportJob);
    unmountBridge();

    // 3. Verify ArchiveSearchView automatically opens AddArchiveModal and pre-selects bridge export when navigated with archiveAddInitialJobId
    const searchState = createMockAppState({
      archiveAddInitialJobId: 'job-bridge-exp-88',
      companies: [{ id: 'comp-alpha', name: 'Alpha Holding', code: 'ALP', projectIds: ['proj-finance'] }],
      projects: [{ id: 'proj-finance', companyId: 'comp-alpha', name: 'Mali Denetim', sourceIds: [] }],
    });

    render(<ArchiveSearchView state={searchState} client={mockClient} />);

    // Modal opens automatically without needing to click "Yeni Arşiv Ekle"
    expect(await screen.findByTestId('archive-add-modal')).toBeDefined();
    expect(await screen.findByTestId('archive-frozen-owner-context')).toBeDefined();
    expect(screen.getByTestId('archive-frozen-owner-context').textContent).toContain('Alpha Holding > Mali Denetim');

    const jobSelect = (await screen.findByTestId('archive-completed-bridge-jobs-select')) as HTMLSelectElement;
    expect(jobSelect.value).toBe('job-bridge-exp-88');
  });

  // 14. Location tree company collapse and re-expand on successive clicks
  it('default-expanded company collapses and re-expands on click in authoritative tree', async () => {
    const state = createMockAppState();

    render(<ArchiveSearchView state={state} client={mockClient} />);

    // Wait for catalog tree to load
    await screen.findByTestId('tree-company-comp-alpha');

    // 1. By default, company projects are expanded and visible
    expect(screen.getByTestId('tree-company-projects-comp-alpha')).toBeDefined();
    expect(screen.getByText('Mali Denetim')).toBeDefined();

    // 2. Click company toggle to collapse
    const companyToggle = screen.getByTestId('tree-company-toggle-comp-alpha');
    fireEvent.click(companyToggle);

    // Company projects container is hidden (removed from DOM)
    expect(screen.queryByTestId('tree-company-projects-comp-alpha')).toBeNull();
    expect(screen.queryByText('Mali Denetim')).toBeNull();

    // 3. Click company toggle again to re-expand
    fireEvent.click(companyToggle);

    // Company projects container is visible again
    expect(screen.getByTestId('tree-company-projects-comp-alpha')).toBeDefined();
    expect(screen.getByText('Mali Denetim')).toBeDefined();
  });

  // 15. authoritative company/project display names in preview/result with localStorage app owners absent
  it('displays authoritative company/project names in preview and result location with localStorage app owners absent', async () => {
    const emptyState = createMockAppState({ companies: [], projects: [] });

    render(<ArchiveSearchView state={emptyState} client={mockClient} />);

    await screen.findByText('Alpha Holding');

    // Select arch-1 to search
    fireEvent.click(screen.getByTestId('checkbox-archive-arch-1'));

    // Wait for search result row to appear
    const resultRow = await screen.findByTestId('search-result-row-msg-001');
    expect(resultRow).toBeDefined();

    // Result location column must reflect authoritative company/project display name
    const locationCell = screen.getByTestId('search-result-location-msg-001');
    expect(locationCell.textContent).toContain('Alpha Holding');
    expect(locationCell.textContent).toContain('Mali Denetim');

    // Click result row to open preview
    fireEvent.click(resultRow);

    await waitFor(() => {
      expect(mockClient.previewArchiveMessage).toHaveBeenCalled();
    });

    // Preview location badge must render authoritative company & project names (not raw IDs or empty)
    const companyBadge = await screen.findByTestId('preview-location-company');
    const projectBadge = await screen.findByTestId('preview-location-project');
    expect(companyBadge.textContent).toBe('Alpha Holding');
    expect(projectBadge.textContent).toBe('Mali Denetim');
  });

  // 16. delayed getJob transitions queued/running/completed then catalog refetch reveals/selects new archive
  it('transitions getJob through queued, running, completed with deferred promises then refetches catalog and selects new archive', async () => {
    const newArchiveItem: ArchiveCatalogItemDto = {
      archiveId: 'arch-delayed-99',
      archiveName: 'Gecikmeli Arşiv',
      companyId: 'comp-alpha',
      projectId: 'proj-finance',
      companyName: 'Alpha Holding',
      projectName: 'Mali Denetim',
      sourceKind: 'eml-tree',
      dialect: 'rfc822',
      sourceFingerprint: 'fp-delayed',
      totalItems: 5,
      totalSizeBytes: 102400,
      truncatedItemsCount: 0,
      status: 'ready',
      createdAtUtc: '2026-09-14T01:00:00Z',
      indexedAtUtc: '2026-09-14T01:05:00Z',
      indexGeneration: 1,
    };

    let resolveQueued: (job: LocalJobRecord) => void = () => {};
    let resolveRunning: (job: LocalJobRecord) => void = () => {};
    let resolveCompleted: (job: LocalJobRecord) => void = () => {};

    let callCount = 0;
    mockClient.getJob = vi.fn().mockImplementation(() => {
      callCount++;
      if (callCount === 1) {
        return new Promise<LocalJobRecord>((resolve) => {
          resolveQueued = resolve;
        });
      }
      if (callCount === 2) {
        return new Promise<LocalJobRecord>((resolve) => {
          resolveRunning = resolve;
        });
      }
      return new Promise<LocalJobRecord>((resolve) => {
        resolveCompleted = resolve;
      });
    });

    const state = createMockAppState({
      selectedArchiveJobId: 'job-delayed-dyn',
    });

    render(<ArchiveSearchView state={state} client={mockClient} />);

    // Initial catalog fetch on mount
    await waitFor(() => {
      expect(mockClient.getArchiveCatalog).toHaveBeenCalledTimes(1);
    });

    // 1. Resolve first getJob as queued
    act(() => {
      resolveQueued({
        sourceFileName: 'fixture.eml', targetFileName: 'Gecikmeli Arşiv', currentFolder: '',
        jobId: 'job-delayed-dyn',
        jobKind: 'archive-ingest',
        status: 'queued',
        stage: 'Sırada',
        itemsRead: 0,
        itemsWritten: 0,
        failedItems: 0,
        totalItems: 5,
        percentComplete: 0,
        createdAt: '2026-09-14T00:00:00Z',
        clientContext: {
          companyId: 'comp-alpha',
          companyName: 'Alpha Holding',
          projectId: 'proj-finance',
          projectName: 'Mali Denetim',
        },
      });
    });

    await waitFor(() => expect(mockClient.getJob).toHaveBeenCalledTimes(2));
    // 2. Resolve second getJob as converting
    act(() => {
      resolveRunning({
        sourceFileName: 'fixture.eml', targetFileName: 'Gecikmeli Arşiv', currentFolder: '',
        jobId: 'job-delayed-dyn',
        jobKind: 'archive-ingest',
        status: 'converting',
        stage: 'İndeksleniyor',
        itemsRead: 3,
        itemsWritten: 3,
        failedItems: 0,
        totalItems: 5,
        percentComplete: 60,
        createdAt: '2026-09-14T00:00:00Z',
        clientContext: {
          companyId: 'comp-alpha',
          companyName: 'Alpha Holding',
          projectId: 'proj-finance',
          projectName: 'Mali Denetim',
        },
      });
    });

    await waitFor(() => expect(mockClient.getJob).toHaveBeenCalledTimes(3));
    // Update catalog mock for when catalog is refetched
    mockClient.getArchiveCatalog = vi.fn().mockResolvedValue([...sampleCatalog, newArchiveItem]);

    // 3. Resolve third getJob as completed with archiveId
    act(() => {
      resolveCompleted({
        sourceFileName: 'fixture.eml', targetFileName: 'Gecikmeli Arşiv', currentFolder: '',
        jobId: 'job-delayed-dyn',
        jobKind: 'archive-ingest',
        archiveId: 'arch-delayed-99',
        archiveName: 'Gecikmeli Arşiv',
        status: 'completed',
        stage: 'Tamamlandı',
        itemsRead: 5,
        itemsWritten: 5,
        failedItems: 0,
        totalItems: 5,
        percentComplete: 100,
        createdAt: '2026-09-14T00:00:00Z',
        clientContext: {
          companyId: 'comp-alpha',
          companyName: 'Alpha Holding',
          projectId: 'proj-finance',
          projectName: 'Mali Denetim',
        },
      });
    });

    // Catalog must be refetched following completed job transition
    await waitFor(() => {
      expect(mockClient.getArchiveCatalog).toHaveBeenCalled();
    });

    // Newly revealed archive must be present in tree and selected
    const newArchCheckbox = (await screen.findByTestId('checkbox-archive-arch-delayed-99')) as HTMLInputElement;
    expect(newArchCheckbox).toBeDefined();
    expect(newArchCheckbox.checked).toBe(true);
  });

  // 17. selected multiple archives fetch/cache their manifests and folder select contains actual folders only; invalid folder clears when scope changes
  it('fetches and caches manifests for multiple selected archives, limits folder select to actual folders only, and clears invalid folder on scope change', async () => {
    (mockClient as any).getArchiveManifest = vi.fn().mockImplementation(async (archiveId: string) => {
      if (archiveId === 'arch-1') {
        return {
          archiveId: 'arch-1',
          folders: [{ folderName: 'Gelen Kutusu' }, { folderName: 'Faturalar' }],
        };
      }
      if (archiveId === 'arch-3') {
        return {
          archiveId: 'arch-3',
          folders: [{ folderName: 'Arsiv-2023' }, { folderName: 'Faturalar' }],
        };
      }
      return { archiveId, folders: [] };
    });

    const state = createMockAppState();
    render(<ArchiveSearchView state={state} client={mockClient} />);

    await screen.findByText('Arşiv 2024');

    // 1. Select arch-1: manifests fetched/cached for arch-1
    fireEvent.click(screen.getByTestId('checkbox-archive-arch-1'));

    await waitFor(() => {
      expect((mockClient as any).getArchiveManifest).toHaveBeenCalledWith('arch-1');
    });

    // 2. Select arch-3: manifests fetched/cached for arch-3 without refetching arch-1
    await screen.findByRole('option', { name: 'Gelen Kutusu' });
    fireEvent.click(screen.getByTestId('checkbox-archive-arch-3'));

    await waitFor(() => {
      expect((mockClient as any).getArchiveManifest).toHaveBeenCalledWith('arch-3');
      expect((mockClient as any).getArchiveManifest).toHaveBeenCalledTimes(2);
    });

    // Deselect and re-select arch-1 to verify cache (should not call getArchiveManifest again)
    await screen.findByRole('option', { name: 'Arsiv-2023' });
    fireEvent.click(screen.getByTestId('checkbox-archive-arch-1'));
    fireEvent.click(screen.getByTestId('checkbox-archive-arch-1'));
    expect((mockClient as any).getArchiveManifest).toHaveBeenCalledTimes(2);

    // 3. Verify folder select contains actual folders only (no hardcoded dummy folders like 'INBOX (Gelen kutusu)' or 'SENT')
    const folderSelect = screen.getByTestId('archive-folder-filter') as HTMLSelectElement;
    const optionValues = Array.from(folderSelect.options).map((opt) => opt.value);
    expect(optionValues).not.toContain('INBOX');
    expect(optionValues).not.toContain('SENT');
    expect(optionValues).not.toContain('ARCHIVE');
    expect(optionValues).toContain('all');
    expect(optionValues).toContain('Faturalar');
    expect(optionValues).toContain('Gelen Kutusu');
    expect(optionValues).toContain('Arsiv-2023');

    // 4. Select a folder specific to arch-3 ('Arsiv-2023')
    fireEvent.change(folderSelect, { target: { value: 'Arsiv-2023' } });
    expect(folderSelect.value).toBe('Arsiv-2023');

    // 5. Deselect arch-3 from scope (now 'Arsiv-2023' is invalid)
    fireEvent.click(screen.getByTestId('checkbox-archive-arch-3'));

    // Folder selection must clear back to 'all' because 'Arsiv-2023' is no longer in scope
    await waitFor(() => {
      expect(folderSelect.value).toBe('all');
    });
  });

  // 18. real-mode footer/labels contain no demo project/FTS5/literal jargon
  it('ensures real-mode labels, placeholders, and footer contain no demo project, FTS5, or literal jargon', async () => {
    const state = createMockAppState();

    const { container } = render(<ArchiveSearchView state={state} client={mockClient} />);

    await screen.findByText('Alpha Holding');

    // Subtitle must describe user-facing search without technical implementation jargon like "FTS5"
    const subtitle = container.querySelector('.page-subtitle');
    expect(subtitle?.textContent).not.toMatch(/FTS5/i);

    // Search input placeholder must use plain search terminology without "literal" jargon
    const searchInput = screen.getByTestId('archive-search-input');
    const placeholder = searchInput.getAttribute('placeholder') || '';
    expect(placeholder).not.toMatch(/literal/i);

    // Labels in real mode should not contain demo simulation references
    expect(screen.queryByText(/Örnek simülasyon/i)).toBeNull();
    expect(screen.queryByText(/Örnek proje/i)).toBeNull();
  });

  // 19. mobile control container semantic/order without layout pixel mocking
  it('preserves mobile control container semantic structure and DOM order without layout pixel mocking', async () => {
    const state = createMockAppState();

    const { container } = render(<ArchiveSearchView state={state} client={mockClient} />);

    await screen.findByText('Alpha Holding');

    // 1. Mobile toggle button semantics and attributes
    const toggleBtn = screen.getByTestId('mobile-location-toggle-btn');
    expect(toggleBtn.tagName.toLowerCase()).toBe('button');
    expect(toggleBtn.getAttribute('aria-expanded')).toBe('true');
    expect(toggleBtn.getAttribute('type')).toBe('button');

    // 2. DOM order: Location panel aside precedes search content grid
    const layout = container.querySelector('.archive-search-layout');
    expect(layout).toBeDefined();
    const children = Array.from(layout!.children);
    const locationAsideIndex = children.findIndex((el) => el.getAttribute('data-testid') === 'search-location-tree-panel');
    const contentGridIndex = children.findIndex((el) => el.classList.contains('search-content-grid'));
    expect(locationAsideIndex).toBeGreaterThanOrEqual(0);
    expect(contentGridIndex).toBeGreaterThan(locationAsideIndex);

    // 3. Inside content grid: results pane precedes preview pane
    const contentGrid = children[contentGridIndex];
    const gridChildren = Array.from(contentGrid.children);
    const resultsPaneIndex = gridChildren.findIndex((el) => el.classList.contains('search-results-pane'));
    const previewPaneIndex = gridChildren.findIndex((el) => el.getAttribute('data-testid') === 'search-preview-pane');
    expect(resultsPaneIndex).toBeGreaterThanOrEqual(0);
    expect(previewPaneIndex).toBeGreaterThan(resultsPaneIndex);

    // 4. Toggle action updates aria-expanded and applies mobile-hidden class
    const locationPanel = screen.getByTestId('search-location-tree-panel');
    fireEvent.click(toggleBtn);
    expect(toggleBtn.getAttribute('aria-expanded')).toBe('false');
    expect(locationPanel.classList.contains('mobile-hidden')).toBe(true);

    fireEvent.click(toggleBtn);
    expect(toggleBtn.getAttribute('aria-expanded')).toBe('true');
    expect(locationPanel.classList.contains('mobile-hidden')).toBe(false);
  });
});
