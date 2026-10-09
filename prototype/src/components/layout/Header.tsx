import React from 'react';
import { NavigationTab } from '../../types';
import { BitigMark } from '../ui/BitigMark';
import { IconSettings } from '../ui/Icons';
import { useIdentity } from '../auth/IdentityGate';

// İş akışı sırası: müşteriyi/projeyi kur → işi başlat → izle → arşivde ara → raporu al.
const NAV_ITEMS: { tab: NavigationTab; label: string; hint: string }[] = [
  { tab: 'clients', label: 'Müşteriler', hint: 'Şirketler, projeler ve posta hesapları' },
  { tab: 'transfers', label: 'Aktarım ve dönüşüm', hint: 'Yeni aktarım, dönüşüm, bölme veya kurtarma işi başlatın' },
  { tab: 'jobs', label: 'İş merkezi', hint: 'Çalışan ve biten işleri izleyin' },
  { tab: 'search', label: 'Arşiv ve arama', hint: 'Arşivlerde birlikte arama yapın' },
  { tab: 'reports', label: 'Raporlar', hint: 'Tamamlanan işlerin raporlarını indirin' },
];

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

      <nav className="nav-tabs" role="navigation" aria-label="Ana menü">
        {NAV_ITEMS.map((item) => (
          <button
            key={item.tab}
            className={`nav-tab-btn ${currentTab === item.tab ? 'active' : ''}`}
            onClick={() => onTabChange(item.tab)}
            data-testid={`nav-tab-${item.tab}`}
            aria-current={currentTab === item.tab ? 'page' : undefined}
            title={item.hint}
          >
            {item.label}
          </button>
        ))}
      </nav>

      <div className="header-right">
        <button
          className="settings-btn"
          onClick={onOpenSettings}
          data-testid="header-settings-btn"
          aria-label="Ayarlar"
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
