# BitigMail — Ürün yapısı ve kimlik taslağı

Tarih: 2026-09-12
Durum: Ayrıntılı ürün taslağı. Kabul edilen görsel temel VISUAL_DESIGN_V1.md, güncel aşama ve kararlar ../ROADMAP.md içinde. P1 örnek verili prototip uygulaması yetkilendirildi; üretim mimarisi önerileri ayrıca doğrulanacak.

## 1. Ürün kimliği

Kategori: BT hizmet firmaları için kurumsal posta taşıma, dönüştürme, kurtarma ve arşiv yönetimi.

Ürün adı: **BitigMail**. Kullanıcı 2026-09-12 tarihinde bu adı seçti. Yazım biçimi bütün ürün yüzeylerinde BitigMail olarak korunacak.

İsim, eski Türkçede yazı/mektup/belge anlamlarında kullanılan bitig sözcüğü ile İngilizce Mail kelimesinin modern birleşimidir. Marka kararları BRAND_IDENTITY.md, kök kaynakları ve ön araştırma BRAND_MAIL_ROOT_CANDIDATES.md içindedir.

Önerilen açıklayıcı alt tanım: Kurumsal Posta Yönetim Platformu.

Marka karakteri önerisi: Sakin, ciddi, öngörülebilir ve açıklayıcı. Uzun işlemlerde operatöre kontrol, müşteriye anlaşılır teslim kaydı sunar.

Kullanıcı MailStore Home'u kısmen kullanılabilirlik referansı almayı önerdi. Önceki isim araştırmaları tarihsel kayıttır; güncel seçim BitigMail'dir.

Kullanıcının seçtiği görsel yön: Sarı, turuncu ve beyaz. Önceki mavi/lacivert tema önerisinin yerine geçer.

Önerilen dağılım: Beyaz ana yüzeyler, sıcak kırık beyaz arka plan, turuncu ana eylemler ve sarı seçili alan/ikincil vurgular. Metinlerde koyu antrasit. Geniş sarı/turuncu yüzeylerden kaçınarak yoğun tablo ve uzun çalışma oturumlarında okunaklılık korunur. Başarı/uyarı/hata yalnız renkle değil metin ve simgeyle gösterilir; sarı marka vurgusu uyarı durumuyla karışmamalıdır. Raporlarda sade tipografi; logoda küçük boyutta okunur sade bir işaret.

İlk palet taslağı (tonlar henüz kullanıcı tarafından seçilmedi): Beyaz #FFFFFF, sıcak arka plan #FAF8F3, sarı #F3C544, turuncu #E97C24, metin #292824. Düğme yazı rengi ve tüm durumların kontrastı gerçek arayüzde ölçülerek seçilecek.

Önerilen arayüz dili: İlk tasarım Türkçe; metinler ileride İngilizce desteğini kolaylaştıracak şekilde arayüzden ayrılır. Dil kapsamı kullanıcıyla kesinleştirilecek.

## 2. Bilgi yapısı

Müşteri → Proje → Kaynaklar ve hedefler → İş planı → Çalıştırmalar → Sonuç raporu.

Bir müşteri birçok proje içerebilir. Bir proje birden fazla dosya, hesap ve aktarım işi içerebilir. Aynı kaynak farklı planlarda kullanılabilir; bağlantı sahibi müşteri açıkça gösterilir. Her çalıştırma, uygulandığı planın değişmez sürümüne bağlanır; sonradan değişen filtreler geçmiş raporu değiştirmez.

## 3. Ana gezinme

| Ekran | Kullanıcının yaptığı iş |
|---|---|
| İş merkezi | Çalışan, sırada, duraklatılmış ve müdahale bekleyen işleri görür |
| Müşteriler | Şirket dizininden projelere ve bağlı posta kutuları/dosya arşivlerine gider; doğrudan bir kişinin posta kutusuna açılmaz |
| Aktarım ve dönüşüm | Müşteri/proje bağlamında kaynak, hedef ve işlem türünü seçer; taşıma, dönüşüm, arşivleme/bölme ve kurtarma planlar |
| Arşiv ve arama | Şirket → proje → posta kutusu/dosya arşivi ağacında birden fazla konumu seçer; sonuçların şirket/proje/kaynak bilgisi görünür |
| İş şablonları | Filtre, klasör eşleme, bölümleme ve yinelenme tercihlerini tekrar kullanır |
| Raporlar | İş ve müşteri bazında sonuçları inceler, teslim raporu oluşturur |
| Ayarlar | Çalışma klasörü, kaynak kullanımı, bildirimler ve saklama tercihlerini düzenler |

Yeni iş seçenekleri: Posta taşı; dosya dönüştür; arşivle/böl; dosyadan kurtar. Hepsi ortak planlama akışını kullanır; yalnızca ilgili seçenekler gösterilir.

Güncel prototip kararı: Kaynak ekleme Müşteriler içindeki proje bağlamında yapılır ve arama/işlem seçimlerinde aynı kayıtlar kullanılır. İş şablonları ayrı ana sekme olarak henüz uygulanmadı; tablodaki şablon kapsamı gelecek ürün önerisidir. Çok şirketli arama açık kapsam seçimi gerektirir; bu görünüm gelecekteki kullanıcı yetkilendirmesinin yerine geçmez. Uygulama sözleşmesi: [PROTOTYPE_ITERATION_2.md](PROTOTYPE_ITERATION_2.md).

## 4. Proje çalışma ekranı

### MailStore Home referansından alınacak davranışlar

Resmi belgelerde klasör ağacından gezinme, alan bazlı arama, kayıtlı aramalar ve kayıtlı arama sonucunu dışa aktarma akışları bulunuyor. Bizim tasarımda bu davranışlar müşteri/proje bağlamına, çok yönlü hedef seçimine, kurtarma ve bölümleme işlerine bağlanacak. MailStore Home'un görsel kimliği veya markası kopyalanmayacak; kullanılabilirlik referansı olarak ele alınacak.

Kaynaklar (2026-09-12):
- https://help.mailstore.com/en/home/Accessing_the_Archive
- https://help.mailstore.com/en/home/Exporting_Email (sayfanın son düzenlemesi 2018; güncel sağlayıcı kimlik doğrulama uyumluluğunun kanıtı olarak kullanılmamalı)
- https://www.mailstore.com/en/products/mailstore-home/

Bu kaynaklardan türetilen ürün önerisi: Kullanıcı arama sonucunu tek adımda “Bu iletilerle iş oluştur” eylemine dönüştürebilsin. Hedef bir hesap veya desteklenen dosya çıktısı olabilir. Canlı kayıtlı arama her kullanımda yeniden değerlendirilebilir; bir aktarım başladığında seçimin/planın sürümü kaydedilir.

Üst bölüm: Müşteri/proje kimliği, aktif iş özeti ve son doğrulama durumu.

Sol bölüm: Kaynak hesap/dosya ve klasör ağacı.

Orta bölüm: Sayfalı ileti listesi, arama ve filtre sonuçları. Henüz taranmayan alanlar sıfır gibi gösterilmez. Sayının kesin mi tahmini mi olduğu belirtilir.

Sağ bölüm: İleti önizlemesi veya hedef/klasör eşleme ayrıntısı.

İş ayrıntıları: Filtreler, klasör eşleme, bölümleme, yinelenen ileti politikası, ön kontrol, çalıştırmalar ve rapor.

Kaynak üzerinde yıkıcı işlem yapmadan sanal eşleme hazırlanır. Kullanıcı kaynak klasörü hedefte yeniden adlandırabilir veya başka bir klasörle birleştirebilir. Kaynak kimlikleri korunur.

## 5. Ortak iş akışı

1. Kaynak ve hedef seçimi. Her ikisinin erişim/yetenek kontrolü.
2. Kaynak tarama ve ileti önizleme. Okunamayan bölgelerin ayrı kaydı.
3. Filtreleme ve klasör/etiket eşleme. VE/VEYA koşul grupları; dahil et/hariç tut ayrımı.
4. Çıktı seçenekleri. Bölümleme, çakışma/yinelenme tercihi, ileti tarihleri ve durumlarının korunması için destek durumu.
5. Ön kontrol. Seçili kapsam, kaynak/hedef kotaları, disk ihtiyacı, desteklenmeyen öğeler ve olası kayıplar. Başlatılacak plan sürümünün kaydı.
6. Arka planda yürütme. İleti/byte ilerlemesi, mevcut aşama, hız, tahmini kalan süre varsa belirsizliği, duraklatma ve devam.
7. Doğrulama ve teslim. Aktarılan, filtre dışı, yinelenme nedeniyle atlanan, kısmi ve başarısız sonuçların ayrı toplamları. Sorunlu öğeler için dar kapsam yeniden deneme.

İş durumları: Taslak → Taranıyor → Ön kontrol → Hazır → Sırada → Çalışıyor → Doğrulanıyor → Tamamlandı / Kısmen tamamlandı / Başarısız. Duraklatıldı, iptal edildi ve müdahale bekliyor durumları ayrı izlenir. İptal, hedefe yazılanları otomatik geri almaz; temizlik ancak hangi öğeleri işin oluşturduğu biliniyorsa ayrı tasarlanır.

## 6. Mimaride önerilen temel kararlar

### Yerelde işleme

Windows uygulaması ileti verisini operatörün cihazında işler. Kullanıcının seçtiği kaynak/hedef posta servislerine doğrudan bağlanır. Üreticiye ait bir bulut hizmetinden posta içeriği geçirmek varsayılan mimari olmasın. Merkezi ekip yönetimi ihtiyacı ayrıca değerlendirilecek.

### Doğrudan taşıma ve kalıcı arşiv ayrımı

İki işlem biçimi önerisi: (1) Doğrudan aktarım: Gerekli indeks/geçici alanla kaynak→hedef; tüm içeriğin kalıcı bir uygulama arşivine alınması zorunlu değil. (2) Arşive al: İçeriğin yönetilen yerel kopyası, arama indeksi ve gerektiğinde yeniden dışa aktarımı. 100 GB girdilerde zorunlu tam ara kopya oluşmasının maliyeti ayrıca hesaba katılır.

Yalnızca kaynak dosyaya referans/index tutmak kalıcı arşivleme veya yedekleme diye etiketlenmez. Kullanıcı verinin bağımsız kopyasının alınıp alınmadığını, nerede saklandığını ve kaynak çıkarıldığında neyin erişilebilir kalacağını görür. Arşivin yedeği ve taşınabilirliği ayrı tasarlanacak.

### Arayüz ve iş motorunun ayrılması

Arayüz yalnız planlar, gösterir ve denetler. Aktarım motoru ayrı süreçte çalışır; ilerleme kaydı diskte tutulur. Arayüzün kapanması, bilgisayarın yeniden başlaması veya bağlantı kesilmesi için farklı devam davranışları tanımlanır. İlk sürümün hizmet/oturum yaşam döngüsü daha sonra seçilecek.

### Kaynak/hedef yetenekleri

Bağlantılar okuma ve yazma rollerini ayrı bildirir. IMAP, Google Workspace ve Exchange ürün kapsamında her iki yönde tasarlanır. Dosya biçimlerinin yazılabilirliği ayrıca doğrulanır. POP, OST ve OLM için bir okuma desteğinden yazma desteği sonucu çıkarılmaz.

### Ortak veri ve özgün içeriğin korunması

İleti kimliği, kaynak konumu, başlıklar, tarihler, gövde, ekler, klasör/etiket ve durum bilgileri ortak modelle işlenir. Özgün veri mümkün olduğu ölçüde korunur; model her sağlayıcının alanlarını eksiksiz temsil ediyormuş gibi davranmaz. Büyük gövdeler ve ekler akışla işlenir; çalışma veritabanı esas olarak indeks ve iş kayıtları içindir.

### Veri bütünlüğü

Kaynak dosya varsayılan olarak salt okunur kullanılır. Kurtarma yeni çıktıya yapılır. Her ileti için kaynak kimliği, hedef kimliği varsa, deneme ve doğrulama sonucu kaydedilir. Yazma sonrası bağlantı kopması belirsiz başarı yaratırsa öğe uzlaştırılmadan tekrar yazılmaz. Yalnız Message-ID ile yinelenme kararı verilmez; sağlayıcı ve içerik özellikleri ayrıca değerlendirilir.

### Müşteri verisi ve önizleme

Müşteri kapsamı bütün bağlantı, iş ve raporlara uygulanır. Kimlik bilgileri raporlara/iş dosyalarına konmaz; korumalı sistem deposu kullanımı değerlendirilir. İleti önizlemesinde etkin içerik çalıştırılmaz; uzak görseller kendiliğinden yüklenmez; ekler otomatik açılmaz. İçerik araması/yerel indeks ve geçici dosyalar için saklama ve temizleme davranışı kullanıcıya açık olmalıdır.

## 7. Kurumsal teslim raporu

Önerilen alanlar: Hizmeti veren firma, müşteri, proje, kaynak/hedef özeti, uygulanan filtreler, plan sürümü, başlangıç/bitiş zamanı, kapsam ve aktarım sayıları, veri miktarı, atlama nedenleri, hatalar, doğrulama düzeyi ve üretilen arşivler.

Müşteriye verilen rapor ileti gövdelerini ve hesap sırlarını içermez. Firma adı/logosu ile rapor markalama bir özellik adayıdır; çok kullanıcılı yetkiler ve merkezi yönetim kapsamı henüz kesinleşmedi.

## 8. Geliştirme planına geçiş için doğrulama adımları

1. OST okuma/PST yazma/kurtarma motoru fizibilitesi: TASK-003. Ticari SDK varsa lisans/dağıtım ve teknik kanıt birlikte değerlendirilir.
2. Kaynak-hedef destek matrisi: Her yön ve korunacak ileti özellikleri ayrı kabul ölçütü alır.
3. Seçilen motorlar için küçük teknik denemeler: Şimdilik yalnız plan; kodlama yetkisi bu belgeyle varsayılmaz.
4. Temsilî veri test planı: Sağlıklı, bozuk, büyük dosyalar; Unicode, ekler, klasör çakışmaları, tarihler ve kesinti sonrası devam. 100 GB desteği gerçek ölçümle kanıtlanır.
5. Ürün akışının görsel tasarımı ve ilk ticari sürüm matrisi kullanıcıyla kesinleştirilir.

Kapsam çok yönlü kalır. Mühendislik adımlarının sıraya konması tek yönlü ürün kararı değildir.

## 9. Açık kararlar

- OST'nin eski hesap/Outlook profili olmadan işlenmesi hedefi: Kullanıcı yanıtı bekleniyor; araştırma iki durumu ayrı ele alacak.
- Tasarım referansı: Kullanıcı MailStore Home'u önerdi. BitigMail adı, logo ve sarı/turuncu/beyaz ekran yönü görsel temel olarak kabul edildi; üretim varlıkları ve kontrast uygulaması tamamlanacak.
- Yerelde işleme önerisi, kurulum biçimi ve ekip paylaşımı.
- Exchange Online/şirket içi Exchange, e-posta dışı veri, ilk ticari sürüm ve lisans modeli.
- P1 prototipi Türkçe React/TypeScript/Vite kullanır. Üretim dil kapsamı, Windows kabuğu, motor kütüphaneleri ve veritabanı henüz seçilmedi.


