import React, { useState, useMemo, useEffect, useRef, useCallback } from 'react';
import { AppState } from '../../state/useAppState';
import { Message } from '../../types';
import {
  ArchiveCatalogItemDto,
  ArchiveMessagePreviewResponse,
  ArchiveScopeTriple,
  ArchiveSearchField,
  ArchiveSearchRequest,
  ArchiveSearchResultItem,
  LocalJobRecord,
  MailFilterDefinition,
} from '../../types/localEngine';
import { LocalEngineClient, localEngineClient } from '../../api/localEngineClient';
import { getMessagesForSourceIds } from '../../data/sampleMessages';
import {
  IconSearch,
  IconMail,
  IconFile,
  IconChevronDown,
  IconChevronRight,
  IconRefreshCw,
  IconAlertCircle,
  IconArchiveBox,
} from '../ui/Icons';
import { Badge } from '../ui/Badge';
import { MessagePreview, MessageLocationContext } from '../workspace/MessagePreview';
import { AddArchiveModal } from '../archive/AddArchiveModal';
import { AdvancedFilterBuilder } from '../filters/AdvancedFilterBuilder';

interface ArchiveSearchViewProps {
  state: AppState;
  client?: LocalEngineClient;
}

export const ArchiveSearchView: React.FC<ArchiveSearchViewProps> = ({
  state,
  client = localEngineClient,
}) => {
  // Mode toggle: Real Local Archive (default) vs Sample Demo Data
  const [mode, setMode] = useState<'real' | 'demo'>('real');

  // Real Mode Catalog State
  const [catalog, setCatalog] = useState<ArchiveCatalogItemDto[]>([]);
  const [isCatalogLoading, setIsCatalogLoading] = useState(false);
  const [catalogError, setCatalogError] = useState<string | null>(null);
  const [folderCache, setFolderCache] = useState<Record<string, string[]>>({});
  const [folderError, setFolderError] = useState<string | null>(null);
  const [trackedJobId, setTrackedJobId] = useState<string | null>(null);
  const [ingestProgress, setIngestProgress] = useState<LocalJobRecord | null>(null);
  const [ingestError, setIngestError] = useState<string | null>(null);

  // Real Mode Scope Multi-Selection State
  const [selectedArchiveIds, setSelectedArchiveIds] = useState<string[]>([]);
  const [isAddModalOpen, setIsAddModalOpen] = useState(false);

  // Real Mode Search Filters
  const [query, setQuery] = useState('');
  const [field, setField] = useState<ArchiveSearchField>('all');
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [attachmentFilter, setAttachmentFilter] = useState<'all' | 'with' | 'without'>('all');
  const [folderFilter, setFolderFilter] = useState<string>('all');
  const [page, setPage] = useState(1);
  const [pageSize] = useState(20);
  const [advancedFilter, setAdvancedFilter] = useState<MailFilterDefinition | null>(null);
  const [advancedFilterUnknownCount, setAdvancedFilterUnknownCount] = useState(0);

  // Real Mode Results & Preview
  const [searchResults, setSearchResults] = useState<ArchiveSearchResultItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [searchWarning, setSearchWarning] = useState<string | null>(null);
  const [isSearching, setIsSearching] = useState(false);
  const [searchError, setSearchError] = useState<string | null>(null);

  const [selectedResultItem, setSelectedResultItem] = useState<ArchiveSearchResultItem | null>(null);
  const [previewData, setPreviewData] = useState<ArchiveMessagePreviewResponse | null>(null);
  const [isPreviewLoading, setIsPreviewLoading] = useState(false);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const [selectedExportIds,setSelectedExportIds]=useState<string[]>([]);
  const [selectedExportJob,setSelectedExportJob]=useState<LocalJobRecord|null>(null);
  const [selectedExportError,setSelectedExportError]=useState<string|null>(null);
  const [governanceStatus,setGovernanceStatus]=useState<string|null>(null);
  const [governanceBusy,setGovernanceBusy]=useState(false);
  const [governanceCompanies,setGovernanceCompanies]=useState<any[]>([]);
  const [governanceCompanyId,setGovernanceCompanyId]=useState('');
  const [governanceProjectId,setGovernanceProjectId]=useState('');
  const [retentionDays,setRetentionDays]=useState(365);
  const [retentionSizeMb,setRetentionSizeMb]=useState('');

  // Generation & Abort Guards
  const generationRef = useRef(0);
  const searchAbortRef = useRef<AbortController | null>(null);
  const previewAbortRef = useRef<AbortController | null>(null);

  // UI States
  const [mobileLocationOpen, setMobileLocationOpen] = useState(true);
  const [expandedCompanies, setExpandedCompanies] = useState<Record<string, boolean>>({});
  const [expandedProjects, setExpandedProjects] = useState<Record<string, boolean>>({});

  // Frozen Job Context (e.g. Navigated from JobCenter)
  const [frozenContextJob, setFrozenContextJob] = useState<LocalJobRecord | null>(null);

  async function startSelectedExport() {
    const chosen = searchResults.filter(x => selectedExportIds.includes(x.messageId));
    if (chosen.length === 0) return;
    const owner = chosen[0];
    if (chosen.some(x => x.companyId !== owner.companyId || x.projectId !== owner.projectId)) {
      setSelectedExportError('Seçili iletiler aynı müşteri ve projeye ait olmalıdır.'); return;
    }
    setSelectedExportError(null);
    try {
      const searchRequest: ArchiveSearchRequest = { selectedScopes, query:query||null, field, startDate:startDate||null, endDate:endDate||null, hasAttachment:attachmentFilter==='all'?null:attachmentFilter==='with', folder:folderFilter==='all'?null:folderFilter, page, pageSize, advancedFilter };
      const folderMappings = [...new Map(chosen.map(x => [`${x.archiveId}:${x.originalFolder}`, { sourceFolderId:`${x.archiveId}:${x.originalFolder}`, targetFolderPath:`${x.archiveName}/${x.originalFolder}` }])).values()];
      const plan = await client.previewArchiveSelected({ searchRequest, selectedMessageIds:selectedExportIds, folderMappings, duplicatePolicy:'PreservePhysical', companyId:owner.companyId, projectId:owner.projectId });
      const output = await client.pickOutputDir(); if (output.cancelled || !output.handle) return;
      const job = await client.startArchiveSelected({ planId:plan.planId, outputDirectoryHandle:output.handle, clientContext:{ companyId:owner.companyId, companyName:owner.companyId, projectId:owner.projectId, projectName:owner.projectId } });
      setSelectedExportJob(job);
    } catch (e) { setSelectedExportError(e instanceof Error ? e.message : 'Seçili sonuç işi başlatılamadı.'); }
  }

  // Demo Mode State
  const [demoSelectedMessage, setDemoSelectedMessage] = useState<Message | null>(null);
  const demoSelectedSourceIds = state.searchSelectedSourceIds;
  const demoSources = state.sources;
  const reportScopeCount = state.setArchiveSearchScopeCount;
  useEffect(() => {
    reportScopeCount?.(mode === 'real' ? selectedArchiveIds.length : 0);
  }, [mode, selectedArchiveIds.length, reportScopeCount]);

  // Auto-switch to real mode and fetch job details if navigated with selectedArchiveJobId
  useEffect(() => {
    let active = true;
    if (state.selectedArchiveJobId) {
      setMode('real');
      client.getJob(state.selectedArchiveJobId)
        .then((job) => {
          if (!active) return;
          setFrozenContextJob(job);
          if (job.status === 'queued' || job.status === 'converting' || job.status === 'verifying') {
            setIngestProgress(job);
            setIngestError(null);
            setTrackedJobId(job.jobId);
          }
          if (job.archiveId) {
            setSelectedArchiveIds((prev) =>
              prev.includes(job.archiveId!) ? prev : [...prev, job.archiveId!]
            );
          }
        })
        .catch(() => {});
    }
    return () => {
      active = false;
    };
  }, [state.selectedArchiveJobId, client]);

  // Open add modal with pre-selected bridge job when navigated via openArchiveSearchAdd
  useEffect(() => {
    if (state.archiveAddInitialJobId) {
      setMode('real');
      setIsAddModalOpen(true);
    }
  }, [state.archiveAddInitialJobId]);

  // Fetch Authoritative Catalog from Backend
  const fetchCatalog = useCallback(async () => {
    setIsCatalogLoading(true);
    setCatalogError(null);
    try {
      const items = await client.getArchiveCatalog();
      setCatalog(items);
      // Auto-expand all companies and projects initially
      const comps: Record<string, boolean> = {};
      const projs: Record<string, boolean> = {};
      for (const item of items) {
        comps[item.companyId] = true;
        projs[item.projectId] = true;
      }
      setExpandedCompanies((prev) => ({ ...comps, ...prev }));
      setExpandedProjects((prev) => ({ ...projs, ...prev }));
      return items;
    } catch (err: any) {
      setCatalogError(err.message || 'Arşiv kataloğu yüklenemedi.');
      return null;
    } finally {
      setIsCatalogLoading(false);
    }
  }, [client]);

  useEffect(() => {
    if (mode === 'real') {
      fetchCatalog();
    }
  }, [mode, fetchCatalog]);

  // Observe the durable job: a successful start is not a published archive.
  useEffect(() => {
    if (!trackedJobId) return;
    let active = true;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const poll = async () => {
      try {
        const job = await client.getJob(trackedJobId);
        if (!active) return;
        setIngestProgress(job);
        if (job.status === 'completed') {
          const items = await fetchCatalog();
          if (!active) return;
          const archive = items?.find((item) => item.archiveId === job.archiveId);
          if (!archive) throw new Error('İş tamamlandı ancak arşiv listesi yenilenemedi. Yeniden kontrol edin.');
          setSelectedArchiveIds([archive.archiveId]);
          setExpandedCompanies((prev) => ({ ...prev, [archive.companyId]: true }));
          setExpandedProjects((prev) => ({ ...prev, [archive.projectId]: true }));
          setTrackedJobId(null);
          return;
        }
        if (job.status === 'failed' || job.status === 'interrupted') {
          setIngestError(job.errorMessage || 'Arşivleme tamamlanmadı. İş merkezinden ayrıntıları kontrol edin.');
          setTrackedJobId(null);
          return;
        }
        timer = setTimeout(poll, 750);
      } catch (err) {
        if (!active) return;
        setIngestError(err instanceof Error ? err.message : 'Arşivleme durumu alınamadı.');
        setTrackedJobId(null);
      }
    };
    void poll();
    return () => { active = false; clearTimeout(timer); };
  }, [trackedJobId, client, fetchCatalog]);

  // Build Real Location Tree Authoritatively from Catalog Snapshots
  const catalogTree = useMemo(() => {
    const compMap = new Map<
      string,
      {
        companyName: string;
        projectMap: Map<string, { projectName: string; archives: ArchiveCatalogItemDto[] }>;
      }
    >();

    for (const item of catalog) {
      const compId = item.companyId;
      const compName =
        item.companyName ||
        state.companies.find((c) => c.id === compId)?.name ||
        compId;
      const projId = item.projectId;
      const projName =
        item.projectName ||
        state.projects.find((p) => p.id === projId)?.name ||
        projId;

      if (!compMap.has(compId)) {
        compMap.set(compId, { companyName: compName, projectMap: new Map() });
      }
      const compEntry = compMap.get(compId)!;
      if (!compEntry.projectMap.has(projId)) {
        compEntry.projectMap.set(projId, { projectName: projName, archives: [] });
      }
      compEntry.projectMap.get(projId)!.archives.push(item);
    }

    const result: Array<{
      companyId: string;
      companyName: string;
      projects: Array<{
        projectId: string;
        projectName: string;
        archives: ArchiveCatalogItemDto[];
      }>;
    }> = [];

    for (const [companyId, { companyName, projectMap }] of compMap.entries()) {
      const projects = [];
      for (const [projectId, { projectName, archives }] of projectMap.entries()) {
        projects.push({ projectId, projectName, archives });
      }
      result.push({ companyId, companyName, projects });
    }
    return result;
  }, [catalog, state.companies, state.projects]);

  // Selected Scope Triples
  const selectedScopes: ArchiveScopeTriple[] = useMemo(() => {
    return catalog
      .filter((a) => selectedArchiveIds.includes(a.archiveId))
      .map((a) => ({
        companyId: a.companyId,
        projectId: a.projectId,
        archiveId: a.archiveId,
      }));
  }, [catalog, selectedArchiveIds]);

  async function runGovernance(action:'backup'|'restore'|'retention'|'audit'|'report') {
    setGovernanceBusy(true);setGovernanceStatus(null);
    try {
      if(action==='backup'){if(!selectedScopes.length)throw new Error('Önce en az bir arşiv seçin.');const out=await client.pickOutputDir();if(out.cancelled||!out.handle)return;const result=await client.createArchiveBackup({scopes:selectedScopes,outputDirectoryHandle:out.handle});setGovernanceStatus(`Yedek doğrulandı: ${result.fileName}`);}
      if(action==='restore'){if(!governanceCompanyId||!governanceProjectId)throw new Error('Geri yükleme için yetkili hedef müşteri ve proje seçin.');const source=await client.pickArchiveBackup();if(source.cancelled||!source.handle)return;const result=await client.restoreArchiveBackup({sourceHandle:source.handle,companyId:governanceCompanyId,projectId:governanceProjectId});setGovernanceStatus(`Geri yükleme tamamlandı: ${result.messageCount} ileti`);await fetchCatalog();}
      if(action==='retention'){if(!governanceCompanyId||!governanceProjectId)throw new Error('Önce hedef kapsam seçin.');const size=retentionSizeMb.trim()===''?null:Number(retentionSizeMb)*1024*1024;const result=await client.previewArchiveRetention({companyId:governanceCompanyId,projectId:governanceProjectId,olderThanDays:retentionDays,largerThanBytes:size});setGovernanceStatus(`Saklama önizlemesi: ${result.candidateCount ?? result.candidates?.length ?? 0} aday; hiçbir arşiv silinmedi.`);}
      if(action==='audit'){const result=await client.getAuditTrail();setGovernanceStatus(result.integrityValid?`Denetim zinciri doğrulandı: ${result.entries.length} kayıt`:`Uyarı: ${result.warning}`);}
      if(action==='report'){if(!frozenContextJob?.jobId)throw new Error('Teslim raporu için bir iş açın.');const result=await client.getDeliveryReport(frozenContextJob.jobId);setGovernanceStatus(`Teslim raporu hazır: ${result.jobId ?? frozenContextJob.jobId}`);}
    } catch(e){setGovernanceStatus(e instanceof Error?e.message:'İşlem başarısız.');} finally {setGovernanceBusy(false);}
  }

  useEffect(()=>{if(mode!=='real')return;client.getCurrentIdentity().then((x:any)=>{const companies=x.companies??[];setGovernanceCompanies(companies);if(!governanceCompanyId&&companies[0]){setGovernanceCompanyId(companies[0].companyId);setGovernanceProjectId(companies[0].projects?.[0]?.projectId??'');}}).catch(()=>{});},[mode,client,governanceCompanyId]);

  // Completed manifests are immutable; cache summaries, scoped to the selection.
  useEffect(() => {
    if (mode !== 'real') return;
    let active = true;
    setFolderError(null);
    const missing = selectedScopes.filter((scope) => !folderCache[scope.archiveId]);
    if (!missing.length) return;
    void Promise.all(missing.map(async ({ archiveId }) => {
      const manifest = await client.getArchiveManifest(archiveId);
      const names: string[] = (manifest.folders || []).map((folder: { folderName: string }) => folder.folderName);
      return [archiveId, names] as const;
    })).then((entries) => {
      if (active) setFolderCache((prev) => ({ ...prev, ...Object.fromEntries(entries) }));
    }).catch((err: unknown) => {
      if (active) setFolderError(err instanceof Error ? err.message : 'Klasörler yüklenemedi.');
    });
    return () => { active = false; };
  }, [mode, selectedScopes, folderCache, client]);

  const availableFolders = useMemo(() => {
    const set = new Set<string>();
    for (const scope of selectedScopes) {
      for (const name of folderCache[scope.archiveId] || []) if (name) set.add(name);
    }
    return Array.from(set).sort();
  }, [selectedScopes, folderCache]);

  useEffect(() => {
    if (selectedScopes.every((scope) => folderCache[scope.archiveId]) &&
        folderFilter !== 'all' && !availableFolders.includes(folderFilter)) {
      setFolderFilter('all');
      setPage(1);
    }
  }, [selectedScopes, folderCache, availableFolders, folderFilter]);

  // Tree selection helpers
  const handleSelectAllReal = () => {
    setSelectedArchiveIds(catalog.map((a) => a.archiveId));
    setPage(1);
  };

  const handleClearSelectionReal = () => {
    setSelectedArchiveIds([]);
    setPage(1);
  };

  const toggleCompanySelectionReal = (compArchiveIds: string[]) => {
    const allSelected = compArchiveIds.every((id) => selectedArchiveIds.includes(id));
    if (allSelected) {
      setSelectedArchiveIds((prev) => prev.filter((id) => !compArchiveIds.includes(id)));
    } else {
      setSelectedArchiveIds((prev) => Array.from(new Set([...prev, ...compArchiveIds])));
    }
    setPage(1);
  };

  const toggleProjectSelectionReal = (projArchiveIds: string[]) => {
    const allSelected = projArchiveIds.every((id) => selectedArchiveIds.includes(id));
    if (allSelected) {
      setSelectedArchiveIds((prev) => prev.filter((id) => !projArchiveIds.includes(id)));
    } else {
      setSelectedArchiveIds((prev) => Array.from(new Set([...prev, ...projArchiveIds])));
    }
    setPage(1);
  };

  const toggleArchiveSelectionReal = (archiveId: string) => {
    setSelectedArchiveIds((prev) =>
      prev.includes(archiveId) ? prev.filter((id) => id !== archiveId) : [...prev, archiveId]
    );
    setPage(1);
  };

  // Reindex actions
  const handleReindexArchive = async (archiveId: string) => {
    try {
      await client.reindexArchive(archiveId, true);
      await fetchCatalog();
    } catch (err: any) {
      alert(err.message || 'Yeniden indeksleme başlatılamadı.');
    }
  };

  const handleReindexAll = async () => {
    try {
      await client.reindexAllArchives();
      await fetchCatalog();
    } catch (err: any) {
      alert(err.message || 'Tüm arşivleri yeniden indeksleme başlatılamadı.');
    }
  };

  // Real Search Execution with Generation Guards & Invalidation
  useEffect(() => {
    if (mode !== 'real') return;

    // Clear preview immediately on any scope or filter change
    setSelectedResultItem(null);
    setPreviewData(null);
    setPreviewError(null);
    setIsPreviewLoading(false);
    if (previewAbortRef.current) {
      previewAbortRef.current.abort();
      previewAbortRef.current = null;
    }

    // Increment generation counter to guard against stale responses
    generationRef.current += 1;
    const currentGen = generationRef.current;

    // Empty scope = 0 results strictly, no API call
    if (selectedScopes.length === 0) {
      setSearchResults([]);
      setTotalCount(0);
      setSearchWarning(null);
      setIsSearching(false);
      return;
    }

    if (searchAbortRef.current) {
      searchAbortRef.current.abort();
    }
    const abortCtrl = new AbortController();
    searchAbortRef.current = abortCtrl;

    setIsSearching(true);
    setSearchError(null);

    const searchReq: ArchiveSearchRequest = {
      selectedScopes,
      query: query.trim() || null,
      field,
      startDate: startDate || null,
      endDate: endDate || null,
      hasAttachment: attachmentFilter === 'with' ? true : attachmentFilter === 'without' ? false : null,
      folder: folderFilter && folderFilter !== 'all' ? folderFilter.trim() : null,
      page,
      pageSize,
      advancedFilter,
    };

    client.searchArchive(searchReq, abortCtrl.signal)
      .then((res) => {
        if (generationRef.current !== currentGen) return; // Discard stale
        setSearchResults(res.items);
        setTotalCount(res.totalCount);
        setSearchWarning(res.warning || null);
        setAdvancedFilterUnknownCount(res.advancedFilterUnknownCount || 0);
        setIsSearching(false);
      })
      .catch((err) => {
        if (generationRef.current !== currentGen) return;
        if (err.name === 'AbortError') return;
        setSearchError(err.message || 'Arama sırasında bir hata oluştu.');
        setIsSearching(false);
      });

    return () => {
      abortCtrl.abort();
    };
  }, [
    mode,
    selectedScopes,
    query,
    field,
    startDate,
    endDate,
    attachmentFilter,
    folderFilter,
    page,
    pageSize,
    advancedFilter,
    client,
  ]);

  // Message Preview Request (Sends MessageId PLUS Full Current SearchRequest)
  const handleSelectResultItem = useCallback(
    (item: ArchiveSearchResultItem) => {
      setSelectedResultItem(item);
      setPreviewData(null);
      setPreviewError(null);
      setIsPreviewLoading(true);

      if (previewAbortRef.current) {
        previewAbortRef.current.abort();
      }
      const abortCtrl = new AbortController();
      previewAbortRef.current = abortCtrl;

      const currentGen = generationRef.current;

      const previewReq = {
        messageId: item.messageId,
        searchRequest: {
          selectedScopes,
          query: query.trim() || null,
          field,
          startDate: startDate || null,
          endDate: endDate || null,
          hasAttachment: attachmentFilter === 'with' ? true : attachmentFilter === 'without' ? false : null,
          folder: folderFilter && folderFilter !== 'all' ? folderFilter.trim() : null,
          page,
          pageSize,
          advancedFilter,
        },
      };

      client.previewArchiveMessage(previewReq, abortCtrl.signal)
        .then((res) => {
          if (generationRef.current !== currentGen) return; // Discard stale
          setPreviewData(res);
          setIsPreviewLoading(false);
        })
        .catch((err) => {
          if (generationRef.current !== currentGen) return;
          if (err.name === 'AbortError') return;
          setPreviewError(err.message || 'İleti önizlemesi alınamadı.');
          setIsPreviewLoading(false);
        });
    },
    [
      selectedScopes,
      query,
      field,
      startDate,
      endDate,
      attachmentFilter,
      folderFilter,
      page,
      pageSize,
      advancedFilter,
      client,
    ]
  );

  // Truncation / Completeness Warning Detection
  const hasTruncatedItems = useMemo(() => {
    const anyInResults = searchResults.some((r) => r.isBodyTruncated);
    const anyInScope = catalog.some(
      (c) => selectedArchiveIds.includes(c.archiveId) && c.truncatedItemsCount > 0
    );
    return anyInResults || anyInScope || Boolean(searchWarning);
  }, [searchResults, catalog, selectedArchiveIds, searchWarning]);

  // Derived Location Context for Preview
  const selectedItemLocationContext: MessageLocationContext | undefined = useMemo(() => {
    if (!selectedResultItem) return undefined;
    const comp = state.companies.find((c) => c.id === selectedResultItem.companyId);
    const proj = state.projects.find((p) => p.id === selectedResultItem.projectId);
    const archive = catalog.find((item) => item.archiveId === selectedResultItem.archiveId);
    const companyName = archive?.companyName || comp?.name || selectedResultItem.companyId;
    const projectName = archive?.projectName || proj?.name || selectedResultItem.projectId;
    const sourceName = selectedResultItem.archiveName;
    const accountOrFile = selectedResultItem.originalFolder;

    return {
      companyName,
      projectName,
      sourceName,
      accountOrFile,
      fullPath: `${companyName} > ${projectName} > ${sourceName} (${accountOrFile})`,
    };
  }, [selectedResultItem, catalog, state.companies, state.projects]);

  // Demo Mode Handlers
  const toggleDemoCompanySelection = (compId: string) => {
    const compSources = demoSources.filter((s) => s.companyId === compId).map((s) => s.id);
    const allSelected = compSources.every((id) => demoSelectedSourceIds.includes(id));
    let nextScope: string[];
    if (allSelected) {
      nextScope = demoSelectedSourceIds.filter((id) => !compSources.includes(id));
    } else {
      const toAdd = compSources.filter((id) => !demoSelectedSourceIds.includes(id));
      nextScope = [...demoSelectedSourceIds, ...toAdd];
    }
    state.updateSearchSelectedSourceIds(nextScope);
  };

  const toggleDemoProjectSelection = (projId: string) => {
    const projSources = demoSources.filter((s) => s.projectId === projId).map((s) => s.id);
    const allSelected = projSources.every((id) => demoSelectedSourceIds.includes(id));
    let nextScope: string[];
    if (allSelected) {
      nextScope = demoSelectedSourceIds.filter((id) => !projSources.includes(id));
    } else {
      const toAdd = projSources.filter((id) => !demoSelectedSourceIds.includes(id));
      nextScope = [...demoSelectedSourceIds, ...toAdd];
    }
    state.updateSearchSelectedSourceIds(nextScope);
  };

  const toggleDemoSourceSelection = (sourceId: string) => {
    let nextScope: string[];
    if (demoSelectedSourceIds.includes(sourceId)) {
      nextScope = demoSelectedSourceIds.filter((id) => id !== sourceId);
    } else {
      nextScope = [...demoSelectedSourceIds, sourceId];
    }
    state.updateSearchSelectedSourceIds(nextScope);
  };

  const demoScopeMessages = useMemo(() => {
    if (demoSelectedSourceIds.length === 0) return [];
    return getMessagesForSourceIds(demoSelectedSourceIds);
  }, [demoSelectedSourceIds]);

  const demoSearchResults = useMemo(() => {
    if (demoSelectedSourceIds.length === 0) return [];
    return demoScopeMessages.filter((msg) => {
      if (folderFilter !== 'all' && msg.folderId !== folderFilter) return false;
      if (!query.trim()) return true;
      const q = query.toLowerCase();
      return (
        msg.subject.toLowerCase().includes(q) ||
        msg.sender.toLowerCase().includes(q) ||
        msg.body.toLowerCase().includes(q) ||
        (msg.attachmentName && msg.attachmentName.toLowerCase().includes(q))
      );
    });
  }, [demoScopeMessages, demoSelectedSourceIds, folderFilter, query]);

  useEffect(() => {
    if (mode === 'demo' && demoSelectedMessage) {
      const stillInScope = demoSearchResults.some((m) => m.id === demoSelectedMessage.id);
      if (!stillInScope) {
        setDemoSelectedMessage(null);
      }
    }
  }, [mode, demoSearchResults, demoSelectedMessage]);

  const getDemoMessageLocation = (msg: Message) => {
    const source = state.sources.find((s) => s.id === msg.sourceId);
    const company = state.companies.find((c) => c.id === (msg.companyId || source?.companyId));
    const project = state.projects.find((p) => p.id === source?.projectId);
    const companyName = company?.name || 'Örnek Şirket';
    const projectName = project?.name || 'Sistem Geçişi';
    const sourceName = source?.name || 'Posta Kaynağı';
    const accountOrFile = source?.accountOrFileName || msg.senderEmail;
    return {
      companyName,
      projectName,
      sourceName,
      accountOrFile,
      fullPath: `${companyName} > ${projectName} > ${sourceName} (${accountOrFile})`,
    };
  };

  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));

  return (
    <div
      style={{ display: 'flex', flexDirection: 'column', flex: 1, height: '100%', minWidth: 0, overflowX: 'hidden' }}
      data-testid="archive-search-view"
    >
      {/* 1. Subheader & Mode Toggle */}
      <div className="page-subheader" style={{ flexWrap: 'wrap', gap: '12px' }}>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap' }}>
            <h1 className="page-title" style={{ margin: 0 }}>Arşiv ve arama</h1>
            <div style={{ display: 'flex', gap: '4px', background: 'var(--bg-subtle)', padding: '2px', borderRadius: '6px' }}>
              <button
                type="button"
                className={`btn ${mode === 'real' ? 'btn-primary-orange' : 'btn-outline-gray'}`}
                style={{ padding: '3px 10px', fontSize: '0.75rem', fontWeight: 600 }}
                onClick={() => setMode('real')}
                data-testid="archive-mode-real-btn"
              >
                Gerçek Yerel Arşiv
              </button>
              <button
                type="button"
                className={`btn ${mode === 'demo' ? 'btn-primary-orange' : 'btn-outline-gray'}`}
                style={{ padding: '3px 10px', fontSize: '0.75rem', fontWeight: 600 }}
                onClick={() => setMode('demo')}
                data-testid="archive-mode-demo-btn"
              >
                Örnek Veri (Demo)
              </button>
            </div>
          </div>
          <p className="page-subtitle">
            {mode === 'real'
              ? 'Şirketlerinize ait arşivlerde birlikte arama yapın.'
              : 'Örnek simülasyon verileri görüntüleniyor · Gerçek verilerle karışmaz'}
          </p>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
          {mode === 'real' && (
            <button
              type="button"
              className="btn btn-primary-orange"
              style={{ padding: '6px 12px', fontSize: '0.8125rem' }}
              onClick={() => setIsAddModalOpen(true)}
              data-testid="archive-open-add-modal-btn"
            >
              <IconArchiveBox size={14} />
              <span>Yeni Arşiv Ekle</span>
            </button>
          )}

          <button
            type="button"
            className="btn btn-outline-gray mobile-location-toggle"
            onClick={() => setMobileLocationOpen((prev) => !prev)}
            data-testid="mobile-location-toggle-btn"
            aria-expanded={mobileLocationOpen}
          >
            <span>{mobileLocationOpen ? '▲ Konumları Gizle' : '▼ Konumları Göster'}</span>
          </button>

          <span
            style={{
              fontSize: '0.8125rem',
              color: (mode === 'real' ? selectedArchiveIds.length : demoSelectedSourceIds.length) > 0 ? '#1e293b' : 'var(--text-muted)',
              background: (mode === 'real' ? selectedArchiveIds.length : demoSelectedSourceIds.length) > 0 ? 'var(--brand-yellow-light)' : 'var(--bg-subtle)',
              padding: '4px 10px',
              borderRadius: '4px',
              fontWeight: 600,
            }}
            data-testid="search-scope-summary-badge"
          >
            {mode === 'real' ? (
              selectedArchiveIds.length === 0
                ? 'Konum seçilmedi'
                : `${selectedArchiveIds.length} arşiv seçili`
            ) : (
              demoSelectedSourceIds.length === 0
                ? 'Konum seçilmedi'
                : `${demoSelectedSourceIds.length} kaynak seçili`
            )}
          </span>
        </div>
      </div>

      {mode === 'real' && (
        <section data-testid="archive-governance-panel" style={{margin:'0 16px 8px',padding:'10px 12px',border:'1px solid var(--border-color)',borderRadius:6,background:'var(--bg-surface)'}}>
          <div style={{display:'flex',alignItems:'center',gap:8,flexWrap:'wrap'}}>
            <strong>Arşiv güvenliği</strong>
            <select aria-label="Yedek hedef müşterisi" value={governanceCompanyId} onChange={e=>{setGovernanceCompanyId(e.target.value);setGovernanceProjectId(governanceCompanies.find(c=>c.companyId===e.target.value)?.projects?.[0]?.projectId??'');}}><option value="">Müşteri seçin</option>{governanceCompanies.map(c=><option key={c.companyId} value={c.companyId}>{c.name}</option>)}</select>
            <select aria-label="Yedek hedef projesi" value={governanceProjectId} onChange={e=>setGovernanceProjectId(e.target.value)}><option value="">Proje seçin</option>{governanceCompanies.find(c=>c.companyId===governanceCompanyId)?.projects?.map((p:any)=><option key={p.projectId} value={p.projectId}>{p.name}</option>)}</select>
            <button className="btn btn-outline-gray" disabled={governanceBusy} onClick={()=>void runGovernance('backup')}>Doğrulanmış yedek oluştur</button>
            <button className="btn btn-outline-gray" disabled={governanceBusy} onClick={()=>void runGovernance('restore')}>Yedeği yeni arşive geri yükle</button>
            <button className="btn btn-outline-gray" disabled={governanceBusy} onClick={()=>void runGovernance('retention')}>Saklama önizlemesi</button>
            <button className="btn btn-outline-gray" disabled={governanceBusy} onClick={()=>void runGovernance('audit')}>Denetim bütünlüğünü kontrol et</button>
            <button className="btn btn-outline-gray" disabled={governanceBusy||!frozenContextJob} onClick={()=>void runGovernance('report')}>Teslim raporu</button>
          </div>
          <div style={{display:'flex',gap:8,alignItems:'center',marginTop:6,flexWrap:'wrap'}}><label>Saklama yaşı (gün) <input aria-label="Saklama yaşı gün" type="number" min={1} max={36500} value={retentionDays} onChange={e=>setRetentionDays(Number(e.target.value))}/></label><label>En az boyut (MB, isteğe bağlı) <input aria-label="Saklama boyutu MB" type="number" min={0} value={retentionSizeMb} onChange={e=>setRetentionSizeMb(e.target.value)}/></label></div>
          <small style={{display:'block',marginTop:6,color:'var(--text-muted)'}}>Geri yükleme yeni kimliklerle yapılır; mevcut arşivler korunur. Hesaplar ve kimlik bilgileri yedeğe dahil edilmez. Saklama işlemi yalnızca önizlemedir.</small>
          {governanceStatus && <div role="status" style={{marginTop:6}}>{governanceStatus}</div>}
        </section>
      )}

      {mode === 'real' && ingestProgress && (
        <div role={ingestError ? 'alert' : 'status'} data-testid="archive-ingest-progress"
          style={{ margin: '0 16px 8px', padding: '10px', background: '#fff7ed', overflowWrap: 'anywhere' }}>
          <strong>{ingestProgress.archiveName || 'Arşivleme'}: </strong>
          {ingestError || (trackedJobId
            ? `${ingestProgress.stage || 'İşleniyor'} · ${ingestProgress.itemsWritten || 0}/${ingestProgress.totalItems || 0} ileti`
            : 'Arşiv hazır.')}
          {ingestError && <button className="btn-outline-gray" style={{ marginLeft: '8px' }} onClick={() => {
            setIngestError(null);
            setTrackedJobId(ingestProgress.jobId);
          }}>Yeniden kontrol et</button>}
        </div>
      )}

      {/* 2. Frozen Context Banner from JobCenter */}
      {mode === 'real' && frozenContextJob && (
        <div
          style={{
            margin: '0 16px 8px 16px',
            padding: '8px 14px',
            background: '#ecfdf5',
            border: '1px solid #a7f3d0',
            borderRadius: '6px',
            color: '#065f46',
            fontSize: '0.8125rem',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            flexWrap: 'wrap',
            gap: '8px',
          }}
          data-testid="archive-frozen-context"
        >
          <div>
            <strong>İş Bağlamı:</strong> {frozenContextJob.clientContext.companyName} &gt; {frozenContextJob.clientContext.projectName}
            {frozenContextJob.archiveName && ` &gt; ${frozenContextJob.archiveName}`}
            <span style={{ color: '#047857', marginLeft: '6px', fontSize: '0.75rem' }}>
              (İş No: {frozenContextJob.jobId})
            </span>
          </div>
          <button
            type="button"
            className="btn-outline-gray"
            style={{ padding: '2px 8px', fontSize: '0.75rem' }}
            onClick={() => {
              setFrozenContextJob(null);
              state.setSelectedArchiveJobId(null);
            }}
          >
            Bağlamı Kapat
          </button>
        </div>
      )}

      {/* 3. Truncation / Search Completeness Warning */}
      {mode === 'real' && hasTruncatedItems && (
        <div
          style={{
            margin: '0 16px 8px 16px',
            padding: '8px 14px',
            background: '#fffbeb',
            border: '1px solid #fde68a',
            borderRadius: '6px',
            color: '#92400e',
            fontSize: '0.8125rem',
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
          }}
          data-testid="archive-truncation-warning"
        >
          <IconAlertCircle size={16} color="#d97706" style={{ flexShrink: 0 }} />
          <span>
            <strong>Arama Tamlığı Uyarısı:</strong>{' '}
            {searchWarning ||
              'Seçili arama kapsamında 512 KiB gövde sınırını aşan iletiler bulunmaktadır. Bu iletilerin gövdesi kısaltılarak indekslenmiştir; tüm içerik aranamamış olabilir.'}
          </span>
        </div>
      )}

      {/* 4. Main 2-Panel Layout */}
      <div className="archive-search-layout" style={{ minWidth: 0 }}>
        {/* Left Panel: Location Tree */}
        <aside
          className={`search-location-panel ${!mobileLocationOpen ? 'mobile-hidden' : ''}`}
          data-testid="search-location-tree-panel"
        >
          <div
            style={{
              padding: '10px 16px',
              borderBottom: '1px solid var(--border-light)',
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
              flexWrap: 'wrap',
              gap: '6px',
            }}
          >
            <span style={{ fontSize: '0.875rem', fontWeight: 700, color: 'var(--text-main)' }}>
              {mode === 'real' ? 'Arşiv Konumları' : 'Örnek Konumlar'}
            </span>
            <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
              <button
                type="button"
                className="btn-outline-gray"
                style={{ padding: '2px 8px', fontSize: '0.75rem', borderRadius: '4px', cursor: 'pointer' }}
                onClick={mode === 'real' ? handleSelectAllReal : () => state.updateSearchSelectedSourceIds(demoSources.map((s) => s.id))}
                data-testid="search-select-all-btn"
              >
                Tümünü seç
              </button>
              <button
                type="button"
                className="btn-outline-gray"
                style={{ padding: '2px 8px', fontSize: '0.75rem', borderRadius: '4px', cursor: 'pointer' }}
                onClick={mode === 'real' ? handleClearSelectionReal : () => state.updateSearchSelectedSourceIds([])}
                data-testid="search-clear-selection-btn"
              >
                Temizle
              </button>
              {mode === 'real' && (
                <button
                  type="button"
                  className="btn-outline-gray"
                  style={{ padding: '2px 6px', fontSize: '0.75rem', borderRadius: '4px', cursor: 'pointer' }}
                  onClick={handleReindexAll}
                  title="Tüm Arşivleri Yeniden İndeksle"
                  data-testid="reindex-all-btn"
                >
                  <IconRefreshCw size={12} />
                </button>
              )}
            </div>
          </div>

          <div style={{ padding: '10px 8px', overflowY: 'auto', flex: 1, minWidth: 0 }}>
            {mode === 'real' ? (
              // REAL MODE AUTHORITATIVE TREE
              isCatalogLoading && catalog.length === 0 ? (
                <div style={{ padding: '16px', textAlign: 'center', color: 'var(--text-muted)', fontSize: '0.8125rem' }}>
                  Arşiv kataloğu taranıyor...
                </div>
              ) : catalogError ? (
                <div style={{ padding: '16px', color: '#991b1b', fontSize: '0.8125rem' }}>
                  Hata: {catalogError}
                </div>
              ) : catalogTree.length === 0 ? (
                <div style={{ padding: '24px 16px', textAlign: 'center', color: 'var(--text-muted)' }}>
                  <p style={{ fontSize: '0.8125rem', marginBottom: '8px' }}>Henüz yerel arşiv eklenmemiş.</p>
                  <button
                    type="button"
                    className="btn btn-outline-orange"
                    style={{ fontSize: '0.75rem', padding: '4px 8px' }}
                    onClick={() => setIsAddModalOpen(true)}
                  >
                    Yeni Arşiv Ekle
                  </button>
                </div>
              ) : (
                catalogTree.map((comp) => {
                  const compArchiveIds = comp.projects.flatMap((p) => p.archives.map((a) => a.archiveId));
                  const selectedInComp = compArchiveIds.filter((id) => selectedArchiveIds.includes(id));
                  const isCompAllSelected = compArchiveIds.length > 0 && selectedInComp.length === compArchiveIds.length;
                  const isCompIndeterminate = selectedInComp.length > 0 && selectedInComp.length < compArchiveIds.length;
                  const isExpanded = expandedCompanies[comp.companyId] ?? true;

                  return (
                    <div key={comp.companyId} style={{ marginBottom: '8px' }} data-testid={`tree-company-${comp.companyId}`}>
                      {/* Company Row */}
                      <div
                        style={{
                          display: 'flex',
                          alignItems: 'center',
                          padding: '4px 6px',
                          borderRadius: '4px',
                          cursor: 'pointer',
                          gap: '6px',
                          userSelect: 'none',
                        }}
                      >
                        <span
                          onClick={() => setExpandedCompanies((prev) => ({ ...prev, [comp.companyId]: !(prev[comp.companyId] ?? true) }))}
                          data-testid={`tree-company-toggle-${comp.companyId}`}
                        >
                          {isExpanded ? <IconChevronDown size={14} color="#64748b" /> : <IconChevronRight size={14} color="#64748b" />}
                        </span>

                        <input
                          type="checkbox"
                          checked={isCompAllSelected}
                          ref={(el) => {
                            if (el) el.indeterminate = isCompIndeterminate;
                          }}
                          onChange={() => toggleCompanySelectionReal(compArchiveIds)}
                          data-testid={`checkbox-company-${comp.companyId}`}
                          style={{ cursor: 'pointer' }}
                        />

                        <span
                          onClick={() => setExpandedCompanies((prev) => ({ ...prev, [comp.companyId]: !(prev[comp.companyId] ?? true) }))}
                          style={{ fontWeight: 700, fontSize: '0.8125rem', color: 'var(--text-main)', flex: 1, overflowWrap: 'anywhere' }}
                        >
                          {comp.companyName}
                        </span>
                      </div>

                      {/* Projects */}
                      {isExpanded && (
                        <div style={{ paddingLeft: '18px' }} data-testid={`tree-company-projects-${comp.companyId}`}>
                          {comp.projects.map((proj) => {
                            const projArchiveIds = proj.archives.map((a) => a.archiveId);
                            const selectedInProj = projArchiveIds.filter((id) => selectedArchiveIds.includes(id));
                            const isProjAllSelected = projArchiveIds.length > 0 && selectedInProj.length === projArchiveIds.length;
                            const isProjIndeterminate = selectedInProj.length > 0 && selectedInProj.length < projArchiveIds.length;
                            const isProjExpanded = expandedProjects[proj.projectId] ?? true;

                            return (
                              <div key={proj.projectId} style={{ marginTop: '4px' }} data-testid={`tree-project-${proj.projectId}`}>
                                {/* Project Row */}
                                <div
                                  style={{
                                    display: 'flex',
                                    alignItems: 'center',
                                    padding: '3px 4px',
                                    borderRadius: '4px',
                                    cursor: 'pointer',
                                    gap: '6px',
                                    userSelect: 'none',
                                  }}
                                >
                                  <span
                                    onClick={() => setExpandedProjects((prev) => ({ ...prev, [proj.projectId]: !(prev[proj.projectId] ?? true) }))}
                                    data-testid={`tree-project-toggle-${proj.projectId}`}
                                  >
                                    {isProjExpanded ? <IconChevronDown size={13} color="#94a3b8" /> : <IconChevronRight size={13} color="#94a3b8" />}
                                  </span>

                                  <input
                                    type="checkbox"
                                    checked={isProjAllSelected}
                                    ref={(el) => {
                                      if (el) el.indeterminate = isProjIndeterminate;
                                    }}
                                    onChange={() => toggleProjectSelectionReal(projArchiveIds)}
                                    data-testid={`checkbox-project-${proj.projectId}`}
                                    style={{ cursor: 'pointer' }}
                                  />

                                  <span
                                    onClick={() => setExpandedProjects((prev) => ({ ...prev, [proj.projectId]: !(prev[proj.projectId] ?? true) }))}
                                    style={{ fontWeight: 600, fontSize: '0.8125rem', color: '#334155', flex: 1, overflowWrap: 'anywhere' }}
                                  >
                                    {proj.projectName}
                                  </span>
                                </div>

                                {/* Archives */}
                                {isProjExpanded && (
                                  <div style={{ paddingLeft: '20px' }}>
                                    {proj.archives.map((arch) => {
                                      const isSelected = selectedArchiveIds.includes(arch.archiveId);
                                      return (
                                        <div
                                          key={arch.archiveId}
                                          style={{
                                            display: 'flex',
                                            alignItems: 'center',
                                            justifyContent: 'space-between',
                                            padding: '4px 6px',
                                            gap: '6px',
                                            fontSize: '0.8125rem',
                                            borderRadius: '4px',
                                            cursor: 'pointer',
                                            background: isSelected ? 'var(--brand-yellow-light)' : 'transparent',
                                          }}
                                          onClick={() => toggleArchiveSelectionReal(arch.archiveId)}
                                          data-testid={`tree-archive-${arch.archiveId}`}
                                        >
                                          <div style={{ display: 'flex', alignItems: 'center', gap: '6px', minWidth: 0, flex: 1 }}>
                                            <input
                                              type="checkbox"
                                              checked={isSelected}
                                              onChange={() => toggleArchiveSelectionReal(arch.archiveId)}
                                              onClick={(e) => e.stopPropagation()}
                                              data-testid={`checkbox-archive-${arch.archiveId}`}
                                              style={{ cursor: 'pointer' }}
                                            />
                                            <IconArchiveBox size={14} color="var(--brand-orange)" style={{ flexShrink: 0 }} />
                                            <span
                                              style={{
                                                color: isSelected ? '#78350f' : 'var(--text-main)',
                                                fontWeight: isSelected ? 600 : 400,
                                                overflowWrap: 'anywhere',
                                              }}
                                            >
                                              {arch.archiveName}
                                            </span>
                                          </div>

                                          <div style={{ display: 'flex', alignItems: 'center', gap: '4px', flexShrink: 0 }}>
                                            {arch.status === 'ready' && (
                                              <Badge variant="success" data-testid={`archive-status-${arch.archiveId}`}>
                                                Hazır
                                              </Badge>
                                            )}
                                            {arch.status === 'indexing' && (
                                              <Badge variant="warning" data-testid={`archive-status-${arch.archiveId}`}>
                                                İndeksleniyor
                                              </Badge>
                                            )}
                                            {arch.status === 'index_failed' && (
                                              <>
                                                <Badge variant="error" data-testid={`archive-status-${arch.archiveId}`}>
                                                  İndeks Hatası
                                                </Badge>
                                                <button
                                                  type="button"
                                                  className="btn-outline-gray"
                                                  style={{ padding: '1px 6px', fontSize: '0.6875rem' }}
                                                  onClick={(e) => {
                                                    e.stopPropagation();
                                                    handleReindexArchive(arch.archiveId);
                                                  }}
                                                  data-testid={`reindex-archive-btn-${arch.archiveId}`}
                                                  title="Yeniden İndeksle"
                                                >
                                                  Onar
                                                </button>
                                              </>
                                            )}
                                            {arch.status === 'corrupted' && (
                                              <>
                                                <Badge variant="error" data-testid={`archive-status-${arch.archiveId}`}>
                                                  Bozuk İndeks
                                                </Badge>
                                                <button
                                                  type="button"
                                                  className="btn-outline-gray"
                                                  style={{ padding: '1px 6px', fontSize: '0.6875rem' }}
                                                  onClick={(e) => {
                                                    e.stopPropagation();
                                                    handleReindexArchive(arch.archiveId);
                                                  }}
                                                  data-testid={`reindex-archive-btn-${arch.archiveId}`}
                                                  title="İndeksi Onar"
                                                >
                                                  Onar
                                                </button>
                                              </>
                                            )}
                                          </div>
                                        </div>
                                      );
                                    })}
                                  </div>
                                )}
                              </div>
                            );
                          })}
                        </div>
                      )}
                    </div>
                  );
                })
              )
            ) : (
              // DEMO MODE TREE
              state.companies.map((comp) => {
                const compProjects = state.projects.filter((p) => p.companyId === comp.id);
                const compSources = demoSources.filter((s) => s.companyId === comp.id);
                const compSourceIds = compSources.map((s) => s.id);
                const selectedInComp = compSourceIds.filter((id) => demoSelectedSourceIds.includes(id));
                const isCompAllSelected = compSourceIds.length > 0 && selectedInComp.length === compSourceIds.length;
                const isCompIndeterminate = selectedInComp.length > 0 && selectedInComp.length < compSourceIds.length;
                const isExpanded = expandedCompanies[comp.id] ?? true;

                return (
                  <div key={comp.id} style={{ marginBottom: '8px' }} data-testid={`tree-company-${comp.id}`}>
                    <div
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        padding: '4px 6px',
                        borderRadius: '4px',
                        cursor: 'pointer',
                        gap: '6px',
                        userSelect: 'none',
                      }}
                    >
                      <span onClick={() => setExpandedCompanies((prev) => ({ ...prev, [comp.id]: !(prev[comp.id] ?? true) }))}>
                        {isExpanded ? <IconChevronDown size={14} color="#64748b" /> : <IconChevronRight size={14} color="#64748b" />}
                      </span>
                      <input
                        type="checkbox"
                        checked={isCompAllSelected}
                        ref={(el) => {
                          if (el) el.indeterminate = isCompIndeterminate;
                        }}
                        onChange={() => toggleDemoCompanySelection(comp.id)}
                        data-testid={`checkbox-company-${comp.id}`}
                        style={{ cursor: 'pointer' }}
                      />
                      <span
                        onClick={() => setExpandedCompanies((prev) => ({ ...prev, [comp.id]: !(prev[comp.id] ?? true) }))}
                        style={{ fontWeight: 700, fontSize: '0.8125rem', color: 'var(--text-main)', flex: 1 }}
                      >
                        {comp.name}
                      </span>
                    </div>

                    {isExpanded && (
                      <div style={{ paddingLeft: '20px' }}>
                        {compProjects.map((proj) => {
                          const projSources = demoSources.filter((s) => s.projectId === proj.id);
                          const projSourceIds = projSources.map((s) => s.id);
                          const selectedInProj = projSourceIds.filter((id) => demoSelectedSourceIds.includes(id));
                          const isProjAllSelected = projSourceIds.length > 0 && selectedInProj.length === projSourceIds.length;
                          const isProjIndeterminate = selectedInProj.length > 0 && selectedInProj.length < projSourceIds.length;
                          const isProjExpanded = expandedProjects[proj.id] ?? true;

                          return (
                            <div key={proj.id} style={{ marginTop: '4px' }} data-testid={`tree-project-${proj.id}`}>
                              <div
                                style={{
                                  display: 'flex',
                                  alignItems: 'center',
                                  padding: '3px 4px',
                                  borderRadius: '4px',
                                  cursor: 'pointer',
                                  gap: '6px',
                                  userSelect: 'none',
                                }}
                              >
                                <span onClick={() => setExpandedProjects((prev) => ({ ...prev, [proj.id]: !(prev[proj.id] ?? true) }))}>
                                  {isProjExpanded ? <IconChevronDown size={13} color="#94a3b8" /> : <IconChevronRight size={13} color="#94a3b8" />}
                                </span>
                                <input
                                  type="checkbox"
                                  checked={isProjAllSelected}
                                  ref={(el) => {
                                    if (el) el.indeterminate = isProjIndeterminate;
                                  }}
                                  onChange={() => toggleDemoProjectSelection(proj.id)}
                                  data-testid={`checkbox-project-${proj.id}`}
                                  style={{ cursor: 'pointer' }}
                                />
                                <span
                                  onClick={() => setExpandedProjects((prev) => ({ ...prev, [proj.id]: !(prev[proj.id] ?? true) }))}
                                  style={{ fontWeight: 600, fontSize: '0.8125rem', color: '#334155', flex: 1 }}
                                >
                                  {proj.name}
                                </span>
                              </div>

                              {isProjExpanded && (
                                <div style={{ paddingLeft: '22px' }}>
                                  {projSources.map((src) => {
                                    const isSelected = demoSelectedSourceIds.includes(src.id);
                                    return (
                                      <div
                                        key={src.id}
                                        style={{
                                          display: 'flex',
                                          alignItems: 'center',
                                          padding: '3px 4px',
                                          gap: '6px',
                                          fontSize: '0.8125rem',
                                          borderRadius: '4px',
                                          cursor: 'pointer',
                                          background: isSelected ? 'var(--brand-yellow-light)' : 'transparent',
                                        }}
                                        onClick={() => toggleDemoSourceSelection(src.id)}
                                        data-testid={`tree-source-${src.id}`}
                                      >
                                        <input
                                          type="checkbox"
                                          checked={isSelected}
                                          onChange={() => toggleDemoSourceSelection(src.id)}
                                          onClick={(e) => e.stopPropagation()}
                                          data-testid={`checkbox-source-${src.id}`}
                                          style={{ cursor: 'pointer' }}
                                        />
                                        {src.kind === 'mailbox' ? (
                                          <IconMail size={14} color="var(--brand-orange)" />
                                        ) : (
                                          <IconFile size={14} color="#d97706" />
                                        )}
                                        <span style={{ flex: 1, color: isSelected ? '#78350f' : 'var(--text-main)' }}>
                                          {src.name}
                                        </span>
                                      </div>
                                    );
                                  })}
                                </div>
                              )}
                            </div>
                          );
                        })}
                      </div>
                    )}
                  </div>
                );
              })
            )}
          </div>
        </aside>

        {/* Right Panel: Search Controls, Results & Preview */}
        <div className="search-content-grid" style={{ minWidth: 0 }}>
          {/* Results Table Section */}
          <div className="search-results-pane" style={{ minWidth: 0 }}>
            {/* Search Controls Bar */}
            <div className="search-controls-bar" style={{ flexWrap: 'wrap', gap: '8px' }}>
              <div className="search-input-wrapper" style={{ flex: '1 1 200px', minWidth: 0 }}>
                <IconSearch size={16} className="search-input-icon" />
                <input
                  type="text"
                  className="text-input"
                  placeholder="Aranacak sözcükleri yazın..."
                  value={query}
                  onChange={(e) => {
                    setQuery(e.target.value);
                    setPage(1);
                  }}
                  data-testid="archive-search-input"
                />
              </div>

              {mode === 'real' && (
                <>
                  <select
                    className="select-input"
                    style={{ width: 'auto', flex: '0 0 auto' }}
                    value={field}
                    onChange={(e) => {
                      setField(e.target.value as ArchiveSearchField);
                      setPage(1);
                    }}
                    data-testid="archive-search-field-select"
                  >
                    <option value="all">Tüm Alanlar</option>
                    <option value="subject">Konu</option>
                    <option value="sender">Gönderen</option>
                    <option value="recipient">Alıcı</option>
                    <option value="body">Gövde</option>
                    <option value="attachmentName">Ek Adı</option>
                  </select>

                  <div style={{ display: 'flex', alignItems: 'center', gap: '4px', flexWrap: 'wrap' }}>
                    <input
                      type="date"
                      className="text-input"
                      style={{ width: 'auto', padding: '4px 8px', fontSize: '0.8125rem' }}
                      value={startDate}
                      onChange={(e) => {
                        setStartDate(e.target.value);
                        setPage(1);
                      }}
                      title="Başlangıç Tarihi (UTC+03 Takvim Günü)"
                      data-testid="archive-search-start-date"
                    />
                    <span style={{ color: 'var(--text-muted)', fontSize: '0.8125rem' }}>—</span>
                    <input
                      type="date"
                      className="text-input"
                      style={{ width: 'auto', padding: '4px 8px', fontSize: '0.8125rem' }}
                      value={endDate}
                      onChange={(e) => {
                        setEndDate(e.target.value);
                        setPage(1);
                      }}
                      title="Bitiş Tarihi (UTC+03 Takvim Günü)"
                      data-testid="archive-search-end-date"
                    />
                  </div>

                  <select
                    className="select-input"
                    style={{ width: 'auto', flex: '0 0 auto' }}
                    value={attachmentFilter}
                    onChange={(e) => {
                      setAttachmentFilter(e.target.value as any);
                      setPage(1);
                    }}
                    data-testid="archive-search-attachment-filter"
                  >
                    <option value="all">Tümü (Ekli / Eksiz)</option>
                    <option value="with">Yalnızca Ekli</option>
                    <option value="without">Yalnızca Eksiz</option>
                  </select>
                </>
              )}

              <select
                className="select-input"
                style={{ width: 'auto', flex: '0 0 auto' }}
                value={folderFilter}
                onChange={(e) => {
                  setFolderFilter(e.target.value);
                  setPage(1);
                }}
                data-testid="archive-folder-filter"
              >
                <option value="all">Tüm Klasörler</option>
                {availableFolders
                  .map((f) => (
                    <option key={f} value={f}>
                      {f}
                    </option>
                  ))}
                {folderFilter &&
                  !['all', ...availableFolders].includes(folderFilter) && (
                    <option value={folderFilter}>{folderFilter}</option>
                  )}
              </select>
              <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Tarihler Türkiye saatiyle (UTC+03)</span>
              {folderError && <span role="alert">Klasörler yüklenemedi. Konum seçimini yenileyin.</span>}
            </div>

            {mode === 'real' && <div style={{padding:'8px 16px'}}><AdvancedFilterBuilder testId="archive-advanced-filter" value={advancedFilter} onChange={value=>{setAdvancedFilter(value);setPage(1);setSelectedExportIds([]);}} unknownCount={advancedFilterUnknownCount} /></div>}

            {/* Status & Paging Bar */}
            <div
              style={{
                padding: '6px 16px',
                background: 'var(--bg-subtle)',
                fontSize: '0.8125rem',
                color: 'var(--text-muted)',
                borderBottom: '1px solid var(--border-light)',
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                flexWrap: 'wrap',
                gap: '8px',
              }}
              data-testid="search-status-bar"
            >
              <div>
                {mode === 'real' ? (
                  selectedScopes.length === 0 ? (
                    <span>Konum seçilmedi · 0 sonuç</span>
                  ) : isSearching ? (
                    <span style={{ color: 'var(--brand-orange)' }}>Aranıyor...</span>
                  ) : (
                    <span>{totalCount} sonuç bulundu</span>
                  )
                ) : (
                  demoSelectedSourceIds.length === 0 ? (
                    <span>Konum seçilmedi · 0 sonuç</span>
                  ) : (
                    <span>{demoSearchResults.length} sonuç bulundu</span>
                  )
                )}
                {searchError && (
                  <span style={{ color: '#dc2626', marginLeft: '8px' }}>({searchError})</span>
                )}
              </div>

              {mode === 'real' && selectedScopes.length > 0 && totalCount > 0 && (
                <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                  <span>
                    Sayfa {page} / {totalPages}
                  </span>
                  <button
                    type="button"
                    className="btn-outline-gray"
                    style={{ padding: '2px 8px', fontSize: '0.75rem' }}
                    disabled={page <= 1 || isSearching}
                    onClick={() => setPage((p) => Math.max(1, p - 1))}
                    data-testid="search-prev-page-btn"
                  >
                    Önceki
                  </button>
                  <button
                    type="button"
                    className="btn-outline-gray"
                    style={{ padding: '2px 8px', fontSize: '0.75rem' }}
                    disabled={page >= totalPages || isSearching}
                    onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                    data-testid="search-next-page-btn"
                  >
                    Sonraki
                  </button>
                </div>
              )}
            </div>

            {/* Results Table or Empty Prompt */}
            {mode==='real'&&searchResults.length>0&&<div style={{padding:'8px',display:'flex',gap:'8px',alignItems:'center'}}><button className="btn btn-orange" data-testid="archive-selected-export-start" disabled={selectedExportIds.length===0} onClick={()=>void startSelectedExport()}>Seçilenleri yeni EML çıktısına aktar ({selectedExportIds.length})</button>{selectedExportJob&&<span data-testid="archive-selected-export-job">İş: {selectedExportJob.jobId} · {selectedExportJob.status}</span>}{selectedExportError&&<span role="alert">{selectedExportError}</span>}</div>}
            <div className="table-container" style={{ flex: 1, overflowY: 'auto', overflowX: 'auto' }}>
              {mode === 'real' ? (
                selectedScopes.length === 0 ? (
                  <div style={{ padding: '48px 24px', textAlign: 'center', color: 'var(--text-muted)' }} data-testid="search-empty-scope-prompt">
                    <IconSearch size={36} color="#94a3b8" />
                    <h3 style={{ fontSize: '1rem', fontWeight: 600, margin: '12px 0 6px 0', color: 'var(--text-main)' }}>
                      Konum seçilmedi
                    </h3>
                    <p style={{ fontSize: '0.875rem', maxWidth: '400px', margin: '0 auto' }}>
                      Arama yapmak için lütfen soldaki ağaçtan en az bir şirket, proje veya arşiv konumu seçin.
                    </p>
                  </div>
                ) : searchResults.length === 0 && !isSearching ? (
                  <div style={{ padding: '48px 24px', textAlign: 'center', color: 'var(--text-muted)' }} data-testid="search-empty-results">
                    <p style={{ fontSize: '0.875rem' }}>Seçili konumlarda arama kriterine uygun ileti bulunamadı.</p>
                  </div>
                ) : (
                  <table className="data-table" data-testid="search-results-table">
                    <thead>
                      <tr>
                        <th style={{width:'4%'}}>Seç</th>
                        <th style={{ width: '25%' }}>Gönderen</th>
                        <th style={{ width: '40%' }}>Konu</th>
                        <th style={{ width: '20%' }}>Konum</th>
                        <th style={{ width: '15%', textAlign: 'right' }}>Tarih</th>
                      </tr>
                    </thead>
                    <tbody>
                      {searchResults.map((item) => {
                        const isSelected = selectedResultItem?.messageId === item.messageId;
                        const owner = catalog.find((archive) => archive.archiveId === item.archiveId);
                        return (
                          <tr
                            key={item.messageId}
                            className={isSelected ? 'selected' : ''}
                            onClick={() => handleSelectResultItem(item)}
                            data-testid={`search-result-row-${item.messageId}`}
                            style={{ cursor: 'pointer' }}
                          >
                            <td onClick={e=>e.stopPropagation()}><input type="checkbox" aria-label={`${item.subject} seç`} checked={selectedExportIds.includes(item.messageId)} onChange={e=>setSelectedExportIds(ids=>e.target.checked?[...ids,item.messageId]:ids.filter(id=>id!==item.messageId))}/></td>
                            <td>
                              <div style={{ fontWeight: 600, overflowWrap: 'anywhere' }}>{item.sender}</div>
                              {item.senderEmail && (
                                <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', overflowWrap: 'anywhere' }}>
                                  {item.senderEmail}
                                </div>
                              )}
                            </td>
                            <td>
                              <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                <span style={{ overflowWrap: 'anywhere', fontWeight: 500 }}>{item.subject}</span>
                                {item.hasAttachments && <span title="Ek dosya">📎</span>}
                                {item.isBodyTruncated && (
                                  <span
                                    title="İleti gövdesi 512 KiB sınırını aştığı için kısaltıldı"
                                    style={{ fontSize: '0.6875rem', padding: '1px 5px', borderRadius: '3px', background: '#fef3c7', color: '#92400e' }}
                                  >
                                    Kısaltıldı
                                  </span>
                                )}
                              </div>
                              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: '340px' }}>
                                {item.snippet}
                              </div>
                            </td>
                            <td data-testid={`search-result-location-${item.messageId}`}>
                              <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                                <span style={{ fontSize: '0.6875rem', overflowWrap: 'anywhere' }}>
                                  {owner?.companyName || item.companyId} · {owner?.projectName || item.projectId}
                                </span>
                                <span style={{ fontSize: '0.75rem', fontWeight: 600, color: '#1e293b', overflowWrap: 'anywhere' }}>
                                  {item.archiveName}
                                </span>
                                <span style={{ fontSize: '0.6875rem', color: 'var(--text-muted)', overflowWrap: 'anywhere' }}>
                                  {item.originalFolder}
                                </span>
                              </div>
                            </td>
                            <td style={{ textAlign: 'right', color: 'var(--text-muted)', fontSize: '0.8125rem' }}>
                              <div>{item.originalMimeDateUtc ? new Date(item.originalMimeDateUtc).toLocaleString('tr-TR', { timeZone: 'Etc/GMT-3' }) : 'Tarih yok'}</div>
                              <div style={{ fontSize: '0.75rem' }}>{(item.sizeBytes / 1024).toFixed(1)} KB</div>
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                )
              ) : (
                // DEMO RESULTS TABLE
                demoSelectedSourceIds.length === 0 ? (
                  <div style={{ padding: '48px 24px', textAlign: 'center', color: 'var(--text-muted)' }} data-testid="search-empty-scope-prompt">
                    <IconSearch size={36} color="#94a3b8" />
                    <h3 style={{ fontSize: '1rem', fontWeight: 600, margin: '12px 0 6px 0', color: 'var(--text-main)' }}>
                      Konum seçilmedi
                    </h3>
                    <p style={{ fontSize: '0.875rem', maxWidth: '400px', margin: '0 auto' }}>
                      Arama yapmak için lütfen soldaki ağaçtan en az bir şirket, proje veya kaynak konumu seçin.
                    </p>
                  </div>
                ) : demoSearchResults.length === 0 ? (
                  <div style={{ padding: '48px 24px', textAlign: 'center', color: 'var(--text-muted)' }} data-testid="search-empty-results">
                    <p style={{ fontSize: '0.875rem' }}>Seçili konumlarda arama kriterine uygun ileti bulunamadı.</p>
                  </div>
                ) : (
                  <table className="data-table" data-testid="search-results-table">
                    <thead>
                      <tr>
                        <th style={{ width: '24%' }}>Gönderen</th>
                        <th style={{ width: '38%' }}>Konu</th>
                        <th style={{ width: '22%' }}>Konum</th>
                        <th style={{ width: '16%', textAlign: 'right' }}>Tarih</th>
                      </tr>
                    </thead>
                    <tbody>
                      {demoSearchResults.map((msg) => {
                        const loc = getDemoMessageLocation(msg);
                        return (
                          <tr
                            key={msg.id}
                            className={msg.id === demoSelectedMessage?.id ? 'selected' : ''}
                            onClick={() => setDemoSelectedMessage(msg)}
                            data-testid={`search-result-row-${msg.id}`}
                            style={{ cursor: 'pointer' }}
                          >
                            <td>
                              <div style={{ fontWeight: 600 }}>{msg.sender}</div>
                              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>{msg.senderEmail}</div>
                            </td>
                            <td>
                              <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                <span>{msg.subject}</span>
                                {msg.hasAttachment && <span title="Ek dosya">📎</span>}
                              </div>
                              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: '300px' }}>
                                {msg.preview}
                              </div>
                            </td>
                            <td data-testid={`search-result-location-${msg.id}`} title={loc.fullPath}>
                              <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                                <span style={{ fontSize: '0.75rem', fontWeight: 600, color: '#1e293b' }}>
                                  {loc.companyName} · {loc.projectName}
                                </span>
                                <span style={{ fontSize: '0.6875rem', color: 'var(--text-muted)' }}>
                                  {loc.sourceName} <span style={{ color: '#64748b' }}>({loc.accountOrFile})</span>
                                </span>
                              </div>
                            </td>
                            <td style={{ textAlign: 'right', color: 'var(--text-muted)', fontSize: '0.8125rem' }}>
                              <div>{msg.date}</div>
                              <div style={{ fontSize: '0.75rem' }}>{msg.sizeFormatted}</div>
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                )
              )}
            </div>
          </div>

          {/* Message Preview Pane */}
          <div className="search-preview-pane" data-testid="search-preview-pane">
            {mode === 'real' ? (
              <MessagePreview
                archiveMessage={previewData}
                locationContext={selectedItemLocationContext}
                isLoading={isPreviewLoading}
                error={previewError}
              />
            ) : (
              <MessagePreview
                message={demoSelectedMessage || undefined}
                locationContext={demoSelectedMessage ? getDemoMessageLocation(demoSelectedMessage) : undefined}
              />
            )}
          </div>
        </div>
      </div>

      {/* Add Archive Modal */}
      {isAddModalOpen && (
        <AddArchiveModal
          isOpen={isAddModalOpen}
          onClose={() => {
            setIsAddModalOpen(false);
            state.setArchiveAddInitialJobId?.(null);
          }}
          state={state}
          client={client}
          initialSourceJobId={state.archiveAddInitialJobId}
          onArchiveAdded={(job) => {
            setIngestProgress(job);
            setIngestError(null);
            setTrackedJobId(job.jobId);
          }}
        />
      )}
    </div>
  );
};
