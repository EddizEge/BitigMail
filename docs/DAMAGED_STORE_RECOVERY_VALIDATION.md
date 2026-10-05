# Hasarlı PST/OST kurtarma — yerel kabul

20 Eylül 2026. 6. aşamanın yerel geliştirmesi kabul edildi. Bu kabul, tüm bozuk dosyaları veya 100 GB kaynakları kurtarma garantisi değildir.

Kaynak salt okunur tutulur ve öncesi/sonrası özeti denetlenir. Kurtarma ayrı bir işçi sürecinde çalışır, ana iş kuyruğuna katılır ve yalnız yeni çıktıya yayın yapar. Durdurulamayan veya durumu belirsiz işçi varsa yeni işler engellenir; bu engel yeniden açılışta korunur. Geçici başarısız çıktılar silinmez. Uygulama kapandıktan sonra kurtarma otomatik devam etmez.

Bilinen iki klasörde dört ileti ve dört ek içeren üretilmiş PST bağımsız MIME okuyucuyla karşılaştırıldı: klasör, tam UTC tarih-saat, ek adı ve ek baytlarının SHA-256 özeti eşleşti. Deneme sürümünün konu/gövde ekleri açıkça nitelendirildi; kaldırılmadı.

Aynı dosyanın yeni bir kopyasında 28672 konumundan başlayan 512 bayt FF ile değiştirildi. İşçi dört doğrulanmış ileti çıkardı, bir okunamayan sınır bildirdi ve sonucu kısmi olarak işaretledi. Bilinen test kümesi dört ileti olsa da ürün raporu bilinmeyen özgün toplamı tahmin etmez. Ayrı 4096 bayta kesilmiş kopya sıfır iletiyle okunamayan kaynak sonucu verdi. Bütün kaynak kopyalarının işlem öncesi/sonrası özetleri değişmedi.

Klasör ve fiziksel kaynak kimliği kayıtları korunur; posta dışı öğeler dışlanır ve sayılır. Kurtarma manifesti arşiv/aktarım seçimlerinde tanınır. Kısmi kaynak uyarısı ve temkinli tarih filtresi engeli sonraki işlere taşınır. Bu kayıt kendi iç tutarlılığını kanıtlar; yayıncı imzası değildir.

Kurtarma ekranı seçili müşteri/projeyi kullanır. Rapor iş ekranından veya İş Merkezi'nden indirilebilir; kayıtlı rapor değişmişse sunulmaz. Gerçek motor akışı üç ekran boyutunda geçti. Genel motor639/639, arayüz148/148; derleme ve statik kontroller başarılı.

Açık sınırlar: temsilî müşteri hasarları, lisanslı çıktı ve büyük dosya kabulü bekliyor. Kök klasör okunamazsa geri kazanım mümkün olmayabilir. Silinen iletileri geri getirme veya blok düzeyinde kurtarma vaat edilmez. SDK içindeki bellek kullanımı için kesin üst sınır doğrulanmadı. Azure ve gerçek Outlook testi 10. aşamadadır.
