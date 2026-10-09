import React, { useState, useEffect } from 'react';
import { AppState } from '../../state/useAppState';
import { JobType } from '../../types';
import { TARGET_OPTIONS } from '../../data/sampleSources';
import { WorkspaceView } from '../workspace/WorkspaceView';
import { PreflightView } from '../preflight/PreflightView';
import { TransferRunView } from './TransferRunView';
import { ConversionWorkspace } from '../convert/ConversionWorkspace';
import { LocalArchiveWorkflow } from '../archive/LocalArchiveWorkflow';
import { ImapTransferWorkflow } from './ImapTransferWorkflow';
import { BridgeTransferWorkflow } from './BridgeTransferWorkflow';
import { PopSnapshotWorkflow } from './PopSnapshotWorkflow';
import { ChoiceSelector, EmptyState, PageHeader } from '../layout/PageHeader';
import { DamagedStoreRecoveryWorkflow } from '../recovery/DamagedStoreRecoveryWorkflow';
import { LocalEngineClient, localEngineClient } from '../../api/localEngineClient';
import { useIdentity } from '../auth/IdentityGate';

export type RealMigrationDirection = 'file-to-imap' | 'imap-to-file' | 'imap-to-imap' | 'pop-to-file';

interface TransfersTabViewProps {
  state: AppState;
  client?: LocalEngineClient;
}

export const TransfersTabView: React.FC<TransfersTabViewProps> = ({ state, client = localEngineClient }) => {
  const identityState = useIdentity();
  const [transferMode, setTransferMode] = useState<'real' | 'sample'>('real');
  const [realDirection, setRealDirection] = useState<RealMigrationDirection>('file-to-imap');

  // Rehydrate real bridge direction when a saved bridge job is opened
  useEffect(() => {
    let alive = true;
    if (state.selectedLocalJobId) {
      const id = state.selectedLocalJobId;
      client.getJob(id).then((job) => {
        if (!alive) return;
        if (job.jobKind === 'bridge-export') {
          setTransferMode('real');
          setRealDirection('imap-to-file');
        } else if (job.jobKind === 'bridge-import') {
          setTransferMode('real');
          setRealDirection('file-to-imap');
        }
      }).catch(() => {});
    }
    return () => { alive = false; };
  }, [state.selectedLocalJobId, client]);

  const catalogCompanies = identityState?.identity?.companies?.map((entry) => ({ id: entry.companyId, name: entry.name, code: '', projectIds: entry.projects?.map((item) => item.projectId) || [] }));
  const companies = catalogCompanies?.length ? catalogCompanies : state.companies;
  const catalogProjects = identityState?.identity?.companies?.flatMap((entry) => entry.projects?.map((item) => ({ id: item.projectId, companyId: entry.companyId, name: item.name, sourceIds: [] })) || []);
  const projects = catalogProjects?.length ? catalogProjects : state.projects;
  const currentCompany = companies.find((c) => c.id === state.plan.companyId) || companies[0];
  const companyProjects = projects.filter((p) => p.companyId === (currentCompany?.id || ''));
  const currentProject = companyProjects.find((p) => p.id === state.plan.projectId) || companyProjects[0];
  const projectSources = state.sources.filter((s) => s.projectId === (currentProject?.id || ''));

  const currentOperation = state.plan.operationType || 'migration';

  useEffect(() => {
    if (!identityState?.identity || !currentCompany || !currentProject) return;
    if (state.plan.companyId === currentCompany.id && state.plan.projectId === currentProject.id) return;
    state.updatePlan((previous) => ({ ...previous, companyId: currentCompany.id, client: currentCompany.name, projectId: currentProject.id, projectName: currentProject.name }));
  }, [identityState?.identity, currentCompany?.id, currentProject?.id]);

  const handleCompanyChange = (newCompanyId: string) => {
    const newComp = companies.find((c) => c.id === newCompanyId);
    const newProjects = projects.filter((p) => p.companyId === newCompanyId);
    const firstProj = newProjects[0];
    const newSources = state.sources.filter((s) => s.projectId === (firstProj?.id || ''));
    const firstSrc = newSources[0];

    const firstFolder = firstSrc?.folders[0]?.id || 'inbox';
    const firstFolderName = firstSrc?.folders[0]?.name || 'Gelen kutusu';

    state.updatePlan((prev) => ({
      ...prev,
      companyId: newCompanyId,
      client: newComp?.name || prev.client,
      projectId: firstProj?.id || '',
      projectName: firstProj?.name || '',
      sourceId: firstSrc?.id || '',
      sourceType: firstSrc?.name || '',
      sourceAccount: firstSrc?.accountOrFileName || '',
      sourceFolderId: firstFolder,
      sourceFolderName: firstFolderName,
      manualSelectionMode: false,
      selectedMessageIds: [],
    }));

    state.setCurrentView('workspace');
  };

  const handleProjectChange = (newProjectId: string) => {
    const newProj = projects.find((p) => p.id === newProjectId);
    const newSources = state.sources.filter((s) => s.projectId === newProjectId);
    const firstSrc = newSources[0];

    const firstFolder = firstSrc?.folders[0]?.id || 'inbox';
    const firstFolderName = firstSrc?.folders[0]?.name || 'Gelen kutusu';

    state.updatePlan((prev) => ({
      ...prev,
      projectId: newProjectId,
      projectName: newProj?.name || '',
      sourceId: firstSrc?.id || '',
      sourceType: firstSrc?.name || '',
      sourceAccount: firstSrc?.accountOrFileName || '',
      sourceFolderId: firstFolder,
      sourceFolderName: firstFolderName,
      manualSelectionMode: false,
      selectedMessageIds: [],
    }));

    state.setCurrentView('workspace');
  };

  const handleSourceChange = (newSourceId: string) => {
    const newSrc = state.sources.find((s) => s.id === newSourceId);
    const firstFolder = newSrc?.folders[0]?.id || 'inbox';
    const firstFolderName = newSrc?.folders[0]?.name || 'Gelen kutusu';

    state.updatePlan((prev) => ({
      ...prev,
      sourceId: newSourceId,
      sourceType: newSrc?.name || prev.sourceType,
      sourceAccount: newSrc?.accountOrFileName || prev.sourceAccount,
      sourceFolderId: firstFolder,
      sourceFolderName: firstFolderName,
      manualSelectionMode: false,
      selectedMessageIds: [],
    }));

    state.setCurrentView('workspace');
  };

  const handleOperationTypeChange = (newType: JobType) => {
    state.updatePlan((prev) => ({
      ...prev,
      operationType: newType,
    }));
  };

  const renderOperationContent = () => {
    if (currentOperation === 'convert') {
      return <ConversionWorkspace state={state} />;
    }
    if (currentOperation === 'archive') {
      return <LocalArchiveWorkflow state={state} />;
    }
    if (currentOperation === 'recovery') {
      return <DamagedStoreRecoveryWorkflow key={`${currentCompany?.id}/${currentProject?.id}`} client={client}
        clientContext={{ companyId: currentCompany?.id || '', companyName: currentCompany?.name || '', projectId: currentProject?.id || '', projectName: currentProject?.name || '' }} />;
    }

    if (transferMode === 'real') {
      if (realDirection === 'pop-to-file') {
        return <PopSnapshotWorkflow companyId={currentCompany?.id || ''} projectId={currentProject?.id || ''} client={client} />;
      }
      if (realDirection === 'imap-to-imap') {
        return (
          <ImapTransferWorkflow
            companyId={currentCompany?.id || state.plan.companyId || ''}
            companyName={currentCompany?.name || state.plan.client || ''}
            projectId={currentProject?.id || state.plan.projectId || ''}
            projectName={currentProject?.name || state.plan.projectName || ''}
            onNavigateToAccounts={() => state.setCurrentTab('clients')}
            embedded
          />
        );
      }

      return (
        <BridgeTransferWorkflow
          companyId={currentCompany?.id || state.plan.companyId || ''}
          companyName={currentCompany?.name || state.plan.client || ''}
          projectId={currentProject?.id || state.plan.projectId || ''}
          projectName={currentProject?.name || state.plan.projectName || ''}
          onNavigateToAccounts={() => state.setCurrentTab('clients')}
          initialDirection={realDirection === 'imap-to-file' ? 'imap-to-file' : 'file-to-imap'}
          onDirectionChange={(dir) => {
            state.setSelectedLocalJobId?.(null);
            setRealDirection(dir);
          }}
          targetJobId={state.selectedLocalJobId}
          onClearTargetJob={() => state.setSelectedLocalJobId?.(null)}
          client={client}
          onAddToArchive={(job) => {
            state.openArchiveSearchAdd?.(
              job.jobId,
              job.clientContext?.companyId,
              job.clientContext?.projectId
            );
          }}
          embedded
        />
      );
    }

    if (state.currentView === 'preflight') {
      return <PreflightView state={state} />;
    }
    if (state.currentView === 'transfer') {
      return <TransferRunView state={state} />;
    }
    return <WorkspaceView state={state} />;
  };

  const selectDirection = (direction: RealMigrationDirection) => {
    state.setSelectedLocalJobId?.(null);
    setRealDirection(direction);
  };

  const header = (
    <PageHeader
      title="Aktarım ve dönüşüm"
      description="Posta aktarımı, dosya dönüşümü, bölme ve kurtarma işlerini buradan başlatın: önce işlemi, sonra türünü seçin."
      testId="transfers-page-header"
      actions={companies.length > 0 ? (
        <div className="transfer-project-picker" data-testid="transfer-context-bar">
          <label>
            <span>Müşteri</span>
            <select
              className="select-input"
              value={currentCompany?.id || ''}
              onChange={(e) => handleCompanyChange(e.target.value)}
              data-testid="transfer-company-select"
            >
              {companies.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}{c.code ? ` (${c.code})` : ''}
                </option>
              ))}
            </select>
          </label>
          <label>
            <span>Proje</span>
            <select
              className="select-input"
              value={currentProject?.id || ''}
              onChange={(e) => handleProjectChange(e.target.value)}
              data-testid="transfer-project-select"
            >
              {companyProjects.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          </label>
        </div>
      ) : undefined}
    />
  );

  if (identityState?.identity && !currentProject) {
    return (
      <div className="transfer-hub" data-testid="transfers-tab-view">
        {header}
        <EmptyState
          title="Önce bir proje gerekiyor"
          testId="transfers-empty-state"
          action={<button className="btn btn-primary-orange" onClick={() => state.setCurrentTab('clients')} data-testid="transfers-goto-clients-btn">Müşteriler ekranına git</button>}
        >
          Her aktarım ve dönüşüm bir müşteri projesine kaydedilir. Müşteriler ekranında şirket ve proje oluşturun,
          posta hesabını projeye ekleyin, sonra buraya dönün.
        </EmptyState>
      </div>
    );
  }

  return (
    <div className="transfer-hub" data-testid="transfers-tab-view">
      {header}
      <section className="transfer-steps" aria-label="İşlem seçimi">
        <span className="step-label">1. İşlemi seçin</span>
        <div className="operation-grid" role="group" aria-label="İşlem türü">
          <button className={currentOperation === 'migration' ? 'active' : ''} aria-pressed={currentOperation === 'migration'} onClick={() => handleOperationTypeChange('migration')} data-testid="op-tab-migration"><strong>Posta aktarımı</strong><span>Posta hesapları ve dosyalar arasında kopyalayın</span></button>
          <button className={currentOperation === 'convert' ? 'active' : ''} aria-pressed={currentOperation === 'convert'} onClick={() => handleOperationTypeChange('convert')} data-testid="op-tab-convert"><strong>Dosya dönüşümü</strong><span>PST, OST, OLM, MBOX, EML ve Apple Mail dosyalarını dönüştürün</span></button>
          <button className={currentOperation === 'archive' ? 'active' : ''} aria-pressed={currentOperation === 'archive'} onClick={() => handleOperationTypeChange('archive')} data-testid="op-tab-archive"><strong>PST bölme</strong><span>Büyük PST / OST dosyasını yıla veya boyuta göre parçalara ayırın</span></button>
          <button className={currentOperation === 'recovery' ? 'active' : ''} aria-pressed={currentOperation === 'recovery'} onClick={() => handleOperationTypeChange('recovery')} data-testid="op-tab-recovery"><strong>Veri kurtarma</strong><span>Hasarlı PST / OST dosyasındaki okunabilen iletileri kurtarın</span></button>
        </div>

        {currentOperation === 'migration' && !identityState?.identity && (
          <div className="transfer-dev-mode" data-testid="transfer-mode-switcher">
            <span>Geliştirme görünümü:</span>
            <button type="button" className={`btn ${transferMode === 'real' ? 'btn-orange' : 'btn-outline-gray'}`} onClick={() => setTransferMode('real')} data-testid="mode-real-imap">Gerçek aktarım</button>
            <button type="button" className={`btn ${transferMode === 'sample' ? 'btn-orange' : 'btn-outline-gray'}`} onClick={() => setTransferMode('sample')} data-testid="mode-sample">Örnek mod</button>
            {transferMode === 'sample' && (
              <>
                <select
                  className="select-input"
                  aria-label="Örnek kaynak"
                  value={state.plan.sourceId}
                  onChange={(e) => handleSourceChange(e.target.value)}
                  data-testid="transfer-source-select"
                >
                  {projectSources.map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.name} ({s.kind === 'mailbox' ? 'Kutu' : 'Dosya'})
                    </option>
                  ))}
                </select>
                <span aria-hidden="true">→</span>
                <select
                  className="select-input"
                  aria-label="Örnek hedef"
                  value={state.plan.targetType}
                  onChange={(e) => {
                    const target = TARGET_OPTIONS.find((t) => t.name === e.target.value);
                    state.updatePlan((prev) => ({
                      ...prev,
                      targetType: e.target.value,
                      targetAccount: target?.accountOrTarget || prev.targetAccount,
                    }));
                  }}
                  data-testid="transfer-target-select"
                >
                  {TARGET_OPTIONS.map((t) => (
                    <option key={t.id} value={t.name}>
                      {t.name}
                    </option>
                  ))}
                </select>
              </>
            )}
          </div>
        )}

        {currentOperation === 'migration' && transferMode === 'real' && (
          <ChoiceSelector<RealMigrationDirection>
            label="2. Aktarım yönünü seçin"
            value={realDirection}
            onChange={selectDirection}
            testId="transfer-direction-selector"
            options={[
              { value: 'file-to-imap', label: 'Dosyadan hesaba', hint: 'EML / MBOX → posta hesabı', testId: 'tab-direction-file-to-imap' },
              { value: 'imap-to-file', label: 'Hesaptan dosyaya', hint: 'Posta hesabı → EML / MBOX', testId: 'tab-direction-imap-to-file' },
              { value: 'imap-to-imap', label: 'Hesaptan hesaba', hint: 'İki posta hesabı arasında', testId: 'tab-direction-imap-to-imap' },
              { value: 'pop-to-file', label: "POP'tan EML'e", hint: 'POP hesabı → EML klasörü', testId: 'tab-direction-pop-to-file' },
            ]}
          />
        )}
      </section>

      <div className="transfer-body">
        {renderOperationContent()}
      </div>
    </div>
  );
};
