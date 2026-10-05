# BitigMail — Yerel çalışma alanı

TASK-013 ile **Aktarım ve dönüşüm → Dönüştürme → EML / MBOX → PST** eklendi. Çoklu EML, EML klasör ağacı ve tek mboxrd dosyası; klasör/tarih önizlemesi, yeni hedef, sabit müşteri/proje bilgisi, kalıcı iş ve deneme farkları raporu çalışır. Sonuç PST mevcut Arşivleme ekranına kaynak olabilir. [Kapsam](../docs/MIME_IMPORT_WORKFLOW.md), [bağımsız doğrulama ve sınırlar](../docs/MIME_IMPORT_VALIDATION.md).

TASK-013 doğrulaması: 56 arayüz birim testi, typecheck/lint/build, üç boyutta gerçek EML/MBOX/filtreli ağaç akışı; rapor indirme, geçmişten açma, eski önizleme yanıtının reddi ve müşteri değişiminde kapsamın temizlenmesi geçti. Önceki OST/arşiv mock regresyonları 30/30 (24 ilk koşu + açıkça simüle edilen servis kesintisi için 6 hedefli tekrar). Gerçek UI testini çalıştırmadan önce ayrı TestingHost'u 6175'te başlatın: `npm run test:e2e -- testinghost-real-mime.spec.ts`. Windows'un gerçek dosya pencereleri otomatik kullanılmaz.

TASK-010 ile **Aktarım ve dönüşüm → Dönüştürme** ekranı gerçek OST dosyasını analiz eder ve yeni PST oluşturur. Müşteri/proje bilgisi, gerçek iş ilerlemesi, çıktı konumu ve rapor kalıcıdır. Arayüz 5173, Windows dosya seçicisini kullanan yerel hizmet 6174 portundadır. İkisini başlatma adımları: [ana çalıştırma kılavuzu](../README.md). Kanıtlar ve sınırlar: [yerel OST doğrulaması](../docs/LOCAL_OST_VALIDATION.md).

TASK-011 bu akışa çoklu klasör seçimi, Türkiye saatine göre dahil tarih aralığı, gerçek ileti/ek önizlemesi ve seçilen/kapsam dışı/dönüştürülen sayılarıyla kalıcı filtre raporu ekler. [Filtreli akış sözleşmesi](../docs/FILTERED_OST_WORKFLOW.md), [doğrulama kaydı](../docs/FILTERED_OST_VALIDATION.md).

TASK-012 ile **Aktarım ve dönüşüm → Arşivleme** gerçek PST/OST kaynaklarını yıl veya gerçek dosya boyutu sınırına göre yeni PST parçalarına böler. Klasör/tarih seçimi, yeni hedef arşiv klasörü, doğrulanmış parça listesi ve kalıcı rapor çalışır. [Bölümleme sözleşmesi](../docs/PST_SPLIT_WORKFLOW.md), [doğrulama ve sınırlar](../docs/PST_SPLIT_VALIDATION.md).

Gerçek IMAP hesap dizini ve iki hesap arasında filtreli, dondurulmuş önizlemeli, raporlu ve kesintiden devam edebilen kopyalama çalışır. Kurumsal Microsoft 365 ve kişisel Outlook.com/Hotmail delegated OAuth ekranları ile yerel motor entegrasyonu hazırdır; kurumsal pilot **LIVE_PILOT_PENDING**, kişisel pilot **LIVE_PERSONAL_PILOT_PENDING** durumundadır. Google sağlayıcı OAuth'u, birleşik arşiv araması ve kurtarma ekranlarının kalan bölümleri P1 örnek verilerini kullanır. Gerçek OST akışının güncel sözleşmesi [LOCAL_OST_WORKFLOW.md](../docs/LOCAL_OST_WORKFLOW.md), IMAP sözleşmesi [IMAP_TRANSFER_WORKFLOW.md](../docs/IMAP_TRANSFER_WORKFLOW.md), kurumsal Microsoft sözleşmesi [MICROSOFT365_CONNECTION_WORKFLOW.md](../docs/MICROSOFT365_CONNECTION_WORKFLOW.md), kişisel Outlook sözleşmesi [OUTLOOK_PERSONAL_WORKFLOW.md](../docs/OUTLOOK_PERSONAL_WORKFLOW.md) belgesindedir.

---

## 1. Mimari ve Dizin Yapısı

Proje **React 18 + TypeScript + Vite** kullanılarak geliştirilmiştir.

```
prototype/
├── public/                 # Statik varlıklar (favicon.svg, bitigmail-mark-v1.png)
├── src/
│   ├── components/         # Arayüz bileşenleri
│   │   ├── layout/         # Header (5 sekmeli ana navigasyon), StatusBar
│   │   ├── clients/        # Müşteriler dizini, müşteri kartları, detay, proje ve kaynak yönetimi
│   │   ├── transfer/       # Aktarım ve dönüşüm sekmesi (Bağlam çubuğu, 4 operasyon, simülasyon)
│   │   ├── jobs/           # İş merkezi, iş tablosu, ilerleme takibi, bağlamlı iş açma
│   │   ├── workspace/      # Kaynak ağacı, ileti tablosu, filtreler, transfer plan paneli
│   │   ├── preflight/      # Ön kontrol listesi, engel çözümü, sıfır kapsam guard'ı
│   │   ├── search/         # Çoklu konum hiyerarşi ağacı, birleşik arama ve konum rozetleri
│   │   ├── reports/        # Sentetik rapor listesi
│   │   ├── settings/       # Demo tercihleri ve veri sıfırlama çekmecesi
│   │   └── ui/             # İkonlar, rozetler, butonlar, modallar, BitigMark
│   ├── data/               # Deterministik sentetik veriler
│   │   ├── sampleSources.ts   # Şirketler, projeler ve bağlı kaynaklar (IMAP, Exchange, PST, MBOX)
│   │   ├── sampleMessages.ts  # Kaynak bazlı iletiler (getMessagesBySourceId, getMessagesForSourceIds)
│   │   └── sampleJobs.ts      # Bağlam etiketli 6 başlangıç işi
│   ├── state/              # İş kuralları ve durum yönetimi
│   │   ├── storage.ts         # localStorage kalıcılığı (şirketler, projeler, kaynaklar, planHash)
│   │   ├── preflightRules.ts  # Ön kontrol, filtreleme, sıfır-kapsam ve bayatlık kuralları
│   │   ├── useSimulation.ts   # Deterministik aktarım motoru ve CSV/JSON üretici
│   │   └── useAppState.ts     # Merkezi durum kancası ve bağlam aktarımı (startTransferWithContext)
│   ├── styles/             # Tasarım belirteçleri ve stil kuralları (index.css)
│   ├── tests/              # Kural ve birim testleri (rules.test.ts)
│   ├── App.tsx             # Ana uygulama kabuğu (5 sekme yönlendirmesi)
│   └── main.tsx            # React DOM montaj noktası
├── tests/
│   └── e2e/
│       ├── iteration-2.spec.ts    # İterasyon 2 çoklu müşteri, operasyonlar ve birleşik arama kabul testleri
│       ├── migration-flow.spec.ts # Playwright uçtan uca akış (Müşteriler, Aktarım, Arama, Simülasyon)
│       └── desktop-layout.spec.ts # Masaüstü geometri ve bağımsız kaydırma testi
├── DESIGN_SYSTEM.md        # Renk, tipografi ve bileşen belirteçleri
├── VISUAL_QA.md            # İterasyon 1 ve İterasyon 2 görsel QA kayıtları
├── package.json            # Script ve bağımlılık tanımları
├── tsconfig.json           # TypeScript yapılandırması
├── vite.config.ts          # Vite yerel sunucu (127.0.0.1:5173) ayarı
└── playwright.config.ts    # Playwright test yapılandırması
```

---

## 2. İterasyon 2 Ana Yenilikleri ve Veri Sözleşmesi

1. **Müşteriler (Company Directory):**
   - Uygulama açılışında tek bir posta kutusuna sıkışmak yerine Müşteriler dizini (`clients`) karşılar.
   - Şirket kartları, proje listeleri ve bağlı kaynaklar görüntülenir.
   - Yeni şirket, yeni proje ve yeni kaynak ekleme modalları kalıcı olarak `localStorage` üzerinde saklanır.
   - "İşlem başlat" butonu bağlamı (`companyId`, `projectId`, `sourceId`) doğrudan Aktarım sekmesine taşır.

2. **Ayrı Aktarım ve Dönüşüm Sekmesi (`transfers`):**
   - Üst bağlam çubuğu ile Müşteri, Proje, Kaynak, Hedef ve Operasyon türü (Taşıma, Dönüştürme, Arşivleme, Kurtarma) dinamik seçilebilir.
   - Taşıma seçildiğinde kanıtlanmış uçtan uca çalışma alanı, ön kontrol ve simülasyon akışı korunur.

3. **İş Merkezi Bağlam Taşıma:**
   - İş merkezinde yer alan işlerin her biri (`job-1`..`job-6`) şirket, proje ve kaynak ID'lerine bağlıdır.
   - "İşi aç" veya "İş ayrıntıları" tıklandığında seçili işin bağlamı doğrudan Aktarım sekmesine yüklenir.

4. **Kaynak Bazlı İleti Kapsamı (`sourceId` Strict Scoping):**
   - İletiler kesin olarak `sourceId` ile sorgulanır (`getMessagesBySourceId(sourceId)`).
   - Sahte veri veya `ALL_SAMPLE_MESSAGES` geri dönüşü (fallback) yoktur.
   - Boş veya yeni eklenen kaynaklar tam olarak **0 ileti** gösterir ve önizleme paneli boş kalır.
   - Boş kaynakta ön kontrol çalıştırıldığında sıfır kapsam engelleyicisi (`scope_guard`) devreye girer ve aktarımı engeller.

5. **Plan Kimliği ve Dondurulmuş Snapshot:**
   - Plan özeti (`planHash`) artık `companyId`, `projectId` ve `sourceId` değişikliklerini içerir; bunlardan biri değiştiğinde taslak ön kontrol geçersizleşir (`isStale: true`).
   - Yürüyen veya duraklatılmış aktarımlarda ise `activeRun.planSnapshot` dondurulmuş olarak korunur.

6. **Arşiv ve Arama (Çoklu Konum Hiyerarşi Ağacı):**
   - Şirket → Proje → Posta Kutusu / Arşiv Dosyası hiyerarşik seçim ağacı.
   - "Tümünü seç", "Temizle" ve kısmi seçim (indeterminate checkbox) desteği.
   - Birden fazla kaynak seçildiğinde sonuçlar tekilleştirilir; konum rozetleri (`Şirket > Proje > Kaynak`) gösterilir.
   - Hiçbir konum seçilmediğinde yönlendirici bilgilendirme görüntülenir.
   - Kapsam dışına çıkan iletinin önizlemesi otomatik temizlenir.

---

## 3. Çalıştırma Komutları

### 3.1. Geliştirme Sunucusu
```powershell
npm run dev
```
Sunucu `http://127.0.0.1:5173` adresinde başlar.

### 3.2. Tip Kontrolü
```powershell
npm run typecheck
```

### 3.3. Derleme (Production Build)
```powershell
npm run build
```

### 3.4. Birim ve Kural Testleri
```powershell
npm run test
```

### 3.5. Headless Uçtan Uca Playwright Testleri
```powershell
npm run test:e2e
```
