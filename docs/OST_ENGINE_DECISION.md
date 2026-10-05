# BitigMail — Bağımsız OST motor değerlendirmesi

Tarih: 2026-09-12. **TASK-008 gerçek OST→PST deneyi DONE_WITH_DIFFERENCES.** 13/13 öğe, bağımsız okuyucu ve profilsiz/ağsız ortam kontrolü geçti; dört ekin dosya içeriği korundu. Satır içi CID ve tam alan uygunluğu açık. Üretim motoru seçilmedi.

Kullanıcının kesin gereksinimi: yalnız OST dosyası eldeyken, eski posta hesabı ve orijinal Outlook profili olmadan iletileri çıkarıp PST'ye yazabilmek. TASK-007'nin [yapay EML/MBOX deneme kümesi](../fixtures/mail-corpus-v1/README.md), yerel IMAP laboratuvarına yüklendi ve kullanıcı tarafından klasik Outlook ile gerçek OST'ye eşitlendi. İlk kopyada yalnız Gelen Kutusu vardı; eksik sunucu abonelikleri düzeltildi ve kullanıcı tam kopyayı teslim etti. Tam kopya 16.818.176 bayt; 12 temel ileti ve 1 Outlook sınama iletisi içeriyor. İlk tam koşuda 13/13 öğe yeni PST'ye yazıldı, kaynak hash'i değişmedi. [Deney raporu](../lab/ost-spike/OST_EXPERIMENT_RESULT.md) ve [ana yol haritası](../ROADMAP.md) güncel kabul durumunu tutar. Aşağıdaki aday matrisi kaynak araştırmasının başlangıç değerlendirmesidir; ölçülmeyen sürüm, hasar ve ölçek desteğine genellenmez.

## 1. Mimari karar: okuma ve dönüştürmeyi ayrı ölç

Aspose'un resmi kılavuzu OST okuma API'sini anlatırken doğrudan `SaveAs` dönüşümünde 2013/2016/2019/2021 ve sonraki OST sürümlerini hariç tutuyor. Bu sınırı bütün modern OST okuma yöntemlerine veya ileti çıkarıp yeni PST'ye ekleme yoluna yayamayız. Aynı sayfada PST→OST için de tutarsız açıklama bulunuyor; bu yönde destek taahhüt edilmeyecek. [Okuma ve dönüştürme kılavuzu](https://docs.aspose.com/email/net/reading-and-converting-outlook-files/).

Diğer resmi sayfa yalnız 2013/2016 istisnasını listeliyor. Yayın tarihleri teyit edilmeden birini “eski sayfa” kabul etmiyoruz; listelerin farklılığı yeni sürümlerde destek kanıtı değildir. [Bilinen sorunlar](https://docs.aspose.com/email/net/known-issues/).

**Deney sırası ve karar:** önce SDK ile öğe çıkarıp yeni PST'ye yazma sınandı. TASK-008'in küçük tam örneğinde bu yol Windows ve temiz Linux ortamında çalıştı; libpff bağımsız okuma karşılaştırması sağladı. A adayı, alan uygunluğu belirsizliklerini gidermek üzere izlenecek ilk yol olmaya devam ediyor. B adayı libpff + ayrı yazıcı, alternatif/kurtarma hattıdır; bu birleşik yazma yolu çalıştırılmış sayılmaz. Tek küçük örnek bütün modern OST sürümlerine destek kanıtı değildir.

## 2. Aday matrisi

Aşağıdaki yetenekler belge/repo beyanıdır; BitigMail'de çalıştırılmış destek matrisi değildir.

| Ölçüt | A: SDK ile öğe okuma → yeni PST | B: libpff okuyucu → ayrı PST yazıcı | Profil üzerinden Outlook çözümü |
|---|---|---|---|
| Somut bileşen | Aspose.Email okuma/çıkarma ve yeni PST API'leri | libpff/pffexport + Aspose.Email yazıcı adayı | Karşılaştırma çizgisi |
| Modern OST | Öğe çıkarımı ayrıca denenecek; doğrudan SaveAs hariç tutulur | Repo 64-bit 4K sıkıştırılmış yapı desteği bildiriyor; örnek bazında test gerekir | Orijinal bağlantıya dayanan akış gereksinimi karşılamaz |
| Eski hesap/profil | Bağımsız dosya API'si; temiz ortamda doğrulanacak | Dosya ayrıştırıcısı; temiz ortamda doğrulanacak | Böyle bir bağımlılık kabul edilmez |
| Yerel Outlook | Aday API yolu bağımsız; laboratuvar ortamında doğrulanacak | Ayrı okuyucu/yazıcı yolu; laboratuvarda doğrulanacak | Kurulum bağımlılığı ayrıca taşır |
| PST yazma | Unicode oluşturma API'si belgelenmiş | libpff yazıcımız değildir; ayrı Unicode yazıcı gerekir | Ana OST çözümü olarak seçilmedi |
| Kurtarma | Sağlıklı ve bozuk örnekte ayrı ölçülecek | Araçta recovery modları var; tam kurtarma garantisi yok | Bu araştırmada değerlendirilmedi |
| Windows dağıtımı | SDK sürümü, çalışma zamanı ve dağıtım paketi test edilecek | Yerel derleme/paket ve süreç veya bağlama yöntemi ayrıca test edilecek | Üretim yolu olarak planlanmadı |
| Lisans | Ticari değerlendirme/dağıtım koşulları aşağıda | LGPL kütüphane + ticari yazıcı koşulları | Bu yol için satın alma kararı yok |
| 100 GB | Ölçülmedi | Ölçülmedi | Ölçülmedi |

PST oluşturma API'si dosya/akış çıktısı ve Unicode sürümü tanımlıyor; ANSI oluşturma için istisna belgelenmiş. Boş PST yaratabilmek bütün MAPI alanlarının korunacağını kanıtlamaz. [PersonalStorage.Create](https://reference.aspose.com/email/net/aspose.email.storage.pst/personalstorage/create/).

libpff deposu ANSI, Unicode, 4K DEFLATE yapıları ve öğe kurtarmayı listeliyor; proje alpha, lisans LGPL-3.0-or-later. Bunlar her hasarlı dosyanın eksiksiz açılması veya Windows paketinin üretime hazır olması anlamına gelmez. [Resmi depo](https://github.com/libyal/libpff).

`pffexport` içindeki items/all/recovered modları çıkarım deneyi için incelenecek. Kurtarılan öğeler normal klasör içeriğinden ayrı sayılacak. [Araç kılavuzu](https://github.com/libyal/libpff/blob/main/manuals/pffexport.1). Yazma gerektiren pffconvert planı ve derleme geçmişi, ayrı yazıcı ihtiyacını araştırmak için kaynak sağlar; dağıtım biçimini kesinleştirmez. [ChangeLog](https://github.com/libyal/libpff/blob/main/ChangeLog).

## 3. Deneme ve dağıtım koşulları

Aspose değerlendirme modu PST klasöründen 50 ileti çıkarma sınırı, bazı çıktı türlerinde konu değişikliği/License.txt ve ek kısıtları içeriyor. Küçük dosya bile bu değişikliklerden etkilenebilir. Bütünlük karşılaştırmasında değerlendirme etkileri açıkça ayrılacak; lisans kısıtı sessiz veri kaybı gibi raporlanmayacak veya gizlenmeyecek. Gerekirse geçici lisans koşulları incelenecek. [Değerlendirme sınırları](https://docs.aspose.com/email/net/licensing-and-limitations/).

Satıcının OEM/SDK/Metered kategorileri kullanım ve dağıtım biçimine göre farklı haklar tanımlıyor. BitigMail'in masaüstü son kullanıcı dağıtımı için uygun kategori ve koşullar satın almadan önce teyit edilecek; bütün Metered lisanslar dış dağıtımı sağlar varsayımı yapılmayacak. libpff dağıtımında da seçilen bağlama/paketleme yöntemi ve lisans metinleri değerlendirilecek. Lisans onayı, fiyat veya satın alma yapılmadı. [Lisans tipleri](https://purchase.aspose.com/policies/license-types/).

## 4. Küçük gerçek OST deneyi

1. TASK-007'nin 12 yapay iletisini ve manifestini hazırla. Türkçe alanlar, üç klasör, ekler, kopyalar, aynı Message-ID ile farklı içerik ve UTC tarih sınırı bilinen vakalardır.
2. Uygun bir test posta kutusu ve OST üreten istemciyle bu iletileri içeri al, tam eşitlemeyi ve klasör/ileti/ek dökümünü kontrol et. İstemci kapalıyken OST kopyası oluştur; üretici sürümünü ve dosya formatını kaydet. Hesaba aktarımın kendisi başlıkları değiştirebilir: kaynak manifest ile OST'ye yerleşmiş içerik arasındaki farkları bu noktada açıkla.
3. Kopyayı eski hesap ve profilin bulunmadığı izole ortamda, kaynak üzerinde değişiklik yapmadan işle. İlk örnek küçük ve sağlıklı olsun. “Modern OST” ile “New Outlook uygulaması” eşanlamlı kullanılmaz; örneğin gerçekten hangi uygulama/formatla oluştuğu kaydedilir.
4. A ve gerekirse B adayında klasör/öğe çıkar; varsa özgün MIME ve mevcut MAPI alanlarını kaydet. OST'nin bütün öğelerde özgün MIME sakladığı varsayılmaz. RTF, kategoriler, okundu durumu ve diğer MAPI özelliklerinin ara MIME dönüşümünde kaybolma ihtimali ayrı alan raporuna girer.
5. Yeni PST oluştur. Yazıcıdan bağımsız okuyucu ile öğeleri tekrar oku; kaynak dökümüyle karşılaştır. Her alan için korundu/dönüştü/kayıp/belirsiz sonucu olsun. Kaynak dosya hash'i işlem öncesi ve sonrası aynı kalmalı.
6. Sağlıklı örnek başarılı olduktan sonra yalnız kopya üzerinde kontrollü hasar vakaları dene. Kurtarılan/okunamayan/şüpheli öğeleri ayır; tam kurtarma oranı vaat etme.

TASK-008 kapsamında yerel hesap/istemci hazırlığı, gerçek OST üretimi, SDK dönüşümü, bağımsız tekrar okuma ve temiz ortam deneyi tamamlandı. Kaynak/çıktı ortak gövde gösterimleri bağımsız kontrolde eşleşti; PST'ye 9 düz metin gösterimi eklendi. İlk EML ile OST görünümü arasında dört fark ve her iki MAPI okumasında boş CID alanı var; dönüşüm sırasında CID kaybı kanıtlanmadı, kabul durumu UNKNOWN. Bu gerçek koşuda filigran gözlenmedi; önceki EML→PST denemesindeki lisans değişiklikleri ayrıdır. Kontrollü hasar, kesintiden devam ve ölçek deneyleri yapılmadı. Posta dosyaları dış servise yüklenmedi.

## 5. Başarı ve başarısızlık ölçütleri

| Kontrol | Kabul | Durdurma / inceleme |
|---|---|---|
| Sağlıklı örnek | Sıfır açıklanamayan eksik öğe; klasör ve sayımlar örtüşür | Sessiz kayıp veya yanlış klasör |
| Ekler | Byte boyu ve SHA-256 aynı | Kesilme veya hash farkı |
| Temel alanlar | Konu/adres/gövde ve eşlenen tarih anlamı korunur; dönüşüm kuralları yazılıdır | Açıklanamayan fark |
| Kopyalar | Manifestteki fiziksel kopya ve aynı-ID/farklı-içerik ayrımı korunur | Yalnız Message-ID nedeniyle farklı içeriğin kaybı |
| Kaynak | Önce/sonra hash aynı; ayrı çıktıya yazılır | Kaynak değişikliği |
| PST uyumluluğu | Bağımsız okuyucu açar, gerekli alanlar karşılaştırılır | Dosya açılamaz veya öğeler okunamaz |
| Devam | İş ortasında kesinti sonrası sayım/ek/alan sonuçları temiz çalıştırmayla eşleşir | Fazladan veya eksik öğe |
| Kaynak tüketimi | Süreç ağacının tepe RAM'i, geçici disk, süre ve çıktı boyutu kaydedilir | Ölçüm yokken ölçek/hız iddiası |

Devam karşılaştırması PST'nin byte-byte aynı olmasını gerektirmez; kapsanan öğe ve alanların eşitliğini gerektirir. Kalıcı ilerleme ve yeniden deneme mekanizması SDK'da hazır varsayılmaz, deneyde ayrıca uygulanır.

## 6. Ölçek ve sıradaki iş

Küçük örnek geçerse yaklaşık 1 GB → 10 GB → 50 GB → 100 GB girdilere kademeli geçilir. Her basamakta RAM/geçici disk, ileti ve byte hızı, toplam süre, çıktı boyutu, kesinti/yeniden deneme doğruluğu ölçülür. Sayısal performans eşiği ilk ölçümlerden sonra kararlaştırılır; 100 GB girdi hedefi tek 100 GB PST çıktısı üretmek zorunda değildir.

TASK-007 yapay EML/MBOX kümesi ve TASK-008 gerçek OST deneyi tamamlandı. Sıradaki dar inceleme, satır içi CID'nin depolama özelliklerinde doğrulanması ve gövde gösterimi kabul kurallarıdır. Lisanslı/büyük klasör koşuları bu belirsizliklerden sonra ele alınacak. Gerçek müşteri verisi kullanılmadı. Üretim mimarisi ve Windows kabuğu henüz seçilmedi.

Araştırma: Gemini; kaynak denetimi: SOL. Astra, okuma/SaveAs ayrımını resmi kaynakta kontrol ederek evrensel destek/engel hükümlerini daralttı ve deney sırasını düzeltti.
