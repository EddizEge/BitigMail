# BitigMail — Ana plan ve yol haritası

Son güncelleme: 2026-10-09 (sürüm 0.9.4)

Bu dosya projenin başlangıç noktasıdır: güncel aşama durumu, kalıcı ürün ilkeleri ve tarihli geçmiş kayıt. Daha ayrıntılı ve tarihli kabul listesi: [tam sürüm listesi](docs/FULL_RELEASE_ROADMAP.md). Kullanıcı rehberi: [docs/KULLANIM_REHBERI.md](docs/KULLANIM_REHBERI.md).

## Güncel durum

**Sürüm 0.9.4 (imzasız iç test sürümü).** Ekranlar sadeleştirildi: üst menü iş sırasında (Müşteriler → Aktarım ve dönüşüm → İş merkezi → Arşiv ve arama → Raporlar), her ekranda tek tip başlık ve açıklama, üç adımlı aktarım ekranı (işlemi seç → türünü/yönünü seç → yalnız o akışın adımları), boş kurulumda dört adımlı "Başlarken" kartı, oturum açılmış uygulamada örnek kayıt yok. 0.9.3'te oturum açıkken OST → PST, PST bölme ve EML/MBOX → PST ekranlarının motoru "çevrimdışı" sanması düzeltildi. Durum çubuğundaki sürüm derlemeden gelir.

Gerçek akışlar (yerel kabul): OST → PST; EML/mboxrd → PST; PST/OST bölme; EML/mboxrd ↔ IMAP; IMAP → IMAP kopyalama; POP → EML; PST/OST/OLM → EML; Apple Mail EMLX → EML; yönetilen yerel arşiv ve çok müşterili arama; arşivden seçili sonuçları EML'e çıkarma; hasarlı PST/OST'tan yeni EML klasörüne kurtarma. Kapsam ve sınırlar: [format ve yön matrisi](docs/FORMAT_DIRECTION_MATRIX.md).

Bağlantı güvenliği: parola tabanlı IMAP/POP'ta varsayılan SSL/TLS; zorunlu STARTTLS seçilebilir; şifresiz bağlantı yalnız hesap bazında açık onayla (TASK043). Kendiliğinden şifresize düşme yok.

Son tarihli tam test kaydı 0.9.3'e aittir: 21 Eylül 2026, 892 motor / 151 arayüz testi ([Windows kabul kaydı](docs/WINDOWS_RELEASE_ACCEPTANCE.md)).

### Aşama tablosu

| Aşama | Durum |
|---|---|
| 1 — İş deneyimi | Tamamlandı (TASK021): ilerleme, son doğrulama, desteklenen işlerde açık devam |
| 2 — Performans ve kuyruk | Yerel kabul (TASK022–029): tek çalışan iş, 32 bekleyen; yaklaşık 1 GiB ölçüm ve bütünlük. Büyük ölçek açık |
| 3 — Servis bağlantıları | Yerel kabul (TASK031): Microsoft ve Google OAuth altyapısı. Canlı sağlayıcı kabulü açık |
| 4 — Formatlar | Yerel kabul: Mac (OLM/EMLX) normalizasyonu, Outlook → EML, POP → EML, uyarıların sonraki işlere taşınması. Biçim ve tarih sınırları sürer |
| 5 — Üretim SDK ve ölçek | Lisans doğrulama altyapısı var. Ticari Aspose lisansı ve temsilî büyük dosya (10–100 GB) kabulü açık |
| 6 — Kurtarma | Yerel kabul: ayrı işçi süreç, kontrollü küçük hasarlar, öğe düzeyi rapor. Gerçek hasarlı geniş veri seti açık |
| 7 — Gelişmiş yönetim | Yerel kabul (TASK037): VE/VEYA filtre, klasör eşleme, şablon, seçili arşiv çıktısı, öncelik, yinelenme politikası |
| 8 — Kurumsal işletim | Yerel kabul (TASK038/039): yerel yetki, denetim kaydı, yedek/geri yükleme, saklama önizlemesi, teslim raporu |
| 9 — Windows ürünü | Kabuk, kurulum, güncelleme, geri dönüş, kaldırma yerel kabul. 0.9.3 (TASK043) ve 0.9.4 arayüz sürümleri. Kod imzalama ve temiz Windows kabulü açık |
| 10 — Pilot ve ticari 1.0 | Bilerek ertelendi: Azure uygulama kaydı ve gerçek kişisel Outlook hesabında kaynak silmeden pilot; diğer canlı sağlayıcılar; BT firması pilotları; lisans, fiyat, marka ve yayın kararları |

"Yerel kabul" geliştirme bilgisayarında testlerle doğrulandı demektir; gerçek sağlayıcı, lisanslı çıktı ve ticari yayın kabulü ayrı şeylerdir.

### Açık kapılar (1.0 öncesi)

- Kod imzalama sertifikası yok; temiz Windows kurulumunda kabul yapılmadı.
- Aspose deneme sürümü: PST çıktılarında değerlendirme işaretleri, klasör başına 50 öğeyi aşan PST/OST kaynaklarında engel (IMAP aktarımında yok).
- 10–100 GB ve gerçek hasarlı dosya kabulü yok.
- Microsoft 365, kişisel Outlook ve Google ile canlı pilot yok (10. aşama).
- MBOX yalnız mboxrd; OLM/EMLX özgün tarih anlamı çözülmedi.

## Ürün amacı ve kesinleşen kapsam


BitigMail, müşteri şirketlere hizmet veren BT firmalarının farklı posta dosyaları ve servisleri arasında iletileri taşımasını, dönüştürmesini, arşivlemesini, bölümlemesini ve kurtarmasını kolaylaştıracak bir Windows uygulamasıdır. Mac'ten gelen arşivleri de işlemesi hedeflenir.

- Çok yönlü kapsam: hesap ↔ hesap, dosya ↔ hesap, dosya ↔ dosya.
- Dosyalar: MBOX, PST, EML, OST, OLM.
- Servisler ve protokoller: IMAP, POP, Google Workspace, Exchange Online ve ihtiyacı ayrıca netleştirilecek şirket içi Exchange.
- Eski hesap/orijinal Outlook profili olmadan yalnız OST dosyasından PST dönüşümü ve bozuk PST'den kurtarma.
- Yaklaşık 100 GB'a ulaşan girdileri işleme; tarih/yıl, klasör ve boyuta göre arşiv bölümleme.
- Tarih, klasör, gönderen/alıcı, alan adı, konu, içerik, ek ve boyut üzerinden seçili aktarım.
- Aynı sağlayıcıdaki farklı hesaplar ve kurumlar arasında aktarım.

Bu liste ürün hedefidir. Bir biçimin okunması yazılabildiği anlamına gelmez. Destek her kaynak/hedef yönü ve sürüm için ayrı kanıtlanacak. Çok yönlü aktarım, sürekli çift yönlü eşitleme veya silme yayılımı taahhüdü değildir.


## Görsel ve kullanım temeli


- Adın standart yazımı: **BitigMail**.
- Beyaz çalışma yüzeyleri, sarı seçim vurguları, turuncu eylemler, koyu okunaklı metin.
- Logo: iki katlanmış belgeyi B harfinde birleştiren işaret.
- Kullanım referansı: MailStore Home'un klasör gezinmesi ve arama davranışları; ürün kimliği özgün.
- İş düzeni: Müşteri → Proje → Kaynak/hedef → İş planı → Çalıştırma → Rapor (0.9.4 üst menüsü de bu sırayı izler).
- Müşteriler sekmesi şirketleri, projeleri ve posta kaynaklarını gösterir; doğrudan tek posta kutusu açmaz.
- Aktarım ve dönüşüm ayrı ana sekmedir: solda kaynaklar; ortada liste, filtre ve altında önizleme; sağda aktarım planı. İş merkezi izleme ekranıdır.
- Arşiv ve aramada şirket → proje → posta kutusu/dosya arşivi ağacından birden fazla konum birlikte seçilir; sonuçlarda kaynak bağlamı korunur.
- Satır odağı/önizleme ile aktarılacak ileti seçimi birbirinden ayrı tutulur.
- Ön kontrolde çözülmemiş sorun varken aktarım başlatılamaz.

Referanslar: [görsel tasarım kaydı](docs/VISUAL_DESIGN_V1.md), [logo](design/concepts/bitigmail-brand-v1.png), [proje ekranı](design/concepts/bitigmail-workspace-v1.png), [İş merkezi](design/concepts/bitigmail-job-center-v1.png), [Ön kontrol](design/concepts/bitigmail-preflight-v1.png).


## Gerçek ürün için mimari ilkeler


1. Kaynağı koruyan kopyalama varsayılanı; kurtarma yeni çıktıya yapılır.
2. Arayüz ve arka plan işçisi ayrı sorumluluk taşır; iş ilerlemesi kalıcıdır.
3. Büyük içerik akışla işlenir, tüm arşiv belleğe alınmaz. Disk alanı gerçek motor davranışına göre hesaplanır.
4. Kaynak okuyucu, hedef yazıcı ve yetenekleri ayrı modellenir.
5. Plan sürümü çalıştırma başladığında sabitlenir; sonraki filtre değişikliği eski raporu değiştirmez.
6. Aktarılan, atlanan, kısmi, başarısız ve belirsiz sonuçlar öğe düzeyinde izlenir. Yeniden deneme kontrolsüz çoğaltmaya yol açmaz.
7. Doğrudan taşıma için zorunlu tam ara arşiv oluşturulmaz. Kalıcı yönetilen arşiv ayrı kullanım biçimidir.
8. Müşteri verileri ve bağlantıları ayrılır; sırlar raporlara yazılmaz. Gerçek kimlik bilgileri için Windows korumalı saklama tasarlanır.
9. İleti önizlemesi etkin içerik çalıştırmaz ve uzak görselleri kendiliğinden yüklemez.
10. Üretici bulutundan posta içeriği geçirmek varsayılan çözüm değildir; merkezi yönetim ayrıca kararlaştırılır.

Bu ilkeler bugünkü motorda uygulanır ve sonraki geliştirmede korunur.


---

# Geçmiş kayıt

> **Bu bölümün altı tarihli geçmiş kayıttır; güncel durum değildir.** Eylül 2026'da yazıldı ve o günkü durumu anlatır. Buradaki "henüz örnek veriyle", "gerçek posta hesabı bağlantısı yok", "Azure/Outlook Aşama 3" ve P0–P6 aşama adları gibi ifadeler sonradan aşıldı: arşiv/arama ve kurtarma bugün gerçek akışlardır; Azure ve kişisel Outlook pilotu 10. aşamadadır; bağlantıda SSL/TLS, STARTTLS ya da açık onayla şifresiz seçilebilir. Güncel durum için yukarıdaki bölümlere bakın. Çalışma düzeni ve ajan adları (ASTRA, SOL, Gemini) da eski düzene aittir.

## Geçmiş — Eylül 2026 durum kayıtları


**Son tamamlanan: Aşama 2 / TASK022–029 — performans ölçümleri, disk kontrolü, bellek iyileştirmeleri ve temel işlem kuyruğu.** Tek etkin işlem/32 bekleyen iş, sayfalı geçmiş, güvenli iptal ve yeniden açılış davranışları kabul edildi. Backend469/469, frontend143/143; üç ekran boyutu ve gerçek iki işlik sıra denemesi geçti. Yaklaşık1GiB testinde8192ileti/2464ek, MBOX/EML/arşiv/arama ve kaynak değişmezliği doğrulandı; eksik faz ölçümleri tamamlandı. Ayrıntılar: [Aşama 2 kapanışı](docs/STAGE2_COMPLETION_CHECKLIST.md), [tam sürüm planı](docs/FULL_RELEASE_ROADMAP.md).

**Aktif talimat: Aşama 3–9 boyunca sırayla geliştir ve doğrula.** Kullanıcı 2026-09-15 tarihinde Azure kurulumu ve gerçek Outlook aktarım testini Aşama 10'a taşıdı. Aşama 10 bu çalışmaya dahil değil. Windows uygulaması ve Aşama 9 kabulü tamamlanınca haber verilecek; rutin aşama onayı veya zamanlayıcı yok. Dış lisans/hesap/temiz Windows gerektiren kabuller kanıt olmadan tamamlandı sayılmaz. Güncel yürütme kaydı: [Aşama 3–9](docs/STAGES_3_9_EXECUTION.md).


**Önceki kabul: TASK-020 — MBOX bellek düzenlemesi ve yaklaşık 1 GiB veri bütünlüğü kabulü tamamlandı.** 8.192 ileti/2.464 ek, 24.858 bağımsız kontrol PASS; backend 445 test PASS. MBOX/EML çıktıları, yönetilen arşiv ve arama doğrulandı. 1 GiB performans ölçümü kısmi; 100 GB, hasarlı PST/OST ve canlı sağlayıcı kabulü açık. Sıradaki öneri: iş ilerlemesi/son doğrulama ve kesintiden devam deneyimi, eksik performans ölçümleri. Azure gerçek hesap pilotu kullanıcı isteğiyle sonraki aşamada manuel hatırlatılacak; zamanlayıcı yok.

**Tam sürüme kadar güncel yapılacaklar:** [Durum ve sürüm listesi](docs/FULL_RELEASE_ROADMAP.md). Aşağıdaki eski teslim paragrafları tarihsel kabul kapsamlarını anlatır; güncel durum için bu özet ve bağlantılı liste esas alınır.

**Tamamlandı: TASK-018 — gerçek arşiv ve çok müşterili arama.** Yönetilen EML/mboxrd ve tamamlanmış aktarım arşivleri; çoklu şirket/proje/arşiv seçimi, gerçek klasör/metin/tarih/ek filtreleri, güvenli önizleme, otomatik katalog yenileme ve İş Merkezi bağlantıları kabul edildi. Backend434/434, frontend129/129; bağımsız API649/649, indeks kurtarma83/83, gerçek EML kesinti203/203 ve MBOX kesinti75/75, masaüstü1660px/telefon390px gerçek akışlar geçti. Root, tekrarlanan AGY araç hatalarından sonra dar UI düzeltmesini tamamladı. [Kapsam](docs/LOCAL_ARCHIVE_SEARCH_WORKFLOW.md), [kabul ve sınırlar](docs/LOCAL_ARCHIVE_SEARCH_VALIDATION.md).

**Son teslim: TASK-017 — dosya ↔ posta hesabı köprüsü tamamlandı.** EML/EML ağacı/mboxrd → IMAP ve IMAP → EML/mboxrd, klasör/tarih seçimi, kalıcı plan/rapor, kesintiden devam ve İş Merkezi'nden geçmiş işe dönüş kabul edildi. Backend369/369 ve frontend110/110; gerçek genişletilmiş API540/540 + ayrı MBOX crash78/78; root ilkAPI190, sınır11+normal24, masaüstü1660/telefon390 akışları ve JobCenter restore geçti. 72 ileti/24 ek, fiziksel duplicate koruması, gerçek süreç çökmesi sonrası import/EML/MBOX devamı ve final kaynak12/4 değişmezliği doğrulandı. Bu EML/IMAP kabulü PST/OST deneme lisansı sınırlarını veya 100 GB/kurtarma belirsizliklerini kaldırmaz. [Kapsam](docs/FILE_ACCOUNT_BRIDGE_WORKFLOW.md), [kabul](docs/FILE_ACCOUNT_BRIDGE_VALIDATION.md).

**Microsoft OAuth altyapısı: TASK-015/016 yerel kabulü tamamlandı.** Bu aşamada 305 backend/90 frontend test ve yerel SDK/API/UI kabulü geçti. Gerçek Microsoft posta hesabıyla pilot yapılmadı. Kullanıcı Azure kaydını erteledi; bu yerel aşamalardan sonra canlı Outlook pilotuna dönüş aşama notu olarak korunur. Zamanlayıcı kullanılmaz.

**Önceki teslim: TASK-014 — gerçek IMAP aktarımı tamamlandı.** Müşteri/projeye bağlı IMAP hesap dizini, gerçek klasör sayıları, dondurulmuş önizleme, tam klasör ve dahil tarih filtresi, iki hesap arasında doğrulanan kopyalama, durable resume ve indirilebilir rapor çalışır. Gerçek Dovecot laboratuvarında filtreli 3 ileti/1 ek, tam 12/4 ve ters yön 3/1 bağımsız posta oracle'ıyla doğrulandı; resume deneyi 0 yeni kopyayla tamamlandı. [Kapsam](docs/IMAP_TRANSFER_WORKFLOW.md), [kabul kanıtları ve sınırlar](docs/IMAP_TRANSFER_VALIDATION.md).

**Önceki teslim: TASK-013 — EML/MBOX → PST tamamlandı.** Aktarım ve dönüşüm → Dönüştürme → EML / MBOX → PST ekranında çoklu EML, EML klasör ağacı veya tek mboxrd arşivi yeni PST'ye aktarılır. Klasör/tarih önizlemesi, yeni hedef, sabit müşteri/proje kaydı, kalıcı iş ve deneme farkları raporu çalışır. Oluşan PST mevcut Arşivleme ekranında bölümlenir. EML ve MBOX için 12 ileti/4 ek; filtreli ağaç için 3 ileti/1 ek; sonraki yıllık bölümlemede 12 ileti/4 ek/dört parça bağımsız okuyucuyla doğrulandı. Deneme işaretleri korunur; kayıp/ölçülmeyen değişiklik başarı sayılmaz. [Kapsam](docs/MIME_IMPORT_WORKFLOW.md), [kabul kanıtları ve sınırlar](docs/MIME_IMPORT_VALIDATION.md).

**Önceki teslim: TASK-012 — PST/OST arşivleme ve bölümleme tamamlandı.** Aktarım ve dönüşüm → Arşivleme ekranı gerçek PST/OST dosyalarından yıl veya gerçek dosya boyutu sınırına göre yeni PST parçaları oluşturur. Klasör/tarih seçimi, yeni hedef arşiv klasörü, tüm parçaların yeniden açılıp doğrulanması ve kalıcı çoklu dosya raporu çalışır. Kaynak dosyalar korunur. [Kabul sözleşmesi](docs/PST_SPLIT_WORKFLOW.md), [doğrulama kaydı](docs/PST_SPLIT_VALIDATION.md). Küçük test verileriyle kabul edildi; 100 GB performansı, kurtarma ve kesintiden devam henüz kanıtlanmadı.

**Önceki teslim: TASK-011 — tarih ve klasör filtreleri tamamlandı.** Gerçek OST→PST akışında çoklu klasör seçimi, Türkiye saatine göre dahil gün aralığı, güncel kaynağa bağlı ileti/ek önizlemesi ve kalıcı filtre raporu çalışır. Testte 13 kaynak iletiden seçilen üçü ve bir ek/CID yeni PST'ye yazıldı, on ileti dışarıda kaldı; bağımsız okuyucuda eksik/fazla ileti ve ortak gövde farkı yok. Kaynak değişmedi. [Kabul sözleşmesi](docs/FILTERED_OST_WORKFLOW.md), [doğrulama kaydı](docs/FILTERED_OST_VALIDATION.md). Bölümleme TASK-012 ile eklendi.

**Önceki teslim: TASK-009 ve TASK-010 tamamlandı.** Aktarım ve dönüşüm → Dönüştürme ekranı gerçek OST seçimi, analiz/ön kontrol, yeni PST, ilerleme ve kalıcı rapor sunar. İşin müşteri/proje bilgisi başlangıçta kaydedilir; İş merkezi eski bir gerçek işi doğru kimliğiyle açar. Çıktının tam konumu yenileme sonrası da görünür ve kopyalanabilir. Normal dosya seçimi Windows penceresidir; posta içeriği dış servise gönderilmez. Sözleşme: [ilk gerçek dosya akışı](docs/LOCAL_OST_WORKFLOW.md). Kanıtlar: [yerel OST doğrulaması](docs/LOCAL_OST_VALIDATION.md). Tarih/klasör filtreleri TASK-011 ile eklendi. Diğer formatlar/hesaplar ve 100 GB desteği henüz uygulanmış değildir; P2 bütünü ve üretim motoru seçimi açık kalır.

**TASK-009 alan doğrulaması:** Astra'nın salt okunur ham alan kontrolü, `0x3712001F` değerinin kaynak OST ve PST'de aynı `proje_logo_cid` olduğunu doğruladı. Satır sonları eşitlenen tam HTML hash'i, HTML içindeki CID başvurusu ve 73 baytlık görsel eşleşti. Gemini yanlış reflection okuyucusunu Unicode/ANSI özellik torbası okuyucusuyla düzeltti. Yeni koşu 13/13, dört ek ve CID kontrolünü geçti, kaynak değişmedi. Bağımsız libpff 13/13 ve dört ek hash'ini yeniden doğruladı. Dosyada CID kaybı yoktu; yanlış kontrol sonucu giderildi. [CID ve gövde kabul politikası](docs/CID_BODY_ACCEPTANCE.md). Önceki TASK-008 belirsizliği tarihsel sonuçtur.

**Son deney: TASK-008 — gerçek OST→PST, DONE_WITH_DIFFERENCES.** Kullanıcının tam OST kopyası (16.818.176 bayt), 12 temel ileti ve 1 Outlook sınama iletisiyle işlendi. Windows ve Outlook/profil bulunmayan ağsız Linux ortamında 13/13 öğe yeni PST'ye yazıldı; hata 0, kaynak SHA-256 değişmedi. Bağımsız libpff kontrolü 13/13 iletiyi, 6/3/4 klasör dağılımını, fiziksel kopyaları ve dört ekin ad/boyut/hash değerlerini doğruladı. Ortak gövde gösterimleri korundu; PST'ye 9 düz metin gösterimi eklendi. İlk EML metni ile OST okumasında dört gösterim farkı var. Satır içi görselin Content-ID alanı hem kaynak hem çıktıda ölçülemedi; bu, dönüşüm sırasında kayıp kanıtı değildir fakat tam içerik uygunluğu henüz kabul edilmedi. Bu gerçek koşuda değerlendirme filigranı gözlenmedi. [Deney sonucu ve kanıtlar](lab/ost-spike/OST_EXPERIMENT_RESULT.md). TASK-008 deneyi tamamlandı; P2 bütünü ve üretim motoru seçimi tamamlanmadı.

**Önceki hazırlıklar: TASK-006 ve TASK-007 tamamlandı.** Kullanıcı eski hesap/orijinal Outlook profili olmadan OST→PST gereksinimini doğruladı ve örnek dosyası olmadığı için yapay veri hazırlanmasını istedi. [Motor adayları](docs/OST_ENGINE_DECISION.md) doğrudan dönüşüm ile öğeleri okuyup yeni PST'ye yazmayı ayrı sınar. [Deneme kümesi](fixtures/mail-corpus-v1/README.md): 12 EML, MBOX, 4 ek, 3 klasör ve beklenen sonuç manifesti. İki bağımsız üretim 13/13 kontrolden geçti; 14 üretilen dosyada hash farkı 0. Üretim motoru seçilmedi.

**Son tamamlanan genel aşama: P1 — Kullanıcı geri bildirimleri uygulandı (TASK-005 DONE).** Müşteriler şirket/proje/kaynak dizinidir; Aktarım ve dönüşüm ayrı ana sekmedir. Arşiv ve arama birden fazla şirket, proje, posta kutusu ve dosya arşivini birlikte seçer; sonuçlar kaynak bağlamını gösterir. Örnek şirket/proje/kaynak ekleme ve seçim kalıcılığı çalışır. Ayrıntılı karar ve kabul ölçütleri: [ikinci yineleme](docs/PROTOTYPE_ITERATION_2.md). P1 teslimi örnek veriliydi; gerçek OST dönüşümü sonraki TASK-010 teslimiyle eklendi. Gerçek posta hesabı bağlantıları henüz yoktur *(o tarihte; sonradan IMAP, POP ve OAuth bağlantıları eklendi)*.

- Ürün gereksinimleri kaydedildi.
- BitigMail adı ve sarı/turuncu/beyaz kimlik seçildi.
- Logo, proje çalışma ekranı, İş merkezi ve Ön kontrol görselleri tasarım temeli olarak kabul edildi.
- SOL → resmi Antigravity CLI → Gemini arka plan iş akışı doğrulandı.
- OST/PST kaynak araştırması, küçük gerçek deney ve ilk yerel ürün akışı tamamlandı. CID ve ölçülen gövde/ek uygunluğu kabul edildi; tüm MAPI alanları ve üretim ölçeği henüz doğrulanmadı.
- P1 uygulama görevi: TASK-004 — DONE. Ana uygulama Gemini, son masaüstü yerleşim düzeltmesi Astra, denetim SOL tarafından tamamlandı.
- Son uygulama görevi: TASK-013 — DONE. İlk backend Gemini; kritik bütünlük/kalıcılık/API ve frontend Astra; deterministik denetim SOL. Gemini frontend çağrısı bireysel kota nedeniyle dosya yazamadı; kullanıcı protokolündeki çözülemeyen iş istisnasıyla Astra devraldı. Yeni Codex ajanı/görevi açılmadı.
- Güncel doğrulama: 152 backend, 56 frontend testi; tip kontrolü, lint ve üretim derlemesi PASS. Eski OST/arşiv mock regresyonları 30/30 (24 ilk koşu + altı açıkça simüle edilen çevrimdışı koşulun hedefli tekrarı); gerçek MIME UI 3/3, root filtreli MIME HTTP 25/25 ve normal motor erişim sınırları 37/37 PASS. Üç ekran boyutunda kaynak/filtre/rapor/geçmiş akışı ve sabit müşteri bilgisi doğrulandı; bozuk/boş/deneme sınırını aşan kaynaklar UI'da da engellendi. Bağımsız libpff özgün MIME gövdelerini, temel üstveriyi ve ekleri ölçülen değerlendirme ekleriyle doğruladı. Önceki TASK012 kanıtları korunur; ayrıntılar MIME kabul belgesindedir.
- Önizleme: http://127.0.0.1:5173/; normal yerel hizmet: 127.0.0.1:6174. Teslimde ikisi açık, test hizmeti 6175 kapalı. Yeniden başlatma: [çalıştırma kılavuzu](README.md). Güncel kanıtlar: [PST bölümleme doğrulaması](docs/PST_SPLIT_VALIDATION.md).

## Geçmiş — Aşamalar ve bitiş ölçütleri


| Aşama | Kapsam | Bitiş ölçütü | Durum |
|---|---|---|---|
| P0 — Ürün ve kimlik | Gereksinimler, marka ve ana ekran yönü | Yazılı kapsam ve kabul edilen görseller | Temel tamamlandı |
| P1 — Tıklanabilir prototip | Örnek verilerle şirket/proje/kaynak dizini, ayrı işlem akışı, çoklu arama, ön kontrol, iş takibi ve rapor | Ana akışlar kullanılabilir; test, derleme ve görsel kontrol; açılabilir yerel önizleme | TASK-005 dahil tamamlandı; kullanıcı incelemesine hazır |
| P2 — Teknik fizibilite | Kaynak/hedef matrisi; OST/PST/OLM motor denemeleri; sağlayıcı bağlantı denemeleri | Sağlıklı/bozuk örneklerde ölçüm, lisans/dağıtım değerlendirmesi, her yön için kanıt ve karar kaydı | TASK-006–010 tamamlandı: 13/13 gerçek OST deneyi, CID/gövde kabulü ve ilk yerel ürün akışı; hasar, ölçek, ölçülmeyen alanlar ve diğer yönler açık |
| P3 — Gerçek iş motoru | Ortak ileti modeli, kalıcı plan/ilerleme, sınırlı bellek, bağlantı katmanları | Küçük gerçek veriyle filtreli aktarım; kesinti ve yeniden denemede tutarlı sonuç | İlk yerel filtreli dosya akışı TASK-011 ile tamamlandı; ölçek, bağlantılar ve kesintiden devam bekliyor |
| P4 — Dönüşüm, arşiv ve kurtarma | Doğrulanmış dosya motorları, bölümleme, yeni çıktıya kurtarma | Temsilî veri setlerinde çıktı doğrulama; kayıpları açıklayan rapor | Küçük gerçek PST/OST bölümleme TASK-012 ile tamamlandı; diğer formatlar, kurtarma ve ölçek bekliyor |
| P5 — Büyük veri ve kurumsal kullanım | 100 GB ölçümleri, çoklu işler, müşteri ayrımı, teslim raporu | Kaynak tüketimi, devam, hata ve veri bütünlüğü kabul testleri | Bekliyor |
| P6 — Windows pilotu ve dağıtım | Kurulum, güncelleme, imzalama, lisanslama ve pilot kullanım | Kararlaştırılmış destek matrisiyle pilot doğrulaması ve sürüm kontrol listesi | Bekliyor |

Aşamalar geliştirme sırasıdır; ürünün kapsamını tek yönlü aktarım aracına daraltmaz. Takvim ve ticari sürüm taahhüdü gerçek fizibilite sonuçları çıkmadan verilmez. Bağımsız teknik denemeler gerektiğinde P2 içinde birlikte yürütülebilir; P3/P4 üretim işleri ilgili motor kararına bağlıdır.

## Geçmiş — P1 prototip kapsamı ve teslimi


Prototip `prototype/` altında yerelde açılan React + TypeScript + Vite arayüzüdür. Bu seçim üretim Windows kabuğunu, gerçek motor dilini veya veritabanını kesinleştirmez. Örnek verilerle etkileşim, Türkçe arayüz ve mevcut görsellere sadakat temel alınmıştır. Aşağıdaki kapsam P1 yapım sözleşmesidir; güncel doğrulama sonuçları üstteki durum bölümündedir.

Ana senaryo: Müşteriler veya İş merkezi → Aktarım ve dönüşüm → müşteri/proje ve örnek kaynak/hedef → tarih ve ek filtresi → klasör eşlemesi → ön kontrol → boyut sorununu çözme → simülasyonu başlatma → duraklat/devam → sonuç raporu. Arşiv ve arama bağımsız çoklu konum kapsamıyla çalışır.

- Arayüzde prototip ve örnek veri durumu görünür kalacak.
- Filtreler gerçek yerel örnek veri üzerinde çalışacak; sayılar filtre sonucundan hesaplanacak.
- Hesap/dosya türleri örnek seçicilerle gösterilecek; gerçek parola, dosya içeriği veya posta hesabı alınmayacak.
- Taşıma, dönüştürme, arşivle/böl ve kurtarma iş türleri görünür olacak. P1'de asıl uçtan uca etkileşim posta taşıma; diğerleri örnek iş şablonu/planı olacak ve gerçek dönüşüm iddiası taşımayacak.
- Rapor örnek işin güncel sonuçlarından üretilecek; demo verisi olarak işaretlenecek.
- Akış, ön kontrol kapısı, sayım tutarlılığı, duraklatma/devam ve görünür tasarım doğrulanacak.

Ayrıntılı yapım sözleşmesi: [PROTOTYPE_SPEC.md](docs/PROTOTYPE_SPEC.md).

## Geçmiş — Açık kararlar ve ele alınacağı aşama


| Karar | Ne zaman gerekli? |
|---|---|
| Küçük gerçek OST deneyi ve CID/gövde kabulü tamamlandı; büyük dosya, hasarlı arşiv ve lisanslı ölçek deneyleri açık | Motorun üretim kabulünden önce |
| Motor/SDK ve dağıtım lisansı | P2 sonunda, P3/P4 üretim bağlantılarından önce |
| İlk ticari sürümde her kaynak/hedef yönü | P2 kanıtlarıyla |
| Exchange Online / şirket içi Exchange kapsamı | P2 sağlayıcı deneyi öncesi |
| Kişi, takvim ve görev aktarımı | İlk ticari sürüm kapsamı belirlenirken |
| Windows kabuğu, işçi dili, veritabanı ve kurulum biçimi | P2/P3 geçişinde |
| Ekip paylaşımı, yetkiler, merkezi yönetim, çevrimdışı kullanım | P3/P5 tasarımında |
| Türkçe/İngilizce kapsamı, ticari model, alan adı ve tescil işlemleri | Dağıtım hazırlığından önce |

Bu kararlar örnek verili P1 prototipini engellemez. Alan adı satın alınmadı ve marka tescili yapılmadı.

## Geçmiş — Çalışma ve doğrulama düzeni


- Yönetim ve mimari: ASTRA HIGH. Normal uygulama: mevcut SOL denetiminde resmi Antigravity CLI üzerinden Gemini.
- Yeni Codex görevi veya Codex alt ajanı oluşturulmaz. Mevcut iki görev korunur.
- Çalışma arka planda terminal üzerinden yürür; kullanıcının masaüstü veya tarayıcısına yazarak Gemini kontrol edilmez.
- Yerel arayüz testleri arka planda headless tarayıcıyla yapılabilir; kullanıcı masaüstü kontrol edilmez.
- SOL gerçek komutları ve sonuçları raporlar. Koşulmayan kontrol PASS sayılmaz. Gereksiz tam ikinci kod incelemesi yapılmaz.
- Kritik veri bütünlüğü/mimari kararları Astra'da kalır.
- Koordinasyon dosyaları .codex-coordination altında yereldir, commit edilmez. Git kurulursa yerel exclude kullanılır.

## Geçmiş — Devam ederken izlenecek kayıt düzeni


Her anlamlı teslim sonunda bu dosyanın “Bugünkü durum” bölümü ve aşağıdaki kayıt güncellenir: yapılan iş, kanıt dosyası, kalan sınırlama ve sıradaki somut adım. Ürün kararları yalnız görev sohbetinde bırakılmaz. Tamamlanmayan aşama tamamlandı işaretlenmez.

| Tarih | Karar / teslim | Kanıt |
|---|---|---|
| 2026-09-12 | Windows önceliği, BT firmaları, çok yönlü kapsam | docs/PRODUCT_BRIEF.md |
| 2026-09-12 | Antigravity CLI uygulama akışı doğrulandı | Yerel TASK-002 sonucu |
| 2026-09-12 | OST/PST kaynak araştırması; gerçek motor deneyi henüz yok | docs/FORMAT_FEASIBILITY.md |
| 2026-09-12 | BitigMail adı ve görsel temel kabul edildi | docs/BRAND_IDENTITY.md, docs/VISUAL_DESIGN_V1.md |
| 2026-09-12 | Kullanıcı ana planın kaydını ve P1 prototip başlangıcını istedi | Bu dosya, TASK-004 |
| 2026-09-12 | P1 tamamlandı: örnek veri akışı, kalıcı demo planı, ön kontrol, duraklat/devam, rapor; masaüstü panel sınırları düzeltildi | prototype/README.md, prototype/VISUAL_QA.md; yerel TASK-004 DONE sonucu |
| 2026-09-12 | TASK-005 tamamlandı: şirket/proje/kaynak dizini ve kalıcılık, ayrı Aktarım ve dönüşüm sekmesi, çok kaynaklı arama; kaynak kapsamı ve dar ekran erişimi doğrulandı | docs/PROTOTYPE_ITERATION_2.md, prototype/VISUAL_QA.md; yerel TASK-005 DONE sonucu |
| 2026-09-12 | Kullanıcı bağımsız OST→PST gereksinimini doğruladı: eski hesap/orijinal Outlook profili olmayabilir. TASK-006 motor adayları ve deney tasarımı başlatıldı | docs/PRODUCT_BRIEF.md; TASK-006 |
| 2026-09-12 | TASK-006 kaynak değerlendirmesi tamamlandı; SaveAs kısıtı ile öğe çıkarımı ayrıldı, motor seçilmedi. Kullanıcı yapay veri istedi; TASK-007 EML/MBOX kümesi 13 kontrol ve iki üretim hash karşılaştırmasıyla tamamlandı. Gerçek OST/PST deneyi yapılmadı | docs/OST_ENGINE_DECISION.md, fixtures/mail-corpus-v1/README.md; yerel TASK-006/TASK-007 sonuçları |
| 2026-09-12 | TASK-008 DONE_WITH_DIFFERENCES: gerçek tam OST 13/13 dönüştürüldü; kaynak değişmedi; bağımsız okuyucu ve profilsiz/ağsız ortam doğrulandı. Dört ekin dosya içeriği korundu; CID ve tam alan uygunluğu açık. İlk eşitlemedeki klasör abonelikleri düzeltildi | lab/ost-spike/OST_EXPERIMENT_RESULT.md; son Windows, temiz ortam ve libpff JSON raporları |

| 2026-09-12 | TASK-009 tamamlandı: gerçek MAPI Content-ID alanı kaynak ve çıktıda korundu; gövde karşılaştırma politikası kesinleşti, 13 öğe ve dört ek yeniden doğrulandı | docs/CID_BODY_ACCEPTANCE.md; yerel TASK-009 sonucu |
| 2026-09-12 | TASK-010 tamamlandı: gerçek OST seçimi, ön kontrol, yeni PST, kalıcı iş/çıktı konumu/rapor; 13/13, dört ek ve CID; bağımsız API/libpff ve gerçek UI kabulü | docs/LOCAL_OST_WORKFLOW.md, docs/LOCAL_OST_VALIDATION.md |
| 2026-09-13 | TASK-011 ve TASK-012 tamamlandı: tarih/klasör filtreli dönüşüm ve gerçek yıl/boyut PST bölümleme | docs/FILTERED_OST_VALIDATION.md, docs/PST_SPLIT_VALIDATION.md |
| 2026-09-13 | TASK-013 tamamlandı: EML/EML ağacı/mboxrd→PST, kalıcı rapor ve mevcut arşivleme entegrasyonu; özgün ölçülen içerik değerlendirme ekleriyle korundu. Kullanıcı lisans almadan deneme çıktılarıyla devam etmeyi seçti | docs/MIME_IMPORT_WORKFLOW.md, docs/MIME_IMPORT_VALIDATION.md |
| 2026-09-13 | TASK-014 tamamlandı: gerçek IMAP hesapları, dondurulmuş filtreli önizleme, iki yönlü kopyalama, durable journal/resume, rapor ve gerçek responsive UI | docs/IMAP_TRANSFER_WORKFLOW.md, docs/IMAP_TRANSFER_VALIDATION.md |

**Güncel durum:** TASK-016 yerel kabulü tamamlandı (**LOCAL_READY / LIVE_PERSONAL_PILOT_PENDING**). Kurumsal Microsoft 365 yanında kişisel Outlook.com/Hotmail `consumers` authority modu, MSA tenant kimliği doğrulaması, mode-switch izolasyonu, reconnect ve kaynak/hedef seçicileri deterministik olarak doğrulandı. BitigMail kişisel uygulama kaydı/Client ID, gerçek giriş ve ayrı BitigMail-Test klasöründeki yapay iletilerle silmesiz pilot henüz yapılmadı. Kurumsal pilot ayrıca **LIVE_PILOT_PENDING** kalır. Google OAuth, 100 GB ve kurtarma desteği henüz kanıtlanmadı. *(Sonradan: Google OAuth altyapısı TASK031, kurtarma Aşama 6 ile yerel kabul aldı; 100 GB açık.)*

## Geçmiş — İlgili belgeler


### 2026-09-14 — Canlı Microsoft pilotunun ertelenmesi ve yerel işlerin onayı

Kullanıcı Azure kaydını ve gerçek Outlook bağlantı pilotunu sonraki aşamaya bıraktı. Entra portalında AADSTS16000 / Microsoft Services dizinine erişim hatası görüldü; Azure kayıt sayfası açıldı ancak uygulama kaydı veya gerçek posta bağlantısı tamamlanmadı. TASK-015/016 yerel kabulü korunur; canlı pilotlar ertelendi, tamamlandı sayılmaz. Kullanıcının mevcut iletilerini silmeme koşulu devam eder.

Kullanıcı bu sıralamayı onayladı: mevcut yerel IMAP laboratuvarında dosya ↔ posta hesabı bağlantısı; ilk kapsam EML/MBOX → IMAP ve IMAP → EML/MBOX, klasör/tarih seçimi, kaynak koruması, kesintiden devam ve öğe/ek bütünlüğü doğrulaması. TASK-017 kabul edildi; sözleşme docs/FILE_ACCOUNT_BRIDGE_WORKFLOW.md. TASK-018 gerçek çok müşterili arşiv/arama uygulaması aktif; aynı SOL/Gemini kanalından yürütülür. PST/OST bağlantıları mevcut deneme motoru sınırlarıyla ayrı ele alınacak. Sonrasında kademeli ölçek/dayanıklılık deneyleri yapılacak. 100 GB ve kurtarma ölçülmeden tamamlandı sayılmaz.

Kullanıcı bu işlerden sonra Azure ve gerçek Outlook pilotunun mutlaka hatırlatılmasını istedi; son talebiyle zamanlayıcı yerine yalnız aşama notu tutulur. `bitigmail-azure-ve-outlook-pilotunu-hat-rlat` otomasyonu PAUSED durumuna alındı. Yerel aşamalar tamamlandığında veya dış bağımlılıklar nedeniyle canlı pilot sıradaki uygulanabilir adım olduğunda yönetici sohbet içinde hatırlatacak. Azure kaydı veya gerçek posta erişimi kendiliğinden yapılmaz.

- [Ürün ihtiyaçları](docs/PRODUCT_BRIEF.md)
- [Ürün yapısı](docs/PRODUCT_BLUEPRINT.md)
- [Biçim fizibilitesi](docs/FORMAT_FEASIBILITY.md)
- [Bağımsız OST motor adayları ve deney kararı](docs/OST_ENGINE_DECISION.md)
- [Yapay posta deneme kümesi](fixtures/mail-corpus-v1/README.md)
- [Marka kimliği](docs/BRAND_IDENTITY.md)
- [Görsel tasarım ve referanslar](docs/VISUAL_DESIGN_V1.md)
- [Prototip yapım sözleşmesi](docs/PROTOTYPE_SPEC.md)
- [Çok müşterili kullanım ve işlem ayrımı](docs/PROTOTYPE_ITERATION_2.md)

Eski belgelerdeki “kodlama başlamadı / kullanıcı seçimi bekleniyor” ifadeleri tarihsel bağlamdır; güncel uygulama durumu bu dosyada tutulur. Öncelik: kullanıcının son kararı → bu ana plan → ilgili ayrıntı belgesi → görev paketi. Çelişki fark edilirse kayıtlar düzeltilir.

2026-09-14 — TASK022 ilk performans dilimi TAMAMLANDI; Aşama2 devam ediyor.
Aynı iletinin ikinci kez indirilmesi kaldırıldı. 1024 ileti/308 ek,133847116 ham bayt ile önce/sonra kabulü: önizleme10,63 ->8,93 sn; aktarım57,53 ->52,01 sn. Tek yerel karşılaştırma; genel hız garantisi değildir. Önizleme tepe belleği biraz arttı; aktarım tepe belleği azaldı. Backend448/448PASS; ham içerik/ek/metadata ve kaynak12/4 korunmasıPASS. Kanıt: .codex-coordination/results/TASK-022.md ve evidence/TASK-022/root-comparison.json.
Sıradaki Aşama2 işi: çıktı alanı için disk ön kontrolü; ardından son doğrulama/klasör erişimi, büyük liste/kuyruk davranışı ve eksik1GiB ölçümleri. Azure/gerçek Outlook pilotu o tarihte Aşama 3 notuydu; sonradan Aşama 10'a taşındı; zamanlayıcı yok.

2026-09-14 — TASK023 disk kontrolünün ilk dilimi TAMAMLANDI; Aşama2 devam ediyor.
IMAP -> EML/mboxrd önizlemesinde tahmini gereken/kullanılabilir alan görünür. Yetersiz veya sorgulanamayan alanda yeni plan engellenir; iş başlamadan tekrar kontrol edilir. Backend454/454 ve aktarım ekranı21/21PASS; typecheck/lint/buildPASS. Gerçek12ileti EML aktarımı eksiksiz; kaynak12/4 korundu. Normal6174 güncelReleasePID24924; Vite5173PID13512; test6175kapalı.
Kapsam ve kalan disk işleri: docs/DISK_CAPACITY_SCOPE.md. Eski tahminsiz planlar önceki devam davranışını korur; kontrol alan rezervasyonu değildir. PST/OST/bölme/yönetilen arşiv disk kontrolleri henüz bu kabulde yok. Ardından son doğrulama/klasör erişimi, bellek/büyük liste/kuyruk ve eksik1GiB ölçümleri açık. Azure/Outlook pilotu (sonradan Aşama 10); zamanlayıcı yok.

2026-09-15 — TASK024 TAMAMLANDI ve root tarafından kabul edildi. Ortak disk kapasitesi kontrolü OST->PST, PST/OST bölme, EML/mboxrd->PST ve yönetilen arşivleme akışlarına eklendi; TASK023 IMAP->EML/mboxrd davranışı korunur. Hedef kullanılabilir alanı ilk çıktı yazımından önce kontrol edilir; bilinmeyen/yetersiz alan açık engeldir. Tahminler güvenlik payıdır, alan rezervasyonu/garantisi değildir. Arşivde mevcut ortak taban için ham+indeks/WAL toplamı hesaplanır; ayrı indeks hedefi desteklenmez.
Kabul: backend460/460PASS0skip; kapasite12/12PASS; ilgili frontend36/36PASS; typecheck/lint/buildPASS. Küçük SDK dönüşüm/bölme ve gerçek corpus arşiv/arama senaryoları tam regresyon içinde çalıştı; bağımsız yeni canlı sağlayıcı testi veya büyük ölçek deneyi yapılmadı. Kanıt .codex-coordination/results/TASK-024.md ve evidence/TASK-024/verification-summary.json. Kapsam docs/DISK_CAPACITY_SCOPE.md.
Normal güncelRelease6174PID20792; Vite5173PID21576; test6175kapalı. Aşama2 DEVAM EDİYOR: sırada son doğrulama/klasör erişimi performansı, bellek ve büyük liste/kuyruk davranışı, eksik1GiB ölçümleri. Azure/gerçek Outlook pilotu (sonradan Aşama 10); zamanlayıcı yok.

2026-09-15 — TASK025 TAMAMLANDI. Dışa aktarımda sadece hash kontrolü için kullanılan üç tam dosya tamponu akış okumasına çevrildi; kısa ömürlü MIME nesneleri kullanım sonrası kapatılır. MBOX yazıcısı ve bütünlük kontrolleri korunur. Backend460/460, hedefli16/16PASS. Aynı1024ileti/308ek,133847116 ham baytta iki koşunun raw/MIME/metadata kontrolüPASS; kaynak12/4 ve önce/sonra kaynak+IMAPsnapshotları değişmedi.
Tek yerel önce/sonra gözlemi: önizleme6,885->6,649sn, tepeWS581910528->496398336bayt(-%14,70); aktarım45,289->43,791sn, tepeWS804315136->732250112bayt(-%8,96). Genel hız/bellek garantisi değildir. Kanıt .codex-coordination/results/TASK-025.md ve evidence/TASK-025/root-comparison.json.
Güncel normalRelease6174PID48492; Vite5173PID21576; test6175kapalı. Aşama2 DEVAM: sunucudaki son hedef doğrulamasında tekrarlanan klasör erişimi için güvenli kapsamlı oturum tasarımı/ölçümü, diğer bellek ve büyük liste/kuyruk işleri, eksik1GiBtelemetri. Klasör oturumu/batch değişikliği henüz yapılmadı; clientmetot sayısı gerçekprotokolkomut ölçümü değildir. Azure/gerçek Outlook pilotu (sonradan Aşama 10); zamanlayıcı yok.

2026-09-15 — TASK031 yerel geliştirme kabulü tamamlandı:511motor/145arayüztesti, GoogleOAuth+sağlayıcıprofilleri/tanılama ve gerçekTestingHost UI. CanlıGoogle/tenant/kota/APPEND doğrulanmadı. Azure/gerçekOutlook10. TASK032 EMLXaktarımı ile Aşama4 başladı. Normalhost yenidenbaşlatma otomatikonaydenetiminde reddedildi; eskimotorönizlemesi korunuyor, geliştirmeler ayrıtestortamında sürer.
