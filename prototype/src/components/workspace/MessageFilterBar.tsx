import React from 'react';
import { FilterCriteria } from '../../types';
import { IconFilter, IconSearch } from '../ui/Icons';
import { formatBytes } from '../../data/sampleMessages';

interface MessageFilterBarProps {
  filters: FilterCriteria;
  onUpdateFilters: (updates: Partial<FilterCriteria>) => void;
  onOpenAdvancedFilters: () => void;
  filteredCount: number;
  totalFilteredBytes: number;
  manualSelectionMode: boolean;
  selectedCount: number;
  onToggleManualSelection: () => void;
  onSelectAll: () => void;
  onDeselectAll: () => void;
}

export const MessageFilterBar: React.FC<MessageFilterBarProps> = ({
  filters,
  onUpdateFilters,
  onOpenAdvancedFilters,
  filteredCount,
  totalFilteredBytes,
  manualSelectionMode,
  selectedCount,
  onToggleManualSelection,
  onSelectAll,
  onDeselectAll,
}) => {
  return (
    <div style={{ background: '#ffffff', borderBottom: '1px solid var(--border-light)' }}>
      {/* Controls row */}
      <div className="filter-bar">
        {/* Search input */}
        <div className="search-input-wrapper">
          <IconSearch size={15} className="search-input-icon" />
          <input
            type="text"
            className="text-input"
            placeholder="iletilerde ara"
            value={filters.searchTerm}
            onChange={(e) => onUpdateFilters({ searchTerm: e.target.value })}
            data-testid="message-search-input"
          />
        </div>

        {/* Year filter */}
        <select
          className="select-input"
          value={filters.year}
          onChange={(e) => onUpdateFilters({ year: e.target.value as any })}
          data-testid="filter-year-select"
          aria-label="Tarih yılı seçimi"
        >
          <option value="2024">Tarih: 2024</option>
          <option value="2023">Tarih: 2023</option>
          <option value="2022">Tarih: 2022</option>
          <option value="all">Tarih: Tümü</option>
        </select>

        {/* Attachment filter */}
        <select
          className="select-input"
          value={filters.attachment}
          onChange={(e) => onUpdateFilters({ attachment: e.target.value as any })}
          data-testid="filter-attachment-select"
          aria-label="Ek durumu seçimi"
        >
          <option value="all">Ek: Tümü</option>
          <option value="with">Ek: Yalnız Ekli</option>
          <option value="without">Ek: Eksiz</option>
        </select>

        {/* Advanced Filters Button */}
        <button
          className="btn btn-outline-gray"
          style={{ padding: '6px 12px', fontSize: '0.8125rem' }}
          onClick={onOpenAdvancedFilters}
          data-testid="open-advanced-filters-btn"
        >
          <IconFilter size={15} color="#64748b" />
          <span>Filtreler</span>
          {(filters.sender || filters.minSizeBytes || filters.maxSizeBytes) && (
            <span
              style={{
                width: '6px',
                height: '6px',
                borderRadius: '50%',
                backgroundColor: 'var(--brand-orange)',
                display: 'inline-block',
              }}
            />
          )}
        </button>

        {/* Manual Selection Mode Toggle */}
        <label
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '6px',
            fontSize: '0.8125rem',
            color: 'var(--text-muted)',
            cursor: 'pointer',
            marginLeft: 'auto',
            userSelect: 'none',
          }}
          data-testid="manual-selection-toggle-label"
        >
          <input
            type="checkbox"
            checked={manualSelectionMode}
            onChange={onToggleManualSelection}
            style={{ cursor: 'pointer' }}
            data-testid="manual-selection-checkbox"
          />
          <span>Tek tek seçim modu</span>
        </label>
      </div>

      {/* Subtitle / summary info */}
      <div
        style={{
          padding: '8px 16px',
          fontSize: '0.8125rem',
          color: 'var(--text-muted)',
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          background: 'var(--bg-subtle)',
          borderBottom: '1px solid var(--border-light)',
        }}
      >
        <span data-testid="scope-summary-text">
          Filtreye uyan {filteredCount} ileti · {formatBytes(totalFilteredBytes)}
        </span>

        {manualSelectionMode && (
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <span style={{ fontWeight: 600, color: 'var(--brand-orange)' }} data-testid="selected-count-badge">
              {selectedCount} / {filteredCount} seçildi
            </span>
            <button
              style={{ background: 'none', border: 'none', color: 'var(--brand-orange)', fontSize: '0.75rem', cursor: 'pointer', fontWeight: 600 }}
              onClick={onSelectAll}
              data-testid="select-all-btn"
            >
              Tümünü Seç
            </button>
            <span style={{ color: '#cbd5e1' }}>|</span>
            <button
              style={{ background: 'none', border: 'none', color: 'var(--text-muted)', fontSize: '0.75rem', cursor: 'pointer' }}
              onClick={onDeselectAll}
              data-testid="deselect-all-btn"
            >
              Temizle
            </button>
          </div>
        )}
      </div>
    </div>
  );
};
