# Temel işlem kuyruğu kabul ölçütleri

TASK028 için kontrol listesi; bu belge tek başına testlerin geçtiği anlamına gelmez. Gerçek sonuç TASK028 kabul kaydında tutulur.

| Senaryo | Beklenen davranış |
| --- | --- |
| Bir işlem çalışırken ikinci iş istenir | Kullanıcının sıraya alma seçimi varsa bekler; aynı anda iki işçi çalışmaz. |
| Eski API istemcisi sıraya alma seçeneği göndermez | Önceki meşgul yanıtı korunur. |
| Aynı istek tekrar gönderilir | Aynı iş döner; kuyrukta ikinci kopya oluşmaz. |
| 32 bekleyen iş varken yeni istek gelir | Açık kapasite yanıtı verir; hayalet iş veya kilitli sıra oluşturmaz. |
| Çalışan iş başarıyla veya hatayla biter | Sıradaki uygun iş başlar; tek işçi sınırı korunur. |
| Bekleyen iş iptal edilir | Yalnız bekleyen işlem kaldırılır; kaynak veya çıktı dosyaları silinmez. |
| İptal ile başlama aynı ana denk gelir | Aynı kilit altında tek sonuç oluşur; çalışan iş bekleyenmiş gibi iptal edilmez. |
| Kuyruğa kayıt veya başlatma kaydı yazılamaz | Başarılı kabul yanıtı verilmez; etkin iş alanı hayalet kayıtla kilitlenmez. |
| Uygulama sırada bekleyen işler varken kapanır | Kayıtlar görünür kalır, kendiliğinden çalışmaz; hiç başlamayan işe yanlış devam düğmesi gösterilmez. |
| Daha önce gerçekten başlamış iş kesilir | Desteklenen iş türünde mevcut doğrulanmış devam davranışı korunur. |
| Yarım işin devam isteği sırada beklerken uygulama kapanır | Önceki günlük korunur; hiç başlamamış yeni iş gibi sınıflandırılmaz ve desteklenen açık devam seçeneği kalır. |
| Sırada bekleyen devam isteği iptal edilir | Yalnız bekleyen devam girişimi kaldırılır; önceki ilerleme ve daha sonra açıkça devam edebilme korunur. |
| Ekranda başka şirket/proje seçilir | Bekleyen işin kaynak/hedef ve sahibi değişmez. |
| Geçmişte 1.000 veya 10.000 iş vardır | Yanıt ve ekran sayfa boyutuyla sınırlıdır; filtre ve toplam sayısı tutarlıdır. |
| Uzun geçmişte etkin/bekleyen iş vardır | Tüm geçmişi indirmeden bulunabilir. |
| Sayfa/filtre değişirken yenileme yanıtı gelir | Eski yanıt yeni seçimi ezmez; üst üste sınırsız sorgu oluşmaz. |

Her mevcut yerel iş türünün ortak sıradan geçtiği kontrol edilir. Temel kuyruk zamanlama, öncelik, toplu şablon veya yeniden açılışta otomatik yürütme taahhüdü içermez; gelişmiş yönetim Aşama 7'de ayrıca ele alınır.
