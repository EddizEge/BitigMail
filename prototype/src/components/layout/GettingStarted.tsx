import React from 'react';

interface GettingStartedProps {
  isAdmin: boolean;
  onCreateWorkspace?: () => void;
}

const STEPS: { title: string; text: string }[] = [
  { title: 'Şirket ve proje oluşturun', text: 'Müşteriler ekranında müşteri şirketi ve ilk projesini açın.' },
  { title: 'Projeye posta hesabı ekleyin', text: 'Müşteri sayfasında proje kartındaki "Hesap bağla" ile IMAP, Microsoft veya Google hesabını ekleyin.' },
  { title: 'İşi seçin', text: 'Aktarım ve dönüşüm ekranında yapılacak işlemi ve yönünü seçin.' },
  { title: 'İzleyin ve raporu alın', text: 'İşi İş merkezi\'nden izleyin; bitince Raporlar\'dan raporu indirin.' },
];

/** Hiç müşteri/proje yokken gösterilen ilk kullanım rehberi. */
export const GettingStarted: React.FC<GettingStartedProps> = ({ isAdmin, onCreateWorkspace }) => (
  <section className="getting-started" data-testid="getting-started-card" aria-labelledby="getting-started-title">
    <div className="getting-started-head">
      <h2 id="getting-started-title">Başlarken</h2>
      <p>BitigMail'de her iş bir müşteri projesine bağlıdır. Dört adımda ilk işinizi başlatın.</p>
    </div>
    <ol className="getting-started-steps">
      {STEPS.map((step, index) => (
        <li key={step.title} className={index === 0 ? 'current' : ''}>
          <span className="getting-started-number" aria-hidden="true">{index + 1}</span>
          <div>
            <strong>{step.title}</strong>
            <span>{step.text}</span>
          </div>
        </li>
      ))}
    </ol>
    {isAdmin ? (
      <button className="btn btn-primary-orange" onClick={onCreateWorkspace} data-testid="getting-started-create-btn">
        İlk müşteri ve projeyi oluştur
      </button>
    ) : (
      <p className="getting-started-note">Size henüz bir proje atanmadı. Yöneticinizden bir projeye erişim izni isteyin.</p>
    )}
  </section>
);
