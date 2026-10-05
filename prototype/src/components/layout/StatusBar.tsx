import React from 'react';
import { IconFolder } from '../ui/Icons';

interface StatusBarProps {
  statusText?: string;
  projectName?: string;
}

export const StatusBar: React.FC<StatusBarProps> = ({
  statusText = '(Örnek simülasyon) 248 ileti filtreye uyuyor',
  projectName = 'Örnek proje',
}) => {
  const hasStatusText = Boolean(statusText && statusText.trim().length > 0);
  return (
    <footer className="status-bar" role="contentinfo" data-testid="status-bar">
      <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
        <IconFolder size={14} color="#64748b" />
        <span>{projectName}</span>
        {hasStatusText && (
          <>
            <span style={{ color: '#cbd5e1' }}>|</span>
            <span data-testid="status-bar-count">{statusText}</span>
          </>
        )}
      </div>
      <div>
        <span data-testid="status-bar-version">v0.9.3 · Yerel çalışma alanı</span>
      </div>
    </footer>
  );
};
