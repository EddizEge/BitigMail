import React from 'react';
import { DuplicatePolicy, TransferPlan } from '../../types';
import { IconServer } from '../ui/Icons';
import { TARGET_OPTIONS } from '../../data/sampleSources';

interface TransferPlanPaneProps {
  plan: TransferPlan;
  onUpdatePlan: (updater: (prev: TransferPlan) => TransferPlan) => void;
  onOpenFolderMapping: () => void;
  onRunPreflight: () => void;
  scopeCount: number;
}

export const TransferPlanPane: React.FC<TransferPlanPaneProps> = ({
  plan,
  onUpdatePlan,
  onOpenFolderMapping,
  onRunPreflight,
  scopeCount,
}) => {
  const isZeroScope = scopeCount === 0;

  return (
    <aside className="workspace-panel plan-pane" data-testid="transfer-plan-pane">
      <div className="panel-title" style={{ fontSize: '1.125rem' }}>
        Aktarım planı
      </div>

      <div className="plan-options">
      {/* Kaynak */}
      <div className="plan-field">
        <span className="plan-label">Kaynak</span>
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginTop: '2px' }}>
          <IconServer size={18} color="var(--brand-orange)" />
          <span className="plan-value" data-testid="plan-source-display">
            {plan.sourceType}
          </span>
        </div>
      </div>

      {/* Hedef */}
      <div className="plan-field">
        <span className="plan-label">Hedef</span>
        <select
          className="select-input"
          value={plan.targetType}
          onChange={(e) => {
            const selectedTarget = TARGET_OPTIONS.find((t) => t.name === e.target.value) || TARGET_OPTIONS[0];
            onUpdatePlan((prev) => ({
              ...prev,
              targetType: selectedTarget.name,
              targetAccount: selectedTarget.accountOrTarget,
            }));
          }}
          data-testid="plan-target-select"
        >
          {TARGET_OPTIONS.map((t) => (
            <option key={t.id} value={t.name}>
              {t.name}
            </option>
          ))}
        </select>
        <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)', marginTop: '2px' }}>
          <div style={{ fontWeight: 500, color: 'var(--text-main)' }}>{plan.targetAccount}</div>
          <div>Hesap veya dosya seç</div>
        </div>
      </div>

      {/* Klasör Eşlemesi */}
      <div className="plan-field">
        <span className="plan-label">Klasör eşlemesi</span>
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginTop: '2px' }}>
          <select
            className="select-input"
            value={plan.sourceFolderName}
            disabled
            style={{ flex: 1, backgroundColor: 'var(--bg-subtle)' }}
          >
            <option value={plan.sourceFolderName}>{plan.sourceFolderName}</option>
          </select>
          <span style={{ color: '#64748b' }}>→</span>
          <select
            className="select-input"
            value={plan.targetFolderName}
            onChange={(e) => {
              const val = e.target.value;
              onUpdatePlan((prev) => ({ ...prev, targetFolderName: val }));
            }}
            style={{ flex: 1 }}
            data-testid="plan-target-folder-select"
          >
            <option value="Gelen kutusu">Gelen kutusu</option>
            <option value="Arşiv">Arşiv</option>
            <option value="Aktarılanlar">Aktarılanlar</option>
          </select>
        </div>
        <div style={{ marginTop: '4px' }}>
          <span className="brand-link" onClick={onOpenFolderMapping} data-testid="edit-mapping-link">
            Eşlemeyi düzenle
          </span>
        </div>
      </div>

      {/* Kapsam */}
      <div className="plan-field">
        <span className="plan-label">Kapsam</span>
        <span className="plan-value" style={{ fontWeight: 600 }} data-testid="plan-scope-value">
          {plan.manualSelectionMode
            ? `Seçilen ${plan.selectedMessageIds.length} ileti`
            : `Filtreye uyan ${scopeCount} ileti`}
        </span>
      </div>

      {/* Yinelenen iletiler */}
      <div className="plan-field">
        <span className="plan-label">Yinelenen iletiler</span>
        <select
          className="select-input"
          value={plan.duplicatePolicy}
          onChange={(e) => {
            const policy = e.target.value as DuplicatePolicy;
            onUpdatePlan((prev) => ({ ...prev, duplicatePolicy: policy }));
          }}
          data-testid="plan-duplicate-policy-select"
        >
          <option value="skip">Atla</option>
          <option value="overwrite">Üzerine yaz</option>
          <option value="separate_folder">Ayrı klasöre taşı</option>
        </select>
      </div>

      {/* Kaynak iletiler */}
      <div className="plan-field">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <span className="plan-label">Kaynak iletiler</span>
          <span style={{ fontSize: '0.875rem', fontWeight: 500, color: 'var(--text-main)' }}>
            Korunur
          </span>
        </div>
      </div>

      </div>

      {/* Primary Action Button */}
      <div className="plan-actions" style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
        <button
          className="btn btn-primary-orange"
          style={{ width: '100%', padding: '12px 16px', fontSize: '0.9375rem' }}
          onClick={onRunPreflight}
          disabled={isZeroScope}
          data-testid="run-preflight-btn"
        >
          Ön kontrolü çalıştır
        </button>

        {isZeroScope ? (
          <p
            style={{ fontSize: '0.75rem', color: '#b91c1c', textAlign: 'center', fontWeight: 500 }}
            data-testid="scope-guard-helper"
          >
            Seçili kapsamda ileti yok. Ön kontrol için ileti içeren bir kaynak seçin.
          </p>
        ) : (
          <p style={{ fontSize: '0.75rem', color: 'var(--text-muted)', textAlign: 'center' }}>
            Aktarım ön kontrolden sonra başlatılır.
          </p>
        )}
      </div>
    </aside>
  );
};
