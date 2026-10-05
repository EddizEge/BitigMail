# BitigMail — İlk görsel tasarım

Tarih: 2026-09-12. Durum: Kullanıcı logo/ana ekran yönünü yakın buldu; ardından İş merkezi ve Ön kontrol için “tamadır iyi görünüyor” geri bildirimi verdi. Bu görseller sonraki tasarımlar için kabul edilen görsel temeldir. Çalışan uygulama veya teknik destek kanıtı değildir.

## Çıktılar

- Logo ve kimlik: ../design/concepts/bitigmail-brand-v1.png
- Proje çalışma ekranı: ../design/concepts/bitigmail-workspace-v1.png
- İş merkezi: ../design/concepts/bitigmail-job-center-v1.png
- Ön kontrol: ../design/concepts/bitigmail-preflight-v1.png
- Yerleşik Imagegen üretim ve düzeltme istemleri: ../design/concepts/PROMPTS.md

Referansların gerçek boyutları: proje ekranı 1660×948, İş merkezi 1659×948, Ön kontrol 1660×948 piksel. Uygulama karşılaştırması bu boyutlarda yapılır; ortak 1660×948 viewport kullanılırsa İş merkezindeki tek piksellik fark kayda geçirilir.

## Tasarım yönü

İki katlanmış belgeyle B harfini çağrıştıran işaret; sarı, turuncu ve beyaz kimlik. Koyu metin, beyaz yüzeyler ve ince ayraçlarla kurumsal, okunaklı bir masaüstü çalışma alanı. Logo üretim öncesinde vektör çizimine ve küçük boyut kontrolüne ihtiyaç duyan raster bir öneridir.

Ekran müşteri/proje bağlamında düzenlendi. Solda hesaplar ve dosyalar; ortada arama, tarih/ek filtreleri, ileti listesi ve altında önizleme; sağda hedef hesap, klasör eşlemesi, kapsam, yinelenme tercihi ve ön kontrol eylemi bulunur. Bu yerleşim önceki sağ önizleme önerisine alternatif olarak önizlemeyi listenin altına alır.

Örnek senaryo Yerel IMAP → Microsoft 365'tir. Ürünün çok yönlü kapsamı korunur; bu görsel bütün destek matrisini veya diğer iş türlerini temsil etmez. Tüm adlar, hesaplar, sayılar ve içerikler örnektir.

## Etkileşim anlamı

- Sarı ileti satırı yalnız önizlenen iletiyi gösterir. Aktarım kapsamı sağda açıkça belirtilen filtreye uyan 248 iletidir.
- Tek tek ileti seçerek kapsam oluşturma ayrı bir tasarım durumu olarak ele alınacak; satır odağıyla karıştırılmayacak.
- Kaynak iletiler korunur; bu ekranda kaynak silme seçeneği yoktur.
- Ön kontrol düğmesi doğrudan aktarım başlatmaz. Kontrol sonucunda çözülmemiş bir karar varsa başlatma düğmesi kapalı kalır. Örnek boyut sınırı 35 MB yalnız temsili hedef ayarıdır; Microsoft 365 için genel sınır iddiası değildir.
- İş merkezinde farklı müşterilerin taşıma, dönüştürme, bölümleme ve kurtarma işleri aynı listede görünür. Durumlar metin ve simgeyle birlikte belirtilir.
- İş ilerlemesi örneğinde 100 aktarılan + 3 atlanan + 1 başarısız = 104 işlenen; 144 bekleyenle toplam 248 ileti vardır. Yaklaşık %42 işlenmiştir; bu oran doğrulanmış aktarım başarısı anlamına gelmez. Yeniden deneme kuyruğundaki başarısız öğe nihai raporda son durumuyla uzlaştırılmalıdır.

## Görsel inceleme

Görseller incelendi: marka yazımı, beyaz/sarı/turuncu yönü, üç bölümün sınırları, Türkçe ana metinler, ana eylemin görünürlüğü ve örnek veri etiketi kontrol edildi. İlk logo üretimindeki koyu/parıltılı fon beyazla değiştirildi. İlk ekranın tek işaretli satırla 248 iletilik kapsamı karıştıran onay kutuları kaldırıldı; kaynak koruma ayarı düz metne çevrildi.

Bu aşamada tarayıcı testi, uygulama derlemesi veya motor doğrulaması yapılmadı; yalnız görsel tasarım üretildi. Kesin renk kontrastları, klavye kullanımı, ekran ölçeklendirme ve küçük pencere davranışı uygulama aşamasında ölçülecek.

## Sonraki tasarım durumları

Kaynak/hedef ekleme; gelişmiş filtreler ve tek tek seçim; ön kontrol kararının çözülmesi; ayrıntılı aktarım ve sonuç raporu; dönüştürme, arşiv bölümleme ve kurtarma akışları. Kabul edilen görsel temelle ilerlenir. Uygulamada küçük bağlantı metinleri de marka paletine uyarlanacak; Ön kontrol görselindeki tek mavi bağlantı bir tasarım sapmasıdır.
