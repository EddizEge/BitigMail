import React, { useState } from 'react';
import { Modal } from '../ui/Modal';
import { Button } from '../ui/Button';

interface FolderMappingModalProps {
  isOpen: boolean;
  onClose: () => void;
  sourceFolder: string;
  targetFolder: string;
  onSaveMapping: (newTargetFolder: string) => void;
}

export const FolderMappingModal: React.FC<FolderMappingModalProps> = ({
  isOpen,
  onClose,
  sourceFolder,
  targetFolder,
  onSaveMapping,
}) => {
  const [selectedTarget, setSelectedTarget] = useState(targetFolder);

  const handleSave = () => {
    onSaveMapping(selectedTarget);
    onClose();
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title="Klasör Eşlemesini Düzenle"
      subtitle="Kaynak posta klasörünün hedefteki karşılığını belirleyin"
      footer={
        <>
          <Button variant="outline-gray" onClick={onClose}>
            İptal
          </Button>
          <Button variant="primary" onClick={handleSave} data-testid="save-mapping-btn">
            Eşlemeyi Kaydet
          </Button>
        </>
      }
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
        <p style={{ fontSize: '0.875rem', color: 'var(--text-muted)' }}>
          Kaynak klasördeki iletiler aktarım sırasında hedef posta kutusunda seçtiğiniz klasör
          altında toplanacaktır.
        </p>

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 24px 1fr', alignItems: 'center', gap: '8px' }}>
          <div>
            <label className="plan-label">Kaynak Klasör</label>
            <input
              type="text"
              className="text-input"
              value={sourceFolder}
              disabled
              style={{ marginTop: '4px', backgroundColor: 'var(--bg-subtle)' }}
            />
          </div>

          <div style={{ textAlign: 'center', color: '#64748b', paddingTop: '20px' }}>→</div>

          <div>
            <label className="plan-label">Hedef Klasör</label>
            <select
              className="select-input"
              value={selectedTarget}
              onChange={(e) => setSelectedTarget(e.target.value)}
              style={{ width: '100%', marginTop: '4px' }}
              data-testid="target-folder-mapping-select"
            >
              <option value="Gelen kutusu">Gelen kutusu</option>
              <option value="Arşiv / 2024">Arşiv / 2024</option>
              <option value="Aktarılanlar">Aktarılanlar</option>
              <option value="Gelen Kutusu (Eski)">Gelen Kutusu (Eski)</option>
            </select>
          </div>
        </div>
      </div>
    </Modal>
  );
};
