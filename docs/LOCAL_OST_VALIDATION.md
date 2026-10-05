# BitigMail — Yerel OST akışı doğrulama kaydı

Tarih: 2026-09-12. TASK-010 durumu: **TAMAMLANDI — sınırlı ilk gerçek OST akışı kabul edildi.** Genel P2/üretim kabulü değildir.

## Gerçek motor denemesi

Astra mevcut ürün `BitigMail.Engine` Release derlemesini gerçek deneme OST'siyle doğrudan çalıştırdı. Bu ara kontrol PowerShell'in .NET ortamından kitaplık çağrısıdır; ASP.NET HTTP veya native seçici kullanımını kanıtlamaz. SOL'un gerçek HTTP ve arayüz denemesi ayrıca gereklidir.

- Kaynak: `lab/ost-spike/input/bitigmail-lab-full.ost`, 16.818.176 bayt.
- Kaynak SHA-256: `B0801758A2E61D4CE6E86799701A81A7A60C38401F73B13C993D94C03A2EE57A`.
- Analiz: 13 fiziksel öğe, dört ek, üç dolu klasör; ön kontrol uygun.
- Dönüşüm: 13 okunan, 13 yazılan, sıfır hata. Önce/sonra kaynak hash'i aynı.
- Yeniden açma: dört ek ve bir Content-ID doğrulandı; ölçülen alan uygunluğu PASS.
- Çıktı: `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task010-qa/astra-0b58e6981eff454f8ad1e9bfb9dad2a4.pst`.
- Çıktı SHA-256: `0c61fecea782040d9891add04daebab4762b64861b88ac48016a538f3601ad53`.
- Motor analiz/rapor JSON dosyaları aynı dizinde, aynı iş kimliğiyle bulunur.

## Bağımsız okuyucu

Astra aynı yeni PST'yi Aspose'dan bağımsız Debian libpff `pffexport 20180714` ile açtı. Docker ağı kapalı, PST bağlantısı salt okunur; profil veya kimlik bilgisi bağlanmadı.

- 13/13 fiziksel ileti; klasör dağılımı Gelen Kutusu 6, Gönderilenler 3, Projeler/İstanbul 4.
- Klasör + temel ileti başlıkları + ek adı/boyutu/SHA-256 çokluk karşılaştırması eşleşti; eksik/fazla öğe sıfır.
- Kaynakta bulunan dışa aktarılmış gövde gösterimlerinin satır sonları eşitlenmiş hash'lerinde fark sıfır.
- PST'deki dokuz ek düz metin gösterimi ayrı kaydedildi; kaynak içeriği kaybı olarak sayılmadı.
- libpff dışa aktarımı Content-ID özelliğini açığa çıkarmıyor; bu alan motorun MAPI doğrulamasında ölçüldü.
- Kanıt: `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task010-qa/independent-2159d827/report.json`.

## Son gerçek HTTP ve ekran koşusu

SOL, normal API kodunu kullanan ayrı TestingHost üzerinde gerçek dosya seçimi adapter'ı → analiz → hedef seçimi → dönüşüm → rapor akışını çalıştırdı. HTTP cevapları taklit edilmedi; yalnız Windows seçici yerine onaylı deneme dosyasını veren adapter kullanıldı.

- İş: `job-12b5840e3c14`; kayıtlı rapor: `runtime/testing-engine/reports/job-12b5840e3c14.json`.
- Çıktı: `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-qa/headless-test-2e0b3d5b398748a8be60fe0a34672ce1.pst`.
- Çıktı SHA-256: `b1d76d3f49b344d333c4bb8486089b27972715b46dfdeffe9516869bccbbc151`.
- Motor yeniden açma sonucu: 13/13, dört ek, bir Content-ID, kaynak hash'i aynı.
- Astra aynı son PST'de bağımsız libpff kontrolünü de çalıştırdı: 13/13, 6/3/4 klasör dağılımı, dört ek; başlık/klasör/ek çokluğu ve ortak gövde hash farkı sıfır. Dokuz ek düz metin gösterimi ayrıca kaydedildi.
- Bağımsız son kanıt: `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task010-qa/independent-e8ea28d1/report.json`.

## Güvenlik ve işlev kontrolleri

- Backend: 48 test geçti; gerçek üretim traversal/doğrulama yolu, onuncu örnekten sonraki ekler, kök öğeleri, fiziksel çokluk, bozulmuş gövde/ek/CID sonrası yayının engellenmesi, kalıcılık ve oturum sınırı kapsandı.
- Frontend: 32 birim testi, tip kontrolü, lint ve üretim derlemesi geçti.
- Gerçek tarayıcı/API akışı iki masaüstü boyutunda geçti. İlk uzun birleşik koşuda 19 başarı ve iki mobil hata görüldü; mobil doğrudan HTTP yoklaması sırasında 6175 hizmetine erişim kayboldu. Ayrı mobil tekrar güvenlik, gerçek dönüşüm, gerçek UI sonucu ve iki kayıt arasından eski işin doğru açılması dahil 4/4 geçti. İlk kesintinin kök nedeni kesinleştirilmedi; birleşik koşu kusursuz geçti diye raporlanmaz.
- Gerçek işlerde örnek Duraklat/Devam et düğmesi gizlendi. Seçilen gerçek iş kimliği dönüşüm ekranına aktarılır; doğru rapor ve iş başında kaydedilmiş müşteri/proje açılır. Çıktının tam yolu yenileme sonrasında da görünür ve kopyalanabilir.
- Astra normal 6174 hizmetinde bağımsız 20/20 API kontrolünü geçirdi: tam Host/Origin, OPTIONS, JSON türü, eksik/geçersiz token, iki sekmenin birlikte çalışması, serbest yolun tutamaç sayılmaması ve test endpoint'inin üretimde bulunmaması. Tüm cevaplarda no-store; native seçici açılmadı. Kanıt: `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task010-qa/astra-api-check.json`.

## Görsel kabul

1660×948, 1366×768 ve 390×844 ilk seçim ekranlarında boş ekran veya JavaScript hatası görülmedi. Teknik ürün metinleri ve gerçek dönüşüm ekranındaki örnek sayaç ayrımı düzeltildi. Uzun çıktı adları ve konumlar kart içinde satırlara bölünür; tablolar kendi alanlarında kaydırılabilir. Son gerçek 390 px UI koşusunda `document.documentElement.scrollWidth === window.innerWidth` ölçüldü ve hedefli test 1/1 geçti. Ekran görüntüsünün fiziksel piksel genişliği cihaz ölçeğinden etkilenir; yatay taşma kararı DOM ölçümüne dayanır.

Astra gerçek analiz ve sonuç görüntülerini, son mobil sonuç kartı dahil görsel olarak inceledi. Görseller: `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task010-qa/sol-real-*.png`; son kart `sol-real-result-card-mobile-narrow.png`. Normal hizmet 6174 ve arayüz 5173 teslimde açık; test hizmeti 6175 kapalıdır.

## Kapsam sınırı

Bu küçük yapay posta dosyasının başarılı olması; 100 GB, hasarlı OST/PST, tüm MAPI alanları, kesintiden devam veya diğer biçim/sağlayıcı yönleri için destek kanıtı değildir. Native Windows dosya pencereleri otomatik kontrol edilmez. Değerlendirme motorunun klasör başına 50 öğe sınırı ürün ön kontrolünde engel olarak uygulanır; lisanslı ölçek deneyi ayrı aşamadır.

Yapım sözleşmesi: [LOCAL_OST_WORKFLOW.md](LOCAL_OST_WORKFLOW.md). Gövde/CID ölçüm politikası: [CID_BODY_ACCEPTANCE.md](CID_BODY_ACCEPTANCE.md).
