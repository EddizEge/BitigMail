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
import { IconFile } from '../ui/Icons';
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
  const currentSource = projectSources.find((s) => s.id === state.plan.sourceId) || projectSources[0];

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

    if (currentOperation !== 'migration') {
      const titles: Record<JobType, string> = {
        migration: 'Posta Geçişi ve Taşıma',
        convert: 'OST / PST Format Dönüştürme',
        archive: 'Yıllık Arşivleme ve PST/MBOX Bölme',
        recovery: 'Bozuk Veri ve Posta Kurtarma',
      };

      const descriptions: Record<JobType, string> = {
        migration: 'Kaynak ve hedef arasında güvenli ve doğrulamalı posta taşıması.',
        convert: 'OST dosyasından yeni Unicode PST oluşturma ve yerel dönüştürme.',
        archive: 'Belirli tarih aralıklarına göre arşiv oluşturma ve boyut sınırına göre bölme.',
        recovery: 'Hasarlı PST/OST dosyalarındaki okunabilir iletileri yeni çıktıya kurtarma.',
      };

      return (
        <div style={{ padding: '32px 28px', maxWidth: '900px', width: '100%', margin: '0 auto' }}>
          <div className="card" style={{ padding: '32px', textAlign: 'center' }}>
            <div style={{ display: 'inline-flex', padding: '16px', background: 'var(--brand-yellow-light)', borderRadius: '50%', marginBottom: '16px' }}>
              <IconFile size={36} color="#b45309" />
            </div>

            <h2 style={{ fontSize: '1.25rem', fontWeight: 700, margin: '0 0 8px 0' }}>
              {titles[currentOperation]}
            </h2>
            <p style={{ color: 'var(--text-muted)', fontSize: '0.9375rem', maxWidth: '560px', margin: '0 auto 20px auto' }}>
              {descriptions[currentOperation]}
            </p>

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '14px', background: 'var(--bg-subtle)', padding: '16px', borderRadius: '8px', textAlign: 'left', marginBottom: '24px' }}>
              <div>
                <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>SEÇİLİ MÜŞTERİ</span>
                <div style={{ fontWeight: 600 }}>{currentCompany?.name || state.plan.client}</div>
              </div>
              <div>
                <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>SEÇİLİ PROJE</span>
                <div style={{ fontWeight: 600 }}>{currentProject?.name || state.plan.projectName || '—'}</div>
              </div>
              <div>
                <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>KAYNAK</span>
                <div style={{ fontWeight: 600 }}>{currentSource?.name || state.plan.sourceType}</div>
              </div>
              <div>
                <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>HEDEF</span>
                <div style={{ fontWeight: 600 }}>{state.plan.targetType}</div>
              </div>
            </div>

            <div style={{ background: '#fffbeb', border: '1px solid #fde68a', borderRadius: '6px', padding: '12px 16px', fontSize: '0.875rem', color: '#92400e', marginBottom: '24px', textAlign: 'left' }}>
              ⚠️ <strong>Sözleşme & Yol Haritası Bildirimi:</strong> Bu işlem türü BitigMail P2–P5 aşamalarında gerçek dönüşüm motorlarıyla entegre edilecektir. Prototip kapsamında çalışan uçtan uca simülasyon <strong>Taşıma</strong> modunda doğrulanmaktadır.
            </div>

            {!identityState?.identity && <button
              className="btn btn-orange"
              onClick={() => handleOperationTypeChange('migration')}
              data-testid="switch-to-migration-btn"
            >
              Taşıma simülasyonuna dön
            </button>}
          </div>
        </div>
      );
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
            onSelectBridge={(dir) => {
              state.setSelectedLocalJobId?.(null);
              setRealDirection(dir);
            }}
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
          onSelectImapToImap={() => {
            state.setSelectedLocalJobId?.(null);
            setRealDirection('imap-to-imap');
          }}
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
          embedded={Boolean(identityState?.identity)}
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

  return (
    <div className="transfer-hub" data-testid="transfers-tab-view">
      <section className="transfer-hero">
        <div><p className="identity-kicker">AKILLI POSTA İŞLEMLERİ</p><h1>Ne yapmak istiyorsunuz?</h1><p>İşlemi seçin; BitigMail yalnızca gerekli adımları gösterir.</p></div>
        <div className="operation-grid" role="group" aria-label="İşlem seçimi">
          <button className={currentOperation === 'migration' ? 'active' : ''} onClick={() => handleOperationTypeChange('migration')} data-testid="op-tab-migration"><strong>Posta aktarımı</strong><span>IMAP, POP veya dosya arasında taşıyın</span></button>
          <button className={currentOperation === 'convert' ? 'active' : ''} onClick={() => handleOperationTypeChange('convert')} data-testid="op-tab-convert"><strong>Dosya dönüşümü</strong><span>PST, OST, MBOX ve EML dönüştürün</span></button>
          <button className={currentOperation === 'archive' ? 'active' : ''} onClick={() => handleOperationTypeChange('archive')} data-testid="op-tab-archive"><strong>Arşiv ve bölme</strong><span>Doğrulayın, arşivleyin ve parçalara ayırın</span></button>
          <button className={currentOperation === 'recovery' ? 'active' : ''} onClick={() => handleOperationTypeChange('recovery')} data-testid="op-tab-recovery"><strong>Veri kurtarma</strong><span>Hasarlı posta depolarını güvenle tarayın</span></button>
        </div>
      </section>
      {/* Top Context & Operation Selection Bar */}
      <div
        style={{
          background: 'var(--bg-white)',
          borderBottom: '1px solid var(--border-light)',
          padding: '8px 16px',
          display: 'flex',
          flexWrap: 'wrap',
          gap: '8px 12px',
          alignItems: 'center',
          justifyContent: 'space-between',
          minWidth: 0,
          maxWidth: '100%',
          boxSizing: 'border-box',
        }}
        data-testid="transfer-context-bar"
      >
        {/* Left: Context Pickers (Company, Project, Source) */}
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px 10px', alignItems: 'center', minWidth: 0, maxWidth: '100%' }}>
          {/* Company Picker */}
          <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
            <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--text-muted)' }}>MÜŞTERİ:</span>
            <select
              className="select-input"
              style={{ fontSize: '0.8125rem', padding: '4px 8px', height: '30px' }}
              value={state.plan.companyId}
              onChange={(e) => handleCompanyChange(e.target.value)}
              data-testid="transfer-company-select"
            >
              {companies.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}{c.code ? ` (${c.code})` : ''}
                </option>
              ))}
            </select>
          </div>

          <span style={{ color: 'var(--border-mid)' }}>/</span>

          {/* Project Picker */}
          <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
            <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--text-muted)' }}>PROJE:</span>
            <select
              className="select-input"
              style={{ fontSize: '0.8125rem', padding: '4px 8px', height: '30px' }}
              value={state.plan.projectId}
              onChange={(e) => handleProjectChange(e.target.value)}
              data-testid="transfer-project-select"
            >
              {companyProjects.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          </div>

          {currentOperation === 'migration' && transferMode === 'sample' && (
            <>
              <span style={{ color: 'var(--border-mid)' }}>/</span>

              {/* Source Picker */}
              <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--text-muted)' }}>KAYNAK:</span>
                <select
                  className="select-input"
                  style={{ fontSize: '0.8125rem', padding: '4px 8px', height: '30px', maxWidth: '180px' }}
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
              </div>

              <span style={{ color: 'var(--border-mid)' }}>→</span>

              {/* Target Picker */}
              <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--text-muted)' }}>HEDEF:</span>
                <select
                  className="select-input"
                  style={{ fontSize: '0.8125rem', padding: '4px 8px', height: '30px' }}
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
              </div>
            </>
          )}
        </div>

        <div className="context-summary" aria-label="Seçili akış özeti"><span>{currentOperation === 'migration' && transferMode === 'real' ? (realDirection === 'file-to-imap' ? 'Dosya' : realDirection === 'imap-to-file' ? 'IMAP hesabı' : realDirection === 'imap-to-imap' ? 'IMAP hesabı' : 'POP hesabı') : currentCompany?.name || 'Kaynak seçin'}</span><b>→</b><span>{currentOperation === 'migration' && transferMode === 'real' ? (realDirection === 'file-to-imap' ? 'IMAP hesabı' : realDirection === 'imap-to-file' ? 'Dosya' : realDirection === 'imap-to-imap' ? 'IMAP hesabı' : 'EML klasörü') : currentOperation === 'migration' ? state.plan.targetType : currentOperation === 'convert' ? 'Dönüştürülmüş dosya' : currentOperation === 'archive' ? 'Doğrulanmış arşiv' : 'Kurtarılmış çıktı'}</span></div>
      </div>

      {/* Mode Switcher for Migration: Real IMAP vs Sample Mode */}
      {currentOperation === 'migration' && (
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            flexWrap: 'wrap',
            gap: '8px 16px',
            padding: '6px 16px',
            background: 'var(--bg-subtle)',
            borderBottom: '1px solid var(--border-light)',
          }}
          data-testid="transfer-mode-switcher"
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
            <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--text-muted)' }}>
              {identityState?.identity ? 'AKTARIM YÖNÜ:' : 'İŞ AKIŞI MODU:'}
            </span>
            {!identityState?.identity && <button
              type="button"
              className={`btn ${transferMode === 'real' ? 'btn-orange' : 'btn-outline-gray'}`}
              style={{ padding: '3px 10px', fontSize: '0.75rem' }}
              onClick={() => setTransferMode('real')}
              data-testid="mode-real-imap"
            >
              Gerçek Aktarım (IMAP / Köprü)
            </button>}
            {!identityState?.identity && <button
              type="button"
              className={`btn ${transferMode === 'sample' ? 'btn-orange' : 'btn-outline-gray'}`}
              style={{ padding: '3px 10px', fontSize: '0.75rem' }}
              onClick={() => setTransferMode('sample')}
              data-testid="mode-sample"
            >
              Örnek mod
            </button>}

            {transferMode === 'real' && (
              <>
                {!identityState?.identity && <span style={{ color: 'var(--border-mid)', margin: '0 4px' }}>|</span>}
                {!identityState?.identity && <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--text-muted)' }}>
                  YÖN:
                </span>}
                <button
                  type="button"
                  className={`btn ${realDirection === 'file-to-imap' ? 'btn-orange' : 'btn-outline-gray'}`}
                  style={{ padding: '3px 10px', fontSize: '0.75rem' }}
                  onClick={() => {
                    state.setSelectedLocalJobId?.(null);
                    setRealDirection('file-to-imap');
                  }}
                  data-testid="tab-direction-file-to-imap"
                >
                  Dosya → IMAP
                </button>
                <button
                  type="button"
                  className={`btn ${realDirection === 'imap-to-file' ? 'btn-orange' : 'btn-outline-gray'}`}
                  style={{ padding: '3px 10px', fontSize: '0.75rem' }}
                  onClick={() => {
                    state.setSelectedLocalJobId?.(null);
                    setRealDirection('imap-to-file');
                  }}
                  data-testid="tab-direction-imap-to-file"
                >
                  IMAP → Dosya
                </button>
                <button
                  type="button"
                  className={`btn ${realDirection === 'imap-to-imap' ? 'btn-orange' : 'btn-outline-gray'}`}
                  style={{ padding: '3px 10px', fontSize: '0.75rem' }}
                  onClick={() => {
                    state.setSelectedLocalJobId?.(null);
                    setRealDirection('imap-to-imap');
                  }}
                  data-testid="tab-direction-imap-to-imap"
                >
                  IMAP ↔ IMAP
                </button>
                <button
                  type="button"
                  className={`btn ${realDirection === 'pop-to-file' ? 'btn-orange' : 'btn-outline-gray'}`}
                  style={{ padding: '3px 10px', fontSize: '0.75rem' }}
                  onClick={() => { state.setSelectedLocalJobId?.(null); setRealDirection('pop-to-file'); }}
                  data-testid="tab-direction-pop-to-file"
                >
                  POP → EML
                </button>
              </>
            )}
          </div>
          <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>
            {transferMode === 'real'
              ? realDirection === 'imap-to-imap'
                ? 'Doğrudan IMAP sunucu bağlantısı ve kontrollü aktarım'
                : 'Dosya ↔ Posta Hesabı Köprüsü'
              : 'Statik şablon ve simülasyon görünümü'}
          </span>
        </div>
      )}

      {/* Main View for the selected operation */}
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0, minWidth: 0, maxWidth: '100%', overflow: 'hidden' }}>
        {renderOperationContent()}
      </div>
    </div>
  );
};
