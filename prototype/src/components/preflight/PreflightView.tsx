import React, { useState } from 'react';
import { AppState } from '../../state/useAppState';
import { Breadcrumbs } from '../layout/Breadcrumbs';
import {
  IconAlertTriangle,
  IconArrowLeft,
  IconCheckCircle,
  IconFile,
  IconPlay,
  IconRefreshCw,
  IconServer,
} from '../ui/Icons';
import { formatBytes } from '../../data/sampleMessages';
import { Modal } from '../ui/Modal';

interface PreflightViewProps {
  state: AppState;
}

export const PreflightView: React.FC<PreflightViewProps> = ({ state }) => {
  const {
    plan,
    preflightState,
    isPreflightStale,
    runPreflightCheck,
    resolveOversizedItem,
    handleStartTransfer,
    setCurrentView,
  } = state;

  const [detailModalOpen, setDetailModalOpen] = useState(false);

  const oversizedItem = preflightState?.oversizedItem;
  const hasBlocker = preflightState?.hasBlocker ?? false;

  const totalScopeBytes = preflightState?.scopeBytes ?? 0;
  const scopeCount = preflightState?.scopeCount ?? 0;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', flex: 1 }} data-testid="preflight-view">
      {/* Sub-header with Stepper */}
      <div className="page-subheader" style={{ alignItems: 'center' }}>
        <div>
          <Breadcrumbs items={['Müşteriler', plan.client, plan.name]} />
          <h1 className="page-title">Ön kontrol</h1>
          <p className="page-subtitle">Örnek veriler · Tasarım taslağı</p>
        </div>

        {/* 4-Step Stepper */}
        <div className="stepper" data-testid="preflight-stepper">
          <div className="step-item completed">
            <span className="step-circle">✓</span>
            <span>Kaynak ve hedef</span>
          </div>
          <span style={{ color: '#cbd5e1' }}>—</span>
          <div className="step-item completed">
            <span className="step-circle">✓</span>
            <span>Kapsam</span>
          </div>
          <span style={{ color: '#cbd5e1' }}>—</span>
          <div className="step-item active">
            <span className="step-circle">3</span>
            <span>Ön kontrol</span>
          </div>
          <span style={{ color: '#cbd5e1' }}>—</span>
          <div className="step-item">
            <span className="step-circle">4</span>
            <span>Aktarım</span>
          </div>
        </div>

        {/* Placeholder spacer for symmetry */}
        <div className="preflight-spacer" style={{ width: '100px' }} />
      </div>

      {/* Stale Warning Banner */}
      {isPreflightStale && (
        <div
          style={{
            padding: '10px 24px',
            background: 'var(--brand-yellow-light)',
            borderBottom: '1px solid var(--brand-yellow-border)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            fontSize: '0.875rem',
            color: '#78350f',
          }}
          data-testid="stale-preflight-banner"
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <IconRefreshCw size={16} />
            <span>
              <strong>Dikkat:</strong> Plan ayarları veya filtreler değiştirildi. Ön kontrol güncel
              değil.
            </span>
          </div>
          <button
            className="btn btn-outline-orange"
            style={{ padding: '4px 12px', fontSize: '0.8125rem' }}
            onClick={runPreflightCheck}
            data-testid="re-evaluate-preflight-btn"
          >
            Yeniden Kontrol Et
          </button>
        </div>
      )}

      {/* Main 2-Panel Content */}
      <div className="preflight-grid">
        {/* Left Side: Checklist & Resolution */}
        <div className="preflight-left">
          <h2 style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--text-main)', marginBottom: '16px' }}>
            Başlatmadan önce çözülmeli
          </h2>

          {/* Warning Banner if Blocker exists */}
          {hasBlocker && (
            <div className="alert-banner alert-warning" data-testid="preflight-blocker-banner">
              <IconAlertTriangle size={22} color="var(--brand-orange)" />
              <div>
                <div style={{ fontWeight: 700, color: '#78350f' }}>1 ileti hedef sınırını aşıyor</div>
                <div style={{ fontSize: '0.8125rem', color: '#92400e', marginTop: '2px' }}>
                  Bir işlem seçmeden aktarım başlatılamaz.
                </div>
              </div>
            </div>
          )}

          {/* Checklist table */}
          <div style={{ border: '1px solid var(--border-light)', borderRadius: '8px', overflow: 'hidden' }}>
            {preflightState?.checks.map((chk) => {
              const isItemSize = chk.key === 'item_size';
              const isPassed = chk.status === 'passed';

              return (
                <div
                  key={chk.key}
                  style={{
                    padding: '14px 18px',
                    borderBottom: '1px solid var(--border-light)',
                    display: 'flex',
                    alignItems: 'flex-start',
                    justifyContent: 'space-between',
                    background: isItemSize && hasBlocker ? 'var(--brand-yellow-subtle)' : '#ffffff',
                  }}
                  data-testid={`checklist-row-${chk.key}`}
                >
                  <div style={{ display: 'flex', alignItems: 'flex-start', gap: '12px' }}>
                    {isPassed ? (
                      <IconCheckCircle size={20} color="var(--status-success)" />
                    ) : (
                      <IconAlertTriangle size={20} color="var(--brand-orange)" />
                    )}
                    <div>
                      <div style={{ fontWeight: 600, color: 'var(--text-main)', fontSize: '0.9375rem' }}>
                        {chk.title}
                      </div>
                      {chk.detail && (
                        <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)', marginTop: '2px' }}>
                          {chk.detail}
                        </div>
                      )}

                      {/* Blocker Interactive Card if item_size check */}
                      {isItemSize && oversizedItem && (
                        <div
                          style={{
                            marginTop: '12px',
                            padding: '14px',
                            background: '#ffffff',
                            border: '1px solid var(--border-mid)',
                            borderRadius: '6px',
                            maxWidth: '560px',
                          }}
                          data-testid="blocker-resolution-box"
                        >
                          <div
                            style={{
                              display: 'flex',
                              justifyContent: 'space-between',
                              alignItems: 'center',
                              marginBottom: '8px',
                            }}
                          >
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                              <IconFile size={18} color="#64748b" />
                              <span style={{ fontWeight: 600, color: 'var(--text-main)' }}>
                                {oversizedItem.subject}
                              </span>
                            </div>
                            <span style={{ fontWeight: 700, color: 'var(--brand-orange)' }}>
                              {oversizedItem.sizeFormatted}
                            </span>
                          </div>

                          <p style={{ fontSize: '0.8125rem', color: 'var(--text-muted)', marginBottom: '12px' }}>
                            Bu örnek hedef için sınır: 35 MB.
                          </p>

                          {/* Resolution Choices */}
                          <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                            <label
                              style={{
                                display: 'flex',
                                alignItems: 'flex-start',
                                gap: '10px',
                                cursor: 'pointer',
                                fontSize: '0.875rem',
                              }}
                              data-testid="resolve-choice-skip-label"
                            >
                              <input
                                type="radio"
                                name="oversized-resolution"
                                checked={plan.oversizedResolution === 'skip_and_report'}
                                onChange={() => resolveOversizedItem('skip_and_report')}
                                style={{ marginTop: '3px', cursor: 'pointer' }}
                                data-testid="resolve-choice-skip-radio"
                              />
                              <div>
                                <div style={{ fontWeight: 600, color: 'var(--text-main)' }}>
                                  Bu iletiyi atla ve raporla
                                </div>
                                <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
                                  Diğer iletilerle devam et
                                </div>
                              </div>
                            </label>

                            <label
                              style={{
                                display: 'flex',
                                alignItems: 'flex-start',
                                gap: '10px',
                                cursor: 'pointer',
                                fontSize: '0.875rem',
                              }}
                              data-testid="resolve-choice-change-target-label"
                            >
                              <input
                                type="radio"
                                name="oversized-resolution"
                                checked={plan.oversizedResolution === 'change_target'}
                                onChange={() => {
                                  resolveOversizedItem('change_target');
                                  setCurrentView('workspace');
                                }}
                                style={{ marginTop: '3px', cursor: 'pointer' }}
                                data-testid="resolve-choice-change-target-radio"
                              />
                              <div>
                                <div style={{ fontWeight: 600, color: 'var(--text-main)' }}>
                                  Hedefi değiştir
                                </div>
                                <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
                                  Uyumlu bir hesap veya dosya seç
                                </div>
                              </div>
                            </label>
                          </div>

                          <div style={{ marginTop: '12px' }}>
                            <span
                              className="brand-link"
                              onClick={() => setDetailModalOpen(true)}
                              data-testid="inspect-oversized-item-link"
                            >
                              İletiyi incele
                            </span>
                          </div>
                        </div>
                      )}
                    </div>
                  </div>

                  <div style={{ fontWeight: 600, fontSize: '0.875rem', color: isPassed ? 'var(--status-success)' : 'var(--brand-orange)' }}>
                    {chk.resultLabel}
                  </div>
                </div>
              );
            })}
          </div>
        </div>

        {/* Right Side: Aktarım Özeti */}
        <div className="preflight-right" data-testid="preflight-summary-pane">
          <h2 style={{ fontSize: '1.125rem', fontWeight: 700, color: 'var(--text-main)' }}>
            Aktarım özeti
          </h2>

          <div className="plan-field">
            <span className="plan-label">Kaynak</span>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginTop: '2px' }}>
              <IconServer size={18} color="var(--brand-orange)" />
              <div>
                <div style={{ fontWeight: 600, fontSize: '0.875rem' }}>{plan.sourceType}</div>
                <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>{plan.sourceAccount}</div>
              </div>
            </div>
          </div>

          <div className="plan-field">
            <span className="plan-label">Hedef</span>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginTop: '2px' }}>
              <IconServer size={18} color="#64748b" />
              <div>
                <div style={{ fontWeight: 600, fontSize: '0.875rem' }}>{plan.targetType}</div>
                <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>{plan.targetAccount}</div>
              </div>
            </div>
          </div>

          <div className="plan-field">
            <span className="plan-label">Kapsam</span>
            <span style={{ fontSize: '0.875rem', color: 'var(--text-main)' }}>
              {plan.filters.year !== 'all' ? `${plan.filters.year} yılı · ` : 'Tüm yıllar · '}
              {plan.sourceFolderName}
            </span>
          </div>

          <div className="plan-field">
            <span className="plan-label">Seçili</span>
            <span style={{ fontSize: '0.875rem', fontWeight: 600, color: 'var(--text-main)' }} data-testid="summary-selected-count">
              {scopeCount} ileti · {formatBytes(totalScopeBytes)}
            </span>
          </div>

          <div className="plan-field">
            <span className="plan-label">Yinelenen</span>
            <span style={{ fontSize: '0.875rem', color: 'var(--text-main)' }} data-testid="summary-duplicate-count">
              {preflightState?.duplicateCount ?? 0} ileti
            </span>
          </div>

          <div className="plan-field">
            <span className="plan-label">Karar bekleyen</span>
            <span
              style={{
                fontSize: '0.875rem',
                fontWeight: 600,
                color: hasBlocker ? 'var(--brand-orange)' : 'var(--status-success)',
              }}
              data-testid="summary-pending-decision-count"
            >
              {hasBlocker ? '1 ileti' : '0 ileti'}
            </span>
          </div>

          <p style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '4px' }}>
            Nihai aktarım sayısı karar sonrası hesaplanır.
          </p>

          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginTop: '10px', fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
            <IconFile size={16} />
            <span>Kaynak iletiler korunur</span>
          </div>
        </div>
      </div>

      {/* Footer Controls */}
      <div
        style={{
          height: '64px',
          flexShrink: 0,
          borderTop: '1px solid var(--border-light)',
          background: '#ffffff',
          padding: '0 24px',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
        }}
      >
        <button
          className="btn btn-outline-gray"
          onClick={() => setCurrentView('workspace')}
          data-testid="back-to-plan-btn"
        >
          <IconArrowLeft size={16} />
          <span>Plana dön</span>
        </button>

        <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
          {hasBlocker && (
            <span style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }} data-testid="start-transfer-helper-text">
              Önce bekleyen kararı tamamlayın
            </span>
          )}

          <button
            className="btn btn-primary-orange"
            onClick={handleStartTransfer}
            disabled={hasBlocker || isPreflightStale || scopeCount === 0}
            data-testid="start-transfer-btn"
          >
            <IconPlay size={16} />
            <span>Aktarımı başlat</span>
          </button>
        </div>
      </div>

      {/* Message Inspection Modal */}
      {oversizedItem && (
        <Modal
          isOpen={detailModalOpen}
          onClose={() => setDetailModalOpen(false)}
          title={oversizedItem.subject}
          subtitle={`${oversizedItem.sender} <${oversizedItem.senderEmail}>`}
          footer={
            <button className="btn btn-outline-gray" onClick={() => setDetailModalOpen(false)}>
              Kapat
            </button>
          }
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
            <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
              Tarih: {oversizedItem.dateTime} | Toplam Boyut: {oversizedItem.sizeFormatted}
            </div>
            <div style={{ fontSize: '0.875rem', whiteSpace: 'pre-wrap', color: 'var(--text-main)' }}>
              {oversizedItem.body}
            </div>
            {oversizedItem.hasAttachment && (
              <div className="attachment-chip">
                <span>📎 {oversizedItem.attachmentName}</span>
                <span>({oversizedItem.attachmentSizeFormatted})</span>
              </div>
            )}
          </div>
        </Modal>
      )}
    </div>
  );
};
