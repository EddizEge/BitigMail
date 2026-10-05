# MailAtlas — İsim kullanım ön taraması

Tarih: 2026-09-12
Amaç: Planlanan kurumsal posta yönetimi uygulaması için MailAtlas adının mevcut kullanım ve çakışmalarını değerlendirmek.
Sonuç: Ürün adı olarak önerilmiyor. Aynı ad, yakın işlevlere sahip mevcut bir e-posta yazılımında kullanılıyor. Bu bir tescil ret kararı veya hukuki ihlal tespiti değildir.

## Doğrulanan bulgular

### 1. Aynı isimle mevcut e-posta yazılımı

[MailAtlas resmi sitesi](https://mailatlas.dev/) yapay zekâ ajanları için e-posta erişim araçları sunuyor. Yayıncının açıklamasında Gmail/IMAP bağlantıları, EML ve MBOX içe aktarma, ekler ve ileti metadatasını koruma, çeşitli dışa aktarma seçenekleri bulunuyor.

Ürün yayıncısının [örnekler deposu](https://github.com/mailatlas/examples) aynı markayı, Python paketini, CLI ve entegrasyon örneklerini gösteriyor. Bu bulgu yalnız park edilmiş bir alan adına değil, yayımlanmış bir yazılım projesine dayanıyor.

Bizim kapsamımızla kesişen noktalar: E-posta hesapları, arşiv dosyaları, ileti işleme, içe/dışa aktarma ve metadata. Ticari değerlendirme: Aynı yazılım alanında adın birebir aynı olması, arama sonuçlarında ve müşterilerin ürünleri ayırt etmesinde belirgin karışıklık yaratabilir. Tescil durumundan bağımsız olarak marka yatırımı için zayıf aday.

### 2. Alan adı

[Verisign resmi .com RDAP kaydı](https://rdap.verisign.com/com/v1/domain/mailatlas.com) MAILATLAS.COM için kayıt döndürdü:
- Kayıt tarihi: 2014-11-16.
- Sorgudaki bitiş tarihi: 2026-11-16.
- Durum: client transfer prohibited.

Sonuç: mailatlas.com normal yeni kayıt için boş görünmüyor. Alan adının sahibi, satışta olup olmadığı ve mailatlas.dev yazılımıyla ilişkisi doğrulanmadı. Bu alan adı kaydı marka tescili anlamına gelmez.

mailatlas.dev üzerinde yukarıdaki ürün sitesi mevcut. .com.tr, .tr ve diğer uzantıların kayıt durumu bu taramada doğrulanmadı.

## Tescil araştırmasının sınırı

Tam ad ve boşluklu varyant için genel web araması yapıldı. TÜRKPATENT, WIPO, EUIPO ve USPTO alan adlarına sınırlandırılan tam ad web-index sorgusunda sonuç dönmedi. Bu, resmi marka sicillerinde kayıt bulunmadığını göstermez: etkileşimli sicillerin tamamı arama motorlarında indekslenmez.

Bu çalışma doğrudan resmi sicillerde tamamlanmış bir benzerlik/sınıf/ülke taraması değildir. Tescil numarası, hak sahibi, kapsanan ülkeler, mal/hizmetler veya başvuru statüsü teyit edilmedi. Türkiye'de ya da başka bir ülkede tescil/kullanım serbestisi sonucu verilemez.

[WIPO açıklaması](https://www.wipo.int/en/web/global-brand-database), küresel veri tabanı yanında ulusal/bölgesel sicillerin de aranmasının yararlı olabileceğini belirtiyor. Bu isimde ilerlemek istenirse hedef pazarlarda marka uzmanıyla kapsamlı kontrol gerekir.

## Yönetici önerisi

MailAtlas'ı nihai marka olarak seçmeyelim. Yeni isim adaylarını sunmadan önce en az tam ad, aynı sektörde kullanım ve temel alan adı çakışmalarını ön taramadan geçirelim. Mevcut sarı/turuncu/beyaz görsel yön korunabilir; isim kararı hâlâ açık.

## Yerel kanıt kayıtları

- ../.firecrawl/search-mailatlas-0.json — resmi ürün sitesi ve yayıncı deposu dahil web sonuçları.
- ../.firecrawl/search-mailatlas-1.json — ek tam ad/şirket/marka araması.
- ../.firecrawl/search-mailatlas-registry-index.json — yalnız web indeksinde resmi sicil alan adları sorgusu; boş sonuç.
- ../.firecrawl/mailatlas-com-rdap.txt — resmi kayıt yanıtı.
- ../.firecrawl/wipo-brand-database.md — WIPO açıklaması.
