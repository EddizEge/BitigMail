import React, { useState, useMemo } from 'react';
import { AppState } from '../../state/useAppState';
import { DataSource } from '../../types';
import {
  IconFolder,
  IconMail,
  IconFile,
  IconPlus,
  IconSearch,
  IconArrowRight,
} from '../ui/Icons';
import { Modal } from '../ui/Modal';
import { Badge } from '../ui/Badge';
import { getMessagesBySourceId } from '../../data/sampleMessages';
import { ProjectImapAccounts } from './ProjectImapAccounts';
import { useIdentity } from '../auth/IdentityGate';
import { PageHeader } from '../layout/PageHeader';
import { GettingStarted } from '../layout/GettingStarted';

interface ClientDirectoryViewProps {
  state: AppState;
}

export const ClientDirectoryView: React.FC<ClientDirectoryViewProps> = ({ state }) => {
  const identityState = useIdentity();
  const authenticated = Boolean(identityState?.identity);
  const isAdmin = identityState?.identity?.role === 'Admin';
  const openManagement = () => identityState?.setManagementOpen(true);
  const [searchQuery, setSearchQuery] = useState('');
  const [addCompanyModalOpen, setAddCompanyModalOpen] = useState(false);
  const [addProjectModalOpen, setAddProjectModalOpen] = useState(false);
  const [addSourceModalOpen, setAddSourceModalOpen] = useState(false);
  const [targetProjectIdForSource, setTargetProjectIdForSource] = useState<string | null>(null);

  // New company form state
  const [newCompanyName, setNewCompanyName] = useState('');
  const [newCompanyCode, setNewCompanyCode] = useState('');
  const [newCompanyDesc, setNewCompanyDesc] = useState('');

  // New project form state
  const [newProjectName, setNewProjectName] = useState('');
  const [newProjectDesc, setNewProjectDesc] = useState('');

  // New source form state
  const [newSourceName, setNewSourceName] = useState('');
  const [newSourceKind, setNewSourceKind] = useState<'mailbox' | 'archive_file'>('mailbox');
  const [newSourceType, setNewSourceType] = useState<'server' | 'file'>('server');
  const [newSourceAccount, setNewSourceAccount] = useState('');

  const selectedCompany = useMemo(() => {
    if (!state.selectedCompanyId) return null;
    return state.companies.find((c) => c.id === state.selectedCompanyId) || null;
  }, [state.companies, state.selectedCompanyId]);

  const companyProjects = useMemo(() => {
    if (!selectedCompany) return [];
    return state.projects.filter((p) => p.companyId === selectedCompany.id);
  }, [state.projects, selectedCompany]);

  const filteredCompanies = useMemo(() => {
    return state.companies.filter((c) => {
      if (!searchQuery.trim()) return true;
      const q = searchQuery.toLowerCase();
      return (
        c.name.toLowerCase().includes(q) ||
        c.code.toLowerCase().includes(q) ||
        (c.description && c.description.toLowerCase().includes(q))
      );
    });
  }, [state.companies, searchQuery]);

  const getSourceStats = (source: DataSource) => {
    const msgs = getMessagesBySourceId(source.id);
    return {
      messageCount: msgs.length,
      folderCount: source.folders.length,
    };
  };

  const handleCreateCompany = (e: React.FormEvent) => {
    e.preventDefault();
    if (authenticated) { setAddCompanyModalOpen(false); identityState?.setManagementOpen(true); return; }
    if (!newCompanyName.trim() || !newCompanyCode.trim()) return;
    const newId = state.addCompany(newCompanyName.trim(), newCompanyCode.trim(), newCompanyDesc.trim());
    state.setSelectedCompanyId(newId);
    setNewCompanyName('');
    setNewCompanyCode('');
    setNewCompanyDesc('');
    setAddCompanyModalOpen(false);
  };

  const handleCreateProject = (e: React.FormEvent) => {
    e.preventDefault();
    if (authenticated) { setAddProjectModalOpen(false); identityState?.setManagementOpen(true); return; }
    if (!selectedCompany || !newProjectName.trim()) return;
    state.addProject(selectedCompany.id, newProjectName.trim(), newProjectDesc.trim());
    setNewProjectName('');
    setNewProjectDesc('');
    setAddProjectModalOpen(false);
  };

  const handleCreateSource = (e: React.FormEvent) => {
    e.preventDefault();
    if (authenticated) { setAddSourceModalOpen(false); return; }
    if (!selectedCompany || !targetProjectIdForSource || !newSourceName.trim() || !newSourceAccount.trim()) return;
    state.addSource(
      selectedCompany.id,
      targetProjectIdForSource,
      newSourceName.trim(),
      newSourceKind,
      newSourceType,
      newSourceAccount.trim()
    );
    setNewSourceName('');
    setNewSourceAccount('');
    setAddSourceModalOpen(false);
    setTargetProjectIdForSource(null);
  };

  // Render Directory List View
  if (!selectedCompany) {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', flex: 1 }} data-testid="client-directory-view">
        <PageHeader
          title="Müşteriler"
          description="Müşteri şirketlerini, projelerini ve projelere bağlı posta hesaplarını buradan yönetin."
          testId="clients-page-header"
          actions={(!authenticated || isAdmin) ? (
            <button
              className="btn btn-orange"
              onClick={() => authenticated ? openManagement() : setAddCompanyModalOpen(true)}
              data-testid="add-company-btn"
            >
              <IconPlus size={16} />
              <span>{authenticated ? 'Müşteri veya proje ekle' : 'Yeni şirket ekle'}</span>
            </button>
          ) : undefined}
        />

        <div className="clients-body">
          {authenticated && state.projects.length === 0 && (
            <GettingStarted isAdmin={isAdmin} onCreateWorkspace={openManagement} />
          )}

          {/* Search bar */}
          {(!authenticated || state.companies.length > 0) && <div style={{ marginBottom: '20px', display: 'flex', gap: '12px' }}>
            <div className="search-input-wrapper" style={{ maxWidth: '400px', flex: 1 }}>
              <IconSearch size={16} className="search-input-icon" />
              <input
                type="text"
                className="text-input"
                placeholder={authenticated ? 'Müşteri adına göre ara' : 'Şirket adı veya koduna göre ara...'}
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                data-testid="client-search-input"
              />
            </div>
          </div>}

          {/* Companies Grid */}
          <div className="company-grid">
            {filteredCompanies.map((comp) => {
              const compProjects = state.projects.filter((p) => p.companyId === comp.id);
              const compSources = state.sources.filter((s) => s.companyId === comp.id);

              return (
                <div
                  key={comp.id}
                  className="card"
                  style={{
                    padding: '20px',
                    display: 'flex',
                    flexDirection: 'column',
                    justifyContent: 'space-between',
                    cursor: 'pointer',
                    transition: 'border-color 0.15s, box-shadow 0.15s',
                  }}
                  onClick={() => state.setSelectedCompanyId(comp.id)}
                  data-testid={`company-card-${comp.id}`}
                >
                  <div>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '8px', marginBottom: '10px' }}>
                      {comp.code ? <span className="company-code">{comp.code}</span> : <span />}
                      <span className="company-meta">
                        {authenticated ? `${compProjects.length} proje` : `${compProjects.length} Proje · ${compSources.length} Kaynak`}
                      </span>
                    </div>

                    <h3 style={{ fontSize: '1.125rem', fontWeight: 700, margin: '0 0 6px 0', color: 'var(--text-main)' }}>
                      {comp.name}
                    </h3>
                    <p style={{ fontSize: '0.875rem', color: 'var(--text-muted)', margin: 0, minHeight: '40px' }}>
                      {authenticated
                        ? (compProjects.map((project) => project.name).join(' · ') || 'Henüz proje yok')
                        : (comp.description || 'Kayıtlı açıklama bulunmuyor.')}
                    </p>
                  </div>

                  <div style={{ marginTop: '16px', paddingTop: '14px', borderTop: '1px solid var(--border-light)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <span style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
                      {authenticated ? 'Projeleri ve posta hesaplarını aç' : (comp.contactEmail || 'İletişim tanımlı')}
                    </span>
                    {authenticated && <IconArrowRight size={14} color="var(--brand-orange)" />}
                    {!authenticated && <button
                      className="btn btn-outline-gray"
                      style={{ padding: '6px 12px', fontSize: '0.8125rem' }}
                      onClick={(e) => {
                        e.stopPropagation();
                        state.setSelectedCompanyId(comp.id);
                      }}
                      data-testid={`view-company-btn-${comp.id}`}
                    >
                      <span>Ayrıntılar</span>
                      <IconArrowRight size={14} />
                    </button>}
                  </div>
                </div>
              );
            })}
          </div>
        </div>

        {/* Add Company Modal */}
        <Modal
          isOpen={addCompanyModalOpen}
          onClose={() => setAddCompanyModalOpen(false)}
          title="Yeni Müşteri Şirketi Ekle"
          size="sm"
        >
          <form onSubmit={handleCreateCompany} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
            <div>
              <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '6px' }}>
                Şirket Adı *
              </label>
              <input
                type="text"
                className="text-input"
                placeholder="Örn: Kuzey Yazılım A.Ş."
                value={newCompanyName}
                onChange={(e) => setNewCompanyName(e.target.value)}
                required
                data-testid="new-company-name-input"
              />
            </div>

            <div>
              <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '6px' }}>
                Kısa Kod *
              </label>
              <input
                type="text"
                className="text-input"
                placeholder="Örn: KUZEY"
                value={newCompanyCode}
                onChange={(e) => setNewCompanyCode(e.target.value)}
                maxLength={10}
                required
                data-testid="new-company-code-input"
              />
            </div>

            <div>
              <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '6px' }}>
                Açıklama
              </label>
              <textarea
                className="text-input"
                rows={2}
                placeholder="Müşteri faaliyeti ve geçiş kapsamı..."
                value={newCompanyDesc}
                onChange={(e) => setNewCompanyDesc(e.target.value)}
                data-testid="new-company-desc-input"
              />
            </div>

            <div style={{ background: 'var(--bg-subtle)', padding: '10px 14px', borderRadius: '6px', fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
              ℹ️ Yeni müşteri eklendiğinde projeler ve örnek veri kaynakları bu şirket altında tanımlanabilir.
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '6px' }}>
              <button
                type="button"
                className="btn btn-outline-gray"
                onClick={() => setAddCompanyModalOpen(false)}
              >
                İptal
              </button>
              <button
                type="submit"
                className="btn btn-orange"
                disabled={!newCompanyName.trim() || !newCompanyCode.trim()}
                data-testid="submit-new-company-btn"
              >
                Kaydet
              </button>
            </div>
          </form>
        </Modal>
      </div>
    );
  }

  // Render Company Detail View
  return (
    <div style={{ display: 'flex', flexDirection: 'column', flex: 1 }} data-testid="client-detail-view">
      {/* Subheader */}
      <PageHeader
        title={selectedCompany.name}
        description={authenticated
          ? 'Bu müşterinin projeleri ve projelere bağlı posta hesapları. Hesap eklemek için proje kartındaki düğmeyi kullanın.'
          : (selectedCompany.description || 'Şirket ayrıntıları ve bağlı veri kaynakları')}
        testId="client-detail-header"
        breadcrumb={<>
          <button
            className="btn-outline-gray"
            style={{ padding: '3px 8px', fontSize: '0.8125rem', cursor: 'pointer', borderRadius: '4px' }}
            onClick={() => state.setSelectedCompanyId(null)}
            data-testid="back-to-directory-btn"
          >
            ← Müşteriler
          </button>
          <span style={{ color: 'var(--text-light)' }}>/</span>
          <span>{selectedCompany.name}</span>
        </>}
        actions={(!authenticated || isAdmin) ? (
          <button
            className="btn btn-outline-orange"
            onClick={() => authenticated ? openManagement() : setAddProjectModalOpen(true)}
            data-testid="add-project-btn"
          >
            <IconPlus size={16} />
            <span>{authenticated ? 'Proje ekle' : 'Yeni proje ekle'}</span>
          </button>
        ) : undefined}
      />

      <div style={{ padding: '20px 28px', maxWidth: '1280px', width: '100%', margin: '0 auto', overflowY: 'auto' }}>
        {/* Projects Section */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
          {companyProjects.length === 0 ? (
            <div className="card" style={{ padding: '40px', textAlign: 'center' }}>
              <IconFolder size={36} color="#94a3b8" />
              <h3 style={{ margin: '12px 0 6px 0', fontSize: '1.125rem' }}>Henüz proje tanımlanmadı</h3>
              <p style={{ color: 'var(--text-muted)', fontSize: '0.875rem', marginBottom: '16px' }}>
                Bu şirkete ait posta kutularını ve dosya arşivlerini gruplamak için bir proje oluşturun.
              </p>
              {(!authenticated || isAdmin) && <button
                className="btn btn-orange"
                onClick={() => authenticated ? openManagement() : setAddProjectModalOpen(true)}
              >
                Proje oluştur
              </button>}
            </div>
          ) : (
            companyProjects.map((project) => {
              const projectSources = state.sources.filter((s) => s.projectId === project.id);

              return (
                <div key={project.id} className="card" style={{ padding: '20px' }} data-testid={`project-card-${project.id}`}>
                  {/* Project Header */}
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '16px' }}>
                    <div>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                        <h2 style={{ fontSize: '1.125rem', fontWeight: 700, margin: 0, color: 'var(--text-main)' }}>
                          {project.name}
                        </h2>
                        {!authenticated && <Badge variant="neutral">{projectSources.length} Kaynak</Badge>}
                      </div>
                      {!authenticated && <p style={{ fontSize: '0.875rem', color: 'var(--text-muted)', margin: '4px 0 0 0' }}>
                        {project.description || 'Tanımlı proje açıklaması'}
                      </p>}
                    </div>

                    {!authenticated && <button
                      className="btn btn-outline-gray"
                      style={{ padding: '6px 12px', fontSize: '0.8125rem' }}
                      onClick={() => {
                        setTargetProjectIdForSource(project.id);
                        setAddSourceModalOpen(true);
                      }}
                      data-testid={`add-source-btn-${project.id}`}
                    >
                      <IconPlus size={14} />
                      <span>Kaynak bağla</span>
                    </button>}
                  </div>

                  {/* Connected Sources Table */}
                  {!authenticated && (projectSources.length === 0 ? (
                    <div style={{ padding: '20px', background: 'var(--bg-subtle)', borderRadius: '6px', textAlign: 'center', fontSize: '0.875rem', color: 'var(--text-muted)' }}>
                      Bu projeye henüz posta kutusu veya dosya arşivi bağlanmadı.
                    </div>
                  ) : (
                    <div className="table-container" style={{ borderRadius: '6px', border: '1px solid var(--border-light)' }}>
                      <table className="data-table" data-testid={`sources-table-${project.id}`}>
                        <thead>
                          <tr>
                            <th style={{ width: '28%' }}>Kaynak Adı & Hesap</th>
                            <th style={{ width: '18%' }}>Tür</th>
                            <th style={{ width: '18%' }}>Ortam</th>
                            <th style={{ width: '16%' }}>Kapsam</th>
                            <th style={{ width: '20%', textAlign: 'right' }}>İşlem</th>
                          </tr>
                        </thead>
                        <tbody>
                          {projectSources.map((source) => {
                            const stats = getSourceStats(source);

                            return (
                              <tr key={source.id} data-testid={`source-row-${source.id}`}>
                                <td>
                                  <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                    {source.kind === 'mailbox' ? (
                                      <IconMail size={16} color="var(--brand-orange)" />
                                    ) : (
                                      <IconFile size={16} color="#d97706" />
                                    )}
                                    <div>
                                      <div style={{ fontWeight: 600, color: 'var(--text-main)' }}>{source.name}</div>
                                      <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>{source.accountOrFileName}</div>
                                    </div>
                                  </div>
                                </td>
                                <td>
                                  {source.kind === 'mailbox' ? (
                                    <span style={{ fontSize: '0.8125rem', color: '#1d4ed8', fontWeight: 600, background: '#eff6ff', padding: '3px 8px', borderRadius: '4px' }}>
                                      Posta kutusu
                                    </span>
                                  ) : (
                                    <span style={{ fontSize: '0.8125rem', color: '#b45309', fontWeight: 600, background: '#fffbeb', padding: '3px 8px', borderRadius: '4px' }}>
                                      Dosya arşivi
                                    </span>
                                  )}
                                </td>
                                <td style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
                                  {source.type === 'server' ? 'Sunucu erişimi' : 'Yerel dosya'}
                                </td>
                                <td>
                                  <span style={{ fontSize: '0.8125rem', fontWeight: 500 }}>
                                    {stats.messageCount > 0 ? (
                                      `${stats.messageCount} ileti · ${stats.folderCount} klasör`
                                    ) : (
                                      <span style={{ color: 'var(--text-muted)' }}>0 ileti (Boş kaynak)</span>
                                    )}
                                  </span>
                                </td>
                                <td style={{ textAlign: 'right' }}>
                                  <button
                                    className="btn btn-outline-orange"
                                    style={{ padding: '5px 12px', fontSize: '0.8125rem' }}
                                    onClick={() => state.startTransferWithContext(selectedCompany.id, project.id, source.id)}
                                    data-testid={`start-transfer-source-${source.id}`}
                                  >
                                    <span>İşlem başlat</span>
                                    <IconArrowRight size={14} />
                                  </button>
                                </td>
                              </tr>
                            );
                          })}
                        </tbody>
                      </table>
                    </div>
                  ))}

                  {/* Real IMAP Accounts Section (TASK-014) */}
                  <ProjectImapAccounts
                    companyId={selectedCompany.id}
                    projectId={project.id}
                    projectName={project.name}
                  />
                </div>
              );
            })
          )}
        </div>
      </div>

      {/* Add Project Modal */}
      <Modal
        isOpen={addProjectModalOpen}
        onClose={() => setAddProjectModalOpen(false)}
        title={`${selectedCompany.name} için Proje Ekle`}
        size="sm"
      >
        <form onSubmit={handleCreateProject} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
          <div>
            <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '6px' }}>
              Proje Adı *
            </label>
            <input
              type="text"
              className="text-input"
              placeholder="Örn: 2024 Bulut Konsolidasyonu"
              value={newProjectName}
              onChange={(e) => setNewProjectName(e.target.value)}
              required
              data-testid="new-project-name-input"
            />
          </div>

          <div>
            <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '6px' }}>
              Proje Açıklaması
            </label>
            <textarea
              className="text-input"
              rows={2}
              placeholder="Geçiş hedefleri ve kapsamı..."
              value={newProjectDesc}
              onChange={(e) => setNewProjectDesc(e.target.value)}
              data-testid="new-project-desc-input"
            />
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '6px' }}>
            <button
              type="button"
              className="btn btn-outline-gray"
              onClick={() => setAddProjectModalOpen(false)}
            >
              İptal
            </button>
            <button
              type="submit"
              className="btn btn-orange"
              disabled={!newProjectName.trim()}
              data-testid="submit-new-project-btn"
            >
              Oluştur
            </button>
          </div>
        </form>
      </Modal>

      {/* Add Source Modal */}
      <Modal
        isOpen={addSourceModalOpen}
        onClose={() => {
          setAddSourceModalOpen(false);
          setTargetProjectIdForSource(null);
        }}
        title="Yeni Kaynak Bağla (Örnek Bağlantı)"
        size="sm"
      >
        <form onSubmit={handleCreateSource} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
          <div>
            <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '6px' }}>
              Kaynak Türü
            </label>
            <div style={{ display: 'flex', gap: '10px' }}>
              <button
                type="button"
                className={`btn ${newSourceKind === 'mailbox' ? 'btn-orange' : 'btn-outline-gray'}`}
                style={{ flex: 1 }}
                onClick={() => {
                  setNewSourceKind('mailbox');
                  setNewSourceType('server');
                }}
                data-testid="source-kind-mailbox-btn"
              >
                <IconMail size={16} />
                <span>Posta Kutusu</span>
              </button>
              <button
                type="button"
                className={`btn ${newSourceKind === 'archive_file' ? 'btn-orange' : 'btn-outline-gray'}`}
                style={{ flex: 1 }}
                onClick={() => {
                  setNewSourceKind('archive_file');
                  setNewSourceType('file');
                }}
                data-testid="source-kind-archive-btn"
              >
                <IconFile size={16} />
                <span>Dosya Arşivi</span>
              </button>
            </div>
          </div>

          <div>
            <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '6px' }}>
              Görünen Kaynak Adı *
            </label>
            <input
              type="text"
              className="text-input"
              placeholder={newSourceKind === 'mailbox' ? 'Örn: Finans Posta Kutusu' : 'Örn: 2023_Eski_Arsiv.pst'}
              value={newSourceName}
              onChange={(e) => setNewSourceName(e.target.value)}
              required
              data-testid="new-source-name-input"
            />
          </div>

          <div>
            <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '6px' }}>
              {newSourceKind === 'mailbox' ? 'E-posta Hesabı *' : 'Dosya Adı / Konumu *'}
            </label>
            <input
              type="text"
              className="text-input"
              placeholder={newSourceKind === 'mailbox' ? 'finans@sirket.example' : 'arsiv_yedek.pst'}
              value={newSourceAccount}
              onChange={(e) => setNewSourceAccount(e.target.value)}
              required
              data-testid="new-source-account-input"
            />
          </div>

          <div style={{ background: 'var(--bg-subtle)', padding: '10px 14px', borderRadius: '6px', fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
            ⚠️ <strong>Sözleşme Notu:</strong> Gerçek parola, sunucu kimlik bilgisi veya dosya içeriği istenmez. Eklenen yeni kaynak örnek bağlantı olarak 0 iletiyle başlatılır; sahte iletilerle doldurulmaz.
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '6px' }}>
            <button
              type="button"
              className="btn btn-outline-gray"
              onClick={() => {
                setAddSourceModalOpen(false);
                setTargetProjectIdForSource(null);
              }}
            >
              İptal
            </button>
            <button
              type="submit"
              className="btn btn-orange"
              disabled={!newSourceName.trim() || !newSourceAccount.trim()}
              data-testid="submit-new-source-btn"
            >
              Kaydet
            </button>
          </div>
        </form>
      </Modal>
    </div>
  );
};
