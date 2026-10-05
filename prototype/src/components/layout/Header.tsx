import React from 'react';
import { NavigationTab } from '../../types';
import { BitigMark } from '../ui/BitigMark';
import { IconSettings } from '../ui/Icons';
import { useIdentity } from '../auth/IdentityGate';

interface HeaderProps {
  currentTab: NavigationTab;
  onTabChange: (tab: NavigationTab) => void;
  onOpenSettings: () => void;
}

export const Header: React.FC<HeaderProps> = ({
  currentTab,
  onTabChange,
  onOpenSettings,
}) => {
  const identityState = useIdentity();
  const identity = identityState?.identity;
  return (
    <header className="top-header" role="banner" data-testid="top-header">
      <div className="brand-section" onClick={() => onTabChange('clients')} data-testid="brand-logo-button">
        <BitigMark size={32} />
        <span className="brand-name">BitigMail</span>
      </div>

      <nav className="nav-tabs" role="navigation" aria-label="Ana Navigasyon">
        <button
          className={`nav-tab-btn ${currentTab === 'jobs' ? 'active' : ''}`}
          onClick={() => onTabChange('jobs')}
          data-testid="nav-tab-jobs"
          aria-current={currentTab === 'jobs' ? 'page' : undefined}
        >
          İş merkezi
        </button>
        <button
          className={`nav-tab-btn ${currentTab === 'clients' ? 'active' : ''}`}
          onClick={() => onTabChange('clients')}
          data-testid="nav-tab-clients"
          aria-current={currentTab === 'clients' ? 'page' : undefined}
        >
          Müşteriler
        </button>
        <button
          className={`nav-tab-btn ${currentTab === 'transfers' ? 'active' : ''}`}
          onClick={() => onTabChange('transfers')}
          data-testid="nav-tab-transfers"
          aria-current={currentTab === 'transfers' ? 'page' : undefined}
        >
          Aktarım ve dönüşüm
        </button>
        <button
          className={`nav-tab-btn ${currentTab === 'search' ? 'active' : ''}`}
          onClick={() => onTabChange('search')}
          data-testid="nav-tab-search"
          aria-current={currentTab === 'search' ? 'page' : undefined}
        >
          Arşiv ve arama
        </button>
        <button
          className={`nav-tab-btn ${currentTab === 'reports' ? 'active' : ''}`}
          onClick={() => onTabChange('reports')}
          data-testid="nav-tab-reports"
          aria-current={currentTab === 'reports' ? 'page' : undefined}
        >
          Raporlar
        </button>
      </nav>

      <div className="header-right">
        <button
          className="settings-btn"
          onClick={onOpenSettings}
          data-testid="header-settings-btn"
          aria-label="Sistem Ayarları ve Demo Tercihleri"
        >
          <IconSettings size={17} />
          <span>Ayarlar</span>
        </button>
        {identity && <button className="account-control" data-testid="header-account-control" onClick={() => identityState?.setManagementOpen(true)} aria-label="Hesap ve çalışma alanı yönetimi">
          <span className="account-avatar">{identity.role === 'Admin' ? 'Y' : 'O'}</span>
          <span className="account-copy"><strong>{identity.role === 'Admin' ? 'Yerel yönetici' : 'Yerel operatör'}</strong><small>Hesap ve erişim</small></span>
          <span aria-hidden="true">⌄</span>
        </button>}
      </div>
    </header>
  );
};
