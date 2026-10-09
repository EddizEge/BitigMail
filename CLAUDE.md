# BitigMail — proje talimatı

Windows için kurumsal posta yönetim uygulaması: PST/OST/EML/MBOX/OLM/Apple Mail dosyaları ile IMAP/POP/Microsoft/Google
hesapları arasında filtreli aktarım, dönüştürme, arşivleme, arama ve kurtarma. Hedef kullanıcı müşteri şirketlere hizmet
veren BT firmaları. Kullanıcıyla iletişim kısa, anlaşılır Türkçe.

**Depo herkese açık** (github.com/EddizEge/BitigMail). Commit'e kişisel bilgi, parola, belirteç, lisans dosyası, gerçek posta
verisi girmez. `Notlar/` ve `CLAUDE.local.md` git dışıdır (`.git/info/exclude`).

## Önce oku
1. `Notlar/main.md` — şu an / sırada / son kayıtlar.
2. `CLAUDE_PROJECT_HANDOFF_TR.md` — mimari, veri bütünlüğü ilkeleri, güvenlik, aşama durumu (24.09.2026; ayrıntı burada).
3. İşe göre: `docs/FORMAT_DIRECTION_MATRIX.md`, `docs/FULL_RELEASE_ROADMAP.md`, `docs/WINDOWS_RELEASE_ACCEPTANCE.md`.
`docs/DEVELOPER_GUIDE.md` ve `ROADMAP.md` eskimiş cümleler içerir; kod ve tarihli kabul belgeleri önceliklidir.

## Yapı
| Klasör | İş |
|---|---|
| `prototype/` | React 18 + TypeScript + Vite; adı prototype ama ürünün gerçek arayüzü |
| `engine/BitigMail.Engine` | Formatlar, MIME, arşiv (SQLite FTS5), planlama, bütünlük (net8.0) |
| `engine/BitigMail.LocalHost` | Yerel HTTP API, kimlik/yetki, iş kuyruğu, IMAP/POP/OAuth (net8.0-windows) |
| `engine/BitigMail.Desktop` | WinForms + WebView2 kabuğu; kendi LocalHost sürecini başlatır |
| `engine/BitigMail.Setup` | Kullanıcı başına kurulum / güncelleme / geri dönüş / kaldırma / başlatma |
| `engine/BitigMail.RecoveryWorker` | Hasarlı dosya okuma için yalıtılmış süreç |
| `engine/BitigMail.TestingHost` | Ayrı profil/port (6175) ile E2E sunucusu |
| `engine/BitigMail.Engine.Tests` | xUnit testleri |
| `fixtures/`, `lab/` | Sentetik test verileri, deneyler (`local-credentials.json` git dışı, okuma/kopyalama yok) |
| `artifacts/`, `runtime/` | Derleme çıktıları ve çalışma verisi; git dışı, topluca silme |
| `.codex-coordination/` | Eski görev / sonuç / kanıt kayıtları (TASK-001…043) |

## Komutlar (PowerShell, proje kökünden)
```powershell
# Motor testleri — proje SDK'sı; global dotnet yalnız runtime olabilir
& ./.tools/dotnet/dotnet.exe test engine/BitigMail.Engine.Tests/BitigMail.Engine.Tests.csproj --artifacts-path .codex-coordination/build/check --verbosity minimal
# Arayüz
cd prototype; npm run typecheck; npm run lint; npm test; npm run build
# Masaüstü derleme / sürüm paketi (yeni sürüm numarası seç; çalışan paketin üstüne yazma)
./scripts/build-desktop.ps1
./scripts/build-windows-release.ps1 -Version 0.9.4
# Kurulu uygulamayı aç
& "$env:LOCALAPPDATA/Programs/BitigMail/BitigMail.Setup.exe" launch
```
Playwright E2E testlerini körlemesine hepsini çalıştırma; bazıları özel fixture / TestingHost ister, testi oku.

## Değişmez kurallar
- **Kaynak korunur:** kaynakta silme, POP DELE, IMAP EXPUNGE yok. "Taşıma" kaynak silme yetkisi değildir.
- Önizleme planı kapsamı/filtreyi sabitler; seçim değişirse yeni önizleme. Mevcut hedefin üzerine yazılmaz.
- Dosya oluşması başarı değildir: çıktı yeniden açılıp hash / ileti / ek sayısıyla doğrulanır.
- Güvenlik kontrolleri (Host/Origin, token, `ApiRoutePolicy` envanteri, ilk yönetici kanıtı) bir hatayı geçmek için kapatılmaz.
- Aspose deneme işaretleri / sınırları aşılmaz, filigran kaldırılmaz. İmzasız paket imzalı sayılmaz.
- Kullanıcı profili (`%LOCALAPPDATA%\BitigMail\desktop`) sıfırlanmaz; testler ayrı Temp profili kullanır.
- Güncellemeden önce uygulama tepsiden **Çıkış** ile kapanmış olmalı; süreç yalnız kendi başlattığın PID ile kapatılır.

## Sürüm
Kurulu ve GitHub'da yayınlı: **0.9.3** (ön sürüm, imzasız). Yeni sürüm: kaynak değişikliği → testler →
`build-windows-release.ps1 -Version 0.9.x` → kullanıcı onayıyla kurulum ve GitHub sürümü.
