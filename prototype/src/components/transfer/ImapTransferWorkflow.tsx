import React from 'react';
import { useImapTransfer } from '../../hooks/useImapTransfer';
import {
  IconServer,
  IconRefreshCw,
  IconPlay,
  IconAlertTriangle,
  IconDownload,
  IconFolder,
  IconClock,
} from '../ui/Icons';
import { Badge } from '../ui/Badge';
import { LocalEngineClient } from '../../api/localEngineClient';
import { LocalJobRecord } from '../../types/localEngine';
import { AdvancedFilterBuilder } from '../filters/AdvancedFilterBuilder';
import { TechnicalDetails } from '../layout/PageHeader';

interface ImapTransferWorkflowProps {
  companyId: string;
  companyName: string;
  projectId: string;
  projectName: string;
  onNavigateToAccounts?: () => void;
  onSelectBridge?: (direction: 'file-to-imap' | 'imap-to-file') => void;
  client?: LocalEngineClient;
  initialJob?: LocalJobRecord | null;
  /** Aktarım sekmesine gömülüyken başlık, müşteri/proje ve yön seçici üst ekranda gösterilir. */
  embedded?: boolean;
}

export const ImapTransferWorkflow: React.FC<ImapTransferWorkflowProps> = ({
  companyId,
  companyName,
  projectId,
  projectName,
  onNavigateToAccounts,
  onSelectBridge,
  client,
  initialJob,
  embedded = false,
}) => {
  const {
    accounts,
    loadingAccounts,
    accountsError,
    sourceAccountId,
    targetAccountId,
    sourceFolders,
    loadingFolders,
    foldersError,
    selectedFolderPaths,
    folderMappings,
    startDate,
    endDate,
    advancedFilter,
    preview,
    previewLoading,
    previewError,
    activeJob,
    activeJobLoading,
    activeJobError,
    resumeLoading,
    resumeError,
    report,
    reportLoading,
    reportError,
    loadReport,
    loadAccounts,
    setSourceAccountId,
    setTargetAccountId,
    toggleFolderSelection,
    selectAllFolders,
    deselectAllFolders,
    setFolderMapping,
    setStartDate,
    setEndDate,
    setAdvancedFilter,
    createPreview,
    startTransfer,
    resumeTransfer,
    resetTransfer,
  } = useImapTransfer({
    companyId,
    projectId,
    companyName,
    projectName,
    client,
  });

  const currentJob = activeJob || initialJob || null;

  const sourceAccount = accounts.find((a) => a.accountId === sourceAccountId);
  const targetAccount = accounts.find((a) => a.accountId === targetAccountId);

  const isSameAccount = Boolean(sourceAccountId && targetAccountId && sourceAccountId === targetAccountId);
  const canRequestPreview = Boolean(
    sourceAccountId &&
    targetAccountId &&
    !isSameAccount &&
    selectedFolderPaths.length > 0 &&
    !previewLoading
  );

  const handleDownloadReport = () => {
    if (!report && !currentJob?.jobId) return;
    const reportData = report || { jobId: currentJob?.jobId, imapTransfer: currentJob?.imapTransfer };
    const blob = new Blob([JSON.stringify(reportData, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `imap-aktarim-raporu-${currentJob?.jobId || 'is'}.json`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '20px', padding: '24px 28px', maxWidth: '1100px', margin: '0 auto', width: '100%', boxSizing: 'border-box' }} data-testid="imap-transfer-workflow">
      {/* Header & Scope Banner */}
      {embedded ? (
        <div className="workflow-actions-bar">
          <button className="btn btn-outline-gray" onClick={loadAccounts} disabled={loadingAccounts} data-testid="refresh-imap-transfer-accounts-btn">
            <IconRefreshCw size={14} className={loadingAccounts ? 'animate-spin' : ''} />
            <span>Hesapları yenile</span>
          </button>
          {onNavigateToAccounts && (
            <button className="btn btn-outline-orange" onClick={onNavigateToAccounts} data-testid="goto-client-accounts-btn">Hesapları yönet</button>
          )}
        </div>
      ) : (
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: '12px' }}>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <IconServer size={22} color="var(--brand-orange)" />
            <h2 style={{ fontSize: '1.25rem', fontWeight: 700, margin: 0, color: 'var(--text-main)' }}>
              Hesaptan hesaba aktarım
            </h2>
            <Badge variant="info">Gerçek Mod</Badge>
          </div>
          <p style={{ color: 'var(--text-muted)', fontSize: '0.875rem', margin: '4px 0 0 0' }}>
            {companyName} / {projectName} projesine kayıtlı IMAP hesapları arasında deterministik, doğrulamalı ve kesintiden devam edebilir kopyalama.
          </p>
        </div>

        <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
          <button
            className="btn btn-outline-gray"
            style={{ padding: '6px 12px', fontSize: '0.8125rem' }}
            onClick={loadAccounts}
            disabled={loadingAccounts}
            title="Hesapları yenile"
            data-testid="refresh-imap-transfer-accounts-btn"
          >
            <IconRefreshCw size={14} className={loadingAccounts ? 'animate-spin' : ''} />
            <span>Hesapları yenile</span>
          </button>
          {onNavigateToAccounts && (
            <button
              className="btn btn-outline-orange"
              style={{ padding: '6px 12px', fontSize: '0.8125rem' }}
              onClick={onNavigateToAccounts}
              data-testid="goto-client-accounts-btn"
            >
              <span>Hesapları yönet</span>
            </button>
          )}
        </div>
      </div>
      )}

      {onSelectBridge && !embedded && (
        <div
          className="card"
          style={{
            padding: '10px 16px',
            background: 'var(--bg-white)',
            border: '1px solid var(--border-light)',
            borderRadius: '8px',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            flexWrap: 'wrap',
            gap: '8px',
          }}
          data-testid="imap-to-bridge-switcher"
        >
          <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--text-muted)' }}>
            Diğer aktarım yönleri
          </span>
          <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
            <button
              type="button"
              className="btn btn-outline-gray"
              style={{ padding: '4px 10px', fontSize: '0.75rem' }}
              onClick={() => onSelectBridge('file-to-imap')}
              data-testid="switch-to-file-to-imap-btn"
            >
              Dosya → IMAP (İçe Aktarım)
            </button>
            <button
              type="button"
              className="btn btn-outline-gray"
              style={{ padding: '4px 10px', fontSize: '0.75rem' }}
              onClick={() => onSelectBridge('imap-to-file')}
              data-testid="switch-to-imap-to-file-btn"
            >
              IMAP → Dosya (Dışa Aktarım)
            </button>
          </div>
        </div>
      )}

      {accountsError && (
        <div style={{ background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', padding: '12px 16px', fontSize: '0.875rem', color: '#b91c1c' }}>
          <strong>Hata:</strong> {accountsError}
        </div>
      )}

      {/* Step 1: Account Selection */}
      <div className="card" style={{ padding: '20px' }}>
        <h3 style={{ fontSize: '1rem', fontWeight: 600, margin: '0 0 14px 0', display: 'flex', alignItems: 'center', gap: '8px' }}>
          <span>1. Kaynak ve hedef hesap</span>
        </h3>

        {accounts.length === 0 && !loadingAccounts ? (
          <div style={{ background: '#fffbeb', border: '1px solid #fde68a', borderRadius: '6px', padding: '14px', fontSize: '0.875rem', color: '#92400e' }}>
            Bu projeye kayıtlı IMAP hesabı bulunmuyor. Aktarım yapabilmek için lütfen önce Müşteriler bölümünden en az iki IMAP hesabı ekleyin.
          </div>
        ) : (
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '16px' }}>
            {/* Source Account Picker */}
            <div>
              <label htmlFor="source-account-select" style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '6px' }}>
                Kaynak hesap (kopyalanacak posta kutusu) *
              </label>
              <select
                id="source-account-select"
                className="text-input"
                value={sourceAccountId}
                onChange={(e) => setSourceAccountId(e.target.value)}
                disabled={Boolean(currentJob && (currentJob.status === 'converting' || currentJob.status === 'verifying'))}
                data-testid="source-account-select"
              >
                <option value="">Kaynak hesabı seçin</option>
                {accounts.map((acc) => {
                  const isPersonal = acc.authKind === 'microsoft365' && acc.tenantId === 'consumers';
                  const isCorporate = acc.authKind === 'microsoft365' && acc.tenantId !== 'consumers';
                  let authLabel = `${acc.host}:${acc.port} [${acc.tlsMode}]`;
                  if (isPersonal) {
                    authLabel = 'Kişisel Outlook [OAuth2]';
                  } else if (isCorporate) {
                    authLabel = 'Microsoft 365 [OAuth2] (Kurumsal)';
                  } else if (acc.authKind === 'google') {
                    authLabel = 'Gmail / Workspace [OAuth2]';
                  }
                  return (
                    <option key={acc.accountId} value={acc.accountId}>
                      {acc.displayName} ({acc.email}) - {authLabel}
                    </option>
                  );
                })}
              </select>
              {sourceAccount && (
                <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '4px' }}>
                  Sunucu: <code>{sourceAccount.host}:{sourceAccount.port}</code> ({sourceAccount.authKind === 'google' ? 'Google OAuth2' : sourceAccount.authKind === 'microsoft365' ? (sourceAccount.tenantId === 'consumers' ? 'Kişisel Outlook OAuth2' : 'Microsoft 365 Kurumsal OAuth2') : `TLS: ${sourceAccount.tlsMode}`}) · Sürüm: v{sourceAccount.version}
                </div>
              )}
            </div>

            {/* Target Account Picker */}
            <div>
              <label htmlFor="target-account-select" style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '6px' }}>
                Hedef hesap (yazılacak posta kutusu) *
              </label>
              <select
                id="target-account-select"
                className="text-input"
                value={targetAccountId}
                onChange={(e) => setTargetAccountId(e.target.value)}
                disabled={Boolean(currentJob && (currentJob.status === 'converting' || currentJob.status === 'verifying'))}
                data-testid="target-account-select"
              >
                <option value="">Hedef hesabı seçin</option>
                {accounts.map((acc) => {
                  const isPersonal = acc.authKind === 'microsoft365' && acc.tenantId === 'consumers';
                  const isCorporate = acc.authKind === 'microsoft365' && acc.tenantId !== 'consumers';
                  let authLabel = `${acc.host}:${acc.port} [${acc.tlsMode}]`;
                  if (isPersonal) {
                    authLabel = 'Kişisel Outlook [OAuth2]';
                  } else if (isCorporate) {
                    authLabel = 'Microsoft 365 [OAuth2] (Kurumsal)';
                  } else if (acc.authKind === 'google') {
                    authLabel = 'Gmail / Workspace [OAuth2]';
                  }
                  return (
                    <option key={acc.accountId} value={acc.accountId}>
                      {acc.displayName} ({acc.email}) - {authLabel}
                    </option>
                  );
                })}
              </select>
              {targetAccount && (
                <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '4px' }}>
                  Sunucu: <code>{targetAccount.host}:{targetAccount.port}</code> ({targetAccount.authKind === 'google' ? 'Google OAuth2' : targetAccount.authKind === 'microsoft365' ? (targetAccount.tenantId === 'consumers' ? 'Kişisel Outlook OAuth2' : 'Microsoft 365 Kurumsal OAuth2') : `TLS: ${targetAccount.tlsMode}`}) · Sürüm: v{targetAccount.version}
                </div>
              )}
            </div>
          </div>
        )}

        {isSameAccount && (
          <div style={{ marginTop: '12px', background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', padding: '10px 14px', fontSize: '0.8125rem', color: '#b91c1c' }}>
            <IconAlertTriangle size={16} style={{ display: 'inline', verticalAlign: 'middle', marginRight: '6px' }} />
            Kaynak ve hedef hesap aynı olamaz. Lütfen farklı bir hedef hesap seçin.
          </div>
        )}
      </div>

      {/* Step 2: Folder Selection & Target Mapping */}
      {sourceAccountId && (
        <div className="card" style={{ padding: '20px' }} data-testid="folder-selection-card">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px', flexWrap: 'wrap', gap: '8px' }}>
            <h3 style={{ fontSize: '1rem', fontWeight: 600, margin: 0 }}>
              2. Kaynak klasörler ve hedef eşlemesi
            </h3>
            <div style={{ display: 'flex', gap: '8px' }}>
              <button
                type="button"
                className="btn btn-outline-gray"
                style={{ padding: '4px 10px', fontSize: '0.75rem' }}
                onClick={selectAllFolders}
                data-testid="select-all-folders-btn"
              >
                Tümünü seç
              </button>
              <button
                type="button"
                className="btn btn-outline-gray"
                style={{ padding: '4px 10px', fontSize: '0.75rem' }}
                onClick={deselectAllFolders}
                data-testid="deselect-all-folders-btn"
              >
                Temizle
              </button>
            </div>
          </div>

          {loadingFolders ? (
            <div style={{ padding: '20px', textAlign: 'center', color: 'var(--text-muted)', fontSize: '0.875rem' }}>
              <IconRefreshCw size={18} className="animate-spin" style={{ display: 'inline', marginRight: '8px' }} />
              Kaynak IMAP sunucusundan klasör yapısı ve ileti sayıları okunuyor...
            </div>
          ) : foldersError ? (
            <div style={{ background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', padding: '12px', fontSize: '0.875rem', color: '#b91c1c' }}>
              {foldersError}
            </div>
          ) : sourceFolders.length === 0 ? (
            <div style={{ color: 'var(--text-muted)', fontSize: '0.875rem' }}>
              Bu hesapta taranabilir klasör bulunamadı.
            </div>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
              <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
                Seçilen her klasör için hedef sunucuda yazılacak klasör yolunu belirleyebilirsiniz. (Alt klasörler kendiliğinden eklenmez; exact eşleme uygulanır).
              </div>

              <div style={{ border: '1px solid var(--border-light)', borderRadius: '6px', overflow: 'hidden' }}>
                <table className="data-table" style={{ width: '100%', borderCollapse: 'collapse' }} data-testid="folder-mappings-table">
                  <thead>
                    <tr style={{ background: 'var(--bg-subtle)', textAlign: 'left', fontSize: '0.75rem' }}>
                      <th style={{ width: '40px', padding: '8px 12px' }}>Seç</th>
                      <th style={{ padding: '8px 12px' }}>Kaynak klasör (tam yol)</th>
                      <th style={{ width: '120px', padding: '8px 12px' }}>İleti Sayısı</th>
                      <th style={{ padding: '8px 12px' }}>Hedef klasör yolu</th>
                    </tr>
                  </thead>
                  <tbody>
                    {sourceFolders.map((f) => {
                      const isSelected = selectedFolderPaths.includes(f.fullPath);
                      const targetPath = folderMappings[f.fullPath] || f.fullPath;
                      return (
                        <tr key={f.fullPath} style={{ borderTop: '1px solid var(--border-light)', background: isSelected ? 'var(--brand-orange-light)' : 'transparent' }}>
                          <td style={{ padding: '8px 12px' }}>
                            <input
                              type="checkbox"
                              checked={isSelected}
                              disabled={!f.isSelectable}
                              onChange={() => toggleFolderSelection(f.fullPath)}
                              aria-label={`Klasör seç: ${f.fullPath}`}
                              data-testid={`folder-checkbox-${f.fullPath}`}
                            />
                          </td>
                          <td style={{ padding: '8px 12px', fontSize: '0.8125rem' }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                              <IconFolder size={14} color="var(--brand-orange)" />
                              <span style={{ fontWeight: isSelected ? 600 : 400 }}>{f.fullPath}</span>
                              {!f.isSelectable && (
                                <Badge variant="neutral">Seçilemez</Badge>
                              )}
                            </div>
                          </td>
                          <td style={{ padding: '8px 12px', fontSize: '0.8125rem' }}>
                            {f.countValid ? (
                              <span>{f.messageCount ?? 0} ileti</span>
                            ) : (
                              <span style={{ color: '#b45309' }} title={f.statusError || 'İleti sayısı okunamadı'}>
                                Okunamadı ⚠️
                              </span>
                            )}
                          </td>
                          <td style={{ padding: '8px 12px' }}>
                            {isSelected ? (
                              <input
                                type="text"
                                className="text-input"
                                style={{ padding: '4px 8px', fontSize: '0.8125rem' }}
                                value={targetPath}
                                onChange={(e) => setFolderMapping(f.fullPath, e.target.value)}
                                placeholder="Hedef klasör adı"
                                aria-label={`Hedef klasör: ${f.fullPath}`}
                                data-testid={`folder-target-input-${f.fullPath}`}
                              />
                            ) : (
                              <span style={{ color: 'var(--text-muted)', fontSize: '0.75rem' }}>—</span>
                            )}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>

              {selectedFolderPaths.length === 0 && (
                <div style={{ fontSize: '0.8125rem', color: '#b91c1c' }}>
                  En az bir klasör seçilmelidir. Boş klasör seçimi aktarıma uygun değildir.
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {/* Step 3: Optional Date Filter */}
      {sourceAccountId && (
        <div className="card" style={{ padding: '20px' }}>
          <h3 style={{ fontSize: '1rem', fontWeight: 600, margin: '0 0 10px 0' }}>
            3. Tarih ve gelişmiş filtre (isteğe bağlı)
          </h3>

          <p style={{ margin: '0 0 12px 0', fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
            Seçilen günler başından sonuna dahildir. Tarihi olmayan iletiler dışarıda kalır ve sayısı raporda yazılır.
          </p>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '14px' }}>
            <div>
              <label htmlFor="filter-start-date" style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                Başlangıç tarihi (dahil)
              </label>
              <input
                id="filter-start-date"
                type="date"
                className="text-input"
                value={startDate}
                onChange={(e) => setStartDate(e.target.value)}
                data-testid="filter-start-date-input"
              />
            </div>
            <div>
              <label htmlFor="filter-end-date" style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                Bitiş tarihi (dahil)
              </label>
              <input
                id="filter-end-date"
                type="date"
                className="text-input"
                value={endDate}
                onChange={(e) => setEndDate(e.target.value)}
                data-testid="filter-end-date-input"
              />
            </div>
          </div>
          <div style={{ marginTop: '14px' }}><AdvancedFilterBuilder testId="imap-advanced-filter" value={advancedFilter} onChange={setAdvancedFilter} unknownCount={preview?.advancedFilterUnknownCount ?? 0} /></div>
          <div style={{ marginTop: '10px' }}>
            <TechnicalDetails summary="Teknik ayrıntılar: tarih sınırı">
              <p><strong>Kapsayıcı UTC+03:00 tarih sınırı:</strong> Tarih filtresi özgün MIME <code>Date</code> başlığı ve açık saat dilimi sınırlarıyla değerlendirilir. Belirtilen günlerin tamamı (00:00:00..23:59:59 UTC+03:00) kapsanır. Tarih başlığı eksik iletiler filtrede dışlanır ve sayısı önizleme ile raporda bildirilir. IMAP <code>INTERNALDATE</code> ayrıca korunur, filtreye sessiz ikame edilmez.</p>
            </TechnicalDetails>
          </div>
        </div>
      )}

      {/* Step 4: Server Preview Action */}
      {sourceAccountId && targetAccountId && !isSameAccount && (
        <div style={{ display: 'flex', justifyContent: 'flex-start', alignItems: 'center', gap: '12px' }}>
          <button
            type="button"
            className="btn btn-orange"
            style={{ padding: '10px 20px', fontSize: '0.9375rem', fontWeight: 600 }}
            onClick={createPreview}
            disabled={!canRequestPreview || previewLoading}
            data-testid="create-preview-btn"
          >
            <IconClock size={16} className={previewLoading ? 'animate-spin' : ''} />
            <span>{previewLoading ? 'Sunucudan Önizleme Alınıyor...' : 'Sunucudan Önizleme Al'}</span>
          </button>
          <span style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
            Önizleme sırasında gerekirse hedefte boş klasörler oluşturulur. İletiler yalnız aktarımı başlatınca kopyalanır; kaynak korunur.
          </span>
        </div>
      )}

      {previewError && (
        <div style={{ background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', padding: '14px', fontSize: '0.875rem', color: '#b91c1c' }} data-testid="preview-error-alert">
          <strong>Önizleme Hatası:</strong> {previewError}
        </div>
      )}

      {/* Step 5: Frozen Preview Summary */}
      {preview && (
        <div className="card" style={{ padding: '22px', borderLeft: preview.canTransfer ? '4px solid #16a34a' : '4px solid #dc2626' }} data-testid="frozen-preview-summary">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '14px', flexWrap: 'wrap', gap: '8px' }}>
            <div>
              <h3 style={{ fontSize: '1.0625rem', fontWeight: 700, margin: 0, color: 'var(--text-main)' }}>
                Önizleme özeti (sabitlendi)
              </h3>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '2px' }}>
                Önizleme ID: <code>{preview.previewId}</code> · Oluşturulma: {new Date(preview.createdAtUtc).toLocaleString('tr-TR')}
              </div>
            </div>

            {preview.canTransfer ? (
              <Badge variant="success">Aktarıma hazır</Badge>
            ) : (
              <Badge variant="error">Aktarım engellendi</Badge>
            )}
          </div>

          {!preview.canTransfer && preview.blockerReason && (
            <div style={{ background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', padding: '12px 16px', fontSize: '0.875rem', color: '#b91c1c', marginBottom: '16px' }} data-testid="blocker-reason-alert">
              <strong>Aktarım engeli:</strong> {preview.blockerReason}
            </div>
          )}

          {/* Counts Grid */}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: '12px', marginBottom: '18px' }}>
            <div style={{ background: 'var(--bg-subtle)', padding: '12px', borderRadius: '6px' }}>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>Toplam kaynak</div>
              <div style={{ fontSize: '1.25rem', fontWeight: 700, marginTop: '2px' }} data-testid="preview-total-count">
                {preview.totalSourceItems}
              </div>
            </div>

            <div style={{ background: '#f0fdf4', border: '1px solid #bbf7d0', padding: '12px', borderRadius: '6px' }}>
              <div style={{ fontSize: '0.75rem', color: '#166534', fontWeight: 600 }}>Kopyalanacak</div>
              <div style={{ fontSize: '1.25rem', fontWeight: 700, color: '#15803d', marginTop: '2px' }} data-testid="preview-eligible-count">
                {preview.eligibleItemsCount}
              </div>
            </div>

            <div style={{ background: 'var(--bg-subtle)', padding: '12px', borderRadius: '6px' }}>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>Filtre dışı kalan</div>
              <div style={{ fontSize: '1.25rem', fontWeight: 700, marginTop: '2px' }} data-testid="preview-excluded-count">
                {preview.excludedCount}
              </div>
            </div>

            <div style={{ background: 'var(--bg-subtle)', padding: '12px', borderRadius: '6px' }}>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>Tarihi eksik (dışlanan)</div>
              <div style={{ fontSize: '1.25rem', fontWeight: 700, marginTop: '2px' }} data-testid="preview-missing-date-count">
                {preview.missingDateExcludedCount}
              </div>
            </div>

            <div style={{ background: 'var(--bg-subtle)', padding: '12px', borderRadius: '6px' }}>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>Silindi işaretli</div>
              <div style={{ fontSize: '1.25rem', fontWeight: 700, color: preview.deletedExcludedCount > 0 ? '#b91c1c' : 'inherit', marginTop: '2px' }} data-testid="preview-deleted-count">
                {preview.deletedExcludedCount}
              </div>
            </div>
          </div>

          {/* Folder Breakdown Table */}
          <div style={{ border: '1px solid var(--border-light)', borderRadius: '6px', overflow: 'hidden', marginBottom: '18px' }}>
            <table className="data-table" style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.8125rem' }}>
              <thead>
                <tr style={{ background: 'var(--bg-subtle)', textAlign: 'left' }}>
                  <th style={{ padding: '8px 12px' }}>Kaynak klasör</th>
                  <th style={{ padding: '8px 12px' }}>Hedef klasör</th>
                  <th style={{ padding: '8px 12px' }}>Toplam İleti</th>
                  <th style={{ padding: '8px 12px' }}>Kopyalanacak</th>
                  <th style={{ padding: '8px 12px' }}>Dışlanan</th>
                </tr>
              </thead>
              <tbody>
                {preview.folders.map((f, idx) => (
                  <tr key={idx} style={{ borderTop: '1px solid var(--border-light)' }}>
                    <td data-label="Kaynak" style={{ padding: '8px 12px', fontWeight: 500 }}>{f.sourceFolder}</td>
                    <td data-label="Hedef" style={{ padding: '8px 12px' }}>{f.targetFolder}</td>
                    <td data-label="Toplam" style={{ padding: '8px 12px' }}>{f.totalItems}</td>
                    <td data-label="Kopyalanacak" style={{ padding: '8px 12px', color: '#15803d', fontWeight: 600 }}>{f.eligibleItems}</td>
                    <td data-label="Dışlanan" style={{ padding: '8px 12px', color: 'var(--text-muted)' }}>{f.excludedCount}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Start Action */}
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: '12px' }}>
            <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
              Yalnız bu özetteki iletiler seçilen hedefe kopyalanır. Kaynak posta kutusu korunur.
            </div>

            <button
              type="button"
              className="btn btn-orange"
              style={{ padding: '10px 24px', fontSize: '0.9375rem', fontWeight: 700 }}
              onClick={startTransfer}
              disabled={!preview.canTransfer || activeJobLoading || Boolean(currentJob && (currentJob.status === 'converting' || currentJob.status === 'verifying'))}
              data-testid="start-transfer-btn"
            >
              <IconPlay size={16} />
              <span>{activeJobLoading ? 'Başlatılıyor...' : 'Başlat / sıraya ekle'}</span>
            </button>
          </div>
        </div>
      )}

      {activeJobError && (
        <div style={{ background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', padding: '14px', fontSize: '0.875rem', color: '#b91c1c' }}>
          <strong>Başlatma hatası:</strong> {activeJobError}
        </div>
      )}

      {/* Step 6: Active Transfer Progress Card */}
      {currentJob && (
        <div className="card" style={{ padding: '22px' }} data-testid="active-transfer-progress-card">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '14px' }}>
            <div>
              <h3 style={{ fontSize: '1.0625rem', fontWeight: 700, margin: 0 }}>
                İşlem Durumu: {currentJob.stage}
              </h3>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>
                İş No: <code>{currentJob.jobId}</code>
              </div>
            </div>

            <Badge
              variant={
                currentJob.status === 'completed'
                  ? 'success'
                  : currentJob.status === 'interrupted' || currentJob.stage.includes('NeedsAttention')
                  ? 'warning'
                  : currentJob.status === 'failed'
                  ? 'error'
                  : 'info'
              }
            >
              {currentJob.status === 'interrupted' || currentJob.stage.includes('NeedsAttention')
                ? 'İnceleme Gerektiriyor'
                : currentJob.status.toUpperCase()}
            </Badge>
          </div>

          {/* Progress bar */}
          <div style={{ background: '#e2e8f0', borderRadius: '999px', height: '10px', overflow: 'hidden', marginBottom: '12px' }}>
            <div
              style={{
                width: (currentJob.status === 'verifying' ? (currentJob.phaseTotal && currentJob.phaseCompleted != null ? Math.min(100, currentJob.phaseCompleted / currentJob.phaseTotal * 100) : 0) : currentJob.status === 'completed' ? 100 : Math.min(99, currentJob.percentComplete)) + '%',
                background: currentJob.status === 'interrupted' ? '#eab308' : 'var(--brand-orange)',
                height: '100%',
                transition: 'width 0.3s ease',
              }}
            />
          </div>

          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.875rem', marginBottom: '16px' }}>
            <span style={{ color: 'var(--text-muted)' }}>
              Yazılan: <strong>{currentJob.itemsWritten} / {currentJob.totalItems}</strong> ileti
            </span>
            <span style={{ fontWeight: 700 }}>{currentJob.status === 'verifying' ? 'Son doğrulama: ' + (currentJob.phaseCompleted ?? '—') + ' / ' + (currentJob.phaseTotal ?? '—') : '%' + (currentJob.status === 'completed' ? 100 : Math.min(99, currentJob.percentComplete))}</span>
          </div>

          {/* Reauthorization Required Guidance Banner (preserves job / resume context) */}
          {Boolean(
            (currentJob.errorMessage && currentJob.errorMessage.toLowerCase().includes('reauthorization_required')) ||
            (activeJobError && activeJobError.toLowerCase().includes('reauthorization_required')) ||
            (resumeError && resumeError.toLowerCase().includes('reauthorization_required')) ||
            (sourceAccount?.oauthStatus === 'reauthorization_required') ||
            (targetAccount?.oauthStatus === 'reauthorization_required')
          ) && (
            <div style={{ background: '#fef3c7', border: '1px solid #fde68a', borderRadius: '6px', padding: '14px', marginBottom: '16px' }} data-testid="reauth-required-banner">
              <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontWeight: 700, color: '#92400e', marginBottom: '4px' }}>
                <IconAlertTriangle size={18} color="#d97706" />
                <span>Microsoft 365 Yeniden Yetkilendirme Gerekli (reauthorization_required)</span>
              </div>
              <p style={{ margin: '0 0 10px 0', fontSize: '0.8125rem', color: '#78350f' }}>
                Microsoft 365 oturumunun süresi dolmuş veya yeniden yetkilendirme gerekiyor. Mevcut aktarım günlüğü ve tamamlanan iletiler korunmuştur. Lütfen Müşteriler bölümünden hesabı yeniden yetkilendirin; ardından aktarımı kaldığı yerden güvenle devam ettirebilirsiniz.
              </p>
              {onNavigateToAccounts && (
                <button
                  type="button"
                  className="btn btn-outline-orange"
                  style={{ padding: '4px 10px', fontSize: '0.8125rem' }}
                  onClick={onNavigateToAccounts}
                  data-testid="reauth-navigate-btn"
                >
                  Hesapları yönet ve yeniden bağlan
                </button>
              )}
            </div>
          )}

          {/* NeedsAttention / Interrupted Banner & Resume Action */}
          {currentJob.status === 'interrupted' && (
            <div style={{ background: '#fffbeb', border: '1px solid #fde68a', borderRadius: '6px', padding: '14px', marginBottom: '16px' }} data-testid="interrupted-resume-banner">
              <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontWeight: 700, color: '#92400e', marginBottom: '4px' }}>
                <IconAlertTriangle size={18} color="#d97706" />
                <span>Kesinti veya İnceleme Durumu (NeedsAttention)</span>
              </div>
              <p style={{ margin: '0 0 10px 0', fontSize: '0.8125rem', color: '#78350f' }}>
                Aktarım duraklatıldı veya ağ kesintisi tespit edildi. Kalıcı hedef keyword uzlaştırması ile doğrulanmış iletiler korunarak güvenle devam ettirilebilir.
              </p>
              {resumeError && (
                <div style={{ color: '#b91c1c', fontSize: '0.8125rem', marginBottom: '8px' }}>
                  {resumeError}
                </div>
              )}
              <button
                type="button"
                className="btn btn-orange"
                onClick={() => resumeTransfer(currentJob?.jobId)}
                disabled={resumeLoading}
                data-testid="resume-transfer-btn"
              >
                <IconPlay size={14} />
                <span>{resumeLoading ? 'Devam Ettiriliyor...' : 'Kesintiden Devam Et'}</span>
              </button>
            </div>
          )}

          {reportLoading && <p role="status">Kalıcı rapor yükleniyor…</p>}
          {reportError && <div role="alert">{reportError} <button className="btn btn-outline-gray" onClick={() => loadReport(currentJob.jobId).catch(() => {})}>Raporu yeniden yükle</button></div>}
          {/* Action buttons */}
          <div style={{ display: 'flex', gap: '10px', marginTop: '12px' }}>
            {(currentJob.status === 'completed' || report) && (
              <button
                type="button"
                className="btn btn-outline-gray"
                style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
                onClick={handleDownloadReport}
                data-testid="download-transfer-report-btn"
              >
                <IconDownload size={14} />
                <span>Raporu indir (JSON)</span>
              </button>
            )}

            <button
              type="button"
              className="btn btn-outline-gray"
              onClick={resetTransfer}
              data-testid="new-transfer-btn"
            >
              Yeni aktarım başlat
            </button>
          </div>
        </div>
      )}

      {/* Step 7: Durable Audit Report Display */}
      {report?.imapTransfer && (
        <div className="card" style={{ padding: '22px' }} data-testid="imap-transfer-audit-report">
          <h3 style={{ fontSize: '1.0625rem', fontWeight: 700, margin: '0 0 12px 0' }}>
            Aktarım doğrulama raporu
          </h3>

          {(report.imapTransfer.startDate || report.imapTransfer.endDate) && <p>Tarih aralığı: {report.imapTransfer.startDate || 'Başlangıç sınırı yok'} — {report.imapTransfer.endDate || 'Bitiş sınırı yok'} (UTC+03)</p>}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: '12px', marginBottom: '16px' }}>
            <div style={{ background: 'var(--bg-subtle)', padding: '10px', borderRadius: '6px' }}>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Planlanan</div>
              <div style={{ fontSize: '1.125rem', fontWeight: 700 }}>{report.imapTransfer.totalPlanned}</div>
            </div>
            <div style={{ background: '#f0fdf4', padding: '10px', borderRadius: '6px' }}>
              <div style={{ fontSize: '0.75rem', color: '#166534' }}>Tam doğrulanan</div>
              <div style={{ fontSize: '1.125rem', fontWeight: 700, color: '#15803d' }}>{report.imapTransfer.totalVerified}</div>
            </div>
            <div style={{ background: 'var(--bg-subtle)', padding: '10px', borderRadius: '6px' }}>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>İnceleme gerekli</div>
              <div style={{ fontSize: '1.125rem', fontWeight: 700 }}>{report.imapTransfer.totalNeedsAttention}</div>
            </div>
            <div style={{ background: 'var(--bg-subtle)', padding: '10px', borderRadius: '6px' }}>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Başarısız</div>
              <div style={{ fontSize: '1.125rem', fontWeight: 700 }}>{report.imapTransfer.totalFailed}</div>
            </div>
          </div>

          <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginBottom: '10px' }}>
            * Bu raporda eski PST doğrulama alanları yer almaz; her ileti hedefte BODY.PEEK tam bayt SHA-256 ve BitigMail keyword ile denetlenmiştir.
          </div>

          {/* Audit trail item snippet */}
          {report.imapTransfer.items && report.imapTransfer.items.length > 0 && (
            <div style={{ maxHeight: '300px', overflowY: 'auto', border: '1px solid var(--border-light)', borderRadius: '6px' }}>
              <table className="data-table" style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.75rem' }}>
                <thead>
                  <tr style={{ background: 'var(--bg-subtle)', textAlign: 'left' }}>
                    <th style={{ padding: '6px 10px' }}>Kaynak klasör / UID</th>
                    <th style={{ padding: '6px 10px' }}>Hedef klasör / UID</th>
                    <th style={{ padding: '6px 10px' }}>BitigMail Keyword</th>
                    <th style={{ padding: '6px 10px' }}>Durum</th>
                    <th style={{ padding: '6px 10px' }}>SHA-256 Özeti</th>
                  </tr>
                </thead>
                <tbody>
                  {report.imapTransfer.items.map((it, idx) => (
                    <tr key={idx} style={{ borderTop: '1px solid var(--border-light)' }}>
                      <td style={{ padding: '6px 10px' }}>{it.sourceFolder} #{it.sourceUid}</td>
                      <td style={{ padding: '6px 10px' }}>{it.targetFolder} #{it.targetUid ?? '—'}</td>
                      <td style={{ padding: '6px 10px' }}><code>{it.bitigMailKeyword}</code></td>
                      <td style={{ padding: '6px 10px' }}>
                        <Badge variant={it.status === 'Verified' ? 'success' : 'warning'}>{it.status}</Badge>
                      </td>
                      <td style={{ padding: '6px 10px' }} title={it.sourceSha256}>
                        <code>{it.sourceSha256 ? it.sourceSha256.substring(0, 12) + '...' : '—'}</code>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </div>
  );
};
