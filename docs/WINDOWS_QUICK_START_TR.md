# BitigMail Windows — iç test sürümü

Bu sürüm Windows üzerinde yerel çalışır. Sarı–turuncu BitigMail penceresi, paketle gelen posta motoruna bağlanır. Microsoft Edge WebView2 çalışma zamanı gereklidir; Node veya ayrı .NET kurulumu gerekmez.

## İlk açılış

İlk yönetici ekranında kendiniz bir kullanıcı adı ve parola belirleyin. Parolanızı sohbetle paylaşmayın. Sonraki açılışlarda aynı kullanıcıyla giriş yapın. Uygulama verileri Windows kullanıcı profilinizde saklanır; geliştirme önizlemesindeki hesaplar yeni masaüstü profiline otomatik aktarılmaz.

Üst menü iş sırasını izler: **Müşteriler → Aktarım ve dönüşüm → İş merkezi → Arşiv ve arama → Raporlar**. Önce **Müşteriler** ekranında şirketi ve projeyi oluşturun (sağ üstteki hesap düğmesi → hesap ve çalışma alanları), ardından proje kartındaki **Hesap bağla** ile posta hesaplarını ekleyin. Aktarım, dönüşüm, PST bölme ve kurtarma işleri **Aktarım ve dönüşüm** ekranından başlatılır; çalışan işler **İş merkezi**nde izlenir, raporlar **Raporlar** ekranından indirilir, arşiv işlemleri **Arşiv ve arama** ekranındadır. Birden fazla şirket ve posta kaynağının kapsamını işlem öncesinde kontrol edin.

## İlk deneme

İçeriğini bildiğiniz küçük bir test kaynağı seçin. Kaynak, hedef, klasör eşlemesi ve filtreleri ayarlayın; önizleme raporunu kontrol edin. Filtre veya hedef değiştiğinde önizlemeyi yeniden oluşturun. İş bittikten sonra sonuç raporundaki eksik, farklı ve doğrulanamayan öğelere bakın; yalnızca “iş bitti” bildirimi tam içerik sadakati anlamına gelmez.

Aspose deneme sürümü bazı çıktılara değerlendirme işaretleri ekler. Bu işaretler lisanslı ürün kabulü değildir. POP yalnız kaynak olarak desteklenir; sunucuda silme komutu kullanılmaz. OLM/EMLX gibi kaynaklardaki tarih belirsizliği sonraki işlemlerde de korunur. Ayrıntılar [destek matrisinde](FORMAT_DIRECTION_MATRIX.md).

## Pencereyi kapatmak ve uygulamadan çıkmak

Pencerenin kapatma düğmesi BitigMail'i sistem tepsisine indirir; etkin işler devam eder. Tamamen çıkmak için tepsideki BitigMail simgesinden **Çıkış** seçin. Uygulama güvenli kapanış için etkin işin ve kayıtların tamamlanmasını bekleyebilir. Bir kayıt hatası bildirilirse uygulamayı zorla kapatmadan hata nedenini giderin ve yeniden deneyin.

## Güncelleme ve veriler

Güncellemeden önce uygulamadan güvenli biçimde çıkın. Kurulum uygulaması çalışan bir veri profiline güncelleme uygulamaz. Önceki sürüme dönüş, mevcut veri yapısıyla uyumluysa yapılabilir. Kaldırma kullanıcı arşivlerini ve hesap verilerini korur; kaldırmak veri yedeği almakla aynı işlem değildir.

İmzasız iç test paketi kullanılıyor. Windows yayıncı kimliğini doğrulayamaz; paket özetlerinin doğru olması yayıncı imzası sağlamaz. Temiz Windows kabulü ve ticari imzalama henüz tamamlanmadı.

## Sonraki aşama

Azure uygulama kaydı ve gerçek kişisel Outlook hesabında kaynak iletileri silmeden aktarım denemesi 10. aşamada yapılacak. Bu adım için zamanlayıcı yok; yol haritasında açık görev olarak tutuluyor.
