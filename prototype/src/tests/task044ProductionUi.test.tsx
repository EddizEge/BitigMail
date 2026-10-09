// @vitest-environment jsdom
import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup } from '@testing-library/react';
import { IdentityContext, IdentityContextValue } from '../components/auth/IdentityGate';
import { TransfersTabView } from '../components/transfer/TransfersTabView';
import { JobCenter } from '../components/jobs/JobCenter';
import { ReportsView } from '../components/reports/ReportsView';
import { ClientDirectoryView } from '../components/clients/ClientDirectoryView';
import { StatusBar } from '../components/layout/StatusBar';
import { Header } from '../components/layout/Header';
import { LocalEngineClient, localEngineClient } from '../api/localEngineClient';
import { Job } from '../types';

afterEach(() => cleanup());

const identityValue = (overrides: Partial<IdentityContextValue['identity']> = {}, setManagementOpen = vi.fn()): IdentityContextValue => ({
  identity: { userId: 'u-1', role: 'Admin', companies: [], ...overrides } as IdentityContextValue['identity'],
  developmentMode: false,
  setManagementOpen,
  logout: vi.fn(),
});

const withIdentity = (node: React.ReactNode, value: IdentityContextValue) => (
  <IdentityContext.Provider value={value}>{node}</IdentityContext.Provider>
);

const transferState = (overrides: Record<string, unknown> = {}): any => ({
  plan: { companyId: 'comp-1', projectId: 'proj-1', operationType: 'migration' },
  companies: [],
  projects: [],
  sources: [],
  currentView: 'workspace',
  selectedLocalJobId: null,
  setSelectedLocalJobId: vi.fn(),
  updatePlan: vi.fn(),
  setCurrentView: vi.fn(),
  setCurrentTab: vi.fn(),
  ...overrides,
});

const quietClient = () => {
  const client = new LocalEngineClient('http://127.0.0.1:6174');
  client.listAccounts = vi.fn().mockResolvedValue([]);
  client.getJob = vi.fn().mockRejectedValue(new Error('not used'));
  return client;
};

describe('TASK-044 tek seçim yeri: Aktarım ve dönüşüm', () => {
  it('oturum açılmış ama proje yokken boş seçim kutuları yerine Müşteriler yönlendirmesi gösterir', () => {
    const state = transferState();
    render(withIdentity(<TransfersTabView state={state} client={quietClient()} />, identityValue()));

    expect(screen.getByTestId('transfers-empty-state')).toBeDefined();
    expect(screen.queryByTestId('transfer-company-select')).toBeNull();
    expect(screen.queryByTestId('op-tab-migration')).toBeNull();
    fireEvent.click(screen.getByTestId('transfers-goto-clients-btn'));
    expect(state.setCurrentTab).toHaveBeenCalledWith('clients');
  });

  it('gerçek uygulamada örnek mod, yol haritası kartı ve iç içe yön seçicisi göstermez', () => {
    const value = identityValue({ companies: [{ companyId: 'comp-1', name: 'Acme', projects: [{ projectId: 'proj-1', name: 'Geçiş' }] }] });
    render(withIdentity(<TransfersTabView state={transferState()} client={quietClient()} />, value));

    expect(screen.getByTestId('transfers-page-header-description').textContent).toContain('önce işlemi');
    expect(screen.queryByTestId('transfer-mode-switcher')).toBeNull();
    expect(screen.queryByTestId('mode-sample')).toBeNull();
    expect(screen.queryByText(/Sözleşme & Yol Haritası/)).toBeNull();
    // Yön yalnız bir yerde seçilir; gömülü akışın kendi yön seçicisi yok.
    expect(screen.getByTestId('transfer-direction-selector')).toBeDefined();
    expect(screen.queryByTestId('bridge-direction-selector')).toBeNull();
    expect(screen.queryByTestId('direction-imap-to-imap-btn')).toBeNull();

    fireEvent.click(screen.getByTestId('tab-direction-imap-to-imap'));
    expect(screen.getByTestId('imap-transfer-workflow')).toBeDefined();
    expect(screen.queryByTestId('imap-to-bridge-switcher')).toBeNull();
    expect(screen.queryByText(/projesine kayıtlı IMAP hesapları arasında/)).toBeNull();
  });

  it('dosya dönüşümünde dört türü tek seçicide sunar', () => {
    const value = identityValue({ companies: [{ companyId: 'comp-1', name: 'Acme', projects: [{ projectId: 'proj-1', name: 'Geçiş' }] }] });
    const state = transferState({ plan: { companyId: 'comp-1', projectId: 'proj-1', operationType: 'convert' } });
    const getJob = vi.spyOn(localEngineClient, 'getJob').mockRejectedValue(new Error('not used'));
    try {
      render(withIdentity(<TransfersTabView state={state} client={quietClient()} />, value));
      const selector = screen.getByTestId('convert-kind-selector');
      for (const id of ['convert-input-ost', 'convert-input-mime', 'convert-input-outlook-eml', 'convert-input-emlx']) {
        expect(selector.querySelector(`[data-testid="${id}"]`)).not.toBeNull();
      }
      expect(screen.queryByTestId('transfer-direction-selector')).toBeNull();
    } finally {
      getJob.mockRestore();
    }
  });
});

describe('TASK-044 İş merkezi gerçek uygulamada örnek iş göstermez', () => {
  const demoJob = { id: 'job-1', title: 'Örnek geçiş', client: 'Örnek', source: 'a', target: 'b', status: 'running', type: 'migration', recentEvents: [] } as unknown as Job;

  it('örnek işleri gizler, boş durum ve "Yeni iş" yönlendirmesi sunar', async () => {
    const client = quietClient();
    client.getAllJobs = vi.fn().mockResolvedValue([]);
    const onStartNewJob = vi.fn();
    render(<JobCenter jobs={[demoJob]} onToggleJobPause={vi.fn()} onNavigateToWorkspace={vi.fn()} onAddNewJob={vi.fn()} client={client} productionMode onStartNewJob={onStartNewJob} />);

    await waitFor(() => expect(screen.getByTestId('jobs-empty-state')).toBeDefined());
    expect(screen.queryByTestId('job-row-job-1')).toBeNull();

    fireEvent.click(screen.getByTestId('new-job-dropdown-btn'));
    fireEvent.click(screen.getByTestId('new-job-convert'));
    expect(onStartNewJob).toHaveBeenCalledWith('convert');
    // Taslak penceresi açılmaz.
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('geliştirme görünümünde örnek işleri göstermeye devam eder', async () => {
    const client = quietClient();
    client.getAllJobs = vi.fn().mockResolvedValue([]);
    render(<JobCenter jobs={[demoJob]} onToggleJobPause={vi.fn()} onNavigateToWorkspace={vi.fn()} onAddNewJob={vi.fn()} client={client} />);
    expect(screen.getByTestId('job-row-job-1')).toBeDefined();
  });
});

describe('TASK-044 Raporlar', () => {
  it('gerçek uygulamada sentetik rapor satırı yok; boş durum ve doğru iş türü adı', async () => {
    const getAllJobs = vi.spyOn(localEngineClient, 'getAllJobs').mockResolvedValue([]);
    try {
      render(<ReportsView productionMode />);
      await waitFor(() => expect(screen.getByTestId('reports-empty-state')).toBeDefined());
      expect(screen.queryByTestId('report-row-rep-001')).toBeNull();
      expect(document.body.textContent).not.toContain('Sentetik');
      expect(document.body.textContent).not.toContain('sentetik');
    } finally {
      getAllJobs.mockRestore();
    }
  });

  it('tamamlanan IMAP işini dönüştürme raporu diye adlandırmaz', async () => {
    const getAllJobs = vi.spyOn(localEngineClient, 'getAllJobs').mockResolvedValue([{
      jobId: 'job-imap-1', jobKind: 'imap-transfer', status: 'completed', stage: 'Tamamlandı', percentComplete: 100,
      itemsRead: 3, itemsWritten: 3, failedItems: 0, totalItems: 3, sourceFileName: 'a@example.test', targetFileName: 'b@example.test',
      createdAt: '2026-10-09T10:00:00Z', clientContext: { companyId: 'c', companyName: 'Acme', projectId: 'p', projectName: 'Geçiş' },
    } as any]);
    try {
      render(<ReportsView productionMode />);
      const row = await screen.findByTestId('report-row-job-imap-1');
      expect(row.textContent).toContain('Hesaptan hesaba aktarım raporu');
      expect(row.textContent).not.toContain('OST Dönüştürme');
    } finally {
      getAllJobs.mockRestore();
    }
  });
});

describe('TASK-044 Müşteriler ilk kullanım rehberi', () => {
  const clientState = (overrides: Record<string, unknown> = {}): any => ({
    companies: [], projects: [], sources: [], selectedCompanyId: null, setSelectedCompanyId: vi.fn(),
    addCompany: vi.fn(), addProject: vi.fn(), addSource: vi.fn(), startTransferWithContext: vi.fn(), ...overrides,
  });

  it('proje yokken Başlarken kartını gösterir ve yöneticiyi oluşturma paneline götürür', () => {
    const setManagementOpen = vi.fn();
    render(withIdentity(<ClientDirectoryView state={clientState()} />, identityValue({}, setManagementOpen)));
    const card = screen.getByTestId('getting-started-card');
    expect(card.textContent).toContain('Şirket ve proje oluşturun');
    expect(card.textContent).toContain('Projeye posta hesabı ekleyin');
    expect(card.textContent).toContain('Raporlar');
    fireEvent.click(screen.getByTestId('getting-started-create-btn'));
    expect(setManagementOpen).toHaveBeenCalledWith(true);
  });

  it('operatöre oluşturma düğmesi yerine yöneticiden izin isteme yönlendirmesi verir', () => {
    render(withIdentity(<ClientDirectoryView state={clientState()} />, identityValue({ role: 'Operator' })));
    expect(screen.queryByTestId('getting-started-create-btn')).toBeNull();
    expect(screen.getByTestId('getting-started-card').textContent).toContain('Yöneticinizden');
    expect(screen.queryByTestId('add-company-btn')).toBeNull();
  });

  it('proje varken rehberi göstermez; kartta boş kod ve örnek kaynak sayısı yok', () => {
    const state = clientState({
      companies: [{ id: 'comp-1', name: 'Acme', code: '', description: 'Yetkili çalışma alanı', projectIds: ['proj-1'] }],
      projects: [{ id: 'proj-1', companyId: 'comp-1', name: 'Geçiş', description: 'Yetkili proje', sourceIds: [] }],
    });
    render(withIdentity(<ClientDirectoryView state={state} />, identityValue()));
    expect(screen.queryByTestId('getting-started-card')).toBeNull();
    const card = screen.getByTestId('company-card-comp-1');
    expect(card.textContent).toContain('1 proje');
    expect(card.textContent).toContain('Geçiş');
    expect(card.textContent).not.toContain('Kaynak');
  });
});

describe('TASK-044 başlık ve durum çubuğu', () => {
  it('durum çubuğu derlenen sürümü gösterir, sabit 0.9.3 ve örnek metin yok', () => {
    render(<StatusBar />);
    const version = screen.getByTestId('status-bar-version').textContent || '';
    expect(version).toContain(`v${__BITIGMAIL_VERSION__}`);
    expect(version).not.toContain('0.9.3');
    expect(screen.getByTestId('status-bar').textContent).not.toContain('Örnek');
  });

  it('üst menü iş akışı sırasındadır', () => {
    render(<Header currentTab="clients" onTabChange={vi.fn()} onOpenSettings={vi.fn()} />);
    const tabs = screen.getAllByRole('button').filter((button) => button.dataset.testid?.startsWith('nav-tab-'));
    expect(tabs.map((tab) => tab.textContent)).toEqual(['Müşteriler', 'Aktarım ve dönüşüm', 'İş merkezi', 'Arşiv ve arama', 'Raporlar']);
  });
});
