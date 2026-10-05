import React, { useState } from 'react';
import { AppState } from '../../state/useAppState';
import { Breadcrumbs } from '../layout/Breadcrumbs';
import {
  IconArrowLeft,
  IconCheckCircle,
  IconDownload,
  IconPause,
  IconPlay,
  IconRefreshCw,
} from '../ui/Icons';
import { generateReportBlob } from '../../state/useSimulation';
import { Badge } from '../ui/Badge';

interface TransferRunViewProps {
  state: AppState;
}

export const TransferRunView: React.FC<TransferRunViewProps> = ({ state }) => {
  const { simulation, plan, setCurrentView } = state;
  const {
    activeRun,
    totalCount,
    transferredCount,
    skippedCount,
    failedCount,
    pendingCount,
    progressPercent,
    pauseSimulation,
    resumeSimulation,
    retryFailedItems,
  } = simulation;

  const [downloadSuccess, setDownloadSuccess] = useState<string | null>(null);

  if (!activeRun) {
    return (
      <div style={{ padding: '40px 24px', textAlign: 'center' }}>
        <p>Aktif bir aktarım oturumu bulunamadı.</p>
        <button
          className="btn btn-outline-gray"
          onClick={() => setCurrentView('workspace')}
          style={{ marginTop: '12px' }}
        >
          Çalışma Alanına Dön
        </button>
      </div>
    );
  }

  const isCompleted = activeRun.status === 'completed';
  const isRunning = activeRun.status === 'running';
  const isPaused = activeRun.status === 'paused';

  const handleDownload = (format: 'json' | 'csv') => {
    const { blob, filename } = generateReportBlob(activeRun, format);
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);

    setDownloadSuccess(`${filename} indirildi.`);
    setTimeout(() => setDownloadSuccess(null), 3500);
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', flex: 1 }} data-testid="transfer-run-view">
      {/* Sub-header */}
      <div className="page-subheader">
        <div>
          <Breadcrumbs items={['Müşteriler', plan.client, plan.name, 'Aktarım']} />
          <h1 className="page-title">
            {isCompleted ? 'Aktarım tamamlandı' : 'Aktarım yürütülüyor'}
          </h1>
          <p className="page-subtitle">
            Sentetik veri simülasyonu · Gerçek posta sunucusu erişimi yoktur
          </p>
        </div>

        {/* Action Controls */}
        <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
          {!isCompleted ? (
            <button
              className="btn btn-outline-orange"
              onClick={isRunning ? pauseSimulation : resumeSimulation}
              data-testid="transfer-pause-resume-btn"
            >
              {isRunning ? (
                <>
                  <IconPause size={16} />
                  <span>Duraklat</span>
                </>
              ) : (
                <>
                  <IconPlay size={16} />
                  <span>Devam et</span>
                </>
              )}
            </button>
          ) : (
            <div style={{ display: 'flex', gap: '8px' }}>
              <button
                className="btn btn-outline-orange"
                onClick={() => handleDownload('json')}
                data-testid="download-json-btn"
              >
                <IconDownload size={16} />
                <span>JSON Raporu</span>
              </button>
              <button
                className="btn btn-primary-orange"
                onClick={() => handleDownload('csv')}
                data-testid="download-csv-btn"
              >
                <IconDownload size={16} />
                <span>CSV Raporu</span>
              </button>
            </div>
          )}

          <button
            className="btn btn-outline-gray"
            onClick={() => setCurrentView('workspace')}
            data-testid="back-to-workspace-btn"
          >
            <IconArrowLeft size={16} />
            <span>Plana dön</span>
          </button>
        </div>
      </div>

      {/* Main Body */}
      <div style={{ padding: '24px 32px', flex: 1, overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: '24px' }}>
        {downloadSuccess && (
          <div className="alert-banner alert-warning" style={{ background: '#ecfdf5', borderColor: '#a7f3d0', color: '#065f46' }}>
            <IconCheckCircle size={18} color="#10b981" />
            <span>{downloadSuccess}</span>
          </div>
        )}

        {/* Progress Card */}
        <div
          style={{
            padding: '20px 24px',
            border: '1px solid var(--border-light)',
            borderRadius: '8px',
            background: '#ffffff',
            boxShadow: '0 1px 3px rgba(0,0,0,0.05)',
          }}
          data-testid="transfer-progress-card"
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '10px' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
              <span style={{ fontWeight: 700, fontSize: '1.125rem' }}>İlerleme</span>
              {isRunning && <Badge variant="warning">Çalışıyor</Badge>}
              {isPaused && <Badge variant="neutral">Duraklatıldı</Badge>}
              {isCompleted && <Badge variant="success">Tamamlandı</Badge>}
            </div>
            <span style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--brand-orange)' }} data-testid="transfer-percent-text">
              %{progressPercent}
            </span>
          </div>

          <div className="progress-track" style={{ height: '10px', marginBottom: '16px' }}>
            <div
              className="progress-fill"
              style={{ width: `${progressPercent}%` }}
              data-testid="transfer-progress-fill"
            />
          </div>

          {/* Counts Grid (Mutually Exclusive Breakdown) */}
          <div
            style={{
              display: 'grid',
              gridTemplateColumns: 'repeat(auto-fit, minmax(130px, 1fr))',
              gap: '12px',
              paddingTop: '12px',
              borderTop: '1px solid var(--border-light)',
            }}
          >
            <div>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Toplam Kapsam</div>
              <div style={{ fontSize: '1.25rem', fontWeight: 700 }} data-testid="count-total">
                {totalCount}
              </div>
            </div>

            <div>
              <div style={{ fontSize: '0.75rem', color: 'var(--status-success)' }}>Aktarılan</div>
              <div style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--status-success)' }} data-testid="count-transferred">
                {transferredCount}
              </div>
            </div>

            <div>
              <div style={{ fontSize: '0.75rem', color: 'var(--brand-yellow)' }}>Atlanan</div>
              <div style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--brand-yellow)' }} data-testid="count-skipped">
                {skippedCount}
              </div>
            </div>

            <div>
              <div style={{ fontSize: '0.75rem', color: failedCount > 0 ? 'var(--status-error)' : 'var(--text-muted)' }}>
                Başarısız
              </div>
              <div
                style={{
                  fontSize: '1.25rem',
                  fontWeight: 700,
                  color: failedCount > 0 ? 'var(--status-error)' : 'inherit',
                }}
                data-testid="count-failed"
              >
                {failedCount}
              </div>
            </div>

            <div>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Bekleyen</div>
              <div style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--text-muted)' }} data-testid="count-pending">
                {pendingCount}
              </div>
            </div>
          </div>

          {/* Retry Button if Failures Exist */}
          {failedCount > 0 && (
            <div
              style={{
                marginTop: '16px',
                padding: '12px 16px',
                background: 'var(--status-error-bg)',
                borderRadius: '6px',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
              }}
              data-testid="retry-failed-banner"
            >
              <div style={{ fontSize: '0.875rem', color: 'var(--status-error)' }}>
                <strong>{failedCount} ileti aktarılamadı:</strong> Ağ zaman aşımı nedeniyle
                başarısız olan öğeler için yeniden deneme kuyruğu hazırdır.
              </div>

              <button
                className="btn btn-primary-orange"
                style={{ padding: '6px 14px', fontSize: '0.8125rem' }}
                onClick={retryFailedItems}
                data-testid="retry-failed-btn"
              >
                <IconRefreshCw size={14} />
                <span>Yeniden Dene (Çift Sayım Yok)</span>
              </button>
            </div>
          )}
        </div>

        {/* Live Items Table */}
        <div style={{ border: '1px solid var(--border-light)', borderRadius: '8px', overflow: 'hidden', background: '#ffffff' }}>
          <div style={{ padding: '12px 18px', borderBottom: '1px solid var(--border-light)', background: 'var(--bg-subtle)', fontWeight: 600, fontSize: '0.875rem' }}>
            İşlenen Öğeler ve Durum Günlüğü
          </div>

          <div style={{ maxHeight: '420px', overflowY: 'auto' }}>
            <table className="data-table" data-testid="simulation-items-table">
              <thead>
                <tr>
                  <th style={{ width: '12%' }}>Durum</th>
                  <th style={{ width: '40%' }}>İleti / Konu</th>
                  <th style={{ width: '18%' }}>Gönderen</th>
                  <th style={{ width: '10%', textAlign: 'right' }}>Boyut</th>
                  <th style={{ width: '20%' }}>İşlem Gerekçesi</th>
                </tr>
              </thead>
              <tbody>
                {activeRun.items.map((item, idx) => {
                  return (
                    <tr key={item.messageId} data-testid={`run-item-row-${idx}`}>
                      <td>
                        {item.status === 'transferred' && <Badge variant="success">Aktarıldı</Badge>}
                        {item.status === 'skipped' && <Badge variant="warning">Atlandı</Badge>}
                        {item.status === 'failed' && <Badge variant="error">Başarısız</Badge>}
                        {item.status === 'pending' && <Badge variant="neutral">Bekliyor</Badge>}
                      </td>
                      <td style={{ fontWeight: 500 }}>{item.subject}</td>
                      <td style={{ color: 'var(--text-muted)' }}>{item.sender}</td>
                      <td style={{ textAlign: 'right', color: 'var(--text-muted)' }}>
                        {item.sizeFormatted}
                      </td>
                      <td style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
                        {item.reason || 'Sırada bekliyor'}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  );
};
