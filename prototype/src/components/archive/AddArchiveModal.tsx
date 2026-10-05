import React, { useState, useEffect, useMemo } from 'react';
import { AppState } from '../../state/useAppState';
import { LocalEngineClient } from '../../api/localEngineClient';
import {
  ArchiveIngestPreviewResponse,
  LocalJobRecord,
  MimeSourceMode,
} from '../../types/localEngine';
import { Modal } from '../ui/Modal';
import { IconAlertCircle, IconCheck, IconFile, IconFolder, IconRefreshCw } from '../ui/Icons';
import { Badge } from '../ui/Badge';

interface AddArchiveModalProps {
  isOpen: boolean;
  onClose: () => void;
  state: AppState;
  client: LocalEngineClient;
  onArchiveAdded: (record: LocalJobRecord) => void;
  initialSourceJobId?: string | null;
}

export const AddArchiveModal: React.FC<AddArchiveModalProps> = ({
  isOpen,
  onClose,
  state,
  client,
  onArchiveAdded,
  initialSourceJobId,
}) => {
  const [companyId, setCompanyId] = useState<string>(() => state.companies[0]?.id || 'comp-default');
  const [projectId, setProjectId] = useState<string>(() => {
    const compProj = state.projects.find((p) => p.companyId === (state.companies[0]?.id || ''));
    return compProj?.id || 'proj-default';
  });

  const [sourceType, setSourceType] = useState<'mime' | 'bridge-export'>('mime');
  const [mimeMode, setMimeMode] = useState<MimeSourceMode>('eml-files');
  const [sourceHandle, setSourceHandle] = useState<string | null>(null);
  const [sourceJobId, setSourceJobId] = useState<string>('');
  const [pickedFileName, setPickedFileName] = useState<string | null>(null);
  const [pickedDisplayPath, setPickedDisplayPath] = useState<string | null>(null);
  const [pickedSizeBytes, setPickedSizeBytes] = useState<number | null>(null);

  const [archiveName, setArchiveName] = useState<string>('');
  const [preview, setPreview] = useState<ArchiveIngestPreviewResponse | null>(null);
  const [isPreviewLoading, setIsPreviewLoading] = useState(false);
  const [isStarting, setIsStarting] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  // Completed bridge-export jobs
  const [completedBridgeJobs, setCompletedBridgeJobs] = useState<LocalJobRecord[]>([]);
  const [loadingJobs, setLoadingJobs] = useState(false);

  useEffect(() => {
    if (!isOpen) return;
    let active = true;
    setLoadingJobs(true);
    Promise.all([
      client.getAllJobs(),
      initialSourceJobId ? client.getJob(initialSourceJobId) : Promise.resolve(null),
    ])
      .then(([records, initialRecord]) => {
        if (!active) return;
        const exports = records.filter(
          (r) => r.jobKind === 'bridge-export' && r.status === 'completed'
        );
        if (initialRecord?.jobKind === 'bridge-export' && initialRecord.status === 'completed' &&
            !exports.some(job => job.jobId === initialRecord.jobId)) exports.unshift(initialRecord);
        setCompletedBridgeJobs(exports);
        setLoadingJobs(false);

        if (initialSourceJobId) {
          setSourceType('bridge-export');
          setSourceJobId(initialSourceJobId);
          const found = initialRecord?.jobId === initialSourceJobId ? initialRecord : exports.find((j) => j.jobId === initialSourceJobId);
          if (found) {
            setCompanyId(found.clientContext.companyId);
            setProjectId(found.clientContext.projectId);
            setArchiveName(found.targetFileName || found.sourceFileName || 'Köprü Arşivi');
          }
        } else if (exports.length > 0 && !sourceJobId) {
          setSourceJobId(exports[0].jobId);
        }
      })
      .catch(() => {
        if (active) setLoadingJobs(false);
      });
    return () => {
      active = false;
    };
  }, [isOpen, initialSourceJobId, client]);

  const selectedBridgeJob = useMemo(() => {
    return completedBridgeJobs.find((j) => j.jobId === sourceJobId) || (!sourceJobId ? completedBridgeJobs[0] : undefined);
  }, [completedBridgeJobs, sourceJobId]);

  const handleSelectBridgeJob = (jobId: string) => {
    setSourceJobId(jobId);
    setPreview(null);
    const job = completedBridgeJobs.find((j) => j.jobId === jobId);
    if (job) {
      setCompanyId(job.clientContext.companyId);
      setProjectId(job.clientContext.projectId);
      if (!archiveName || archiveName === 'Köprü Arşivi') {
        setArchiveName(job.targetFileName || job.sourceFileName || 'Köprü Arşivi');
      }
    }
  };

  const currentCompany = state.companies.find((c) => c.id === companyId);
  const companyProjects = state.projects.filter((p) => p.companyId === companyId);
  const currentProject = companyProjects.find((p) => p.id === projectId);

  const handlePickSource = async () => {
    setActionError(null);
    setPreview(null);
    try {
      const res = await client.pickArchiveSource(mimeMode);
      if (!res.cancelled && res.handle) {
        setSourceHandle(res.handle);
        setPickedFileName(res.fileName || 'MIME Kaynağı');
        setPickedDisplayPath(res.displayPath || null);
        setPickedSizeBytes(res.sizeBytes || null);
        if (!archiveName) {
          const defaultName = res.fileName
            ? res.fileName.replace(/\.[^/.]+$/, '')
            : 'Yeni Arşiv';
          setArchiveName(defaultName);
        }
      }
    } catch (err: any) {
      setActionError(err.message || 'Kaynak dosyası seçilemedi.');
    }
  };

  const handleCreatePreview = async () => {
    setActionError(null);
    if (!archiveName.trim()) {
      setActionError('Lütfen arşiv adı belirtin.');
      return;
    }
    if (sourceType === 'mime' && !sourceHandle) {
      setActionError('Lütfen bir MIME kaynak dosyası veya klasörü seçin.');
      return;
    }

    const effectiveJobId = sourceType === 'bridge-export'
      ? (sourceJobId || selectedBridgeJob?.jobId || '').trim()
      : null;

    if (sourceType === 'bridge-export' && !effectiveJobId) {
      setActionError('Lütfen tamamlanmış bir köprü aktarım işi seçin.');
      return;
    }

    const effectiveCompanyId = sourceType === 'bridge-export' && selectedBridgeJob
      ? selectedBridgeJob.clientContext.companyId
      : (companyId || 'comp-default');
    const effectiveProjectId = sourceType === 'bridge-export' && selectedBridgeJob
      ? selectedBridgeJob.clientContext.projectId
      : (projectId || 'proj-default');
    const effectiveCompanyName = sourceType === 'bridge-export' && selectedBridgeJob
      ? selectedBridgeJob.clientContext.companyName
      : (currentCompany?.name || companyId || 'Varsayılan Şirket');
    const effectiveProjectName = sourceType === 'bridge-export' && selectedBridgeJob
      ? selectedBridgeJob.clientContext.projectName
      : (currentProject?.name || projectId || 'Varsayılan Proje');

    setIsPreviewLoading(true);
    try {
      const response = await client.createArchivePreview({
        sourceHandle: sourceType === 'mime' ? sourceHandle : null,
        sourceJobId: effectiveJobId,
        archiveName: archiveName.trim(),
        companyId: effectiveCompanyId,
        projectId: effectiveProjectId,
        companyName: effectiveCompanyName,
        projectName: effectiveProjectName,
      });
      setPreview(response);
    } catch (err: any) {
      setActionError(err.message || 'Arşiv önizlemesi oluşturulamadı.');
    } finally {
      setIsPreviewLoading(false);
    }
  };

  const handleStartIngest = async () => {
    if (!preview || !preview.canIngest) return;
    setIsStarting(true);
    setActionError(null);
    try {
      const idempotencyKey = `arch-start-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;
      const jobRecord = await client.startArchiveIngest({
        previewId: preview.previewId,
        idempotencyKey,
        enqueueIfBusy: true,
      });
      onArchiveAdded(jobRecord);
      onClose();
    } catch (err: any) {
      setActionError(err.message || 'Arşivleme işlemi başlatılamadı.');
    } finally {
      setIsStarting(false);
    }
  };

  const formatBytes = (bytes: number): string => {
    if (!bytes || bytes <= 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${sizes[i]}`;
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title="Yeni Yerel Arşiv Ekle"
      subtitle="Fiziksel EML, MBOX veya tamamlanmış köprü işinden yeni yönetilen arşiv oluşturun"
      maxWidth="680px"
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }} data-testid="archive-add-modal">
        {actionError && (
          <div
            style={{
              padding: '10px 14px',
              background: '#fef2f2',
              border: '1px solid #fecaca',
              borderRadius: '6px',
              color: '#991b1b',
              fontSize: '0.8125rem',
              display: 'flex',
              alignItems: 'center',
              gap: '8px',
            }}
            data-testid="archive-add-error"
          >
            <IconAlertCircle size={16} color="#dc2626" />
            <span>{actionError}</span>
          </div>
        )}

        {/* 1. Şirket ve Proje Seçimi */}
        {sourceType === 'bridge-export' ? (
          <div
            style={{
              padding: '10px 14px',
              background: '#f8fafc',
              border: '1px solid #e2e8f0',
              borderRadius: '6px',
            }}
            data-testid="archive-frozen-owner-context"
          >
            <div style={{ fontSize: '0.75rem', fontWeight: 600, color: 'var(--text-muted)' }}>
              Sabitlenmiş Müşteri / Proje Bağlamı (Köprü İşinden Alındı):
            </div>
            <div style={{ fontSize: '0.875rem', fontWeight: 700, color: 'var(--text-main)', marginTop: '2px' }}>
              {selectedBridgeJob?.clientContext?.companyName || currentCompany?.name || companyId} &gt;{' '}
              {selectedBridgeJob?.clientContext?.projectName || currentProject?.name || projectId}
            </div>
            <span style={{ fontSize: '0.6875rem', color: '#64748b', marginTop: '2px', display: 'block' }}>
              Köprü aktarımlarında arşiv sahibi işin dondurulmuş müşteri ve proje kaydına kilitlidir.
            </span>
          </div>
        ) : (
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '12px' }}>
            <div>
              <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, color: 'var(--text-main)', marginBottom: '4px' }}>
                Müşteri Şirket
              </label>
              {state.companies.length > 0 ? (
                <select
                  className="select-input"
                  value={companyId}
                  onChange={(e) => {
                    setCompanyId(e.target.value);
                    const firstProj = state.projects.find((p) => p.companyId === e.target.value);
                    if (firstProj) setProjectId(firstProj.id);
                    setPreview(null);
                  }}
                  data-testid="archive-add-company-select"
                >
                  {state.companies.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name}
                    </option>
                  ))}
                </select>
              ) : (
                <input
                  type="text"
                  className="text-input"
                  placeholder="Şirket adı..."
                  value={companyId}
                  onChange={(e) => setCompanyId(e.target.value)}
                  data-testid="archive-add-company-select"
                />
              )}
            </div>

            <div>
              <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, color: 'var(--text-main)', marginBottom: '4px' }}>
                İlgili Proje
              </label>
              {companyProjects.length > 0 ? (
                <select
                  className="select-input"
                  value={projectId}
                  onChange={(e) => {
                    setProjectId(e.target.value);
                    setPreview(null);
                  }}
                  data-testid="archive-add-project-select"
                >
                  {companyProjects.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name}
                    </option>
                  ))}
                </select>
              ) : (
                <input
                  type="text"
                  className="text-input"
                  placeholder="Proje adı..."
                  value={projectId}
                  onChange={(e) => setProjectId(e.target.value)}
                  data-testid="archive-add-project-select"
                />
              )}
            </div>
          </div>
        )}

        {/* 2. Kaynak Türü Seçimi */}
        <div>
          <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, color: 'var(--text-main)', marginBottom: '6px' }}>
            Kaynak Türü
          </label>
          <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
            <button
              type="button"
              className={`btn ${sourceType === 'mime' ? 'btn-orange' : 'btn-outline-gray'}`}
              style={{ padding: '6px 12px', fontSize: '0.8125rem' }}
              onClick={() => {
                setSourceType('mime');
                setPreview(null);
              }}
              data-testid="source-type-mime-btn"
            >
              <IconFile size={14} />
              <span>Yerel Dosya / Klasör (MIME)</span>
            </button>
            <button
              type="button"
              className={`btn ${sourceType === 'bridge-export' ? 'btn-orange' : 'btn-outline-gray'}`}
              style={{ padding: '6px 12px', fontSize: '0.8125rem' }}
              onClick={() => {
                setSourceType('bridge-export');
                setPreview(null);
                if (completedBridgeJobs.length > 0) {
                  const targetJob = completedBridgeJobs.find((j) => j.jobId === sourceJobId) || completedBridgeJobs[0];
                  setSourceJobId(targetJob.jobId);
                  setCompanyId(targetJob.clientContext.companyId);
                  setProjectId(targetJob.clientContext.projectId);
                  if (!archiveName || archiveName === 'Köprü Arşivi') {
                    setArchiveName(targetJob.targetFileName || targetJob.sourceFileName || 'Köprü Arşivi');
                  }
                }
              }}
              data-testid="source-type-bridge-btn"
            >
              <IconFolder size={14} />
              <span>Köprü Dışa Aktarımı (Bridge Job)</span>
            </button>
          </div>
        </div>

        {/* 3. MIME Kaynağı Seçici veya Köprü İş Seçici */}
        {sourceType === 'mime' ? (
          <div style={{ background: 'var(--bg-subtle)', padding: '12px', borderRadius: '6px', border: '1px solid var(--border-light)' }}>
            <div style={{ display: 'flex', gap: '8px', alignItems: 'center', marginBottom: '10px', flexWrap: 'wrap' }}>
              <label style={{ fontSize: '0.8125rem', fontWeight: 600 }}>Biçim:</label>
              <select
                className="select-input"
                style={{ width: 'auto', padding: '4px 8px', fontSize: '0.8125rem' }}
                value={mimeMode}
                onChange={(e) => {
                  setMimeMode(e.target.value as MimeSourceMode);
                  setSourceHandle(null);
                  setPickedFileName(null);
                  setPreview(null);
                }}
                data-testid="archive-add-mime-mode"
              >
                <option value="eml-files">EML Dosyaları</option>
                <option value="eml-tree">EML Klasör Ağacı</option>
                <option value="mbox">MBOX Dosyası (mboxrd)</option>
              </select>

              <button
                type="button"
                className="btn btn-outline-orange"
                style={{ padding: '5px 12px', fontSize: '0.8125rem' }}
                onClick={handlePickSource}
                data-testid="archive-pick-source-btn"
              >
                Kaynak Dosya / Klasör Seç...
              </button>
            </div>

            {pickedFileName && (
              <div
                style={{ fontSize: '0.8125rem', color: '#15803d', background: '#f0fdf4', padding: '8px 10px', borderRadius: '4px' }}
                data-testid="archive-picked-source-info"
              >
                <strong>Seçilen Kaynak:</strong> {pickedFileName}{' '}
                {pickedSizeBytes ? `(${formatBytes(pickedSizeBytes)})` : ''}
                {pickedDisplayPath && (
                  <div style={{ fontSize: '0.75rem', color: '#475569', marginTop: '2px', wordBreak: 'break-all' }}>
                    {pickedDisplayPath}
                  </div>
                )}
              </div>
            )}
          </div>
        ) : (
          <div style={{ background: 'var(--bg-subtle)', padding: '12px', borderRadius: '6px', border: '1px solid var(--border-light)' }}>
            <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '6px' }}>
              Tamamlanmış Köprü Dışa Aktarımı
            </label>
            {loadingJobs ? (
              <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
                Köprü işleri yükleniyor...
              </div>
            ) : completedBridgeJobs.length === 0 ? (
              <div
                style={{
                  fontSize: '0.8125rem',
                  color: '#92400e',
                  background: '#fffbeb',
                  padding: '10px 12px',
                  borderRadius: '6px',
                  border: '1px solid #fde68a',
                }}
                data-testid="archive-no-completed-bridge-jobs"
              >
                Tamamlanmış köprü dışa aktarımı (IMAP → Dosya) bulunamadı. Aktarım sekmesinden bir dışa aktarım tamamlayın veya yukarıdan yerel dosya seçin.
              </div>
            ) : (
              <div>
                <select
                  className="select-input"
                  value={sourceJobId || selectedBridgeJob?.jobId || ''}
                  onChange={(e) => handleSelectBridgeJob(e.target.value)}
                  data-testid="archive-completed-bridge-jobs-select"
                >
                  {completedBridgeJobs.map((j) => {
                    const compName = j.clientContext.companyName || j.clientContext.companyId;
                    const projName = j.clientContext.projectName || j.clientContext.projectId;
                    const dateStr = j.createdAt ? new Date(j.createdAt).toLocaleDateString('tr-TR') : '—';
                    const label = `${compName} / ${projName} — ${j.sourceFileName || 'IMAP'} → ${j.targetFileName || 'Dışa Aktarım'} (${dateStr})`;
                    return (
                      <option key={j.jobId} value={j.jobId}>
                        {label}
                      </option>
                    );
                  })}
                </select>

                {selectedBridgeJob && (
                  <div
                    style={{
                      marginTop: '8px',
                      padding: '8px 10px',
                      background: '#ffffff',
                      border: '1px solid var(--border-light)',
                      borderRadius: '4px',
                      fontSize: '0.75rem',
                      display: 'flex',
                      flexDirection: 'column',
                      gap: '4px',
                      color: 'var(--text-muted)',
                    }}
                    data-testid="archive-selected-bridge-job-details"
                  >
                    <div>
                      <strong style={{ color: 'var(--text-main)' }}>Kaynak / Hedef:</strong>{' '}
                      {selectedBridgeJob.sourceFileName} → {selectedBridgeJob.targetFileName}
                    </div>
                    <div>
                      <strong style={{ color: 'var(--text-main)' }}>İletiler:</strong>{' '}
                      {selectedBridgeJob.itemsWritten} ileti aktarıldı ({selectedBridgeJob.stage})
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>
        )}

        {/* 4. Arşiv Adı */}
        <div>
          <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, color: 'var(--text-main)', marginBottom: '4px' }}>
            Arşiv Adı
          </label>
          <input
            type="text"
            className="text-input"
            placeholder="Örn: Kurumsal İletiler 2024"
            value={archiveName}
            onChange={(e) => {
              setArchiveName(e.target.value);
              setPreview(null);
            }}
            data-testid="archive-add-name-input"
          />
        </div>

        {/* 5. Önizleme Butonu */}
        <div style={{ display: 'flex', justifyContent: 'flex-start' }}>
          <button
            type="button"
            className="btn btn-outline-gray"
            onClick={handleCreatePreview}
            disabled={
              isPreviewLoading ||
              (sourceType === 'mime' ? !sourceHandle : (!sourceJobId.trim() && completedBridgeJobs.length === 0)) ||
              !archiveName.trim()
            }
            data-testid="archive-preview-btn"
          >
            {isPreviewLoading ? (
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px' }}>
                <IconRefreshCw size={14} className="spin-animation" />
                <span>Doğrulanıyor...</span>
              </span>
            ) : (
              <span>Önizle ve Doğrula</span>
            )}
          </button>
        </div>

        {/* 6. Önizleme Bulguları */}
        {preview && (
          <div
            style={{
              border: '1px solid var(--border-mid)',
              borderRadius: '6px',
              padding: '12px 14px',
              background: preview.canIngest ? '#fafafa' : '#fff7ed',
            }}
            data-testid="archive-preview-summary"
          >
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '8px' }}>
              <span style={{ fontWeight: 700, fontSize: '0.875rem' }}>Ön Kontrol ve Plan Özeti</span>
              {preview.canIngest ? (
                <Badge variant="success">✓ Arşive Almaya Hazır</Badge>
              ) : (
                <Badge variant="error">⛔ Engellendi</Badge>
              )}
            </div>

            {preview.blockerReason && (
              <div style={{ color: '#b91c1c', fontSize: '0.8125rem', marginBottom: '8px' }}>
                <strong>Engel:</strong> {preview.blockerReason}
              </div>
            )}

            {preview.qualificationWarnings && preview.qualificationWarnings.length > 0 && (
              <div className="mime-notice" data-testid="archive-qualification-warnings">
                <strong>Kaynak sadakat uyarıları</strong>
                {preview.qualificationWarnings.map((warning, index) => <span key={index}>{warning}</span>)}
              </div>
            )}

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(110px, 1fr))', gap: '8px', marginBottom: '10px' }}>
              <div style={{ background: '#fff', padding: '6px 10px', borderRadius: '4px', border: '1px solid var(--border-light)' }}>
                <span style={{ fontSize: '0.6875rem', color: 'var(--text-muted)' }}>TOPLAM ÖĞE</span>
                <div style={{ fontWeight: 700, fontSize: '1rem', color: 'var(--brand-orange)' }}>{preview.totalItems}</div>
              </div>
              <div style={{ background: '#fff', padding: '6px 10px', borderRadius: '4px', border: '1px solid var(--border-light)' }}>
                <span style={{ fontSize: '0.6875rem', color: 'var(--text-muted)' }}>TOPLAM BOYUT</span>
                <div style={{ fontWeight: 700, fontSize: '1rem' }}>{formatBytes(preview.totalSizeBytes)}</div>
              </div>
              <div style={{ background: '#fff', padding: '6px 10px', borderRadius: '4px', border: '1px solid var(--border-light)' }}>
                <span style={{ fontSize: '0.6875rem', color: 'var(--text-muted)' }}>KLASÖR SAYISI</span>
                <div style={{ fontWeight: 700, fontSize: '1rem' }}>{preview.folders.length}</div>
              </div>
              {preview.estimatedRequiredBytes != null ? (
                <div style={{ background: '#fff', padding: '6px 10px', borderRadius: '4px', border: '1px solid var(--border-light)' }}>
                  <span style={{ fontSize: '0.6875rem', color: 'var(--text-muted)' }}>TAHMİNİ GEREKEN ALAN</span>
                  <div style={{ fontWeight: 700, fontSize: '1rem' }} data-testid="archive-estimated-required-bytes">{formatBytes(preview.estimatedRequiredBytes)}</div>
                </div>
              ) : null}
              {preview.availableFreeBytes != null ? (
                <div style={{ background: '#fff', padding: '6px 10px', borderRadius: '4px', border: '1px solid var(--border-light)' }}>
                  <span style={{ fontSize: '0.6875rem', color: 'var(--text-muted)' }}>KULLANILABİLİR ALAN</span>
                  <div style={{ fontWeight: 700, fontSize: '1rem' }} data-testid="archive-available-free-bytes">{formatBytes(preview.availableFreeBytes)}</div>
                </div>
              ) : null}
            </div>

            {preview.estimateBasis ? (
              <p style={{ fontSize: '0.6875rem', color: 'var(--text-muted)', margin: '0 0 10px' }}>
                Planlama tahmini: {preview.estimateBasis}. Alan rezervasyonu veya kesin üst sınır değildir.
              </p>
            ) : null}

            {preview.folders.length > 0 && (
              <div style={{ maxHeight: '120px', overflowY: 'auto', border: '1px solid var(--border-light)', borderRadius: '4px' }}>
                <table className="data-table" style={{ fontSize: '0.75rem', width: '100%' }}>
                  <thead>
                    <tr>
                      <th>Klasör</th>
                      <th style={{ textAlign: 'right' }}>İleti Sayısı</th>
                      <th style={{ textAlign: 'right' }}>Boyut</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.folders.map((f, idx) => (
                      <tr key={idx}>
                        <td>{f.folderName}</td>
                        <td style={{ textAlign: 'right' }}>{f.itemCount}</td>
                        <td style={{ textAlign: 'right' }}>{formatBytes(f.totalSizeBytes)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}

        {/* 7. Alt Aksiyonlar */}
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px', borderTop: '1px solid var(--border-light)', paddingTop: '12px' }}>
          <button
            type="button"
            className="btn btn-outline-gray"
            onClick={onClose}
            data-testid="archive-add-cancel-btn"
          >
            Vazgeç
          </button>
          <button
            type="button"
            className="btn btn-primary-orange"
            disabled={!preview || !preview.canIngest || isStarting}
            onClick={handleStartIngest}
            data-testid="archive-start-btn"
          >
            {isStarting ? (
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px' }}>
                <IconRefreshCw size={14} className="spin-animation" />
                <span>Başlatılıyor...</span>
              </span>
            ) : (
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px' }}>
                <IconCheck size={16} />
                <span>Başlat / sıraya ekle</span>
              </span>
            )}
          </button>
        </div>
      </div>
    </Modal>
  );
};
