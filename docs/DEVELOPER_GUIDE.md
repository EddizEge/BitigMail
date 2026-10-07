# BitigMail — geliştirici notları

> Bu belge Eylül 2026 ortasındaki geliştirme durumunu anlatır; bazı cümleleri eskimiştir (arşiv/arama ve kurtarma artık gerçek akışlardır). Güncel özet için ana [README](../README.md) ve [yol haritasına](FULL_RELEASE_ROADMAP.md) bakın.

BitigMail, Windows için geliştirilen posta yönetim uygulamasıdır. Gerçek dosya akışları **OST → yeni PST**, **EML dosyaları / EML klasörü / MBOX → yeni PST** ve **PST/OST → yıl veya boyuta göre PST bölümleme**: dosya analizi, tarih/klasör seçimi, ileti/ek önizlemesi, kaynak koruması, müşteri/proje bağlamı, ilerleme ve kalıcı rapor çalışır. **IMAP hesapları arasında filtreli, kesintiden devam edebilen kopyalama** küçük gerçek laboratuvar verisiyle doğrulandı. Microsoft OAuth altyapısı yerel olarak hazır; gerçek Microsoft posta pilotları bekliyor. Birleşik arşiv araması ve kurtarma henüz örnek verilerle çalışır.

**Tamamlanan yeni akış (TASK-017):** EML/MBOX ↔ IMAP dosya/hesap köprüsü; tarih/klasör seçimi, kalıcı rapor, kesintiden devam ve İş Merkezi'nden geçmiş işe dönüş gerçek API ve masaüstü/telefon görünümünde kabul edildi. 72 fiziksel ileti/24 ek ve gerçek süreç çökmesi sonrası EML/MBOX devamı doğrulandı; [sözleşme](FILE_ACCOUNT_BRIDGE_WORKFLOW.md), [kabul kaydı](FILE_ACCOUNT_BRIDGE_VALIDATION.md). Sonraki onaylı aşama [gerçek arşiv/arama](LOCAL_ARCHIVE_SEARCH_WORKFLOW.md), ardından [kademeli ölçek/dayanıklılık](SCALE_AND_RESILIENCE_PLAN.md). Azure kaydı ve gerçek Outlook pilotu bu işlerin ardından sohbet içinde hatırlatılacak; kullanıcı isteğiyle zamanlayıcı kullanılmaz.

Güncel kapsam: [Yol haritası](../ROADMAP.md). Gerçek dosya test sonuçları ve sınırlar: [Yerel OST doğrulaması](LOCAL_OST_VALIDATION.md), [filtreli dönüşüm doğrulaması](FILTERED_OST_VALIDATION.md), [PST bölümleme doğrulaması](PST_SPLIT_VALIDATION.md).

Yeni EML/MBOX akışı ve bağımsız içerik karşılaştırması: [kullanım ve kapsam](MIME_IMPORT_WORKFLOW.md), [doğrulama kaydı](MIME_IMPORT_VALIDATION.md).

## Proje Bileşenleri

- **`prototype/`**: React 18, TypeScript, Vite arayüz prototipi (`http://127.0.0.1:5173`).
- **`engine/BitigMail.Engine`**: .NET 8 ve Aspose.Email 24.8.0 tabanlı OST okuma ve yeni Unicode PST yazma kütüphanesi.
- **`engine/BitigMail.LocalHost`**: ASP.NET Core `net8.0-windows` yerel servis köprüsü (`http://127.0.0.1:6174`), STA Windows Forms dosya diyalogları.
- **`engine/BitigMail.TestingHost`**: Headless E2E ve entegrasyon testleri için izole test sunucusu (`http://127.0.0.1:6175`).
- **`engine/BitigMail.Engine.Tests`**: Güvenlik, format doğrulaması, 50-öğe kısıtı ve bütünlük negatif kontrollerini doğrulayan xUnit test paketi.

## Hızlı Başlangıç

### 1. Arayüzü Başlatma (Vite)
```powershell
cd prototype
npm install
npm run dev
```
Arayüz `http://127.0.0.1:5173` adresinde açılır.

### 2. Yerel Dönüştürme Motorunu Başlatma

İkinci bir terminali projenin ana klasöründe (`Mail Manager`) açın:
```powershell
.\scripts\start-local-engine.ps1
```
Servis `http://127.0.0.1:6174` adresinde dinler ve tarayıcıdan gelen talepleri Origin/Host/Token doğrulaması ile kabul eder.

Arayüzde **Aktarım ve dönüşüm → Dönüştürme** yolunu açın. Müşteri ve projeyi seçin; **OST dosyası seç** ile dosyayı inceleyin. Aktarılacak klasörleri ve isteğe bağlı tarih aralığını seçip ileti/ek önizlemesini kontrol edin; yeni PST için bir konum belirleyin ve dönüştürmeyi başlatın. Tarihler Türkiye saatine (UTC+03) göre, başlangıç ve bitiş günleri dahil uygulanır. Her klasörün kutusu kendi iletilerini seçer; alt klasörler ayrıca seçilir. Boş sonuçta dönüşüm başlamaz. Sonuç, kullanılan filtreler ve kayıt konumu İş merkezi/Raporlar üzerinden tekrar bulunabilir. Mevcut hedef dosyanın üzerine yazılmaz. Deneme motoru, seçilen kapsam daha küçük olsa bile herhangi bir klasöründe 50'den fazla öğe bulunan kaynak PST/OST dosyalarını engeller. Bu Aspose deneme sınırı IMAP aktarımına uygulanmaz.

Gerçek IMAP aktarımı için önce müşteri/proje ayrıntılarında hesapları ekleyip bağlantı ve klasör sayılarını doğrulayın. Ardından **Aktarım ve dönüşüm → Taşıma → Gerçek IMAP Taşıma** yolunda kaynak/hedef hesapları, tam klasör yollarını, hedef eşlemelerini ve isteğe bağlı tarih aralığını seçin. Önizleme gerekirse hedefte boş klasörler oluşturabilir; iletiler yalnız aktarımı başlatınca kopyalanır. Kullanıcı adı/parola + zorunlu TLS yanında kurumsal Microsoft 365 ve kişisel Outlook.com/Hotmail delegated OAuth bağlantıları yerel olarak hazırdır. Kurumsal canlı pilot **LIVE_PILOT_PENDING**, kişisel canlı pilot **LIVE_PERSONAL_PILOT_PENDING** durumundadır.

**Aktarım ve dönüşüm → Arşivleme** ekranında PST veya OST seçin; klasör/tarih filtresini belirleyip yıllara ya da dosya boyutuna göre bölümlemeyi seçin. Hedef üst klasörü belirlediğinizde uygulama yeni bir arşiv klasörüne doğrulanmış PST parçalarını ve manifesti kaydeder. Boyut sınırı gerçek kapatılmış dosya boyutuna uygulanır; tek bir ileti ekleriyle sınırı aşıyorsa işlem durur. Parça listesi, toplam ek sayısı ve rapor İş merkezi üzerinden yeniden açılır. Küçük test verileriyle doğrulanmıştır; 100 GB performansı, bozuk dosya kurtarma ve kesintiden devam henüz desteklenmez.

### 3. EML / MBOX İçe Aktarma

**EML / MBOX için:** Aktarım ve dönüşüm → Dönüştürme → **EML / MBOX → PST** yolunu açın. Çoklu EML dosyası, alt klasörleriyle EML dizini veya tek MBOX seçin. MBOX bu sürümde açıkça **mboxrd** olarak okunur ve tek PST klasörüne aktarılır. EML dizininin klasörleri korunur; diğer dosyaların kaçının atlandığı gösterilir. Klasörleri ve isteğe bağlı tarih aralığını seçip güncel önizlemeyi kontrol edin, yeni PST konumunu belirleyin ve aktarın. Hiç klasör seçilmemişse işlem başlamaz; tarih filtresinde özgün tarihi eksik/geçersiz iletiler dışlanır.

Sonuçta **Deneme işaretleri içeriyor** bildirimi görünür: Aspose değerlendirme sürümü konu ve gövdeye işaret ekler. İşaretler kaldırılmaz; ölçülen özgün içerik korunur, bilinmeyen içerik değişikliği veya bozuk kaynak işlemi engeller. Rapor indirilebilir ve İş Merkezi'nden yenileme sonrasında açılabilir. **Arşivlemeye geç** ile mevcut bölümleme ekranını açıp oluşan PST'yi kaynak seçerek yıllara veya boyuta göre bölün. EML/MBOX 12 ileti/4 ek, filtreli EML ağacı 3 ileti/1 ek ve yeni PST'den dört yıllık parça küçük testlerle doğrulandı. Bu, 100 GB, bozuk dosya kurtarma, tüm MBOX türleri veya tüm Outlook alanları için üretim garantisi değildir.

### 4. Yerel Motoru Durdurma

```powershell
.\scripts\stop-local-engine.ps1
```

## Doğrulama ve Testler

```powershell
# .NET Motor Testleri
& .\.tools\dotnet\dotnet.exe test engine/BitigMail.Engine.Tests/BitigMail.Engine.Tests.csproj -c Release

# Arayüz Birim Testleri (Vitest)
cd prototype
npm run test

# Arayüz Uçtan Uca Testleri (Playwright)
npm run test:e2e
```
