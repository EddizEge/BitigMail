# BitigMail — Ürün ve mimari başlangıç taslağı

Tarih: 2026-09-12
Durum: Görüşme taslağı; ürün kapsamı ve teknoloji seçimi henüz kesinleşmedi.

## Kullanıcının kesinleştirdiği kararlar

- Ürün adı: **BitigMail** (2026-09-12 kullanıcı kararı).
- İlk sürüm Windows üzerinde çalışacak; Mac'ten gelen arşivlerin işlenmesi hedefleniyor. İlk sürüm için macOS uygulaması istenmiyor.
- Birincil kullanıcı, müşteri şirketlere hizmet veren BT firmaları.
- Görsel kimlik: Kullanıcı sarı, turuncu ve beyaz tema istedi. Mavi/lacivert önerisi terk edildi; kesin renk tonları henüz seçilmedi.
- Ürün çok yönlü ve kapsamlı olacak: IMAP, Google Workspace ve Exchange kaynak/hedef rolleri tek bir yöne sabitlenmeyecek. Dosya→hesap, hesap→dosya, dosya→dosya ve hesap→hesap işleri aynı ürün kapsamına dahil.
- Bu hedefe yönelik yapı önerisi: Müşteri → Proje → Kaynaklar/Hedefler → Aktarım işleri → Raporlar. Müşterilerin dosyaları, bağlantıları ve işlem kayıtları birbirinden ayrılmalı.

## Kullanıcının tanımladığı ihtiyaç

Farklı posta uygulamaları, arşiv biçimleri ve servisler arasındaki taşıma, düzenleme ve arşivleme işlemlerini kolaylaştıran; kurumsal kullanım için uygun kimliği olan bir uygulama.

İstenen kapsam adayları: MBOX, PST, EML, OST, OLM; IMAP, POP, Exchange ve Google Workspace ortamları. Bu liste ürün hedefidir, henüz doğrulanmış format/bağlantı desteği değildir. Her kaynak-hedef yönü ayrı değerlendirilmelidir.

Örnek senaryo: MacBook'ta POP hesabıyla kullanılan e-postaları, Windows bilgisayara geçiş sırasında Exchange hesabına; tarih filtreleri ve istenen klasör düzeniyle aktarmak.

## Kullanıcının belirttiği somut kullanım alanları

- Eski posta hesabı ve orijinal Outlook profiline erişim olmadan, yalnız eldeki bağımsız OST dosyasını okuyup PST'ye dönüştürmek. Kullanıcı bu koşulu açıkça doğruladı (2026-09-12). Hasarlı dosyada kurtarılabilen kapsam ayrıca raporlanır; eksiksiz kurtarma garantisi verilmez.
- Bozuk PST dosyalarından veri kurtarmak ve kullanılabilir arşiv üretmek.
- Yaklaşık 100 GB'a ulaşan posta dosyalarını yönetmek, arşivlemek ve bölümlemek.
- IMAP ↔ Google Workspace, IMAP ↔ Exchange, Google Workspace ↔ Exchange ve aynı servis türündeki farklı hesap/kurumlar arasında posta taşımak. Yerel IMAP→bulut geçişi örneklerden yalnızca biri.
- Bütün posta kutusu yerine filtrelerle seçilen iletileri aktarmak.

## Bu ihtiyaçlara yönelik ürün tasarımı önerisi

Üç ana çalışma alanı aynı aktarım ve doğrulama motorunu kullansın:

1. Dönüştür ve kurtar: Sağlıklı arşiv dönüştürme ile bozuk dosyadan kurtarma ayrı işlem türleri olsun. Ön tarama okunabilirlik durumunu ve bulunan hataları raporlasın. Kurtarma özgün dosyayı değiştirmeden yeni çıktılar üretsin; okunamayan bölgeler/öğeler ve belirsiz kayıp sayıları dürüstçe raporlansın.
2. Arşivle ve böl: Yıl/tarih aralığı, klasör veya seçilen azami çıktı boyutuna göre yeni arşivler üretilebilsin. Tarih ve boyut koşulları birleştirilebilsin. Tek ileti bölünmesin; sınırdan büyük ileti için ayrı rapor/politika bulunsun. Dosya biçiminin ek yükü nedeniyle sonuç boyutu ayrıca kontrol edilsin; giriş boyutundan kesin parça sayısı türetilmesin.
3. Hesaplar arasında taşı: IMAP, Google Workspace ve Exchange için her iki yönde seçili içerik aktarımı; aynı servis içindeki farklı hesaplar ve kurumlar arası geçiş; klasör eşlemesi, hedef yetenek ön kontrolü, kesinti sonrası devam ve sonradan gelen iletiler için fark aktarımı tasarlansın. Exchange Online ile şirket içi Exchange kapsamı ayrı kararlaştırılsın. İki yönde aktarım desteği, otomatik sürekli çift yönlü eşitleme veya silme yayılımı anlamına gelmez; bunlar ayrıca tanımlanacak özelliklerdir.

Ortak filtreler: Tarih aralığı (hangi tarih alanı olduğu açık), klasör, gönderen/alıcı, alan adı, konu, içerik, ek varlığı/türü ve ileti boyutu. İçerik/ek taramasının daha fazla okuma gerektirmesi önizlemede açıklansın. Kurallar VE/VEYA gruplarıyla birleştirilebilsin; seçilen iletiler örnek liste ve sayılarla önceden görülsün.

100 GB girdi hedefinin mimari etkisi: Arşivin tamamını belleğe yüklemeden sınırlı bellekle işleme, disk üzerinde indeks, ayrı arka plan işçisi, kalıcı ilerleme kaydı, boş disk alanı ön kontrolü ve kesinti testleri. Çıktı büyüklüğü, geçici alan, sayım ve performans gerçekçi veri setleriyle ölçülmeden destek/hız garantisi verilmeyecek.

İlk teknik araştırma: OST okuma ve PST yazma motorunun sağlıklı/bozuk örneklerle, büyük dosyada ve kurulum bağımlılıklarıyla fizibilitesi. SDK/lisans seçimi henüz yapılmadı. Kullanıcı yalnız dosyayla, eski hesap/profil olmadan OST→PST ihtiyacını doğruladı. Ana çözüm orijinal profilin erişilebilir olmasına dayanamaz. Hedef makinede yeni Outlook kurulumu gerekip gerekmediği motor karşılaştırmasında ayrı kriterdir; kullanıcıdan böyle bir şart onayı alınmış sayılmaz.

## Teknik kaynak notları — 2026-09-12

- Microsoft, PST onarımından önce yedek alınmasını öneriyor ve ağır bozulmuş bazı öğelerin kurtarılamayabileceğini belirtiyor. Ürün tam kurtarma garantisi vermemeli; kurtarılan veriyi ayrı çıktıyla doğrulamalı. Kaynak: https://support.microsoft.com/en-US/Outlook/repair-outlook-data-files-pst-and-ost
- Microsoft, Unicode PST/OST boyut sınırlarını yükseltmenin performansı düşürebileceğini belirtiyor. Büyük girdi okuyabilme ile büyük tek çıktı üretme hedefleri ayrılmalı. Kaynak: https://learn.microsoft.com/en-us/microsoft-365-apps/outlook/data-files/configure-size-limit-outlook-data-files
- IMAP ile posta taşınabilir; kişi/takvim/görev aktarımı ayrıca ele alınmalı. Microsoft'un kendi taşıma aracının sayısal limitleri bizim gelecekteki motorumuzun genel limiti olarak alınmamalı. Kaynak: https://learn.microsoft.com/en-us/exchange/mailbox-migration/mailbox-migration

## Önerilen konumlandırma

Kurumsal posta taşıma ve arşiv yönetimi uygulaması.

Ürün vaadi: Kullanıcı hangi postaların nereye aktarılacağını önceden görür; işlem sonunda aktarılan, atlanan ve aktarılamayan öğelerin hesabını alır.

Kimlik önerisi: Sade, ciddi, anlaşılır; güveni işlem görünürlüğü ve doğrulanabilir sonuçlarla kuran bir ürün. Kullanıcı sarı/turuncu/beyaz tema seçti. Ürün adı BitigMail olarak seçildi. Logo ve ana ekranların görsel yönü kabul edildi; üretim tipografisi, kontrast uygulaması, dil seçenekleri ve lisans modeli ayrıca netleştirilecek. Marka kararları BRAND_IDENTITY.md içinde.

## Önerilen ana kullanıcı akışı

1. Kaynağı ekle: Arşiv dosyası, dosya klasörü veya desteklenen posta hesabı.
2. Kaynağı incele: Klasörler, ileti sayıları, tarih aralığı, boyut ve okunamayan öğeler.
3. Hedefi seç: Başka bir posta hesabı veya desteklenen arşiv çıktısı.
4. Kapsamı belirle: Tarih, klasör, gönderen/alıcı, ileti boyutu ve ek ölçütleri.
5. Klasör eşlemesini düzenle: Yapıyı koru, yeniden adlandır veya seçilen klasörleri birleştir.
6. Ön kontrolü gör: Planlanan aktarım, yinelenme politikası ve hedefin desteklemediği özellikler.
7. İşlemi çalıştır: İlerleme, duraklatma, güvenli devam ve hata yeniden denemeleri.
8. Sonucu doğrula: Kaynak/hedef hesabı, öğe bazında sonuçlar ve dışa aktarılabilir rapor.

## Önerilen mimari sınırlar

- Kaynak okuyucuları: Dosyalar ve servislerden erişilebilen öğeleri okur, yeteneklerini bildirir.
- Ortak ileti modeli: Özgün ileti içeriğini ve kaynak kimliğini mümkün olduğunca korur; klasör, tarih, durum ve ekleri ayrı izler. Birbirine uymayan özellikleri sessizce kaybetmez.
- Kural ve eşleme katmanı: Filtreleri, klasör eşlemesini ve yinelenen ileti politikasını uygular.
- İş motoru: Kuyruk, kayıtlı ilerleme noktaları, tekrar deneme ve yarıda kalan işin devamını yönetir.
- Hedef yazıcıları: Hedef servisin veya biçimin izin verdiği işlemleri uygular; oluşturulan hedef öğelerini kaydeder.
- Yetenek matrisi: Her bağlantının okuma/yazma, klasör/etiket, tarih/durum koruma, filtreleme ve devam özellikleri ayrı kaydedilsin. Kaynak-hedef seçimi bu matrise dayansın. Ürünün çok yönlü olması her dosya biçiminin mutlaka yazılabilir hedef olacağını varsaydırmasın; OST/OLM/POP dahil her biçim/protokolün rolü teknik fizibilitede doğrulansın.
- Doğrulama ve raporlama: Her öğeyi aktarıldı, atlandı veya başarısız olarak gerekçesiyle izler. Doğrulama seviyesi hedefin sunduğu verilere göre açıklanır.

Bu ayrım bir mimari öneridir; dil, arayüz çatısı, veritabanı ve kütüphaneler henüz seçilmedi.

## Önerilen güvenilirlik ilkeleri

- Varsayılan aktarım kaynağı koruyan kopyalama olsun. Kaynaktan silme ayrı, açıkça seçilen işlem olsun.
- Yeniden çalıştırma ve kopma sonrası devam, aynı iletileri kontrolsüz çoğaltmasın.
- Tarih filtresinin dayandığı alan ve saat dilimi kullanıcıya açık olsun.
- Desteklenmeyen kaynaklar veya aktarılamayan alanlar ön kontrolde açıkça gösterilsin.
- İş raporunda hassas ileti içeriği ve kimlik bilgileri gereksiz yere bulunmasın.
- Kullanıcı verisinin nerede işlendiği, geçici dosyaların ömrü ve kimlik bilgilerinin saklanması mimari kararı olarak ele alınsın.

## Sürümlendirme önerisi

Ürün kapsamı baştan çok yönlü planlanacak. Mühendislik doğrulamaları ve uygulama işleri aşamalara ayrılabilir; bu, ürünü tek yönlü bir geçiş aracına daraltma kararı değildir. İlk ticari sürümün destek matrisi ve teslim aşamaları kullanıcıyla ayrıca kesinleştirilecek.

Kurumsal genişleme adayları: Çoklu posta kutusu işleri, kayıtlı iş şablonları, zamanlama, operatör yetkileri ve merkezi raporlar. Bunlar henüz ilk sürüm taahhüdü değildir.

## Açık kararlar

- Windows uygulamasının kurulum/taşınabilir kullanım ve ekip kullanım biçimi.
- İlk sürümün kaynak-hedef öncelikleri; Mac/POP → Exchange örneğinin zorunlu ilk senaryo olup olmadığı.
- Exchange türleri ve şirket içi sunucu ihtiyacı.
- İş başına posta kutusu ve ileti sayısı; kullanıcı dosya başına yaklaşık 100 GB'a varan girdileri belirtti.
- E-posta dışındaki kişiler/takvim/görevlerin kapsamı.
- Çevrimdışı çalışma, verinin cihaz dışına çıkması ve ekip kullanım gereksinimleri.
- Logo vektörleştirme, erişilebilir renk uygulaması, dil ve ticari model.

## Mevcut çalışma durumu

2026-09-12: Kullanıcı tıklanabilir prototipi başlatmayı istedi. TASK-004 mevcut SOL/Gemini akışına iletildi. P1 örnek verili arayüz tamamlandı ve doğrulandı; gerçek posta motoru henüz geliştirilmedi. Güncel aşama ve kabul ölçütleri ROADMAP.md ve docs/PROTOTYPE_SPEC.md içinde.




