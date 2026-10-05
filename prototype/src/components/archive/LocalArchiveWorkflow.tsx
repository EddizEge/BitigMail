import React, { useState } from 'react';
import { AppState } from '../../state/useAppState';
import { useLocalSplitEngine } from '../../hooks/useLocalSplitEngine';
import {
  IconAlertCircle,
  IconArchiveBox,
  IconCheck,
  IconClock,
  IconCopy,
  IconDownload,
  IconFile,
  IconRefreshCw,
} from '../ui/Icons';
import { Badge } from '../ui/Badge';

interface LocalArchiveWorkflowProps {
  state: AppState;
}

interface EffectiveArchiveSource {
  handle: string;
  fileName: string;
  sizeBytes: number;
  displayPath?: string | null;
  sha256?: string | null;
}

export const LocalArchiveWorkflow: React.FC<LocalArchiveWorkflowProps> = ({ state }) => {
  const splitEngine = useLocalSplitEngine({ targetJobId: state.selectedLocalJobId });
  const [copiedReport, setCopiedReport] = useState(false);
  const [copiedLocation, setCopiedLocation] = useState(false);

  const currentCompany = state.companies.find((c) => c.id === state.plan.companyId) || state.companies[0];
  const companyProjects = state.projects.filter((p) => p.companyId === (currentCompany?.id || ''));
  const currentProject = companyProjects.find((p) => p.id === state.plan.projectId) || companyProjects[0];

  const frozenContext = splitEngine.jobReport?.clientContext || splitEngine.activeJob?.clientContext;
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
    await splitEngine.pickSplitSourceFile();
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
    const loc =
      splitEngine.jobReport?.outputDirectoryPath ||
      splitEngine.activeJob?.outputDirectoryPath ||
      splitEngine.jobReport?.outputPath ||
      splitEngine.activeJob?.outputPath;
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
    if (!splitEngine.jobReport) return;
    const json = JSON.stringify(splitEngine.jobReport, null, 2);
    const blob = new Blob([json], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `rapor-${splitEngine.jobReport.jobId}.json`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  };

  const handleCopyReport = () => {
    if (!splitEngine.jobReport) return;
    const json = JSON.stringify(splitEngine.jobReport, null, 2);
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
    if (!bytes || bytes <= 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  };

  const effectiveSource: EffectiveArchiveSource | null = splitEngine.selectedSource
    ? {
        ...splitEngine.selectedSource,
        sha256: splitEngine.analysis?.sourceSha256 || null,
      }
    : (splitEngine.jobReport || splitEngine.activeJob)
    ? {
        handle: '',
        fileName: splitEngine.jobReport?.sourceFileName || splitEngine.activeJob?.sourceFileName || '',
        sizeBytes: splitEngine.jobReport?.sourceSizeBytes || 0,
        displayPath: null,
        sha256: splitEngine.jobReport?.sourceSha256Before || null,
      }
    : null;

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
      data-testid="local-archive-workflow"
    >
      {/* 1. Header & Client Context Bar */}
      <div className="card" style={{ padding: '16px', background: 'var(--bg-white)', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '12px', minWidth: 0, maxWidth: '100%' }}>
          <div style={{ minWidth: 0, flex: '1 1 auto' }}>
            <h2 style={{ margin: '0 0 6px 0', fontSize: '1.125rem', fontWeight: 700, color: 'var(--text-primary)', overflowWrap: 'anywhere' }}>
              Arşivleme ve bölümleme
            </h2>
            <div style={{ display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: '6px 8px', fontSize: '0.875rem', fontWeight: 600, minWidth: 0 }}>
              <span style={{ minWidth: 0, overflowWrap: 'anywhere', wordBreak: 'break-word' }}>
                Müşteri: <strong data-testid="context-company-name">{clientContext.companyName}</strong>
              </span>
              <span style={{ color: 'var(--border-mid)' }}>/</span>
              <span style={{ minWidth: 0, overflowWrap: 'anywhere', wordBreak: 'break-word' }}>
                Proje: <strong data-testid="context-project-name">{clientContext.projectName}</strong>
              </span>
            </div>
          </div>

          <div style={{ display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: '8px', flexShrink: 0 }}>
            <span style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>Motor Durumu:</span>
            {splitEngine.serviceStatus === 'checking' && (
              <Badge variant="warning">Bağlanıyor...</Badge>
            )}
            {splitEngine.serviceStatus === 'online' && (
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', color: '#16a34a', fontWeight: 600, fontSize: '0.8125rem' }}>
                <span style={{ width: '8px', height: '8px', borderRadius: '50%', background: '#16a34a' }} />
                <span>{splitEngine.client.getBaseUrl().replace(/^https?:\/\//, '')} Çevrimiçi</span>
              </span>
            )}
            {splitEngine.serviceStatus === 'offline' && (
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', color: '#dc2626', fontWeight: 600, fontSize: '0.8125rem' }}>
                <span style={{ width: '8px', height: '8px', borderRadius: '50%', background: '#dc2626' }} />
                <span>Çevrimdışı</span>
              </span>
            )}
            <button
              className="btn btn-outline-gray"
              style={{ padding: '4px 8px', fontSize: '0.75rem', flexShrink: 0 }}
              onClick={splitEngine.checkService}
              title="Bağlantıyı Yenile"
              data-testid="refresh-service-status-btn"
            >
              <IconRefreshCw size={12} />
            </button>
          </div>
        </div>
      </div>

      {/* 2. Service Offline Warning Banner */}
      {splitEngine.serviceStatus === 'offline' && (
        <div
          style={{
            background: '#fffbeb',
            border: '1px solid #fde68a',
            borderRadius: '8px',
            padding: '14px 16px',
            display: 'flex',
            gap: '12px',
            alignItems: 'flex-start',
            minWidth: 0,
            maxWidth: '100%',
            boxSizing: 'border-box',
          }}
          data-testid="service-offline-banner"
        >
          <IconAlertCircle size={24} color="#d97706" style={{ flexShrink: 0, marginTop: '2px' }} />
          <div style={{ minWidth: 0, flex: 1, overflowWrap: 'anywhere' }}>
            <h4 style={{ margin: '0 0 4px 0', fontSize: '0.9375rem', fontWeight: 700, color: '#92400e', overflowWrap: 'anywhere' }}>
              Yerel hizmet çevrimdışı (127.0.0.1:6174)
            </h4>
            <p style={{ margin: '0 0 8px 0', fontSize: '0.875rem', color: '#b45309', overflowWrap: 'anywhere' }}>
              Bölümlemeye başlamak için BitigMail yerel hizmetini çalıştırın.
            </p>
            <div style={{ background: '#fef3c7', padding: '8px 12px', borderRadius: '4px', fontFamily: 'monospace', fontSize: '0.8125rem', color: '#78350f', overflowWrap: 'anywhere', wordBreak: 'break-all', minWidth: 0 }}>
              .\scripts\start-local-engine.ps1
            </div>
          </div>
        </div>
      )}

      {/* 3. Source PST/OST File Selection Card */}
      <div className="card" style={{ padding: '16px 20px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }} data-testid="source-selection-card">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '8px', marginBottom: '16px' }}>
          <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
            <IconFile size={18} color="var(--brand-orange)" style={{ flexShrink: 0 }} />
            <span style={{ overflowWrap: 'anywhere' }}>1. Kaynak PST / OST Dosyası</span>
          </h3>
          {effectiveSource && effectiveSource.fileName ? (
            <button
              className="btn btn-outline-gray"
              style={{ padding: '4px 10px', fontSize: '0.8125rem', flexShrink: 0 }}
              onClick={handlePickSource}
              disabled={splitEngine.serviceStatus !== 'online' || splitEngine.isAnalyzing || splitEngine.activeJob?.status === 'converting' || splitEngine.activeJob?.status === 'verifying'}
              data-testid="reselect-source-btn"
            >
              Farklı Dosya Seç
            </button>
          ) : null}
        </div>

        {!effectiveSource || !effectiveSource.fileName ? (
          <div style={{ textAlign: 'center', padding: '24px 16px', background: 'var(--bg-subtle)', borderRadius: '8px', border: '1px dashed var(--border-mid)', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
            <div style={{ marginBottom: '12px', color: 'var(--text-muted)', overflowWrap: 'anywhere' }}>
              Windows yerel dosya seçici ile bilgisayarınızdaki gerçek PST veya OST arşiv dosyasını seçin.
            </div>
            <button
              className="btn btn-orange"
              onClick={handlePickSource}
              disabled={splitEngine.serviceStatus !== 'online'}
              data-testid="pick-source-btn"
            >
              PST veya OST dosyası seç
            </button>
            <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '8px', overflowWrap: 'anywhere' }}>
              Hem Outlook kişisel klasör (.pst) hem de çevrimdışı önbellek (.ost) dosyaları desteklenir.
            </div>
          </div>
        ) : (
          <div style={{ minWidth: 0, maxWidth: '100%' }}>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 180px), 1fr))', gap: '12px', background: 'var(--bg-subtle)', padding: '14px', borderRadius: '6px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
              <div style={{ minWidth: 0, overflowWrap: 'anywhere' }}>
                <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>DOSYA ADI</span>
                <div style={{ fontWeight: 600, overflowWrap: 'anywhere', wordBreak: 'break-all', minWidth: 0 }} data-testid="source-filename">
                  {effectiveSource.fileName}
                </div>
              </div>
              <div style={{ minWidth: 0 }}>
                <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>BOYUT</span>
                <div style={{ fontWeight: 600 }}>{formatBytes(effectiveSource.sizeBytes)}</div>
              </div>
              {(splitEngine.analysis?.sourceSha256 || effectiveSource.sha256) && (
                <div style={{ minWidth: 0, overflowWrap: 'anywhere' }}>
                  <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>SHA-256 HASH</span>
                  <div style={{ fontSize: '0.75rem', fontFamily: 'monospace', color: 'var(--text-muted)', overflowWrap: 'anywhere', wordBreak: 'break-all', minWidth: 0 }} data-testid="source-sha256">
                    {splitEngine.analysis?.sourceSha256 || effectiveSource.sha256}
                  </div>
                </div>
              )}
              {splitEngine.analysis?.formatInfo && (
                <div style={{ minWidth: 0 }}>
                  <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>BİÇİM İMZASI</span>
                  <div style={{ fontWeight: 600, fontSize: '0.8125rem', color: '#15803d' }}>
                    {splitEngine.analysis.formatInfo.formatName}
                  </div>
                </div>
              )}
              {effectiveSource.displayPath && (
                <div style={{ gridColumn: '1 / -1', minWidth: 0, maxWidth: '100%', overflowWrap: 'anywhere' }}>
                  <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>KAYNAK DOSYA KONUMU</span>
                  <div style={{ fontSize: '0.8125rem', fontFamily: 'monospace', overflowWrap: 'anywhere', wordBreak: 'break-all', color: 'var(--text-primary)', minWidth: 0 }} data-testid="source-display-path">
                    {effectiveSource.displayPath}
                  </div>
                </div>
              )}
            </div>

            {splitEngine.isAnalyzing && (
              <div style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '12px 0', color: 'var(--brand-orange)', fontSize: '0.875rem', minWidth: 0, overflowWrap: 'anywhere' }}>
                <span style={{ width: '14px', height: '14px', borderRadius: '50%', border: '2px solid var(--brand-orange)', borderTopColor: 'transparent', display: 'inline-block', animation: 'spin 1s linear infinite', flexShrink: 0 }} />
                <span>PST/OST dosyası inceleniyor ve klasör yapısı taranıyor...</span>
              </div>
            )}
          </div>
        )}

        {splitEngine.analysisError && (
          <div style={{ marginTop: '12px', padding: '10px 14px', background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', color: '#991b1b', fontSize: '0.875rem', minWidth: 0, overflowWrap: 'anywhere' }} data-testid="source-analysis-error">
            <strong>Hata:</strong> {splitEngine.analysisError}
          </div>
        )}
      </div>

      {/* 4. Analysis & Preflight Card */}
      {splitEngine.analysis && (
        <div className="card" style={{ padding: '16px 20px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }} data-testid="analysis-preflight-card">
          <h3 style={{ margin: '0 0 16px 0', fontSize: '1rem', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0, overflowWrap: 'anywhere' }}>
            <IconCheck size={18} color="#16a34a" style={{ flexShrink: 0 }} />
            <span>2. Dosya Analizi ve Ön Kontrol Bulguları</span>
          </h3>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 120px), 1fr))', gap: '10px', marginBottom: '16px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
            <div style={{ background: 'var(--bg-subtle)', padding: '12px', borderRadius: '6px', textAlign: 'center', minWidth: 0 }}>
              <div style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--brand-orange)' }}>{splitEngine.analysis.totalItems}</div>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Fiziksel İleti</div>
            </div>
            <div style={{ background: 'var(--bg-subtle)', padding: '12px', borderRadius: '6px', textAlign: 'center', minWidth: 0 }}>
              <div style={{ fontSize: '1.25rem', fontWeight: 700, color: '#2563eb' }}>{splitEngine.analysis.totalAttachments}</div>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Ek / Görsel</div>
            </div>
            <div style={{ background: 'var(--bg-subtle)', padding: '12px', borderRadius: '6px', textAlign: 'center', minWidth: 0 }}>
              <div style={{ fontSize: '1.25rem', fontWeight: 700, color: '#16a34a' }}>{splitEngine.analysis.activeFoldersCount}</div>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Dolu Klasör</div>
            </div>
            <div style={{ background: 'var(--bg-subtle)', padding: '12px', borderRadius: '6px', textAlign: 'center', minWidth: 0 }}>
              <div style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--text-muted)' }}>{splitEngine.analysis.emptyFoldersCount}</div>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Boş Klasör</div>
            </div>
          </div>

          {/* Folder Breakdown */}
          <div style={{ marginBottom: '16px', minWidth: 0, maxWidth: '100%' }}>
            <div style={{ fontSize: '0.8125rem', fontWeight: 700, color: 'var(--text-muted)', marginBottom: '8px' }}>
              KLASÖR YAPISI VE SAYIMLAR
            </div>
            <div style={{ maxHeight: '180px', overflowY: 'auto', overflowX: 'auto', WebkitOverflowScrolling: 'touch', border: '1px solid var(--border-light)', borderRadius: '6px', maxWidth: '100%', minWidth: 0, boxSizing: 'border-box' }}>
              <table className="data-table" style={{ fontSize: '0.8125rem', width: '100%', minWidth: '320px' }}>
                <thead>
                  <tr>
                    <th>Klasör Yolu</th>
                    <th>Öğe Sayısı</th>
                    <th>Durum</th>
                  </tr>
                </thead>
                <tbody>
                  {splitEngine.analysis.folders.map((f, idx) => (
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

          {/* Preflight Check Banners */}
          {splitEngine.analysis.preflight.hasTrialBlocker ? (
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
                <span>Deneme Sürümü Sınırı Engeli: Bölümleme Başlatılamaz</span>
              </div>
              <div>{splitEngine.analysis.preflight.trialBlockerReason}</div>
              <div style={{ fontSize: '0.75rem', marginTop: '6px', color: '#b91c1c' }}>
                Deneme sürümü sınırı gereği 50 öğeden fazla içeren klasörler işlenemez.
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
              <span>Ön kontrol başarılı: 50 öğe sınırını aşan klasör bulunmuyor, kaynak arşiv formatı doğrulandı.</span>
            </div>
          )}
        </div>
      )}

      {/* 5. Filter & Authoritative Selection Preview Card */}
      {splitEngine.analysis && !splitEngine.analysis.preflight.hasTrialBlocker && (
        <div className="card" style={{ padding: '16px 20px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }} data-testid="filter-selection-card">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px', flexWrap: 'wrap', gap: '8px', minWidth: 0, maxWidth: '100%' }}>
            <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
              <IconFile size={18} color="var(--brand-orange)" style={{ flexShrink: 0 }} />
              <span>3. Filtreleme ve Seçim Önizlemesi</span>
            </h3>
            <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
              Türkiye Saati (UTC+03:00)
            </div>
          </div>

          {/* Date Filter Inputs */}
          <div style={{ background: 'var(--bg-subtle)', padding: '14px', borderRadius: '6px', marginBottom: '16px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
            <div style={{ fontSize: '0.8125rem', fontWeight: 700, color: 'var(--text-muted)', marginBottom: '8px' }}>
              TARİH ARALIĞI (İsteğe Bağlı)
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 180px), 1fr))', gap: '12px', alignItems: 'center' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 600, color: 'var(--text-muted)', marginBottom: '4px' }}>
                  Başlangıç Tarihi (Dahil)
                </label>
                <input
                  type="date"
                  className="form-input"
                  style={{ width: '100%', padding: '6px 10px', fontSize: '0.875rem', borderRadius: '4px', border: '1px solid var(--border-mid)' }}
                  value={splitEngine.startDate}
                  onChange={(e) => splitEngine.setStartDate(e.target.value)}
                  data-testid="filter-start-date"
                />
              </div>
              <div>
                <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 600, color: 'var(--text-muted)', marginBottom: '4px' }}>
                  Bitiş Tarihi (Dahil)
                </label>
                <input
                  type="date"
                  className="form-input"
                  style={{ width: '100%', padding: '6px 10px', fontSize: '0.875rem', borderRadius: '4px', border: '1px solid var(--border-mid)' }}
                  value={splitEngine.endDate}
                  onChange={(e) => splitEngine.setEndDate(e.target.value)}
                  data-testid="filter-end-date"
                />
              </div>
              {(splitEngine.startDate || splitEngine.endDate) && (
                <div style={{ alignSelf: 'flex-end' }}>
                  <button
                    className="btn btn-outline-gray"
                    style={{ padding: '6px 12px', fontSize: '0.8125rem', width: '100%' }}
                    onClick={() => {
                      splitEngine.setStartDate('');
                      splitEngine.setEndDate('');
                    }}
                    data-testid="clear-dates-btn"
                  >
                    Tarihleri Temizle
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
                KLASÖR SEÇİMİ
              </div>
              <div style={{ display: 'flex', gap: '8px' }}>
                <button
                  className="btn btn-outline-gray"
                  style={{ padding: '3px 8px', fontSize: '0.75rem' }}
                  onClick={splitEngine.selectAllFolders}
                  data-testid="select-all-folders-btn"
                >
                  Tümünü Seç
                </button>
                <button
                  className="btn btn-outline-gray"
                  style={{ padding: '3px 8px', fontSize: '0.75rem' }}
                  onClick={splitEngine.clearAllFolders}
                  data-testid="clear-all-folders-btn"
                >
                  Tümünü Kaldır
                </button>
              </div>
            </div>
            <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginBottom: '8px' }}>
              Her onay kutusu yalnızca o klasörün doğrudan kendi iletilerini içerir; alt klasörlerin bağımsız onay kutuları vardır.
            </div>

            <div style={{ maxHeight: '180px', overflowY: 'auto', border: '1px solid var(--border-light)', borderRadius: '6px', padding: '8px 12px', background: '#ffffff' }}>
              {splitEngine.analysis.folders.map((f) => {
                const isChecked = splitEngine.selectedFolderIds.includes(f.folderId);
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
                      onChange={() => splitEngine.toggleFolder(f.folderId)}
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
              SEÇİM ÖZETİ
            </div>

            {splitEngine.isLoadingPreview && (
              <div style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '12px', background: 'var(--bg-subtle)', borderRadius: '6px', color: 'var(--brand-orange)', fontSize: '0.875rem' }} data-testid="preview-loading">
                <span style={{ width: '14px', height: '14px', borderRadius: '50%', border: '2px solid var(--brand-orange)', borderTopColor: 'transparent', display: 'inline-block', animation: 'spin 1s linear infinite', flexShrink: 0 }} />
                <span>Seçim ve filtre önizlemesi sunucudan hesaplanıyor...</span>
              </div>
            )}

            {splitEngine.previewError && (
              <div style={{ padding: '10px 14px', background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', color: '#991b1b', fontSize: '0.875rem' }} data-testid="preview-error-alert">
                <strong>Önizleme Hatası:</strong> {splitEngine.previewError}
              </div>
            )}

            {splitEngine.selectionPreview && (
              <div style={{ background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: '6px', padding: '14px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }} data-testid="selection-preview-card">
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 130px), 1fr))', gap: '10px', marginBottom: '10px' }}>
                  <div style={{ background: '#ffffff', padding: '10px', borderRadius: '6px', textAlign: 'center', border: '1px solid #e2e8f0' }}>
                    <div style={{ fontSize: '1.25rem', fontWeight: 700, color: '#16a34a' }} data-testid="preview-selected-count">
                      {splitEngine.selectionPreview.selectedMessagesCount}
                    </div>
                    <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Seçilen İleti</div>
                  </div>
                  <div style={{ background: '#ffffff', padding: '10px', borderRadius: '6px', textAlign: 'center', border: '1px solid #e2e8f0' }}>
                    <div style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--brand-orange)' }} data-testid="preview-attachments-count">
                      {splitEngine.selectionPreview.selectedAttachmentsCount}
                    </div>
                    <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Seçilen Ek</div>
                  </div>
                  <div style={{ background: '#ffffff', padding: '10px', borderRadius: '6px', textAlign: 'center', border: '1px solid #e2e8f0' }}>
                    <div style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--text-muted)' }} data-testid="preview-excluded-count">
                      {splitEngine.selectionPreview.excludedMessagesCount}
                    </div>
                    <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Hariç Tutulan</div>
                  </div>
                  <div style={{ background: '#ffffff', padding: '10px', borderRadius: '6px', textAlign: 'center', border: '1px solid #e2e8f0' }}>
                    <div style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--text-muted)' }} data-testid="preview-total-count">
                      {splitEngine.selectionPreview.totalSourceMessages}
                    </div>
                    <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Kaynak Toplam</div>
                  </div>
                </div>

                {splitEngine.selectionPreview.missingDateExcludedCount > 0 && (
                  <div style={{ fontSize: '0.75rem', color: '#b45309', marginBottom: '8px' }} data-testid="missing-date-alert">
                    ℹ {splitEngine.selectionPreview.missingDateExcludedCount} ileti tarihi olmadığı için tarih filtresi gereği hariç bırakıldı.
                  </div>
                )}

                {splitEngine.selectionPreview.selectedMessagesCount === 0 && (
                  <div style={{ padding: '8px 12px', background: '#fffbeb', border: '1px solid #fde68a', borderRadius: '6px', color: '#92400e', fontSize: '0.8125rem', fontWeight: 600 }} data-testid="zero-match-alert">
                    ⚠️ Seçim kriterlerinize uyan hiçbir ileti bulunamadı. Sıfır iletili bölme başlatılamaz.
                  </div>
                )}

                {!splitEngine.selectionPreview.canConvert && (splitEngine.selectionPreview.blockerReason || splitEngine.selectionPreview.blockReason) && (
                  <div style={{ padding: '8px 12px', background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', color: '#991b1b', fontSize: '0.8125rem', fontWeight: 600, marginTop: '8px' }} data-testid="preview-block-alert">
                    ⛔ {splitEngine.selectionPreview.blockerReason || splitEngine.selectionPreview.blockReason}
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      )}

      {/* 6. Split Options & Authoritative Split Plan Card */}
      {splitEngine.analysis && !splitEngine.analysis.preflight.hasTrialBlocker && (
        <div className="card" style={{ padding: '16px 20px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }} data-testid="split-options-card">
          <h3 style={{ margin: '0 0 16px 0', fontSize: '1rem', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
            <IconArchiveBox size={18} color="var(--brand-orange)" style={{ flexShrink: 0 }} />
            <span>4. Bölümleme Seçenekleri ve Plan Önizlemesi</span>
          </h3>

          {/* Mode Selection */}
          <div style={{ background: 'var(--bg-subtle)', padding: '14px', borderRadius: '6px', marginBottom: '16px' }}>
            <div style={{ fontSize: '0.8125rem', fontWeight: 700, color: 'var(--text-muted)', marginBottom: '10px' }}>
              BÖLÜMLEME YÖNTEMİ
            </div>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '10px', alignItems: 'center' }}>
              <label
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: '8px',
                  padding: '8px 14px',
                  borderRadius: '6px',
                  background: splitEngine.splitMode === 'year' ? '#fff7ed' : '#ffffff',
                  border: `1.5px solid ${splitEngine.splitMode === 'year' ? 'var(--brand-orange)' : 'var(--border-mid)'}`,
                  cursor: 'pointer',
                  fontWeight: 600,
                  fontSize: '0.875rem',
                }}
                data-testid="split-mode-year-label"
              >
                <input
                  type="radio"
                  name="splitMode"
                  value="year"
                  checked={splitEngine.splitMode === 'year'}
                  onChange={() => splitEngine.setSplitMode('year')}
                  data-testid="split-mode-year-radio"
                />
                <span>Yıllara göre</span>
              </label>

              <label
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: '8px',
                  padding: '8px 14px',
                  borderRadius: '6px',
                  background: splitEngine.splitMode === 'size' ? '#fff7ed' : '#ffffff',
                  border: `1.5px solid ${splitEngine.splitMode === 'size' ? 'var(--brand-orange)' : 'var(--border-mid)'}`,
                  cursor: 'pointer',
                  fontWeight: 600,
                  fontSize: '0.875rem',
                }}
                data-testid="split-mode-size-label"
              >
                <input
                  type="radio"
                  name="splitMode"
                  value="size"
                  checked={splitEngine.splitMode === 'size'}
                  onChange={() => splitEngine.setSplitMode('size')}
                  data-testid="split-mode-size-radio"
                />
                <span>Boyuta göre</span>
              </label>
            </div>

            {/* Size Cap Configuration (only visible in size mode) */}
            {splitEngine.splitMode === 'size' && (
              <div style={{ marginTop: '14px', paddingTop: '14px', borderTop: '1px solid var(--border-light)' }}>
                <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 700, color: 'var(--text-main)', marginBottom: '6px' }}>
                  Azami Parça Boyutu Sınırı (MB):
                </label>
                <div style={{ display: 'flex', alignItems: 'center', gap: '10px', maxWidth: '280px' }}>
                  <input
                    type="number"
                    min="1"
                    step="1"
                    className="form-input"
                    style={{ width: '140px', padding: '6px 10px', fontSize: '0.875rem', borderRadius: '4px', border: '1px solid var(--border-mid)' }}
                    value={splitEngine.sizeCapMb}
                    onChange={(e) => splitEngine.setSizeCapMb(parseInt(e.target.value, 10) || 1)}
                    data-testid="split-size-cap-input"
                  />
                  <span style={{ fontSize: '0.875rem', fontWeight: 600, color: 'var(--text-muted)' }}>MB (Sabit Üst Sınır)</span>
                </div>
                <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '8px' }} data-testid="split-size-honest-preview">
                  Parça sayısı işlem sırasında belirlenir. Tek bir ileti ekleriyle birlikte bu sınırı aşarsa işlem durur; daha yüksek bir sınır seçmeniz gerekir.
                </div>
              </div>
            )}
          </div>

          {/* Plan Loading / Error */}
          {splitEngine.isLoadingPlan && (
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '12px', background: 'var(--bg-subtle)', borderRadius: '6px', color: 'var(--brand-orange)', fontSize: '0.875rem', marginBottom: '14px' }} data-testid="split-plan-loading">
              <span style={{ width: '14px', height: '14px', borderRadius: '50%', border: '2px solid var(--brand-orange)', borderTopColor: 'transparent', display: 'inline-block', animation: 'spin 1s linear infinite', flexShrink: 0 }} />
              <span>Bölümleme planı sunucuda hesaplanıyor...</span>
            </div>
          )}

          {splitEngine.planError && (
            <div style={{ padding: '10px 14px', background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', color: '#991b1b', fontSize: '0.875rem', marginBottom: '14px' }} data-testid="split-plan-error">
              <strong>Plan Hatası:</strong> {splitEngine.planError}
            </div>
          )}

          {/* Authoritative Plan Results */}
          {splitEngine.splitPlan && (
            <div style={{ background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: '6px', padding: '14px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }} data-testid="split-plan-card">
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '10px', flexWrap: 'wrap', gap: '8px' }}>
                <span style={{ fontSize: '0.8125rem', fontWeight: 700, color: 'var(--text-muted)' }}>
                  PLANLANAN ÇIKTI YAPISI ({splitEngine.splitPlan.splitMode === 'year' ? 'Yıllık Gruplar' : 'Boyut Sınırı'})
                </span>
              </div>

              {splitEngine.splitPlan.splitMode === 'year' && (
                <div>
                  {splitEngine.splitPlan.yearGroups && splitEngine.splitPlan.yearGroups.length > 0 ? (
                    <div style={{ overflowX: 'auto', border: '1px solid var(--border-light)', borderRadius: '6px', background: '#ffffff' }}>
                      <table className="data-table" style={{ fontSize: '0.8125rem', width: '100%' }} data-testid="split-year-groups-table">
                        <thead>
                          <tr>
                            <th>Yıl / Grup</th>
                            <th>İleti Sayısı</th>
                            <th>Ek Sayısı</th>
                            <th>Hedef PST Dosya Adı</th>
                          </tr>
                        </thead>
                        <tbody>
                          {splitEngine.splitPlan.yearGroups.map((yg, idx) => (
                            <tr key={idx} data-testid={`year-group-row-${yg.year}`}>
                              <td style={{ fontWeight: 700 }}>{yg.year}</td>
                              <td>{yg.messageCount}</td>
                              <td>{yg.attachmentCount}</td>
                              <td style={{ fontFamily: 'monospace', color: 'var(--brand-orange)', fontWeight: 600 }}>{yg.targetFileName}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  ) : (
                    <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)', fontStyle: 'italic', padding: '8px' }}>
                      Henüz yıl grubu bulunamadı veya seçim sıfır ileti içeriyor.
                    </div>
                  )}
                </div>
              )}

              {splitEngine.splitPlan.splitMode === 'size' && (
                <div style={{ background: '#ffffff', padding: '12px 14px', borderRadius: '6px', border: '1px solid #e2e8f0' }} data-testid="split-size-info-banner">
                  <div style={{ fontSize: '0.875rem', fontWeight: 600, color: 'var(--text-primary)', marginBottom: '6px' }}>
                    Seçilen İleti: <strong>{splitEngine.splitPlan.selectedMessagesCount || splitEngine.selectionPreview?.selectedMessagesCount || 0}</strong> adet | Ek: <strong>{splitEngine.splitPlan.selectedAttachmentsCount || splitEngine.selectionPreview?.selectedAttachmentsCount || 0}</strong> adet
                  </div>
                  <div style={{ fontSize: '0.8125rem', color: '#475569', lineHeight: 1.5 }}>
                    Belirlenen sabit sınır: <strong>{splitEngine.sizeCapMb} MB</strong> ({formatBytes((splitEngine.splitPlan.sizeCapBytes ?? (splitEngine.sizeCapMb * 1_000_000)))}). Parça sayısı işlem sırasında belirlenir. Tek bir ileti ekleriyle birlikte bu sınırı aşarsa işlem durur; daha yüksek bir sınır seçmeniz gerekir.
                  </div>
                </div>
              )}

              {splitEngine.splitPlan.estimatedRequiredBytes != null && (
                <div style={{ marginTop: '8px', fontSize: '0.8125rem', color: 'var(--text-muted)' }} data-testid="split-estimated-required-bytes">
                  Tahmini gereken alan: <strong>{formatBytes(splitEngine.splitPlan.estimatedRequiredBytes)}</strong>
                  {splitEngine.splitPlan.estimatedPartCount != null ? ` · Tahmini parça: ${splitEngine.splitPlan.estimatedPartCount}` : ''}.
                  {' '}Tüm değişmez kaynak temel alınır; rezervasyon veya kesin üst sınır değildir.
                </div>
              )}

              {!splitEngine.splitPlan.canSplit && splitEngine.splitPlan.blockerReason && (
                <div style={{ padding: '8px 12px', background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', color: '#991b1b', fontSize: '0.8125rem', fontWeight: 600, marginTop: '10px' }} data-testid="split-plan-blocker-alert">
                  ⛔ {splitEngine.splitPlan.blockerReason}
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {/* 7. Output Directory Selection Card */}
      {splitEngine.analysis && !splitEngine.analysis.preflight.hasTrialBlocker && (
        <div className="card" style={{ padding: '16px 20px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }} data-testid="output-dir-selection-card">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px', flexWrap: 'wrap', gap: '8px', minWidth: 0, maxWidth: '100%' }}>
            <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
              <IconFile size={18} color="var(--brand-orange)" style={{ flexShrink: 0 }} />
              <span>5. Hedef Çıktı Klasörü</span>
            </h3>
            {splitEngine.selectedOutputDir && (
              <button
                className="btn btn-outline-gray"
                style={{ padding: '4px 10px', fontSize: '0.8125rem', flexShrink: 0 }}
                onClick={splitEngine.pickOutputDir}
                disabled={splitEngine.activeJob?.status === 'converting'}
                data-testid="reselect-output-dir-btn"
              >
                Farklı Klasör Seç
              </button>
            )}
          </div>

          {!splitEngine.selectedOutputDir ? (
            <div style={{ textAlign: 'center', padding: '24px 16px', background: 'var(--bg-subtle)', borderRadius: '8px', border: '1px dashed var(--border-mid)', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
              <button
                className="btn btn-orange"
                onClick={splitEngine.pickOutputDir}
                data-testid="pick-output-dir-btn"
                style={{ maxWidth: '100%' }}
              >
                Hedef Klasör Seç (Üst Dizin)
              </button>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '8px', overflowWrap: 'anywhere' }}>
                Seçilen dizin altında yeni ve benzersiz bir arşiv paketi klasörü oluşturulur. Mevcut dosyaların üzerine yazılmaz.
              </div>
            </div>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', background: 'var(--bg-subtle)', padding: '12px 16px', borderRadius: '6px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: '8px', minWidth: 0, maxWidth: '100%' }}>
                <div style={{ minWidth: 0, flex: '1 1 auto', overflowWrap: 'anywhere' }}>
                  <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>SEÇİLEN ÜST DİZİN</span>
                  <div style={{ fontWeight: 600, overflowWrap: 'anywhere', wordBreak: 'break-all', minWidth: 0 }} data-testid="output-dir-name">
                    {splitEngine.selectedOutputDir.fileName}
                  </div>
                </div>
                <span style={{ flexShrink: 0 }}>
                  <Badge variant="success">Hedef Dizin Hazır</Badge>
                </span>
              </div>
              {splitEngine.selectedOutputDir.displayPath && (
                <div style={{ minWidth: 0, maxWidth: '100%', overflowWrap: 'anywhere' }}>
                  <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>TAM DİZİN YOLU</span>
                  <div style={{ fontSize: '0.8125rem', fontFamily: 'monospace', overflowWrap: 'anywhere', wordBreak: 'break-all', color: 'var(--text-primary)', minWidth: 0 }} data-testid="output-dir-path">
                    {splitEngine.selectedOutputDir.displayPath}
                  </div>
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {/* 8. Split Execution & Real-Time Progress Card */}
      {((splitEngine.selectedSource && !splitEngine.analysis?.preflight.hasTrialBlocker) || (splitEngine.activeJob && splitEngine.activeJob.status !== 'completed')) && (
        <div className="card" style={{ padding: '16px 20px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }} data-testid="split-progress-card">
          <h3 style={{ margin: '0 0 16px 0', fontSize: '1rem', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
            <IconClock size={18} color="var(--brand-orange)" style={{ flexShrink: 0 }} />
            <span>6. Bölümleme ve İlerleme</span>
          </h3>

          {!splitEngine.activeJob || splitEngine.activeJob.status === 'queued' ? (
            <div>
              <button
                className="btn btn-orange"
                style={{ padding: '10px 24px', fontSize: '0.9375rem', fontWeight: 700, maxWidth: '100%' }}
                onClick={() => splitEngine.startSplit(clientContext)}
                disabled={
                  splitEngine.isStarting ||
                  !splitEngine.splitPlan ||
                  !splitEngine.splitPlan.canSplit ||
                  splitEngine.splitPlan.selectedMessagesCount === 0 ||
                  !splitEngine.selectedOutputDir
                }
                data-testid="start-split-btn"
              >
                {splitEngine.isStarting ? 'Bölümleme Başlatılıyor...' : 'Başlat / sıraya ekle'}
              </button>
            </div>
          ) : (
            <div style={{ minWidth: 0, maxWidth: '100%' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px', flexWrap: 'wrap', gap: '8px', minWidth: 0, maxWidth: '100%' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexShrink: 0 }}>
                  <span style={{ fontSize: '0.875rem', fontWeight: 700 }}>Aşama:</span>
                  <Badge
                    variant={
                      splitEngine.activeJob.status === 'completed'
                        ? 'success'
                        : splitEngine.activeJob.status === 'failed' || splitEngine.activeJob.status === 'interrupted'
                        ? 'error'
                        : 'warning'
                    }
                  >
                    {splitEngine.activeJob.stage}
                  </Badge>
                </div>

                <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)', minWidth: 0, overflowWrap: 'anywhere', wordBreak: 'break-all' }}>
                  İş Kimliği: <code style={{ fontSize: '0.75rem', overflowWrap: 'anywhere', wordBreak: 'break-all' }} data-testid="split-job-id">{splitEngine.activeJob.jobId}</code>
                </div>
              </div>

              {/* Progress Counters */}
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 120px), 1fr))', gap: '10px', background: 'var(--bg-subtle)', padding: '14px', borderRadius: '6px', marginBottom: '14px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
                <div style={{ minWidth: 0 }}>
                  <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>OKUNAN</span>
                  <div style={{ fontSize: '1.125rem', fontWeight: 700 }} data-testid="counter-items-read">{splitEngine.activeJob.itemsRead}</div>
                </div>
                <div style={{ minWidth: 0 }}>
                  <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>YAZILAN</span>
                  <div style={{ fontSize: '1.125rem', fontWeight: 700, color: '#16a34a' }} data-testid="counter-items-written">{splitEngine.activeJob.itemsWritten}</div>
                </div>
                <div style={{ minWidth: 0 }}>
                  <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>BAŞARISIZ</span>
                  <div style={{ fontSize: '1.125rem', fontWeight: 700, color: splitEngine.activeJob.failedItems > 0 ? '#dc2626' : 'var(--text-muted)' }}>
                    {splitEngine.activeJob.failedItems}
                  </div>
                </div>
                <div style={{ minWidth: 0 }}>
                  <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>İŞLENEN KLASÖR</span>
                  <div style={{ fontSize: '0.8125rem', fontWeight: 600, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', overflowWrap: 'anywhere', wordBreak: 'break-all' }}>
                    {splitEngine.activeJob.currentFolder || '—'}
                  </div>
                </div>
              </div>
            </div>
          )}

          {splitEngine.jobError && (
            <div style={{ marginTop: '12px', padding: '10px 14px', background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', color: '#991b1b', fontSize: '0.875rem', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box', overflowWrap: 'anywhere' }} data-testid="split-job-error">
              <strong>Hata:</strong> {splitEngine.jobError}
            </div>
          )}
        </div>
      )}

      {/* 9. Multipart Split Verification Report & Download Card */}
      {splitEngine.jobReport && (() => {
        const outputLocation =
          splitEngine.jobReport.outputDirectoryPath ||
          splitEngine.jobReport.outputPath ||
          splitEngine.activeJob?.outputDirectoryPath ||
          splitEngine.activeJob?.outputPath;
        const parts = splitEngine.jobReport.parts || [];

        return (
          <div className="card" style={{ padding: '16px 20px', background: '#ffffff', border: '1px solid #bbf7d0', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }} data-testid="conversion-report-card">
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px', flexWrap: 'wrap', gap: '8px', minWidth: 0, maxWidth: '100%' }}>
              <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 700, color: '#166534', display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0, overflowWrap: 'anywhere' }}>
                <IconCheck size={20} color="#16a34a" style={{ flexShrink: 0 }} />
                <span>Arşivleme, Bölümleme ve Çok Parçalı Doğrulama Tamamlandı</span>
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

            {/* Output Directory Path Banner */}
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 180px), 1fr))', gap: '12px', background: '#f0fdf4', padding: '14px', borderRadius: '6px', marginBottom: '16px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
              {outputLocation && (
                <div style={{ gridColumn: '1 / -1', background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: '6px', padding: '10px 14px', display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '12px', flexWrap: 'wrap', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }}>
                  <div style={{ minWidth: 0, flex: '1 1 200px', overflowWrap: 'anywhere' }}>
                    <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)', fontWeight: 600 }}>YENİ ARŞİV ÇIKTI DİZİNİ</span>
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

              {/* Summary Stats */}
              {splitEngine.jobReport.clientContext && (
                <div style={{ minWidth: 0, overflowWrap: 'anywhere' }}>
                  <span style={{ fontSize: '0.75rem', color: '#166534', fontWeight: 600 }}>MÜŞTERİ / PROJE</span>
                  <div style={{ fontWeight: 700, overflowWrap: 'anywhere', wordBreak: 'break-word', minWidth: 0 }} data-testid="report-client-context">
                    {splitEngine.jobReport.clientContext.companyName} / {splitEngine.jobReport.clientContext.projectName}
                  </div>
                  <div style={{ fontSize: '0.75rem', color: '#15803d' }}>Kayıtlı Sözleşme Bağlamı</div>
                </div>
              )}

              <div style={{ minWidth: 0, overflowWrap: 'anywhere' }}>
                <span style={{ fontSize: '0.75rem', color: '#166534', fontWeight: 600 }}>İŞ NO</span>
                <div style={{ fontWeight: 700, fontFamily: 'monospace', fontSize: '0.8125rem', overflowWrap: 'anywhere', wordBreak: 'break-all', minWidth: 0 }} data-testid="report-job-id">
                  {splitEngine.jobReport.jobId}
                </div>
                <div style={{ fontSize: '0.75rem', color: '#15803d' }}>Doğrulanmış Bölümleme İşi</div>
              </div>

              <div style={{ minWidth: 0, overflowWrap: 'anywhere' }}>
                <span style={{ fontSize: '0.75rem', color: '#166534', fontWeight: 600 }}>BÖLÜMLEME MODU</span>
                <div style={{ fontWeight: 700, overflowWrap: 'anywhere', wordBreak: 'break-all', minWidth: 0 }} data-testid="report-split-mode">
                  {splitEngine.jobReport.splitMode === 'year'
                    ? 'Yıllara Göre'
                    : `Boyuta Göre (${formatBytes(splitEngine.jobReport.splitSizeCapBytes || 0)})`}
                </div>
                <div style={{ fontSize: '0.75rem', color: '#15803d' }}>{parts.length} Doğrulanmış PST Parçası</div>
              </div>

              <div style={{ minWidth: 0, overflowWrap: 'anywhere' }}>
                <span style={{ fontSize: '0.75rem', color: '#166534', fontWeight: 600 }}>YAZILAN / SEÇİLEN</span>
                <div style={{ fontWeight: 700 }} data-testid="report-items-count">
                  {splitEngine.jobReport.itemsWritten} / {splitEngine.jobReport.selectedMessagesCount ?? splitEngine.jobReport.itemsWritten} öğe
                </div>
                <div style={{ fontSize: '0.75rem', color: '#15803d' }}>Süre: {splitEngine.jobReport.elapsedMilliseconds} ms</div>
              </div>

              <div style={{ minWidth: 0, overflowWrap: 'anywhere' }}>
                <span style={{ fontSize: '0.75rem', color: '#166534', fontWeight: 600 }}>KAYNAK HASH KORUMASI</span>
                <div style={{ fontWeight: 700, color: splitEngine.jobReport.sourceHashMatch ? '#166534' : '#dc2626' }}>
                  {splitEngine.jobReport.sourceHashMatch ? 'BİT DÜZEYİNDE EŞLEŞTİ' : 'UYUŞMAZLIK'}
                </div>
                <div style={{ fontSize: '0.75rem', color: '#15803d' }}>Sıfır Değişiklik</div>
              </div>

              <div style={{ minWidth: 0, overflowWrap: 'anywhere' }}>
                <span style={{ fontSize: '0.75rem', color: '#166534', fontWeight: 600 }}>EK & CID DOĞRULAMASI</span>
                <div style={{ fontWeight: 700 }}>
                  {splitEngine.jobReport.reopenedPstVerification.totalAttachmentsVerified} Ek, {splitEngine.jobReport.reopenedPstVerification.totalCidVerified} CID
                </div>
                <div style={{ fontSize: '0.75rem', color: '#15803d' }}>PR_ATTACH_CONTENT_ID korundu</div>
              </div>
            </div>

            {/* Filter Summary */}
            {splitEngine.jobReport.isFiltered && (
              <div style={{ background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: '6px', padding: '12px 14px', marginBottom: '16px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box' }} data-testid="report-filter-summary">
                <div style={{ fontSize: '0.75rem', color: '#166534', fontWeight: 700, marginBottom: '6px' }}>
                  FİLTRELİ ARŞİV BİLGİSİ
                </div>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 150px), 1fr))', gap: '8px', fontSize: '0.8125rem' }}>
                  <div>
                    <span style={{ color: 'var(--text-muted)', fontSize: '0.75rem' }}>Seçilen / Toplam İleti: </span>
                    <strong data-testid="report-selected-messages">{splitEngine.jobReport.selectedMessagesCount} / {splitEngine.jobReport.totalSourceMessages}</strong>
                  </div>
                  <div>
                    <span style={{ color: 'var(--text-muted)', fontSize: '0.75rem' }}>Filtre Dışı Kalan: </span>
                    <strong data-testid="report-excluded-messages">{splitEngine.jobReport.excludedMessagesCount}</strong>
                  </div>
                  {splitEngine.jobReport.missingDateExcludedCount !== undefined && splitEngine.jobReport.missingDateExcludedCount > 0 && (
                    <div>
                      <span style={{ color: 'var(--text-muted)', fontSize: '0.75rem' }}>Tarihsiz Hariç: </span>
                      <strong data-testid="report-missing-date-excluded">{splitEngine.jobReport.missingDateExcludedCount}</strong>
                    </div>
                  )}
                  {splitEngine.jobReport.selectionFilter && (
                    <div style={{ gridColumn: '1 / -1', fontSize: '0.75rem', color: 'var(--text-muted)' }}>
                      <div>
                        Zaman Dilimi: {splitEngine.jobReport.selectionFilter.timeZone} | Tarih Aralığı: {splitEngine.jobReport.selectionFilter.startDate || 'Başlangıç serbest'} / {splitEngine.jobReport.selectionFilter.endDate || 'Bitiş serbest'}
                      </div>
                      {splitEngine.jobReport.selectionFilter.selectedFolders && splitEngine.jobReport.selectionFilter.selectedFolders.length > 0 && (
                        <div style={{ marginTop: '0.25rem', overflowWrap: 'anywhere' }} data-testid="report-selected-folders">
                          <span style={{ fontWeight: 600 }}>Seçilen Klasörler ({splitEngine.jobReport.selectionFilter.selectedFolders.length}): </span>
                          <span>
                            {splitEngine.jobReport.selectionFilter.selectedFolders.map(f => f.folderPath || f.displayName).join(', ')}
                          </span>
                        </div>
                      )}
                    </div>
                  )}
                </div>
              </div>
            )}

            {/* Per-Part Detailed Table */}
            {parts.length > 0 && (
              <div style={{ marginBottom: '16px', minWidth: 0, maxWidth: '100%' }}>
                <div style={{ fontSize: '0.8125rem', fontWeight: 700, color: '#166534', marginBottom: '8px' }}>
                  DOĞRULANMIŞ ARŞİV PARÇALARI ({parts.length} Parça)
                </div>
                <div style={{ overflowX: 'auto', border: '1px solid var(--border-light)', borderRadius: '6px', background: '#ffffff' }}>
                  <table className="data-table" style={{ fontSize: '0.8125rem', width: '100%' }} data-testid="split-parts-table">
                    <thead>
                      <tr>
                        <th>Parça Dosyası</th>
                        <th>Boyut</th>
                        <th>İleti</th>
                        <th>Ek / CID</th>
                        <th>SHA-256 Hash</th>
                        <th>Yeniden Açma Doğrulaması</th>
                      </tr>
                    </thead>
                    <tbody>
                      {parts.map((p, idx) => (
                        <tr key={idx} data-testid={`split-part-row-${idx}`}>
                          <td style={{ fontWeight: 700, color: 'var(--brand-orange)', fontFamily: 'monospace' }}>
                            {p.partFileName}
                          </td>
                          <td style={{ whiteSpace: 'nowrap' }}>{formatBytes(p.partSizeBytes)}</td>
                          <td style={{ fontWeight: 600 }}>{p.itemsWritten}</td>
                          <td style={{ whiteSpace: 'nowrap' }}>{p.totalAttachmentsVerified} Ek, {p.totalCidVerified} CID</td>
                          <td style={{ fontFamily: 'monospace', fontSize: '0.75rem', color: 'var(--text-muted)', overflowWrap: 'anywhere', wordBreak: 'break-all' }}>
                            {p.partSha256}
                          </td>
                          <td>
                            {p.reopenedPstVerification?.itemCountMatch ? (
                              <span style={{ color: '#16a34a', fontWeight: 600 }}>✓ Doğrulandı ({p.reopenedPstVerification.totalPhysicalItemsFound} öğe)</span>
                            ) : (
                              <span style={{ color: '#dc2626', fontWeight: 600 }}>Uyuşmazlık</span>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            )}

            {/* Contract Disclaimer */}
            <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', borderTop: '1px solid var(--border-light)', paddingTop: '10px', minWidth: 0, maxWidth: '100%', boxSizing: 'border-box', overflowWrap: 'anywhere' }}>
              <strong>Sözleşme Notu:</strong> Tüm arşiv parçaları kapatıldıktan sonra bağımsız olarak yeniden açılmış, ekler ve satır içi görseller (CID) doğrulanmıştır. Her parça için üst klasör hiyerarşisi korunmuştur.
            </div>
          </div>
        );
      })()}
    </div>
  );
};
