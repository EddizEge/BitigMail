import React from 'react';
import { IconFolder } from '../ui/Icons';

/** Sürüm, derleme sırasında yayın betiğinin verdiği BITIGMAIL_VERSION değerinden gelir (vite.config.ts). */
export const APP_VERSION = `v${__BITIGMAIL_VERSION__}`;

interface StatusBarProps {
  statusText?: string;
  projectName?: string;
}

export const StatusBar: React.FC<StatusBarProps> = ({
  statusText = '',
  projectName = '',
}) => {
  const hasStatusText = Boolean(statusText && statusText.trim().length > 0);
  return (
    <footer className="status-bar" role="contentinfo" data-testid="status-bar">
      <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
        {projectName && <IconFolder size={14} color="var(--text-muted)" />}
        {projectName && <span>{projectName}</span>}
        {hasStatusText && (
          <>
            {projectName && <span style={{ color: 'var(--border-mid)' }}>|</span>}
            <span data-testid="status-bar-count">{statusText}</span>
          </>
        )}
      </div>
      <div>
        <span data-testid="status-bar-version">BitigMail {APP_VERSION} · Veriler bu bilgisayarda</span>
      </div>
    </footer>
  );
};
