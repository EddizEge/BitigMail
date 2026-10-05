# Aşama8 — Yerel yetki sınırları

2026-09-15 yönetici tasarımı. Henüz uygulanmış yetkilendirme değildir. Mevcut SessionManager rastgele oturum anahtarı tutar; uygulama kullanıcısı, süre ve rol yetkisi henüz sağlamaz.

| İşlem grubu | Sunucuda gereken kapsam | Önemli negatif kabul |
| --- | --- | --- |
| İlk kurulum / kullanıcı yönetimi | Tek kullanımlık yerel kurulum kanıtı / yönetici | HTTP oturum açma tek başına ilk yönetici yaratamaz; kurulum yarışında tek yönetici |
| Müşteri / proje kataloğu | İzinli şirket; proje o şirkete ait | Başka şirketin proje kimliği kabul edilmez |
| Dosya / hedef seçici ve handle | Gerçek kullanıcı, oturum ve plan kapsamı | Başkasının geçerli handle'ı veya eski oturum handle'ı reddedilir |
| Hesaplar / OAuth işlemleri | Hesabın gerçek kayıtlı şirket/proje erişimi | İstek query'sine kendi şirketini yazmak başka hesap erişimi vermez |
| Analiz / önizleme / başlangıç | Kaynak+hedef+plan+başlatan kullanıcı | Önizleme sonrasında kapsam değişikliği veya plan kimliği değiştirme reddedilir |
| Kuyruk / devam / iptal | Kayıtlı işin yetkisi; dispatch tekrar kontrolü | Rolü kaldırılan kullanıcı yeni iş çalıştıramaz veya devam ettiremez |
| İş geçmişi / sayfalama / rapor | İzinli işlerde sunucu filtresi | Sonuç sayısı, arama ve sayfa toplamı başka şirket bilgisini sızdırmaz |
| Arşiv kataloğu / manifest / arama / ileti | Her seçili arşivin gerçek kayıtlı kapsamı | Seçili arşivlerden biri yetkisizse sessizce genişletme olmaz |
| İndeks yenileme / tümünü yenileme | Yalnız izinli arşivler; işletim rolü | Toplu işlem yetkisiz arşive atlamaz |
| Yedek / geri yükleme / denetim | Yönetici + kaynak/hedef müşteri kapsamı | Paket içindeki companyId yetki yaratmaz; restore mevcut veriyi ezmez |

Doğrulama, handler'ın istekteki clientContext alanını onaylamasıyla bitmez. Kaydedilmiş kaynak/hesap/iş/arşiv sahipliği ile oturumun gerçek erişimi birleştirilir. Tüm endpoint aileleri tabloya bağlanır; yeni route güvenli varsayılanla kapalı olur. 404/403 tercihi varlık sızıntısını sınırlar ve tutarlı uygulanır.

Ürün tek Windows kullanıcısının verisi altında çalışır. Bu yerel rol modeli aynı Windows kullanıcısı yetkisiyle diski okuyabilen zararlı programa karşı işletim sistemi yalıtımı iddiası taşımaz. Merkezi ekip sunucusu için ayrıca mimari gerekir.

İlk kurulum kanıtı yalnız masaüstü kabuğunun sahip olduğu yerel kanalda oluşturulur; URL/query/log/ortak dosyaya sır bırakılmaz. Geliştirme sunucusu için açık ayrı development profili üretim izinlerini gevşetmez. Dev profilinden üretime veri geçişi açık yönetici işlemi ve doğrulanmış yeni katalog ile yapılır.

Masaüstü aynı origin UI/API sunarsa GET isteklerinin Origin başlığı her zaman bulunmayabilir. Geçerli oturum ve exact Host kontrolleri korunur; mutasyonlarda CSRF/Origin doğrulaması zorunlu. Statik UI yolu ile /api yolları ayrı politikadadır. Eksik Origin'i tüm API için koşulsuz açmak veya wildcard CORS eklemek çözüm değildir. Son seçilen WebView2 ağ düzeni üzerinde gerçek tarayıcı kabulü gerekir.

## 19 Eylül 2026 — Uygulama öncesi yönetici kararları

Bunlar kabul edilmiş uygulama değil, TASK038/040 için güvenlik sözleşmesidir.

- Üretim modu varsayılandır; geliştirme oturumu ancak açık ayrı profil ve ayrı veri dizininde kullanılabilir. Oturum açamayan istemciye eski anonim /api/session üzerinden yönetici verilmez. Yeni API uçları açıkça politika atanmamışsa reddedilir.
- İlk yönetici kurulum kanıtı, masaüstü ana sürecinden başlattığı işçiye devralınmış özel pipe üzerinden aktarılır. En az32rastgele bayt, tek kullanımlık ve kısa ömürlü; süreç argümanı, URL, günlük veya diskte açık metin bulunmaz. HTTP üzerinden yalnız kurulum-durumu sorgusu anonim olabilir, kurulum işlemi kanıt gerektirir. Kalıcı katalog kurulumu atomik tek kazanan olur. Genel erişilebilir porttan yerel olmak tek başına kanıt değildir.
- Parola için platform Identity PasswordHasher tercih edilir; sürümlü hash formatı, yeniden hash desteği, en az12karakter/üst uzunluk sınırı ve sınırlı eşzamanlı doğrulama. Hatalar kullanıcı var/yok ayrımı yapmaz. Denemeler kullanıcı+yerel profil bazında kademeli sınırlandırılır, kalıcı sonsuz kilit uygulanmaz.
- Oturum anahtarı rastgele32bayt; bellek içi saklama,8saat mutlak ömür ve30dakika etkin olmama sınırı. Yeniden başlatmada tüm oturumlar iptal olur. Çıkış, parola değişimi ve rol değişimi kullanıcı güvenlik sürümünü değiştirir. Browser localStorage'a uzun ömürlü kimlik bilgisi koyulmaz.
- Kaynak/çıktı handle, önizleme ve OAuth operation hem oturuma hem kullanıcıya bağlanır. Oturum değişince eski handle yeniden kullanılamaz. Kalıcı işte kullanıcı kimliği ve şirket/proje ayrıca saklanır; kullanıcı şirket erişimini kaybettiğinde dispatch/resume reddedilir. Çalışan kaynak-korumalı iş güvenli bitişine devam edebilir; yeni okuma/indirme yetkisi doğmaz.
- Operatör izinli müşteri projelerinde aktarım/arama yapabilir. Kullanıcı, rol ve şirket izin yönetimi; yedek/geri yükleme; global denetim yöneticiye aittir. Son etkin yöneticinin silinmesi veya yetkisinin alınması reddedilir. Arşivleri olan müşteri/proje silinmez; pasifleştirme ve açık veri taşıma akışı gerekir.
- Sunucu gerçek kaynak kayıtlarının kapsamını çözer; sorgu şirketi yalnız filtre olabilir. İş listesi ve toplamları yetki uygulandıktan sonra hesaplanır. Birden çok seçili kaynakta bir yetkisiz kaynak bütün isteği reddettirir. Bilinmeyen ve yetkisiz kaynaklarda tutarlı404 kullanılır.
- Masaüstü statik UI aynı loopback origin üzerinden sunulabilir. Tam Host eşleşmesi her yerde; API session zorunlu; mutasyon exact Origin ve JSON ister. Origin'siz GET yalnız oturumlu güvenli okuma olabilir. Statik SPA fallback hiçbir /api veya bilinmeyen dosya yolunu HTML ile başarıya çeviremez.
- Mail önizlemesinde mevcut React metin gösterimi korunur. Ham HTML, script, uzaktan resim ve ek içeriği masaüstü ayrıcalıklı sayfasında çalıştırılmaz. WebView dış gezinme/new-window varsayılan engelli; sistem tarayıcısına yalnız açık izin verilen HTTP(S) kullanıcı eylemi gider. file/javascript/data/custom protocol çalıştırılmaz.
- Eski geliştirme verisi olduğu gibi korunur; üretim kataloğuna sessizce sahiplik atanmaz. Açık yöneticili import veya ayrı geliştirme profili; eski verinin silinmesi yok.

TASK038 uygulayıcısı somut sözleşmeyi bu sınırlara göre sunar; yönetici yalnız farklılıkları değerlendirir. Deterministik uç nokta envanteri testi her /api rotasının public/setup/authenticated/admin ve scope politikasını kanıtlamalıdır; henüz yazılmamış gelecek rotalar için test başarısı iddia edilmez.

##20Eylül — Aşama7 sonrası yeni uç nokta kapsamları

TASK038 runtime route inventory must include the new037families, not only old76routes:
- Transfer templates: contain user-entered filter text, possibly personal identifiers. Production templates are creator-user owned by default; list/apply/delete resolve stored creator, not requestuserId. Admin may explicitly manage all. A template is configuration, never a capability granting source/company access. Re-preview under fresh callerpermissions. Existingglobaldevtemplate store stays in separatedevprofile; no implicitimport. Store wrapper keepscreator apartfromTemplate's portableconfiguration.
- Archive selected-result preview/start/report: validate every selected storedarchiveowner andtargetproject plusactor. Bind preview tosession/user; deny mixed unauthorizedselection wholesale. Query/manifest/filter/mapping/dedup fingerprints mustnotreplace auth.
- Waitingpriority/cancel/resume: samepermission asrecordedjob, not arbitraryclientcompanyquery. Revoke access before dispatch preventsqueuedstart. Recoveryjobs and selectedarchive jobs sharethis rule.
- SDKlicense choose/save isadmin-only; status authenticated andcontainsnopath/licensebytes. OLM/EMLX/POP/recovery/nativefilepickerhandles bindactor/session justlikeMimeandPST.
- Multi-scope archive advancedsearch appliesauthorization BEFORE counting/scanning/pagination; queryfiltercannotgrantaccess. Raw-MIME body scan honors cancellationand64MiBpermessage gate; no unauthorized metadata throughunknowncount/warning.

These are architecture requirements, not evidence that currentanonymousdevelopmentendpoints alreadyenforceidentity.
