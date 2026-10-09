import React, { useState } from 'react';
import { useBridgeTransfer } from '../../hooks/useBridgeTransfer';
import { LocalEngineClient, localEngineClient } from '../../api/localEngineClient';
import { LocalJobRecord, ImapAccountPublicDto } from '../../types/localEngine';
import {
  IconFile,
  IconServer,
  IconRefreshCw,
  IconPlay,
  IconDownload,
  IconFolder,
  IconCheck,
  IconArchiveBox,
} from '../ui/Icons';
import { Badge } from '../ui/Badge';
import { AdvancedFilterBuilder } from '../filters/AdvancedFilterBuilder';

export interface BridgeTransferWorkflowProps {
  companyId: string;
  companyName: string;
  projectId: string;
  projectName: string;
  onNavigateToAccounts?: () => void;
  onSelectImapToImap?: () => void;
  client?: LocalEngineClient;
  initialJob?: LocalJobRecord | null;
  initialDirection?: 'file-to-imap' | 'imap-to-file';
  onDirectionChange?: (dir: 'file-to-imap' | 'imap-to-file') => void;
  targetJobId?: string | null;
  onClearTargetJob?: () => void;
  onAddToArchive?: (job: LocalJobRecord) => void;
  embedded?: boolean;
}

export function getAccountBadgeText(acc: ImapAccountPublicDto): string {
  if (acc.authKind === 'microsoft365') {
    if (acc.tenantId?.toLowerCase() === 'consumers') {
      return 'Bireysel Outlook';
    }
    return 'Kurumsal Microsoft 365';
  }
  return 'Standart IMAP';
}

function formatCapacity(bytes: number): string {
  return `${(bytes / (1024 * 1024)).toFixed(2)} MiB`;
}

export const BridgeTransferWorkflow: React.FC<BridgeTransferWorkflowProps> = ({
  companyId,
  companyName,
  projectId,
  projectName,
  onNavigateToAccounts,
  onSelectImapToImap,
  client = localEngineClient,
  initialJob,
  initialDirection = 'file-to-imap',
  onDirectionChange,
  targetJobId,
  onClearTargetJob,
  onAddToArchive,
  embedded = false,
}) => {
  const {
    direction,
    setDirection,
    accounts,
    loadingAccounts,
    accountsError,
    loadAccounts,

    // File -> IMAP
    sourceMode,
    setSourceMode,
    sourceHandle,
    sourceDisplayPath,
    sourceTotalSize,
    sourceDescriptor,
    descriptorLoading,
    descriptorError,
    pickMimeSource,
    targetAccountId,
    setTargetAccountId,
    selectedSourceFolders,
    targetFolderMappings,
    toggleSourceFolder,
    selectAllSourceFolders,
    deselectAllSourceFolders,
    setFolderMapping,

    // IMAP -> File
    sourceAccountId,
    setSourceAccountId,
    sourceFolders,
    loadingFolders,
    foldersError,
    selectedImapFolders,
    toggleImapFolder,
    selectAllImapFolders,
    deselectAllImapFolders,
    targetDirHandle,
    targetDirName,
    pickOutputDir,
    targetFormat,
    setTargetFormat,

    // Filter
    startDate,
    endDate,
    setStartDate,
    setEndDate,
    advancedFilter,
    setAdvancedFilter,

    // Preview
    preview,
    previewLoading,
    previewError,
    createPreview,

    // Run & Resume
    activeJob,
    activeJobLoading,
    activeJobError,
    startTransfer,
    resumeTransfer,
    resumeLoading,
    resumeError,

    // Report
    report,
    loadReport,
  } = useBridgeTransfer({
    companyId,
    projectId,
    companyName,
    projectName,
    client,
    initialDirection,
    targetJobId,
    onClearTargetJob,
  });

  const [pickingSource, setPickingSource] = useState(false);
  const [pickingDir, setPickingDir] = useState(false);

  // A previously selected/completed local job must not override a new explicit top-level direction choice
  const currentJob = (() => {
    const job = activeJob || initialJob || null;
    if (!job) return null;
    if (direction === 'file-to-imap' && job.jobKind && job.jobKind !== 'bridge-import') return null;
    if (direction === 'imap-to-file' && job.jobKind && job.jobKind !== 'bridge-export') return null;
    return job;
  })();

  const effectiveCompanyName = currentJob?.clientContext?.companyName || companyName;
  const effectiveProjectName = currentJob?.clientContext?.projectName || projectName;

  const handleDirectionChange = (newDir: 'file-to-imap' | 'imap-to-file') => {
    setDirection(newDir);
    if (onClearTargetJob) {
      onClearTargetJob();
    }
    if (onDirectionChange) {
      onDirectionChange(newDir);
    }
  };

  const isPreviewMatchingDirection =
    preview &&
    ((direction === 'file-to-imap' && 'targetAccountId' in preview) ||
      (direction === 'imap-to-file' && 'targetDirHandle' in preview));

  const handlePickSource = async (mode: 'eml-files' | 'eml-tree' | 'mbox') => {
    setPickingSource(true);
    try {
      setSourceMode(mode);
      await pickMimeSource(mode);
    } finally {
      setPickingSource(false);
    }
  };

  const handlePickOutputDir = async () => {
    setPickingDir(true);
    try {
      await pickOutputDir();
    } finally {
      setPickingDir(false);
    }
  };

  const handleDownloadReport = async () => {
    const jId = currentJob?.jobId;
    if (!jId) return;
    try {
      const repData = report || (await loadReport(jId));
      const blob = new Blob([JSON.stringify(repData, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `bridge-aktarim-raporu-${jId}.json`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch {
      // ignore
    }
  };

  return (
    <div
      className="bridge-transfer-workflow"
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: '20px',
        padding: '24px 20px',
        maxWidth: '1200px',
        margin: '0 auto',
        width: '100%',
        boxSizing: 'border-box',
        minWidth: 0,
      }}
      data-testid="bridge-transfer-workflow"
    >
      {/* Header & Scope Banner */}
      {!embedded && <><div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'flex-start',
          flexWrap: 'wrap',
          gap: '12px',
          minWidth: 0,
          maxWidth: '100%',
        }}
      >
        <div style={{ minWidth: 0, maxWidth: '100%' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
            <IconServer size={22} color="var(--brand-orange)" />
            <h2 style={{ fontSize: '1.25rem', fontWeight: 700, margin: 0, color: 'var(--text-main)' }}>
              Dosya ve posta hesabı arasında aktarım
            </h2>
            <Badge variant="info">Gerçek Mod</Badge>
          </div>
          <p style={{ color: 'var(--text-muted)', fontSize: '0.875rem', margin: '4px 0 0 0' }}>
            {effectiveCompanyName} / {effectiveProjectName} · EML/MBOX dosyaları ile kayıtlı IMAP hesapları arasında güvenli, doğrulamalı ve kesintiden devam edebilir köprü.
          </p>
        </div>

        <div style={{ display: 'flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap' }}>
          <button
            className="btn btn-outline-gray"
            style={{ padding: '6px 12px', fontSize: '0.8125rem' }}
            onClick={loadAccounts}
            disabled={loadingAccounts}
            title="Hesapları yenile"
            data-testid="refresh-bridge-accounts-btn"
          >
            <IconRefreshCw size={14} className={loadingAccounts ? 'animate-spin' : ''} />
            <span>Hesapları yenile</span>
          </button>
          {onNavigateToAccounts && (
            <button
              className="btn btn-outline-orange"
              style={{ padding: '6px 12px', fontSize: '0.8125rem' }}
              onClick={onNavigateToAccounts}
              data-testid="bridge-manage-accounts-btn"
            >
              <span>Hesapları yönet</span>
            </button>
          )}
        </div>
      </div>

      {accountsError && (
        <div style={{ background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', padding: '10px 14px', fontSize: '0.8125rem', color: '#b91c1c' }} role="alert">
          <strong>Hata:</strong> {accountsError}
        </div>
      )}

      {/* Direction Selection Bar */}
      <div
        className="bridge-direction-selector card"
        style={{
          padding: '12px 16px',
          background: 'var(--bg-white)',
          border: '1px solid var(--border-light)',
          borderRadius: '8px',
          display: 'flex',
          flexDirection: 'column',
          gap: '10px',
        }}
        data-testid="bridge-direction-selector"
      >
        <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--text-muted)', letterSpacing: '0.5px' }}>
          Aktarım yönü
        </span>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '10px' }}>
          <button
            type="button"
            className={`btn ${direction === 'file-to-imap' ? 'btn-orange' : 'btn-outline-gray'}`}
            style={{ padding: '8px 16px', fontSize: '0.875rem', display: 'flex', alignItems: 'center', gap: '8px' }}
            onClick={() => handleDirectionChange('file-to-imap')}
            data-testid="direction-file-to-imap-btn"
          >
            <IconFile size={16} />
            <span>Dosya → IMAP (İçe Aktarım)</span>
          </button>

          <button
            type="button"
            className={`btn ${direction === 'imap-to-file' ? 'btn-orange' : 'btn-outline-gray'}`}
            style={{ padding: '8px 16px', fontSize: '0.875rem', display: 'flex', alignItems: 'center', gap: '8px' }}
            onClick={() => handleDirectionChange('imap-to-file')}
            data-testid="direction-imap-to-file-btn"
          >
            <IconServer size={16} />
            <span>IMAP → Dosya (Dışa Aktarım)</span>
          </button>

          {onSelectImapToImap && (
            <button
              type="button"
              className="btn btn-outline-gray"
              style={{ padding: '8px 16px', fontSize: '0.875rem', display: 'flex', alignItems: 'center', gap: '8px', marginLeft: 'auto' }}
              onClick={onSelectImapToImap}
              data-testid="direction-imap-to-imap-btn"
            >
              <IconRefreshCw size={16} />
              <span>IMAP ↔ IMAP Taşıma</span>
            </button>
          )}
        </div>
      </div></>}

      {embedded && accountsError && (
        <div className="notice notice-warning" role="alert"><strong>Hesaplar yüklenemedi</strong><p>{accountsError}</p></div>
      )}

      {embedded && <div className="bridge-embedded-actions workflow-actions-bar">{(effectiveCompanyName !== companyName || effectiveProjectName !== projectName) && <span>{`Açık iş kaydının projesi: ${effectiveCompanyName} / ${effectiveProjectName}`}</span>}<div><button className="btn btn-outline-gray" onClick={loadAccounts} disabled={loadingAccounts} data-testid="refresh-bridge-accounts-btn"><IconRefreshCw size={14}/><span>Hesapları yenile</span></button>{onNavigateToAccounts && <button className="btn btn-outline-orange" onClick={onNavigateToAccounts} data-testid="bridge-manage-accounts-btn">Hesapları yönet</button>}</div></div>}

      {/* Safety & User Copy Notices */}
      <div
        className="bridge-safety-notices"
        style={{
          display: 'flex',
          flexDirection: 'column',
          gap: '8px',
          background: '#f8fafc',
          border: '1px solid #e2e8f0',
          borderRadius: '8px',
          padding: '12px 16px',
          fontSize: '0.8125rem',
          color: '#334155',
        }}
        data-testid="bridge-safety-notices"
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <span style={{ color: '#a64f0c', fontWeight: 700 }}>Kaynak korunur:</span>
          <span>
            Kaynak posta ve dosyalar yalnız okunur; kaynaktaki iletiler silinmez veya değiştirilmez.
          </span>
        </div>
        <details><summary>Teknik ayrıntılar: biçim ve doğrulama</summary><p><strong>Standart biçim sınırları:</strong> EML ve MBOX biçimleri bazı IMAP üstverilerini gövdede taşımaz. Dışa aktarımda bu bilgiler doğrulama manifestine kaydedilir. <strong>Mboxrd kuralı:</strong> MBOX işlemlerinde standart mboxrd kullanılır.</p></details>
      </div>

      {/* Direction Form 1: File -> IMAP */}
      {direction === 'file-to-imap' && (
        <div
          className="bridge-import-form card"
          style={{
            padding: '20px',
            background: 'var(--bg-white)',
            border: '1px solid var(--border-light)',
            borderRadius: '8px',
            display: 'flex',
            flexDirection: 'column',
            gap: '16px',
          }}
          data-testid="bridge-import-form"
        >
          <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 700, color: 'var(--text-main)' }}>
            1. Kaynak dosya ve hedef hesap
          </h3>

          {/* Source Mode Pickers */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
            <span className="wf-caption">
              Kaynak türü ve dosya
            </span>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px' }}>
              <button
                type="button"
                className={`btn ${sourceMode === 'eml-files' && sourceHandle ? 'btn-orange' : 'btn-outline-gray'}`}
                style={{ padding: '6px 12px', fontSize: '0.8125rem' }}
                onClick={() => handlePickSource('eml-files')}
                disabled={pickingSource}
                data-testid="pick-eml-files-btn"
              >
                <span>EML dosyaları seç</span>
              </button>
              <button
                type="button"
                className={`btn ${sourceMode === 'eml-tree' && sourceHandle ? 'btn-orange' : 'btn-outline-gray'}`}
                style={{ padding: '6px 12px', fontSize: '0.8125rem' }}
                onClick={() => handlePickSource('eml-tree')}
                disabled={pickingSource}
                data-testid="pick-eml-tree-btn"
              >
                <span>EML klasör ağacı seç</span>
              </button>
              <button
                type="button"
                className={`btn ${sourceMode === 'mbox' && sourceHandle ? 'btn-orange' : 'btn-outline-gray'}`}
                style={{ padding: '6px 12px', fontSize: '0.8125rem' }}
                onClick={() => handlePickSource('mbox')}
                disabled={pickingSource}
                data-testid="pick-mbox-btn"
              >
                <span>MBOX dosyası (.mbox) seç</span>
              </button>
            </div>

            {sourceHandle && (
              <div
                style={{
                  background: '#f0fdf4',
                  border: '1px solid #bbf7d0',
                  borderRadius: '6px',
                  padding: '8px 12px',
                  fontSize: '0.8125rem',
                  color: '#166534',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '8px',
                }}
                data-testid="selected-source-handle-badge"
              >
                <IconCheck size={16} color="#166534" />
                <span>
                  <strong>Seçili kaynak:</strong> {sourceDisplayPath}{sourceTotalSize > 0 ? ` · ${(sourceTotalSize / (1024 * 1024)).toFixed(2)} MB` : ''}
                  <details style={{ display: 'inline-block', marginLeft: '6px', fontSize: '0.75rem', color: 'var(--text-muted)' }}>
                    <summary style={{ cursor: 'pointer', display: 'inline' }}>Teknik ayrıntılar</summary>
                    <span> (Tanıtıcı: {sourceHandle})</span>
                  </details>
                </span>
              </div>
            )}

            {descriptorLoading && (
              <span style={{ fontSize: '0.8125rem', color: 'var(--brand-orange)' }}>
                Kaynak klasörleri analiz ediliyor...
              </span>
            )}
            {descriptorError && (
              <span role="alert" style={{ fontSize: '0.8125rem', color: 'var(--status-error)' }}>
                {descriptorError}
              </span>
            )}
          </div>

          {/* Target Account Dropdown with Corporate/Personal labels */}
          <div className="wf-stack">
            <span className="wf-caption">
              Hedef posta hesabı
            </span>
            <select
              className="select-input"
              style={{ fontSize: '0.875rem', padding: '8px 12px', height: '40px', maxWidth: '400px' }}
              value={targetAccountId}
              onChange={(e) => setTargetAccountId(e.target.value)}
              data-testid="bridge-target-account-select"
            >
              <option value="">Hedef posta hesabını seçin</option>
              {accounts.map((acc) => {
                const badge = getAccountBadgeText(acc);
                return (
                  <option key={acc.accountId} value={acc.accountId}>
                    {acc.displayName || acc.email} ({badge}) · {acc.email}
                  </option>
                );
              })}
            </select>
          </div>

          {/* Discovered Folders & Mapping */}
          {sourceDescriptor && sourceDescriptor.folders.length > 0 && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', marginTop: '8px' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <span className="wf-caption">
                  KAYNAK KLASÖRLER VE HEDEF EŞLEŞTİRMELERİ ({selectedSourceFolders.length} / {sourceDescriptor.folders.length} seçili):
                </span>
                <div style={{ display: 'flex', gap: '8px' }}>
                  <button
                    type="button"
                    className="btn btn-outline-gray"
                    style={{ padding: '2px 8px', fontSize: '0.75rem' }}
                    onClick={selectAllSourceFolders}
                    data-testid="select-all-source-folders-btn"
                  >
                    Tümünü seç
                  </button>
                  <button
                    type="button"
                    className="btn btn-outline-gray"
                    style={{ padding: '2px 8px', fontSize: '0.75rem' }}
                    onClick={deselectAllSourceFolders}
                    data-testid="deselect-all-source-folders-btn"
                  >
                    Temizle
                  </button>
                </div>
              </div>

              <div
                style={{
                  border: '1px solid var(--border-light)',
                  borderRadius: '6px',
                  maxHeight: '220px',
                  overflowY: 'auto',
                }}
                data-testid="source-folders-table"
              >
                {sourceDescriptor.folders.map((fld) => {
                  const name = fld.folderName;
                  const count = fld.itemCount ?? 0;
                  const isChecked = selectedSourceFolders.includes(name);
                  const targetMapping = targetFolderMappings[name] ?? name;

                  return (
                    <div
                      key={name}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'space-between',
                        padding: '6px 12px',
                        borderBottom: '1px solid var(--border-light)',
                        gap: '12px',
                        flexWrap: 'wrap',
                      }}
                      data-testid={`source-folder-row-${name}`}
                    >
                      <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer', flex: 1, minWidth: '180px' }}>
                        <input
                          type="checkbox"
                          checked={isChecked}
                          onChange={() => toggleSourceFolder(name)}
                          data-testid={`checkbox-folder-${name}`}
                        />
                        <span style={{ fontWeight: 600, fontSize: '0.875rem' }}>{name}</span>
                        <span className="wf-note">({count} ileti)</span>
                      </label>

                      {isChecked && (
                        <div className="wf-row">
                          <span className="wf-note">Hedef klasör:</span>
                          <input
                            type="text"
                            className="text-input"
                            style={{ fontSize: '0.8125rem', padding: '4px 8px', width: '160px' }}
                            value={targetMapping}
                            onChange={(e) => setFolderMapping(name, e.target.value)}
                            placeholder={name}
                            data-testid={`mapping-input-${name}`}
                          />
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>
            </div>
          )}

          {/* Date Filter */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px', marginTop: '6px' }}>
            <span className="wf-caption">
              Tarih filtresi (isteğe bağlı, seçilen günler dahil)
            </span>
            <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
              <div className="wf-row">
                <span style={{ fontSize: '0.8125rem' }}>Başlangıç:</span>
                <input
                  type="date"
                  className="text-input"
                  style={{ fontSize: '0.8125rem', padding: '4px 8px' }}
                  value={startDate}
                  onChange={(e) => setStartDate(e.target.value)}
                  data-testid="bridge-start-date-input"
                />
              </div>
              <div className="wf-row">
                <span style={{ fontSize: '0.8125rem' }}>Bitiş:</span>
                <input
                  type="date"
                  className="text-input"
                  style={{ fontSize: '0.8125rem', padding: '4px 8px' }}
                  value={endDate}
                  onChange={(e) => setEndDate(e.target.value)}
                  data-testid="bridge-end-date-input"
                />
              </div>
            </div>
          </div>

          <AdvancedFilterBuilder testId="bridge-import-advanced-filter" value={advancedFilter} onChange={setAdvancedFilter} unknownCount={preview?.advancedFilterUnknownCount ?? 0} />

          {/* Preview Trigger */}
          <div style={{ marginTop: '8px' }}>
            <button
              type="button"
              className="btn btn-primary-orange"
              style={{ padding: '8px 18px', fontSize: '0.875rem' }}
              onClick={createPreview}
              disabled={previewLoading || !sourceHandle || !targetAccountId || selectedSourceFolders.length === 0}
              data-testid="create-bridge-import-preview-btn"
            >
              <IconPlay size={16} />
              <span>{previewLoading ? 'Önizleme Alınıyor...' : 'Önizleme ve doğrulama oluştur'}</span>
            </button>
            {previewError && (
              <p role="alert" style={{ color: 'var(--status-error)', fontSize: '0.8125rem', marginTop: '6px' }}>
                {previewError}
              </p>
            )}
          </div>
        </div>
      )}

      {/* Direction Form 2: IMAP -> File */}
      {direction === 'imap-to-file' && (
        <div
          className="bridge-export-form card"
          style={{
            padding: '20px',
            background: 'var(--bg-white)',
            border: '1px solid var(--border-light)',
            borderRadius: '8px',
            display: 'flex',
            flexDirection: 'column',
            gap: '16px',
          }}
          data-testid="bridge-export-form"
        >
          <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 700, color: 'var(--text-main)' }}>
            1. Kaynak hesap, klasörler ve çıktı biçimi
          </h3>

          {/* Source Account Dropdown */}
          <div className="wf-stack">
            <span className="wf-caption">
              Kaynak posta hesabı
            </span>
            <select
              className="select-input"
              style={{ fontSize: '0.875rem', padding: '8px 12px', height: '40px', maxWidth: '400px' }}
              value={sourceAccountId}
              onChange={(e) => setSourceAccountId(e.target.value)}
              data-testid="bridge-source-account-select"
            >
              <option value="">Kaynak posta hesabını seçin</option>
              {accounts.map((acc) => {
                const badge = getAccountBadgeText(acc);
                return (
                  <option key={acc.accountId} value={acc.accountId}>
                    {acc.displayName || acc.email} ({badge}) · {acc.email}
                  </option>
                );
              })}
            </select>
            {loadingFolders && (
              <span style={{ fontSize: '0.8125rem', color: 'var(--brand-orange)' }}>
                Klasör listesi sunucudan alınıyor...
              </span>
            )}
            {foldersError && (
              <span role="alert" style={{ fontSize: '0.8125rem', color: 'var(--status-error)' }}>
                {foldersError}
              </span>
            )}
          </div>

          {/* IMAP Folders Multiselect */}
          {sourceFolders.length > 0 && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <span className="wf-caption">
                  DIŞA AKTARILACAK KLASÖRLER ({selectedImapFolders.length} / {sourceFolders.length} seçili):
                </span>
                <div style={{ display: 'flex', gap: '8px' }}>
                  <button
                    type="button"
                    className="btn btn-outline-gray"
                    style={{ padding: '2px 8px', fontSize: '0.75rem' }}
                    onClick={selectAllImapFolders}
                    data-testid="select-all-imap-folders-btn"
                  >
                    Tümünü seç
                  </button>
                  <button
                    type="button"
                    className="btn btn-outline-gray"
                    style={{ padding: '2px 8px', fontSize: '0.75rem' }}
                    onClick={deselectAllImapFolders}
                    data-testid="deselect-all-imap-folders-btn"
                  >
                    Temizle
                  </button>
                </div>
              </div>

              <div
                style={{
                  border: '1px solid var(--border-light)',
                  borderRadius: '6px',
                  maxHeight: '200px',
                  overflowY: 'auto',
                }}
                data-testid="imap-folders-table"
              >
                {sourceFolders.map((fld) => {
                  const isChecked = selectedImapFolders.includes(fld.fullPath);
                  return (
                    <label
                      key={fld.fullPath}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '8px',
                        padding: '6px 12px',
                        borderBottom: '1px solid var(--border-light)',
                        cursor: 'pointer',
                      }}
                      data-testid={`imap-folder-row-${fld.fullPath}`}
                    >
                      <input
                        type="checkbox"
                        checked={isChecked}
                        onChange={() => toggleImapFolder(fld.fullPath)}
                        data-testid={`checkbox-imap-folder-${fld.fullPath}`}
                      />
                      <span style={{ fontWeight: 600, fontSize: '0.875rem' }}>{fld.fullPath}</span>
                      {fld.messageCount != null && (
                        <span className="wf-note">
                          ({fld.messageCount} ileti)
                        </span>
                      )}
                    </label>
                  );
                })}
              </div>
            </div>
          )}

          {/* Target Output Parent Directory Picker */}
          <div className="wf-stack">
            <span className="wf-caption">
              Çıktının yazılacağı üst klasör
            </span>
            <div style={{ display: 'flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap' }}>
              <button
                type="button"
                className="btn btn-outline-gray"
                style={{ padding: '6px 14px', fontSize: '0.8125rem' }}
                onClick={handlePickOutputDir}
                disabled={pickingDir}
                data-testid="pick-output-dir-btn"
              >
                <IconFolder size={14} />
                <span>Üst klasör seç</span>
              </button>
              {targetDirHandle && (
                <div
                  style={{
                    background: '#f0fdf4',
                    border: '1px solid #bbf7d0',
                    borderRadius: '6px',
                    padding: '6px 10px',
                    fontSize: '0.8125rem',
                    color: '#166534',
                  }}
                  data-testid="selected-output-dir-badge"
                >
                  ✓ Seçili Dizin: {targetDirName} (Tanıtıcı: {targetDirHandle})
                </div>
              )}
            </div>
          </div>

          {/* Target Output Format */}
          <div className="wf-stack">
            <span className="wf-caption">
              Çıktı biçimi
            </span>
            <div style={{ display: 'flex', gap: '16px', flexWrap: 'wrap' }}>
              <label style={{ display: 'flex', alignItems: 'center', gap: '6px', cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="bridgeTargetFormat"
                  value="eml-tree"
                  checked={targetFormat === 'eml-tree'}
                  onChange={() => setTargetFormat('eml-tree')}
                  data-testid="format-eml-tree-radio"
                />
                <span style={{ fontSize: '0.875rem' }}>EML klasör ağacı (her ileti ayrı .eml)</span>
              </label>

              <label style={{ display: 'flex', alignItems: 'center', gap: '6px', cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="bridgeTargetFormat"
                  value="mboxrd"
                  checked={targetFormat === 'mboxrd'}
                  onChange={() => setTargetFormat('mboxrd')}
                  data-testid="format-mboxrd-radio"
                />
                <span style={{ fontSize: '0.875rem' }}>Klasör başına bir MBOX dosyası (.mbox)</span>
              </label>
            </div>
          </div>

          {/* Date Filter */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px', marginTop: '6px' }}>
            <span className="wf-caption">
              Tarih filtresi (isteğe bağlı, seçilen günler dahil)
            </span>
            <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
              <div className="wf-row">
                <span style={{ fontSize: '0.8125rem' }}>Başlangıç:</span>
                <input
                  type="date"
                  className="text-input"
                  style={{ fontSize: '0.8125rem', padding: '4px 8px' }}
                  value={startDate}
                  onChange={(e) => setStartDate(e.target.value)}
                  data-testid="bridge-export-start-date-input"
                />
              </div>
              <div className="wf-row">
                <span style={{ fontSize: '0.8125rem' }}>Bitiş:</span>
                <input
                  type="date"
                  className="text-input"
                  style={{ fontSize: '0.8125rem', padding: '4px 8px' }}
                  value={endDate}
                  onChange={(e) => setEndDate(e.target.value)}
                  data-testid="bridge-export-end-date-input"
                />
              </div>
            </div>
          </div>

          <AdvancedFilterBuilder testId="bridge-export-advanced-filter" value={advancedFilter} onChange={setAdvancedFilter} unknownCount={preview?.advancedFilterUnknownCount ?? 0} />

          {/* Preview Trigger */}
          <div style={{ marginTop: '8px' }}>
            <button
              type="button"
              className="btn btn-primary-orange"
              style={{ padding: '8px 18px', fontSize: '0.875rem' }}
              onClick={createPreview}
              disabled={previewLoading || !sourceAccountId || !targetDirHandle || selectedImapFolders.length === 0}
              data-testid="create-bridge-export-preview-btn"
            >
              <IconPlay size={16} />
              <span>{previewLoading ? 'Önizleme Alınıyor...' : 'Önizleme ve doğrulama oluştur'}</span>
            </button>
            {previewError && (
              <p role="alert" style={{ color: 'var(--status-error)', fontSize: '0.8125rem', marginTop: '6px' }}>
                {previewError}
              </p>
            )}
          </div>
        </div>
      )}

      {/* Immutable Server Preview Card */}
      {isPreviewMatchingDirection && preview && (
        <div
          className="bridge-preview-card card"
          style={{
            padding: '20px',
            background: '#fafafa',
            border: '1px solid var(--border-mid)',
            borderRadius: '8px',
            display: 'flex',
            flexDirection: 'column',
            gap: '14px',
          }}
          data-testid="bridge-preview-card"
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '8px' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <span style={{ fontWeight: 700, fontSize: '1rem', color: 'var(--text-main)' }}>
                Doğrulanmış önizleme planı
              </span>
              <Badge variant={preview.canTransfer ? 'success' : 'error'}>
                {preview.canTransfer ? 'Aktarıma hazır' : 'Ön Kontrol Engeli Var'}
              </Badge>
            </div>
            <details className="wf-note">
              <summary style={{ cursor: 'pointer', display: 'inline' }}>Plan ayrıntısı</summary>
              <span> Plan No: {preview.previewId}</span>
            </details>
          </div>

          {/* Metrics Grid */}
          <div
            style={{
              display: 'grid',
              gridTemplateColumns: 'repeat(auto-fit, minmax(140px, 1fr))',
              gap: '12px',
            }}
          >
            <div className="wf-panel">
              <span className="wf-note">Toplam kaynak</span>
              <div style={{ fontSize: '1.25rem', fontWeight: 700 }} data-testid="preview-total-items">
                {preview.totalSourceItems}
              </div>
            </div>

            {'estimatedRequiredBytes' in preview && preview.estimatedRequiredBytes != null && (
              <div className="wf-panel">
                <span className="wf-note">Tahmini gereken alan</span>
                <div style={{ fontSize: '1rem', fontWeight: 700 }} data-testid="preview-estimated-required-bytes">
                  {formatCapacity(preview.estimatedRequiredBytes)}
                </div>
              </div>
            )}

            {'availableFreeBytes' in preview && preview.availableFreeBytes != null && (
              <div className="wf-panel">
                <span className="wf-note">Kullanılabilir alan</span>
                <div style={{ fontSize: '1rem', fontWeight: 700 }} data-testid="preview-available-free-bytes">
                  {formatCapacity(preview.availableFreeBytes)}
                </div>
              </div>
            )}

            <div className="wf-panel">
              <span style={{ fontSize: '0.75rem', color: 'var(--status-success)' }}>Uygun İletiler</span>
              <div style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--status-success)' }} data-testid="preview-eligible-items">
                {preview.eligibleItemsCount}
              </div>
            </div>

            <div className="wf-panel">
              <span className="wf-note">Filtre dışı</span>
              <div style={{ fontSize: '1.25rem', fontWeight: 700 }} data-testid="preview-excluded-items">
                {preview.excludedCount}
              </div>
            </div>

            {'deletedExcludedCount' in preview && (preview as any).deletedExcludedCount > 0 && (
              <div className="wf-panel">
                <span style={{ fontSize: '0.75rem', color: 'var(--status-error)' }}>Silinmiş (\\Deleted)</span>
                <div style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--status-error)' }} data-testid="preview-deleted-excluded">
                  {(preview as any).deletedExcludedCount}
                </div>
              </div>
            )}
          </div>

          {'qualificationWarnings' in preview && preview.qualificationWarnings && preview.qualificationWarnings.length > 0 && (
            <div className="mime-notice" data-testid="bridge-qualification-warnings">
              <strong>Kaynak sadakat uyarıları</strong>
              {preview.qualificationWarnings.map((warning, index) => <span key={index}>{warning}</span>)}
            </div>
          )}

          {/* Folder Breakdown */}
          {preview.folders && preview.folders.length > 0 && (
            <div className="wf-stack">
              <span className="wf-caption">
                Klasör dökümü
              </span>
              <div style={{ border: '1px solid var(--border-light)', borderRadius: '6px', background: '#ffffff', overflow: 'hidden' }}>
                {preview.folders.map((f, idx) => (
                  <div
                    key={idx}
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      padding: '6px 12px',
                      borderBottom: idx === preview.folders.length - 1 ? 'none' : '1px solid var(--border-light)',
                      fontSize: '0.8125rem',
                    }}
                  >
                    <span>
                      <strong>{f.sourceFolder}</strong> → <em>{f.targetFolder}</em>
                    </span>
                    <span style={{ color: 'var(--text-muted)' }}>
                      {f.eligibleItems} / {f.totalItems} ileti uygun
                    </span>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Blocker Alert */}
          {!preview.canTransfer && (
            <div
              style={{
                background: '#fef2f2',
                border: '1px solid #fecaca',
                borderRadius: '6px',
                padding: '10px 14px',
                color: '#991b1b',
                fontSize: '0.875rem',
              }}
              role="alert"
              data-testid="preview-blocker-alert"
            >
              <strong>Ön kontrol engeli:</strong> {preview.blockerReason || 'Aktarım başlatılamaz.'}
            </div>
          )}

          {/* Start Action */}
          <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
            <button
              type="button"
              className="btn btn-primary-orange"
              style={{ padding: '10px 24px', fontSize: '0.9375rem', fontWeight: 600 }}
              onClick={startTransfer}
              disabled={activeJobLoading || !preview.canTransfer || preview.eligibleItemsCount === 0}
              data-testid="start-bridge-transfer-btn"
            >
              <IconPlay size={16} />
              <span>{activeJobLoading ? 'Aktarım Başlatılıyor...' : 'Başlat / sıraya ekle'}</span>
            </button>
            {activeJobError && (
              <span role="alert" style={{ color: 'var(--status-error)', fontSize: '0.8125rem' }}>
                {activeJobError}
              </span>
            )}
          </div>
        </div>
      )}

      {/* Active Job / Progress Monitor */}
      {currentJob && (
        <div
          className="bridge-job-monitor card"
          style={{
            padding: '20px',
            background: 'var(--bg-white)',
            border: '1px solid var(--border-light)',
            borderRadius: '8px',
            display: 'flex',
            flexDirection: 'column',
            gap: '14px',
          }}
          data-testid="bridge-job-monitor"
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '8px' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <span style={{ fontWeight: 700, fontSize: '1rem', color: 'var(--text-main)' }}>
                İş Durumu: {currentJob.jobId}
              </span>
              <Badge
                variant={
                  currentJob.status === 'completed'
                    ? 'success'
                    : currentJob.status === 'failed' || currentJob.status === 'interrupted'
                    ? 'error'
                    : 'warning'
                }
              >
                {currentJob.status === 'completed'
                  ? 'Tamamlandı'
                  : currentJob.status === 'failed' || currentJob.status === 'interrupted'
                  ? 'Müdahale bekliyor'
                  : 'Çalışıyor'}
              </Badge>
            </div>
            <span className="wf-note">
              Aşama: {currentJob.stage}
            </span>
          </div>

          {/* Progress Bar */}
          <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
            <div className="progress-track" style={{ flex: 1 }}>
              <div
                className="progress-fill"
                style={{ width: (currentJob.status === 'verifying' ? (currentJob.phaseTotal && currentJob.phaseCompleted != null ? Math.min(100, currentJob.phaseCompleted / currentJob.phaseTotal * 100) : 0) : currentJob.status === 'completed' ? 100 : Math.min(99, currentJob.percentComplete)) + '%' }}
                data-testid="bridge-job-progress-bar"
              />
            </div>
            <span style={{ fontSize: '0.875rem', fontWeight: 600 }}>{currentJob.status === 'verifying' ? 'Son doğrulama: ' + (currentJob.phaseCompleted ?? '—') + ' / ' + (currentJob.phaseTotal ?? '—') + ' ileti' : '%' + (currentJob.status === 'completed' ? 100 : Math.min(99, currentJob.percentComplete))}</span>
          </div>

          {/* Counts */}
          <div style={{ display: 'flex', gap: '20px', fontSize: '0.875rem', flexWrap: 'wrap' }}>
            <div>
              <span style={{ color: 'var(--text-muted)' }}>İşlenen: </span>
              <strong data-testid="bridge-job-written-count">{currentJob.itemsWritten}</strong> / {currentJob.totalItems || currentJob.itemsRead}
            </div>
            <div>
              <span style={{ color: 'var(--text-muted)' }}>Başarısız: </span>
              <strong style={{ color: currentJob.failedItems > 0 ? 'var(--status-error)' : 'inherit' }}>
                {currentJob.failedItems}
              </strong>
            </div>
          </div>

          {/* Error & Resume */}
          {(currentJob.status === 'interrupted' || currentJob.status === 'failed') && (
            <div
              style={{
                background: '#fef2f2',
                border: '1px solid #fecaca',
                borderRadius: '6px',
                padding: '12px 14px',
                display: 'flex',
                flexDirection: 'column',
                gap: '8px',
              }}
              data-testid="bridge-job-error-state"
            >
              <div style={{ color: '#991b1b', fontSize: '0.875rem', fontWeight: 600 }}>
                {currentJob.errorMessage || 'İşlem kesintiye uğradı veya durduruldu.'}
              </div>
              <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                {currentJob.status === 'interrupted' && !currentJob.errorMessage?.includes('reauthorization_required') && <button
                  type="button"
                  className="btn btn-orange"
                  style={{ padding: '6px 14px', fontSize: '0.8125rem' }}
                  onClick={() => resumeTransfer(currentJob.jobId)}
                  disabled={resumeLoading}
                  data-testid="bridge-job-resume-btn"
                >
                  <IconPlay size={14} />
                  <span>{resumeLoading ? 'Devam Ettiriliyor...' : 'Kesintiden Devam Et'}</span>
                </button>}
                {currentJob.errorMessage?.includes('reauthorization_required') && <span>Devam etmeden önce hesap bağlantısını yeniden kurun.</span>}
                {resumeError && (
                  <span role="alert" style={{ color: 'var(--status-error)', fontSize: '0.8125rem' }}>
                    {resumeError}
                  </span>
                )}
              </div>
            </div>
          )}

          {/* Download Report and Add To Archive buttons */}
          {currentJob.status === 'completed' && (
            <div style={{ marginTop: '6px', display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
              <button
                type="button"
                className="btn btn-outline-gray"
                style={{ padding: '6px 14px', fontSize: '0.8125rem', display: 'flex', alignItems: 'center', gap: '6px' }}
                onClick={handleDownloadReport}
                data-testid="bridge-download-report-btn"
              >
                <IconDownload size={14} />
                <span>Aktarım raporunu indir (JSON)</span>
              </button>
              {currentJob.jobKind === 'bridge-export' && onAddToArchive && (
                <button
                  type="button"
                  className="btn btn-orange"
                  style={{ padding: '6px 14px', fontSize: '0.8125rem', display: 'flex', alignItems: 'center', gap: '6px' }}
                  onClick={() => onAddToArchive(currentJob)}
                  data-testid="bridge-export-add-to-archive-btn"
                >
                  <IconArchiveBox size={14} />
                  <span>Arşive ekle</span>
                </button>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
};
