# BitigMail — P1 prototip yapım sözleşmesi

2026-09-12. Kullanıcı tıklanabilir prototipi başlatmayı istedi. Bu belge TASK-004 kapsamını belirler; gerçek posta motoru değildir.

Güncelleme: TASK-004 tesliminden sonraki kullanıcı kararıyla gezinme ve çok müşterili kaynak seçimi [PROTOTYPE_ITERATION_2.md](PROTOTYPE_ITERATION_2.md) kapsamında değişir. Müşteriler artık şirket/proje/kaynak dizinidir; posta çalışma ekranı ayrı Aktarım ve dönüşüm sekmesindedir. Aşağıdaki ilk teslim sözleşmesinin buna aykırı gezinme ifadeleri tarihsel kayıttır; simülasyon ve doğrulama ölçütleri geçerlidir.

## Teknik sınır

`prototype/` altında React + TypeScript + Vite. Mevcut projede başka uygulama yok. Düşük sayıda standart bağımlılık, kilit dosyası, tekrar çalıştırılabilir komutlar. Bileşenler, örnek veriler, iş durumu/kuralları, stiller ve testler ayrı dosyalarda. Yerel sunucu yalnız loopback üzerinde. Ağ üzerinden posta erişimi, OAuth, gerçek dosya okuma veya sunucuya içerik gönderimi yok. Üretim masaüstü/motor teknolojisi seçilmez.

## Tasarım sözleşmesi

Kaynaklar `design/concepts/bitigmail-workspace-v1.png`, `bitigmail-job-center-v1.png`, `bitigmail-preflight-v1.png`, marka görseli `bitigmail-brand-v1.png`. Uygulama öncesinde Gemini bu görselleri dosyadan incelemeli. Arayüz resim olarak gösterilmez; metin, tablo, alanlar ve düğmeler gerçek bileşenlerdir. Logo ayrı görsel varlığıdır; `design/assets/bitigmail-mark-v1.png` kullanılacak.

Saf beyaz yüzey, turuncu eylem, sarı etkin durum, koyu metin. Görsellerden türetilen tasarım belirteçleri ve bileşen ailesi `prototype/DESIGN_SYSTEM.md` içinde kaydedilir. Sistem fontu Segoe UI kullanılabilir; başka ağ fontuna ihtiyaç yok. Başlık/menü/tablo/düğme tipografi ölçekleri ayrı tanımlanır. Mevcut görsellerin ana navigasyonu ve üç/duble panel düzeni korunur. Pazarlama bölümleri, dekoratif istatistik kartları veya başka tema eklenmez.

İzin verilen sapmalar: 248 ileti sayısı ve toplam boyut aynı örnek veri kümesinden hesaplanır, görseldeki 1,8 GB'a zorla eşitlenmez; temsili yeni işler aynı tasarım ailesinde eklenebilir; mavi bağlantı koyu metin/turuncu marka uygulamasına çevrilir; küçük ekran için panel/tablo kaydırma ve ayrıntı çekmecesi; prototip etiketi. Yeni modal/çekmeceler yalnız aşağıdaki zorunlu akışları tamamlar, ayrıca pazarlama veya kurumsal özellik icat edilmez.

## Örnek veri ve gezinme

- Deterministik, yalnız sentetik ileti verisi. En az 248 adet 2024 Gelen kutusu iletisi; başka tarih/klasör ve ekli/eksiz örnekler filtreleri anlamlı kılar. Tekrarlı 3 öğe ve ayrı 1 büyük öğe ön kontrol senaryosu. Büyük öğe 38 MB, seçili örnek hedef ayarı 35 MB; bu bir sağlayıcı standardı diye sunulmaz.
- İş merkezi ana giriş. Altı örnek iş ve seçilebilir ayrıntı; arama/durum sekmeleri çalışır. Varsayılan örnek çalıştırma 100 aktarılan + 3 atlanan + 1 son denemesi başarısız + 144 bekleyen = 248. Yeni iş akışı başlatılınca kendi sayımını kullanır; ilk veriler sabit başarı iddiası değildir.
- Müşteriler sekmesi proje ekranına; Arşiv ve arama aynı örnek kaynaklar üzerinde arama görünümüne; Raporlar örnek sonuç listesine gider. Ayarlar yerel demo tercihi/sıfırlama çekmecesi açar. Görünen navigasyon ölü düğme olmamalı.
- Yeni iş menüsü dört türü açar: Posta taşı, Dosya dönüştür, Arşivle ve böl, Dosyadan kurtar. Diğer üç tür örnek kaynak/hedef seçenekleriyle taslak olarak kaydedilebilir; gerçek motorlarının bulunmadığı açıkça belirtilir. Tam çalıştırma simülasyonu yalnız posta taşıma içindir.

## Tamamlanacak ana akış

1. Yeni posta taşıma işi oluştur veya örnek projeyi aç.
2. Kaynak ağacında hesap/klasör değiştir, örnek kaynak ekleme çekmecesinden hesap/dosya seç. Gerçek dosya seçici veya parola alanı açma.
3. Kaynak ve hedefi bağımsız seç: IMAP, Microsoft 365, Google Workspace; dosya örnekleri MBOX/PST/EML; OST/OLM bu aşamada örnek kaynak. POP kaynak rolü olarak örneklenebilir. Gösterilen seçenekler prototip demosudur. Kaynak-hedef değiştirme state'i gerçekten günceller.
4. Arama, yıl/tarih ve ek filtresi listeyi ve toplamı günceller. Filtre çekmecesinde gönderen ve boyut sınırı da çalışır. Satır tıklama yalnız önizleme; kapsam varsayılan filtreye uyan bütün iletiler. İsteğe bağlı tek tek seçim modu ayrı açık kontrol ve ayrı sayımla açılır.
5. Klasör eşlemesini düzenle, yinelenme tercihini seç, işi kaydet. Demo planı localStorage'da sürümlü küçük JSON olarak saklanabilir; yalnız sentetik veri. Bozuk kayıt güvenli sıfırlanır. Plan değişirse eski ön kontrol geçersizleşir.
6. Ön kontrolü çalıştır: boyutu aşan öğe çözülmeden başlatma kapalı. Atla/raporla seçimi sorunu çözer, hedef değişimi plana geri götürür ve yeniden kontrol ister. Yinelenen/büyük/filtre dışı öğeler birbirini iki kez saymaz. Sıfır kapsamlı plan başlatılmaz.
7. Aktarımı başlat: görünür simülasyon etiketi, ilerleme, duraklat/devam. Başlatılmış planın anlık görüntüsü sabittir; sonradan taslak düzenleme çalışan işi değiştirmez. Zamanlayıcı duraklatınca ilerlememeli; tekrar tıklama birden fazla zamanlayıcı yaratmamalı. Yenilemede örnek çalışma duraklamış/geri yüklenmiş olarak tutarlı devam eder veya açıkça sıfırlanır.
8. Bittiğinde sentetik sonuçları doğrulama aşaması ve sayısal teslim özeti. Tekil öğe sonuçlarına dayalı aktarılan/atlanan/başarısız toplamı ve gerekçeler. Raporu indir: yerel JSON veya CSV, demo etiketi, filtre ve kaynak/hedef özeti; gerçek veri yok. Yeniden deneme varsa daha önce başarılı öğeleri iki kez saymaz.

## Doğrulama ve teslim

- Paket komutları: build, typecheck, lint ve test; TypeScript kontrolü açık komutla çalıştırılır.
- Anlamlı kural testleri: filtre sayımı, ön kontrol kapısı ve geçersizleşmesi, yinelenme/boyut çakışmasında tek sayım, değişmez çalıştırma planı, duraklat/devam ve son toplamlar.
- Headless tarayıcıdan uçtan uca: kaynak/hedef seç → filtre → ön kontrol engeli → çöz → başlat → duraklat/devam → rapor. Konsol/page errors kontrolü. Yenileme davranışı kontrolü.
- Kullanıcının PC kontrolünü reddetmesi nedeniyle doğrulama terminalde çalışan headless Playwright ile; bilgisayar kullanımı/masaüstü/IAB otomasyonu yok. Kullanıcıya yalnız yerel önizleme açılabilir.
- Ana üç ekranı referans oranında yaklaşık 1660×948 ve 1366×768, ayrıca 390×844 dar görünümde kontrol et. Birincil eylem ve içerik kaybolmamalı; tablonun kendi yatay kaydırması kabul edilebilir.
- Referans ve uygulama ekran görüntülerini aynı görsel kontrolde incele. En az beş somut karşılaştırma: metin, panel oranları, tipografi, palet, aralık/ikonlar. Sonuçlar `prototype/VISUAL_QA.md`; üç teslim ekranı `prototype/qa/` altında. Gereksiz geçici QA dosyalarını bırakma.
- `prototype/README.md`: çalıştırma, komutlar, örnek veri sınırı. Sonuç raporunda gerçek komutlar/çıkış kodları, hatalar, sapmalar ve yerel URL bulunur. Çalıştırılmayan kontrolü geçti sayma.

İş bittiğinde Astra ROADMAP.md içindeki gerçek durumu ve kalan işi günceller. SOL ana yol haritasını eşzamanlı düzenlemez.
