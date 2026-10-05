# BitigMail — TASK-014 IMAP hesapları ve filtreli kopyalama

Durum: ACTIVE. Kullanıcı 13 Eylül 2026 tarihinde başlamayı onayladı.

## Kullanıcıya teslim

Müşteri/projeye kayıtlı IMAP hesapları, bağlantı sınaması, gerçek klasör ve ileti sayıları. Aktarım ve dönüşüm > Taşıma içinde gerçek IMAP akışı: kaynak ve hedef hesap, birden fazla klasör, hedef klasör eşlemesi, isteğe bağlı kapsayıcı tarih aralığı, sunucudan alınan önizleme, başlatma, ilerleme, kesintiden kontrollü devam ve indirilebilir kalıcı rapor. Her hesap iki yönde de kullanılabilir. Müşteriler hesap dizinidir; aktarım ayrı sekmededir. Eski örnek taşıma ekranı açıkça örnek mod olarak erişilebilir kalır.

Bu adım IMAP kullanıcı/parola bağlantısıdır. OAuth, Google Workspace ve Exchange'e özel yetkilendirme/etiket modelleri, POP, sürekli eşitleme ve dosya↔hesap daha sonraki aşamalardır. Kaynaktaki iletiler kopyalanır; silme/EXPUNGE/SMTP yoktur. Aspose bu yolda kullanılmaz; eski PST değerlendirme sınırları değişmez.

## Mimari kararlar

- MailKit 4.17.0 ve tam MimeKit 4.17.0 kullanımı onaylıdır. Mevcut MimeKitLite 4.17.0 referansı tam MimeKit ile değiştirilir; iki MimeKit assembly yan yana bırakılmaz. Aspose 24.8.0 ve System.Formats.Asn1 8.0.1 sabit kalır. Restore/bağımlılık denetimi ve eski MIME testleri zorunludur. Lisans bildirimleri güncellenir.
- MailKit APPEND, MimeMessage yeniden yazımı yapar. Gate A: gerçek kaynak FETCH baytları ile gönderilecek DOS satır sonlu FormatOptions çıktısı birebir karşılaştırılır (HiddenHeaders boş, EnsureNewLine true (MailKit CreateAppendOptions bunu zorunlu kılar)). Yalnız eşit içerik yazılır; farklılıkta önizleme/iş engellenir. Gizli reflection/internal API/elle IMAP protokolü eklenmez. Üründe de aynı kontrol ve hedeften BODY.PEEK tam bayt SHA-256 doğrulaması uygulanır. Kaynak kimliği Message-ID değildir: hesap + tam klasör + UIDVALIDITY + UID + SHA-256.
- UI'dan bağımsız yerel worker. Tek ortak aktif iş sınırı eski dönüşüm/bölümleme ile paylaşılır. JobManager partial uzantısı kabul edilir; özel kalıcı IMAP plan/journal ve ortak LocalJobRecord/jobKind=imap-transfer, raporda ImapTransfer detayı. Eski PST alanları IMAP için doğrulandı gibi gösterilmez.
- Önizleme sunucuda dondurulur: şirket/proje kimliği, hesap sürümleri, kaynak UIDVALIDITY/UID/özet/tarih/flags/keywords, klasör eşlemesi ve hedef. Başlat isteği yalnız previewId/idempotencyKey taşır; istemcinin adet/UID/host beyanına güvenilmez. Aynı idempotency farklı planla 409. Boş klasör listesi boş seçimdir ve engellenir.
- Tarih filtresi özgün MIME Date ve açık timezone, UTC+03 kapsayıcı günler; IMAP INTERNALDATE ayrıca korunur, filtreye sessiz ikame edilmez. Eksik tarih filtrede dışlanır ve sayısı raporlanır. Exact folder seçimi; alt klasörler kendiliğinden eklenmez. Özgün 12 fiziksel ileti (iki aynı bayt kopya ve aynı Message-ID farklı içerik dahil) ayrı öğe olarak korunur.
- Kaynak EXAMINE/read-only + BODY.PEEK. İş başlamadan seçili kaynak UIDVALIDITY/UID/özet ve hesap sürümü tekrar kontrol edilir; değişiklikte yazmadan engelle. Başarı öncesi kaynak ve hedef yeniden okunur. Önizlemeyi takip eden yeni kaynak iletileri bu dondurulmuş işe alınmaz.
- Hedefte Seen/Answered/Flagged/Draft ve kaynak özel keyword'ler destek doğrulamasıyla korunur. Recent aktarılmaz; Deleted seçili öğe bu sürümde engellenir (hedefte sonraki silinmeyi tetiklememek için). Desteklenmeyen flags/keywords sessizce kaybolmaz.
- Güvenli devam için hedef klasör kalıcı özel keyword desteği gerektirir. Her fiziksel öğe için rastgele 128-bit BitigMail işlem keyword'ü APPEND ile birlikte eklenir; bu keyword hedefte kalır ve raporda açıklanır, içerik değiştirilmez. Token kaynak Message-ID'den türetilmez.
- Journal atomik dosya değiştirme + Flush(true): Planned -> AppendIntent (token ve source/target UIDVALIDITY, hash, flags/date) diske yazılır -> APPEND -> target UID/verified -> durable Verified. İstemciye tamamlandı ancak rapor+job diskteyken gösterilir. Disk hatası yeni APPEND'i engeller, daha önceki hedef UID'leri kaybolmaz; ortak slot sonunda bırakılır.
- Yeniden başlatmada çalışan iş Interrupted. Devamda önce hedef token aranır: tam bir eşleşme ve hash/date/flags doğruysa uzlaştırıp devam; birden çok eşleşme veya fark NeedsAttention. AppendIntent ve sıfır eşleşme belirsizdir: otomatik yeniden APPEND YOK, açık inceleme gerektirir. Planned öğe güvenle başlayabilir. Önceden Verified hedef silinmiş/değişmiş veya UIDVALIDITY değişmişse dur. Tekrar deneme yeni kopya üretmemeli. Kaynak değişimi, çoklu çağrı, disk yazım arızası ve bağlantı kopması deterministik test edilir.

## Güvenlik sınırı

- Hesap metadata + parolalar yalnız backend tarafından yönetilir; parola Windows DPAPI CurrentUser ile saklanır. Windows dışı veya koruma hatasında düz metne fallback yok. API/list/job/report/log/localStorage parolayı içermez. Giriş alanı password, gönderimden sonra temizlenir. Hesap create/update yanıtı public DTO'dur; ham exception/IMAP protokol logları kullanıcıya verilmez.
- Host yalnız DNS adı/IP, port 1..65535, CR/LF/control/path/URL reddi; kullanıcı/parola ve klasörlerde protokol kontrol karakterleri reddedilir. TLS üretimde zorunlu: SslOnConnect veya StartTls; Auto/StartTlsWhenAvailable/downgrade/cert bypass yok. Normal sertifika+hostname doğrulaması korunur.
- Plaintext istisna yalnız TestingHost DI politikasındaki tam 127.0.0.1:5143 laboratuvar endpoint'i; istemci bayrağı production politikasını değiştiremez. Normal host test setup/fault routes içermez.
- Mevcut Origin/Host/session/JSON/no-store sınırları bütün yeni endpointlerde korunur. Şirket/proje uyuşmazlığı backend'de reddedilir. Yerel tek kullanıcı modeli tenant yetkilendirmesi olarak pazarlanmaz.
- Yeni lab eski Outlook laboratuvarından ayrıdır; yalnız loopback. Mevcut profiller/veriler değiştirilmez. Gerçek müşteri hesaplarına bağlanılmaz. Test hesapları sunucuda hazırlanır, test-only seed endpoint public DTO döndürür; secret dönmez.

## Kabul

1. İki gerçek lab hesabı ile bağlantı/klasör; yanlış parola güvenli hata, secret disk/API/log sızıntısı yok, DPAPI restart ve yanlış kapsam reddi.
2. 12 fiziksel ileti/4 ek/1 inline CID/Türkçe klasörler tam aktarım; seçili İstanbul 2024-01-01..2024-02-16 = 3 ileti/1 ek; ters yön ayrı hedef klasöründe denenir. Python stdlib IMAP/FETCH + MIME parser bağımsız multiset/body/attachment/header/date/flags oracle, kaynak değişmeden.
3. Resume: verified öğeler atlanır; APPEND sonrası yanıt kaybında keyword uzlaştırma tek kopya; sıfır/çoklu token belirsizliği tekrar yazmadan durur. Yeniden başlatma, UIDVALIDITY, kaynak değişimi ve persistence fault testleri.
4. Gerçek API/Playwright tam akış, müşteri değişince dondurulmuş rapor, geçmişten açma, masaüstü/dar ekran ve hata ekranları. Typecheck/lint/build, mevcut backend/frontend regresyonları.
5. Belgelerde doğrulanan destek ve ölçülmemiş sınırlar ayrı. 100 GB/hasar kurtarma/provider desteği iddiası yok.

## Birincil teknik dayanaklar

- [MailKit 4.17.0 proje kaynağı](https://github.com/jstedfast/MailKit/blob/4.17.0/MailKit/MailKit.csproj): net8 hedefi ve MimeKit bağımlılığı.
- [MailKit APPEND literal](https://github.com/jstedfast/MailKit/blob/4.17.0/MailKit/Net/Imap/ImapLiteral.cs): DOS satır sonlarıyla MimeMessage.WriteTo kullanımı.
- [MimeKit 4.17.0 serialization](https://github.com/jstedfast/MimeKit/blob/4.17.0/MimeKit/MimeMessage.cs): yeniden yazımın içerik üzerindeki etkisi kontrol edilmelidir.
- [RFC 4315 §3](https://www.rfc-editor.org/rfc/rfc4315.html#section-3): APPENDUID/UIDVALIDITY ve hedefte FETCH/SEARCH ile bulma; kimliksiz tekrar yazma için exactly-once garantisi değildir.




## TASK043 — açık şifresiz bağlantı seçimi (21 Eylül 2026)

Önceki üretimde TLS zorunluluğu, yalnız parola ile bağlanan IMAP/POP hesapları için kullanıcı isteğiyle genişletildi. Varsayılan SSL/TLS, alternatif zorunlu STARTTLS olarak kalır. `none` modu hesap bazında `allowUnencryptedConnection: true` gerektirir; bu seçim kalıcı hesap kaydında tutulur. IMAP sunucusu veya portu değişince yeniden açık seçim gerekir; TLS moduna geçilince izin temizlenir. Kayıtlı bağlantı testi, klasör keşfi ve aktarım aynı seçimi kullanır. OAuth sağlayıcıları sabit TLS uç noktalarını kullanmaya devam eder. Otomatik TLS düşürme, isteğe bağlı STARTTLS veya sertifika denetimini kapatma yoktur.

Doğrulama: izin verilmeden ret, yeniden başlatma sonrası izin, sunucu değişiminde yeni onay, TLS dönüşünde temizleme, geçersiz mod reddi ve üretim ilkesiyle yerel POP sunucusundan 12 iletinin silinmeden indirilmesi testleri geçti. Gerçek müşteri sunucusu testi henüz yapılmadı.
