import React, { useState } from 'react';
import { Modal } from '../ui/Modal';
import { Button } from '../ui/Button';
import { IconFile, IconMail } from '../ui/Icons';

export interface AddSourceModalProps {
  isOpen: boolean;
  onClose: () => void;
  projectName?: string;
  onAddSource: (name: string, kind: 'mailbox' | 'archive_file', accountOrFileName: string) => void;
}

export const AddSourceModal: React.FC<AddSourceModalProps> = ({
  isOpen,
  onClose,
  projectName,
  onAddSource,
}) => {
  const [name, setName] = useState('');
  const [kind, setKind] = useState<'mailbox' | 'archive_file'>('mailbox');
  const [accountOrFileName, setAccountOrFileName] = useState('');

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim() || !accountOrFileName.trim()) return;
    onAddSource(name.trim(), kind, accountOrFileName.trim());
    setName('');
    setAccountOrFileName('');
    onClose();
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title="Projeye Yeni Kaynak Ekle"
      subtitle={projectName ? `${projectName} projesine bağlı veri kaynağı ekleyin` : 'Projeye bağlı veri kaynağı ekleyin'}
      size="sm"
    >
      <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
        <div className="alert-banner alert-warning" style={{ fontSize: '0.8125rem' }}>
          <div>
            <strong>Sentetik Gösterim:</strong> Gerçek şifre, OAuth veya yerel dosya tarayıcısı
            açılmaz. Eklenen kaynak 0 iletiyle başlatılır ve paylaşılan veri havuzunda saklanır.
          </div>
        </div>

        <div>
          <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '6px' }}>
            Kaynak Türü
          </label>
          <div style={{ display: 'flex', gap: '10px' }}>
            <button
              type="button"
              className={`btn ${kind === 'mailbox' ? 'btn-orange' : 'btn-outline-gray'}`}
              style={{ flex: 1 }}
              onClick={() => setKind('mailbox')}
              data-testid="source-kind-mailbox-btn"
            >
              <IconMail size={16} />
              <span>Posta Kutusu</span>
            </button>
            <button
              type="button"
              className={`btn ${kind === 'archive_file' ? 'btn-orange' : 'btn-outline-gray'}`}
              style={{ flex: 1 }}
              onClick={() => setKind('archive_file')}
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
            placeholder={kind === 'mailbox' ? 'Örn: Finans Posta Kutusu' : 'Örn: 2023_Eski_Arsiv.pst'}
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
            data-testid="new-source-name-input"
          />
        </div>

        <div>
          <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '6px' }}>
            {kind === 'mailbox' ? 'E-posta Hesabı *' : 'Dosya Adı / Konumu *'}
          </label>
          <input
            type="text"
            className="text-input"
            placeholder={kind === 'mailbox' ? 'finans@sirket.example' : 'arsiv_yedek.pst'}
            value={accountOrFileName}
            onChange={(e) => setAccountOrFileName(e.target.value)}
            required
            data-testid="new-source-account-input"
          />
        </div>

        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '6px' }}>
          <Button variant="outline-gray" type="button" onClick={onClose}>
            İptal
          </Button>
          <Button
            variant="primary"
            type="submit"
            disabled={!name.trim() || !accountOrFileName.trim()}
            data-testid="submit-new-source-btn"
          >
            Kaydet
          </Button>
        </div>
      </form>
    </Modal>
  );
};
