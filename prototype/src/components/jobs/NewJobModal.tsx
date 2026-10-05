import React, { useState } from 'react';
import { Job, JobType } from '../../types';
import { Modal } from '../ui/Modal';
import { Button } from '../ui/Button';

interface NewJobModalProps {
  type: JobType | null;
  isOpen: boolean;
  onClose: () => void;
  onSaveDraftJob: (newJob: Job) => void;
}

export const NewJobModal: React.FC<NewJobModalProps> = ({
  type,
  isOpen,
  onClose,
  onSaveDraftJob,
}) => {
  const [client, setClient] = useState('Yeni Müşteri Ltd.');
  const [title, setTitle] = useState('');
  const [source, setSource] = useState('Eski_Arsiv.ost');
  const [target, setTarget] = useState('Cikti_Arsiv.pst');

  if (!isOpen || !type || type === 'migration') return null;

  const modalConfig = {
    convert: {
      title: 'Dosya Dönüştürme İşi Oluştur',
      defaultTitle: 'OST → PST Dönüşümü',
      sources: ['Eski_Arsiv.ost', 'Kullanici_Gelen.ost', 'Yedek_Mac.olm'],
      targets: ['Cikti_Arsiv.pst', 'Bolunmus_Kutular.pst'],
      badge: 'Dönüştürme Taslağı',
    },
    archive: {
      title: 'Arşivle ve Böl İşi Oluştur',
      defaultTitle: 'Yıllık Posta Bölümleme',
      sources: ['Sirket_Posta_Tam.pst', 'Buyuk_Arsiv_50GB.pst'],
      targets: ['Yillik_2023.pst / 2024.pst', 'Boyut_Sinirli_10GB_Parcalar.pst'],
      badge: 'Bölümleme Taslağı',
    },
    recovery: {
      title: 'Dosyadan Kurtarma İşi Oluştur',
      defaultTitle: 'Bozuk PST İndeks Onarımı',
      sources: ['Hasarli_Sektor_Yedek.pst', 'Acilamayan_Eski_Posta.pst'],
      targets: ['Kurtarilan_Yeni_Arsiv.pst', 'Ayiklanan_Eml_Klasoru'],
      badge: 'Kurtarma Taslağı',
    },
  }[type];

  const handleCreate = () => {
    const jobTitle = title.trim() || modalConfig.defaultTitle;
    const newJob: Job = {
      id: `job-${Date.now()}`,
      title: jobTitle,
      client: client.trim() || 'Örnek Müşteri',
      type,
      source,
      target,
      status: 'draft',
      progressPercent: 0,
      processedCount: 0,
      totalCount: 150,
      transferredCount: 0,
      skippedCount: 0,
      failedCount: 0,
      pendingCount: 150,
      recentEvents: [
        {
          time: new Date().toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' }),
          text: 'Taslak iş kaydı oluşturuldu (Sentetik prototip)',
        },
      ],
    };
    onSaveDraftJob(newJob);
    onClose();
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title={modalConfig.title}
      subtitle="Örnek veriler · Tasarım taslağı"
      footer={
        <>
          <Button variant="outline-gray" onClick={onClose}>
            İptal
          </Button>
          <Button variant="primary" onClick={handleCreate} data-testid="save-draft-job-btn">
            Taslak Olarak Kaydet
          </Button>
        </>
      }
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
        <div className="alert-banner alert-warning" style={{ fontSize: '0.8125rem' }}>
          <div>
            <strong>Sentetik Prototip Uyarısı:</strong> Bu iş türü için P1 aşamasında yalnız taslak
            arayüz ve iş merkezi entegrasyonu sunulmaktadır. Gerçek dönüştürme/kurtarma motoru P4
            aşamasında uygulanacaktır.
          </div>
        </div>

        <div>
          <label className="plan-label">Müşteri Adı</label>
          <input
            type="text"
            className="text-input"
            value={client}
            onChange={(e) => setClient(e.target.value)}
            style={{ marginTop: '4px' }}
            data-testid="draft-job-client-input"
          />
        </div>

        <div>
          <label className="plan-label">İş Başlığı</label>
          <input
            type="text"
            className="text-input"
            placeholder={modalConfig.defaultTitle}
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            style={{ marginTop: '4px' }}
            data-testid="draft-job-title-input"
          />
        </div>

        <div>
          <label className="plan-label">Örnek Kaynak Dosya / Biçim</label>
          <select
            className="select-input"
            value={source}
            onChange={(e) => setSource(e.target.value)}
            style={{ width: '100%', marginTop: '4px' }}
            data-testid="draft-job-source-select"
          >
            {modalConfig.sources.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label className="plan-label">Örnek Hedef Çıktı / Biçim</label>
          <select
            className="select-input"
            value={target}
            onChange={(e) => setTarget(e.target.value)}
            style={{ width: '100%', marginTop: '4px' }}
            data-testid="draft-job-target-select"
          >
            {modalConfig.targets.map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </select>
        </div>
      </div>
    </Modal>
  );
};
