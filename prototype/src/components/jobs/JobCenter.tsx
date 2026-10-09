import React, { useState, useMemo, useEffect, useRef, useDeferredValue } from 'react';
import { Job, JobType } from '../../types';
import { localEngineClient, LocalEngineClient } from '../../api/localEngineClient';
import {
  IconArchiveBox,
  IconChevronDown,
  IconClock,
  IconDownload,
  IconFile,
  IconLaptop,
  IconMail,
  IconPause,
  IconPlay,
  IconRefreshCw,
  IconSearch,
  IconUsers,
  IconWrench,
} from '../ui/Icons';
import { Badge } from '../ui/Badge';
import { NewJobModal } from './NewJobModal';
import { PageHeader } from '../layout/PageHeader';

interface JobCenterProps {
  jobs: Job[];
  onToggleJobPause: (jobId: string) => void;
  onNavigateToWorkspace: (job?: Job) => void;
  onAddNewJob: (newJob: Job) => void;
  client?: LocalEngineClient;
  onAddToArchive?: (job: Job) => void;
  /** Oturum açılmış gerçek uygulama: örnek işler ve taslak pencereleri gösterilmez. */
  productionMode?: boolean;
  /** "Yeni iş" menüsü gerçek uygulamada ilgili Aktarım ve dönüşüm işlemini açar. */
  onStartNewJob?: (type: JobType) => void;
}

export const JobCenter: React.FC<JobCenterProps> = ({
  jobs,
  onToggleJobPause,
  onNavigateToWorkspace,
  onAddNewJob,
  client = localEngineClient,
  onAddToArchive,
  productionMode = false,
  onStartNewJob,
}) => {
  const [searchQuery, setSearchQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | 'running' | 'queued' | 'attention' | 'completed'>('all');
  const [selectedJobId, setSelectedJobId] = useState<string>('job-1');
  const [menuOpen, setMenuOpen] = useState(false);
  const [draftModalType, setDraftModalType] = useState<JobType | null>(null);
  const [realLocalJobs, setRealLocalJobs] = useState<Job[]>([]);
  const [localRefresh, setLocalRefresh] = useState(0);
  const initialSelectionSet = useRef(false);
  const realJobsRef = useRef<Job[]>([]);
  const [imapActionError, setImapActionError] = useState<{ jobId: string; message: string } | null>(null);
  const [resumingJobId, setResumingJobId] = useState<string | null>(null);
  const [historyPage, setHistoryPage] = useState(1);
  const [historyTotal, setHistoryTotal] = useState(0);
  const deferredSearch = useDeferredValue(searchQuery);

  useEffect(() => {
    let isMounted = true;
    let pollTimer: ReturnType<typeof setTimeout> | undefined;
    // Existing callers/tests may inject only getAllJobs on an otherwise concrete client.
    const request = !Object.prototype.hasOwnProperty.call(client, 'getAllJobs') && typeof client.getJobsPage === 'function'
      ? client.getJobsPage({ page: historyPage, pageSize: 50, status: statusFilter, search: deferredSearch })
      : client.getAllJobs().then(items => ({ items, page: 1, pageSize: 50, totalCount: items.length }));
    request.then((pageResult) => {
      if (!isMounted) return;
      const records = pageResult.items;
      const mapped: Job[] = records.map((r) => {
        const isSplit = r.jobKind === 'split';
        const isImap = r.jobKind === 'imap-transfer';
        const isBridgeImport = r.jobKind === 'bridge-import';
        const isBridgeExport = r.jobKind === 'bridge-export';
        const isBridge = isBridgeImport || isBridgeExport;
        const isArchiveIngest = r.jobKind === 'archive-ingest';
        const isArchiveReindex = r.jobKind === 'archive-reindex';
        const isArchive = isArchiveIngest || isArchiveReindex;
        const isRecovery = r.jobKind === 'damaged-recovery';
        return {
          id: r.jobId,
          title: isSplit
            ? `Yerel PST Bölümleme (${r.sourceFileName})`
            : r.jobKind === 'mime-import' ? `EML / MBOX → PST (${r.sourceFileName})`
            : isImap ? `IMAP Aktarımı (${r.sourceFileName} → ${r.targetFileName})`
            : isBridgeImport ? `Dosya → IMAP Aktarımı (${r.sourceFileName} → ${r.targetFileName})`
            : isBridgeExport ? `IMAP → Dosya Aktarımı (${r.sourceFileName} → ${r.targetFileName})`
            : isArchiveIngest ? `Arşiv Alma (${r.archiveName || r.sourceFileName})`
            : isArchiveReindex ? `Arşiv Yeniden İndeksleme (${r.archiveName || r.sourceFileName})`
            : isRecovery ? `Hasarlı PST/OST Kurtarma (${r.sourceFileName})`
            : r.jobKind === 'pop-snapshot' ? 'POP → EML Kaynak Anlık Görüntüsü'
            : `Yerel OST Dönüştürme (${r.sourceFileName})`,
          client: r.clientContext.companyName || 'Yerel Müşteri',
          type: (isRecovery ? 'recovery' : isSplit ? 'archive' : isArchive ? 'archive' : (isImap || isBridge) ? 'migration' : 'convert') as JobType,
          jobKind: r.jobKind,
          archiveId: r.archiveId || undefined,
          archiveName: r.archiveName || undefined,
          source: isArchiveIngest ? (r.sourceFileName || 'MIME Kaynağı') : isArchiveReindex ? (r.archiveName || 'Arşiv') : r.sourceFileName,
          target: isRecovery ? 'Yeni doğrulanmış EML klasörü' : isSplit ? (r.outputDirectoryPath || 'Çoklu PST Klasörü') : isArchiveIngest ? (r.archiveName || r.targetFileName || 'Yönetilen Arşiv') : isArchiveReindex ? 'Arama İndeksi' : r.targetFileName,
          status: r.status === 'completed'
            ? 'completed'
            : (r.status === 'converting' || r.status === 'verifying')
            ? 'running'
            : (r.status === 'interrupted' || r.status === 'failed')
            ? 'needs_attention'
            : 'queued',
          progressPercent: r.status === 'completed' ? 100 : Math.min(99, r.percentComplete),
          engineStatus: r.status,
          engineError: r.errorMessage,
          progressPhase: r.progressPhase ?? (r.status === 'verifying' ? 'verifying' : null),
          phaseCompleted: r.phaseCompleted,
          phaseTotal: r.phaseTotal,
          processedCount: r.itemsWritten,
          totalCount: r.totalItems || r.itemsRead,
          transferredCount: r.itemsWritten,
          skippedCount: 0,
          failedCount: r.failedItems,
          pendingCount: Math.max(0, (r.totalItems || r.itemsRead) - r.itemsWritten),
          recentEvents: [
            { time: new Date(r.createdAt).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' }), text: `Aşama: ${r.stage}` },
            ...(r.errorMessage ? [{ time: '—', text: r.errorMessage }] : [])
          ],
          companyId: r.clientContext.companyId,
          projectId: r.clientContext.projectId,
          isLocalEngine: true,
          reportAvailable: isRecovery ? Boolean(r.recoveryReportSha256) : r.status === 'completed',
          waitingAtShutdown: r.waitingAtShutdown,
          neverStartedQueued: r.neverStartedQueued
          ,waitingPriority: r.waitingPriority ?? 0
        };
      });
      realJobsRef.current = mapped;
      setRealLocalJobs(mapped);
      setHistoryTotal(pageResult.totalCount);
      if (mapped.length > 0 && !initialSelectionSet.current) {
        initialSelectionSet.current = true;
        setSelectedJobId(mapped[0].id);
      }
      if (mapped.some(job => job.status === 'running' || job.status === 'queued')) {
        pollTimer = setTimeout(() => setLocalRefresh(value => value + 1), 1200);
      }
    }).catch(() => {
      // offline fallback
      if (isMounted && realJobsRef.current.some(job => job.status === 'running' || job.status === 'queued')) {
        pollTimer = setTimeout(() => setLocalRefresh(value => value + 1), 2400);
      }
    });
    return () => { isMounted = false; if (pollTimer) clearTimeout(pollTimer); };
  }, [localRefresh, client, historyPage, statusFilter, deferredSearch]);

  // Filtered jobs
  const filteredJobs = useMemo(() => {
    const filteredDemoJobs = jobs.filter((job) => {
      // Search
      const matchesSearch =
        job.title.toLowerCase().includes(searchQuery.toLowerCase()) ||
        job.client.toLowerCase().includes(searchQuery.toLowerCase()) ||
        job.source.toLowerCase().includes(searchQuery.toLowerCase()) ||
        job.target.toLowerCase().includes(searchQuery.toLowerCase());

      if (!matchesSearch) return false;

      // Status
      if (statusFilter === 'running') return job.status === 'running';
      if (statusFilter === 'queued') return job.status === 'queued';
      if (statusFilter === 'attention') return job.status === 'needs_attention';
      if (statusFilter === 'completed') return job.status === 'completed';
      return true;
    });
    return productionMode ? realLocalJobs : [...realLocalJobs, ...filteredDemoJobs];
  }, [realLocalJobs, jobs, searchQuery, statusFilter, productionMode]);

  const startNewJob = (type: JobType) => {
    setMenuOpen(false);
    if (onStartNewJob) {
      onStartNewJob(type);
      return;
    }
    if (type === 'migration') onNavigateToWorkspace();
    else setDraftModalType(type);
  };

  const selectedJob = useMemo(() => {
    return filteredJobs.find((j) => j.id === selectedJobId) || filteredJobs[0];
  }, [filteredJobs, selectedJobId]);

  const renderJobIcon = (type: JobType, id: string) => {
    if (id === 'job-3') return <IconUsers size={20} color="#475569" />;
    if (id === 'job-5') return <IconWrench size={20} color="#475569" />;
    if (id === 'job-6') return <IconLaptop size={20} color="#475569" />;
    if (type === 'migration') return <IconMail size={20} color="#475569" />;
    if (type === 'archive') return <IconFile size={20} color="#475569" />;
    return <IconFile size={20} color="#475569" />;
  };

  const renderStatusBadge = (job: Job) => {
    switch (job.status) {
      case 'running':
        return (
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', color: 'var(--brand-orange)', fontWeight: 600 }}>
            <span
              style={{
                width: '12px',
                height: '12px',
                borderRadius: '50%',
                border: '2.5px solid var(--brand-orange)',
                borderTopColor: 'transparent',
                display: 'inline-block',
                animation: 'spin 1s linear infinite',
              }}
            />
            <span>{job.progressPhase === 'verifying' ? 'Son doğrulama yapılıyor' : `Çalışıyor · %${job.progressPercent}`}</span>
          </span>
        );
      case 'paused':
        return <Badge variant="warning">Duraklatıldı · %{job.progressPercent}</Badge>;
      case 'queued':
        return (
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', color: 'var(--text-muted)' }}>
            <IconClock size={16} />
            <span>Sırada</span>
          </span>
        );
      case 'needs_attention':
        return (
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', color: 'var(--status-error)', fontWeight: 600 }}>
            <span style={{ color: 'var(--status-error)' }}>▲</span>
            <span>Müdahale bekliyor</span>
          </span>
        );
      case 'draft':
        return (
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', color: 'var(--text-muted)' }}>
            <IconFile size={16} />
            <span>Taslak</span>
          </span>
        );
      case 'partially_completed':
        return (
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', color: 'var(--text-main)' }}>
            <span style={{ border: '2px dotted #64748b', borderRadius: '50%', width: '14px', height: '14px' }} />
            <span>Kısmen tamamlandı</span>
          </span>
        );
      case 'completed':
        return (
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', color: 'var(--status-success)', fontWeight: 600 }}>
            <span style={{ color: 'var(--status-success)', fontSize: '1rem' }}>✓</span>
            <span>Tamamlandı</span>
          </span>
        );
    }
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', flex: 1 }} data-testid="job-center-view">
      <PageHeader
        title="İş merkezi"
        description={productionMode || realLocalJobs.length > 0
          ? 'Başlatılan aktarım, dönüşüm, bölme ve kurtarma işlerini buradan izleyin; kesilen işleri devam ettirin.'
          : 'Örnek veriler · Tasarım taslağı'}
        testId="jobs-page-header"
        actions={
        <div style={{ position: 'relative' }}>
          <button
            className="btn btn-primary-orange"
            onClick={() => setMenuOpen(!menuOpen)}
            data-testid="new-job-dropdown-btn"
            style={{ padding: '8px 16px' }}
          >
            <span>Yeni iş</span>
            <IconChevronDown size={16} />
          </button>

          {menuOpen && (
            <div
              style={{
                position: 'absolute',
                right: 0,
                top: '100%',
                marginTop: '6px',
                width: '210px',
                background: '#ffffff',
                border: '1px solid var(--border-light)',
                borderRadius: '8px',
                boxShadow: '0 10px 15px -3px rgba(0,0,0,0.1)',
                zIndex: 50,
                overflow: 'hidden',
              }}
              data-testid="new-job-menu"
            >
              <button
                style={{
                  width: '100%',
                  padding: '10px 14px',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '10px',
                  border: 'none',
                  background: 'none',
                  textAlign: 'left',
                  cursor: 'pointer',
                  fontSize: '0.875rem',
                  color: 'var(--text-main)',
                }}
                onClick={() => startNewJob('migration')}
                data-testid="new-job-mail-migration"
              >
                <IconMail size={16} color="var(--brand-orange)" />
                <span>Posta aktar</span>
              </button>

              <button
                style={{
                  width: '100%',
                  padding: '10px 14px',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '10px',
                  border: 'none',
                  background: 'none',
                  textAlign: 'left',
                  cursor: 'pointer',
                  fontSize: '0.875rem',
                  color: 'var(--text-main)',
                  borderTop: '1px solid var(--border-light)',
                }}
                onClick={() => startNewJob('convert')}
                data-testid="new-job-convert"
              >
                <IconFile size={16} color="#64748b" />
                <span>Dosya dönüştür</span>
              </button>

              <button
                style={{
                  width: '100%',
                  padding: '10px 14px',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '10px',
                  border: 'none',
                  background: 'none',
                  textAlign: 'left',
                  cursor: 'pointer',
                  fontSize: '0.875rem',
                  color: 'var(--text-main)',
                  borderTop: '1px solid var(--border-light)',
                }}
                onClick={() => startNewJob('archive')}
                data-testid="new-job-archive"
              >
                <IconArchiveBox size={16} color="#64748b" />
                <span>PST böl</span>
              </button>

              <button
                style={{
                  width: '100%',
                  padding: '10px 14px',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '10px',
                  border: 'none',
                  background: 'none',
                  textAlign: 'left',
                  cursor: 'pointer',
                  fontSize: '0.875rem',
                  color: 'var(--text-main)',
                  borderTop: '1px solid var(--border-light)',
                }}
                onClick={() => startNewJob('recovery')}
                data-testid="new-job-recovery"
              >
                <IconRefreshCw size={16} color="#64748b" />
                <span>Veri kurtar</span>
              </button>
            </div>
          )}
        </div>
        }
      />

      {/* Main 2-column Job Center Layout */}
      <div className="job-center-grid">
        {/* Left column: Filter & Job Table */}
        <div className="job-center-left">
          {/* Filter Bar */}
          <div className="job-filter-bar">
            <div className="search-input-wrapper" style={{ maxWidth: '320px' }}>
              <IconSearch size={16} className="search-input-icon" />
              <input
                type="text"
                className="text-input"
                placeholder="İş veya müşteri ara"
                value={searchQuery}
                onChange={(e) => { setSearchQuery(e.target.value); setHistoryPage(1); }}
                data-testid="job-search-input"
              />
            </div>

            {/* Status Pills */}
            <div className="status-pills-container">
              <button
                className={`status-pill-btn ${statusFilter === 'all' ? 'active' : ''}`}
                onClick={() => { setStatusFilter('all'); setHistoryPage(1); }}
                data-testid="status-tab-all"
              >
                Tümü
              </button>
              <button
                className={`status-pill-btn ${statusFilter === 'running' ? 'active' : ''}`}
                onClick={() => { setStatusFilter('running'); setHistoryPage(1); }}
                data-testid="status-tab-running"
              >
                Çalışan
              </button>
              <button
                className={`status-pill-btn ${statusFilter === 'queued' ? 'active' : ''}`}
                onClick={() => { setStatusFilter('queued'); setHistoryPage(1); }}
                data-testid="status-tab-queued"
              >
                Sıradakiler
              </button>
              <button
                className={`status-pill-btn ${statusFilter === 'attention' ? 'active' : ''}`}
                onClick={() => { setStatusFilter('attention'); setHistoryPage(1); }}
                data-testid="status-tab-attention"
              >
                Müdahale bekleyen
              </button>
              <button
                className={`status-pill-btn ${statusFilter === 'completed' ? 'active' : ''}`}
                onClick={() => { setStatusFilter('completed'); setHistoryPage(1); }}
                data-testid="status-tab-completed"
              >
                Tamamlanan
              </button>
            </div>
          </div>

          {productionMode && filteredJobs.length === 0 && (
            <div className="empty-state-card" data-testid="jobs-empty-state">
              <h2>{statusFilter === 'all' && !searchQuery ? 'Henüz iş yok' : 'Bu filtreye uyan iş yok'}</h2>
              <div className="empty-state-copy">
                {statusFilter === 'all' && !searchQuery
                  ? 'Aktarım ve dönüşüm ekranından bir iş başlattığınızda burada ilerlemesini görürsünüz.'
                  : 'Arama metnini veya durum filtresini değiştirin.'}
              </div>
              {statusFilter === 'all' && !searchQuery && (
                <div className="empty-state-action">
                  <button className="btn btn-primary-orange" onClick={() => startNewJob('migration')} data-testid="jobs-empty-start-btn">Yeni iş başlat</button>
                </div>
              )}
            </div>
          )}
          {/* Jobs List Table */}
          <div className="table-container" style={productionMode && filteredJobs.length === 0 ? { display: 'none' } : undefined}>
            <table className="data-table" data-testid="jobs-table">
              <thead>
                <tr>
                  <th style={{ width: '40%' }}>Müşteri / İş</th>
                  <th style={{ width: '35%' }}>Kaynak → Hedef</th>
                  <th style={{ width: '25%' }}>Durum</th>
                </tr>
              </thead>
              <tbody>
                {filteredJobs.map((job) => {
                  const isSelected = job.id === selectedJob?.id;
                  return (
                    <tr
                      key={job.id}
                      className={isSelected ? 'selected' : ''}
                      onClick={() => setSelectedJobId(job.id)}
                      data-testid={`job-row-${job.id}`}
                    >
                      <td>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                          <div>{renderJobIcon(job.type, job.id)}</div>
                          <div>
                            <div style={{ fontWeight: 600, color: 'var(--text-main)', display: 'flex', alignItems: 'center', gap: '6px' }}>
                              <span>{job.title}</span>
                              {job.isLocalEngine && !productionMode && (
                                <span style={{ fontSize: '0.6875rem', padding: '1px 6px', borderRadius: '4px', background: '#ecfdf5', color: '#065f46', fontWeight: 600 }}>
                                  Yerel Motor
                                </span>
                              )}
                            </div>
                            <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
                              {job.client}
                            </div>
                          </div>
                        </div>
                      </td>
                      <td style={{ color: 'var(--text-main)', fontSize: '0.875rem' }}>
                        {job.source} → {job.target}
                      </td>
                      <td>{renderStatusBadge(job)}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
          {historyTotal > 50 ? (
            <nav aria-label="İş geçmişi sayfaları" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '10px 4px' }}>
              <button className="btn btn-secondary" disabled={historyPage === 1} onClick={() => setHistoryPage(page => Math.max(1, page - 1))}>Önceki</button>
              <span aria-live="polite">Sayfa {historyPage} / {Math.ceil(historyTotal / 50)} · {historyTotal} iş</span>
              <button className="btn btn-secondary" disabled={historyPage * 50 >= historyTotal} onClick={() => setHistoryPage(page => page + 1)}>Sonraki</button>
            </nav>
          ) : null}
        </div>

        {/* Right column: Selected Job Details */}
        {selectedJob && (
          <div className="job-center-right" data-testid="job-details-pane">
            <div>
              <h2 style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--text-main)' }}>
                {selectedJob.title}
              </h2>
              <div style={{ fontSize: '0.875rem', color: 'var(--text-muted)', marginTop: '2px' }}>
                {selectedJob.client}
              </div>
              <div style={{ marginTop: '10px' }}>{renderStatusBadge(selectedJob)}</div>
            </div>

            {/* Aktarım ilerlemesi */}
            <div style={{ borderTop: '1px solid var(--border-light)', paddingTop: '16px' }}>
              <h3 className="plan-label" style={{ marginBottom: '10px' }}>
                {selectedJob.engineStatus === 'verifying' ? 'Son doğrulama ilerlemesi' : 'Aktarım ilerlemesi'}
              </h3>

              <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '14px' }}>
                <div className="progress-track" style={{ flex: 1 }}>
                  <div
                    className="progress-fill"
                    style={{ width: `${selectedJob.engineStatus === 'verifying' ? (selectedJob.phaseTotal && selectedJob.phaseCompleted != null ? Math.min(100, selectedJob.phaseCompleted / selectedJob.phaseTotal * 100) : 0) : selectedJob.progressPercent}%` }}
                    data-testid="job-progress-bar"
                  />
                </div>
                <span style={{ fontSize: '0.875rem', fontWeight: 600, color: 'var(--text-main)' }}>
                  {selectedJob.engineStatus === 'verifying' ? (selectedJob.phaseTotal && selectedJob.phaseCompleted != null ? '%' + Math.floor(selectedJob.phaseCompleted / selectedJob.phaseTotal * 100) : 'Doğrulanıyor') : '%' + selectedJob.progressPercent}
                </span>
              </div>

              <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', fontSize: '0.875rem' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                  <span style={{ color: 'var(--text-muted)' }}>İşlenen</span>
                  <span style={{ fontWeight: 600 }} data-testid="job-processed-count">
                    {selectedJob.progressPhase === 'verifying' && selectedJob.engineStatus === 'verifying'
                      ? `Son doğrulama: ${selectedJob.phaseCompleted ?? '—'} / ${selectedJob.phaseTotal ?? '—'} ileti`
                      : `${selectedJob.processedCount} / ${selectedJob.totalCount} ileti`}
                  </span>
                </div>
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                  <span style={{ color: 'var(--text-muted)' }}>Aktarılan</span>
                  <span style={{ fontWeight: 600 }} data-testid="job-transferred-count">
                    {selectedJob.transferredCount}
                  </span>
                </div>
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                  <span style={{ color: 'var(--text-muted)' }}>Atlanan</span>
                  <span style={{ fontWeight: 600 }} data-testid="job-skipped-count">
                    {selectedJob.skippedCount}
                  </span>
                </div>
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                  <span style={{ color: 'var(--text-muted)' }}>Başarısız</span>
                  <span style={{ fontWeight: 600, color: selectedJob.failedCount > 0 ? 'var(--status-error)' : 'inherit' }} data-testid="job-failed-count">
                    {selectedJob.failedCount}
                  </span>
                </div>
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                  <span style={{ color: 'var(--text-muted)' }}>Bekleyen</span>
                  <span style={{ fontWeight: 600 }} data-testid="job-pending-count">
                    {selectedJob.pendingCount}
                  </span>
                </div>
              </div>

              <p style={{ marginTop: '12px' }}>
                {selectedJob.engineStatus === 'completed' ? 'İş tamamlandı. Sonuçları rapordan inceleyebilirsiniz.' : selectedJob.engineStatus === 'verifying' ? 'İletiler işlendi. İş tamamlanmadan önce içerik ve kayıtlar son kez kontrol ediliyor.' : 'Aktarılan, atlanan ve sorunlu öğeler sonuç raporunda ayrı gösterilir.'}
              </p>
              <details><summary>Teknik doğrulama ayrıntıları</summary>
              <p style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '12px' }} data-testid="job-verification-note">
                {selectedJob.jobKind === 'bridge-import'
                  ? 'Dosya → IMAP aktarımı: Hedef sunucuda RFC822 SHA-256 ve BitigMail keyword ile doğrulanır (PST başarı alanları uygulanmaz).'
                  : selectedJob.jobKind === 'bridge-export'
                  ? 'IMAP → Dosya aktarımı: Kaynak sunucudan indirilip yerel EML/MBOX ile doğrulanır (PST başarı alanları uygulanmaz).'
                  : selectedJob.jobKind === 'archive-ingest'
                  ? 'Yerel Arşiv Alma: Fiziksel MIME kayıtları SHA-256 ve manifest ile kopyalanır, arama indeksi FTS5 üzerine yazılır.'
                  : selectedJob.jobKind === 'archive-reindex'
                  ? 'Arşiv Yeniden İndeksleme: Doğrulanmış manifest ve EML kopyalarından SQLite FTS5 indeksi yeniden kurulur.'
                  : selectedJob.jobKind === 'damaged-recovery'
                  ? 'Hasarlı PST/OST kurtarma: Kaynak salt okunur kalır; yalnızca geçerli çağrıya bağlı manifest ve SHA-256 doğrulanmış EML çıktıları yayımlanır. Kısmi sonuçlar tam başarı sayılmaz.'
                  : selectedJob.type === 'migration'
                  ? 'IMAP iletileri hedef sunucuda SHA-256 ve BitigMail keyword ile doğrulanır (PST alanları uygulanmaz).'
                  : 'Doğrulama aktarım sonrası yapılacak.'}
              </p></details>

              {selectedJob.isLocalEngine && selectedJob.engineStatus === 'interrupted' && (
                <p role="status">İş kesintiye uğradı. Kaydedilen müşteri ve proje bilgileri korunuyor.
                  {selectedJob.neverStartedQueued && ' Bu iş kuyrukta hiç başlamadı; yeniden planlayın veya yeni bir iş başlatın.'}
                  {!['imap-transfer', 'bridge-import', 'bridge-export', 'archive-ingest', 'pop-snapshot'].includes(selectedJob.jobKind || '') && ' Bu iş türü kaldığı yerden devam etmiyor; kaynak ve mevcut çıktıları inceleyerek yeni bir iş oluşturun.'}
                  {selectedJob.engineError?.includes('reauthorization_required') && ' Önce ilgili hesabın bağlantısını yeniden kurun.'}
                </p>
              )}
              {selectedJob.isLocalEngine && selectedJob.engineStatus === 'failed' && <p role="status">İş tamamlanamadı. Ayrıntıları inceleyip nedeni giderdikten sonra yeni bir plan oluşturun.</p>}
              {/* Action Buttons */}
              {imapActionError?.jobId === selectedJob.id && <p role="alert" style={{ color: 'var(--status-error)' }}>{imapActionError.message}</p>}
              <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginTop: '16px' }}>
                {selectedJob.isLocalEngine && selectedJob.engineStatus === 'queued' && <label style={{fontSize:'0.8125rem'}}>Bekleme önceliği <select data-testid="job-waiting-priority" value={selectedJob.waitingPriority ?? 0} onChange={async e=>{setImapActionError(null);try{await client.setWaitingPriority(selectedJob.id,Number(e.target.value),selectedJob.companyId||'',selectedJob.projectId||'');setLocalRefresh(v=>v+1);}catch(error){setImapActionError({jobId:selectedJob.id,message:error instanceof Error?error.message:'Öncelik değiştirilemedi.'});}}}><option value="-10">Düşük</option><option value="0">Normal</option><option value="10">Yüksek</option></select></label>}
                {selectedJob.isLocalEngine && ['imap-transfer', 'bridge-import', 'bridge-export'].includes(selectedJob.jobKind || '') && selectedJob.engineStatus === 'interrupted' && !selectedJob.neverStartedQueued && !selectedJob.engineError?.includes('reauthorization_required') && (
                  <button
                    className="btn btn-orange"
                    style={{ padding: '6px 12px', fontSize: '0.8125rem' }}
                    onClick={async () => {
                      setResumingJobId(selectedJob.id);
                      setImapActionError(null);
                      try {
                        if (selectedJob.jobKind === 'bridge-import') {
                          await client.resumeBridgeImport(selectedJob.id, selectedJob.companyId || '', selectedJob.projectId || '', true);
                        } else if (selectedJob.jobKind === 'bridge-export') {
                          await client.resumeBridgeExport(selectedJob.id, selectedJob.companyId || '', selectedJob.projectId || '', true);
                        } else {
                          await client.resumeImapTransfer(selectedJob.id, selectedJob.companyId || '', selectedJob.projectId || '', true);
                        }
                        setLocalRefresh(value => value + 1);
                      } catch (error) {
                        setImapActionError({ jobId: selectedJob.id, message: error instanceof Error ? error.message : 'Aktarıma devam edilemedi.' });
                      } finally {
                        setResumingJobId(null);
                      }
                    }}
                    disabled={resumingJobId === selectedJob.id}
                    data-testid={selectedJob.jobKind?.startsWith('bridge-') ? 'job-resume-bridge-btn' : 'job-resume-imap-btn'}
                  >
                    <IconPlay size={14} />
                    <span>Kesintiden Devam Et</span>
                  </button>
                )}

                {selectedJob.isLocalEngine && selectedJob.jobKind === 'archive-ingest' && selectedJob.engineStatus === 'interrupted' && !selectedJob.neverStartedQueued && (
                  <button
                    className="btn btn-orange"
                    style={{ padding: '6px 12px', fontSize: '0.8125rem' }}
                    onClick={async () => {
                      setResumingJobId(selectedJob.id);
                      setImapActionError(null);
                      try {
                        await client.resumeArchiveIngest(selectedJob.id, true);
                        setLocalRefresh(value => value + 1);
                      } catch (error) {
                        setImapActionError({ jobId: selectedJob.id, message: error instanceof Error ? error.message : 'Arşivlemeye devam edilemedi.' });
                      } finally {
                        setResumingJobId(null);
                      }
                    }}
                    disabled={resumingJobId === selectedJob.id}
                    data-testid="job-resume-archive-btn"
                  >
                    <IconPlay size={14} />
                    <span>Kesintiden Devam Et</span>
                  </button>
                )}

                {selectedJob.isLocalEngine && selectedJob.jobKind === 'pop-snapshot' && ['failed','interrupted'].includes(selectedJob.engineStatus || '') && !selectedJob.neverStartedQueued && (
                  <button className="btn btn-orange" style={{padding:'6px 12px',fontSize:'0.8125rem'}} disabled={resumingJobId===selectedJob.id} data-testid="job-resume-pop-btn" onClick={async()=>{setResumingJobId(selectedJob.id);setImapActionError(null);try{await client.resumePop(selectedJob.id,selectedJob.companyId||'',selectedJob.projectId||'',true);setLocalRefresh(v=>v+1)}catch(error){setImapActionError({jobId:selectedJob.id,message:error instanceof Error?error.message:'POP işine devam edilemedi.'})}finally{setResumingJobId(null)}}}><IconPlay size={14}/><span>POP İndirmesine Devam Et</span></button>
                )}

                {selectedJob.isLocalEngine && selectedJob.engineStatus === 'queued' && selectedJob.waitingAtShutdown && (
                  <button
                    className="btn btn-outline-gray"
                    onClick={async () => {
                      setImapActionError(null);
                      try {
                        await client.cancelPendingJob(selectedJob.id, selectedJob.companyId || '', selectedJob.projectId || '');
                        setLocalRefresh(value => value + 1);
                      } catch (error) {
                        setImapActionError({ jobId: selectedJob.id, message: error instanceof Error ? error.message : 'Bekleyen iş iptal edilemedi.' });
                      }
                    }}
                    data-testid="job-cancel-pending-btn"
                  >
                    <span>Sıradan kaldır</span>
                  </button>
                )}

                {!selectedJob.isLocalEngine && (
                  <button
                    className="btn btn-outline-orange"
                    style={{ flex: 1 }}
                    onClick={() => onToggleJobPause(selectedJob.id)}
                    data-testid="job-pause-btn"
                  >
                    {selectedJob.status === 'running' ? (
                      <>
                        <IconPause size={16} />
                        <span>Duraklat</span>
                      </>
                    ) : (
                      <>
                        <IconPlay size={16} />
                        <span>Devam et</span>
                      </>
                    )}
                  </button>
                )}

                <button
                  className="btn btn-outline-gray"
                  onClick={() => onNavigateToWorkspace(selectedJob)}
                  data-testid="job-details-link-btn"
                >
                  <span>İş ayrıntıları</span>
                  <span>&gt;</span>
                </button>
                <button
                  className="btn btn-orange"
                  style={{ padding: '6px 12px', fontSize: '0.8125rem' }}
                  onClick={() => onNavigateToWorkspace(selectedJob)}
                  data-testid="job-open-btn"
                >
                  <span>İşi aç</span>
                </button>

                {selectedJob.isLocalEngine && selectedJob.jobKind === 'bridge-export' && selectedJob.status === 'completed' && (
                  <button
                    className="btn btn-primary-orange"
                    style={{ padding: '6px 12px', fontSize: '0.8125rem', display: 'flex', alignItems: 'center', gap: '4px' }}
                    onClick={() => onAddToArchive ? onAddToArchive(selectedJob) : onNavigateToWorkspace(selectedJob)}
                    data-testid="job-add-to-archive-btn"
                  >
                    <IconArchiveBox size={14} />
                    <span>Arşive ekle</span>
                  </button>
                )}

                {selectedJob.reportAvailable && (
                  <button
                    className="btn btn-outline-gray"
                    style={{ padding: '6px 10px', fontSize: '0.8125rem', display: 'flex', alignItems: 'center', gap: '4px' }}
                    onClick={async () => {
                      try {
                        const blob = selectedJob.jobKind === 'damaged-recovery'
                          ? await client.getRecoveryReport(selectedJob.id)
                          : new Blob([JSON.stringify(await client.getJobReport(selectedJob.id), null, 2)], { type: 'application/json' });
                        const url = URL.createObjectURL(blob);
                        const a = document.createElement('a');
                        a.href = url;
                        a.download = `rapor-${selectedJob.id}.json`;
                        document.body.appendChild(a);
                        a.click();
                        document.body.removeChild(a);
                        URL.revokeObjectURL(url);
                      } catch {
                        setImapActionError({ jobId: selectedJob.id, message: 'Rapor doğrulanamadı veya alınamadı.' });
                      }
                    }}
                    title="JSON Raporu İndir"
                    data-testid="job-download-report-btn"
                  >
                    <IconDownload size={14} />
                    <span>Rapor</span>
                  </button>
                )}
              </div>
            </div>

            {/* Son olaylar */}
            <div style={{ borderTop: '1px solid var(--border-light)', paddingTop: '16px' }}>
              <h3 className="plan-label" style={{ marginBottom: '10px' }}>
                Son olaylar
              </h3>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', fontSize: '0.8125rem' }}>
                {selectedJob.recentEvents.map((evt, idx) => (
                  <div key={idx} style={{ color: 'var(--text-main)' }}>
                    <span style={{ color: 'var(--text-muted)', marginRight: '6px' }}>{evt.time} ·</span>
                    <span>{evt.text}</span>
                  </div>
                ))}
              </div>
            </div>
          </div>
        )}
      </div>

      <NewJobModal
        type={draftModalType}
        isOpen={!productionMode && draftModalType !== null}
        onClose={() => setDraftModalType(null)}
        onSaveDraftJob={onAddNewJob}
      />
    </div>
  );
};
