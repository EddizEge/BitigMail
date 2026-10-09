# BitigMail — geliştirici notları

Güncelleme: 2026-10-09 (sürüm 0.9.4). Kullanıcı gözünden ekranlar: [kullanım rehberi](KULLANIM_REHBERI.md). Aşama durumu: [yol haritası](../ROADMAP.md) ve [tam sürüm listesi](FULL_RELEASE_ROADMAP.md). Gerçek yön kapsamı: [format ve yön matrisi](FORMAT_DIRECTION_MATRIX.md).

## Durum özeti

BitigMail, Windows için yerel çalışan bir posta yönetim uygulamasıdır. Şu akışlar gerçek motorla çalışır ve yerel testlerle kabul edildi:

| Akış | Arayüzdeki yeri |
|---|---|
| OST → yeni PST | Aktarım ve dönüşüm → Dosya dönüşümü → OST → PST |
| EML dosyaları / EML klasörü / mboxrd → yeni PST | Aktarım ve dönüşüm → Dosya dönüşümü → EML / MBOX → PST |
| PST/OST/OLM → EML | Aktarım ve dönüşüm → Dosya dönüşümü → PST / OST / OLM → EML |
| Apple Mail EMLX → EML | Aktarım ve dönüşüm → Dosya dönüşümü → Apple Mail EMLX → EML |
| PST/OST → yıl veya boyuta göre PST parçaları | Aktarım ve dönüşüm → PST bölme |
| Hasarlı PST/OST → kurtarılan EML klasörü | Aktarım ve dönüşüm → Veri kurtarma |
| EML/mboxrd → IMAP | Aktarım ve dönüşüm → Posta aktarımı → Dosyadan hesaba |
| IMAP → EML/mboxrd | Aktarım ve dönüşüm → Posta aktarımı → Hesaptan dosyaya |
| IMAP → IMAP (kopyalama) | Aktarım ve dönüşüm → Posta aktarımı → Hesaptan hesaba |
| POP → EML (kaynakta silme yok) | Aktarım ve dönüşüm → Posta aktarımı → POP'tan EML'e |
| Yönetilen arşiv, çok müşterili arama, seçili sonuçları EML'e çıkarma | Arşiv ve arama |

Microsoft (kurumsal ve kişisel) ve Google OAuth bağlantı altyapısı yerel kabul aldı; gerçek hesaplarla canlı pilot 10. aşamada. Kurtarma yalnız küçük, kontrollü hasarlarla doğrulandı. Aspose deneme sürümü sınırları geçerli (aşağıda).

## Proje bileşenleri

| Klasör | İş |
|---|---|
| `prototype/` | React 18 + TypeScript + Vite; adı prototype ama ürünün gerçek arayüzü. Geliştirmede `http://127.0.0.1:5173` |
| `engine/BitigMail.Engine` | Formatlar, MIME, arşiv (SQLite FTS5), planlama, bütünlük (`net8.0`; Aspose.Email, MimeKit/MailKit) |
| `engine/BitigMail.LocalHost` | Yerel HTTP API, kimlik/yetki, iş kuyruğu, IMAP/POP/OAuth (`net8.0-windows`). Doğrudan çalıştırmada varsayılan port 6174 |
| `engine/BitigMail.Desktop` | WinForms + WebView2 kabuğu; kendi LocalHost sürecini rastgele loopback portta başlatır |
| `engine/BitigMail.Setup` | Kullanıcı başına kurulum, güncelleme, geri dönüş, kaldırma, başlatma |
| `engine/BitigMail.RecoveryWorker` | Hasarlı dosya okuma için yalıtılmış süreç |
| `engine/BitigMail.TestingHost` | Ayrı profil ve port (6175) ile E2E/entegrasyon sunucusu |
| `engine/BitigMail.Engine.Tests` | xUnit motor, bütünlük, güvenlik ve yaşam döngüsü testleri |

Masaüstü paketinin portu sabit 6174 değildir. Mimari ayrıntılar: [devir notu](../CLAUDE_PROJECT_HANDOFF_TR.md) §4–§5.

## Geliştirme ortamı

Komutlar PowerShell'de, proje kökünden. .NET için projedeki SDK'yı kullanın (`.tools/dotnet/dotnet.exe`); genel `dotnet` yalnız çalışma zamanı olabilir.

### Arayüz (Vite)

```powershell
cd prototype
npm install        # ya da kilitli temiz kurulum için: npm ci
npm run dev
```

### Yerel motor

```powershell
.\scripts\start-local-engine.ps1
```

Servis `http://127.0.0.1:6174` adresinde dinler; istekleri Host/Origin ve token denetimiyle kabul eder. Durdurmak için `.\scripts\stop-local-engine.ps1`; önce motorun size ait olduğunu ve çalışan iş olmadığını kontrol edin. E2E için `.\scripts\start-testing-engine.ps1` (port 6175).

Vite + boş LocalHost üretim kimlik ekranındaki ilk yönetici kanıtını üretmez. Normal kullanıcı deneyimini paketli masaüstü uygulamasıyla, E2E'yi TestingHost ve ilgili testin güvenli kurulum yöntemiyle doğrulayın. Güvenlik denetimlerini (Host/Origin, token, `ApiRoutePolicy` envanteri, ilk yönetici kanıtı) bir hatayı geçmek için kapatmayın.

## Akışları elle denemek

Her iş bir müşteri projesine kaydedilir. Önce **Müşteriler**'de şirket ve proje oluşturun (yönetici), hesap gerektiren akışlar için proje kartındaki **Hesap bağla** ile IMAP / Microsoft / Google hesabı ekleyip bağlantıyı ve klasör sayılarını doğrulayın. Sonra **Aktarım ve dönüşüm** ekranında sağ üstte müşteri ve projeyi seçin; 1. adımda işlemi, 2. adımda türü/yönü seçin.

**OST → PST** (Dosya dönüşümü → OST → PST): **OST dosyası seç** ile dosyayı inceleyin. Aktarılacak klasörleri ve isteğe bağlı tarih aralığını seçip ileti/ek önizlemesini kontrol edin; yeni PST konumunu belirleyip başlatın. Tarihler Türkiye saatine (UTC+03) göre, başlangıç ve bitiş günleri dahil uygulanır. Her klasörün kutusu kendi iletilerini seçer; alt klasörler ayrıca seçilir. Boş sonuçta iş başlamaz. Sonuç ve kayıt konumu İş merkezi ve Raporlar'dan yeniden bulunur. Mevcut hedef dosyanın üzerine yazılmaz.

**EML / MBOX → PST** (Dosya dönüşümü → EML / MBOX → PST): Çoklu EML dosyası, alt klasörleriyle EML klasörü ya da tek MBOX seçin. MBOX açıkça **mboxrd** olarak okunur ve tek PST klasörüne aktarılır. EML klasörünün yapısı korunur; atlanan diğer dosyaların sayısı gösterilir. Tarih filtresinde özgün tarihi eksik/geçersiz iletiler dışlanır. Sonuçta "Deneme işaretleri içeriyor" bildirimi görünür.

**PST bölme**: PST veya OST seçin; klasör/tarih filtresini belirleyip yıla ya da dosya boyutuna göre bölmeyi seçin. Hedef üst klasörde yeni bir arşiv klasörüne doğrulanmış parçalar ve manifest yazılır. Boyut sınırı kapanmış dosyanın gerçek boyutuna uygulanır; tek bir ileti ekleriyle sınırı aşıyorsa iş durur.

**Posta aktarımı → Hesaptan hesaba**: Aynı projedeki iki IMAP hesabında kaynak/hedef, tam klasör yolları, hedef eşlemeleri ve isteğe bağlı tarih aralığı seçilir. Önizleme gerekirse hedefte boş klasör oluşturabilir; iletiler yalnız iş başlatılınca kopyalanır. Kaynakta silme/EXPUNGE yoktur. Dosyadan hesaba ve hesaptan dosyaya aynı plan/önizleme/doğrulama düzenini kullanır.

**Bağlantı güvenliği** (IMAP/POP hesap formu): varsayılan SSL/TLS; zorunlu STARTTLS; şifresiz bağlantı yalnız hesap bazında açık onayla. Sunucu ya da port değişince onay yenilenir, TLS'ye geçince temizlenir. Otomatik düşürme veya sertifika doğrulama atlama yoktur. Kod: `ImapConnectionPolicy.cs`, `ImapAccountStore.cs`, `PopAccountStore.cs`.

**Aspose deneme sınırı:** Deneme motoru, seçilen kapsam küçük olsa bile herhangi bir klasöründe 50'den fazla öğe bulunan PST/OST kaynaklarını engeller ve PST çıktılarına değerlendirme işaretleri ekler. İşaretler kaldırılmaz, sınır aşılmaz. Bu sınır IMAP aktarımına uygulanmaz.

Ayrıntılı sözleşme ve kabul kayıtları: [OST](LOCAL_OST_WORKFLOW.md) · [filtreli OST](FILTERED_OST_VALIDATION.md) · [EML/MBOX → PST](MIME_IMPORT_WORKFLOW.md) · [PST bölme](PST_SPLIT_WORKFLOW.md) · [IMAP](IMAP_TRANSFER_WORKFLOW.md) · [dosya ↔ hesap](FILE_ACCOUNT_BRIDGE_WORKFLOW.md) · [POP](POP_SOURCE_TO_EML_WORKFLOW.md) · [arşiv/arama](LOCAL_ARCHIVE_SEARCH_VALIDATION.md) · [kurtarma](DAMAGED_STORE_RECOVERY_VALIDATION.md).

## Doğrulama ve testler

```powershell
# Motor testleri (proje SDK'sı; çalışan derleme çıktısını kilitlememek için ayrı artifacts dizini)
& ./.tools/dotnet/dotnet.exe test engine/BitigMail.Engine.Tests/BitigMail.Engine.Tests.csproj --artifacts-path .codex-coordination/build/check --verbosity minimal

# Arayüz
cd prototype; npm run typecheck; npm run lint; npm test; npm run build
```

Playwright E2E testlerini (`npm run test:e2e`, yapılandırma `prototype/playwright.config.ts`) körlemesine hepsini çalıştırmayın: bazıları özel fixture ya da TestingHost ister; önce ilgili testi ve hazırlığını okuyun. Üretim kimliği testi `prototype/tests/e2e/task038-production-identity.spec.ts` kendi test profillerini kullanır; kullanıcı profiline yönlendirmeyin.

## Masaüstü derleme ve sürüm paketi

```powershell
./scripts/build-desktop.ps1
./scripts/build-windows-release.ps1 -Version 0.9.x   # yeni bir sürüm numarası seçin; çalışan paketin üstüne yazmayın
& "$env:LOCALAPPDATA/Programs/BitigMail/BitigMail.Setup.exe" launch
```

Sürüm paketi `artifacts/BitigMail-Internal-<sürüm>` altına çıkar; içinde `BitigMail.Setup.exe`, `payload/`, `package-manifest.json`, `SHA256SUMS.txt`, `README.txt` ve `WINDOWS_QUICK_START_TR.md` bulunur. Paket imzasızdır; SHA-256 yalnız dosya bütünlüğünü gösterir.

Kurulu uygulamayı güncellemeden önce uygulama tepsiden **Çıkış** ile kapanmış olmalı. Süreci yalnız kendi başlattığınız PID ile kapatın. Kullanıcı profili (`%LOCALAPPDATA%\BitigMail\desktop`) sıfırlanmaz; testler ayrı Temp profili kullanır.
