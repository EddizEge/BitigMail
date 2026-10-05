# BitigMail Local Engine (.NET 8 & ASP.NET Core)

Bu dizin, BitigMail'in gerçek yerel dosya işleme motorunu ve Windows yerel dosya seçici köprüsünü içerir. OST→PST, EML/EML ağacı/mboxrd→PST ve PST/OST yıl/boyut bölümleme akışları çalışır (TASK-010–013).

## Projeler

1. **`BitigMail.Engine`**:
   - .NET 8 sınıf kitaplığı (`net8.0`).
   - Pinned bağımlılıklar: `Aspose.Email 24.8.0` (ticari/değerlendirme), `MimeKitLite 4.17.0` (MIT), `System.Formats.Asn1 8.0.1` (MIT; eski geçişli sürümün güvenlik düzeltmesi).
   - MIME kaynağı iki okuyucuya aynı ham baytlarla verilir; mboxrd kaçışı ham gövdede bir kez çözülür. Özgün gövde/ek/temel üstveri ve ölçülen değerlendirme ekleri doğrulanır; bilinmeyen değişiklikte PST yayımlanmaz. [MIME kabul kaydı](../docs/MIME_IMPORT_VALIDATION.md).
   - Gerçek OST dosyalarının salt-okunur analizi (`OstAnalyzer`), klasör sayımı, fiziksel öğe ve ek sayımı, sınırlı üstveri örneklemesi.
   - 50-öğe vendor deneme kısıtının ön kontrolü: herhangi bir klasörde 50'den fazla öğe varsa dönüştürme kesinlikle engellenir; sessiz kırpma yapılmaz.
   - Öğe bazlı okuma ve yeni Unicode PST yazma (`OstToPstConverter`).
   - TASK-009 inline CID ve gövde politikası: `PR_ATTACH_CONTENT_ID_W` (`0x3712001F`) ve `PR_ATTACH_CONTENT_ID_A` (`0x3712001E`) ile bit-düzeyinde koruma.
   - Atomik çıktı: önce hedef klasörde `.partial` dosyasına yazma, kapatıp yeniden açarak aynı SDK ile doğrulama, ardından yeni PST adına üzerine yazmadan atomik taşıma.

2. **`BitigMail.LocalHost`**:
   - ASP.NET Core Web API (`net8.0-windows`).
   - Yalnızca `http://127.0.0.1:6174` adresinde dinler.
   - Windows Forms `OpenFileDialog` ve `SaveFileDialog` bileşenlerini ayrı STA iş parçacıklarında çalıştırır.
   - Keyfi dosya yolu kabul eden genel endpoint bulunmaz; dosya seçimleri sunucu belleğinde rastgele opak tanıtıcılar (`src_...`, `tgt_...`) arkasında tutulur.
   - Güvenlik:
     - Host başlığı tam `127.0.0.1:6174` olmalıdır.
     - Origin başlığı tam `http://127.0.0.1:5173` olmalıdır; `null`, eksik veya yabancı origin reddedilir (HTTP 403).
     - Oturum token'ı (`POST /api/session`) bellekte tutulur, URL/log/localStorage'a yazılmaz.
     - Tüm yanıtlarda `Cache-Control: no-store` zorunludur.
   - Kesinti kurtarma: Başlangıçta yarım kalan işler (`converting`, `queued`, `verifying`) taranır ve `interrupted` olarak işaretlenir; sahte tamamlandı veya otomatik devam taklidi yapılmaz.

3. **`BitigMail.TestingHost`**:
   - Otomatik headless testler için ayrı test süreci (`net8.0`).
   - Yalnızca `http://127.0.0.1:6175` adresinde dinler (üretim portu 6174'e dokunmaz).
   - Test seçici adapter'ı GUI penceresi açmadan onaylı test fixture'ını (`lab/ost-spike/input/bitigmail-lab-full.ost`) ve sistem temp çıktı dosyasını otomatik seçer.

4. **`BitigMail.Engine.Tests`**:
   - xUnit test paketi (`net8.0`).
   - Güvenlik (Origin, Host, Token), başlık denetimi (!BDN, client magic SO vs SM, Aspose FileFormat.Ost), 50-öğe deneme engeli, kilitli/değişmiş kaynak, hedef çakışma ve bağlam dondurma testleri.

## Başlatma ve Durdurma Betikleri

```powershell
# Yerel motoru başlatma (127.0.0.1:6174)
.\scripts\start-local-engine.ps1

# Yerel motoru durdurma
.\scripts\stop-local-engine.ps1

# Headless test sunucusunu başlatma (127.0.0.1:6175)
.\scripts\start-testing-engine.ps1

# Headless test sunucusunu durdurma
.\scripts\stop-testing-engine.ps1
```
