import { useState, useCallback, useMemo } from 'react';
import {
  Company,
  DataSource,
  FilterCriteria,
  Job,
  JobType,
  NavigationTab,
  OversizedResolution,
  PreflightState,
  Project,
  TransferPlan,
} from '../types';
import { getMessagesBySourceId } from '../data/sampleMessages';
import {
  DEFAULT_TRANSFER_PLAN,
  computePlanHash,
  loadCurrentView,
  loadSavedCompanies,
  loadSavedJobs,
  loadSavedPlan,
  loadSavedProjects,
  loadSavedRun,
  loadSavedSearchScope,
  loadSavedSources,
  resetAllStorage,
  saveCurrentView,
  savePlan,
  saveSavedCompanies,
  saveSavedJobs,
  saveSavedProjects,
  saveSavedSearchScope,
  saveSavedSources,
} from './storage';
import { evaluatePreflight, getFilteredMessages } from './preflightRules';
import { useSimulation } from './useSimulation';

export function useAppState() {
  const [currentTab, setCurrentTab] = useState<NavigationTab>(() => {
    const savedRun = loadSavedRun();
    if (savedRun && (savedRun.status === 'running' || savedRun.status === 'paused' || savedRun.status === 'completed')) {
      return 'transfers';
    }
    const view = loadCurrentView();
    if (view === 'transfer' || view === 'preflight') {
      return 'transfers';
    }
    return 'clients';
  });
  const [currentView, setCurrentViewState] = useState<'workspace' | 'preflight' | 'transfer'>(() => {
    return loadCurrentView();
  });

  const setCurrentView = useCallback((view: 'workspace' | 'preflight' | 'transfer') => {
    setCurrentViewState(view);
    saveCurrentView(view);
  }, []);

  // Multi-client directory & data sources state
  const [companies, setCompanies] = useState<Company[]>(() => loadSavedCompanies());
  const [projects, setProjects] = useState<Project[]>(() => loadSavedProjects());
  const [sources, setSources] = useState<DataSource[]>(() => loadSavedSources());

  const syncAuthorizedCatalog = useCallback((authorizedCompanies: Company[], authorizedProjects: Project[]) => {
    setCompanies(authorizedCompanies);
    setProjects(authorizedProjects);
    setSources([]);
    setJobs([]);
    setSelectedCompanyId((current) => authorizedCompanies.some((item) => item.id === current) ? current : null);
    setSelectedProjectId((current) => authorizedProjects.some((item) => item.id === current) ? current : null);
    if (!authorizedCompanies.length || !authorizedProjects.length) return;
    setPlan((previous) => {
      const company = authorizedCompanies.find((item) => item.id === previous.companyId) || authorizedCompanies[0];
      const availableProjects = authorizedProjects.filter((item) => item.companyId === company.id);
      const project = availableProjects.find((item) => item.id === previous.projectId) || availableProjects[0];
      if (!project) return previous;
      return { ...previous, companyId: company.id, client: company.name, projectId: project.id, projectName: project.name };
    });
  }, []);

  // Client view navigation selection (starts at directory level: null)
  const [selectedCompanyId, setSelectedCompanyId] = useState<string | null>(null);
  const [selectedProjectId, setSelectedProjectId] = useState<string | null>(null);

  // Explicitly selected real local engine job ID for workspace/convert screen
  const [selectedLocalJobId, setSelectedLocalJobId] = useState<string | null>(null);

  // Explicitly selected real local archive job ID for archive search screen
  const [selectedArchiveJobId, setSelectedArchiveJobId] = useState<string | null>(null);

  // Initial bridge export source job ID when navigating directly to Add Archive modal
  const [archiveAddInitialJobId, setArchiveAddInitialJobId] = useState<string | null>(null);

  // Real archive search selected scope count (for neutral footer status)
  const [archiveSearchScopeCount, setArchiveSearchScopeCount] = useState<number>(0);

  // Search scope multi-location selection state
  const [searchSelectedSourceIds, setSearchSelectedSourceIds] = useState<string[]>(() =>
    loadSavedSearchScope()
  );

  const updateSearchSelectedSourceIds = useCallback((newScope: string[]) => {
    setSearchSelectedSourceIds(newScope);
    saveSavedSearchScope(newScope);
  }, []);

  // Load versioned plan with safe fallback
  const [plan, setPlan] = useState<TransferPlan>(() => loadSavedPlan());

  // Jobs state initialized from storage or defaults
  const [jobs, setJobs] = useState<Job[]>(() => loadSavedJobs());

  // Derive source messages strictly from plan.sourceId with ZERO fallback to all messages
  const sourceMessages = useMemo(() => {
    return getMessagesBySourceId(plan.sourceId);
  }, [plan.sourceId]);

  // Derived filtered messages for the current plan
  const filteredMessages = useMemo(() => {
    return getFilteredMessages(sourceMessages, plan);
  }, [sourceMessages, plan]);

  // Selected message for preview panel (default: first filtered item or undefined)
  const [selectedMessageId, setSelectedMessageId] = useState<string>('msg-2024-inbox-001');

  // Derived selected message: strictly undefined if filteredMessages is empty
  const selectedMessage = useMemo(() => {
    if (filteredMessages.length === 0) {
      return undefined;
    }
    return (
      filteredMessages.find((m) => m.id === selectedMessageId) ||
      filteredMessages[0]
    );
  }, [filteredMessages, selectedMessageId]);

  // Preflight state evaluated strictly against sourceMessages
  const [preflightState, setPreflightState] = useState<PreflightState | null>(() => {
    const initialPlan = loadSavedPlan();
    const initialSourceMsgs = getMessagesBySourceId(initialPlan.sourceId);
    return evaluatePreflight(initialSourceMsgs, initialPlan);
  });

  // Modal / drawer states
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [newJobMenuOpen, setNewJobMenuOpen] = useState(false);
  const [draftJobModalType, setDraftJobModalType] = useState<JobType | null>(null);
  const [folderMappingModalOpen, setFolderMappingModalOpen] = useState(false);
  const [addSourceModalOpen, setAddSourceModalOpen] = useState(false);
  const [filterDrawerOpen, setFilterDrawerOpen] = useState(false);
  const [blockerDetailModalOpen, setBlockerDetailModalOpen] = useState(false);
  const [reportModalOpen, setReportModalOpen] = useState(false);

  // Simulation engine
  const simulation = useSimulation();

  // Derived stale check
  const isPreflightStale = useMemo(() => {
    if (!preflightState) return true;
    return preflightState.planHash !== computePlanHash(plan);
  }, [preflightState, plan]);

  // Update plan functionally and persist
  const updatePlan = useCallback((updater: (prev: TransferPlan) => TransferPlan) => {
    setPlan((prev) => {
      const next = updater(prev);
      savePlan(next);
      return next;
    });
  }, []);

  const updateFilters = useCallback(
    (filterUpdates: Partial<FilterCriteria>) => {
      updatePlan((prev) => ({
        ...prev,
        filters: {
          ...prev.filters,
          ...filterUpdates,
        },
      }));
    },
    [updatePlan]
  );

  const runPreflightCheck = useCallback(() => {
    const msgs = getMessagesBySourceId(plan.sourceId);
    const result = evaluatePreflight(msgs, plan);
    setPreflightState(result);
    setCurrentView('preflight');
  }, [plan, setCurrentView]);

  const resolveOversizedItem = useCallback(
    (resolution: OversizedResolution) => {
      updatePlan((prev) => ({
        ...prev,
        oversizedResolution: resolution,
      }));
      // Re-evaluate immediately with resolution
      setPreflightState(() => {
        const nextPlan = { ...plan, oversizedResolution: resolution };
        const msgs = getMessagesBySourceId(nextPlan.sourceId);
        return evaluatePreflight(msgs, nextPlan);
      });
    },
    [plan, updatePlan]
  );

  const toggleManualSelection = useCallback(() => {
    updatePlan((prev) => ({
      ...prev,
      manualSelectionMode: !prev.manualSelectionMode,
      selectedMessageIds: !prev.manualSelectionMode ? prev.selectedMessageIds : [],
    }));
  }, [updatePlan]);

  const toggleMessageSelected = useCallback(
    (id: string) => {
      updatePlan((prev) => {
        const exists = prev.selectedMessageIds.includes(id);
        const nextIds = exists
          ? prev.selectedMessageIds.filter((mId) => mId !== id)
          : [...prev.selectedMessageIds, id];
        return {
          ...prev,
          selectedMessageIds: nextIds,
        };
      });
    },
    [updatePlan]
  );

  const selectAllFiltered = useCallback(() => {
    updatePlan((prev) => ({
      ...prev,
      selectedMessageIds: filteredMessages.map((m) => m.id),
    }));
  }, [filteredMessages, updatePlan]);

  const deselectAll = useCallback(() => {
    updatePlan((prev) => ({
      ...prev,
      selectedMessageIds: [],
    }));
  }, [updatePlan]);

  const handleStartTransfer = useCallback(() => {
    const msgs = getMessagesBySourceId(plan.sourceId);
    simulation.startSimulation(plan, msgs);
    setCurrentView('transfer');
  }, [plan, simulation, setCurrentView]);

  // Add Company
  const addCompany = useCallback((name: string, code: string, description?: string) => {
    const id = `comp-${Date.now()}`;
    const newComp: Company = {
      id,
      name,
      code: code.toUpperCase(),
      description: description || 'Örnek müşteri şirketi',
      projectIds: [],
    };
    setCompanies((prev) => {
      const next = [...prev, newComp];
      saveSavedCompanies(next);
      return next;
    });
    return id;
  }, []);

  // Add Project
  const addProject = useCallback((companyId: string, name: string, description?: string) => {
    const id = `proj-${Date.now()}`;
    const newProj: Project = {
      id,
      companyId,
      name,
      description: description || 'Örnek proje',
      sourceIds: [],
    };
    setProjects((prev) => {
      const next = [...prev, newProj];
      saveSavedProjects(next);
      return next;
    });
    setCompanies((prev) => {
      const next = prev.map((c) =>
        c.id === companyId ? { ...c, projectIds: [...c.projectIds, id] } : c
      );
      saveSavedCompanies(next);
      return next;
    });
    return id;
  }, []);

  // Add Source (mailbox or file archive) - zero messages by default
  const addSource = useCallback(
    (
      companyId: string,
      projectId: string,
      name: string,
      kind: 'mailbox' | 'archive_file',
      type: 'server' | 'file',
      accountOrFileName: string
    ) => {
      const id = `src-${Date.now()}`;
      const newSource: DataSource = {
        id,
        companyId,
        projectId,
        name,
        kind,
        type,
        accountOrFileName,
        folders: [
          {
            id: 'inbox',
            name: kind === 'mailbox' ? 'Gelen kutusu' : 'Arşiv',
            icon: kind === 'mailbox' ? 'inbox' : 'folder',
            count: 0,
          },
        ],
      };
      setSources((prev) => {
        const next = [...prev, newSource];
        saveSavedSources(next);
        return next;
      });
      setProjects((prev) => {
        const next = prev.map((p) =>
          p.id === projectId ? { ...p, sourceIds: [...p.sourceIds, id] } : p
        );
        saveSavedProjects(next);
        return next;
      });
      return id;
    },
    []
  );

  // Navigate to transfers tab with full customer / project / source context
  const startTransferWithContext = useCallback(
    (companyId: string, projectId: string, sourceId: string, operationType: JobType = 'migration') => {
      const comp = companies.find((c) => c.id === companyId);
      const proj = projects.find((p) => p.id === projectId);
      const src = sources.find((s) => s.id === sourceId);

      const folderId = src?.folders[0]?.id || 'inbox';
      const folderName = src?.folders[0]?.name || 'Gelen kutusu';

      updatePlan((prev) => ({
        ...prev,
        companyId,
        projectId,
        sourceId,
        client: comp?.name || prev.client,
        projectName: proj?.name || prev.projectName,
        sourceType: src?.name || prev.sourceType,
        sourceAccount: src?.accountOrFileName || prev.sourceAccount,
        sourceFolderId: folderId,
        sourceFolderName: folderName,
        operationType,
        manualSelectionMode: false,
        selectedMessageIds: [],
      }));

      // Immediately evaluate preflight for the new context
      const newMsgs = getMessagesBySourceId(sourceId);
      const tempPlan: TransferPlan = {
        ...plan,
        companyId,
        projectId,
        sourceId,
        sourceFolderId: folderId,
        sourceFolderName: folderName,
        operationType,
        manualSelectionMode: false,
        selectedMessageIds: [],
      };
      setPreflightState(evaluatePreflight(newMsgs, tempPlan));

      setCurrentTab('transfers');
      setCurrentView('workspace');
    },
    [companies, projects, sources, plan, updatePlan, setCurrentView]
  );

  const openLocalJob = useCallback(
    (jobId: string, companyId?: string, projectId?: string, operationType: JobType = 'convert') => {
      setSelectedLocalJobId(jobId);
      updatePlan((prev) => ({
        ...prev,
        operationType,
        companyId: companyId || prev.companyId,
        projectId: projectId || prev.projectId,
      }));
      setCurrentTab('transfers');
      setCurrentView('workspace');
    },
    [updatePlan, setCurrentTab, setCurrentView]
  );

  const openArchiveSearchJob = useCallback(
    (jobId: string, companyId?: string, projectId?: string, _archiveIdOrName?: string) => {
      setSelectedArchiveJobId(jobId);
      if (companyId) setSelectedCompanyId(companyId);
      if (projectId) setSelectedProjectId(projectId);
      setCurrentTab('search');
    },
    [setCurrentTab]
  );

  const openArchiveSearchAdd = useCallback(
    (sourceJobId: string, companyId?: string, projectId?: string) => {
      setArchiveAddInitialJobId(sourceJobId);
      if (companyId) setSelectedCompanyId(companyId);
      if (projectId) setSelectedProjectId(projectId);
      setCurrentTab('search');
    },
    [setCurrentTab]
  );

  const handleResetDemoData = useCallback(() => {
    resetAllStorage();
    const defaultPlan = { ...DEFAULT_TRANSFER_PLAN };
    setPlan(defaultPlan);
    setCompanies(loadSavedCompanies());
    setProjects(loadSavedProjects());
    setSources(loadSavedSources());
    setSelectedCompanyId(null);
    setSelectedProjectId(null);
    setSearchSelectedSourceIds(loadSavedSearchScope());
    const defaultMsgs = getMessagesBySourceId(defaultPlan.sourceId);
    setPreflightState(evaluatePreflight(defaultMsgs, defaultPlan));
    simulation.resetSimulation();
    setJobs(loadSavedJobs());
    setCurrentTab('transfers');
    setCurrentView('workspace');
    setSettingsOpen(false);
  }, [simulation, setCurrentView, setCurrentTab]);

  const toggleJobPause = useCallback(
    (jobId: string) => {
      if (jobId === 'job-1' && simulation.activeRun) {
        if (simulation.activeRun.status === 'running') {
          simulation.pauseSimulation();
        } else if (simulation.activeRun.status === 'paused') {
          simulation.resumeSimulation();
        }
      }
      setJobs((prevJobs) => {
        const next = prevJobs.map((j) => {
          if (j.id === jobId) {
            const nextStatus = j.status === 'running' ? 'paused' : 'running';
            return {
              ...j,
              status: nextStatus as any,
              recentEvents: [
                {
                  time: new Date().toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' }),
                  text: nextStatus === 'paused' ? 'Kullanıcı tarafından duraklatıldı' : 'Aktarıma devam ediliyor',
                },
                ...j.recentEvents,
              ],
            };
          }
          return j;
        });
        saveSavedJobs(next);
        return next;
      });
    },
    [simulation]
  );

  return {
    currentTab,
    setCurrentTab,
    currentView,
    setCurrentView,
    // Multi-client directory
    companies,
    projects,
    sources,
    syncAuthorizedCatalog,
    selectedCompanyId,
    setSelectedCompanyId,
    selectedProjectId,
    setSelectedProjectId,
    addCompany,
    addProject,
    addSource,
    startTransferWithContext,
    // Search scope
    searchSelectedSourceIds,
    updateSearchSelectedSourceIds,
    // Plan & items
    plan,
    updatePlan,
    updateFilters,
    jobs,
    setJobs,
    toggleJobPause,
    sourceMessages,
    filteredMessages,
    selectedMessage,
    selectedMessageId,
    setSelectedMessageId,
    preflightState,
    isPreflightStale,
    runPreflightCheck,
    resolveOversizedItem,
    toggleManualSelection,
    toggleMessageSelected,
    selectAllFiltered,
    deselectAll,
    handleStartTransfer,
    handleResetDemoData,
    simulation,
    // Real Local Engine Navigation State
    selectedLocalJobId,
    setSelectedLocalJobId,
    openLocalJob,
    selectedArchiveJobId,
    setSelectedArchiveJobId,
    openArchiveSearchJob,
    archiveAddInitialJobId,
    setArchiveAddInitialJobId,
    openArchiveSearchAdd,
    archiveSearchScopeCount,
    setArchiveSearchScopeCount,
    // Modals
    settingsOpen,
    setSettingsOpen,
    newJobMenuOpen,
    setNewJobMenuOpen,
    draftJobModalType,
    setDraftJobModalType,
    folderMappingModalOpen,
    setFolderMappingModalOpen,
    addSourceModalOpen,
    setAddSourceModalOpen,
    filterDrawerOpen,
    setFilterDrawerOpen,
    blockerDetailModalOpen,
    setBlockerDetailModalOpen,
    reportModalOpen,
    setReportModalOpen,
  };
}

export type AppState = Omit<ReturnType<typeof useAppState>, 'syncAuthorizedCatalog'> & {
  syncAuthorizedCatalog?: ReturnType<typeof useAppState>['syncAuthorizedCatalog'];
};
