# BitigMail — Çok müşterili kullanım ve işlem ayrımı

Durum: TASK-005 DONE; uygulandı ve doğrulandı. Kaynak: kullanıcının P1 prototip geri bildirimi, 2026-09-12. Derleme/tip/lint, 19 birim testi, 24 yeni kullanım ekran testi ve 22 taşıma/yerleşim testi geçti. Masaüstüne özel iki test mobilde kapsam gereği atlandı. Görsel kanıtlar: [VISUAL_QA.md](../prototype/VISUAL_QA.md).

Bu kararlar ilk prototipteki Müşteriler → doğrudan posta ekranı düzeninin yerini alır. Gerçek posta motoru bu çalışmanın kapsamında değildir.

## Kabul edilen bilgi mimarisi

- **Müşteriler:** şirket dizini → şirket ayrıntısı → projeler → bağlı posta kutuları ve dosya arşivleri. İlk açılış bir kişinin posta kutusuna gitmez. Şirket/proje/kaynak ekleme örnek veriyle çalışır; yeni boş kaynak sahte iletilerle doldurulmaz.
- **Aktarım ve dönüşüm:** ayrı ana sekme. Müşteri ve proje bağlamı, kaynak, hedef ve işlem türü açıkça seçilir. Taşıma, dönüştürme, arşivleme/bölme ve kurtarma burada bulunur. Mevcut uçtan uca taşıma simülasyonu korunur; diğer türler gerçek motor varmış gibi gösterilmez.
- **İş merkezi:** iş kuyruğu, ilerleme, müdahale ve sonuç takibi. Bir işi açmak ilgili müşteri/proje bağlamıyla Aktarım ve dönüşüm sekmesine gider.
- **Arşiv ve arama:** şirket → proje → posta kutusu/dosya arşivi ağacından çoklu konum seçimi. Klasör filtresi bu seçili kapsamın içinde çalışır. Tek aramada birden fazla şirket/proje/kaynak açık seçimle birleştirilebilir.
- **Raporlar:** mevcut örnek iş sonuçları.

## Ortak veri ve davranış sözleşmesi

1. Müşteriler, işlemler ve arama aynı şirket/proje/kaynak kayıtlarını kullanır. Görünen adlardan bağımsız kalıcı kimlikler ve açık üst-alt ilişkiler bulunur.
2. En az iki örnek şirket; en az bir şirkette iki proje; aynı projede en az iki posta kutusu ve bir dosya arşivi bulunur. Mesajlar kaynak kimliğiyle ilişkilidir. Aynı klasör adı veya mesaj kimliği farklı kaynaklarda çakışmaz.
3. Şirket/proje üst seçimi alt kaynakları topluca seçer veya kaldırır. Kısmi seçim görünürdür. Tümünü seç ve temizle bulunur; seçili kaynak sayısı ve kapsam özeti gösterilir.
4. Konum seçilmediyse arama sonuçları boş olur ve konum seçme açıklaması çıkar. Boş bir kaynak seçildiğinde başka kaynağın mesajları görünmez.
5. Sonuçlar seçili kapsamın birleşimidir; üst ve alt seçim aynı mesajı iki kez saydırmaz. Her sonuçta şirket, proje ve posta kutusu/arşiv bilgisi bulunur. Arama sorgusu ve klasör filtresi birlikte uygulanır; kapsam değişince kapsam dışı önizleme temizlenir.
6. Müşteri ayrıntısından işlem başlatma müşteri/proje/kaynak bağlamını taşır. Şirket/proje değişiminde eski kaynağın görünmez şekilde kullanılmasına izin verilmez. Aktarım ekranı kaynak ve hedefi belirgin biçimde gösterir.
7. Örnek şirket/proje/kaynak ekleme ve arama kapsamı sekmeler arasında korunur, yenilemede sürdürülür. Mevcut demo saklama verisi güvenle uyarlanır; aktif işin dondurulmuş planı değişmez.
8. Gerçek parola, bağlantı veya dosya içeriği istenmez. Eklenen kayıtların örnek bağlantı olduğu açıklanır. Taşıma kaynak koruyan simülasyon olarak kalır.

## Doğrulama ve görsel kabul

- Müşteriler açılınca şirket listesi; ayrı işlem sekmesinde kaynak/hedef akışı; şirket/proje değişiminde doğru bağlam.
- Şirket, proje ve örnek kaynak ekleme; arama ve işlem seçimlerinde görünme; yenilemede kalıcılık.
- İki farklı şirketteki kaynakları seçip birleşik sonuç ve kaynak bilgisi doğrulama; sorgu ve klasör kesişimi; kısmi üst seçim; seçim temizleme; boş kaynak; kapsam dışı önizlemenin temizlenmesi.
- Eski ön kontrol, duraklat/devam, yenileme ve rapor akışları bozulmaz. Testler yeni gezinmeye uyarlanır; eski işlevsel ve masaüstü görünürlük kontrolleri zayıflatılmaz.
- Derleme, tip kontrolü, lint, anlamlı birim testleri ve headless ekran testleri. 1660×948, 1366×768 ve 390×844 görsel kontrol; masaüstünde işlem eylemleri/önizleme kesilmez, mobilde tüm yeni seçimlere erişilir.
- Sarı/turuncu/beyaz kimlik, logo ve mevcut tasarım sistemi korunur. Yeni şirket dizini, işlem sekmesi ve çoklu arama ekranları için son renderlar ve kısa görsel kontrol kaydı teslim edilir.

## Sonraki aşamalar

Bu yineleme ürün kapsamını daraltmaz. Gerçek çoklu müşteri veri ayrımı/yetkilendirme, gerçek hesap doğrulama ve format motorları P2–P5 içinde ayrıca tasarlanıp doğrulanacaktır.
