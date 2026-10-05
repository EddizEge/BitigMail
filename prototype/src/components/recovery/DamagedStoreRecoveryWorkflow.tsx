import React, { useEffect, useState } from 'react';
import { LocalEngineClient, localEngineClient } from '../../api/localEngineClient';
import type { ClientProjectContext, FilePickResult, RecoveryJob, RecoveryPreview } from '../../types/localEngine';

const outcomeLabels: Record<string, string> = {
  pending: 'Başlaması bekleniyor', healthy_extraction: 'Okunabilir içerik çıkarıldı',
  partial_recovered: 'Kısmi içerik kurtarıldı', unreadable_source: 'Kaynak okunamadı',
  cancelled: 'İptal edildi', timed_out: 'Süre sınırına ulaşıldı',
  unresolved_worker: 'İşçi durumu kontrol edilmeli', failed: 'Tamamlanamadı',
};

export const DamagedStoreRecoveryWorkflow: React.FC<{
  client?: LocalEngineClient; clientContext: ClientProjectContext;
}> = ({ client = localEngineClient, clientContext }) => {
  const [source, setSource] = useState<FilePickResult | null>(null);
  const [output, setOutput] = useState<FilePickResult | null>(null);
  const [preview, setPreview] = useState<RecoveryPreview | null>(null);
  const [job, setJob] = useState<RecoveryJob | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const jobId = job?.jobId, jobStatus = job?.status;
  const active = jobStatus === 'queued' || jobStatus === 'running';
  const validScope = Boolean(clientContext.companyId && clientContext.projectId);

  useEffect(() => {
    if (!jobId || !active) return;
    let alive = true, timer: ReturnType<typeof setTimeout>;
    const refresh = async () => {
      try { const next = await client.getRecoveryJob(jobId); if (alive) setJob(next); }
      catch { if (alive) setError('İş durumu alınamadı; bağlantı yeniden denenecek.'); }
      finally { if (alive) timer = setTimeout(refresh, 1000); }
    };
    timer = setTimeout(refresh, 700);
    return () => { alive = false; clearTimeout(timer); };
  }, [client, jobId, active]);

  const act = async (action: () => Promise<void>) => {
    setBusy(true); setError('');
    try { await action(); }
    catch (failure) { setError(failure instanceof Error ? failure.message : 'İşlem başarısız.'); }
    finally { setBusy(false); }
  };

  return <div style={{ padding: '24px', maxWidth: 900, margin: '0 auto', overflowWrap: 'anywhere' }} data-testid="recovery-workflow">
    <h2>Salt Okunur PST/OST Kurtarma</h2>
    <p>Kaynak dosya değiştirilmez. Kurtarılan iletiler yeni bir klasöre, doğrulanmış EML dosyaları olarak yazılır.</p>
    <p><strong>Müşteri:</strong> {clientContext.companyName || 'Seçilmedi'} · <strong>Proje:</strong> {clientContext.projectName || 'Seçilmedi'}</p>
    {!validScope && <p role="status">Başlamadan önce bir müşteri ve proje seçin.</p>}
    <div className="card" style={{ padding: 20, display: 'grid', gap: 12 }}>
      <button className="btn btn-secondary" disabled={busy || !!job} onClick={() => act(async () => {
        const picked = await client.pickSplitSource(); if (!picked.cancelled) { setSource(picked); setPreview(null); }
      })}>Hasarlı PST/OST seç</button>
      <span>{source?.fileName || 'Kaynak seçilmedi'}</span>
      <button className="btn btn-secondary" disabled={busy || !!job} onClick={() => act(async () => {
        const picked = await client.pickOutputDir(); if (!picked.cancelled) { setOutput(picked); setPreview(null); }
      })}>Yeni çıktı klasörü seç</button>
      <span>{output?.displayPath || output?.fileName || 'Hedef seçilmedi'}</span>
      <button className="btn btn-orange" disabled={busy || !validScope || !source?.handle || !output?.handle || !!job}
        onClick={() => act(async () => setPreview(await client.previewRecovery(source!.handle!, output!.handle!)))}>Salt okunur önizleme</button>
      {preview && !job && <section data-testid="recovery-preview">
        <p>Özgün toplam: {preview.originalTotal ?? 'Bilinmiyor'}</p>
        <p>Temsilî hasarlı dosyalarda geniş kapsamlı kabul henüz tamamlanmadı.</p>
        {preview.warnings.map(warning => <p key={warning}>⚠️ {warning}</p>)}
        <details><summary>Dosya doğrulama bilgisi</summary><p>{preview.sourceSha256}</p></details>
        <button className="btn btn-orange" disabled={busy || !validScope || !preview.canStart}
          onClick={() => act(async () => setJob(await client.startRecovery(preview.sourceHandle, preview.outputDirectoryHandle, preview.sourceSha256, clientContext)))}>
          Yeni çıktıya kurtarmayı başlat
        </button>
      </section>}
      {job && <section aria-live="polite" data-testid="recovery-result">
        <h3>{job.stage}</h3><p>{outcomeLabels[job.outcome] || 'Sonuç kontrol ediliyor'}</p>
        <p>Kurtarılan: {job.recoveredCount} · Okunamayan bölge/öğe: {job.failedBoundaryCount} · Özgün toplam: {job.originalTotal ?? 'Bilinmiyor'}</p>
        {job.partial && <p>Bu çıktı kısmi olarak doğrulanmıştır; eksik bölgeler raporda listelenir.</p>}
        {job.error && <p role="alert">{job.error}</p>}
        {active && <button className="btn btn-secondary" disabled={busy} onClick={() => act(async () => {
          await client.cancelRecovery(job.jobId); setJob(await client.getRecoveryJob(job.jobId));
        })}>İptal et</button>}
        {job.reportAvailable && <button className="btn btn-secondary" disabled={busy} onClick={() => act(async () => {
          const report = await client.getRecoveryReport(job.jobId); const url = URL.createObjectURL(report);
          const link = document.createElement('a'); link.href = url; link.download = `BitigMail-kurtarma-${job.jobId}.json`; link.click();
          setTimeout(() => URL.revokeObjectURL(url), 1000);
        })}>Ayrıntılı kurtarma raporunu indir</button>}
        {!active && <button className="btn btn-outline-gray" disabled={busy} onClick={() => { setJob(null); setPreview(null); setError(''); }}>Yeni kurtarma hazırla</button>}
      </section>}
      {error && <p role="alert">{error}</p>}
    </div>
  </div>;
};
