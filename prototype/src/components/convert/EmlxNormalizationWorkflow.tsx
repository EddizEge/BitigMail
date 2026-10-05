import { useEffect, useState } from 'react';
import { localEngineClient } from '../../api/localEngineClient';
import { AppState } from '../../state/useAppState';
import { EmlxPreviewResult, FilePickResult, LocalJobRecord } from '../../types/localEngine';
import './mimeWorkflow.css';

export function EmlxNormalizationWorkflow({ state }: { state: AppState }) {
  const company = state.companies.find(c => c.id === state.plan.companyId) || state.companies[0];
  const project = state.projects.find(p => p.id === state.plan.projectId) || state.projects.find(p => p.companyId === company?.id);
  const context = { companyId: company?.id || '', companyName: company?.name || '', projectId: project?.id || '', projectName: project?.name || '' };
  const [mode, setMode] = useState<'tree' | 'files'>('tree'); const [source, setSource] = useState<FilePickResult>();
  const [preview, setPreview] = useState<EmlxPreviewResult>(); const [output, setOutput] = useState<FilePickResult>();
  const [job, setJob] = useState<LocalJobRecord>(); const [error, setError] = useState(''); const [busy, setBusy] = useState(false);
  useEffect(() => { if (!job || !['queued', 'converting', 'verifying'].includes(job.status)) return; const timer = setInterval(() => localEngineClient.getJob(job.jobId).then(setJob).catch(e => setError(e.message)), 700); return () => clearInterval(timer); }, [job]);
  async function pick() { setBusy(true); setError(''); try { const picked = await localEngineClient.pickEmlxSource(mode); if (picked.error) throw new Error(picked.error); if (!picked.cancelled && picked.handle) { setSource(picked); setPreview(await localEngineClient.previewEmlx(picked.handle)); setOutput(undefined); setJob(undefined); } } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  async function pickOutput() { try { const picked = await localEngineClient.pickOutputDir(); if (picked.error) throw new Error(picked.error); if (!picked.cancelled) setOutput(picked); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } }
  async function start() { if (!source?.handle || !output?.handle || !preview) return; setBusy(true); try { setJob(await localEngineClient.startEmlx({ sourceHandle: source.handle, outputDirHandle: output.handle, expectedSourceFingerprint: preview.sourceFingerprint, idempotencyKey: crypto.randomUUID(), clientContext: context, enqueueIfBusy: true })); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  return <section className="mime-workflow" data-testid="emlx-workflow" aria-label="Apple Mail EMLX normalizasyonu">
    <header className="mime-header"><div><span className="mime-eyebrow">APPLE MAIL DOSYALARINDAN</span><h2>EMLX → doğrulanmış EML klasörleri</h2><p>İletilerin özgün içeriğini koruyarak yeni EML klasörleri oluşturur.</p></div></header>
    <div className="mime-context"><span>Müşteri <strong>{context.companyName}</strong></span><span>Proje <strong>{context.projectName}</strong></span></div>
    <div className="mime-notice"><strong>Bilinen kapsam</strong><span>Apple ek bilgileri ayrı dosyada saklanır; bazı Apple Mail özellikleri henüz diğer hedeflere aktarılmaz.</span></div>
    <details><summary>Teknik koruma ayrıntıları</summary><p className="mime-muted">MIME içeriği değiştirilmez; plist bilgileri sidecar olarak korunur. Apple bayrakları ve tarihleri IMAP/PST alanlarına eşlenmez. Harici ek depoları açık engeldir.</p></details>
    {error && <div className="mime-error" role="alert">{error}</div>}
    <section className="card mime-card"><div className="mime-section-title"><span className="mime-step">1</span><h3>Apple Mail kaynağını seç</h3></div>
      <div className="mime-modes"><button className={`mime-mode ${mode === 'tree' ? 'selected' : ''}`} onClick={() => setMode('tree')}><strong>EMLX klasör ağacı</strong><small>Alt klasörleri korur</small></button><button className={`mime-mode ${mode === 'files' ? 'selected' : ''}`} onClick={() => setMode('files')}><strong>EMLX dosyaları</strong><small>Fiziksel kopyaları korur</small></button></div>
      <div className="mime-actions"><button className="btn btn-primary-orange" data-testid="emlx-pick" disabled={busy} onClick={pick}>{busy ? 'Kontrol ediliyor…' : 'EMLX kaynağı seç'}</button><span>{source?.displayPath}</span></div>
      {preview && <><div className="mime-stats"><div><strong data-testid="emlx-count">{preview.totalItems}</strong><span>Tam EMLX</span></div><div><strong>{Math.ceil(preview.totalBytes / 1024)}</strong><span>KiB kaynak</span></div></div>{preview.warnings.map(w => <p className="mime-muted" key={w}>{w}</p>)}</>}
    </section>
    {preview && <section className="card mime-card"><div className="mime-section-title"><span className="mime-step">2</span><h3>Yeni EML çıktı klasörü</h3></div><p className="mime-muted">Kaynak dışında yeni ve çakışmasız bir ağaç atomik olarak yayınlanır; mevcut dosyaların üzerine yazılmaz.</p><div className="mime-actions"><button className="btn btn-outline-gray" data-testid="emlx-output" onClick={pickOutput}>Çıktı klasörü seç</button><span>{output?.displayPath || 'Henüz seçilmedi'}</span></div><button className="btn btn-primary-orange" data-testid="emlx-start" disabled={!output?.handle || busy} onClick={start}>Normalizasyonu başlat / sıraya ekle</button></section>}
    {job && <section className="card mime-card" data-testid="emlx-job"><h3>{job.status === 'completed' ? 'EML ağacı hazır' : job.status === 'failed' ? 'Normalizasyon tamamlanamadı' : 'Normalizasyon sürüyor'}</h3><p><strong>{job.clientContext.companyName}</strong> · {job.clientContext.projectName}</p><progress max="100" value={job.percentComplete} /><p>{job.stage} · {job.itemsWritten}/{job.totalItems}</p>{job.errorMessage && <p className="mime-error" role="alert">{job.errorMessage}</p>}{job.outputPath && <><p className="mime-path">{job.outputPath}</p><p className="mime-muted">Bu EML ağacını Arşiv veya Transfer ekranında kaynak olarak seçebilirsin.</p></>}</section>}
  </section>;
}
