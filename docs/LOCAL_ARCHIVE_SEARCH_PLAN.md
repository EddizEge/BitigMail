# Gerçek yerel arşiv ve çok müşterili arama — sıradaki onaylı kapsam

2026-09-14, PLAN. Kullanıcı yerel iş sıralamasını onayladı. TASK-017 köprüsü kabul edildikten sonra ayrı görev olarak uygulanacak; bu dosya çalışan arşiv/arama motoru iddiası değildir.

## Kullanıcıya teslim

Arşiv ve arama sekmesinde gerçek EML/mboxrd arşivlerini müşteri → proje → arşiv/kaynak ağacına kaydetme, birden fazla şirket/proje/arşivi aynı anda seçme, başlık/gönderen/alıcı/gövde/ek adı/tarih/ek varlığı filtreleri, sayfalı sonuçlar ve güvenli ileti önizlemesi. TASK017 ile hesaptan alınan arşivler tekrar dosya seçme zorunluluğu olmadan, açık kullanıcı eylemiyle aynı kataloğa alınabilir. Kaynak dosya veya posta kutusu silinmez. Gerçek ve örnek arama modları ayrılır; gerçek sonuçta her satırın şirket/proje/arşiv/özgün klasör kimliği görünür.

## Karar yönü

- Yerel yönetilen arşiv: onaylı dosya seçicisinden alınan ham EML veya mboxrd fiziksel kayıtları yeni arşiv alanına kopyalanır; kaynak SHA ve hedef SHA doğrulanır. Arşiv verisi ve indeksi uygulama bulutuna gönderilmez. Fiziksel kopyalar ayrı öğelerdir; Message-ID dedup anahtarı değildir.
- İlk gerçek indeks için SQLite FTS5 ve Microsoft.Data.Sqlite tercih edilir. Root izole uyumluluk kapısı geçti: Microsoft.Data.Sqlite **10.0.12**, net8.0, native SQLite **3.53.3**; dört Türkçe FTS sorgusu ve transaction rollback olmak üzere **5/5 PASS**. NuGet doğrudan+geçişli bilinen güvenlik açığı taraması bulgu döndürmedi. Yalnız `.codex-coordination/task018-sqlite-probe` içinde denendi; ürün paketleri henüz değiştirilmedi. Bu sabit sürüm TASK018 uygulaması için root tarafından kabul edildi; dağıtım bildirimi ve ürün içinde regresyon uygulama görevinde tamamlanacak. Microsoft'un varsayılan paketi FTS5 içeren SQLitePCLRaw bundle kullanır: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/custom-versions .
- İndeks kaynak verinin yerine geçmez; schema version ve arşiv manifestleriyle yeniden kurulabilir. Import staging + flush + içerik doğrulama + transaction/atomik yayım gerekir; yarım arşiv tamamlanmış sonuç olarak görünmez. İndeks hata/bozulması sıfır sonuç olarak gösterilmez.
- API sorgusunda her seçili arşiv company/project/registered archive ID olarak doğrulanır. Boş seçim tüm kaynaklar anlamına gelmez. Sorgu toplamları, sayfalar, snippet ve preview yalnız seçili kapsamdan gelir. Yeni seçimden sonra eski async yanıt ve odaklanmış mesaj temizlenir. Yerel kullanıcı modelini kurumsal rol yetkilendirmesi olarak sunma.
- SQL parametreli; FTS kullanıcı sorgusu varsayılan düz metin arama terimleri olarak güvenli biçimde derlenir, kullanıcı SQL/FTS programı çalıştırmaz. Sorgu uzunluğu, sayfa boyutu, içerik/ileti büyüklüğü ve eşzamanlı index işleri sınırlı ve görünürdür.
- Türkçe arama davranışı açık test edilir (İstanbul/istanbul, IĞDIR/ığdır, Unicode birleşik karakterler). SQLite NOCASE/LIKE yalnız ASCII harflerde case folding sağlar; buna Türkçe kapsamı için güvenilmez: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/collation . Normalleştirilmiş arama alanı ayrı tutulur, özgün içerik değişmez.
- Önizleme raw HTML çalıştırmaz, uzak içerik yüklemez. Ek adı araması ilk kapsamdadır; ek dosyalarının içindeki metni indeksleme ayrıca değerlendirilir. Arşiv/mesaj yolları API istemcisinden doğrudan alınmaz.

Uygulama sözleşmesi `docs/LOCAL_ARCHIVE_SEARCH_WORKFLOW.md` olarak hazırlandı. Genişletilmiş izole FTS kapısı **10/10 PASS**: Türkçe örneklere ek İngilizce INVOICE/invoice, birleşik Unicode, literal FTS operatörü/asterisk ve rollback. Karar: yalnız arama kopyasında NFC + tr-TR lowercase ardından `ı→i`; I/İ/ı/i aramada aynı kabul edilir, özgün ileti değişmez. Diğer diakritikler korunur. Bu ürünün gerçek arşiv/arama kabulü değildir; henüz üretim arama motoru uygulanmadı.

## Kabul yönü

İki farklı şirket, en az üç arşiv (EML/MBOX ve TASK017 export), aynı Message-ID ve aynı raw bytes içeren fiziksel kopyalar. Çoklu kapsam sayımları; sıfır seçim; başka şirkete taşan preview/sayım engeli; Türkçe/tarih/ek filtreleri; kaynak ve arşiv hashleri; indeks sırasında kesinti/disk arızası; restart ve reindex sonrası aynı sonuçlar; gerçek API+masaüstü/telefon UI. Büyük ölçek iddiası ayrı kademeli deneylerden sonra yapılır.

Ardından 72 ileti sınır testi üzerine daha büyük yapay veriyle süre/bellek/disk ölçümü, aktarım ve arşiv indekslemede kesintiden devam, rapor tutarlılığı. PST/OST 100 GB deneyi mevcut Aspose değerlendirme sınırlarıyla kabul edilemez; lisans/temsilî veri bağımlılığı açık tutulur. Yerelde yapılabilir aşamalar sonunda Azure ve silmesiz gerçek Outlook pilotuna dönüş sohbet içinde hatırlatılır; kullanıcı isteğiyle zamanlayıcı kullanılmaz.
