import React, { useState } from 'react';
import { Breadcrumbs } from '../layout/Breadcrumbs';
import { SourceTree } from './SourceTree';
import { MessageFilterBar } from './MessageFilterBar';
import { MessageTable } from './MessageTable';
import { MessagePreview } from './MessagePreview';
import { TransferPlanPane } from './TransferPlanPane';
import { FolderMappingModal } from './FolderMappingModal';
import { AddSourceModal } from './AddSourceModal';
import { FilterDrawer } from './FilterDrawer';
import { AppState } from '../../state/useAppState';
import { savePlan } from '../../state/storage';

interface WorkspaceViewProps {
  state: AppState;
}

export const WorkspaceView: React.FC<WorkspaceViewProps> = ({ state }) => {
  const [saveToast, setSaveToast] = useState(false);

  const handleSavePlanManually = () => {
    savePlan(state.plan);
    setSaveToast(true);
    setTimeout(() => setSaveToast(false), 2500);
  };

  const totalFilteredBytes = state.filteredMessages.reduce((sum, m) => sum + m.sizeBytes, 0);
  const projectSources = state.sources.filter((s) => s.projectId === state.plan.projectId);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', flex: 1 }} data-testid="workspace-view">
      {/* Sub-header */}
      <div className="page-subheader">
        <div>
          <Breadcrumbs items={['Müşteriler', state.plan.client, state.plan.name]} />
          <h1 className="page-title">{state.plan.name}</h1>
          <p className="page-subtitle">Örnek veriler · Tasarım taslağı</p>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
          {saveToast && (
            <span
              style={{
                fontSize: '0.8125rem',
                color: 'var(--status-success)',
                fontWeight: 600,
                background: 'var(--status-success-bg)',
                padding: '4px 10px',
                borderRadius: '4px',
              }}
              data-testid="save-plan-toast"
            >
              Plan yerel depolamaya kaydedildi ✓
            </span>
          )}

          <button
            className="btn btn-outline-orange"
            onClick={handleSavePlanManually}
            data-testid="save-job-plan-btn"
          >
            İşi kaydet
          </button>
        </div>
      </div>

      {/* 3-Column Workspace Grid */}
      <div className="workspace-grid">
        {/* Left: Sources */}
        <SourceTree
          sources={projectSources}
          selectedSourceId={state.plan.sourceId}
          selectedFolderId={state.plan.sourceFolderId}
          onSelectSourceFolder={(source, folderId, folderName) => {
            state.updatePlan((prev) => ({
              ...prev,
              companyId: source.companyId,
              projectId: source.projectId,
              sourceId: source.id,
              sourceType: source.name,
              sourceAccount: source.accountOrFileName,
              sourceFolderId: folderId,
              sourceFolderName: folderName,
              manualSelectionMode: false,
              selectedMessageIds: [],
            }));
          }}
          onOpenAddSource={() => state.setAddSourceModalOpen(true)}
        />

        {/* Middle: Filter Bar + Messages Table + Preview */}
        <section className="workspace-panel" style={{ flex: 1, minWidth: 0 }} data-testid="middle-messages-panel">
          <div className="panel-header" style={{ padding: '10px 18px' }}>
            <span className="panel-title">İletiler</span>
          </div>

          <MessageFilterBar
            filters={state.plan.filters}
            onUpdateFilters={state.updateFilters}
            onOpenAdvancedFilters={() => state.setFilterDrawerOpen(true)}
            filteredCount={state.filteredMessages.length}
            totalFilteredBytes={totalFilteredBytes}
            manualSelectionMode={state.plan.manualSelectionMode}
            selectedCount={state.plan.selectedMessageIds.length}
            onToggleManualSelection={state.toggleManualSelection}
            onSelectAll={state.selectAllFiltered}
            onDeselectAll={state.deselectAll}
          />

          <MessageTable
            messages={state.filteredMessages}
            selectedMessageId={state.selectedMessageId}
            onSelectMessage={(id) => state.setSelectedMessageId(id)}
            manualSelectionMode={state.plan.manualSelectionMode}
            selectedMessageIds={state.plan.selectedMessageIds}
            onToggleCheckbox={state.toggleMessageSelected}
          />

          <MessagePreview
            message={state.selectedMessage}
            locationContext={{
              companyName: state.plan.client || 'Örnek Şirket',
              projectName: state.plan.projectName || state.projects.find((p) => p.id === state.plan.projectId)?.name || 'Sistem Geçişi',
              sourceName: state.plan.sourceType || 'Kaynak',
              accountOrFile: state.plan.sourceAccount || 'hesap',
              fullPath: `${state.plan.client || 'Örnek Şirket'} > ${state.plan.projectName || state.projects.find((p) => p.id === state.plan.projectId)?.name || 'Sistem Geçişi'} > ${state.plan.sourceType || 'Kaynak'} (${state.plan.sourceAccount || 'hesap'})`,
            }}
          />
        </section>

        {/* Right: Transfer Plan */}
        <TransferPlanPane
          plan={state.plan}
          onUpdatePlan={state.updatePlan}
          onOpenFolderMapping={() => state.setFolderMappingModalOpen(true)}
          onRunPreflight={state.runPreflightCheck}
          scopeCount={
            state.plan.manualSelectionMode
              ? state.plan.selectedMessageIds.length
              : state.filteredMessages.length
          }
        />
      </div>

      {/* Modals & Drawers */}
      <FolderMappingModal
        isOpen={state.folderMappingModalOpen}
        onClose={() => state.setFolderMappingModalOpen(false)}
        sourceFolder={state.plan.sourceFolderName}
        targetFolder={state.plan.targetFolderName}
        onSaveMapping={(newTargetFolder) => {
          state.updatePlan((prev) => ({ ...prev, targetFolderName: newTargetFolder }));
        }}
      />

      <AddSourceModal
        isOpen={state.addSourceModalOpen}
        onClose={() => state.setAddSourceModalOpen(false)}
        projectName={state.plan.projectName}
        onAddSource={(name, kind, accountOrFileName) => {
          const newSourceId = state.addSource(
            state.plan.companyId,
            state.plan.projectId,
            name,
            kind,
            kind === 'mailbox' ? 'server' : 'file',
            accountOrFileName
          );
          state.updatePlan((prev) => ({
            ...prev,
            sourceId: newSourceId,
            sourceType: name,
            sourceAccount: accountOrFileName,
            sourceFolderId: 'inbox',
            sourceFolderName: kind === 'mailbox' ? 'Gelen kutusu' : 'Arşiv',
            manualSelectionMode: false,
            selectedMessageIds: [],
          }));
        }}
      />

      <FilterDrawer
        isOpen={state.filterDrawerOpen}
        onClose={() => state.setFilterDrawerOpen(false)}
        filters={state.plan.filters}
        onApplyFilters={state.updateFilters}
      />
    </div>
  );
};
