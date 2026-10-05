# Microsoft 365 bağlantısı — doğrulama kaydı

TASK-015, 2026-09-13. Durum **LOCAL_READY / LIVE_PILOT_PENDING**. Yerel uygulama kabulü tamamlandı. Canlı Microsoft hesabı/kurum erişimi henüz doğrulanmadı.

| Kontrol | Durum | Kanıt |
|---|---|---|
| Resmi MSAL4.89.0 yetkilendirme URL'si, PKCE S256, form_post, state, sabit tenant/client/scope ve localhost | PASS | Bağımsız root SDK probe |
| Gerçek SDK giriş iptali, yanlış state ile sentetik code reddi, access_denied ve her durumda listener kapanışı | 3/3 PASS | Temp/bitigmail-task015-qa/msal-probe-report.json |
| Backend tüm testler, eski regresyonlar dahil | 278/278 PASS, 0 atlanan; derleme 0 hata/uyarı | backend-final.json |
| Root güvenlik policy testleri | 26/26 PASS | Microsoft365PolicyTests.cs |
| Root kayıt/iptal/refresh/yeniden bağlama bütünlüğü | 13/13 PASS; tamamlanmış bağlantı iptal edilmiş gösterilmez | Microsoft365LifecycleCriticalTests.cs |
| Gerçek korunan API üzerinden SDK başlat/iptal/reddet ve kapsam/sır koruması | 138/138 PASS, son normal host 6174 | api-oauth-real-msal-report.json |
| API sorgulanmadan 5 dk expiry ve listener kapanışı | PASS, durum sorgusundan önce listener kapalı | api-oauth-expiry-report.json |
| Gerçek IMAP hesap API regresyonu | 120/120 PASS | api-accounts-report.json |
| Gerçek IMAP aktarım ve bağımsız içerik/ek karşılaştırması | 155/155 PASS; eksik/fazla 0, kaynak değişmedi | transfer-b039bd936274 kanıtları |
| Normal host IMAP sınırları | 4/4 PASS | normal-imap-boundary.json |
| Frontend testleri, typecheck, lint, build | 83/83 PASS; diğer kontroller PASS | TASK-015 sonucu; 531.98 kB paket boyutu uyarısı |
| Arayüz masaüstü/390px mobil yerel form ve yardım | PASS; parola alanı yok; yerel form kontrolünde konsol hatası 0 | acceptance-summary.json |
| LAN önizleme son üretim dosyaları | HTTP 200; dağıtım HTML'iyle birebir eşleşme ve JS 200 | lan-final-build.json |
| Canlı Microsoft OAuth login, klasör okuma ve iki yönlü aktarım | YAPILMADI | Test hesabı ve kayıt kimlikleri gerekiyor |

Root SDK deneyi canlı Microsoft hesabına giriş yapmadı, tarayıcı açmadı, token almadı ve hiçbir posta kutusuna bağlanmadı. Yetkilendirme URL'si gerçek resmi SDK tarafından üretildi. Yerel callback'e gönderilen hata/sentetik yanlış-state yanıtları yalnız testti. İlk araştırma harness'i GET callback'in yeni SDK tarafından reddedildiğini ortaya çıkardı; son kabul form_post kullanır. Consent-denied ile yanlış-state birleşiminde SDK önce hatayı işler; yanlış-state kabul testi bu nedenle sentetik code + yanlış state kullanır ve `state_mismatch` doğrular. Ürün protokolü değiştirilmedi.

Kalıcı kanıtlar: `.codex-coordination/evidence/TASK-015/`; dosya bütünlük listesi `SHA256.json`. SOL sonucu `.codex-coordination/results/TASK-015.md`. Geçici test dosyalarının gerekli raporları kalıcı alana kopyalandı.

Sınırlar: Microsoft giriş bağlantısını da inceleyen tarayıcı otomasyon komutu otomatik onay denetimi tarafından yalnız `blocked by policy` gerekçesiyle reddedildi ve tekrar çalıştırılmadı. Daha dar yerel tarayıcı kontrolü yalnız boş formu ve kurulum yardımını açtı; OAuth başlatmadı. Masaüstü ekran görüntüsü araç tarafından kırpıldı; tam genişlik DOM sınırlarıyla kontrol edildi. Telefon/LAN adresi arayüz önizlemesidir; yerel hesap API'sine bağlanmaz. Gerçek giriş ve işlemler motorun bulunduğu bilgisayarda yapılır.

Canlı Microsoft klasör/APPEND/flag/keyword davranışı, iki yönlü aktarım bütünlüğü ve tenant politikaları henüz kabul edilmedi. Google OAuth, paylaşılan kutu, app-only, şirket içi Exchange ve 100 GB ölçek bu teslimin dışında kalır.

Yapım sözleşmesi: [MICROSOFT365_CONNECTION_WORKFLOW.md](MICROSOFT365_CONNECTION_WORKFLOW.md). Kullanıcı hazırlığı: [MICROSOFT365_SETUP.md](MICROSOFT365_SETUP.md).
