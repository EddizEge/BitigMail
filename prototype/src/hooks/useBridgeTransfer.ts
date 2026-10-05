import { useState, useEffect, useCallback, useRef } from 'react';
import { localEngineClient, LocalEngineClient } from '../api/localEngineClient';
import {
  ImapAccountPublicDto,
  ImapFolderDto,
  LocalJobRecord,
  ConversionReport,
  BridgeDirection,
  BridgeExportFormat,
  BridgeSourceDescriptorResponse,
  BridgeImportPreviewRequest,
  BridgeImportPreviewResponse,
  BridgeExportPreviewRequest,
  BridgeExportPreviewResponse,
  BridgeStartRequest,
  FilePickResult,
  MailFilterDefinition,
} from '../types/localEngine';

export interface UseBridgeTransferOptions {
  companyName?: string;
  projectName?: string;
  companyId?: string | null;
  projectId?: string | null;
  client?: LocalEngineClient;
  autoFetchAccounts?: boolean;
  initialDirection?: BridgeDirection;
  targetJobId?: string | null;
  onClearTargetJob?: () => void;
}

export function useBridgeTransfer(options: UseBridgeTransferOptions) {
  const {
    companyId,
    projectId,
    companyName,
    projectName,
    client = localEngineClient,
    autoFetchAccounts = true,
    initialDirection = 'file-to-imap',
    targetJobId,
    onClearTargetJob,
  } = options;

  // Active Direction: File -> IMAP (Import) or IMAP -> File (Export)
  const [direction, setDirectionState] = useState<BridgeDirection>(initialDirection);

  // Accounts
  const [accounts, setAccounts] = useState<ImapAccountPublicDto[]>([]);
  const [loadingAccounts, setLoadingAccounts] = useState<boolean>(false);
  const [accountsError, setAccountsError] = useState<string | null>(null);

  // File -> IMAP State
  const [sourceMode, setSourceMode] = useState<'eml-files' | 'eml-tree' | 'mbox'>('eml-tree');
  const [sourceHandle, setSourceHandle] = useState<string>('');
  const [sourceDisplayPath, setSourceDisplayPath] = useState<string>('');
  const [sourceTotalSize, setSourceTotalSize] = useState<number>(0);
  const [sourceDescriptor, setSourceDescriptor] = useState<BridgeSourceDescriptorResponse | null>(null);
  const [descriptorLoading, setDescriptorLoading] = useState<boolean>(false);
  const [descriptorError, setDescriptorError] = useState<string | null>(null);
  const [targetAccountId, setTargetAccountId] = useState<string>('');
  const [selectedSourceFolders, setSelectedSourceFolders] = useState<string[]>([]);
  const [targetFolderMappings, setTargetFolderMappings] = useState<Record<string, string>>({});

  // IMAP -> File State
  const [sourceAccountId, setSourceAccountIdState] = useState<string>('');
  const [sourceFolders, setSourceFolders] = useState<ImapFolderDto[]>([]);
  const [loadingFolders, setLoadingFolders] = useState<boolean>(false);
  const [foldersError, setFoldersError] = useState<string | null>(null);
  const [selectedImapFolders, setSelectedImapFolders] = useState<string[]>([]);
  const [targetDirHandle, setTargetDirHandle] = useState<string>('');
  const [targetDirName, setTargetDirName] = useState<string>('');
  const [targetFormat, setTargetFormat] = useState<BridgeExportFormat>('eml-tree');

  // Shared Date Range Filter
  const [startDate, setStartDate] = useState<string>('');
  const [endDate, setEndDate] = useState<string>('');
  const [advancedFilter, setAdvancedFilter] = useState<MailFilterDefinition | null>(null);

  // Preview State (Immutable server plan response)
  const [preview, setPreview] = useState<BridgeImportPreviewResponse | BridgeExportPreviewResponse | null>(null);
  const [previewLoading, setPreviewLoading] = useState<boolean>(false);
  const [previewError, setPreviewError] = useState<string | null>(null);

  // Job Execution & Polling State
  const [activeJob, setActiveJob] = useState<LocalJobRecord | null>(null);
  const [activeJobLoading, setActiveJobLoading] = useState<boolean>(false);
  const [activeJobError, setActiveJobError] = useState<string | null>(null);

  // Resume State
  const [resumeLoading, setResumeLoading] = useState<boolean>(false);
  const [resumeError, setResumeError] = useState<string | null>(null);

  // Report State
  const [report, setReport] = useState<ConversionReport | null>(null);
  const [reportLoading, setReportLoading] = useState<boolean>(false);
  const [reportError, setReportError] = useState<string | null>(null);

  // References for async isolation & cancellation
  const isMountedRef = useRef<boolean>(true);
  const isRehydratingRef = useRef<boolean>(false);
  const previewSequenceRef = useRef<number>(0);
  const descriptorSequenceRef = useRef<number>(0);
  const previewSelectionRef = useRef<string>('');
  const startKeysRef = useRef(new Map<string, string>());

  const selectionSignature = JSON.stringify([
    direction,
    companyId,
    projectId,
    direction === 'file-to-imap'
      ? [sourceHandle, targetAccountId, selectedSourceFolders, targetFolderMappings]
      : [sourceAccountId, targetDirHandle, targetFormat, selectedImapFolders],
    startDate,
    endDate,
    advancedFilter,
  ]);
  const selectionRef = useRef(selectionSignature);
  selectionRef.current = selectionSignature;

  // Whenever selection parameters change, invalidate preview and discard stale responses (unless rehydrating a job)
  useEffect(() => {
    if (isRehydratingRef.current || targetJobId) return;
    setPreview(null);
    setPreviewError(null);
    setPreviewLoading(false);
  }, [selectionSignature, targetJobId]);

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
    };
  }, []);

  // When company/project scope changes, completely reset state (unless an opened job is active)
  useEffect(() => {
    if (targetJobId || activeJob) return;
    setAccounts([]);
    setAccountsError(null);
    setSourceHandle('');
    setSourceDisplayPath('');
    setSourceTotalSize(0);
    setSourceDescriptor(null);
    setDescriptorError(null);
    setSelectedSourceFolders([]);
    setTargetFolderMappings({});
    setTargetAccountId('');
    setSourceAccountIdState('');
    setSourceFolders([]);
    setSelectedImapFolders([]);
    setTargetDirHandle('');
    setTargetDirName('');
    setPreview(null);
    setPreviewError(null);
    setActiveJob(null);
    setActiveJobError(null);
    setReport(null);
    setReportError(null);
  }, [companyId, projectId, targetJobId, activeJob]);

  // Load registered IMAP accounts for current scope (frozen job context takes precedence)
  const loadAccounts = useCallback(async () => {
    const cId = activeJob?.clientContext?.companyId || companyId;
    const pId = activeJob?.clientContext?.projectId || projectId;
    if (!cId || !pId) {
      setAccounts([]);
      return;
    }

    setLoadingAccounts(true);
    setAccountsError(null);
    try {
      const list = await client.listAccounts(cId, pId);
      if (isMountedRef.current) {
        setAccounts(list);
      }
    } catch (err: any) {
      if (isMountedRef.current) {
        setAccountsError(err?.message || 'Hesaplar yüklenirken bir hata oluştu.');
      }
    } finally {
      if (isMountedRef.current) {
        setLoadingAccounts(false);
      }
    }
  }, [client, companyId, projectId, activeJob?.clientContext?.companyId, activeJob?.clientContext?.projectId]);

  useEffect(() => {
    const cId = activeJob?.clientContext?.companyId || companyId;
    const pId = activeJob?.clientContext?.projectId || projectId;
    if (autoFetchAccounts && cId && pId) {
      loadAccounts();
    }
  }, [autoFetchAccounts, companyId, projectId, activeJob?.clientContext?.companyId, activeJob?.clientContext?.projectId, loadAccounts]);

  // Direction Switcher: cleanly invalidate preview and clear opposite direction handles
  const setDirection = useCallback((newDir: BridgeDirection) => {
    setDirectionState(newDir);
    setPreview(null);
    setPreviewError(null);
    setPreviewLoading(false);
    setActiveJob(null);
    setActiveJobError(null);
    setResumeError(null);
    setReport(null);
    setReportError(null);
    previewSequenceRef.current++;
    descriptorSequenceRef.current++;
    onClearTargetJob?.();
  }, [onClearTargetJob]);

  // Synchronize when initialDirection prop actually changes
  const prevInitialDirectionRef = useRef(initialDirection);
  useEffect(() => {
    if (initialDirection && initialDirection !== prevInitialDirectionRef.current) {
      prevInitialDirectionRef.current = initialDirection;
      if (!targetJobId) {
        setDirection(initialDirection);
      } else {
        setDirectionState(initialDirection);
      }
    }
  }, [initialDirection, setDirection, targetJobId]);

  // Rehydrate a saved job from backend and freeze its context
  const loadJob = useCallback(
    async (jobId: string) => {
      isRehydratingRef.current = true;
      setActiveJobLoading(true);
      setActiveJobError(null);
      try {
        const job = await client.getJob(jobId);
        if (!isMountedRef.current) return null;

        setActiveJob(job);
        const isExport = job.jobKind === 'bridge-export';
        const isImport = job.jobKind === 'bridge-import';
        const newDir: BridgeDirection = isExport ? 'imap-to-file' : 'file-to-imap';
        setDirectionState(newDir);
        prevInitialDirectionRef.current = newDir;

        const compId = job.clientContext?.companyId || companyId || '';
        const projId = job.clientContext?.projectId || projectId || '';

        // Form fields rehydration
        if (isImport) {
          if (job.sourceFileName) setSourceDisplayPath(job.sourceFileName);
          if (job.bridgeTransfer?.targetIdentifier) setTargetAccountId(job.bridgeTransfer.targetIdentifier);
          if (job.bridgeTransfer?.startDate) setStartDate(job.bridgeTransfer.startDate);
          if (job.bridgeTransfer?.endDate) setEndDate(job.bridgeTransfer.endDate);
        } else if (isExport) {
          if (job.bridgeTransfer?.sourceIdentifier) setSourceAccountIdState(job.bridgeTransfer.sourceIdentifier);
          if (job.targetFileName || job.bridgeTransfer?.outputPath) {
            setTargetDirName(job.targetFileName || job.bridgeTransfer?.outputPath || '');
          }
          if (job.bridgeTransfer?.startDate) setStartDate(job.bridgeTransfer.startDate);
          if (job.bridgeTransfer?.endDate) setEndDate(job.bridgeTransfer.endDate);
        }

        // Try loading persisted preview if planId is present
        const planId = job.bridgeTransfer?.planId || job.selectionId;
        if (planId && compId && projId) {
          try {
            const prev = isExport
              ? await client.getBridgeExportPreview(planId, compId, projId)
              : await client.getBridgeImportPreview(planId, compId, projId);
            if (isMountedRef.current) {
              setPreview(prev);
            }
          } catch {
            // fallback to building preview from bridgeTransfer report detail if available
            if (isMountedRef.current && job.bridgeTransfer) {
              const bt = job.bridgeTransfer;
              if (isExport) {
                setPreview({
                  previewId: bt.planId || planId,
                  companyId: compId,
                  projectId: projId,
                  sourceAccountId: bt.sourceIdentifier,
                  sourceAccountVersion: 1,
                  targetDirHandle: bt.targetIdentifier,
                  targetFormat: bt.targetFormat || 'eml-tree',
                  totalSourceItems: bt.totalPlanned,
                  eligibleItemsCount: bt.totalPlanned,
                  excludedCount: 0,
                  missingDateExcludedCount: 0,
                  deletedExcludedCount: 0,
                  folders: bt.folders || [],
                  createdAtUtc: job.createdAt,
                  canTransfer: true,
                });
              } else {
                setPreview({
                  previewId: bt.planId || planId,
                  companyId: compId,
                  projectId: projId,
                  sourceHandle: bt.sourceIdentifier,
                  sourceFingerprint: '',
                  sourceKind: '',
                  targetAccountId: bt.targetIdentifier,
                  targetAccountVersion: 1,
                  totalSourceItems: bt.totalPlanned,
                  eligibleItemsCount: bt.totalPlanned,
                  excludedCount: 0,
                  missingDateExcludedCount: 0,
                  folders: bt.folders || [],
                  createdAtUtc: job.createdAt,
                  canTransfer: true,
                });
              }
            }
          }
        } else if (job.bridgeTransfer) {
          const bt = job.bridgeTransfer;
          if (isExport) {
            setPreview({
              previewId: bt.planId || `preview-${job.jobId}`,
              companyId: compId,
              projectId: projId,
              sourceAccountId: bt.sourceIdentifier,
              sourceAccountVersion: 1,
              targetDirHandle: bt.targetIdentifier,
              targetFormat: bt.targetFormat || 'eml-tree',
              totalSourceItems: bt.totalPlanned,
              eligibleItemsCount: bt.totalPlanned,
              excludedCount: 0,
              missingDateExcludedCount: 0,
              deletedExcludedCount: 0,
              folders: bt.folders || [],
              createdAtUtc: job.createdAt,
              canTransfer: true,
            });
          } else {
            setPreview({
              previewId: bt.planId || `preview-${job.jobId}`,
              companyId: compId,
              projectId: projId,
              sourceHandle: bt.sourceIdentifier,
              sourceFingerprint: '',
              sourceKind: '',
              targetAccountId: bt.targetIdentifier,
              targetAccountVersion: 1,
              totalSourceItems: bt.totalPlanned,
              eligibleItemsCount: bt.totalPlanned,
              excludedCount: 0,
              missingDateExcludedCount: 0,
              folders: bt.folders || [],
              createdAtUtc: job.createdAt,
              canTransfer: true,
            });
          }
        }

        // If job completed, load report
        if (job.status === 'completed') {
          try {
            const rep = await client.getJobReport(job.jobId);
            if (isMountedRef.current) {
              setReport(rep);
            }
          } catch {
            // report fetch err
          }
        }
        return job;
      } catch (err: any) {
        if (isMountedRef.current) {
          setActiveJobError(err?.message || 'İş kaydı yüklenemedi.');
        }
        return null;
      } finally {
        isRehydratingRef.current = false;
        if (isMountedRef.current) {
          setActiveJobLoading(false);
        }
      }
    },
    [client, companyId, projectId]
  );

  useEffect(() => {
    if (targetJobId) {
      loadJob(targetJobId);
    }
  }, [targetJobId, loadJob]);

  // Vendor-Free MIME Source Discovery
  const describeMimeSource = useCallback(
    async (handle: string): Promise<BridgeSourceDescriptorResponse | null> => {
      if (!handle || !handle.startsWith('msrc_')) {
        setSourceDescriptor(null);
        return null;
      }

      setDescriptorLoading(true);
      setDescriptorError(null);
      const seq = ++descriptorSequenceRef.current;

      try {
        const desc = await client.describeMimeSource(handle);
        if (isMountedRef.current && descriptorSequenceRef.current === seq) {
          setSourceHandle(handle);
          if (desc.displayPath) {
            setSourceDisplayPath(desc.displayPath);
          }
          if (desc.totalSizeBytes) {
            setSourceTotalSize(desc.totalSizeBytes);
          }
          setSourceDescriptor(desc);
          const allFolderNames = desc.folders.map((f) => f.folderName);
          setSelectedSourceFolders(allFolderNames);
          const initialMappings: Record<string, string> = {};
          allFolderNames.forEach((name) => {
            initialMappings[name] = name;
          });
          setTargetFolderMappings(initialMappings);
        }
        return desc;
      } catch (err: any) {
        if (isMountedRef.current && descriptorSequenceRef.current === seq) {
          setDescriptorError(err?.message || 'Kaynak klasörleri incelenirken bir hata oluştu.');
          setSourceDescriptor(null);
        }
        return null;
      } finally {
        if (isMountedRef.current && descriptorSequenceRef.current === seq) {
          setDescriptorLoading(false);
        }
      }
    },
    [client]
  );

  // Native MIME picker trigger
  const pickMimeSource = useCallback(
    async (mode?: 'eml-files' | 'eml-tree' | 'mbox'): Promise<FilePickResult> => {
      const activeMode = mode || sourceMode;
      const res = await client.pickMimeSource(activeMode);
      if (res && !res.cancelled && res.handle) {
        setSourceHandle(res.handle);
        setSourceDisplayPath(res.fileName || res.displayPath || res.handle);
        setSourceTotalSize(res.sizeBytes || 0);
        await describeMimeSource(res.handle);
      }
      return res;
    },
    [client, sourceMode, describeMimeSource]
  );

  // Toggle/Select folders in File -> IMAP
  const toggleSourceFolder = useCallback((folderName: string) => {
    setSelectedSourceFolders((prev) => {
      const exists = prev.includes(folderName);
      const next = exists ? prev.filter((f) => f !== folderName) : [...prev, folderName];
      setTargetFolderMappings((m) => {
        if (!exists && !m[folderName]) {
          return { ...m, [folderName]: folderName };
        }
        return m;
      });
      return next;
    });
    setPreview(null);
  }, []);

  const selectAllSourceFolders = useCallback(() => {
    if (!sourceDescriptor) return;
    const all = sourceDescriptor.folders.map((f) => f.folderName);
    setSelectedSourceFolders(all);
    setTargetFolderMappings((prev) => {
      const next = { ...prev };
      all.forEach((name) => {
        if (!next[name]) next[name] = name;
      });
      return next;
    });
    setPreview(null);
  }, [sourceDescriptor]);

  const deselectAllSourceFolders = useCallback(() => {
    setSelectedSourceFolders([]);
    setPreview(null);
  }, []);

  const setFolderMapping = useCallback((sourceFolder: string, targetFolder: string) => {
    setTargetFolderMappings((prev) => ({
      ...prev,
      [sourceFolder]: targetFolder,
    }));
    setPreview(null);
  }, []);

  // IMAP Account selection for export (source)
  const setSourceAccountId = useCallback(
    async (accId: string) => {
      setSourceAccountIdState(accId);
      setSelectedImapFolders([]);
      setSourceFolders([]);
      setFoldersError(null);
      setPreview(null);

      if (!accId || !companyId || !projectId) return;

      setLoadingFolders(true);
      try {
        const res = await client.listAccountFolders(accId, companyId, projectId);
        if (isMountedRef.current) {
          setSourceFolders(res.folders);
          const selectable = res.folders.filter((f) => f.isSelectable).map((f) => f.fullPath);
          setSelectedImapFolders(selectable);
        }
      } catch (err: any) {
        if (isMountedRef.current) {
          setFoldersError(err?.message || 'Hesap klasörleri alınamadı.');
        }
      } finally {
        if (isMountedRef.current) {
          setLoadingFolders(false);
        }
      }
    },
    [client, companyId, projectId]
  );

  // Toggle folders in IMAP -> File
  const toggleImapFolder = useCallback((folderPath: string) => {
    setSelectedImapFolders((prev) => {
      return prev.includes(folderPath) ? prev.filter((p) => p !== folderPath) : [...prev, folderPath];
    });
    setPreview(null);
  }, []);

  const selectAllImapFolders = useCallback(() => {
    const selectable = sourceFolders.filter((f) => f.isSelectable).map((f) => f.fullPath);
    setSelectedImapFolders(selectable);
    setPreview(null);
  }, [sourceFolders]);

  const deselectAllImapFolders = useCallback(() => {
    setSelectedImapFolders([]);
    setPreview(null);
  }, []);

  // Native Output Directory picker for export target
  const pickOutputDir = useCallback(async (): Promise<FilePickResult> => {
    const res = await client.pickOutputDir();
    if (res && !res.cancelled && res.handle) {
      setTargetDirHandle(res.handle);
      setTargetDirName(res.fileName || res.displayPath || res.handle);
      setPreview(null);
    }
    return res;
  }, [client]);

  // Create immutable preview for active direction
  const createPreview = useCallback(async () => {
    if (!companyId || !projectId) {
      throw new Error('Müşteri ve proje seçimi zorunludur.');
    }

    if (startDate && endDate && startDate > endDate) {
      throw new Error('Tarih filtresi başlangıcı bitişinden büyük olamaz.');
    }

    setPreviewLoading(true);
    setPreviewError(null);
    const capturedSelection = selectionRef.current;
    const seq = ++previewSequenceRef.current;

    try {
      if (direction === 'file-to-imap') {
        if (!sourceHandle) {
          throw new Error('En az bir kaynak dosya seçilmelidir.');
        }
        if (!targetAccountId) {
          throw new Error('Hedef IMAP hesabı zorunludur.');
        }
        if (selectedSourceFolders.length === 0) {
          throw new Error('En az bir klasör seçilmelidir.');
        }

        const req: BridgeImportPreviewRequest = {
          companyId,
          projectId,
          ...(companyName ? { companyName } : {}),
          ...(projectName ? { projectName } : {}),
          sourceHandle,
          targetAccountId,
          selectedFolders: selectedSourceFolders,
          targetFolderMappings,
          startDate: startDate.trim() || null,
          endDate: endDate.trim() || null,
          ...(advancedFilter ? { advancedFilter } : {}),
        };

        const res = await client.createBridgeImportPreview(req);
        if (isMountedRef.current && selectionRef.current === capturedSelection && previewSequenceRef.current === seq) {
          previewSelectionRef.current = capturedSelection;
          setPreview(res);
        }
        return res;
      } else {
        if (!sourceAccountId) {
          throw new Error('Kaynak IMAP hesabı zorunludur.');
        }
        if (!targetDirHandle) {
          throw new Error('Hedef klasör seçilmelidir.');
        }
        if (selectedImapFolders.length === 0) {
          throw new Error('En az bir klasör seçilmelidir.');
        }

        const req: BridgeExportPreviewRequest = {
          companyId,
          projectId,
          ...(companyName ? { companyName } : {}),
          ...(projectName ? { projectName } : {}),
          sourceAccountId,
          targetDirHandle,
          targetFormat,
          selectedFolders: selectedImapFolders,
          startDate: startDate.trim() || null,
          endDate: endDate.trim() || null,
          ...(advancedFilter ? { advancedFilter } : {}),
        };

        const res = await client.createBridgeExportPreview(req);
        if (isMountedRef.current && selectionRef.current === capturedSelection && previewSequenceRef.current === seq) {
          previewSelectionRef.current = capturedSelection;
          setPreview(res);
        }
        return res;
      }
    } catch (err: any) {
      const msg = err?.message || 'Önizleme oluşturulamadı.';
      if (isMountedRef.current && selectionRef.current === capturedSelection && previewSequenceRef.current === seq) {
        setPreviewError(msg);
      }
      throw err;
    } finally {
      if (isMountedRef.current && previewSequenceRef.current === seq) {
        setPreviewLoading(false);
      }
    }
  }, [
    client,
    direction,
    companyId,
    projectId,
    companyName,
    projectName,
    sourceHandle,
    targetAccountId,
    selectedSourceFolders,
    targetFolderMappings,
    sourceAccountId,
    targetDirHandle,
    targetFormat,
    selectedImapFolders,
    startDate,
    endDate,
    advancedFilter,
  ]);

  // Start bridge job
  const startTransfer = useCallback(async (): Promise<LocalJobRecord> => {
    if (!preview || previewSelectionRef.current !== selectionRef.current) {
      throw new Error('Aktarımı başlatmadan önce geçerli bir sunucu önizlemesi alınmalıdır.');
    }
    if (!preview.canTransfer) {
      throw new Error(preview.blockerReason || 'Önizlemedeki engeller nedeniyle aktarım başlatılamaz.');
    }

    setActiveJobLoading(true);
    setActiveJobError(null);

    const idempotencyKey =
      startKeysRef.current.get(preview.previewId) || `bridge-${direction}-${preview.previewId}-${crypto.randomUUID()}`;
    startKeysRef.current.set(preview.previewId, idempotencyKey);

    const req: BridgeStartRequest = {
      previewId: preview.previewId,
      idempotencyKey,
      companyId: companyId || undefined,
      projectId: projectId || undefined,
      enqueueIfBusy: true,
    };

    try {
      const job =
        direction === 'file-to-imap'
          ? await client.startBridgeImport(req)
          : await client.startBridgeExport(req);

      if (isMountedRef.current) {
        setActiveJob(job);
      }
      return job;
    } catch (err: any) {
      const msg = err?.message || 'Aktarım başlatılamadı.';
      if (isMountedRef.current) {
        setActiveJobError(msg);
      }
      throw err;
    } finally {
      if (isMountedRef.current) {
        setActiveJobLoading(false);
      }
    }
  }, [client, preview, direction, companyId, projectId]);

  // Resume bridge job
  const resumeTransfer = useCallback(
    async (jobId?: string): Promise<LocalJobRecord> => {
      const targetResumeJobId = jobId || activeJob?.jobId;
      const compId = activeJob?.clientContext?.companyId || companyId;
      const projId = activeJob?.clientContext?.projectId || projectId;
      if (!targetResumeJobId || !compId || !projId) {
        throw new Error('Devam ettirilecek geçerli bir iş numarası ve müşteri/proje kapsamı bulunamadı.');
      }

      setResumeLoading(true);
      setResumeError(null);

      try {
        const isImport = activeJob?.jobKind ? activeJob.jobKind === 'bridge-import' : direction === 'file-to-imap';
        const job =
          isImport
            ? await client.resumeBridgeImport(targetResumeJobId, compId, projId, true)
            : await client.resumeBridgeExport(targetResumeJobId, compId, projId, true);

        if (isMountedRef.current) {
          setActiveJob(job);
        }
        return job;
      } catch (err: any) {
        const msg = err?.message || 'Aktarım devam ettirilemedi.';
        if (isMountedRef.current) {
          setResumeError(msg);
        }
        throw err;
      } finally {
        if (isMountedRef.current) {
          setResumeLoading(false);
        }
      }
    },
    [client, activeJob, direction, companyId, projectId]
  );

  // Load report for completed job
  const loadReport = useCallback(
    async (jobId: string): Promise<ConversionReport> => {
      setReportLoading(true);
      setReportError(null);
      try {
        const rep = await client.getJobReport(jobId);
        if (isMountedRef.current) {
          setReport(rep);
        }
        return rep;
      } catch (err: any) {
        const msg = err?.message || 'Rapor alınamadı.';
        if (isMountedRef.current) {
          setReportError(msg);
        }
        throw err;
      } finally {
        if (isMountedRef.current) {
          setReportLoading(false);
        }
      }
    },
    [client]
  );

  // Auto-poll active job progress
  useEffect(() => {
    if (!activeJob?.jobId) return;
    if (activeJob.status === 'completed' || activeJob.status === 'failed' || activeJob.status === 'interrupted') {
      return;
    }

    const interval = setInterval(async () => {
      try {
        const updated = await client.getJob(activeJob.jobId);
        if (isMountedRef.current) {
          setActiveJob(updated);
        }
      } catch {
        // Polling error silently handled
      }
    }, 1500);

    return () => clearInterval(interval);
  }, [client, activeJob?.jobId, activeJob?.status]);

  return {
    direction,
    setDirection,
    accounts,
    loadingAccounts,
    accountsError,
    loadAccounts,

    // File -> IMAP
    sourceMode,
    setSourceMode,
    sourceHandle,
    sourceDisplayPath,
    sourceTotalSize,
    sourceDescriptor,
    descriptorLoading,
    descriptorError,
    describeMimeSource,
    pickMimeSource,
    targetAccountId,
    setTargetAccountId,
    selectedSourceFolders,
    targetFolderMappings,
    toggleSourceFolder,
    selectAllSourceFolders,
    deselectAllSourceFolders,
    setFolderMapping,

    // IMAP -> File
    sourceAccountId,
    setSourceAccountId,
    sourceFolders,
    loadingFolders,
    foldersError,
    selectedImapFolders,
    toggleImapFolder,
    selectAllImapFolders,
    deselectAllImapFolders,
    targetDirHandle,
    targetDirName,
    pickOutputDir,
    targetFormat,
    setTargetFormat,

    // Filter
    startDate,
    endDate,
    setStartDate,
    setEndDate,
    advancedFilter,
    setAdvancedFilter,

    // Preview
    preview,
    previewLoading,
    previewError,
    createPreview,

    // Run & Resume
    activeJob,
    activeJobLoading,
    activeJobError,
    startTransfer,
    resumeTransfer,
    resumeLoading,
    resumeError,
    loadJob,

    // Report
    report,
    reportLoading,
    reportError,
    loadReport,
  };
}
