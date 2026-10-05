# Aşama 5–9 — Yönetici mimari kararları

2026-09-15. Geliştirme planı; henüz uygulanmış veya kabul edilmiş ürün değildir. Kullanıcının kesintisiz ilerleme yetkisi kapsamında rutin seçimler burada kaydedilir.

## 5 — Lisans ve ölçek
Mevcut Aspose motorunu koru; lisans geçerliliğini SDK'nın gerçek doğrulamasıyla ölç. Ortam değişkeni veya UI işareti lisanslı kabulü yaratamaz. Lisans başlangıçta yüklenir; başarısız konfigürasyon sessizce lisanslı görünmez. Lisans dosyası/kimliği public DTO/loglarda gösterilmez. Deneme sınırlamaları kaynak analizi, önizleme ve iş yürütmede aynı politikanın sonucudur. Üretim lisansı yokken deneme engellerini aşma veya filigran temizleme yok.

PST/OST testi tüm alanlar ve bağımsız okuyucu ile yapılır. Büyük test önce disk rezervi ve beklenen kopya sayısını hesaplar; C sürücüsünde 2026-09-15 kontrolünde yaklaşık160GB boşluk vardır, birden fazla100GB kopyaya yetmez. Başka sürücüde kullanıcı verilerine dokunulmaz; gerekirse yalnız yeni açıkça adlandırılmış test alanı seçilir. Gerçek100GB PST/OST, dosyayı sıfırlarla büyüterek veya sparse dosya yaratarak taklit edilmez. Lisanslı büyük veri kabulü dış bağımlılık olarak açık kalır; diğer geliştirme sürer.

## 6 — Kurtarma
Kaynak salt okunur ve hash ile sabittir. SDK bozuk depolama taraması ayrı modda, yeni çıktıya çalışır. Okunamayan klasör/öğe, kesilmiş tarama ve bilinmeyen toplamlar raporda görünür; hedef dosyanın oluşması tam başarı değildir. SDK çağrısı takılmasına karşı ayrı işçi süreci ve iptal/timeout sınırı gerekir. Test hasarları yalnız oluşturulan kopyalara uygulanır; fixture/orijinal OST değişmez. Kurtarılan öğe ve ekler bağımsız oracle ile karşılaştırılır. Geri kazanım oranı yalnız bilinen kontrollü hasar fixture için verilir.

## 7 — Tutarlı seçim ve toplu işler
Tek sürümlü filtre modeli: metin alanı/gönderen/alıcı/tarih/boyut/ek, açık VE/VEYA grupları; boş filtre ve Unicode davranışı tanımlı. Sunucu önizleme ve worker aynı seçim motorunu kullanır; istemci özetini güvenilir plan sayma. Filtre, klasör eşleme, duplicate tercihi ve müşteri planın parçasıdır, devamda değişmez. Fiziksel kopyaları varsayılan koru. Aynı Message-ID tek başına dedup anahtarı değildir. Arama sonuçlarından iş oluşturma, sabit seçili public ID'ler ve ham arşiv hash'leriyle başlar. Şablonlar parola/token içermez. Öncelik yalnız bekleyen işlerin sırasını değiştirir; çalışan işi kesmez, eski FIFO varsayılanı korunur.

## 8 — Yerel kurumsal yetkilendirme
İlk Windows ürünü tek cihazda yerel işletimdir; merkezi ekip sunucusu kapsamı ayrıca ele alınır. Müşteri/proje kaydı sunucuda kalıcı katalog olur; tarayıcıdan gelen companyId/projectId tek başına yetki değildir. Oturum gerçek yerel uygulama kullanıcısına bağlanır; yönetici ve operatör rolleri ile müşteri erişim izinleri sunucuda denetlenir. Yeni kullanıcı/rol yetkisi yalnız yöneticiye verilir. Oturum süresi, çıkış ve iptal edilen yetki tüm okuma/iş/hesap/arama yollarına uygulanır. Eski anonim bootstrapping üretimde yönetici yetkisi vermez; güvenli ilk kurulum gerekir. Mevcut örnek veriler demo olarak ayrıdır; otomatik yetki/müşteri yaratmaz.

Yeni iş ve bekleyen iş dispatch aşamalarında yetki doğrulanır. İş geçmişinde gerçek başlatan kullanıcı saklanır. Tüm silme/yedek/geri yükleme işlemleri kapsam ve sahiplik kontrolüne tabidir. Denetim kayıtları token/parola/ileti gövdesi içermez. Denetim dosyası hash zinciri yalnız değişiklik tespiti sağlar; yöneticiye karşı değiştirilemezlik iddiası yok.

Yedekler arşiv dosyaları+manifest+indeks yeniden kurma bilgisini taşır; DPAPI hesap cache'leri başka makineye taşınabilir sır diye sunulmaz. Restore yeni alana ve önce içerik doğrulamasıyla yapılır; mevcut arşivi üzerine yazmaz. Saklama politikası varsayılan salt rapor/önizleme; gerçek kaynak posta silme işlevi eklenmez.

## 9 — Windows paket ve yaşam döngüsü
Mevcut .NET8 Windows motoru + WinForms/WebView2 masaüstü kabuğu; mevcut React build statik paketlenir. Node/Vite geliştirme sunucusu son kullanıcı bağımlılığı olmaz. OAuth sistem tarayıcısında kalır. WebView2 çalışma zamanı önkontrolü yapılır; kurulu değilse açık kurulum bilgisi, sahte çalışıyor durumu yok.

Loopback API korumaları korunur. Paket statik UI ve API portlarını sahiplenir; port çakışmasında yabancı süreç sonlandırılmaz. Yeni packaged-data yolu kullanıcıya özel LocalAppData altında, repo keşfinden bağımsızdır. Eski çalışma alanı verileri sessizce taşınmaz veya silinmez; isteğe bağlı doğrulanmış içe alma gerekir. Masaüstü pencereyi kapatma ile aktarımı sonlandırma ayrı kullanıcı davranışlarıdır; sistem tepsisinde çalışan işi görünür tut. Gerçek çıkışta journal kapanışı ve yeniden başlatmada açık devam politikası korunur.

Kurulum kullanıcı başına ve yönetici gerektirmeyen paket olarak hazırlanır. Program dosyası ile veri dizini ayrıdır. Kaldırma kullanıcı arşivlerini varsayılan korur. Güncelleme otomatik indirme/çalıştırma değil, doğrulanmış paketle kontrollü geçiş; sürüm manifesti ve önceki sürüme dönüş yolu. Kod imzalama yalnız gerçek uygun sertifika ile kabul edilir; kendinden imzalı sertifika ticari güvenilir imza sayılmaz. Temiz Windows ortamında kurulum/çalışma/kaldırma ve çökme kurtarma testleri tamamlanmadan Aşama9 TAMAMLANDI bildirimi yapılmaz.

## Açık kabul bağımlılıkları
Üretim SDK lisansı, gerçek kod imzalama sertifikası ve temiz Windows test ortamı henüz yok/doğrulanmadı. Bunları kullanıcıdan gereksiz erken onay istemek için kullanma; bağımsız geliştirme/test devam eder. Gerektiğinde somut hazır çıktı ve kalan kabul engeli bildirilir. Azure ve gerçek Outlook testi ise özellikle Aşama10'a ertelenmiştir.

2026-09-15 salt okunur ortam kontrolü: Windows11 Pro64bit10.0.26200; WindowsSandbox.exe yok. Hyper-V modülü var ancak Get-VM envanteri VirtualizationException ile okunamadı; temiz VM var/yok sonucu çıkarılamaz. Makine özelliği açma, yeniden başlatma veya mevcut VM değiştirme yapılmadı. iscc/signtool/msixmgr PATH üzerinde bulunmadı. C boş alanı son kontrolde146,8GiB; büyük test hedefi hazırlanırken yeniden ölçülür. Diğer disklerdeki mevcut kullanıcı verileri kullanılmaz. Bu bulgular kurulum yazılımının geliştirilmesini engellemez, temiz ortam kabulü yerine geçmez.

2026-09-19 yeniden başlatma önkontrolü: C boş alanı153397948416bayt (yaklaşık142,9GiB). WebView2 runtime153.0.4234.32 uygulama dizini mevcut. Bu envanter gerçek masaüstü başlatma veya temizWindows kabulü değildir.

19 Eylül paketleme önkontrolü: Windows SDK10.0.26100.0 içinde x64 signtool.exe ve makeappx.exe bulundu (PATH üzerinde değiller). Mevcut kullanıcı sertifika deposunda özel anahtarlı kod imzalama sertifikası görülmedi. HypervisorPresent=true ve Hyper-V modülü var; bunlar temiz test VM'si bulunduğu anlamına gelmez. WindowsSandbox/iscc/msixmgr/VBoxManage/vmrun PATH üzerinde bulunmadı. Geliştirme makinesinde unsigned iç test paketi hazırlanabilir; güvenilir imza ve temizWindows kabulü ayrı kalır. Mevcut kullanıcı VM'leri değiştirilmez.
