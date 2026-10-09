import React, { useState, useEffect } from 'react';
import { localEngineClient } from '../../api/localEngineClient';
import { LocalJobRecord } from '../../types/localEngine';
import { IconFile } from '../ui/Icons';
import { Badge } from '../ui/Badge';
import { PageHeader } from '../layout/PageHeader';

const JOB_KIND_LABELS: Record<string, string> = {
  split: 'PST bölme',
  convert: 'OST → PST dönüşümü',
  'mime-import': 'EML / MBOX → PST dönüşümü',
  'outlook-eml-normalize': 'PST / OST / OLM → EML dönüşümü',
  'emlx-normalize': 'Apple Mail EMLX → EML dönüşümü',
  'imap-transfer': 'Hesaptan hesaba aktarım',
  'bridge-import': 'Dosyadan hesaba aktarım',
  'bridge-export': 'Hesaptan dosyaya aktarım',
  'pop-snapshot': "POP'tan EML'e aktarım",
  'archive-ingest': 'Arşive alma',
  'archive-reindex': 'Arşiv dizinini yenileme',
  'damaged-recovery': 'Hasarlı dosya kurtarma',
};

export const jobKindLabel = (jobKind?: string | null) => JOB_KIND_LABELS[jobKind || ''] || 'Yerel iş';

interface ReportsViewProps {
  /** Oturum açılmış gerçek uygulama: örnek rapor satırları gösterilmez. */
  productionMode?: boolean;
}

export const ReportsView: React.FC<ReportsViewProps> = ({ productionMode = false }) => {
  const [realJobs, setRealJobs] = useState<LocalJobRecord[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [downloadError, setDownloadError] = useState('');

  useEffect(() => {
    let isMounted = true;
    localEngineClient.getAllJobs().then((jobs) => {
      if (isMounted) {
        setRealJobs(jobs.filter((j) => j.status === 'completed'));
        setLoaded(true);
      }
    }).catch(() => {
      if (isMounted) setLoaded(true);
    });
    return () => { isMounted = false; };
  }, []);

  const handleDownloadRealReport = async (jobId: string) => {
    try {
      const report = await localEngineClient.getJobReport(jobId);
      const json = JSON.stringify(report, null, 2);
      const blob = new Blob([json], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `rapor-${jobId}.json`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      setDownloadError('');
    } catch {
      setDownloadError('Rapor indirilemedi. Uygulamayı kapatıp yeniden açtıktan sonra tekrar deneyin.');
    }
  };

  const sampleReports = productionMode ? [] : [
    {
      id: 'rep-001',
      title: 'Posta Geçişi Tamamlanma Raporu',
      jobName: 'Posta geçişi (Örnek Şirket)',
      date: '18.12.2024 14:35',
      itemsCount: 248,
      transferred: 244,
      skipped: 4,
      failed: 0,
      type: 'migration',
    },
    {
      id: 'rep-002',
      title: 'Mac Arşivi İçe Aktarım Özeti',
      jobName: 'Mac arşivi aktarımı (Örnek Eğitim)',
      date: '12.12.2024 09:15',
      itemsCount: 312,
      transferred: 310,
      skipped: 2,
      failed: 0,
      type: 'migration',
    },
    {
      id: 'rep-003',
      title: 'PST Kurtarma ve Ayıklama Günlüğü',
      jobName: 'Posta kurtarma (Örnek Mimarlık)',
      date: '08.12.2024 10:10',
      itemsCount: 1000,
      transferred: 850,
      skipped: 0,
      failed: 150,
      type: 'recovery',
    },
  ];

  const handleDownloadSample = (rep: (typeof sampleReports)[0], ext: 'json' | 'csv') => {
    const content =
      ext === 'json'
        ? JSON.stringify(
            {
              rapor: rep.title,
              is: rep.jobName,
              tarih: rep.date,
              ozet: { toplam: rep.itemsCount, aktarilan: rep.transferred, atlanan: rep.skipped, basarisiz: rep.failed },
              not: 'BitigMail Sentetik Demo Raporu',
            },
            null,
            2
          )
        : `Rapor,Is,Tarih,Toplam,Aktarilan,Atlanan,Basarisiz\n"${rep.title}","${rep.jobName}","${rep.date}",${rep.itemsCount},${rep.transferred},${rep.skipped},${rep.failed}`;

    const blob = new Blob([content], { type: ext === 'json' ? 'application/json' : 'text/csv' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${rep.id}-${ext}.${ext}`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', flex: 1 }} data-testid="reports-view">
      <PageHeader
        title="Raporlar"
        description={productionMode
          ? 'Tamamlanan işlerin doğrulama raporlarını buradan indirin; müşteriye teslim için saklayın.'
          : 'Tamamlanan işlerin raporları ve örnek rapor dökümleri'}
        testId="reports-page-header"
      />

      <div className="reports-body">
        {downloadError && <div className="notice notice-warning" role="alert" style={{ marginBottom: '12px' }}>{downloadError}</div>}
        {productionMode && loaded && realJobs.length === 0 ? (
          <div className="empty-state-card" data-testid="reports-empty-state">
            <h2>Henüz rapor yok</h2>
            <div className="empty-state-copy">Bir iş tamamlandığında doğrulama raporu burada listelenir. İşleri İş merkezi'nden izleyebilirsiniz.</div>
          </div>
        ) : (
        <div style={{ border: '1px solid var(--border-light)', borderRadius: '8px', overflowX: 'auto', background: '#ffffff' }}>
          <table className="data-table" data-testid="reports-table">
            <thead>
              <tr>
                <th style={{ width: '30%' }}>Rapor</th>
                <th style={{ width: '25%' }}>Müşteri / proje</th>
                <th style={{ width: '15%' }}>Tarih</th>
                <th style={{ width: '15%' }}>Sonuç</th>
                <th style={{ width: '15%', textAlign: 'right' }}>İndir</th>
              </tr>
            </thead>
            <tbody>
              {realJobs.map((job) => (
                <tr key={job.jobId} style={{ background: '#fcfdfa' }} data-testid={`report-row-${job.jobId}`}>
                  <td style={{ fontWeight: 600 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                      <IconFile size={16} color="#16a34a" />
                      <span>
                        {jobKindLabel(job.jobKind)} raporu{job.sourceFileName ? ` (${job.sourceFileName})` : ''}
                      </span>
                      {!productionMode && (
                        <span style={{ fontSize: '0.6875rem', padding: '1px 6px', borderRadius: '4px', background: '#ecfdf5', color: '#065f46', fontWeight: 600 }}>
                          Gerçek yerel rapor
                        </span>
                      )}
                    </div>
                  </td>
                  <td style={{ color: 'var(--text-main)' }}>
                    {job.clientContext.companyName} / {job.clientContext.projectName}
                  </td>
                  <td style={{ color: 'var(--text-muted)' }}>
                    {job.completedAt ? new Date(job.completedAt).toLocaleString('tr-TR') : new Date(job.createdAt).toLocaleString('tr-TR')}
                  </td>
                  <td>
                    <Badge variant={job.failedItems > 0 ? 'warning' : 'success'}>
                      {job.itemsWritten} / {job.totalItems || job.itemsRead} öğe doğrulandı
                    </Badge>
                  </td>
                  <td style={{ textAlign: 'right' }}>
                    <button
                      className="btn btn-orange"
                      style={{ padding: '4px 10px', fontSize: '0.75rem' }}
                      onClick={() => handleDownloadRealReport(job.jobId)}
                      data-testid={`download-json-${job.jobId}`}
                    >
                      Raporu indir (JSON)
                    </button>
                  </td>
                </tr>
              ))}

              {sampleReports.map((rep) => (
                <tr key={rep.id} data-testid={`report-row-${rep.id}`}>
                  <td style={{ fontWeight: 600 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                      <IconFile size={16} color="var(--brand-orange)" />
                      <span>{rep.title}</span>
                      <span style={{ fontSize: '0.6875rem', padding: '1px 6px', borderRadius: '4px', background: '#f1f5f9', color: '#64748b' }}>
                        Örnek
                      </span>
                    </div>
                  </td>
                  <td style={{ color: 'var(--text-muted)' }}>{rep.jobName}</td>
                  <td style={{ color: 'var(--text-muted)' }}>{rep.date}</td>
                  <td>
                    <Badge variant={rep.failed > 0 ? 'warning' : 'success'}>
                      {rep.transferred} / {rep.itemsCount} başarılı
                    </Badge>
                  </td>
                  <td style={{ textAlign: 'right' }}>
                    <div style={{ display: 'inline-flex', gap: '6px' }}>
                      <button
                        className="btn btn-outline-gray"
                        style={{ padding: '4px 8px', fontSize: '0.75rem' }}
                        onClick={() => handleDownloadSample(rep, 'json')}
                        data-testid={`download-json-${rep.id}`}
                      >
                        JSON
                      </button>
                      <button
                        className="btn btn-outline-gray"
                        style={{ padding: '4px 8px', fontSize: '0.75rem' }}
                        onClick={() => handleDownloadSample(rep, 'csv')}
                        data-testid={`download-csv-${rep.id}`}
                      >
                        CSV
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        )}
      </div>
    </div>
  );
};
