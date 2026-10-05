import { useState, useEffect, useCallback, useRef } from 'react';
import { localEngineClient, LocalEngineClient } from '../api/localEngineClient';
import {
  ClientProjectContext,
  ConversionReport,
  FilePickResult,
  LocalJobRecord,
  OstAnalysisResult,
  SelectionPreviewResult,
} from '../types/localEngine';

export interface UseLocalEngineOptions {
  client?: LocalEngineClient;
  autoCheck?: boolean;
  targetJobId?: string | null;
}

export function useLocalEngine(options?: UseLocalEngineOptions) {
  const client = options?.client || localEngineClient;

  const [serviceStatus, setServiceStatus] = useState<'checking' | 'online' | 'offline'>('checking');
  const [selectedSource, setSelectedSource] = useState<{ handle: string; fileName: string; sizeBytes: number; displayPath?: string | null } | null>(null);
  const [selectedTarget, setSelectedTarget] = useState<{ handle: string; fileName: string; displayPath?: string | null } | null>(null);
  const [analysis, setAnalysis] = useState<OstAnalysisResult | null>(null);
  const [isAnalyzing, setIsAnalyzing] = useState<boolean>(false);
  const [analysisError, setAnalysisError] = useState<string | null>(null);

  // Selection filter state (TASK-011)
  const [selectedFolderIds, setSelectedFolderIds] = useState<string[]>([]);
  const [startDate, setStartDateState] = useState<string>('');
  const [endDate, setEndDateState] = useState<string>('');
  const [selectionPreview, setSelectionPreview] = useState<SelectionPreviewResult | null>(null);
  const [isLoadingPreview, setIsLoadingPreview] = useState<boolean>(false);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const previewRequestIdRef = useRef<number>(0);

  const [activeJob, setActiveJob] = useState<LocalJobRecord | null>(null);
  const [isStarting, setIsStarting] = useState<boolean>(false);
  const [jobError, setJobError] = useState<string | null>(null);
  const [jobReport, setJobReport] = useState<ConversionReport | null>(null);
  const [localJobs, setLocalJobs] = useState<LocalJobRecord[]>([]);

  const pollTimerRef = useRef<any>(null);
  const isMountedRef = useRef<boolean>(true);
  const hasRestoredRef = useRef<boolean>(false);

  // Polling routine for active job
  const pollJobStatus = useCallback(
    async (jobId: string) => {
      try {
        const job = await client.getJob(jobId);
        if (!isMountedRef.current) return;

        setActiveJob(job);

        if (job.status === 'completed') {
          // Fetch final report
          try {
            const report = await client.getJobReport(jobId);
            if (isMountedRef.current) {
              setJobReport(report);
            }
          } catch {
            // report fetch err
          }
          // Refresh jobs list
          const all = await client.getJobsPage({ pageSize: 100, jobKind: 'convert' }).then(page => page.items).catch(() => []);
          if (isMountedRef.current) {
            setLocalJobs(all);
          }
        } else if (job.status === 'failed' || job.status === 'interrupted') {
          const all = await client.getJobsPage({ pageSize: 100, jobKind: 'convert' }).then(page => page.items).catch(() => []);
          if (isMountedRef.current) {
            setLocalJobs(all);
          }
        } else {
          // Continue polling while converting or verifying
          pollTimerRef.current = setTimeout(() => {
            pollJobStatus(jobId);
          }, 750);
        }
      } catch (err: any) {
        if (isMountedRef.current) {
          setJobError(err.message || 'İş durumu sorgulanırken hata oluştu.');
        }
      }
    },
    [client]
  );

  // Explicitly load a specific job and its report
  const loadJob = useCallback(
    async (jobId: string) => {
      try {
        setJobError(null);
        const job = await client.getJob(jobId);
        if (!isMountedRef.current) return;

        setActiveJob(job);

        if (job.status === 'completed') {
          try {
            const report = await client.getJobReport(jobId);
            if (isMountedRef.current) {
              setJobReport(report);
            }
          } catch {
            // report fetch err
          }
        } else {
          setJobReport(null);
          if (job.status === 'converting' || job.status === 'verifying' || job.status === 'queued') {
            pollJobStatus(job.jobId);
          }
        }
      } catch (err: any) {
        if (isMountedRef.current) {
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
      await client.initSession();
      const status = await client.getStatus();
      if (status && isMountedRef.current) {
        setServiceStatus('online');
        // Refresh past jobs
        try {
          const activeJobs = await client.getJobsPage({ pageSize: 100, status: 'active', jobKind: 'convert' }).then(page => page.items);
          const jobs = activeJobs.length > 0
            ? activeJobs
            : await client.getJobsPage({ pageSize: 1, status: 'completed', jobKind: 'convert' }).then(page => page.items);
          if (isMountedRef.current) {
            setLocalJobs(jobs);

            // Restore state across page refresh / initial load
            if (options?.targetJobId) {
              hasRestoredRef.current = true;
              await loadJob(options.targetJobId);
            } else if (!hasRestoredRef.current && jobs.length > 0) {
              hasRestoredRef.current = true;
              const runningJob = jobs.find(
                (j) => (j.status === 'converting' || j.status === 'verifying' || j.status === 'queued') && j.jobKind !== 'split' && j.jobKind !== 'mime-import'
              );
              if (runningJob) {
                setActiveJob(runningJob);
                pollJobStatus(runningJob.jobId);
              } else {
                const latestCompleted = jobs.find((j) => j.status === 'completed' && j.jobKind !== 'split' && j.jobKind !== 'mime-import');
                if (latestCompleted) {
                  setActiveJob(latestCompleted);
                  try {
                    const report = await client.getJobReport(latestCompleted.jobId);
                    if (isMountedRef.current) {
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

  // Authoritative selection preview fetch (TASK-011)
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
        // Discard stale asynchronous response
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

  // Debounced auto-fetch of preview when filters change
  useEffect(() => {
    if (!selectedSource?.handle || !analysis) {
      setSelectionPreview(null);
      return;
    }

    const timer = setTimeout(() => {
      fetchSelectionPreview();
    }, 150);

    return () => clearTimeout(timer);
  }, [fetchSelectionPreview, selectedSource?.handle, analysis]);

  const toggleFolder = useCallback((folderId: string) => {
    setSelectionPreview(null);
    setSelectedFolderIds((prev) => {
      return prev.includes(folderId) ? prev.filter((id) => id !== folderId) : [...prev, folderId];
    });
  }, []);

  const selectAllFolders = useCallback(() => {
    if (!analysis) return;
    setSelectionPreview(null);
    setSelectedFolderIds(analysis.folders.map((f) => f.folderId));
  }, [analysis]);

  const clearAllFolders = useCallback(() => {
    setSelectionPreview(null);
    setSelectedFolderIds([]);
  }, []);

  const setStartDate = useCallback((date: string) => {
    setSelectionPreview(null);
    setStartDateState(date);
  }, []);

  const setEndDate = useCallback((date: string) => {
    setSelectionPreview(null);
    setEndDateState(date);
  }, []);

  // Pick Source OST file via native WinForms dialog
  const pickSourceFile = useCallback(async () => {
    try {
      setAnalysisError(null);
      const res: FilePickResult = await client.pickSource();

      if (res.cancelled) {
        // Safe: preserves prior selection intact
        return;
      }

      if (res.error) {
        setAnalysisError(res.error);
        return;
      }

      if (res.handle && res.fileName) {
        setSelectedSource({
          handle: res.handle,
          fileName: res.fileName,
          sizeBytes: res.sizeBytes || 0,
          displayPath: res.displayPath || null,
        });
        setSelectedTarget(null);
        setAnalysis(null);
        setJobReport(null);
        setActiveJob(null);
        setSelectedFolderIds([]);
        setStartDateState('');
        setEndDateState('');
        setSelectionPreview(null);
        setPreviewError(null);

        // Immediately analyze selected source
        setIsAnalyzing(true);
        try {
          const result = await client.analyzeSource(res.handle);
          if (isMountedRef.current) {
            setAnalysis(result);
            setSelectedFolderIds(result.folders.map((f) => f.folderId));
          }
        } catch (err: any) {
          if (isMountedRef.current) {
            setAnalysisError(err.message || 'OST analizi gerçekleştirilemedi.');
          }
        } finally {
          if (isMountedRef.current) {
            setIsAnalyzing(false);
          }
        }
      }
    } catch (err: any) {
      setAnalysisError(err.message || 'Kaynak seçici açılamadı.');
    }
  }, [client]);

  // Pick Target PST file via native SaveFileDialog
  const pickTargetFile = useCallback(async () => {
    try {
      setJobError(null);
      const res: FilePickResult = await client.pickTarget(selectedSource?.handle);

      if (res.cancelled) {
        // Safe: preserves prior selection intact
        return;
      }

      if (res.error) {
        setJobError(res.error);
        return;
      }

      if (res.handle && res.fileName) {
        setSelectedTarget({
          handle: res.handle,
          fileName: res.fileName,
          displayPath: res.displayPath || null,
        });
      }
    } catch (err: any) {
      setJobError(err.message || 'Hedef seçici açılamadı.');
    }
  }, [client, selectedSource]);

  // Start conversion
  const startConversion = useCallback(
    async (context: ClientProjectContext) => {
      if (!selectedSource || !selectedTarget) {
        setJobError('Lütfen kaynak OST ve hedef PST dosyalarını seçin.');
        return;
      }

      if (analysis?.preflight.hasTrialBlocker) {
        setJobError('Ön kontrol engelleri varken dönüştürme başlatılamaz: ' + (analysis.preflight.trialBlockerReason || ''));
        return;
      }

      if (!selectionPreview) {
        setJobError('Lütfen dönüştürme öncesinde geçerli bir filtre önizlemesi alın.');
        return;
      }

      if (!selectionPreview.canConvert || selectionPreview.selectedMessagesCount === 0) {
        setJobError(selectionPreview.blockerReason || selectionPreview.blockReason || 'Seçim kriterlerinize uyan hiçbir ileti bulunamadı. Sıfır iletili dönüştürme başlatılamaz.');
        return;
      }

      setIsStarting(true);
      setJobError(null);
      setJobReport(null);

      const idempotencyKey = `idemp_${Date.now()}_${Math.random().toString(36).substring(2, 9)}`;

      try {
        const job = await client.startJob({
          sourceHandle: selectedSource.handle,
          targetHandle: selectedTarget.handle,
          selectionId: selectionPreview.selectionId,
          idempotencyKey,
          expectedSourceSha256: analysis?.sourceSha256,
          estimatedTotalItems: selectionPreview.selectedMessagesCount,
          enqueueIfBusy: true,
          clientContext: {
            companyId: context.companyId,
            companyName: context.companyName,
            projectId: context.projectId,
            projectName: context.projectName,
          },
        });

        setActiveJob(job);
        pollJobStatus(job.jobId);
      } catch (err: any) {
        setJobError(err.message || 'Dönüştürme başlatılamadı.');
      } finally {
        setIsStarting(false);
      }
    },
    [client, selectedSource, selectedTarget, analysis, selectionPreview, pollJobStatus]
  );

  return {
    client,
    serviceStatus,
    checkService,
    selectedSource,
    selectedTarget,
    analysis,
    isAnalyzing,
    analysisError,
    // Selection filter state & actions (TASK-011)
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
    // Job state & actions
    activeJob,
    isStarting,
    jobError,
    jobReport,
    localJobs,
    loadJob,
    pickSourceFile,
    pickTargetFile,
    startConversion,
  };
}
