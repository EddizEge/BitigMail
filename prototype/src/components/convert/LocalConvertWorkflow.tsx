import React, { useState } from 'react';
import { AppState } from '../../state/useAppState';
import { useLocalEngine } from '../../hooks/useLocalEngine';
import {
  IconAlertCircle,
  IconCheck,
  IconClock,
  IconCopy,
  IconDownload,
  IconFile,
  IconRefreshCw,
} from '../ui/Icons';
import { Badge } from '../ui/Badge';
import { EngineOfflineNotice } from '../layout/PageHeader';

interface LocalConvertWorkflowProps {
  state: AppState;
}

export const LocalConvertWorkflow: React.FC<LocalConvertWorkflowProps> = ({ state }) => {
  const localEngine = useLocalEngine({ targetJobId: state.selectedLocalJobId });
  const [copiedReport, setCopiedReport] = useState(false);
  const [copiedLocation, setCopiedLocation] = useState(false);

  const currentCompany = state.companies.find((c) => c.id === state.plan.companyId) || state.companies[0];
  const companyProjects = state.projects.filter((p) => p.companyId === (currentCompany?.id || ''));
  const currentProject = companyProjects.find((p) => p.id === state.plan.projectId) || companyProjects[0];

  const frozenContext = localEngine.jobReport?.clientContext || localEngine.activeJob?.clientContext;
  const displayedCompanyId = frozenContext?.companyId || currentCompany?.id || 'comp-default';
  const displayedCompanyName = frozenContext?.companyName || currentCompany?.name || 'Varsayılan Şirket';
  const displayedProjectId = frozenContext?.projectId || currentProject?.id || 'proj-default';
  const displayedProjectName = frozenContext?.projectName || currentProject?.name || 'Varsayılan Proje';

  const clientContext = {
    companyId: displayedCompanyId,
    companyName: displayedCompanyName,
    projectId: displayedProjectId,
    projectName: displayedProjectName,
  };

  const handlePickSource = async () => {
    state.setSelectedLocalJobId(null);
    await localEngine.pickSourceFile();
  };

  const fallbackCopyText = (text: string) => {
    try {
      const textArea = document.createElement('textarea');
      textArea.value = text;
      textArea.style.position = 'fixed';
      textArea.style.left = '-999999px';
      textArea.style.top = '-999999px';
      document.body.appendChild(textArea);
      textArea.focus();
      textArea.select();
      document.execCommand('copy');
      textArea.remove();
    } catch {
      // ignore fallback error
    }
  };

  const handleCopyLocation = () => {
    const loc = localEngine.jobReport?.outputPath || localEngine.activeJob?.outputPath;
    if (!loc) return;
    try {
      if (navigator.clipboard && window.isSecureContext) {
        navigator.clipboard.writeText(loc).catch(() => {
          fallbackCopyText(loc);
        });
      } else {
        fallbackCopyText(loc);
      }
    } catch {
      fallbackCopyText(loc);
    }
    setCopiedLocation(true);
    setTimeout(() => setCopiedLocation(false), 2500);
  };

  const handleDownloadReport = () => {
    if (!localEngine.jobReport) return;
    const json = JSON.stringify(localEngine.jobReport, null, 2);
    const blob = new Blob([json], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `rapor-${localEngine.jobReport.jobId}.json`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  };

  const handleCopyReport = () => {
    if (!localEngine.jobReport) return;
    const json = JSON.stringify(localEngine.jobReport, null, 2);
    try {
      if (navigator.clipboard && window.isSecureContext) {
        navigator.clipboard.writeText(json).catch(() => {
          fallbackCopyText(json);
        });
      } else {
        fallbackCopyText(json);
      }
    } catch {
      fallbackCopyText(json);
    }
    setCopiedReport(true);
    setTimeout(() => setCopiedReport(false), 2500);
  };

  const formatBytes = (bytes: number): string => {
    if (bytes <= 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  };

  return (
    <div
      style={{
        padding: '16px',
        maxWidth: '1080px',
        width: '100%',
        margin: '0 auto',
        overflowY: 'auto',
        overflowX: 'hidden',
        boxSizing: 'border-box',
        minWidth: 0,
        display: 'flex',
        flexDirection: 'column',
        gap: '16px',
      }}
      data-testid="local-convert-workflow"
    >
      {/* 1. Header & Client Context Bar */}
      <div className="card" style={{ padding: '16px', background: 'var(--bg-white)', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '12px', minWidth: 0, maxWidth: '100%' }}>
          <div style={{ minWidth: 0, flex: '1 1 auto' }}>
            <h2 style={{ margin: '0 0 6px 0', fontSize: '1.125rem', fontWeight: 700, color: 'var(--text-primary)', overflowWrap: 'anywhere' }}>
              OST → PST dönüştürme
            </h2>
            {frozenContext && (
              <div style={{ display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: '6px 8px', fontSize: '0.875rem', fontWeight: 600, minWidth: 0 }}>
              <span style={{ minWidth: 0, overflowWrap: 'anywhere', wordBreak: 'break-word' }}>İş kaydı: <strong data-testid="context-company-name">{clientContext.companyName}</strong></span>
              <span style={{ color: 'var(--border-mid)' }}>/</span>
              <span style={{ minWidth: 0, overflowWrap: 'anywhere', wordBreak: 'break-word' }}><strong data-testid="context-project-name">{clientContext.projectName}</strong></span>
            </div>
            )}
          </div>

          <div style={{ display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: '8px', flexShrink: 0 }}>
                        {localEngine.serviceStatus === 'checking' && (
              <Badge variant="warning">Bağlanıyor...</Badge>
            )}
            {localEngine.serviceStatus === 'online' && (
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', color: '#16a34a', fontWeight: 600, fontSize: '0.8125rem' }}>
                <span style={{ width: '8px', height: '8px', borderRadius: '50%', background: '#16a34a' }} />
                <span>Motor hazır</span>
              </span>
            )}
            {localEngine.serviceStatus === 'offline' && (
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', color: '#dc2626', fontWeight: 600, fontSize: '0.8125rem' }}>
                <span style={{ width: '8px', height: '8px', borderRadius: '50%', background: '#dc2626' }} />
                <span>Motor bağlantısı yok</span>
              </span>
            )}
            <button
              className="btn btn-outline-gray"
              style={{ padding: '4px 8px', fontSize: '0.75rem', flexShrink: 0 }}
              onClick={localEngine.checkService}
              title="Bağlantıyı yenile" aria-label="Bağlantıyı yenile"
              data-testid="refresh-service-status-btn"
            >
              <IconRefreshCw size={12} />
            </button>
          </div>
        </div>
      </div>

      {localEngine.serviceStatus === 'offline' && <EngineOfflineNotice action="Dönüştürmeye başlamak" />}

      {/* 3. Source OST File Selection Card */}
      <div className="card" style={{ padding: '16px 20px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }} data-testid="source-selection-card">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '8px', marginBottom: '16px' }}>
          <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
            <IconFile size={18} color="var(--brand-orange)" style={{ flexShrink: 0 }} />
            <span style={{ overflowWrap: 'anywhere' }}>1. Kaynak OST dosyası</span>
          </h3>
          {localEngine.selectedSource && (
            <button
              className="btn btn-outline-gray"
              style={{ padding: '4px 10px', fontSize: '0.8125rem', flexShrink: 0 }}
              onClick={handlePickSource}
              disabled={localEngine.serviceStatus !== 'online' || localEngine.isAnalyzing || localEngine.activeJob?.status === 'converting'}
              data-testid="reselect-source-btn"
            >
              Farklı dosya seç
            </button>
          )}
        </div>

        {!localEngine.selectedSource ? (
          <div style={{ textAlign: 'center', padding: '24px 16px', background: 'var(--bg-subtle)', borderRadius: '8px', border: '1px dashed var(--border-mid)', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
            <div style={{ marginBottom: '12px', color: 'var(--text-muted)', overflowWrap: 'anywhere' }}>
              Windows yerel dosya seçici ile bilgisayarınızdaki gerçek OST dosyasını seçin.
            </div>
            <button
              className="btn btn-orange"
              onClick={handlePickSource}
              disabled={localEngine.serviceStatus !== 'online'}
              data-testid="pick-source-btn"
            >
              OST dosyası seç
            </button>
            <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '8px', overflowWrap: 'anywhere' }}>
              Dosya hiçbir sunucuya yüklenmez; yerel motor akışıyla doğrudan okunur.
            </div>
          </div>
        ) : (
          <div style={{ minWidth: 0, maxWidth: '100%' }}>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 180px), 1fr))', gap: '12px', background: 'var(--bg-subtle)', padding: '14px', borderRadius: '6px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
              <div style={{ minWidth: 0, overflowWrap: 'anywhere' }}>
                <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>Dosya adı</span>
                <div style={{ fontWeight: 600, overflowWrap: 'anywhere', wordBreak: 'break-all', minWidth: 0 }} data-testid="source-filename">
                  {localEngine.selectedSource.fileName}
                </div>
              </div>
              <div style={{ minWidth: 0 }}>
                <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>Boyut</span>
                <div style={{ fontWeight: 600 }}>{formatBytes(localEngine.selectedSource.sizeBytes)}</div>
              </div>
              {localEngine.analysis && (
                <details className="tech-details" style={{ gridColumn: '1 / -1' }}><summary>Teknik ayrıntılar: SHA-256 özeti</summary><div className="tech-details-body">
                  <div style={{ fontSize: '0.75rem', fontFamily: 'monospace', color: 'var(--text-muted)', overflowWrap: 'anywhere', wordBreak: 'break-all', minWidth: 0 }} data-testid="source-sha256">
                    {localEngine.analysis.sourceSha256}
                  </div></div></details>
              )}
              {localEngine.selectedSource.displayPath && (
                <div style={{ gridColumn: '1 / -1', minWidth: 0, maxWidth: '100%', overflowWrap: 'anywhere' }}>
                  <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>Kaynak dosya konumu</span>
                  <div style={{ fontSize: '0.8125rem', fontFamily: 'monospace', overflowWrap: 'anywhere', wordBreak: 'break-all', color: 'var(--text-primary)', minWidth: 0 }} data-testid="source-display-path">
                    {localEngine.selectedSource.displayPath}
                  </div>
                </div>
              )}
            </div>

            {localEngine.isAnalyzing && (
              <div style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '12px 0', color: 'var(--brand-orange)', fontSize: '0.875rem', minWidth: 0, overflowWrap: 'anywhere' }}>
                <span style={{ width: '14px', height: '14px', borderRadius: '50%', border: '2px solid var(--brand-orange)', borderTopColor: 'transparent', display: 'inline-block', animation: 'spin 1s linear infinite', flexShrink: 0 }} />
                <span>OST dosyası salt-okunur inceleniyor ve klasör yapısı taranıyor...</span>
              </div>
            )}
          </div>
        )}

        {localEngine.analysisError && (
          <div style={{ marginTop: '12px', padding: '10px 14px', background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', color: '#991b1b', fontSize: '0.875rem', minWidth: 0, overflowWrap: 'anywhere' }} data-testid="source-analysis-error">
            <strong>Hata:</strong> {localEngine.analysisError}
          </div>
        )}
      </div>

      {/* 4. Analysis & Preflight Card */}
      {localEngine.analysis && (
        <div className="card" style={{ padding: '16px 20px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }} data-testid="analysis-preflight-card">
          <h3 style={{ margin: '0 0 16px 0', fontSize: '1rem', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0, overflowWrap: 'anywhere' }}>
            <IconCheck size={18} color="#16a34a" style={{ flexShrink: 0 }} />
            <span>2. Dosya analizi ve ön kontrol</span>
          </h3>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 120px), 1fr))', gap: '10px', marginBottom: '16px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
            <div style={{ background: 'var(--bg-subtle)', padding: '12px', borderRadius: '6px', textAlign: 'center', minWidth: 0 }}>
              <div style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--brand-orange)' }}>{localEngine.analysis.totalItems}</div>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Fiziksel İleti</div>
            </div>
            <div style={{ background: 'var(--bg-subtle)', padding: '12px', borderRadius: '6px', textAlign: 'center', minWidth: 0 }}>
              <div style={{ fontSize: '1.25rem', fontWeight: 700, color: '#2563eb' }}>{localEngine.analysis.totalAttachments}</div>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Ek / Görsel</div>
            </div>
            <div style={{ background: 'var(--bg-subtle)', padding: '12px', borderRadius: '6px', textAlign: 'center', minWidth: 0 }}>
              <div style={{ fontSize: '1.25rem', fontWeight: 700, color: '#16a34a' }}>{localEngine.analysis.activeFoldersCount}</div>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Dolu klasör</div>
            </div>
            <div style={{ background: 'var(--bg-subtle)', padding: '12px', borderRadius: '6px', textAlign: 'center', minWidth: 0 }}>
              <div style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--text-muted)' }}>{localEngine.analysis.emptyFoldersCount}</div>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Boş klasör</div>
            </div>
          </div>

          {/* Folder Breakdown */}
          <div style={{ marginBottom: '16px', minWidth: 0, maxWidth: '100%' }}>
            <div style={{ fontSize: '0.8125rem', fontWeight: 700, color: 'var(--text-muted)', marginBottom: '8px' }}>
              Klasör yapısı ve sayımlar
            </div>
            <div style={{ maxHeight: '180px', overflowY: 'auto', overflowX: 'auto', WebkitOverflowScrolling: 'touch', border: '1px solid var(--border-light)', borderRadius: '6px', maxWidth: '100%', minWidth: 0, boxSizing: 'border-box' }}>
              <table className="data-table" style={{ fontSize: '0.8125rem', width: '100%', minWidth: '320px' }}>
                <thead>
                  <tr>
                    <th>Klasör yolu</th>
                    <th>Öğe Sayısı</th>
                    <th>Durum</th>
                  </tr>
                </thead>
                <tbody>
                  {localEngine.analysis.folders.map((f, idx) => (
                    <tr key={idx}>
                      <td style={{ fontWeight: f.itemCount > 0 ? 600 : 400, overflowWrap: 'anywhere' }}>{f.folderPath}</td>
                      <td>{f.itemCount}</td>
                      <td>
                        {f.itemCount > 50 ? (
                          <span style={{ color: '#dc2626', fontWeight: 700, whiteSpace: 'nowrap' }}>⛔ 50+ Engeli</span>
                        ) : f.itemCount > 0 ? (
                          <span style={{ color: '#16a34a', fontWeight: 600 }}>Aktif</span>
                        ) : (
                          <span style={{ color: 'var(--text-muted)' }}>{f.category}</span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          {/* Sample Messages Preview (strictly text, no HTML) */}
          {localEngine.analysis.sampleMessages.length > 0 && (
            <div style={{ marginBottom: '16px', minWidth: 0, maxWidth: '100%' }}>
              <div style={{ fontSize: '0.8125rem', fontWeight: 700, color: 'var(--text-muted)', marginBottom: '8px', overflowWrap: 'anywhere' }}>
                ÖRNEK İLETİ ÜSTVERİLERİ (HTML ve uzak görsel yüklenmez)
              </div>
              <div style={{ maxHeight: '160px', overflowY: 'auto', overflowX: 'auto', WebkitOverflowScrolling: 'touch', border: '1px solid var(--border-light)', borderRadius: '6px', maxWidth: '100%', minWidth: 0, boxSizing: 'border-box' }}>
                <table className="data-table" style={{ fontSize: '0.8125rem', width: '100%', minWidth: '340px' }}>
                  <thead>
                    <tr>
                      <th>Konu</th>
                      <th>Gönderen</th>
                      <th>Tarih</th>
                      <th>Ek</th>
                    </tr>
                  </thead>
                  <tbody>
                    {localEngine.analysis.sampleMessages.map((m, idx) => (
                      <tr key={idx}>
                        <td style={{ fontWeight: 600, overflowWrap: 'anywhere' }}>{m.subject}</td>
                        <td style={{ color: 'var(--text-muted)', overflowWrap: 'anywhere' }}>{m.sender}</td>
                        <td style={{ color: 'var(--text-muted)', whiteSpace: 'nowrap' }}>{m.dateUtc}</td>
                        <td style={{ whiteSpace: 'nowrap' }}>{m.hasAttachments ? `Var (${m.attachmentCount})` : '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {/* Preflight Check Banners */}
          {localEngine.analysis.preflight.hasTrialBlocker ? (
            <div
              style={{
                background: '#fef2f2',
                border: '1px solid #fecaca',
                borderRadius: '6px',
                padding: '12px 16px',
                color: '#991b1b',
                fontSize: '0.875rem',
                minWidth: 0,
                maxWidth: '100%',
                boxSizing: 'border-box',
                overflowWrap: 'anywhere',
              }}
              data-testid="trial-blocker-alert"
            >
              <div style={{ fontWeight: 700, display: 'flex', alignItems: 'center', gap: '6px', marginBottom: '4px' }}>
                <IconAlertCircle size={18} style={{ flexShrink: 0 }} />
                <span>Deneme sürümü sınırı: dönüştürme başlatılamaz</span>
              </div>
              <div>{localEngine.analysis.preflight.trialBlockerReason}</div>
              <div style={{ fontSize: '0.75rem', marginTop: '6px', color: '#b91c1c' }}>
                Vendor kısıtı gereği değerlendirme motorunda sessiz kırpma veya sahte tam başarı yapılmaz.
              </div>
            </div>
          ) : (
            <div
              style={{
                background: '#f0fdf4',
                border: '1px solid #bbf7d0',
                borderRadius: '6px',
                padding: '10px 14px',
                color: '#166534',
                fontSize: '0.875rem',
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
                minWidth: 0,
                maxWidth: '100%',
                boxSizing: 'border-box',
                overflowWrap: 'anywhere',
              }}
              data-testid="preflight-success-banner"
            >
              <IconCheck size={18} color="#16a34a" style={{ flexShrink: 0 }} />
              <span>Ön kontrol başarılı: 50 öğe sınırını aşan klasör bulunmuyor, biçim imzası OST olarak doğrulandı.</span>
            </div>
          )}
        </div>
      )}

      {/* 3. Filter & Authoritative Selection Preview Card (TASK-011) */}
      {localEngine.analysis && !localEngine.analysis.preflight.hasTrialBlocker && (
        <div className="card" style={{ padding: '16px 20px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }} data-testid="filter-selection-card">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px', flexWrap: 'wrap', gap: '8px', minWidth: 0, maxWidth: '100%' }}>
            <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
              <IconFile size={18} color="var(--brand-orange)" style={{ flexShrink: 0 }} />
              <span>3. Filtreleme ve Seçim Önizlemesi</span>
            </h3>
            <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
              Türkiye saati (UTC+03:00)
            </div>
          </div>

          {/* Date Filter Inputs */}
          <div style={{ background: 'var(--bg-subtle)', padding: '14px', borderRadius: '6px', marginBottom: '16px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
            <div style={{ fontSize: '0.8125rem', fontWeight: 700, color: 'var(--text-muted)', marginBottom: '8px' }}>
              Tarih aralığı (isteğe bağlı)
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 180px), 1fr))', gap: '12px', alignItems: 'center' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 600, color: 'var(--text-muted)', marginBottom: '4px' }}>
                  Başlangıç tarihi (dahil)
                </label>
                <input
                  type="date"
                  className="form-input"
                  style={{ width: '100%', padding: '6px 10px', fontSize: '0.875rem', borderRadius: '4px', border: '1px solid var(--border-mid)' }}
                  value={localEngine.startDate}
                  onChange={(e) => localEngine.setStartDate(e.target.value)}
                  data-testid="filter-start-date"
                />
              </div>
              <div>
                <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 600, color: 'var(--text-muted)', marginBottom: '4px' }}>
                  Bitiş tarihi (dahil)
                </label>
                <input
                  type="date"
                  className="form-input"
                  style={{ width: '100%', padding: '6px 10px', fontSize: '0.875rem', borderRadius: '4px', border: '1px solid var(--border-mid)' }}
                  value={localEngine.endDate}
                  onChange={(e) => localEngine.setEndDate(e.target.value)}
                  data-testid="filter-end-date"
                />
              </div>
              {(localEngine.startDate || localEngine.endDate) && (
                <div style={{ alignSelf: 'flex-end' }}>
                  <button
                    className="btn btn-outline-gray"
                    style={{ padding: '6px 12px', fontSize: '0.8125rem', width: '100%' }}
                    onClick={() => {
                      localEngine.setStartDate('');
                      localEngine.setEndDate('');
                    }}
                    data-testid="clear-dates-btn"
                  >
                    Tarihleri temizle
                  </button>
                </div>
              )}
            </div>
            <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '8px' }}>
              İletiler gönderim tarihine (yoksa teslim tarihine) göre filtrelenir. Seçilen başlangıç ve bitiş günlerinin tamamı (00:00:00 - 23:59:59) aralığa dahildir.
            </div>
          </div>

          {/* Folder Selection Controls */}
          <div style={{ marginBottom: '16px', minWidth: 0, maxWidth: '100%' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '8px', flexWrap: 'wrap', gap: '8px' }}>
              <div style={{ fontSize: '0.8125rem', fontWeight: 700, color: 'var(--text-muted)' }}>
                Klasör seçimi
              </div>
              <div style={{ display: 'flex', gap: '8px' }}>
                <button
                  className="btn btn-outline-gray"
                  style={{ padding: '3px 8px', fontSize: '0.75rem' }}
                  onClick={localEngine.selectAllFolders}
                  data-testid="select-all-folders-btn"
                >
                  Tümünü seç
                </button>
                <button
                  className="btn btn-outline-gray"
                  style={{ padding: '3px 8px', fontSize: '0.75rem' }}
                  onClick={localEngine.clearAllFolders}
                  data-testid="clear-all-folders-btn"
                >
                  Tümünü kaldır
                </button>
              </div>
            </div>
            <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginBottom: '8px' }}>
              Her onay kutusu yalnızca o klasörün doğrudan kendi iletilerini içerir; alt klasörlerin bağımsız onay kutuları vardır.
            </div>

            <div style={{ maxHeight: '180px', overflowY: 'auto', border: '1px solid var(--border-light)', borderRadius: '6px', padding: '8px 12px', background: '#ffffff' }}>
              {localEngine.analysis.folders.map((f) => {
                const isChecked = localEngine.selectedFolderIds.includes(f.folderId);
                return (
                  <label
                    key={f.folderId}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: '8px',
                      padding: '4px 0',
                      cursor: 'pointer',
                      fontSize: '0.8125rem',
                      userSelect: 'none',
                    }}
                    data-testid={`folder-label-${f.folderId}`}
                  >
                    <input
                      type="checkbox"
                      checked={isChecked}
                      onChange={() => localEngine.toggleFolder(f.folderId)}
                      data-testid={`folder-checkbox-${f.folderId}`}
                    />
                    <span style={{ fontWeight: f.itemCount > 0 ? 600 : 400, overflowWrap: 'anywhere' }}>
                      {f.folderPath}
                    </span>
                    <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginLeft: 'auto' }}>
                      ({f.itemCount} ileti)
                    </span>
                  </label>
                );
              })}
            </div>
          </div>

          {/* Authoritative Preview Section */}
          <div style={{ minWidth: 0, maxWidth: '100%' }}>
            <div style={{ fontSize: '0.8125rem', fontWeight: 700, color: 'var(--text-muted)', marginBottom: '8px' }}>
              Seçim özeti
            </div>

            {localEngine.isLoadingPreview && (
              <div style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '12px', background: 'var(--bg-subtle)', borderRadius: '6px', color: 'var(--brand-orange)', fontSize: '0.875rem' }} data-testid="preview-loading">
                <span style={{ width: '14px', height: '14px', borderRadius: '50%', border: '2px solid var(--brand-orange)', borderTopColor: 'transparent', display: 'inline-block', animation: 'spin 1s linear infinite', flexShrink: 0 }} />
                <span>Seçim ve filtre önizlemesi sunucudan hesaplanıyor...</span>
              </div>
            )}

            {localEngine.previewError && (
              <div style={{ padding: '10px 14px', background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', color: '#991b1b', fontSize: '0.875rem' }} data-testid="preview-error-alert">
                <strong>Önizleme Hatası:</strong> {localEngine.previewError}
              </div>
            )}

            {localEngine.selectionPreview && (
              <div style={{ background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: '6px', padding: '14px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }} data-testid="selection-preview-card">
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 130px), 1fr))', gap: '10px', marginBottom: '10px' }}>
                  <div style={{ background: '#ffffff', padding: '10px', borderRadius: '6px', textAlign: 'center', border: '1px solid #e2e8f0' }}>
                    <div style={{ fontSize: '1.25rem', fontWeight: 700, color: '#16a34a' }} data-testid="preview-selected-count">
                      {localEngine.selectionPreview.selectedMessagesCount}
                    </div>
                    <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Seçilen İleti</div>
                  </div>
                  <div style={{ background: '#ffffff', padding: '10px', borderRadius: '6px', textAlign: 'center', border: '1px solid #e2e8f0' }}>
                    <div style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--brand-orange)' }} data-testid="preview-attachments-count">
                      {localEngine.selectionPreview.selectedAttachmentsCount}
                    </div>
                    <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Seçilen ek</div>
                  </div>
                  <div style={{ background: '#ffffff', padding: '10px', borderRadius: '6px', textAlign: 'center', border: '1px solid #e2e8f0' }}>
                    <div style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--text-muted)' }} data-testid="preview-excluded-count">
                      {localEngine.selectionPreview.excludedMessagesCount}
                    </div>
                    <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Hariç tutulan</div>
                  </div>
                  <div style={{ background: '#ffffff', padding: '10px', borderRadius: '6px', textAlign: 'center', border: '1px solid #e2e8f0' }}>
                    <div style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--text-muted)' }} data-testid="preview-total-count">
                      {localEngine.selectionPreview.totalSourceMessages}
                    </div>
                    <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Kaynak toplam</div>
                  </div>
                </div>

                {localEngine.selectionPreview.missingDateExcludedCount > 0 && (
                  <div style={{ fontSize: '0.75rem', color: '#b45309', marginBottom: '8px' }} data-testid="missing-date-alert">
                    ℹ {localEngine.selectionPreview.missingDateExcludedCount} ileti tarihi olmadığı için tarih filtresi gereği hariç bırakıldı.
                  </div>
                )}

                {localEngine.selectionPreview.selectedMessagesCount === 0 && (
                  <div style={{ padding: '8px 12px', background: '#fffbeb', border: '1px solid #fde68a', borderRadius: '6px', color: '#92400e', fontSize: '0.8125rem', fontWeight: 600 }} data-testid="zero-match-alert">
                    ⚠️ Seçim kriterlerinize uyan hiçbir ileti bulunamadı. Sıfır iletili dönüştürme başlatılamaz.
                  </div>
                )}

                {!localEngine.selectionPreview.canConvert && (localEngine.selectionPreview.blockerReason || localEngine.selectionPreview.blockReason) && (
                  <div style={{ padding: '8px 12px', background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', color: '#991b1b', fontSize: '0.8125rem', fontWeight: 600, marginTop: '8px' }} data-testid="preview-block-alert">
                    ⛔ {localEngine.selectionPreview.blockerReason || localEngine.selectionPreview.blockReason}
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      )}

      {/* 4. Target PST Selection Card */}
      {localEngine.analysis && !localEngine.analysis.preflight.hasTrialBlocker && (
        <div className="card" style={{ padding: '16px 20px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }} data-testid="target-selection-card">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px', flexWrap: 'wrap', gap: '8px', minWidth: 0, maxWidth: '100%' }}>
            <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
              <IconFile size={18} color="var(--brand-orange)" style={{ flexShrink: 0 }} />
              <span>4. Hedef PST Dosyası</span>
            </h3>
            {localEngine.selectedTarget && (
              <button
                className="btn btn-outline-gray"
                style={{ padding: '4px 10px', fontSize: '0.8125rem', flexShrink: 0 }}
                onClick={localEngine.pickTargetFile}
                disabled={localEngine.activeJob?.status === 'converting'}
                data-testid="reselect-target-btn"
              >
                Farklı konum seç
              </button>
            )}
          </div>

          {!localEngine.selectedTarget ? (
            <div style={{ textAlign: 'center', padding: '24px 16px', background: 'var(--bg-subtle)', borderRadius: '8px', border: '1px dashed var(--border-mid)', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
              <button
                className="btn btn-orange"
                onClick={localEngine.pickTargetFile}
                data-testid="pick-target-btn"
                style={{ maxWidth: '100%' }}
              >
                Hedef PST dosyası seç (yeni konum)
              </button>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '8px', overflowWrap: 'anywhere' }}>
                Mevcut dosyaların üzerine yazılmaz. Çıktı önce geçici dosya olarak yazılır, doğrulama sonrası atomik olarak taşınır.
              </div>
            </div>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', background: 'var(--bg-subtle)', padding: '12px 16px', borderRadius: '6px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: '8px', minWidth: 0, maxWidth: '100%' }}>
                <div style={{ minWidth: 0, flex: '1 1 auto', overflowWrap: 'anywhere' }}>
                  <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>Hedef dosya adı</span>
                  <div style={{ fontWeight: 600, overflowWrap: 'anywhere', wordBreak: 'break-all', minWidth: 0 }} data-testid="target-filename">{localEngine.selectedTarget.fileName}</div>
                </div>
                <span style={{ flexShrink: 0 }}>
                  <Badge variant="success">Hedef hazır</Badge>
                </span>
              </div>
              {localEngine.selectedTarget.displayPath && (
                <div style={{ minWidth: 0, maxWidth: '100%', overflowWrap: 'anywhere' }}>
                  <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>Hedef dosya konumu</span>
                  <div style={{ fontSize: '0.8125rem', fontFamily: 'monospace', overflowWrap: 'anywhere', wordBreak: 'break-all', color: 'var(--text-primary)', minWidth: 0 }} data-testid="target-display-path">
                    {localEngine.selectedTarget.displayPath}
                  </div>
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {/* 5. Conversion Execution & Real-Time Progress Card */}
      {((localEngine.selectedSource && localEngine.selectedTarget && !localEngine.analysis?.preflight.hasTrialBlocker) || (localEngine.activeJob && localEngine.activeJob.status !== 'completed')) && (
        <div className="card" style={{ padding: '16px 20px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }} data-testid="conversion-progress-card">
          <h3 style={{ margin: '0 0 16px 0', fontSize: '1rem', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
            <IconClock size={18} color="var(--brand-orange)" style={{ flexShrink: 0 }} />
            <span>5. Dönüştürme ve İlerleme</span>
          </h3>

          {!localEngine.activeJob || localEngine.activeJob.status === 'queued' ? (
            <div>
              <button
                className="btn btn-orange"
                style={{ padding: '10px 24px', fontSize: '0.9375rem', fontWeight: 700, maxWidth: '100%' }}
                onClick={() => localEngine.startConversion(clientContext)}
                disabled={localEngine.isStarting || !localEngine.selectionPreview || !localEngine.selectionPreview.canConvert || localEngine.selectionPreview.selectedMessagesCount === 0}
                data-testid="start-conversion-btn"
              >
                {localEngine.isStarting ? 'Dönüştürme Başlatılıyor...' : 'Başlat / sıraya ekle'}
              </button>
            </div>
          ) : (
            <div style={{ minWidth: 0, maxWidth: '100%' }}>
              {/* Real Counters & Status Badge */}
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px', flexWrap: 'wrap', gap: '8px', minWidth: 0, maxWidth: '100%' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexShrink: 0 }}>
                  <span style={{ fontSize: '0.875rem', fontWeight: 700 }}>Aşama:</span>
                  <Badge
                    variant={
                      localEngine.activeJob.status === 'completed'
                        ? 'success'
                        : localEngine.activeJob.status === 'failed' || localEngine.activeJob.status === 'interrupted'
                        ? 'error'
                        : 'warning'
                    }
                  >
                    {localEngine.activeJob.stage}
                  </Badge>
                </div>

                <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)', minWidth: 0, overflowWrap: 'anywhere', wordBreak: 'break-all' }}>
                  İş Kimliği: <code style={{ fontSize: '0.75rem', overflowWrap: 'anywhere', wordBreak: 'break-all' }}>{localEngine.activeJob.jobId}</code>
                </div>
              </div>

              {/* Progress Counters */}
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 120px), 1fr))', gap: '10px', background: 'var(--bg-subtle)', padding: '14px', borderRadius: '6px', marginBottom: '14px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
                <div style={{ minWidth: 0 }}>
                  <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>Okunan</span>
                  <div style={{ fontSize: '1.125rem', fontWeight: 700 }} data-testid="counter-items-read">{localEngine.activeJob.itemsRead}</div>
                </div>
                <div style={{ minWidth: 0 }}>
                  <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>Yazılan</span>
                  <div style={{ fontSize: '1.125rem', fontWeight: 700, color: '#16a34a' }} data-testid="counter-items-written">{localEngine.activeJob.itemsWritten}</div>
                </div>
                <div style={{ minWidth: 0 }}>
                  <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>Başarısız</span>
                  <div style={{ fontSize: '1.125rem', fontWeight: 700, color: localEngine.activeJob.failedItems > 0 ? '#dc2626' : 'var(--text-muted)' }}>
                    {localEngine.activeJob.failedItems}
                  </div>
                </div>
                <div style={{ minWidth: 0 }}>
                  <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>İşlenen klasör</span>
                  <div style={{ fontSize: '0.8125rem', fontWeight: 600, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', overflowWrap: 'anywhere', wordBreak: 'break-all' }}>
                    {localEngine.activeJob.currentFolder || '—'}
                  </div>
                </div>
              </div>
            </div>
          )}

          {localEngine.jobError && (
            <div style={{ marginTop: '12px', padding: '10px 14px', background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', color: '#991b1b', fontSize: '0.875rem', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box', overflowWrap: 'anywhere' }}>
              <strong>Hata:</strong> {localEngine.jobError}
            </div>
          )}
        </div>
      )}

      {/* 7. Verification Report & Download Card */}
      {localEngine.jobReport && (() => {
        const outputLocation = localEngine.jobReport.outputPath || localEngine.activeJob?.outputPath;
        return (
        <div className="card" style={{ padding: '16px 20px', background: '#ffffff', border: '1px solid #bbf7d0', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }} data-testid="conversion-report-card">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px', flexWrap: 'wrap', gap: '8px', minWidth: 0, maxWidth: '100%' }}>
            <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 700, color: '#166534', display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0, overflowWrap: 'anywhere' }}>
              <IconCheck size={20} color="#16a34a" style={{ flexShrink: 0 }} />
              <span>Dönüştürme ve yeniden okuma doğrulaması tamamlandı</span>
            </h3>

            <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', maxWidth: '100%' }}>
              <button
                className="btn btn-outline-gray"
                style={{ padding: '6px 12px', fontSize: '0.8125rem', display: 'flex', alignItems: 'center', gap: '6px', flexShrink: 0 }}
                onClick={handleCopyLocation}
                data-testid="copy-location-btn"
              >
                <IconCopy size={14} style={{ flexShrink: 0 }} />
                <span>{copiedLocation ? 'Kopyalandı!' : 'Konumu kopyala'}</span>
              </button>
              <button
                className="btn btn-outline-gray"
                style={{ padding: '6px 12px', fontSize: '0.8125rem', display: 'flex', alignItems: 'center', gap: '6px', flexShrink: 0 }}
                onClick={handleCopyReport}
                data-testid="copy-report-btn"
              >
                <IconCopy size={14} style={{ flexShrink: 0 }} />
                <span>{copiedReport ? 'Kopyalandı!' : 'JSON Raporu Kopyala'}</span>
              </button>
              <button
                className="btn btn-orange"
                style={{ padding: '6px 12px', fontSize: '0.8125rem', display: 'flex', alignItems: 'center', gap: '6px', flexShrink: 0 }}
                onClick={handleDownloadReport}
                data-testid="download-report-btn"
              >
                <IconDownload size={14} style={{ flexShrink: 0 }} />
                <span>JSON Raporu İndir</span>
              </button>
            </div>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 180px), 1fr))', gap: '12px', background: '#f0fdf4', padding: '14px', borderRadius: '6px', marginBottom: '16px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
            {outputLocation && (
              <div style={{ gridColumn: '1 / -1', background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: '6px', padding: '10px 14px', display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '12px', flexWrap: 'wrap', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
                <div style={{ minWidth: 0, flex: '1 1 200px', overflowWrap: 'anywhere' }}>
                  <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>Tamamlanan çıktı konumu</span>
                  <div style={{ fontSize: '0.8125rem', fontFamily: 'monospace', overflowWrap: 'anywhere', wordBreak: 'break-all', color: 'var(--text-primary)', fontWeight: 600, minWidth: 0 }} data-testid="output-location-path">
                    {outputLocation}
                  </div>
                </div>
                <button
                  className="btn btn-outline-gray"
                  style={{ padding: '4px 8px', fontSize: '0.75rem', display: 'flex', alignItems: 'center', gap: '4px', whiteSpace: 'nowrap', background: '#ffffff', flexShrink: 0 }}
                  onClick={handleCopyLocation}
                  title="Konumu panoya kopyala"
                  data-testid="copy-location-inline-btn"
                >
                  <IconCopy size={12} style={{ flexShrink: 0 }} />
                  <span>{copiedLocation ? 'Kopyalandı!' : 'Kopyala'}</span>
                </button>
              </div>
            )}
            {localEngine.jobReport.isFiltered && (
              <div style={{ gridColumn: '1 / -1', background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: '6px', padding: '12px 14px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }} data-testid="report-filter-summary">
                <div style={{ fontSize: '0.75rem', color: '#166534', fontWeight: 700, marginBottom: '6px' }}>
                  Filtreli dönüştürme bilgisi
                </div>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 150px), 1fr))', gap: '8px', fontSize: '0.8125rem' }}>
                  <div>
                    <span style={{ color: 'var(--text-muted)', fontSize: '0.75rem' }}>Seçilen / Toplam İleti: </span>
                    <strong data-testid="report-selected-messages">{localEngine.jobReport.selectedMessagesCount} / {localEngine.jobReport.totalSourceMessages}</strong>
                  </div>
                  <div>
                    <span style={{ color: 'var(--text-muted)', fontSize: '0.75rem' }}>Filtre dışı kalan: </span>
                    <strong data-testid="report-excluded-messages">{localEngine.jobReport.excludedMessagesCount}</strong>
                  </div>
                  {localEngine.jobReport.missingDateExcludedCount !== undefined && localEngine.jobReport.missingDateExcludedCount > 0 && (
                    <div>
                      <span style={{ color: 'var(--text-muted)', fontSize: '0.75rem' }}>Tarihsiz hariç: </span>
                      <strong data-testid="report-missing-date-excluded">{localEngine.jobReport.missingDateExcludedCount}</strong>
                    </div>
                  )}
                  {localEngine.jobReport.selectionFilter && (
                    <div style={{ gridColumn: '1 / -1', fontSize: '0.75rem', color: 'var(--text-muted)' }}>
                      <div>
                        Zaman Dilimi: {localEngine.jobReport.selectionFilter.timeZone} | Tarih Aralığı: {localEngine.jobReport.selectionFilter.startDate || 'Başlangıç serbest'} / {localEngine.jobReport.selectionFilter.endDate || 'Bitiş serbest'}
                      </div>
                      {localEngine.jobReport.selectionFilter.selectedFolders && localEngine.jobReport.selectionFilter.selectedFolders.length > 0 && (
                        <div style={{ marginTop: '0.25rem', overflowWrap: 'anywhere' }} data-testid="report-selected-folders">
                          <span style={{ fontWeight: 600 }}>Seçilen Klasörler ({localEngine.jobReport.selectionFilter.selectedFolders.length}): </span>
                          <span>
                            {localEngine.jobReport.selectionFilter.selectedFolders.map(f => f.folderPath || f.displayName).join(', ')}
                          </span>
                        </div>
                      )}
                    </div>
                  )}
                </div>
              </div>
            )}
            {localEngine.jobReport.clientContext && (
              <div style={{ minWidth: 0, overflowWrap: 'anywhere' }}>
                <span style={{ fontSize: '0.75rem', color: '#166534', fontWeight: 600 }}>Müşteri / proje</span>
                <div style={{ fontWeight: 700, overflowWrap: 'anywhere', wordBreak: 'break-word', minWidth: 0 }} data-testid="report-client-context">
                  {localEngine.jobReport.clientContext.companyName} / {localEngine.jobReport.clientContext.projectName}
                </div>
                <div style={{ fontSize: '0.75rem', color: '#15803d' }}>Kayıtlı müşteri / proje</div>
              </div>
            )}
            <div style={{ minWidth: 0, overflowWrap: 'anywhere' }}>
              <span style={{ fontSize: '0.75rem', color: '#166534', fontWeight: 600 }}>İş no</span>
              <div style={{ fontWeight: 700, fontFamily: 'monospace', fontSize: '0.8125rem', overflowWrap: 'anywhere', wordBreak: 'break-all', minWidth: 0 }} data-testid="report-job-id">
                {localEngine.jobReport.jobId}
              </div>
              <div style={{ fontSize: '0.75rem', color: '#15803d' }}>Doğrulanmış İş Kaydı</div>
            </div>
            <div style={{ minWidth: 0, overflowWrap: 'anywhere' }}>
              <span style={{ fontSize: '0.75rem', color: '#166534', fontWeight: 600 }}>OLUŞTURULAN PST</span>
              <div style={{ fontWeight: 700, overflowWrap: 'anywhere', wordBreak: 'break-all', minWidth: 0 }}>{localEngine.jobReport.outputPstFileName}</div>
              <div style={{ fontSize: '0.75rem', color: '#15803d' }}>{formatBytes(localEngine.jobReport.outputPstSizeBytes)}</div>
            </div>
            <div style={{ minWidth: 0, overflowWrap: 'anywhere' }}>
              <span style={{ fontSize: '0.75rem', color: '#166534', fontWeight: 600 }}>
                {localEngine.jobReport.isFiltered ? 'Dönüştürülen / seçilen' : 'Dönüştürülen / okunan'}
              </span>
              <div style={{ fontWeight: 700 }}>
                {localEngine.jobReport.isFiltered
                  ? `${localEngine.jobReport.itemsWritten} / ${localEngine.jobReport.selectedMessagesCount ?? localEngine.jobReport.itemsWritten} öğe`
                  : `${localEngine.jobReport.itemsWritten} / ${localEngine.jobReport.itemsRead} öğe`}
              </div>
              <div style={{ fontSize: '0.75rem', color: '#15803d' }}>Süre: {localEngine.jobReport.elapsedMilliseconds} ms</div>
            </div>
            <div style={{ minWidth: 0, overflowWrap: 'anywhere' }}>
              <span style={{ fontSize: '0.75rem', color: '#166534', fontWeight: 600 }}>Kaynak dosya bütünlüğü</span>
              <div style={{ fontWeight: 700, color: localEngine.jobReport.sourceHashMatch ? '#166534' : '#dc2626' }}>
                {localEngine.jobReport.sourceHashMatch ? 'Bit düzeyinde eşleşti' : 'Uyuşmazlık'}
              </div>
              <div style={{ fontSize: '0.75rem', color: '#15803d' }}>Sıfır değişiklik</div>
            </div>
            <div style={{ minWidth: 0, overflowWrap: 'anywhere' }}>
              <span style={{ fontSize: '0.75rem', color: '#166534', fontWeight: 600 }}>Ek ve CID doğrulaması</span>
              <div style={{ fontWeight: 700 }}>
                {localEngine.jobReport.reopenedPstVerification.totalAttachmentsVerified} Ek, {localEngine.jobReport.reopenedPstVerification.totalCidVerified} CID
              </div>
              <div style={{ fontSize: '0.75rem', color: '#15803d' }}>PR_ATTACH_CONTENT_ID korundu</div>
            </div>
          </div>

          {/* Unmeasured Fields Disclaimer */}
          <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', borderTop: '1px solid var(--border-light)', paddingTop: '10px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box', overflowWrap: 'anywhere' }}>
            <strong>Not:</strong> Taşıma üstbilgileri (RFC 822 transport headers) ve özel MAPI named properties gibi henüz ölçülmeyen alanlar raporda <code>UNKNOWN</code> olarak işaretlenmiştir; kanıt olmadan varsayılmaz.
          </div>
        </div>
        );
      })()}
    </div>
  );
};
