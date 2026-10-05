# BitigMail — Gerçek OST → PST deneyi

Tarih: 12 Eylül 2026. TASK-008: **DONE_WITH_DIFFERENCES — deney tamamlandı, tam içerik uygunluğu kabul edilmedi.**

**TASK-009 güncellemesi:** Aşağıdaki CID belirsizliği kapatıldı. Ham MAPI `0x3712001F` alanı kaynak ve çıktıda aynı `proje_logo_cid` değerini taşıyor; HTML başvurusu ve görsel de korunuyor. Eski kontrol kodu bulunmayan bir CLR özelliğine baktığından boş sonuç üretmişti. Dar okuyucu düzeltmesi sonrası 13/13 dönüşüm, dört ek ve CID kontrolü geçti. [Güncel CID/gövde kabul politikası](../../docs/CID_BODY_ACCEPTANCE.md). Bu sayfanın TASK-008 belirsizlikleri tarihsel bulgulardır; bütün MAPI alanları ve büyük dosya desteği yine onaylanmış değildir.

Kullanıcının hazırladığı gerçek Outlook OST kopyası, orijinal posta hesabına veya Outlook profiline bağlanmadan okunup yeni Unicode PST'ye yazıldı. Aynı işlem Outlook bulunmayan, ağ bağlantısı kapalı bir Linux ortamında da çalıştı. Küçük ve sağlıklı bu örnekte dönüşüm mekanizması doğrulandı; üretim motoru henüz seçilmedi.

## Dosyalar ve sonuç

- [Üretilen PST](output/genuine-full-converted-04.pst)
- [Windows koşusu ve alan karşılaştırması](output/genuine-full-converted-04-report.json)
- [Bağımsız libpff kontrolü](output/independent-libpff-full-04-report.json)
- [Profilsiz ve ağsız ortam koşusu](output/clean-container-full-04-report.json)

| Kontrol | Sonuç |
|---|---|
| Kaynak | `input/bitigmail-lab-full.ost`, 16.818.176 bayt |
| Gerçek biçim | OST; 64-bit, 4K sayfa; istemci alanı `0x4F53`, sürüm 36 |
| Okunan / yazılan | **13 / 13**, işlem hatası **0** |
| Test kapsamı | **12/12** temel ileti ve ayrı tutulan **1 Outlook sınama iletisi** |
| Dolu posta klasörleri | Gelen Kutusu **6**, Gönderilenler **3**, Projeler → İstanbul **4** |
| Kaynak değişmezliği | İşlem öncesi, sonrası ve saklanan kopyanın SHA-256 değerleri aynı |
| PST çıktısı | 271.360 bayt; Unicode PST |
| Bağımsız okuyucu | libpff, OST ve PST'de 13/13 ileti ve aynı klasör dağılımını doğruladı |
| Fiziksel kopyalar | Aynı içerikli iki ileti ve aynı Message-ID ile farklı içerikli iki ileti ayrı korundu |
| Eklerin dosya içeriği | Dört ekin ad, boyut ve SHA-256 değerleri eşleşti |
| Temel alanlar | Konu, Message-ID, gönderen, sıralı To alıcıları ve tarih denetimleri 12/12 geçti |
| Gövde akışları | Bağımsız okuyucunun her iki dosyadan çıkardığı ortak gösterimlerin satır sonları eşitlendikten sonraki SHA-256 değerleri eşleşti; PST'de 9 ek düz metin gösterimi oluştu |
| Profilsiz ve ağsız ortam | Resmî .NET runtime container'ında **13/13**, hata 0; kaynak değişmedi |
| Yanlış türde girdi | PST, OST işlemi için reddedildi; hedef dosya oluşturulmadı |
| Derleme | 0 hata, 0 uyarı |

Kaynak SHA-256: `b0801758a2e61d4ce6e86799701a81a7a60c38401f73b13c993d94c03a2ee57a`.

Çıktı SHA-256: `98c1971dbed4f504865b32922147244ede2ad669ad3694e0dce0997270d6ac44`.

## Tam kayıpsız dönüşüm neden henüz kabul edilmedi?

**EML → Outlook OST ve OST → PST ayrı aşamalardır.** İlk EML metni ile SDK'nın OST'den sunduğu düz metin karşılaştırmasında 8/12 eşleşme, dört gösterim farkı var. Bağımsız OST/PST çıkarımında ortak gövde gösterimleri korunuyor; dokuz iletiye ek düz metin gösterimi geliyor. İlk EML ile OST arasında oluşmuş gösterim farkları tek başına OST→PST veri kaybını kanıtlamaz.

**Satır içi görsel bağlantısı belirsiz.** `logo.png` dosyasının 73 baytı ve özeti aynen korunuyor. Ancak ölçülen MAPI `ContentId` alanı hem kaynak OST okumasında hem PST tekrar okumasında boş. Başlangıç EML'sindeki CID bağlantısının Outlook'a alma aşamasında mı değiştiği, SDK'nın alanı mı sunamadığı ayrıca incelenmeli. libpff dışa aktarımı bu özelliği doğrulamadığı için durum **UNKNOWN**; dönüşüm sırasında kaybolduğu kanıtlanmış değil. Görselin ileti içinde doğru yerde gösterildiği henüz kabul edilmedi.

**Bu gerçek koşuda değerlendirme filigranı gözlenmedi.** Önceki EML→PST hazırlık denemesinde gözlenen filigranlar bu koşunun sonucu olarak sunulmuyor. Kullanılan Aspose.Email 24.8.0 değerlendirme sürümünün klasör başına 50 öğe sınırı gibi belgelenmiş kısıtları sürüyor; bu örnek o sınırı aşmıyor.

Özel MAPI alanları, özgün taşıma başlığı akışı, sıkıştırılmış RTF eşitlemesi ve depolamaya özgü kayıt kimlikleri bütünüyle karşılaştırılmadı. Bu alanlara uygunluk onayı verilmedi.

## İlk kopyada neden yalnız 6 öğe vardı?

İlk sunucu hazırlığında özel klasörler oluşturulmuş fakat IMAP abonelikleri açılmamıştı. Kullanıcı Outlook'ta yalnız Gelen Kutusu'nu görmüştü. Aspose ve libpff, bu ilk OST'de aynı 6 öğeyi buldu: 5 temel ileti ve 1 Outlook sınama iletisi. Diğer 7 temel ileti sunucuda duruyordu.

Astra yalnız test klasörlerinin aboneliklerini açtı ve hazırlama betiğine üst/alt klasör aboneliklerini ekledi. Kullanıcı yeniden eşitleyip Outlook'u normal kapatarak ikinci kopyayı sağladı. İlk kopya `output/source-snapshot-01.ost`, tam kopya `output/source-snapshot-full-02.ost` olarak ayrı tutuldu. İletiler silinmedi veya yeniden yüklenerek çoğaltılmadı.

## Kullanılan ortam ve sıradaki iş

.NET SDK 8.0.425, Aspose.Email 24.8.0; bağımsız okuyucu Debian `pff-tools` 20180714-3+b2. Temiz dönüşüm ortamı resmî .NET 8.0.31 runtime imajı. Deneme sırasında ağ kapalı, kaynak salt okunur; Outlook profili veya hesap bilgisi bağlanmadı. İmaj kimlikleri ve bağımsız araç kaydı JSON raporundadır; çalıştırma kanıtları yerel TASK-008 sonucunda tutulur.

**Sıradaki dar iş:** satır içi CID alanının gerçek depolama özelliklerinden doğrulanması ve gövde gösterim kurallarının kabul ölçütlerine bağlanması. Bundan sonra lisanslı/büyük klasör deneyi, kesintiden devam ve kontrollü hasar testleri planlanabilir. 100 GB, bozuk dosya kurtarma ve tüm Outlook sürümleri bu deneyde sınanmadı.

Uygulama: Gemini; çalıştırma ve denetim: SOL. Deney kapsamı, kaynak bütünlüğü, abonelik düzeltmesi ve sonuçların kabulü: Astra. Gerçek müşteri verisi kullanılmadı; posta dosyaları dış servise yüklenmedi.
