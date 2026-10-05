# BitigMail Windows kabul kaydı

**0.9.2 düzeltmesi — 21 Eylül:** İlk yönetici ekranının beş dakikalık kurulum anahtarı süresi dolduğunda genel hata göstermesi düzeltildi. Her kurulum denemesinde özel yerel kanal üzerinden yeni doğrulama alınır; süre sınırı kaldırılmadı. Sayfa yenilemesinden sonra gerçek Windows ekranındaki düğmeyle test yöneticisi oluşturuldu ve oturum ekranı geçildi. 18 hedefli güvenlik/kimlik testi, 7 gerçek motor kontrolü, 54 ilgili motor testi ve 151 arayüz testi başarılı. Kullanıcının mevcut profili korunarak 0.9.2 kuruldu ve uygulama yeniden açıldı. Aşağıdaki 0.9.1 kayıtları önceki teslimin kanıtıdır.

Güncelleme: 21 Eylül 2026. **BitigMail 0.9.1 Windows iç test sürümü kuruldu ve kullanıcı incelemesi için açıldı.** Masaüstü, kurulum ve sürüm yönetimi yerel kabulü geçti. Yayıncı imzası ve temiz Windows kabulü açık olduğundan 9. aşama ticari kabulünün tamamı kapanmadı.

## Tamamlanan masaüstü çalışması — TASK040

- Gerçek Windows penceresi ve WebView2 içinde paketlenmiş arayüz. Vite veya Node çalıştırılmadan, proje dışındaki bir klasörden açılış doğrulandı.
- Uygulamanın başlattığı yerel motorla özel kanal üzerinden doğrulanmış bağlantı. Yanlış adres/kaynak, başka uygulamanın kullandığı port ve aynı veri profilinin ikinci kez açılması reddedilir.
- Pencereyi kapatmak çalışan işleri kesmeden sistem tepsisine indirir. Çıkış yeni işleri durdurur ve etkin işlerin/güvenli kayıtların tamamlanmasını bekler. Kayıt hatası veya çözümlenmemiş iş güvenli çıkışı engeller ve kullanıcıya bildirilir.
- Yönetici oluşturma, giriş ve bağlantı hatası ekranları sarı–turuncu kimlikle düzenlendi.
- Motor kapanınca profil kilidi serbest kalır; aynı profil tekrar açılabilir. Uygulama dışındaki süreçler sonlandırılmaz.

Kanıtlar: TASK040 kapanışında 866 motor / 151 arayüz testi, derleme ve arayüz kontrolleri başarılı. Masaüstü odaklı 56 test geçti. Bağımsız gerçek motor süreç deneyi 10 sınırı doğruladı: `.codex-coordination/evidence/STAGE9/root040-native-host-boundary.json`. Gerçek WebView2 görüntüsü yönetici kurulum ekranını gösterdi; bu görüntü tüm iş akışlarının masaüstünde yeniden denendiği anlamına gelmez. Kapanış hata yolları hedefli testlerle sınandı; görünür pencere deneyi başarılı açılış ve çıkışı kapsar.

## Tamamlanan yerel dağıtım çalışması — TASK041

- Gerçek kullanıcı bazlı kurulum uygulaması ve sürümlü dosya paketi.
- İçerik özeti ve kesin dosya listesiyle paket doğrulama; kayıt dışı ve değiştirilmiş paketleri reddetme.
- Uygulama çalışırken güncellemeyi reddetme; sürüm seçimini güvenli değiştirme.
- Kalıcı veri şemasına göre geri dönüşü kabul veya ret etme.
- Kaldırmada arşivleri, hesapları ve kullanıcı eklediği/değiştirdiği dosyaları koruma.
- Gerçek kurulum, güncelleme, geri dönüş ve kaldırma deneyleri; son derlenmiş uygulamanın kullanıcıya açılması.

877 motor / 151 arayüz testi, 13 kurulum sınır testi ve 11 gerçek motor süreç kontrolü geçti. Paket proje dışında ve yalnız Windows sistem araçlarını içeren PATH ile kuruldu, gerçek WebView2 penceresi açıldı ve güvenle kapandı. 0.9.0 → 0.9.1 güncelleme ve 0.9.0'a geri dönüş doğrulandı. Kaldırma sonrasında sürüm dosyaları silindi, veri profili korundu. Kurulum başlatıcısı bakım için tutulur. Eski sürümün doğrudan açılması motor başlatılmadan reddedildi.

Dağıtım: `artifacts/BitigMail-Internal-0.9.1`. 1.118 dosya özeti doğrulandı. Kanıtlar `.codex-coordination/results/TASK-041.md` ve `.codex-coordination/evidence/STAGE9/sol041-release-lifecycle.json` dosyalarında. Son küçük masaüstü açılış düzeltmesinden sonra paket yeniden derlendi ve gerçek doğrudan açılış testleri tekrarlandı; 877 motor regresyonu bu yalnız masaüstü değişikliğinden önceki motor sürümüne aittir.

Gerçek kullanıcı kurulumu: `%LOCALAPPDATA%\Programs\BitigMail`, veri profili `%LOCALAPPDATA%\BitigMail\desktop`. Kurulum çıkışı 0, etkin sürüm 0.9.1, Başlat menüsü kısayolu mevcut. Kurulan `BitigMail.exe` penceresi kullanıcı incelemesi için açık bırakıldı. Kayıt: `.codex-coordination/evidence/STAGE9/root041-default-install-open.json`.

## Açık dış kabul koşulları

Kullanıcı kod imzalama sertifikası olmadığını bildirdi. Paket imzasız iç test sürümüdür; dosya özetleri yayıncı kimliği doğrulaması değildir. Temiz bir Windows kurulumunda test henüz yapılmadı. Geliştirme bilgisayarında proje dışından çalıştırmak bu koşulun yerine geçmez. WebView2 çalışma zamanı gerekir; .NET çalışma zamanı paketle birlikte gelir.

Aspose deneme sürümü işaretleri ve ilgili çıktı sınırlamaları korunur. Ticari lisanslı motor, temsilî büyük/hasarlı dosyalar ve 100 GB ölçek kabulü tamamlandı sayılmaz. Desteklenen yönler ve kaynak sınırlamaları [destek matrisinde](FORMAT_DIRECTION_MATRIX.md) yer alır.

Azure kurulumu ve kişisel Outlook hesabında kaynak iletileri silmeden gerçek aktarım pilotu **10. aşamada** yapılacak. Zamanlayıcı kurulmadı.


## 21 Eylül — 0.9.3 arayüz ve hesap bağlantısı revizyonu

TASK043 yerel yazılım kabulü tamamlandı. Yönetim paneli, aktarım yönü seçimi, temalı filtre ve hesap kontrolleri, gerçek müşteri/proje listesi ve mevcut müşteriye proje ekleme doğrulandı. Şifresiz IMAP/POP bağlantısı yalnız hesap bazında açık seçimle çalışır; SSL/TLS varsayılanı, zorunlu STARTTLS ve sağlayıcı OAuth uç noktaları korunur.

892 motor testi, 151 arayüz testi, typecheck/lint/build geçti. Üretim güvenliğiyle ilk yönetici, operatörün yetkili projeye hesap kaydetmesi ve mevcut müşteriye proje ekleme gerçek API ile doğrulandı. Playwright Chromium ile 1440×900 ve 390×844, bağımsız arayüz kontrolünde 1263×900 ve 390×900; konsol hatası, boş sayfa, hata kaplaması veya belge yatay taşması yok. Browser eklentisi mevcut olmadığından Playwright kullanıldı. Ayrı kurulmuş test profilindeki Windows WebView2 görünümü de geçti.

Paket: `artifacts/BitigMail-Internal-0.9.3`; son derlemenin 1.118 dosya özeti eşleşti. Kanıtlar `.codex-coordination/evidence/TASK-043/sol043-final.json`, `root043-backend-final.trx`, `root043-package-hashes.json`, `root043-ui-review.json`. Kullanıcının açık 0.9.2 uygulaması ve profili değiştirilmedi; normal çıkış sonrası gerçek güncelleme/açılış bekleniyor. Gerçek müşteri sunucusunda şifresiz bağlantı denenmedi; yerel IMAP/POP test sunucularında doğrulandı. Yukarıdaki dış kabul sınırları sürer.
