import { useEffect, useRef, useState } from 'react';
import { localEngineClient as client } from '../api/localEngineClient';
import { ClientProjectContext, ConversionReport, DuplicatePolicy, FilePickResult, LocalJobRecord, MimeAnalysisResult, MimeSourceMode, SelectionPreviewResult } from '../types/localEngine';

const message = (e: unknown) => {
  const text = e instanceof Error ? e.message : 'İşlem tamamlanamadı.';
  if (/Failed to parse message headers|End of stream/i.test(text))
    return 'Posta dosyası okunamadı. Dosya boş veya geçersiz olabilir; geçerli bir EML ya da mboxrd arşivi seç.';
  if (/Failed to fetch|NetworkError/i.test(text))
    return 'Yerel motora ulaşılamıyor. Bağlantıyı kontrol edip yeniden dene.';
  return text;
};
export const isMimeJobRunning = (job: LocalJobRecord | null) => !!job && ['queued', 'converting', 'verifying'].includes(job.status);

export function useMimeImport(context: ClientProjectContext, targetJobId: string | null) {
  const [mode, setMode] = useState<MimeSourceMode>('eml-files');
  const [source, setSource] = useState<FilePickResult | null>(null);
  const [target, setTarget] = useState<FilePickResult | null>(null);
  const [analysis, setAnalysis] = useState<MimeAnalysisResult | null>(null);
  const [folders, setFolders] = useState<string[]>([]);
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [advancedFilterJson,setAdvancedFilterJson]=useState('');
  const [folderMappings,setFolderMappings]=useState<Record<string,string>>({});
  const [duplicatePolicy,setDuplicatePolicy]=useState<DuplicatePolicy>('PreservePhysical');
  const [preview, setPreview] = useState<{ scope: string; value: SelectionPreviewResult } | null>(null);
  const [busy, setBusy] = useState(false);
  const [starting, setStarting] = useState(false);
  const [loadingPreview, setLoadingPreview] = useState(false);
  const [status, setStatus] = useState<'checking' | 'online' | 'offline'>('checking');
  const [error, setError] = useState('');
  const [job, setJob] = useState<LocalJobRecord | null>(null);
  const [report, setReport] = useState<ConversionReport | null>(null);
  const mounted = useRef(false);
  const pickerEpoch = useRef(0);
  const jobEpoch = useRef(0);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const attempt = useRef<{ scope: string; target: string; key: string } | null>(null);
  const contextKey = JSON.stringify(context);
  const previousContext = useRef(contextKey);
  const draftContext = useRef(contextKey);
  const scope = JSON.stringify([contextKey, mode, source?.handle, folders, startDate, endDate,advancedFilterJson,folderMappings,duplicatePolicy]);
  const scopeRef = useRef(scope);
  scopeRef.current = scope;
  const activePreview = preview?.scope === scope ? preview.value : null;
  const running = isMimeJobRunning(job);

  function clearDraft(clearJob: boolean) {
    pickerEpoch.current++;
    jobEpoch.current++;
    if (clearJob) { clearTimeout(timer.current); setJob(null); setReport(null); }
    setSource(null); setTarget(null); setAnalysis(null); setFolders([]);
    setStartDate(''); setEndDate(''); setAdvancedFilterJson(''); setFolderMappings({}); setDuplicatePolicy('PreservePhysical'); setPreview(null); setError(''); setBusy(false); setLoadingPreview(false);
    draftContext.current = contextKey;
  }

  async function loadJob(id: string) {
    const epoch = ++jobEpoch.current;
    clearTimeout(timer.current); setReport(null); setError('');
    async function tick() {
      try {
        const next = await client.getJob(id);
        if (!mounted.current || epoch !== jobEpoch.current) return;
        if (next.jobKind !== 'mime-import') throw new Error('Bu kayıt bir EML / MBOX içe aktarma işi değil.');
        setJob(next);
        if (next.status === 'completed') {
          const finalReport = await client.getJobReport(id);
          if (mounted.current && epoch === jobEpoch.current) setReport(finalReport);
        } else if (isMimeJobRunning(next)) {
          timer.current = setTimeout(tick, 500);
        }
      } catch (e) { if (mounted.current && epoch === jobEpoch.current) setError(message(e)); }
    }
    await tick();
  }

  async function connect() {
    setStatus('checking');
    try { await client.checkReady({ reuseSession: true }); if (mounted.current) setStatus('online'); }
    catch { if (mounted.current) setStatus('offline'); }
  }

  useEffect(() => {
    mounted.current = true;
    const epoch = jobEpoch.current;
    void connect();
    if (!targetJobId) {
      client.getJobsPage({ pageSize: 100, status: 'active', jobKind: 'mime-import' })
        .then(async page => page.items.length > 0 ? page.items : (await client.getJobsPage({ pageSize: 1, jobKind: 'mime-import' })).items)
        .then(jobs => {
        if (!mounted.current || epoch !== jobEpoch.current) return;
        const latest = jobs.find(j => j.jobKind === 'mime-import');
        if (latest) void loadJob(latest.jobId);
      }).catch(() => { /* connection banner provides retry */ });
    }
    return () => { mounted.current = false; pickerEpoch.current++; jobEpoch.current++; clearTimeout(timer.current); };
  }, []);

  useEffect(() => { if (targetJobId) void loadJob(targetJobId); }, [targetJobId]);
  useEffect(() => {
    if (previousContext.current !== contextKey) {
      previousContext.current = contextKey;
      // Keep the active/result job's frozen context and its polling intact.
      const savedEpoch = jobEpoch.current;
      clearDraft(false);
      jobEpoch.current = savedEpoch;
    }
  }, [contextKey]);

  useEffect(() => {
    setPreview(null); setLoadingPreview(false);
    if (!source?.handle || !analysis || folders.length === 0 || draftContext.current !== contextKey) return;
    if (startDate && endDate && startDate > endDate) { setError('Başlangıç tarihi bitiş tarihinden sonra olamaz.'); return; }
    let cancelled = false;
    setLoadingPreview(true); setError('');
    const requestScope = scope;
    const delay = setTimeout(() => {
      let advancedFilter=null;try{advancedFilter=advancedFilterJson.trim()?JSON.parse(advancedFilterJson):null}catch{setError('Gelişmiş filtre JSON biçimi geçersiz.');setLoadingPreview(false);return;}
      client.previewMimeSelection({ sourceHandle: source.handle!, folderIds: folders, startDate: startDate || null, endDate: endDate || null,advancedFilter,
        folderMappings: folders.map(sourceFolderId=>({sourceFolderId,targetFolderPath:(folderMappings[sourceFolderId]||analysis.folders.find(f=>f.folderId===sourceFolderId)?.folderPath||'Inbox').trim()})), duplicatePolicy }).then(value => {
        if (!cancelled && mounted.current && scopeRef.current === requestScope) setPreview({ scope: requestScope, value });
      }).catch(e => {
        if (!cancelled && mounted.current && scopeRef.current === requestScope) setError(message(e));
      }).finally(() => {
        if (!cancelled && mounted.current && scopeRef.current === requestScope) setLoadingPreview(false);
      });
    }, 150);
    return () => { cancelled = true; clearTimeout(delay); };
  }, [scope, analysis]);

  async function pickSource() {
    if (running || starting) return;
    clearDraft(true);
    const epoch = ++pickerEpoch.current;
    setBusy(true);
    try {
      const picked = await client.pickMimeSource(mode);
      if (!mounted.current || epoch !== pickerEpoch.current) return;
      if (picked.error) throw new Error(picked.error);
      if (picked.cancelled || !picked.handle) return;
      const result = await client.analyzeMimeSource(picked.handle);
      if (!mounted.current || epoch !== pickerEpoch.current) return;
      setSource(picked); setAnalysis(result); setFolders(result.folders.filter(f => f.itemCount > 0).map(f => f.folderId));
    } catch (e) { if (mounted.current && epoch === pickerEpoch.current) setError(message(e)); }
    finally { if (mounted.current && epoch === pickerEpoch.current) setBusy(false); }
  }

  async function pickTarget() {
    if (!source?.handle || running || starting || busy) return;
    const epoch = pickerEpoch.current;
    setBusy(true); setError('');
    try {
      const value = await client.pickTarget(source.handle);
      if (!mounted.current || epoch !== pickerEpoch.current) return;
      if (value.error) throw new Error(value.error);
      if (!value.cancelled && value.handle) setTarget(value);
    } catch (e) { if (mounted.current && epoch === pickerEpoch.current) setError(message(e)); }
    finally { if (mounted.current && epoch === pickerEpoch.current) setBusy(false); }
  }

  const canStart = status === 'online' && !busy && !starting && !running && job?.status !== 'completed' && !!source?.handle && !!target?.handle &&
    !!activePreview?.canConvert && activePreview.selectedMessagesCount > 0 && !loadingPreview && draftContext.current === contextKey;
  async function start() {
    if (!canStart || !source?.handle || !target?.handle || !activePreview || !analysis) return;
    setStarting(true); setError('');
    jobEpoch.current++; clearTimeout(timer.current); setJob(null); setReport(null);
    if (!attempt.current || attempt.current.scope !== scope || attempt.current.target !== target.handle)
      attempt.current = { scope, target: target.handle, key: crypto.randomUUID() };
    try {
      const started = await client.startMimeJob({ sourceHandle: source.handle, targetHandle: target.handle,
        selectionId: activePreview.selectionId, expectedSourceSha256: analysis.sourceFingerprint,
        idempotencyKey: attempt.current.key, clientContext: { ...context }, enqueueIfBusy: true });
      if (mounted.current) { setJob(started); await loadJob(started.jobId); }
    } catch (e) { if (mounted.current) setError(message(e)); }
    finally { if (mounted.current) setStarting(false); }
  }
  return { mode, source, target, analysis, folders, startDate, endDate,advancedFilterJson,folderMappings,duplicatePolicy, preview: activePreview, busy, starting, loadingPreview,
    status, error, job, report, running, canStart, pickSource, pickTarget, start, connect, loadJob,
    setMode: (value: MimeSourceMode) => { if (!running && !starting) { clearDraft(true); setMode(value); } },
    setFolders, setStartDate, setEndDate,setAdvancedFilterJson,setFolderMappings,setDuplicatePolicy };
}
