# BitigMail — OST/PST Laboratuvarı (lab/ost-spike)

**Güncel sonuç (12 Eylül 2026):** gerçek OST deneyi tamamlandı. Windows ve profilsiz/ağsız ortamda 13/13 öğe dönüştürüldü; bağımsız libpff ile dört ek ve klasör/ileti sayımları doğrulandı. Tam içerik uygunluğu CID/gövde gösterimi belirsizlikleri nedeniyle henüz kabul edilmedi. Güncel çıktılar ve sınırlar: [OST_EXPERIMENT_RESULT.md](OST_EXPERIMENT_RESULT.md). Aşağıdaki EML smoke komutları hazırlık denemesidir.

Bu dizin, BitigMail projesinin izole OST okuma, doğrulama ve PST oluşturma deneylerini (TASK-008 Phase A ve Phase B) barındırır.
Mevcut kullanıcı Outlook profillerine, kayıt defterine veya müşteri verilerine dokunulmaz.

---

## 1. Mimari Genel Bakış

```
lab/ost-spike/
├── OUTLOOK_SETUP.md              # Kullanıcı laboratuvar Outlook profil yönergeleri (USER_STEP_REQUIRED)
├── README.md                     # Bu dosya: Laboratuvar kılavuzu ve komutlar
├── local-credentials.example.json# Yerel kimlik bilgisi şema örneği
├── local-credentials.json        # Üretilen yerel parola (Git tarafından yoksayılır)
├── seed-manifest.json            # IMAP UID ve UIDVALIDITY eşleme dökümü
├── scripts/
│   ├── lab_mail_start.ps1        # GreenMail Docker kapsayıcısını başlatır
│   ├── lab_mail_status.ps1       # Loopback portlarını ve soketleri doğrular
│   ├── lab_mail_stop.ps1         # Kapsayıcıyı durdurur
│   ├── seed_and_verify_mailbox.py# Python stdlib ile 12 fixture'ı yükler ve doğrular
│   ├── install_local_dotnet.ps1  # .tools/ içine sistem PATH'ini bozmadan .NET SDK kurar
│   ├── build_harness.ps1         # C# Aspose.Email harness'ını derler
│   └── run_harness.ps1           # Harness'ı çalıştırır (smoke veya ost-to-pst)
├── harness/
│   ├── BitigMail.OstHarness.csproj # net8.0 + Aspose.Email 24.8.0
│   └── Program.cs                # Per-item akış ve güvenlik kontrolleri içeren C# harness
├── input/                        # Alınan salt-okunur OST kopyaları (Git yoksayar)
└── output/                       # Üretilen PST ve JSON raporları (Git yoksayar)
```

---

## 2. Phase A — Yerel Test Posta Kutusu

### Kapsayıcı Özellikleri
- **İmaj**: `greenmail/standalone:2.1.12@sha256:9f32971b4f25d32b4de6fa2e297423768441c65e4541f6aecd7631c890a229a7` (SOL denetimli sabit digest)
- **Kapsayıcı Adı**: `bitigmail-lab-mail`
- **Portlar**: Yalnızca `127.0.0.1` yerel döngü adresine bağlıdır:
  - `127.0.0.1:3025` -> `3025` (SMTP — Yalnızca yerel yakalama modu, dış iletim yok)
  - `127.0.0.1:3143` -> `3143` (IMAP)
  - Sınır: Yalnızca yapılandırılan bu test sunucusu rotası yerel döngüde çalışır ve yakalama modundadır.
- **Güvenlik Sınırları**: Privileged bayrağı yok, host network yok, docker socket bağlama yok.
- **Kimlik Bilgisi**: `lab/ost-spike/local-credentials.json` (otomatik üretilir, gizli tutulur, loglara basılmaz).
  - E-posta: `lab@bitigmail.example`
  - Giriş Kullanıcı Adı: `lab` (GreenMail `login:password@domain` standardı gereği)

### Komutlar

#### Sunucuyu Başlatma
```powershell
pwsh -ExecutionPolicy Bypass -File lab/ost-spike/scripts/lab_mail_start.ps1
```

#### Durum ve Port Denetimi
```powershell
pwsh -ExecutionPolicy Bypass -File lab/ost-spike/scripts/lab_mail_status.ps1
```

#### İletileri Yükleme ve Doğrulama (Python Stdlib)
```powershell
python lab/ost-spike/scripts/seed_and_verify_mailbox.py
```
- Yeniden başlatmalarda sessiz çift veri oluşmasını engeller; dolu posta kutusuna ekleme yapmayı reddeder.
- Sıfırdan temizleyip yeniden yüklemek için: `python lab/ost-spike/scripts/seed_and_verify_mailbox.py --reseed`
- Yalnızca doğrulamak için: `python lab/ost-spike/scripts/seed_and_verify_mailbox.py --verify-only`
- **Açık Klasör Eşlemesi**: `Gelen Kutusu -> INBOX` (ayrı bir Gelen Kutusu posta kutusu açılmaz; IMAP `INBOX` doğrudan kullanılır).
- Diğer klasörler modified UTF-7 ile kodlanır (`G&APY-nderilenler` ve `Projeler<delim>&ATA-stanbul`); IMAP `LIST` hiyerarşi ayırıcısı otomatik algılanır.
- 12 temel fixture'ın tümünü (bayt kopyası `msg-01`/`02` ve ortak Message-ID'li `msg-03`/`04` çiftleri dahil 12 fiziksel öğe) çoklu-küme (`multiset`) eşlemesiyle doğrular.
- 3-kaynak-klasör / 12-ileti oraklı yalnızca bu 3 klasördeki temel fixture'ları denetler; istemcinin oluşturabileceği ilgisiz/boş sistem klasörleri veya Outlook sınama iletisi gibi sonradan eklenen öğeler oraktan ayrı `clientExtraItems` altında raporlanır.
- Sunucu UID'lerini, UIDVALIDITY değerini ve ayırıcıyı `lab/ost-spike/seed-manifest.json` dosyasına yazar.
- Satır içi CID PNG dahil olmak üzere toplam 4 ekin SHA-256 özetini ve metin gövdelerini FETCH ile denetler.

#### Sunucuyu Durdurma
```powershell
pwsh -ExecutionPolicy Bypass -File lab/ost-spike/scripts/lab_mail_stop.ps1
# Veya kapsayıcıyı kaldırmak için:
pwsh -ExecutionPolicy Bypass -File lab/ost-spike/scripts/lab_mail_stop.ps1 -Remove
```

---

## 3. Phase B — Aspose.Email C# Dönüşüm Harness'ı

### SDK ve Bağımlılık İzolasyonu
- **.NET SDK**: `install_local_dotnet.ps1` betiği ile doğrudan `.tools/dotnet/` altına kurulur. Sürüm resmi Microsoft 2026-09-08 tarihli dağıtımı olan `8.0.425` sürümüne sabitlenmiştir (Kaynak: [Microsoft .NET 8.0](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)). Makine genelindeki `PATH` veya ortam değişkenleri değiştirilmez.
- **Süreç ayarları**: Betikler yalnız kendi PowerShell süreçleri ve alt işlemleri için `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`, `DOTNET_CLI_TELEMETRY_OPTOUT=1` ve `DOTNET_NOLOGO=1` tanımlar; makine genelindeki PATH değiştirilmez. İlk SDK çalıştırmasının çıktısında ASP.NET Core geliştirme sertifikası kurulduğu bildirildi. Sertifika kaldırma veya güven deposu değişikliği yapılmadı; sonraki çalıştırmalar için bu süreç ayarları eklendi. Bunlar tüm ilk çalıştırma yan etkilerinin engellendiğine dair doğrulama değildir.
- **Paketler**: `Aspose.Email` 24.8.0 sürümüne sabitlenmiştir (`BitigMail.OstHarness.csproj`). İleti çıkarımı doğrudan `PersonalStorage.ExtractMessage(MessageInfo)` API'si üzerinden öğe bazında akışla yapılır.

### Derleme
```powershell
# 1. Yerel .NET SDK kurulumu (gerekirse)
pwsh -ExecutionPolicy Bypass -File lab/ost-spike/scripts/install_local_dotnet.ps1

# 2. Harness derleme
pwsh -ExecutionPolicy Bypass -File lab/ost-spike/scripts/build_harness.ps1
```

### Mod 1: Sentetik EML -> Yeni Unicode PST (Smoke Testi & Same-SDK Yeniden Açma Doğrulaması)
> [!WARNING]  
> Bu mod Unicode PST oluşturma, öğe yazma ve aynı SDK ile yeniden açarak aslına uygunluk (fidelity) denetimi yapar. **KESİNLİKLE OST DÖNÜŞÜM KANITI DEĞİLDİR.** Bağımsız okuyucu denetimi `NOT_RUN` olarak kalır.

- **Yazma Sonrası Yeniden Açma Denetimi (Same-SDK Reopen)**: Yazma tutamacı kapatılıp disk tamponu boşaltıldıktan sonra, oluşturulan PST dosyası Aspose.Email 24.8.0 ile salt-okunur olarak yeniden açılır.
- **Klasör ve Fiziksel Öğe Doğrulaması**: Beklenen 3 mantıksal klasör (`Gelen Kutusu` [5], `Gönderilenler` [3], iç içe geçmiş `Projeler/İstanbul` [4]) taranarak tam 12 fiziksel iletinin varlığı denetlenir.
- **Fiziksel Çoklu-Küme Eşlemesi (Multiset Matching)**: Ortak Message-ID taşıyan iletiler (`msg-03`/`msg-04`) konu başlığı ile ayırt edilir; bayt kopyası çiftler (`msg-01`/`msg-02`) ayrı fiziksel öğeler olarak tüketilir.
- **Ek Bütünlüğü**: Toplam 4 ekin (metin, ikili, PNG, satır içi CID PNG) SHA-256 özetleri manifest ile bayt bazında karşılaştırılır.
- **Dürüst Durum Ayrımı**: Yazma başarısı (`itemsWritten: 12`) ile aslına uygunluk durumu ayrılmıştır. Deneme sürümü filigranı veya başlık değişikliği tespit edildiğinde durum dürüstçe `DIFFERENCES` olarak işaretlenir; yalnızca tam eşleşmede `PASS` verilir.

```powershell
pwsh -ExecutionPolicy Bypass -File lab/ost-spike/scripts/run_harness.ps1 -Mode synthetic-smoke
```

### Mod 2: Gerçek OST -> Yeni Unicode PST (Öğe Bazında Çıkarım)
Kullanıcı [OUTLOOK_SETUP.md](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/lab/ost-spike/OUTLOOK_SETUP.md) yönergelerine göre oluşturulan geçerli OST dosyasını `lab/ost-spike/input/bitigmail-lab.ost` konumuna koyduktan sonra çalıştırılır.

```powershell
pwsh -ExecutionPolicy Bypass -File lab/ost-spike/scripts/run_harness.ps1 -Mode ost-to-pst -InputOst "lab/ost-spike/input/bitigmail-lab.ost"
```

### Sıkı Güvenlik ve Doğrulama Kuralları
1. **Hedef Koruma**: Çıktı PST dosyası zaten mevcutsa işlem derhal reddedilir (`Refusing to overwrite`).
2. **Kaynak Bütünlüğü**: Girdi OST dosyası salt-okunur açılır (`FileAccess.Read`, `FileShare.Read`). Dönüşümden **önce** ve **sonra** SHA-256 hesaplanır ve tam eşitlik (`HASH_MATCH: TRUE`) zorunlu tutulur.
3. **Biçim İncelemesi**: Yalnızca dosya uzantısına bakılmaz; başlık baytları (`!BDN` - `0x21 0x42 0x44 0x4E`) ve sürüm baytı (0x17 Unicode / 0x24 4K) kontrol edilir.
4. **Hafıza ve Akış**: Tüm klasör veya iletiler RAM'e yüklenmez; `ExtractMessage` ile tek tek çekilip yeni PST'ye eklenir ve bellek serbest bırakılır.
5. **Dürüst Lisans Raporlaması**: Lisanssız çalışma modunda Aspose'un konu başlığına eklediği `Evaluation Only...` filigranı ve klasör başına 50 ileti sınırı açıkça fark raporuna yazılır, sessiz veri kaybı sayılmaz.
6. **Bağımsız Okuyucu Durumu**: SDK içi tekrar okuma `SAME_SDK_ONLY` olarak işaretlenir; bağımsız `libpff` / `pffexport` denetimi yapılmadığı sürece bağımsız okuyucu durumu `NOT RUN` olarak korunur.
