import { useState, useEffect, useCallback, useRef } from 'react';
import { localEngineClient, LocalEngineClient } from '../api/localEngineClient';
import {
  ImapAccountPublicDto,
  ImapFolderDto,
  ImapTransferPreviewRequest,
  ImapTransferPreviewResponse,
  ImapTransferStartRequest,
  LocalJobRecord,
  ConversionReport,
  MailFilterDefinition,
} from '../types/localEngine';

export interface UseImapTransferOptions {
  companyName?: string;
  projectName?: string;
  companyId?: string | null;
  projectId?: string | null;
  client?: LocalEngineClient;
  autoFetchAccounts?: boolean;
}

export function useImapTransfer(options: UseImapTransferOptions) {
  const { companyId, projectId, companyName, projectName, client = localEngineClient, autoFetchAccounts = true } = options;

  const [accounts, setAccounts] = useState<ImapAccountPublicDto[]>([]);
  const [loadingAccounts, setLoadingAccounts] = useState<boolean>(false);
  const [accountsError, setAccountsError] = useState<string | null>(null);

  const [sourceAccountId, setSourceAccountId] = useState<string>('');
  const [targetAccountId, setTargetAccountId] = useState<string>('');

  const [sourceFolders, setSourceFolders] = useState<ImapFolderDto[]>([]);
  const [loadingFolders, setLoadingFolders] = useState<boolean>(false);
  const [foldersError, setFoldersError] = useState<string | null>(null);

  const [selectedFolderPaths, setSelectedFolderPaths] = useState<string[]>([]);
  const [folderMappings, setFolderMappings] = useState<Record<string, string>>({});

  const [startDate, setStartDate] = useState<string>('');
  const [endDate, setEndDate] = useState<string>('');
  const [advancedFilter, setAdvancedFilter] = useState<MailFilterDefinition | null>(null);

  const [preview, setPreview] = useState<ImapTransferPreviewResponse | null>(null);
  const [previewLoading, setPreviewLoading] = useState<boolean>(false);
  const [previewError, setPreviewError] = useState<string | null>(null);

  const [activeJob, setActiveJob] = useState<LocalJobRecord | null>(null);
  const [activeJobLoading, setActiveJobLoading] = useState<boolean>(false);
  const [activeJobError, setActiveJobError] = useState<string | null>(null);

  const [resumeLoading, setResumeLoading] = useState<boolean>(false);
  const [resumeError, setResumeError] = useState<string | null>(null);

  const [report, setReport] = useState<ConversionReport | null>(null);
  const [reportLoading, setReportLoading] = useState<boolean>(false);
  const [reportError, setReportError] = useState<string | null>(null);

  const isMountedRef = useRef<boolean>(true);
  const generationRef = useRef<number>(0);
  const startKeysRef = useRef(new Map<string, string>());
  const selectionSignature = JSON.stringify([companyId, projectId, sourceAccountId, targetAccountId, selectedFolderPaths, folderMappings, startDate, endDate, advancedFilter]);
  const selectionRef = useRef(selectionSignature);
  selectionRef.current = selectionSignature;
  const previewSelectionRef = useRef('');
  const previewSequenceRef = useRef(0);
  useEffect(() => { setPreview(null); setPreviewError(null); setPreviewLoading(false); }, [selectionSignature]);
  const activeScopeRef = useRef<{ companyId?: string | null; projectId?: string | null }>({
    companyId,
    projectId,
  });

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
    };
  }, []);

  // When customer/project scope changes, guard against stale in-flight results
  useEffect(() => {
    activeScopeRef.current = { companyId, projectId };
    generationRef.current += 1;

    setAccounts([]);
    setAccountsError(null);
    setSourceAccountId('');
    setTargetAccountId('');
    setSourceFolders([]);
    setFoldersError(null);
    setSelectedFolderPaths([]);
    setFolderMappings({});
    setPreview(null);
    setPreviewError(null);
    setActiveJob(null);
    setActiveJobError(null);
    setReport(null);
    setReportError(null);
  }, [companyId, projectId]);

  const loadAccounts = useCallback(async () => {
    const capturedScope = { companyId, projectId };
    const currentGen = ++generationRef.current;

    if (!companyId || !projectId) {
      setAccounts([]);
      setLoadingAccounts(false);
      return;
    }

    setLoadingAccounts(true);
    setAccountsError(null);
    try {
      const data = await client.listAccounts(companyId, projectId);
      if (
        isMountedRef.current &&
        generationRef.current === currentGen &&
        activeScopeRef.current.companyId === capturedScope.companyId &&
        activeScopeRef.current.projectId === capturedScope.projectId
      ) {
        setAccounts(data);
      }
    } catch (err: any) {
      if (
        isMountedRef.current &&
        generationRef.current === currentGen &&
        activeScopeRef.current.companyId === capturedScope.companyId &&
        activeScopeRef.current.projectId === capturedScope.projectId
      ) {
        setAccountsError(err?.message || 'Hesaplar yüklenemedi.');
      }
    } finally {
      if (isMountedRef.current && generationRef.current === currentGen) {
        setLoadingAccounts(false);
      }
    }
  }, [client, companyId, projectId]);

  useEffect(() => {
    if (autoFetchAccounts && companyId && projectId) {
      loadAccounts();
    }
  }, [autoFetchAccounts, companyId, projectId, loadAccounts]);

  const loadSourceFolders = useCallback(
    async (accId: string) => {
      if (!accId || !companyId || !projectId) {
        setSourceFolders([]);
        return;
      }

      const capturedScope = { companyId, projectId };
      const currentGen = ++generationRef.current;

      setLoadingFolders(true);
      setFoldersError(null);
      try {
        const res = await client.listAccountFolders(accId, companyId, projectId);
        if (
          isMountedRef.current &&
          generationRef.current === currentGen &&
          activeScopeRef.current.companyId === capturedScope.companyId &&
          activeScopeRef.current.projectId === capturedScope.projectId
        ) {
          setSourceFolders(res.folders || []);
          // Pre-select selectable folders by default
          const selectable = (res.folders || []).filter((f) => f.isSelectable).map((f) => f.fullPath);
          setSelectedFolderPaths(selectable);
          const initialMap: Record<string, string> = {};
          selectable.forEach((p) => {
            initialMap[p] = p;
          });
          setFolderMappings(initialMap);
        }
      } catch (err: any) {
        if (
          isMountedRef.current &&
          generationRef.current === currentGen &&
          activeScopeRef.current.companyId === capturedScope.companyId &&
          activeScopeRef.current.projectId === capturedScope.projectId
        ) {
          setFoldersError(err?.message || 'Klasörler yüklenemedi.');
          setSourceFolders([]);
          setSelectedFolderPaths([]);
        }
      } finally {
        if (isMountedRef.current && generationRef.current === currentGen) {
          setLoadingFolders(false);
        }
      }
    },
    [client, companyId, projectId]
  );

  const handleSetSourceAccountId = useCallback(
    (id: string) => {
      setSourceAccountId(id);
      setPreview(null);
      setPreviewError(null);
      if (id) {
        loadSourceFolders(id);
      } else {
        setSourceFolders([]);
        setSelectedFolderPaths([]);
        setFolderMappings({});
      }
    },
    [loadSourceFolders]
  );

  const handleSetTargetAccountId = useCallback((id: string) => {
    setTargetAccountId(id);
    setPreview(null);
    setPreviewError(null);
  }, []);

  const toggleFolderSelection = useCallback((path: string) => {
    setSelectedFolderPaths((prev) => {
      const exists = prev.includes(path);
      const next = exists ? prev.filter((p) => p !== path) : [...prev, path];
      setFolderMappings((m) => {
        if (!exists && !m[path]) {
          return { ...m, [path]: path };
        }
        return m;
      });
      return next;
    });
    setPreview(null);
  }, []);

  const selectAllFolders = useCallback(() => {
    const selectable = sourceFolders.filter((f) => f.isSelectable).map((f) => f.fullPath);
    setSelectedFolderPaths(selectable);
    setFolderMappings((prev) => {
      const next = { ...prev };
      selectable.forEach((p) => {
        if (!next[p]) next[p] = p;
      });
      return next;
    });
    setPreview(null);
  }, [sourceFolders]);

  const deselectAllFolders = useCallback(() => {
    setSelectedFolderPaths([]);
    setPreview(null);
  }, []);

  const setFolderMapping = useCallback((sourcePath: string, targetPath: string) => {
    setFolderMappings((prev) => ({
      ...prev,
      [sourcePath]: targetPath,
    }));
    setPreview(null);
  }, []);

  const createPreview = useCallback(async (): Promise<ImapTransferPreviewResponse> => {
    if (!companyId || !projectId) {
      throw new Error('Müşteri ve proje seçimi zorunludur.');
    }
    if (!sourceAccountId || !targetAccountId) {
      throw new Error('Kaynak ve hedef IMAP hesapları seçilmelidir.');
    }
    if (sourceAccountId === targetAccountId) {
      throw new Error('Kaynak ve hedef hesap aynı olamaz. Lütfen farklı bir hedef hesap seçin.');
    }
    if (selectedFolderPaths.length === 0) {
      throw new Error('En az bir klasör seçilmelidir. Boş klasör listesi ile önizleme oluşturulamaz.');
    }

    setPreviewLoading(true);
    setPreviewError(null);
    const capturedSelection = selectionRef.current;
    const sequence = ++previewSequenceRef.current;

    const req: ImapTransferPreviewRequest = {
      companyId,
      projectId,
      ...(companyName ? { companyName } : {}),
      ...(projectName ? { projectName } : {}),
      sourceAccountId,
      targetAccountId,
      selectedFolders: selectedFolderPaths.map((p) => ({
        sourceFolderPath: p,
        targetFolderPath: (folderMappings[p] || p).trim() || p,
      })),
      startDate: startDate.trim() || null,
      endDate: endDate.trim() || null,
      ...(advancedFilter ? { advancedFilter } : {}),
    };

    try {
      const res = await client.createImapTransferPreview(req);
      if (isMountedRef.current && selectionRef.current === capturedSelection && previewSequenceRef.current === sequence) {
        previewSelectionRef.current = capturedSelection;
        setPreview(res);
      }
      return res;
    } catch (err: any) {
      const msg = err?.message || 'Önizleme oluşturulamadı.';
      if (isMountedRef.current && selectionRef.current === capturedSelection && previewSequenceRef.current === sequence) {
        setPreviewError(msg);
      }
      throw err;
    } finally {
      if (isMountedRef.current && previewSequenceRef.current === sequence) {
        setPreviewLoading(false);
      }
    }
  }, [client, companyId, projectId, companyName, projectName, sourceAccountId, targetAccountId, selectedFolderPaths, folderMappings, startDate, endDate, advancedFilter]);

  const startTransfer = useCallback(async (): Promise<LocalJobRecord> => {
    if (!preview || previewSelectionRef.current !== selectionRef.current) {
      throw new Error('Aktarımı başlatmadan önce geçerli bir sunucu önizlemesi alınmalıdır.');
    }
    if (!preview.canTransfer) {
      throw new Error(preview.blockerReason || 'Önizlemedeki engeller nedeniyle aktarım başlatılamaz.');
    }

    setActiveJobLoading(true);
    setActiveJobError(null);

    // Client sends strictly previewId and a fresh idempotencyKey.
    // Client does NOT trust or send message counts, UIDs, or host names.
    const idempotencyKey = startKeysRef.current.get(preview.previewId) || `imap-xfer-${preview.previewId}-${crypto.randomUUID()}`;
    startKeysRef.current.set(preview.previewId, idempotencyKey);
    const req: ImapTransferStartRequest = {
      previewId: preview.previewId,
      idempotencyKey,
      enqueueIfBusy: true,
    };

    try {
      const job = await client.startImapTransfer(req);
      if (isMountedRef.current && activeScopeRef.current.companyId === job.clientContext.companyId && activeScopeRef.current.projectId === job.clientContext.projectId) {
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
  }, [client, preview, companyId, projectId]);

  const resumeTransfer = useCallback(
    async (jobId?: string): Promise<LocalJobRecord> => {
      const targetJobId = jobId || activeJob?.jobId;
      if (!targetJobId || !companyId || !projectId) {
        throw new Error('Devam ettirilecek geçerli bir iş numarası ve kapsam bulunamadı.');
      }

      setResumeLoading(true);
      setResumeError(null);
      try {
        const job = await client.resumeImapTransfer(targetJobId, companyId, projectId, true);
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
    [client, activeJob, companyId, projectId]
  );

  const loadReport = useCallback(
    async (jobId: string): Promise<ConversionReport> => {
      setReportLoading(true);
      setReportError(null);
      try {
        const rep = await client.getJobReport(jobId);
        if (isMountedRef.current && activeScopeRef.current.companyId === rep.clientContext.companyId && activeScopeRef.current.projectId === rep.clientContext.projectId) {
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

  const pollJobStatus = useCallback(
    async (jobId: string) => {
      try {
        const job = await client.getJob(jobId);
        if (isMountedRef.current && activeScopeRef.current.companyId === job.clientContext.companyId && activeScopeRef.current.projectId === job.clientContext.projectId) {
          setActiveJob(job);
          if (job.status === 'completed' || job.status === 'interrupted' || job.status === 'failed') {
            loadReport(jobId).catch(() => {});
          }
        }
      } catch {
        // ignore polling network blips
      }
    },
    [client, loadReport]
  );

  // Poll active job while running
  useEffect(() => {
    if (!activeJob) return;
    const isRunning = activeJob.status === 'queued' || activeJob.status === 'converting' || activeJob.status === 'verifying';
    if (!isRunning) return;

    const interval = setInterval(() => {
      pollJobStatus(activeJob.jobId);
    }, 1500);

    return () => clearInterval(interval);
  }, [activeJob, pollJobStatus]);

  const resetTransfer = useCallback(() => {
    setPreview(null);
    setPreviewError(null);
    setActiveJob(null);
    setActiveJobError(null);
    setReport(null);
    setReportError(null);
  }, []);

  return {
    accounts,
    loadingAccounts,
    accountsError,
    sourceAccountId,
    targetAccountId,
    sourceFolders,
    loadingFolders,
    foldersError,
    selectedFolderPaths,
    folderMappings,
    startDate,
    endDate,
    advancedFilter,
    preview,
    previewLoading,
    previewError,
    activeJob,
    activeJobLoading,
    activeJobError,
    resumeLoading,
    resumeError,
    report,
    reportLoading,
    reportError,
    loadAccounts,
    setSourceAccountId: handleSetSourceAccountId,
    setTargetAccountId: handleSetTargetAccountId,
    toggleFolderSelection,
    selectAllFolders,
    deselectAllFolders,
    setFolderMapping,
    setStartDate,
    setEndDate,
    setAdvancedFilter,
    createPreview,
    startTransfer,
    resumeTransfer,
    loadReport,
    resetTransfer,
  };
}
