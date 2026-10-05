# BitigMail — Görsel QA Raporu

Güncel teslim: TASK-005 DONE. Derleme/tip/lint ve 19 birim testi geçti. Yeni çok müşterili kullanım testleri 24/24; mevcut taşıma/yerleşim kontrolleri 22 geçti, masaüstüne özgü iki kontrol mobilde kapsam gereği atlandı. Aşağıdaki ilk karşılaştırmalar TASK-004 geçmişidir; güncel yeni yüzeyler “İterasyon 2” bölümündedir. Astra temiz ilk açılışı, ortak kaynak ağacını, kaynak bilgisini ve 390 ekranında konum → sorgu → sonuç → önizleme erişimini bağımsız headless kontrol etti; son kompakt masaüstü ve arama renderlarını inceledi.

## Kanıt seti ve yöntem

Kabul edilen referanslar `../design/concepts/bitigmail-workspace-v1.png`, `../design/concepts/bitigmail-job-center-v1.png`, `../design/concepts/bitigmail-preflight-v1.png`; marka varlığı `../design/assets/bitigmail-mark-v1.png` dosyalarıdır.

Render'lar headless Playwright ile, sayfa en üste kaydırıldıktan sonra `fullPage: false` viewport çekimi olarak üretilir. TASK-004 masaüstü/tarayıcı UI kontrolünü açıkça yasakladığı için etkileşimli tarayıcı eklentisi yerine terminal tabanlı headless Playwright kullanılmıştır.

| Proje | Viewport | Çıktı eki |
|---|---:|---|
| desktop-reference | 1660×948 | `desktop-reference` |
| desktop-compact | 1366×768 | `desktop-compact` |
| mobile-narrow | 390×844 | `mobile-narrow` |

Üç kanonik teslim: `qa/workspace-desktop-reference.png`, `qa/job-center-desktop-reference.png`, `qa/preflight-desktop-reference.png`. Diğer altı render responsive regresyon kanıtıdır.

## Somut karşılaştırmalar

| Boyut | Referanstaki yön | Uygulama/render karşılığı | Sonuç |
|---|---|---|---|
| Palet | Beyaz yüzeyler, sarı seçim/vurgu, turuncu birincil aksiyon, koyu metin | Son entegrasyonda antrasit `#292824`, turuncu `#e97c24` ve sarı `#f3c544`/`#fef3c7` kabul edilen konsepte göre düzeltildi. | Uyumlu; birebir piksel eşliği iddia edilmez. |
| Çalışma alanı yapısı | Solda kaynaklar, ortada ileti listesi ve önizleme, sağda aktarım planı | Masaüstünde üç panel aynı bilgi hiyerarşisiyle eşzamanlı görünür. | Uyumlu. |
| Liste yoğunluğu | Yoğun posta tablosu; seçili satır ve bağlı önizleme | 248 sentetik ileti, seçili satır ve ayrıntı önizlemesi sunulur; liste kendi sınırı içinde kayar, önizleme ilk viewport'ta sabit kalır. | 1660×948 ve 1366×768 bounding-rect testiyle doğrulandı. |
| İş merkezi yapısı | Solda işler/filtreler, sağda seçili iş ayrıntıları ve metrikler | Altı deterministik iş ve öğeden türetilen 104/248 dağılımı, masaüstünde yaklaşık 65/35 kolon oranında gösterilir. | Son 1660×948 render'ında uyumlu. |
| Ön kontrol akışı | Dört adım, sol kontrol/engel içeriği, sağ özet ve erişilebilir CTA | 38 MB engeli, atla-ve-raporla kararı, özet ve başlatma aksiyonu aynı karar sırasını korur; gövde ile alt eylem şeridi ayrıdır. | Alt şerit iki masaüstü viewport'unda tamamen görünür. |
| Tipografi ve hiyerarşi | Belirgin sayfa/panel başlıkları, daha küçük tablo ve yardımcı metin | Başlık, panel, rozet ve tablo ölçekleri Segoe UI sistem ailesinde katmanlanır. | Yakın eşleşme; raster referans ölçümü değildir. |
| Aralık ve yoğunluk | Masaüstü odaklı sık fakat okunaklı operasyon düzeni | Sabit panel sınırları, ince ayırıcılar ve kompakt kontroller uygulanır. | Referans karakteri korunur. |
| Responsive davranış | Kabul edilmiş ayrı bir mobil konsept yok | 390×844'te paneller akışta istiflenir, tablo 360 px içinde kayar, sekmeler yatay kayar ve CTA'lar header altında kalmaz. | Bilinçli uyarlama. |

## Metin farkları ve düzeltilen sapmalar

- Referanstaki temsili toplam boyut yerine 248 sentetik iletiden hesaplanan tutarlı toplam gösterilir.
- Ön kontroldeki bağlantı, ürünün turuncu vurgu sistemiyle uyumlu tutulmuştur.
- İş merkezi ekran görüntüsü, dört “Yeni iş” seçeneği test edildikten sonra menü kapalıyken alınır; başlık örtülmez.
- Çalışma alanındaki 248 satır artık belgeyi on binlerce piksel uzatmaz; liste bağımsız kaydırılır ve önizleme/plan görünür kalır.
- Sticky header için içerik ve tıklama hedeflerine üst boşluk verildi; ön kontrol uyarısı ve mobil kontroller örtülmez.
- Yenileme sırasında aktif aktarım ve transfer görünümü saklanır; görsel akış kopmaz.
- Son masaüstü entegrasyonunda çalışma alanı `100dvh` ile sınırlandı; plan seçenekleri kaydırılabilir gövdede, birincil aksiyon ayrı footer'da tutuldu.
- Son Playwright sonucu: 16 geçti; yalnızca masaüstüne özel iki geometri testi mobil projede tasarım gereği atlandı. Mobil ana akışın dört testi geçti.
- Son kolon oranı entegrasyonundan sonra İş Merkezi hedefli E2E testi üç viewport'ta 3/3 geçti ve ilgili render'lar yenilendi; production build tekrar geçti.

## İterasyon 2 Görsel ve Fonksiyonel Doğrulama (TASK-005)

### Yeni Görsel Teslimler (3 Viewport × 3 Yüzey = 9 Render)
Playwright E2E testleri (`iteration-2.spec.ts`) ile her üç viewport (`desktop-reference`, `desktop-compact`, `mobile-narrow`) için tepeye kaydırma sonrası (`fullPage: false`) üretilen ekran görüntüleri:
- `qa/customers-${projectName}.png`: Müşteriler şirket dizini ve filtreleme yüzeyi.
- `qa/operations-${projectName}.png`: Aktarım ve dönüşüm sekmesi, bağlam çubuğu (Müşteri/Proje/Kaynak/Hedef) ve 4 operasyon türü seçici.
- `qa/multi-search-${projectName}.png`: Arşiv ve arama hiyerarşik konum seçim ağacı, kısmi seçim ve birleşik arama sonuçları.

| Boyut / Ekran | İterasyon 2 Yönü | Uygulama Karşılığı | Durum |
|---|---|---|---|
| Üst Gezinti (Header) | 5 ayrı ana sekme (`jobs`, `clients`, `transfers`, `search`, `reports`) | Sarı alt çizgi vurgusu (`#f3c544`), 56px sabit header | Uyumlu |
| Müşteriler (Clients) | Çoklu müşteri kartları, şirket kodu rozeti, proje/kaynak sayaçları | `ClientDirectoryView` ızgara düzeni, şirket içi proje/kaynak detayları | Uyumlu |
| Aktarım & Dönüşüm | Ayrı sekme altında bağlam çubuğu (Müşteri, Proje, Kaynak, Hedef) ve 4 operasyon | `TransfersTabView` üst çubuk, Taşıma/Dönüştürme/Arşiv/Kurtarma düğmeleri | Uyumlu |
| Arşiv ve Arama | Sol panelde Şirket → Proje → Kaynak hiyerarşik ağacı, sağda birleşik arama | `ArchiveSearchView` hiyerarşi ağacı, kısmi seçim, tekilleştirilmiş arama ve konum rozetleri | Uyumlu |
| Sıfır Kapsam Koruması | Yeni/boş kaynakta 0 ileti, boş önizleme, ön kontrolde engel | Boş kaynakta `empty-messages-placeholder`, devre dışı CTA ve görünür `scope-guard-helper` uyarısı | Uyumlu |
| Bağlam Taşıma | Müşteri dizininden veya İş merkezinden aktarıma geçişte ID'lerin yüklenmesi | `startTransferWithContext` ile `companyId`, `projectId`, `sourceId` aktarımı | Uyumlu |
| Mobil Arşiv & Arama (390×844) | İstiflenmiş düzen, konum ağacı aç/kapa çekmecesi, tam genişlikte erişilebilir sonuç ve önizleme | `.archive-search-layout` dikey istifleme, `mobile-location-toggle-btn`, turuncu `accent-color` form girdileri | Uyumlu |
| İleti Önizleme Menşe Rozeti | Şirket > Proje > Kaynak (hesap/dosya) menşe satırı unclip ve okunurluk | `flex-shrink: 0`, `min-height: 24px`, `line-height: 1.4` ile tam okunabilir rozet (bounding height >= 20px) | Uyumlu |

## Sınırlamalar

- Tüm posta, hesap ve iş verileri sentetiktir; gerçek kimlik doğrulama veya posta motoru yoktur.
- Kabul edilen marka PNG'si beyaz zeminli raster varlıktır; şeffaf vektör gibi davranması beklenmez.
- Mobil görünüm, masaüstü referanslardan türetilmiş erişilebilir bir uyarlamadır; kabul edilmiş mobil mockup bulunmadığından görsel eşdeğerlik iddiası yoktur.
- Sonuç işlevsel ve görsel regresyon kanıtı sağlar; piksel-perfect eşleşme iddia edilmez.


