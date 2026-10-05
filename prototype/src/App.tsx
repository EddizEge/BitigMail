import React from 'react';
import { useAppState } from './state/useAppState';
import { Header } from './components/layout/Header';
import { StatusBar } from './components/layout/StatusBar';
import { JobCenter } from './components/jobs/JobCenter';
import { ClientDirectoryView } from './components/clients/ClientDirectoryView';
import { TransfersTabView } from './components/transfer/TransfersTabView';
import { ArchiveSearchView } from './components/search/ArchiveSearchView';
import { ReportsView } from './components/reports/ReportsView';
import { SettingsDrawer } from './components/settings/SettingsDrawer';
import { IdentityGate, useIdentity } from './components/auth/IdentityGate';

const AuthenticatedApp: React.FC = () => {
  const state = useAppState();
  const identityState = useIdentity();

  React.useEffect(() => {
    const catalog = identityState?.identity?.companies;
    if (!catalog) return;
    state.syncAuthorizedCatalog?.(
      catalog.map((company) => ({ id: company.companyId, name: company.name, code: '', description: 'Yetkili çalışma alanı', projectIds: company.projects?.map((project) => project.projectId) || [] })),
      catalog.flatMap((company) => company.projects?.map((project) => ({ id: project.projectId, companyId: company.companyId, name: project.name, description: 'Yetkili proje', sourceIds: [] })) || []),
    );
  }, [identityState?.identity]);

  const renderContent = () => {
    switch (state.currentTab) {
      case 'jobs':
        return (
          <JobCenter
            jobs={state.jobs}
            onToggleJobPause={state.toggleJobPause}
            onNavigateToWorkspace={(job) => {
              if (job?.isLocalEngine) {
                if (job.jobKind === 'archive-ingest' || job.jobKind === 'archive-reindex') {
                  state.openArchiveSearchJob(job.id, job.companyId, job.projectId, job.archiveId || job.title);
                } else {
                  state.openLocalJob(job.id, job.companyId, job.projectId, job.type || 'convert');
                }
              } else if (job?.companyId && job?.projectId && job?.sourceId) {
                state.startTransferWithContext(job.companyId, job.projectId, job.sourceId, job.type || 'migration');
              } else {
                state.setCurrentTab('transfers');
                state.setCurrentView('workspace');
              }
            }}
            onAddNewJob={(newJob) => {
              state.setJobs((prev) => [newJob, ...prev]);
            }}
            onAddToArchive={(job) => {
              state.openArchiveSearchAdd?.(job.id, job.companyId, job.projectId);
            }}
          />
        );
      case 'clients':
        return <ClientDirectoryView state={state} />;
      case 'transfers':
        return <TransfersTabView state={state} />;
      case 'search':
        return <ArchiveSearchView state={state} />;
      case 'reports':
        return <ReportsView />;
      default:
        return <ClientDirectoryView state={state} />;
    }
  };

  const getStatusText = () => {
    if (state.currentTab === 'jobs') {
      if (identityState?.identity) return '';
      return `${state.jobs.length} örnek iş`;
    }
    if (state.currentTab === 'clients') {
      if (identityState?.identity) return `${state.companies.length} müşteri, ${state.projects.length} proje`;
      return `${state.companies.length} müşteri, ${state.projects.length} proje, ${state.sources.length} kaynak kayıtlı`;
    }
    if (state.currentTab === 'transfers') {
      if (identityState?.identity) return '';
      if (state.plan.operationType === 'convert' || state.plan.operationType === 'archive') {
        return '';
      }
      if (state.currentView === 'transfer') {
        const processed = state.simulation.processedCount;
        const total = state.simulation.totalCount;
        return `Aktarım: ${processed} / ${total} ileti işlendi`;
      }
      if (state.currentView === 'preflight') {
        return `${state.preflightState?.scopeCount ?? 0} ileti ön kontrolden geçirildi`;
      }
      return `(Örnek simülasyon) ${state.filteredMessages.length} ileti filtreye uyuyor (${state.plan.client || 'Müşteri'} / ${state.plan.sourceType || 'Kaynak'})`;
    }
    if (state.currentTab === 'search') {
      if (state.archiveSearchScopeCount === 0) {
        return 'Konum seçilmedi';
      }
      return `${state.archiveSearchScopeCount} arşiv seçili arama kapsamı`;
    }
    return 'Örnek arşiv hazır';
  };

  const getProjectName = () => {
    if (identityState?.identity) {
      return state.projects.find((project) => project.id === state.plan.projectId)?.name || 'Çalışma alanı seçilmedi';
    }
    if (state.currentTab === 'search') {
      return 'Yerel Arşiv';
    }
    return state.plan.projectName || 'Örnek proje';
  };

  return (
    <div className="app-shell" data-testid="app-shell">
      <Header
        currentTab={state.currentTab}
        onTabChange={(tab) => {
          state.setCurrentTab(tab);
          if (tab === 'transfers' && state.currentView === 'transfer' && state.simulation.activeRun?.status === 'completed') {
            state.setCurrentView('workspace');
          }
        }}
        onOpenSettings={() => state.setSettingsOpen(true)}
      />

      <main className="app-main">
        {renderContent()}
      </main>

      <StatusBar projectName={getProjectName()} statusText={getStatusText()} />

      <SettingsDrawer
        isOpen={state.settingsOpen}
        onClose={() => state.setSettingsOpen(false)}
        onResetDemoData={state.handleResetDemoData}
        authenticated={Boolean(identityState?.identity)}
      />
    </div>
  );
};

export const App: React.FC = () => <IdentityGate><AuthenticatedApp /></IdentityGate>;
