# BitigMail — Tam sürüme kadar durum listesi

Güncelleme: 2026-09-21. Sıralama geliştirme planıdır; tarih/teslim taahhüdü değildir. Tamamlandı ifadesi belirtilen test kapsamıyla sınırlıdır. Aşağıdaki tarihli eski kayıtlar kendi dönemlerinin kanıtıdır.

**21 Eylül — Arayüz ve hesap bağlantısı revizyonu (TASK043):** 0.9.3 Windows paketi hazır ve yerel kabulü geçti; açık 0.9.2 uygulamasından normal çıkış sonrasında kullanıcı kurulumu güncellenecek. Yönetim formları ayrı temalı panele taşındı, aktarım yönleri sadeleştirildi, gelişmiş filtreler açılır bölüme alındı, gerçek müşteri/proje kayıtları hesap ekranlarına bağlandı. Mevcut müşteriye proje ekleme ve açık seçimle şifresiz IMAP/POP bağlantısı eklendi. 892 motor / 151 arayüz testi, masaüstü/telefon ve gerçek Windows görünümü geçti. [Kabul kaydı](WINDOWS_RELEASE_ACCEPTANCE.md). Azure/gerçek Outlook 10. aşamada kalır.

**19 Eylül 2026 — Çalışma yeniden başladı:** Kullanıcı 9. aşama dahil devam edilmesini istedi. Kota başlangıçta %93; %5 payı koruma kuralı sürüyor. TASK033 kabul eksikleri tamamlanarak sıralı paketlere geçiliyor. Güncel durum [yürütme kaydında](STAGES_3_9_EXECUTION.md). Önizleme bu tarihte güncel kaynaklardan yeniden başlatıldı. Aşağıdaki duraklama kaydı 15 Eylül geçmişidir.

**15 Eylül duraklama geçmişi:** Kullanıcının%5kota payını korumak için%6kalan seviyesinde Aşama4/TASK033 devam kaydı bırakıldı. Son526motor/145arayüztesti ve gerçekUI3/3PASS; TASK033genel alan sadakati/negatif örnek/kısmi rapor kapıları açık. Aşama9/Windows hazır değil. [Devam kaydı](DEVELOPMENT_CHECKPOINT_2026-09-15.md) güncel durumu ve sonraki işleri içerir.

**Güncel durum (20 Eylül):** Aşamalar 1–2 tamamlandı; 3 ve 4 yerel geliştirme kabulünü geçti. Aşama 4: EMLX, PST/OST/OLM → EML, POP → EML ve sonraki işlemlerde kaynak uyarılarının korunması. Format/yön sınırları [destek matrisinde](FORMAT_DIRECTION_MATRIX.md). Canlı sağlayıcı kabulü ve OLM tarih belirsizliği açık.

**Önceki teslim: BitigMail 0.9.1 Windows iç test sürümü kuruldu ve kullanıcı incelemesi için açıldı.** TASK040/041 yerel geliştirme ve dağıtım kabulü tamamlandı: 877 motor / 151 arayüz testi, kurulum sınırları, gerçek güncelleme/geri dönüş/kaldırma ve doğrudan eski sürüm açılışının reddi doğrulandı. [Windows kabul kaydı](WINDOWS_RELEASE_ACCEPTANCE.md). [8. aşama kabulü](STAGE8_ACCEPTANCE.md) de tamamlandı. Kullanıcının ekran revizyonları bekleniyor. Azure ve gerçek Outlook 10. aşamadadır.

**Windows kabul sınırı:** Kullanıcı kod imzalama sertifikası olmadığını doğruladı. İmzasız iç test paketi hazır; güvenilir yayıncı imzası ve temiz Windows testi açık. Bu nedenle 9. aşamanın tüm ticari kabul koşulları tamamlandı sayılmaz.

**Aktif talimat (2026-09-15):** Kullanıcı Aşama 3–9 geliştirmelerinin ara onay beklemeden sırayla yürütülmesini istedi. Azure kurulumu ve gerçek Outlook aktarım pilotu Aşama 10'a taşındı; Aşama 10 bu çalışmanın dışında. Aşama 9 kabulü ve Windows uygulaması hazır olduğunda kullanıcıya haber verilecek. Zamanlayıcı yok. Dış bağımlı doğrulamalar kanıtsız tamamlandı sayılmaz; bağımsız geliştirmeler sürdürülür. Takip: [yürütme kaydı](STAGES_3_9_EXECUTION.md).

**Kabul sınırları:** Temel kuyruk tek etkin işlem ve32bekleyen işle sınırlıdır; yeniden açılışta otomatik çalışmaz. İş merkezi sayfalıdır; Raporlar ve Arşive ekle seçicisi eski kayıtları gizlememek için halen tam geçmişi tek seferde okur. Yaklaşık1GiB testinde8192ileti/2464ek doğrulandı; bu100GB/PST/OST, kurtarma veya canlı sağlayıcı kabulü değildir. Gözlenen iş süreleri: içe aktarım35dk22sn, MBOX21dk03sn, EML12dk24sn, arşiv1dk38sn; önizleme ayrıdır. Ölçülen en yüksek süreç belleği yaklaşık3,37GB; azami bellek/hız garantisi verilmez. Kanıt: TASK028 ve TASK029 kabul kayıtları.

## Yapılanlar
- Ürün adı, sarı/turuncu/beyaz kimlik, Windows önceliği ve BT firması hedefi belirlendi.
- Müşteri/proje/kaynak dizini, ayrı Aktarım ve dönüşüm, İş merkezi, çok konumlu Arşiv ve arama arayüzleri yapıldı.
- Bağımsız OST -> yeni PST, tarih/klasör seçimi, PST/OST -> yıl/boyut bazlı yeni PST parçaları, EML/mboxrd -> PST çalışır; küçük gerçek örneklerde doğrulandı. Aspose deneme farkları/sınırları ve üretim lisansı konusu açık.
- IMAP -> IMAP, EML/mboxrd -> IMAP ve IMAP -> EML/mboxrd laboratuvarında çalışır. Kaynak koruma, klasör/tarih seçimi, sabit iş planı, sonuç raporu ve belirli kesintilerden aynı işe devam doğrulandı.
- Yönetilen yerel arşiv, birden fazla şirket/proje/arşiv kapsamında arama, klasör/metin/tarih/ek filtreleri, güvenli önizleme ve indeks kurtarma çalışır. Arama kapsamı seçimi kurumsal kullanıcı yetkilendirmesi değildir.
- Microsoft OAuth kişisel/kurumsal bağlantı altyapısı yerel kabul aldı; gerçek hesap pilotu yapılmadı.
- MBOX oluştururken klasörün tüm ham iletilerini saklayan liste kaldırıldı; içerik tek ileti bazında okunup doğrulanır. Metadata ileti sayısıyla büyür.
- 128MiB ve yaklaşık1GiB veri bütünlüğü kabulü tamamlandı. TASK029 ile8192ileti/2464ek üzerinde ayrı süre/CPU/bellek ölçümleri, MBOX/EML/arşiv/arama ve kaynak değişmezliği yeniden doğrulandı. Son backend469 ve frontend143 test PASS.100GB/PST/OST/hasar desteği kanıtı değildir.

## Aşamalar ve tam sürüme kadar kalan sıra
1. **Tamamlandı — İş deneyimi (TASK021):** ayrı ilerleme/son doğrulama, desteklenen yarım işlere açık devam, gerçek motorla masaüstü ve telefon kabulü.
2. **Tamamlandı — Performans ve temel kuyruk (TASK022–029):** indirme/bellek/son doğrulama iyileştirmeleri, disk önkontrolü, sayfalı İş merkezi,32bekleyen işlik tek çalışan kuyruk, yaklaşık1GiB faz ölçümleri ve bağımsız veri bütünlüğü kabulü. Daha büyük boyutlar Aşama5'te ayrı ölçülür.
3. **Yerel geliştirme kabulü tamamlandı — Servis bağlantıları (TASK031):** Google OAuth, Microsoft/Google sağlayıcı profilleri, güvenli oturum ve hata tanılama altyapısı.511motor/145arayüztesti,3ekranboyutu ve gerçekTestingHostbağlantıkaydı kabulü. Canlı Google/kurum/kota/APPEND davranışları halen kabul bekliyor. Şirket içi Exchange yalnız yapılandırılmış genelIMAP düzeyinde, doğrulanmış Exchange desteği değil. Azure kurulumu ve gerçek Outlook silmesiz pilotu Aşama10'da; yerel kabul gerçek sağlayıcı kabulü değildir.
4. **Yerel geliştirme kabulü tamamlandı — Format matrisi:** OLM ve Mac'ten gelen posta arşivlerini araştır/uygula; POP kaynak alma davranışını belirle; PST/OST ile hesaplar ve diğer dosya biçimleri arasında eksik bağlantıları tamamla. Her formatın okuma/yazma yönü ayrı; bütünformatlara bütünyönlerde yazma sözü yok. GenelMBOX varyantları mevcutmboxrd kabulünden ayrı.
5. **Yerel altyapı kabul edildi; ticari lisans/ölçek açık — PST/OST üretim motoru:** SDK/dağıtım lisansı kararı, denemeişaretsiz çıktı kabulü; temsilî büyük ve çeşitli dosyalar;10/25/50/100GB gibi kademeler kapasite ve kabul kararına bağlı. Klasör,ileti,ek,tarih,işaret alanları ve yenidenbaşlatma ayrı doğrulanır.
6. **Yerel kontrollü hasar kabulü tamamlandı — Kurtarma:** bozukPST/OST analiz ve yeniçıktıya kurtarma; okunabilen/kurtarılamayan bölgeler, kısmi sonuç ve öğe düzeyi rapor. Sağlıklı dosya dönüşümü kurtarma kabulü değildir.
7. **Yerel kabul tamamlandı — Gelişmiş yönetim (TASK037):** kapsamlıgönderen/alıcı/konu/ek/boyut/VE-VEYA filtrelerinin aktarım akışlarına tutarlı uygulanması; seçilen arama sonuçlarından iş oluşturma; klasör/etiket eşleme, işşablonları, gelişmiş topluiş/öncelikli kuyruk ve tercihli yinelenme politikası. Kişi/takvim/görevler ilk ticari sürüm için açık kapsam kararı.
8. **Yerel kabul tamamlandı — Kurumsal işletim:** müşteri verisi ayrımı ve gerçek yetkilendirme kapsamı; işlemlerin denetim kaydı; güvenli hesap erişimi/sır yönetimi; taşınabilir arşiv, yedekleme/geri yükleme ve saklama önizlemesi; müşteri teslim raporu. Saklama önizlemesi veri silmez; hukuki saklama güvencesi değildir. Merkezi ekip yönetimi/ortak sunucu isteğe bağlı açık karar, mevcut çok şirketli arayüz bunun yerine geçmez.
9. Windows ürünü: masaüstü kabuğu ve arkaplanişçisi yaşam döngüsü; kurulum/kaldırma, sürümgüncelleme/geri dönüş, kodimzalama, bağımlılıklar, temizWindows testleri ve çökme kurtarma.
10. Pilot ve ticari1.0 (bu çalışmanın dışında): Önce Azure uygulama kaydı ve gerçek kişisel Outlook hesabında kaynak silmeden aktarım pilotu. Ardından BTfirmalarıyla temsilî müşteri senaryoları; kabul edilmiş kaynak/hedefmatrisi ve bilinen sınırlar; güvenlik/veribütünlüğü/yükseltme testleri; kullanıcı rehberi/destek; lisanslama/fiyatlama ve marka/alanadı kararları; sürümkontrol listesi.

## Tam sürüm için bitiş ölçütü
Arayüzde bulunması yeterli değildir: duyurulan her kaynak/hedef yönü gerçek ve temsilî veride, kesinti ve alan koruma testleriyle kabul edilmiş; ticari lisanslı motorla çalışan, Windows'ta kurulup güncellenebilen, kullanıcıya kısmi/başarısız sonuçları açıkça bildiren ürün. Kapsam ve motor kararları kapanmadan güvenilir yüzde veya teslim tarihi verilmez.

Kanıtlar: docs/SCALE_AND_RESILIENCE_VALIDATION.md; docs/LOCAL_ARCHIVE_SEARCH_VALIDATION.md; docs/FILE_ACCOUNT_BRIDGE_VALIDATION.md; docs/PST_SPLIT_VALIDATION.md; .codex-coordination/results/TASK-020.md.
2026-09-14: Kullanıcı aşamaları sırayla uygulamayı onayladı. Aşama1 TASK021 ile başlatıldı; henüz tamamlanmadı. Sonraki aşamaya ilgili kabul kaydından sonra geçilecek.

2026-09-14: Aşama1 TASK021 ile TAMAMLANDI. Gerçek ilerleme/son doğrulama ve explicitkesintidendevam UI kabulü; backend448/frontend136PASS, desktop1660/mobile390 gerçekAPI. Kanıt: .codex-coordination/results/TASK-021.md. SıradakiAşama2: performans ve kaynakölçümleri. Azuremanualsonrakiaşamanotu korunur.

2026-09-14: Aşama2 TASK022 ile BAŞLADI. Önizlemede yinelenen ileti indirme adayının önce/sonra ölçümü, aşama bazlı korunankayıtlar; sonra diğer darboğazlar. Henüz tamamlanmadı.

2026-09-14 — TASK022 ilk performans dilimi TAMAMLANDI; Aşama2 devam ediyor.
Aynı iletinin ikinci kez indirilmesi kaldırıldı. 1024 ileti/308 ek,133847116 ham bayt ile önce/sonra kabulü: önizleme10,63 ->8,93 sn; aktarım57,53 ->52,01 sn. Tek yerel karşılaştırma; genel hız garantisi değildir. Önizleme tepe belleği biraz arttı; aktarım tepe belleği azaldı. Backend448/448PASS; ham içerik/ek/metadata ve kaynak12/4 korunmasıPASS. Kanıt: .codex-coordination/results/TASK-022.md ve evidence/TASK-022/root-comparison.json.
Sıradaki Aşama2 işi: çıktı alanı için disk ön kontrolü; ardından son doğrulama/klasör erişimi, büyük liste/kuyruk davranışı ve eksik1GiB ölçümleri. Azure/gerçekOutlook Aşama3 manuel hatırlatma olarak açık; zamanlayıcı yok.

2026-09-14 — TASK023 disk kontrolünün ilk dilimi TAMAMLANDI; Aşama2 devam ediyor.
IMAP -> EML/mboxrd önizlemesinde tahmini gereken/kullanılabilir alan görünür. Yetersiz veya sorgulanamayan alanda yeni plan engellenir; iş başlamadan tekrar kontrol edilir. Backend454/454 ve aktarım ekranı21/21PASS; typecheck/lint/buildPASS. Gerçek12ileti EML aktarımı eksiksiz; kaynak12/4 korundu. Normal6174 güncelReleasePID24924; Vite5173PID13512; test6175kapalı.
Kapsam ve kalan disk işleri: docs/DISK_CAPACITY_SCOPE.md. Eski tahminsiz planlar önceki devam davranışını korur; kontrol alan rezervasyonu değildir. PST/OST/bölme/yönetilen arşiv disk kontrolleri henüz bu kabulde yok. Ardından son doğrulama/klasör erişimi, bellek/büyük liste/kuyruk ve eksik1GiB ölçümleri açık. Azure/Outlook Aşama3; zamanlayıcı yok.

2026-09-15 — TASK024 TAMAMLANDI ve root tarafından kabul edildi. Ortak disk kapasitesi kontrolü OST->PST, PST/OST bölme, EML/mboxrd->PST ve yönetilen arşivleme akışlarına eklendi; TASK023 IMAP->EML/mboxrd davranışı korunur. Hedef kullanılabilir alanı ilk çıktı yazımından önce kontrol edilir; bilinmeyen/yetersiz alan açık engeldir. Tahminler güvenlik payıdır, alan rezervasyonu/garantisi değildir. Arşivde mevcut ortak taban için ham+indeks/WAL toplamı hesaplanır; ayrı indeks hedefi desteklenmez.
Kabul: backend460/460PASS0skip; kapasite12/12PASS; ilgili frontend36/36PASS; typecheck/lint/buildPASS. Küçük SDK dönüşüm/bölme ve gerçek corpus arşiv/arama senaryoları tam regresyon içinde çalıştı; bağımsız yeni canlı sağlayıcı testi veya büyük ölçek deneyi yapılmadı. Kanıt .codex-coordination/results/TASK-024.md ve evidence/TASK-024/verification-summary.json. Kapsam docs/DISK_CAPACITY_SCOPE.md.
Normal güncelRelease6174PID20792; Vite5173PID21576; test6175kapalı. Aşama2 DEVAM EDİYOR: sırada son doğrulama/klasör erişimi performansı, bellek ve büyük liste/kuyruk davranışı, eksik1GiB ölçümleri. Azure/gerçekOutlook Aşama3; zamanlayıcı yok.

2026-09-15 — TASK025 TAMAMLANDI. Dışa aktarımda sadece hash kontrolü için kullanılan üç tam dosya tamponu akış okumasına çevrildi; kısa ömürlü MIME nesneleri kullanım sonrası kapatılır. MBOX yazıcısı ve bütünlük kontrolleri korunur. Backend460/460, hedefli16/16PASS. Aynı1024ileti/308ek,133847116 ham baytta iki koşunun raw/MIME/metadata kontrolüPASS; kaynak12/4 ve önce/sonra kaynak+IMAPsnapshotları değişmedi.
Tek yerel önce/sonra gözlemi: önizleme6,885->6,649sn, tepeWS581910528->496398336bayt(-%14,70); aktarım45,289->43,791sn, tepeWS804315136->732250112bayt(-%8,96). Genel hız/bellek garantisi değildir. Kanıt .codex-coordination/results/TASK-025.md ve evidence/TASK-025/root-comparison.json.
Güncel normalRelease6174PID48492; Vite5173PID21576; test6175kapalı. Aşama2 DEVAM: sunucudaki son hedef doğrulamasında tekrarlanan klasör erişimi için güvenli kapsamlı oturum tasarımı/ölçümü, diğer bellek ve büyük liste/kuyruk işleri, eksik1GiBtelemetri. Klasör oturumu/batch değişikliği henüz yapılmadı; clientmetot sayısı gerçekprotokolkomut ölçümü değildir. Azure/gerçekOutlook Aşama3; zamanlayıcı yok.

