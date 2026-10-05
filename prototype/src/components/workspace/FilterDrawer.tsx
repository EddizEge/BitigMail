import React, { useState, useEffect } from 'react';
import { FilterCriteria } from '../../types';
import { IconX } from '../ui/Icons';
import { Button } from '../ui/Button';

interface FilterDrawerProps {
  isOpen: boolean;
  onClose: () => void;
  filters: FilterCriteria;
  onApplyFilters: (updates: Partial<FilterCriteria>) => void;
}

export const FilterDrawer: React.FC<FilterDrawerProps> = ({
  isOpen,
  onClose,
  filters,
  onApplyFilters,
}) => {
  const [sender, setSender] = useState(filters.sender);
  const [minSizeMB, setMinSizeMB] = useState(
    filters.minSizeBytes ? (filters.minSizeBytes / (1024 * 1024)).toString() : ''
  );
  const [maxSizeMB, setMaxSizeMB] = useState(
    filters.maxSizeBytes ? (filters.maxSizeBytes / (1024 * 1024)).toString() : ''
  );

  useEffect(() => {
    if (isOpen) {
      setSender(filters.sender);
      setMinSizeMB(filters.minSizeBytes ? (filters.minSizeBytes / (1024 * 1024)).toString() : '');
      setMaxSizeMB(filters.maxSizeBytes ? (filters.maxSizeBytes / (1024 * 1024)).toString() : '');
    }
  }, [isOpen, filters]);

  if (!isOpen) return null;

  const handleApply = () => {
    const minBytes = minSizeMB ? parseFloat(minSizeMB) * 1024 * 1024 : null;
    const maxBytes = maxSizeMB ? parseFloat(maxSizeMB) * 1024 * 1024 : null;
    onApplyFilters({
      sender,
      minSizeBytes: minBytes,
      maxSizeBytes: maxBytes,
    });
    onClose();
  };

  const handleClear = () => {
    setSender('');
    setMinSizeMB('');
    setMaxSizeMB('');
    onApplyFilters({
      sender: '',
      minSizeBytes: null,
      maxSizeBytes: null,
    });
    onClose();
  };

  return (
    <div className="modal-backdrop" onClick={onClose} data-testid="filter-drawer-backdrop">
      <div
        className="drawer-dialog"
        onClick={(e) => e.stopPropagation()}
        data-testid="filter-drawer-dialog"
      >
        <div
          style={{
            padding: '16px 20px',
            borderBottom: '1px solid var(--border-light)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
          }}
        >
          <div>
            <h3 style={{ fontSize: '1.125rem', fontWeight: 700, color: 'var(--text-main)' }}>
              Gelişmiş Filtreler
            </h3>
            <p style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
              Gönderen ve boyut sınırlarına göre daraltın
            </p>
          </div>
          <button
            onClick={onClose}
            style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--text-muted)' }}
            aria-label="Kapat"
            data-testid="filter-drawer-close-btn"
          >
            <IconX size={18} />
          </button>
        </div>

        <div style={{ padding: '20px', flex: 1, display: 'flex', flexDirection: 'column', gap: '20px', overflowY: 'auto' }}>
          <div>
            <label className="plan-label">Gönderen / E-posta</label>
            <input
              type="text"
              className="text-input"
              placeholder="Örn: Deniz Akın veya @ornek.example"
              value={sender}
              onChange={(e) => setSender(e.target.value)}
              style={{ marginTop: '6px' }}
              data-testid="advanced-filter-sender-input"
            />
          </div>

          <div>
            <label className="plan-label">En Küçük İleti Boyutu (MB)</label>
            <input
              type="number"
              step="0.1"
              min="0"
              className="text-input"
              placeholder="Örn: 1.0"
              value={minSizeMB}
              onChange={(e) => setMinSizeMB(e.target.value)}
              style={{ marginTop: '6px' }}
              data-testid="advanced-filter-min-size-input"
            />
          </div>

          <div>
            <label className="plan-label">En Büyük İleti Boyutu (MB)</label>
            <input
              type="number"
              step="0.1"
              min="0"
              className="text-input"
              placeholder="Örn: 35.0"
              value={maxSizeMB}
              onChange={(e) => setMaxSizeMB(e.target.value)}
              style={{ marginTop: '6px' }}
              data-testid="advanced-filter-max-size-input"
            />
          </div>
        </div>

        <div
          style={{
            padding: '16px 20px',
            borderTop: '1px solid var(--border-light)',
            background: 'var(--bg-subtle)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
          }}
        >
          <Button variant="outline-gray" onClick={handleClear} data-testid="filter-drawer-clear-btn">
            Filtreleri Temizle
          </Button>
          <Button variant="primary" onClick={handleApply} data-testid="filter-drawer-apply-btn">
            Filtreleri Uygula
          </Button>
        </div>
      </div>
    </div>
  );
};
