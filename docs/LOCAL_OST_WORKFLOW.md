# BitigMail — İlk gerçek dosya akışı

Tarih: 2026-09-12. Kullanıcı onayı: “harika devam”. TASK-009 CID/gövde doğrulamasını tamamlar; TASK-010 bu belgeye göre gerçek OST → PST akışını uygulamaya bağlar.

**Teslim durumu: TASK-009 ve TASK-010 tamamlandı; Astra kabul etti.** Gerçek dosya, güvenlik, arayüz ve bağımsız okuyucu kanıtları ile kapsam sınırları [LOCAL_OST_VALIDATION.md](LOCAL_OST_VALIDATION.md) belgesindedir. Aşağıdaki maddeler yapım sözleşmesinin kaydıdır.

TASK-011 bu temel akışa tarih/klasör filtreli dönüşümü ekler: [filtre sözleşmesi](FILTERED_OST_WORKFLOW.md) ve [kanıtlar](FILTERED_OST_VALIDATION.md). Aşağıdaki filtreleme kapsam dışı ifadesi yalnız ilk TASK-010 tesliminin tarihsel kapsamıdır.

## Kullanıcıya sunulan sonuç

Aktarım ve dönüşüm → Dönüştür → müşteri/proje → OST dosyası seç → dosya analizi/ön kontrol → PST hedefini seç → dönüştür → ilerleme ve rapor.

Bu ilk yerel akış gerçek dosya işler. Diğer hesap/biçim yönleri örnek çalışma olarak kalır. Büyük dosya, filtreleme, bölümleme, kurtarma ve kesintiden devam bu görevin parçası değildir. Gerçek akışta demo zamanlayıcısı, hayali ilerleme veya çalışan duraklatma iddiası kullanılmaz.

## Mimari karar

- Mevcut React/TypeScript/Vite arayüzü ve müşteri/proje yapısı korunur. `convert` işlemindeki yer tutucu gerçek yerel dönüşüm ekranına dönüşür; gerçek işlerde örnek kaynak/hedef seçicileri gösterilmez.
- `engine/BitigMail.Engine`: .NET 8 kitaplığı, Aspose.Email 24.8.0; TASK-008/009'un doğrulanmış öğe bazlı okuma/yazma ve CID davranışı. Deneme aracının tüm manifest/rapor kodunu ürün motoruna kopyalamaz. Motor gerçek dosyalar için manifest gerektirmez.
- `engine/BitigMail.LocalHost`: ASP.NET Core, `net8.0-windows`, WinForms dosya diyalogları. Gerekli WindowsDesktop runtime zaten proje yerel SDK'sında var. Native seçim ayrı STA iş parçacığında çalışır; kullanıcı uygulamadaki düğmeye basınca açılır. Masaüstü otomasyonu yapılmaz.
- Arayüz `http://127.0.0.1:5173/`, hizmet yalnız `http://127.0.0.1:6174/`. Başlatma betiği yalnız proje süreçlerini yönetir; port doluysa ilgisiz süreci sonlandırmaz. Hizmet/bağımlılık kurulumu makine geneline yapılmaz.
- Dosya tarayıcıya yüklenmez; yerel okuyucu seçilen dosyayı akışla açar. İçerik dış servise gönderilmez. Ekrana sınırlı ileti üstverisi ve klasör/sayım bilgisi gelir; bu aşamada HTML posta önizlemesi gerekmez.

## Yerel API sınırı

- Host başlığı tam izinli loopback hizmet adresi olmalı; dinleme yalnız 127.0.0.1. API Origin izin listesi tam `http://127.0.0.1:5173`; joker, `null` veya yabancı origin reddedilir. CORS tek başına koruma sayılmaz; istek sunucuda da denetlenir.
- Başlangıç oturumu endpoint'i Origin/Host denetiminden sonra rastgele süreç oturum anahtarı verir. Diğer API çağrıları bu anahtarı özel header'da taşır. Anahtar URL, log veya localStorage'a yazılmaz; cevaplar `no-store`. Değişiklik yapan çağrılar JSON ve özel header ister. Origin olmayan tarayıcı dışı çağrılar da normal API'de reddedilir; test istemcisi doğru Origin/anahtar kullanır.
- Native kaynak/hedef seçimi dışında serbest dosya yolu kabul eden genel endpoint yoktur. Seçim kayıtları rastgele kimlikle sunucuda tutulur. Analiz ve iş istekleri bu kimliklere başvurur. Dosya açma/okuma/rapor endpoint'leri keyfi path almaz.
- Müşteri/proje kimliği ve adı iş başında sabitlenir. Sonraki UI seçimleri çalışan işin bağlamını değiştirmez. Bu yerel tek kullanıcı köprüsü kurumsal çok kullanıcılı yetkilendirme iddiası değildir.
- Native pencere açılması, dosya seçilmesi veya işlem başlatılması geçersiz token/origin ile tetiklenemez. Seçici iptali normal sonuçtur.

## İş motoru ve veri bütünlüğü

1. Kaynak gerçek OST imzası/istemci alanı ve SDK biçimiyle doğrulanır; yalnız uzantıya güvenilmez. PST ve rastgele dosya OST olarak reddedilir.
2. Analiz arka planda çalışır; kaynak boyutu/SHA-256, klasör ve öğe sayıları, ek sayıları, sınırlı ileti üstverisi ve ön kontrol sonucu üretir. Boş sistem klasörleri ile dolu posta klasörleri ayrılır; klasör yol kimlikleri çakışmaz.
3. Kaynak salt okunur açılır; Windows yazma kilidi veya okuma hatası varsa açıklanır. Çalıştırma öncesi kaynak yeniden doğrulanır: analizden sonra değişmişse yeni analiz gerekir. İş süresince yazmayı/dosyayı değiştirmeyi engelleyen okuma paylaşımı kullanılır. Kaynak önce/sonra hash'i raporda bulunur.
4. Değerlendirme motorunda herhangi bir klasör 50 öğeyi aşıyorsa ön kontrol ENGELLER; sessiz 50 öğelik kırpma veya tam başarı olmaz. Desteklenmeyen/okunamayan öğe ve klasörler gizlenmez. İlk akışta desteklenmeyen posta dışı öğeler varsa açık engel üretilebilir.
5. Hedef Windows kaydetme diyaloğuyla seçilir; mevcut hedefe veya kaynağa yazılmaz. Boş disk alanı kontrol edilir; tahmin garanti diye gösterilmez. Çıktı önce aynı klasörde iş kimliği taşıyan `.partial` dosyasına yazılır, doğrulama sonrası yeni `.pst` adına üzerine yazmadan atomik taşınır. Hedef sonradan oluşmuşsa işlem durur, dosya korunur.
6. Tek gerçek dönüşüm aynı anda çalışır. Başlatma çift tıklaması/yanıt kaybında idempotency anahtarı aynı işi döndürür; yeniden aynı çıktıya kopyalama başlatmaz. İlerleme gerçek okunan/yazılan/doğrulanan sayaçlardır.
7. Yeni PST kapatılıp tekrar okunur; kaynak/çıktı klasör ve öğe sayıları, fiziksel çokluk, temel alanlar, ek hash'leri ve TASK009 CID politikası ölçülür. Tam başarı, uyarılı tamamlandı ve başarısız sonuçları ayrıdır; her belirsizlik kayıp diye etiketlenmez. Deneme lisansı ve ölçülmeyen alanlar kısa bilgi olarak görünür.
8. İş kaydı atomik JSON ile `runtime/local-engine/` altında kalıcıdır (Git dışında). Yenilemede durum/rapor yeniden yüklenir. Hizmet yeniden başlarsa yarım iş `interrupted` olur; tamamlandı/otomatik devam taklidi yapılmaz. Yarım çıktı başarı dosyası olarak sunulmaz. Gövde içerikleri veya sırlar iş loguna yazılmaz.

## Arayüz

- Sarı/turuncu/beyaz kimlik, mevcut gezinme ve çoklu şirket düzeni korunur. Dönüştür seçilince gerçek dosya ekranı; Taşıma/Arşiv/Kurtarma örnek durumları açık ve mevcut davranışla uyumludur. Kanıtlanmamış “kayıpsız bütün formatlar” metni kaldırılır.
- Dosya seçim kartı: seçili ad/konum, boyut, analizin ilerlemesi, anlaşılır hata ve yeniden seçme.
- Analiz kartı: dolu/boş klasörler, toplam öğe/ek, küçük ileti listesi (konu/gönderen/tarih), kaynak→PST yönü. Örnek e-postalar gerçek dosya ekranına karışmaz.
- Ön kontrol: engeller varsa başlat kapalı; hedef seçimi ve kaynak değişikliği analizi günceller. Uyarılar kısa, kullanıcının kararını etkileyen bilgi içerir; teknik property/tag/CLI ayrıntıları ürün ekranına konmaz.
- Çalışma kartı: gerçek aşama, sayaçlar, hata/uyarılar; bitince çıktı konumu ve JSON raporu indirme/kopyalama. HTML çalıştırılmaz, uzak görsel yüklenmez.
- İş merkezi ve Raporlar'da gerçek yerel işler müşteri/projeleriyle görünür ve ilgili gerçek işe açılır. Örnek işler açıkça örnektir; demo sıfırlama gerçek dosya/iş geçmişini silmez.
- Hizmet kapalıysa sahte çalışma olmaz; bağlantı durumu ve çalıştırma kılavuzu görünür. Kaynak/hedef seçimi iptal edildiğinde önceki geçerli durum bozulmaz.

## Kabul ve doğrulama

- Gerçek 13 öğeli OST ile tam yerel motor akışı ve UI uçtan uca koşusu: 13/13, dört ek hash'i/CID, kaynak hash'i değişmez, PST gerçek okuyucuyla açılır.
- Native diyalog otomasyonu yok. Headless E2E için ayrı Testing hizmetinde seçici bağımlılığı test fixture/kısıtlı temp hedef veren adapter ile değiştirilir; bu üretim API'sinde serbest path deliği açmaz. Normal süreçte test seçici/endpoint kullanılamaz. Test raporu native pencerenin elle kullanımının otomatik doğrulanmadığını belirtir.
- Anlamlı testler: bozuk/yanlış tip, seçimi iptal, hedef mevcut/race, kilitli/değişmiş kaynak, 50+ klasör engeli, çift başlatma, hizmet kesintisi/yenileme, sahte origin/token/path isteği, şirket/proje bağlamı ve demo/gerçek ayrımı. Regresyonlar mevcut plan/simülasyon/çoklu arama davranışını kapsar.
- Frontend React beceri ilkeleri: ayrı hooks, kontrollü polling/temizlik, büyük posta listesi state'e yığılmaz, mevcut bağımlılıklar kullanılır.
- Browser plugin absent; mevcut Playwright headless kullanılacak. Yeni gerçek akış masaüstü 1366/1660 ve dar ekran kontrolü; sayfa kimliği/boş ekran/overlay/console/etkileşim/screenshot kanıtları. QA ekranları ve geçici raporlar repo dışında kullanıcı temp çıktı dizininde tutulur; kalıcı plan bu belgededir.
- SOL normal uygulamayı Gemini CLI ile yürütür; derleme/test/kanıtlarını çalıştırır. Astra yerel API sınırı ve veri bütünlüğü entegrasyonunu kabul eder. Projeye özel normal bağımlılık kurulumu bu kapsamda yetkilidir; satın alma, hesap açma, Outlook profil erişimi ve harici posta gönderimi yoktur.

## Teknik kaynaklar ve sürüm sınırı

Microsoft CORS kılavuzu CORS'un tek başına güvenlik sınırı olmadığını açıklar; bu nedenle Origin/Host ve oturum anahtarı denetimleri API'de birlikte uygulanır: [ASP.NET Core CORS](https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-8.0). Host denetimi bağlama adresinden ayrıdır: [Kestrel](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel?view=aspnetcore-10.0). Native seçim WinForms ortak dosya diyaloğudur: [Microsoft OpenFileDialog örneği](https://learn.microsoft.com/ru-ru/dotnet/desktop/winforms/controls/how-to-open-files-using-the-openfiledialog-component).

İlk yerel akış mevcut doğrulanmış .NET 8 kurulumu üzerinde geliştirilir. Microsoft'a göre .NET 8 desteği **10 Kasım 2026** tarihinde biter; üretim/pilot paketlemesinden önce desteklenen LTS sürüme geçiş kabul kontrolüne eklenmiştir. Bugünkü deney, uzun vadeli runtime seçimi değildir. [Resmî destek bildirimi](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/).
