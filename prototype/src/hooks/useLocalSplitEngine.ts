import { useState, useEffect, useCallback, useRef, SetStateAction } from 'react';
import { localEngineClient, LocalEngineClient } from '../api/localEngineClient';
import {
  ClientProjectContext,
  ConversionReport,
  FilePickResult,
  LocalJobRecord,
  OstAnalysisResult,
  SelectionPreviewResult,
  SplitPlanResult,
} from '../types/localEngine';

export interface UseLocalSplitEngineOptions {
  client?: LocalEngineClient;
  autoCheck?: boolean;
  targetJobId?: string | null;
}

export function useLocalSplitEngine(options?: UseLocalSplitEngineOptions) {
  const client = options?.client || localEngineClient;

  const [serviceStatus, setServiceStatus] = useState<'checking' | 'online' | 'offline'>('checking');
  const [selectedSource, setSelectedSource] = useState<{ handle: string; fileName: string; sizeBytes: number; displayPath?: string | null } | null>(null);
  const [selectedOutputDir, setSelectedOutputDir] = useState<{ handle: string; fileName: string; displayPath?: string | null } | null>(null);
  const [analysis, setAnalysis] = useState<OstAnalysisResult | null>(null);
  const [isAnalyzing, setIsAnalyzing] = useState<boolean>(false);
  const [analysisError, setAnalysisError] = useState<string | null>(null);

  // Selection filter state (adapted from TASK-011)
  const [selectedFolderIds, setSelectedFolderIdsState] = useState<string[]>([]);
  const [startDate, setStartDateState] = useState<string>('');
  const [endDate, setEndDateState] = useState<string>('');
  const [selectionPreview, setSelectionPreview] = useState<SelectionPreviewResult | null>(null);
  const [isLoadingPreview, setIsLoadingPreview] = useState<boolean>(false);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const previewRequestIdRef = useRef<number>(0);

  // Split options state (TASK-012)
  const [splitMode, setSplitModeState] = useState<'year' | 'size'>('year');
  const [sizeCapMb, setSizeCapMbState] = useState<number>(2048);
  const [splitPlan, setSplitPlan] = useState<SplitPlanResult | null>(null);
  const [isLoadingPlan, setIsLoadingPlan] = useState<boolean>(false);
  const [planError, setPlanError] = useState<string | null>(null);
  const [stableIdempotencyKey, setStableIdempotencyKey] = useState<string | null>(null);
  const planRequestIdRef = useRef<number>(0);

  // Active Job & Reports state
  const [activeJob, setActiveJob] = useState<LocalJobRecord | null>(null);
  const [isStarting, setIsStarting] = useState<boolean>(false);
  const [jobError, setJobError] = useState<string | null>(null);
  const [jobReport, setJobReport] = useState<ConversionReport | null>(null);
  const [localJobs, setLocalJobs] = useState<LocalJobRecord[]>([]);

  const workflowEpochRef = useRef<number>(0);
  const pollTimerRef = useRef<any>(null);
  const isMountedRef = useRef<boolean>(true);
  const hasRestoredRef = useRef<boolean>(false);
  const userChosenSourceRef = useRef<boolean>(false);
  const selectedSourceRef = useRef(selectedSource);

  useEffect(() => {
    selectedSourceRef.current = selectedSource;
  }, [selectedSource]);

  // Polling routine for active job
  const pollJobStatus = useCallback(
    async (jobId: string, generationEpoch?: number) => {
      const epoch = generationEpoch !== undefined ? generationEpoch : workflowEpochRef.current;
      try {
        const job = await client.getJob(jobId);
        if (!isMountedRef.current || epoch !== workflowEpochRef.current) return;

        setActiveJob(job);

        if (job.status === 'completed') {
          try {
            const report = await client.getJobReport(jobId);
            if (!isMountedRef.current || epoch !== workflowEpochRef.current) return;
            setJobReport(report);
          } catch {
            // report fetch err
          }
          const all = await client.getJobsPage({ pageSize: 100, jobKind: 'split' }).then(page => page.items).catch(() => []);
          if (!isMountedRef.current || epoch !== workflowEpochRef.current) return;
          setLocalJobs(all);
        } else if (job.status === 'failed' || job.status === 'interrupted') {
          const all = await client.getJobsPage({ pageSize: 100, jobKind: 'split' }).then(page => page.items).catch(() => []);
          if (!isMountedRef.current || epoch !== workflowEpochRef.current) return;
          setLocalJobs(all);
        } else {
          pollTimerRef.current = setTimeout(() => {
            if (isMountedRef.current && epoch === workflowEpochRef.current) {
              pollJobStatus(jobId, epoch);
            }
          }, 750);
        }
      } catch (err: any) {
        if (isMountedRef.current && epoch === workflowEpochRef.current) {
          setJobError(err.message || 'İş durumu sorgulanırken hata oluştu.');
        }
      }
    },
    [client]
  );

  // Explicitly load a specific job and its report
  const loadJob = useCallback(
    async (jobId: string) => {
      const epoch = ++workflowEpochRef.current;
      if (pollTimerRef.current) {
        clearTimeout(pollTimerRef.current);
        pollTimerRef.current = null;
      }
      try {
        setJobError(null);
        const job = await client.getJob(jobId);
        if (!isMountedRef.current || epoch !== workflowEpochRef.current) return;

        setActiveJob(job);

        if (job.status === 'completed') {
          try {
            const report = await client.getJobReport(jobId);
            if (!isMountedRef.current || epoch !== workflowEpochRef.current) return;
            setJobReport(report);
          } catch {
            // report fetch err
          }
        } else {
          setJobReport(null);
          if (job.status === 'converting' || job.status === 'verifying' || job.status === 'queued') {
            pollJobStatus(job.jobId, epoch);
          }
        }
      } catch (err: any) {
        if (isMountedRef.current && epoch === workflowEpochRef.current) {
          setJobError(err.message || 'İş durumu sorgulanırken hata oluştu.');
        }
      }
    },
    [client, pollJobStatus]
  );

  // Check backend health & connectivity
  const checkService = useCallback(async () => {
    try {
      setServiceStatus('checking');
      await client.checkReady();
      if (isMountedRef.current) {
        setServiceStatus('online');
        try {
          const currentEpoch = workflowEpochRef.current;
          const activeJobs = await client.getJobsPage({ pageSize: 100, status: 'active', jobKind: 'split' }).then(page => page.items);
          const jobs = activeJobs.length > 0
            ? activeJobs
            : await client.getJobsPage({ pageSize: 1, status: 'completed', jobKind: 'split' }).then(page => page.items);
          if (isMountedRef.current && currentEpoch === workflowEpochRef.current) {
            setLocalJobs(jobs);

            // Restore state across page refresh / initial load
            if (options?.targetJobId) {
              hasRestoredRef.current = true;
              await loadJob(options.targetJobId);
            } else if (!hasRestoredRef.current && !userChosenSourceRef.current && !selectedSourceRef.current && jobs.length > 0) {
              hasRestoredRef.current = true;
              const runningJob = jobs.find(
                (j) =>
                  (j.status === 'converting' || j.status === 'verifying' || j.status === 'queued') &&
                  j.jobKind === 'split'
              );
              if (runningJob) {
                if (!userChosenSourceRef.current && !selectedSourceRef.current && currentEpoch === workflowEpochRef.current) {
                  setActiveJob(runningJob);
                  pollJobStatus(runningJob.jobId, currentEpoch);
                }
              } else {
                const latestCompleted = jobs.find((j) => j.status === 'completed' && j.jobKind === 'split');
                if (latestCompleted && !userChosenSourceRef.current && !selectedSourceRef.current && currentEpoch === workflowEpochRef.current) {
                  setActiveJob(latestCompleted);
                  try {
                    const report = await client.getJobReport(latestCompleted.jobId);
                    if (isMountedRef.current && !userChosenSourceRef.current && !selectedSourceRef.current && currentEpoch === workflowEpochRef.current) {
                      setJobReport(report);
                    }
                  } catch {
                    // report fetch err
                  }
                }
              }
            }
          }
        } catch {
          // ignore job fetch failure on start
        }
      }
    } catch {
      if (isMountedRef.current) {
        setServiceStatus('offline');
      }
    }
  }, [client, pollJobStatus, loadJob, options?.targetJobId]);

  useEffect(() => {
    isMountedRef.current = true;
    if (options?.autoCheck !== false) {
      checkService();
    }
    return () => {
      isMountedRef.current = false;
      if (pollTimerRef.current) {
        clearTimeout(pollTimerRef.current);
      }
    };
  }, [checkService, options?.autoCheck]);

  useEffect(() => {
    if (options?.targetJobId) {
      loadJob(options.targetJobId);
    }
  }, [options?.targetJobId, loadJob]);

  // Invalidate plan and idempotency key
  const invalidatePlan = useCallback(() => {
    planRequestIdRef.current += 1;
    setIsLoadingPlan(false);
    setSplitPlan(null);
    setStableIdempotencyKey(null);
    setPlanError(null);
  }, []);

  // Invalidate selection preview
  const invalidatePreview = useCallback(() => {
    previewRequestIdRef.current += 1;
    setIsLoadingPreview(false);
    setSelectionPreview(null);
    setPreviewError(null);
  }, []);

  // Authoritative selection preview fetch
  const fetchSelectionPreview = useCallback(async () => {
    if (!selectedSource?.handle || !analysis) return;

    const reqId = ++previewRequestIdRef.current;
    setIsLoadingPreview(true);
    setPreviewError(null);

    try {
      const preview = await client.getSelectionPreview({
        sourceHandle: selectedSource.handle,
        folderIds: selectedFolderIds,
        startDate: startDate.trim() || null,
        endDate: endDate.trim() || null,
      });

      if (!isMountedRef.current || reqId !== previewRequestIdRef.current) {
        return;
      }

      setSelectionPreview(preview);
    } catch (err: any) {
      if (!isMountedRef.current || reqId !== previewRequestIdRef.current) {
        return;
      }
      setSelectionPreview(null);
      setPreviewError(err.message || 'Filtre önizlemesi alınamadı.');
    } finally {
      if (isMountedRef.current && reqId === previewRequestIdRef.current) {
        setIsLoadingPreview(false);
      }
    }
  }, [client, selectedSource, analysis, selectedFolderIds, startDate, endDate]);

  // Debounced auto-fetch of selection preview
  useEffect(() => {
    if (!selectedSource?.handle || !analysis) {
      setSelectionPreview(null);
      setIsLoadingPreview(false);
      return;
    }

    const timer = setTimeout(() => {
      fetchSelectionPreview();
    }, 150);

    return () => clearTimeout(timer);
  }, [fetchSelectionPreview, selectedSource?.handle, analysis]);

  // Authoritative split plan fetch
  const fetchSplitPlan = useCallback(async () => {
    if (!selectedSource?.handle || !analysis || !selectionPreview) {
      setSplitPlan(null);
      setStableIdempotencyKey(null);
      setIsLoadingPlan(false);
      return;
    }

    // If trial blocker or zero items selected, split plan is blocked immediately
    if (!selectionPreview.canConvert || selectionPreview.selectedMessagesCount === 0 || analysis.preflight.hasTrialBlocker) {
      const blockerReason =
        analysis.preflight.trialBlockerReason ||
        selectionPreview.blockerReason ||
        selectionPreview.blockReason ||
        (selectionPreview.selectedMessagesCount === 0
          ? 'Seçim kriterlerinize uyan hiçbir ileti bulunamadı. Sıfır iletili bölme başlatılamaz.'
          : 'Bölme başlatılamaz.');

      setSplitPlan({
        planId: '',
        sourceHandle: selectedSource.handle,
        sourceSha256: analysis.sourceSha256,
        selectionId: selectionPreview.selectionId,
        splitMode,
        sizeCapBytes: splitMode === 'size' ? Math.floor(Math.max(1, Math.floor(sizeCapMb)) * 1_000_000) : null,
        totalSourceMessages: selectionPreview.totalSourceMessages,
        selectedMessagesCount: selectionPreview.selectedMessagesCount,
        excludedMessagesCount: selectionPreview.excludedMessagesCount,
        selectedAttachmentsCount: selectionPreview.selectedAttachmentsCount,
        yearGroups: [],
        canSplit: false,
        blockerReason,
      });
      setStableIdempotencyKey(null);
      setIsLoadingPlan(false);
      return;
    }

    const reqId = ++planRequestIdRef.current;
    setIsLoadingPlan(true);
    setPlanError(null);

    const sizeCapBytes =
      splitMode === 'size'
        ? Math.floor(Math.max(1, Math.floor(sizeCapMb)) * 1_000_000)
        : null;

    try {
      const plan = await client.createSplitPlan({
        sourceHandle: selectedSource.handle,
        selectionId: selectionPreview.selectionId,
        splitMode,
        sizeCapBytes,
        folderIds: selectedFolderIds,
        startDate: startDate.trim() || null,
        endDate: endDate.trim() || null,
      });

      if (!isMountedRef.current || reqId !== planRequestIdRef.current) {
        return;
      }

      const enrichedPlan: SplitPlanResult = {
        ...plan,
        totalSourceMessages: plan.totalSourceMessages || selectionPreview.totalSourceMessages,
        selectedMessagesCount: plan.selectedMessagesCount || selectionPreview.selectedMessagesCount,
        excludedMessagesCount: plan.excludedMessagesCount || selectionPreview.excludedMessagesCount,
        selectedAttachmentsCount: plan.selectedAttachmentsCount || selectionPreview.selectedAttachmentsCount,
      };

      setSplitPlan(enrichedPlan);
      if (enrichedPlan.canSplit && enrichedPlan.planId) {
        // Generate one stable idempotency key per unchanged plan attempt
        setStableIdempotencyKey(`idemp_split_${enrichedPlan.planId}_${Math.random().toString(36).substring(2, 9)}`);
      } else {
        setStableIdempotencyKey(null);
      }
    } catch (err: any) {
      if (!isMountedRef.current || reqId !== planRequestIdRef.current) {
        return;
      }
      setSplitPlan(null);
      setStableIdempotencyKey(null);
      setPlanError(err.message || 'Bölümleme planı alınamadı.');
    } finally {
      if (isMountedRef.current && reqId === planRequestIdRef.current) {
        setIsLoadingPlan(false);
      }
    }
  }, [client, selectedSource, analysis, selectionPreview, splitMode, sizeCapMb, selectedFolderIds, startDate, endDate]);

  // Debounced auto-fetch of split plan whenever selection preview or split options change
  useEffect(() => {
    if (!selectedSource?.handle || !analysis || !selectionPreview) {
      setSplitPlan(null);
      setStableIdempotencyKey(null);
      setIsLoadingPlan(false);
      return;
    }

    const timer = setTimeout(() => {
      fetchSplitPlan();
    }, 150);

    return () => clearTimeout(timer);
  }, [fetchSplitPlan, selectedSource?.handle, analysis, selectionPreview, splitMode, sizeCapMb]);

  // Folder selection handlers (invalidate preview and plan)
  const toggleFolder = useCallback((folderId: string) => {
    invalidatePreview();
    invalidatePlan();
    setSelectedFolderIdsState((prev) => {
      return prev.includes(folderId) ? prev.filter((id) => id !== folderId) : [...prev, folderId];
    });
  }, [invalidatePreview, invalidatePlan]);

  const selectAllFolders = useCallback(() => {
    if (!analysis) return;
    invalidatePreview();
    invalidatePlan();
    setSelectedFolderIdsState(analysis.folders.map((f) => f.folderId));
  }, [analysis, invalidatePreview, invalidatePlan]);

  const clearAllFolders = useCallback(() => {
    invalidatePreview();
    invalidatePlan();
    setSelectedFolderIdsState([]);
  }, [invalidatePreview, invalidatePlan]);

  const setSelectedFolderIds = useCallback((action: SetStateAction<string[]>) => {
    invalidatePreview();
    invalidatePlan();
    setSelectedFolderIdsState(action);
  }, [invalidatePreview, invalidatePlan]);

  // Date bounds handlers (invalidate preview and plan)
  const setStartDate = useCallback((date: string) => {
    invalidatePreview();
    invalidatePlan();
    setStartDateState(date);
  }, [invalidatePreview, invalidatePlan]);

  const setEndDate = useCallback((date: string) => {
    invalidatePreview();
    invalidatePlan();
    setEndDateState(date);
  }, [invalidatePreview, invalidatePlan]);

  // Split options handlers (invalidate plan)
  const setSplitMode = useCallback((mode: 'year' | 'size') => {
    invalidatePlan();
    setSplitModeState(mode);
  }, [invalidatePlan]);

  const setSizeCapMb = useCallback((mb: number) => {
    const validMb = Math.max(1, Math.floor(mb) || 1);
    invalidatePlan();
    setSizeCapMbState(validMb);
  }, [invalidatePlan]);

  // Pick Split Source (PST or OST) via native WinForms dialog
  const pickSplitSourceFile = useCallback(async () => {
    try {
      setAnalysisError(null);
      const res: FilePickResult = await client.pickSplitSource();

      if (res.cancelled) {
        return;
      }

      if (res.error) {
        setAnalysisError(res.error);
        return;
      }

      if (res.handle && res.fileName) {
        const sourceEpoch = ++workflowEpochRef.current;
        if (pollTimerRef.current) {
          clearTimeout(pollTimerRef.current);
          pollTimerRef.current = null;
        }
        userChosenSourceRef.current = true;
        hasRestoredRef.current = true;

        const sourceData = {
          handle: res.handle,
          fileName: res.fileName,
          sizeBytes: res.sizeBytes || 0,
          displayPath: res.displayPath || null,
        };
        selectedSourceRef.current = sourceData;
        setSelectedSource(sourceData);
        setSelectedOutputDir(null);
        setAnalysis(null);
        setJobReport(null);
        setActiveJob(null);
        setJobError(null);
        setSelectedFolderIdsState([]);
        setStartDateState('');
        setEndDateState('');
        invalidatePreview();
        invalidatePlan();

        setIsAnalyzing(true);
        try {
          const result = await client.analyzeSplitSource(res.handle);
          if (isMountedRef.current && sourceEpoch === workflowEpochRef.current) {
            setAnalysis(result);
            setSelectedFolderIdsState(result.folders.map((f) => f.folderId));
          }
        } catch (err: any) {
          if (isMountedRef.current && sourceEpoch === workflowEpochRef.current) {
            setAnalysisError(err.message || 'Kaynak analizi gerçekleştirilemedi.');
          }
        } finally {
          if (isMountedRef.current && sourceEpoch === workflowEpochRef.current) {
            setIsAnalyzing(false);
          }
        }
      }
    } catch (err: any) {
      setAnalysisError(err.message || 'Kaynak seçici açılamadı.');
    }
  }, [client, invalidatePlan, invalidatePreview]);

  // Pick Output Directory via native FolderBrowserDialog
  const pickOutputDir = useCallback(async () => {
    try {
      setJobError(null);
      const res: FilePickResult = await client.pickOutputDir();

      if (res.cancelled) {
        return;
      }

      if (res.error) {
        setJobError(res.error);
        return;
      }

      if (res.handle) {
        setSelectedOutputDir({
          handle: res.handle,
          fileName: res.fileName || 'Arşiv Çıktı Klasörü',
          displayPath: res.displayPath || null,
        });
      }
    } catch (err: any) {
      setJobError(err.message || 'Hedef klasör seçici açılamadı.');
    }
  }, [client]);

  const clearActiveJob = useCallback(() => {
    workflowEpochRef.current += 1;
    if (pollTimerRef.current) {
      clearTimeout(pollTimerRef.current);
      pollTimerRef.current = null;
    }
    userChosenSourceRef.current = true;
    setActiveJob(null);
    setJobReport(null);
    setJobError(null);
  }, []);

  // Start Split Job
  const startSplit = useCallback(
    async (context: ClientProjectContext) => {
      if (!selectedSource) {
        setJobError('Lütfen kaynak PST veya OST dosyasını seçin.');
        return;
      }

      if (!selectedOutputDir) {
        setJobError('Lütfen hedef çıktı klasörünü seçin.');
        return;
      }

      if (analysis?.preflight.hasTrialBlocker) {
        setJobError(
          'Ön kontrol engelleri varken bölümleme başlatılamaz: ' +
            (analysis.preflight.trialBlockerReason || '')
        );
        return;
      }

      if (!splitPlan || !splitPlan.canSplit || !splitPlan.planId) {
        setJobError(
          splitPlan?.blockerReason ||
            'Geçerli bir bölümleme planı bulunamadı. Lütfen plan önizlemesini yenileyin.'
        );
        return;
      }

      if (splitPlan.selectedMessagesCount === 0) {
        setJobError('Seçim kriterlerinize uyan hiçbir ileti bulunamadı. Sıfır iletili bölme başlatılamaz.');
        return;
      }

      const epoch = ++workflowEpochRef.current;
      if (pollTimerRef.current) {
        clearTimeout(pollTimerRef.current);
        pollTimerRef.current = null;
      }

      setIsStarting(true);
      setJobError(null);
      setJobReport(null);

      // Re-use stable idempotency key generated for this plan, or generate one if missing
      const key =
        stableIdempotencyKey ||
        `idemp_split_${splitPlan.planId}_${Math.random().toString(36).substring(2, 9)}`;

      try {
        const job = await client.startSplitJob({
          planId: splitPlan.planId,
          outputDirHandle: selectedOutputDir.handle,
          idempotencyKey: key,
          enqueueIfBusy: true,
          clientContext: {
            companyId: context.companyId,
            companyName: context.companyName,
            projectId: context.projectId,
            projectName: context.projectName,
          },
        });

        if (!isMountedRef.current || epoch !== workflowEpochRef.current) {
          return;
        }

        userChosenSourceRef.current = false;
        setActiveJob(job);
        pollJobStatus(job.jobId, epoch);
      } catch (err: any) {
        if (isMountedRef.current && epoch === workflowEpochRef.current) {
          setJobError(err.message || 'Bölümleme başlatılamadı.');
        }
      } finally {
        if (isMountedRef.current && epoch === workflowEpochRef.current) {
          setIsStarting(false);
        }
      }
    },
    [client, selectedSource, selectedOutputDir, analysis, splitPlan, stableIdempotencyKey, pollJobStatus]
  );

  return {
    client,
    serviceStatus,
    checkService,
    selectedSource,
    selectedOutputDir,
    analysis,
    isAnalyzing,
    analysisError,
    // Selection filter state & actions
    selectedFolderIds,
    setSelectedFolderIds,
    toggleFolder,
    selectAllFolders,
    clearAllFolders,
    startDate,
    setStartDate,
    endDate,
    setEndDate,
    selectionPreview,
    isLoadingPreview,
    previewError,
    fetchSelectionPreview,
    invalidatePreview,
    // Split mode & options
    splitMode,
    setSplitMode,
    sizeCapMb,
    setSizeCapMb,
    splitPlan,
    isLoadingPlan,
    planError,
    fetchSplitPlan,
    invalidatePlan,
    stableIdempotencyKey,
    // Job state & actions
    activeJob,
    isStarting,
    jobError,
    jobReport,
    localJobs,
    loadJob,
    clearActiveJob,
    pickSplitSourceFile,
    pickOutputDir,
    startSplit,
  };
}
