import React, { useEffect, useState } from 'react';
import { IconRefreshCw, IconX } from '../ui/Icons';
import { Button } from '../ui/Button';
import { localEngineClient } from '../../api/localEngineClient';
import type { AsposeSdkStatus } from '../../types/localEngine';
import { APP_VERSION } from '../layout/StatusBar';

interface SettingsDrawerProps {
  isOpen: boolean;
  onClose: () => void;
  onResetDemoData: () => void;
  authenticated?: boolean;
}

export const SettingsDrawer: React.FC<SettingsDrawerProps> = ({
  isOpen,
  onClose,
  onResetDemoData,
  authenticated = false,
}) => {
  const [sdk,setSdk]=useState<AsposeSdkStatus|null>(null);
  const [restartRequired,setRestartRequired]=useState(false);
  useEffect(()=>{if(isOpen)localEngineClient.getSdkStatus().then(setSdk).catch(()=>setSdk(null))},[isOpen]);
  if (!isOpen) return null;

  return (
    <div className="modal-backdrop" onClick={onClose} data-testid="settings-drawer-backdrop">
      <div
        className="drawer-dialog"
        onClick={(e) => e.stopPropagation()}
        data-testid="settings-drawer-dialog"
      >
        <div
          style={{
            padding: '16px 20px',
            borderBottom: '1px solid var(--border-light)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
          }}
        >
          <div>
            <h3 style={{ fontSize: '1.125rem', fontWeight: 700, color: 'var(--text-main)' }}>
              {authenticated ? 'Ayarlar' : 'Prototip ayarları'}
            </h3>
            <p style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
              {authenticated ? `Lisans durumu ve uygulama bilgisi · BitigMail ${APP_VERSION}` : 'Yerel demo tercihleri ve veri sıfırlama'}
            </p>
          </div>
          <button
            onClick={onClose}
            style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--text-muted)' }}
            aria-label="Kapat"
            data-testid="settings-drawer-close-btn"
          >
            <IconX size={18} />
          </button>
        </div>

        <div style={{ padding: '20px', flex: 1, display: 'flex', flexDirection: 'column', gap: '20px', overflowY: 'auto' }}>
          <div data-testid="sdk-license-status"><span className="plan-label">Posta biçimi kitaplığı (Aspose) ve lisans</span><p>{sdk ? `${sdk.licenseState==='unlicensed'?'Lisans yapılandırılmadı':sdk.licenseState==='licensed_loaded'?'Lisans başlangıçta yüklendi':'Lisans yapılandırma hatası'} · ${sdk.initializationState==='initialized'?'SDK başlangıcı hazır':'SDK başlangıç hatası'}` : 'Durum alınamadı. Uygulamayı kapatıp yeniden açın.'}</p><p style={{fontSize:'0.8125rem',color:'var(--text-muted)'}}>Lisans dosyası yalnız bu Windows kullanıcısına bağlı korumalı yerel depoda tutulur. Değişiklik çalışan işlere uygulanmaz; uygulamanın yeniden başlatılması gerekir.</p><p>{sdk?.qualification==='LICENSED_OUTPUT_ACCEPTANCE_PENDING'?'Lisanslı çıktı kabulü henüz yapılmadı.':sdk?.qualification}</p><Button variant="outline-gray" onClick={async()=>{const r=await localEngineClient.selectSdkLicense();setRestartRequired(r.restartRequired)}}>Lisans dosyası seç</Button>{restartRequired&&<p role="alert">Lisans yapılandırması kaydedildi. Uygulamayı yeniden başlatın.</p>}</div>
          {!authenticated && <div>
            <span className="plan-label">Sentetik veri sözleşmesi</span>
            <p style={{ fontSize: '0.8125rem', color: 'var(--text-muted)', marginTop: '4px' }}>
              Demo kayıtları sentetiktir. Gerçek yerel motor akışları, kullanıcı tarafından yapılandırılan posta hesaplarına ve dosyalara yalnız açık işlemler sırasında bağlanabilir.
            </p>
          </div>}

          {!authenticated && <div>
            <span className="plan-label">Örnek hedef sınırı</span>
            <p style={{ fontSize: '0.8125rem', color: 'var(--text-muted)', marginTop: '4px' }}>
              Microsoft 365 hedefi için temsili tekil ileti sınırı <strong>35 MB</strong> olarak
              ayarlanmıştır. 38 MB boyutundaki örnek ileti bu kuralı doğrulamak için ön kontrolde
              engellenir.
            </p>
          </div>}

          {!authenticated && <div style={{ borderTop: '1px solid var(--border-light)', paddingTop: '16px' }}>
            <span className="plan-label">Yerel bellek ve sıfırlama</span>
            <p style={{ fontSize: '0.8125rem', color: 'var(--text-muted)', marginTop: '4px', marginBottom: '12px' }}>
              Tarayıcı yerel depolamasında (localStorage) saklanan plan, simülasyon ve iş durumlarını
              varsayılan fabrika değerlerine geri döndürür.
            </p>

            <Button
              variant="outline-orange"
              onClick={onResetDemoData}
              data-testid="reset-demo-storage-btn"
              style={{ width: '100%' }}
            >
              <IconRefreshCw size={16} />
              <span>Demo verilerini sıfırla</span>
            </Button>
          </div>}
        </div>

        <div
          style={{
            padding: '14px 20px',
            borderTop: '1px solid var(--border-light)',
            background: 'var(--bg-subtle)',
            display: 'flex',
            justifyContent: 'flex-end',
          }}
        >
          <Button variant="outline-gray" onClick={onClose} data-testid="settings-close-bottom-btn">
            Kapat
          </Button>
        </div>
      </div>
    </div>
  );
};
