# Kişisel Outlook bağlantısı doğrulaması

TASK-016, başlangıç 2026-09-13, yerel kabul 2026-09-14. Durum **LOCAL_READY / LIVE_PERSONAL_PILOT_PENDING**; gerçek kullanıcı hesabı bağlanmadı.

| Kontrol | Sonuç |
|---|---|
| Root kişisel authority / kimlik sınırı | 18/18 PASS |
| Resmî MSAL 4.89 consumers URL, PKCE ve yerel iptal/hatalı-state/izin-reddi | 3/3 PASS; listener kapandı |
| Tüm backend testleri | 305/305 PASS, 0 atlanan; derleme 0 hata/uyarı |
| Kişisel hesap gerçek MSAL API kontrolleri | 147/147 PASS, normal motor 6174 |
| Kurumsal bağlantı API regresyonu | 129/129 PASS, aynı güncel motor |
| Frontend test/typecheck/lint/build | 90/90 PASS; typecheck/lint/build PASS; paket boyutu 537.86 kB uyarısı |
| Yerel form masaüstü/mobil kontrolü | 1660 ve 390 px PASS; kişisel formda tenant/parola alanı 0; son konsol hata/uyarı 0 |
| Gerçek Outlook giriş / IMAP / çift yönlü aktarım | Çalıştırılmadı |

SDK ve API deneyleri tarayıcı açmadı, Microsoft hesabına giriş yapmadı veya token almadı. MSAL tarafından oluşturulan kısa ömürlü consumers akışı yerel test callback'leriyle iptal/reddedildi. Gerçek posta sunucusuna bağlanılmadı. Kalıcı kanıt alanı `.codex-coordination/evidence/TASK-016/`; raporların özetleri `acceptance-summary.json`, dosya bütünlük listesi `SHA256.json` içindedir. API kontrol sayıları durum sorgusu sayısını da içerir.

Arayüz kontrolü gizli uygulama içi tarayıcıda yalnız müşteri ayrıntıları, boş hesap formu, hesap türü seçimi ve kurulum yardımıyla yapıldı. Alan doldurulmadı veya OAuth başlatılmadı. İlk girişimde durmuş önizleme sunucusu bağlantıyı reddetti; sunucu yeniden başlatıldıktan sonra normal sayfa açıldı. Araç eski hata sayfasının data URL'sini seçmeyi reddetti; bu sayfa incelenmeden normal yerel uygulama sekmesi kullanıldı. Telefon düğmelerinde saptanan kesilme ve fazla yükseklik düzeltildi; son kişisel düğme yüksekliği 40 px, içerik genişliği 286 px ve taşma yok. Masaüstü genişliği/scrollWidth 1660/1660, mobil genişliği/scrollWidth 390/375. Son ekran görüntüleri konuşmanın araç çıktılarındadır; görünüm boyutu sıfırlandı ve test sekmesi kapatıldı.

Silme yasağı bu pilot için kalıcıdır. Aktarım adaptörünün mevcut kaynak okuma yolları ReadOnly açılır ve klasör kapanışlarında expunge=false kullanılır. Bu inceleme canlı sağlayıcı kabulü sayılmaz. Kullanıcının mevcut postaları üzerinde test veya otomatik temizleme yapılmadı; canlı pilot ayrı klasördeki yapay iletilerle yapılacak.

Kurumsal Microsoft 365 pilotu ayrıca beklemededir. Kişisel hesap desteği, müşteri şirketlerin tenant politikalarının kabulü anlamına gelmez. Uygun BitigMail uygulama kaydı ve kullanıcının bilgisayarda Microsoft oturumu açması hâlâ gereklidir.
