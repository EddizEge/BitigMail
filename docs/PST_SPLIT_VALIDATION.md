# PST bölümleme doğrulaması — TASK-012

2026-09-13. Status: DONE — ASTRA ACCEPTED.

## Kaynak ve bağımsız beklenen sonuç

Yıl deneyi için mevcut gerçek PST: `lab/ost-spike/output/genuine-full-converted-04.pst`, 271.360 bayt, SHA-256 `98C1971DBED4F504865B32922147244EDE2AD669AD3694E0DCE0997270D6AC44`. Önceki doğrulanmış OST dönüşümünün çıktısıdır; bu görevde PST girdi olarak kullanılacaktır. Aynı kaynak kümesinin OST'si `lab/ost-spike/input/bitigmail-lab-full.ost`, SHA-256 `B0801758A2E61D4CE6E86799701A81A7A60C38401F73B13C993D94C03A2EE57A`.

Astra mevcut PST'nin değişmemiş hash'ini doğruladı; libpff kaynak dışa aktarımındaki **Client submit time**, yoksa **Delivery time** üzerinden UTC+03 yılını bağımsız hesapladı:

| Yıl | İleti |
|---|---:|
| 2022 | 1 |
| 2023 | 2 |
| 2024 | 8 |
| 2025 | 1 |
| 2026 | 1 |
| Toplam | 13 |

Toplam dört ek. 2024'ün ilk günündeki CID iletisi UTC'de 2023-12-31 21:30 olduğu için yıl sınırını özellikle sınar. 2023'teki iki fiziksel kopya birleştirilmeyecek. Kaynak oracle: `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task012-qa/year-source-oracle.json`.

## Kabul kontrolleri

### Tamamlanan motor ve bağımsız dosya kabulü

- Son backend derleme/test: **120/120 PASS, 0 skipped**. Root dört odaklı test ekledi: rapor/final iş kaydı diske yazılana kadar public durumun `verifying` kalması ve ikinci convert/split işinin reddi; gerçek PST ve OST parçalarının fiziksel klasör yolu/ileti histogramının kaynakla aynı olması.
- Root, bölümleme traversal'ında klasör adının iki kez eklenmesini düzeltti. Önceki başarısız bağımsız çıktılar `independent-bb165a9d` / `independent-f7c0c730` altında tarihsel olarak korunuyor; bunlar kabul çıktısı değildir.
- Eski ve yeni idempotency/restart testlerinde, arka plan yazıcısı bitmeden aynı runtime'a ikinci manager açılması veya kaynak temizlenmesi yarışları giderildi; bounded active-writer bekleyişi kullanılıyor, ürünün idempotency doğrulaması gevşetilmedi.
- Son gerçek HTTP kabulü **81/81 PASS**: session/allowlist/typed handle sınırları, geçersiz plan/filtre/boyut, boş seçim, aynı key/aynı ve farklı hedef, gerçek PST yılı, filtreli OST, 1 MB boyut bölümü; tüm final dosya boyutları/SHA ve manifest yolları fiziksel diskle eşleşti. Root aracı `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task012-qa/api-split-check.py`, raporu `api-split-report.json`. Yalnız TestingHost fake picker kullanıldı; normal kullanıcı seçicisi otomatik kullanılmadı.
- Gerçek PST yıl çıktısı **job-118f1bb76df7**, `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-qa-split/split-out-24f8997f289d4074a756d280a74fbb09/arsiv-job-118f1bb76df7`: beş PST; 2022=1, 2023=2, 2024=8, 2025=1, 2026=1; toplam 13 ileti, dört ek. Bağımsız libpff kaynak/parçalar çoklu-kümesinde eksik=0, fazla=0, ortak gövde farkı=0; klasörler ve ek SHA'ları eşleşti. Kanıt `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task012-qa/independent-5d8ac727/report.json`.
- Filtreli OST **job-4dc66c12efde**, İstanbul 2024-01-01..2024-02-16: kaynak13/seçilen3/dışlanan10; bir 2024 PST, bir ek. HTTP/aynı SDK doğrulaması PASS.
- Boyut çıktısı **job-8ca8b1357eb4**, `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-qa-split/split-out-a04bc332aa264adbadc09513c900afda/arsiv-job-8ca8b1357eb4`: kaynak `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-testing-fixtures/synthetic-size.pst`, SHA `85ac9ed3832ffc37dcecaeb4c719106c3954a065973e9ab396318ca73fcdd7f0`. Dokuz ileti/dokuz 256 KiB ek, beş parça (2/2/2/1/2 ileti); dört dosya 779.264, biri 525.312 bayt; tamamı 1.000.000 bayt altında. Bağımsız libpff eksik=0/fazla=0/ortak gövde farkı=0, tüm ek hash'leri ve klasörler eşleşti. Kanıt `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task012-qa/independent-3b198fc8/report.json`.
- Bağımsız libpff 20180714 `-f all`, kaynaktaki bazı sıkıştırılmış RTF akışlarında checksum uyuşmazlığı nedeniyle ileti başına ek dışa aktarımını atlayabiliyor ve yine exit 0 dönebiliyor. Bu yüzden kanıt ayrı `-f text` ve `-f html` geçişleriyle toplandı; export hata metni de kontrol ediliyor. Metin/HTML ve ekler ölçüldü; sıkıştırılmış RTF, CID ve özel MAPI alanları bağımsız PASS sayılmadı.
- Son test fixture düzeltmesi: eski boyut kaynağı korunarak `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-testing-fixtures/task012-v2/synthetic-size.pst` kullanılıyor. SHA `b550a53a389b469c168b03752f39658fd47a6eeccf3cf433a1005dc573784b59`. Üreticide temizlenmiş tarih alanları bu yeni dosyada etkin; libpff dokuz ileti/dokuz deterministik 256 KiB ek ve gerçekten tarihsiz iki iletiyi doğruladı. Ayrıntılı bağımsız konu/yıl/klasör/ek manifesti `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task012-qa/fixture-v2-90c2d4c7/report.json`; TestingHost sidecar'ı yalnız dosya SHA/boyut/adet kontrolüdür. Konu değerlendirme işaretleri dosyalarda korunur, beklenen başlangıç konusu ile farkı gözlem olarak yazılır. Bu test-fixture yolu değişikliğinden sonra 42 güvenlik testi PASS; ana motor değişmedi.

### Kullanıcı arayüzü ve son entegrasyon

- SOL: frontend typecheck/lint/build PASS; Vitest **49/49**, mock Playwright **10/10** PASS. Tam gerçek TestingHost Playwright **7/7 PASS**; test hizmeti koşu sonunda kapatıldı.
- Root gerçek UI kabulü **3/3 PASS**: 1660×948 gerçek PST yıl bölümü `job-6c8568509691` (13 ileti, 4 ek, 1 CID, 5 parça); 1366×768 v2 boyut bölümü `job-38e9c4253adf` (9 ileti, 9 ek, 5 parça); 390×844 filtreli OST `job-f6a9fb0d3451` (13 kaynaktan 3 ileti, 1 ek/CID, 10 dışlanan, 1 parça).
- Kaynak/klasör/tarih/strateji/hedef seçimi, boş seçimin engellenmesi, eski gecikmiş 1 MB planının yeni 2 MB planını bozamaması, JSON rapor indirme, sayfa yenileme ve İş merkezi üzerinden aynı işe/filtrelere dönüş doğrulandı. Üç genişlikte belge taşması, JavaScript veya konsol hatası yok; parça tablosu dar ekranda kendi içinde yatay kayar. Kanıtlar `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task012-qa/ui-split-report.json`, `report-*.png`, `parts-*.png`, `download-*.json`.
- Görsel kabulde root, üst raporun varsayılan sıfır ek/CID sayısını gösterdiğini buldu. Parça dosyaları ve parça raporları doğruydu. `PstSplitter` toplam yeniden açma doğrulamasını tüm parçaların ölçülen sonuçlarından dolduracak şekilde düzeltildi; split kanıt etiketi de OST dönüşümü yerine bölümlemeyi belirtir. Gerçek PST/OST testleri 13 ileti/4 ek/1 CID toplamını ve parça toplamları eşitliğini sınar. Son **120/120 backend PASS, 0 skipped**; UI indirilen rapor toplamları da doğrulandı. Eski deney raporları değiştirilmedi.
- Son v2 UI boyut çıktısı bağımsız libpff ile yeniden doğrulandı: 9 ileti, 9 ek, 5 parça; eksik=0, fazla=0, ortak gövde farkı=0; tarihsiz iki ileti korunuyor. Dört parça 779.264, biri 525.312 bayt; 1.000.000 bayt sınırını aşan yok. Kaynak değişmedi. Kanıt `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task012-qa/independent-0985a32a/report.json`.
- Normal 6174 hizmeti yeniden başlatıldı; **28/28** HTTP güvenlik/sınır kontrolü PASS, native seçici çağrılmadı. Session, Origin/Host, JSON, no-store, ham yol reddi ve normal motorda test uç noktalarının bulunmaması doğrulandı. Vite 5173 HTTP 200. Son teslimde normal motor 6174 (PID 47032) ve Vite 5173 (PID 40012) açık; TestingHost 6175 kapalı. PID değerleri bu teslim anına aittir. Son kaynak SHA değerleri yukarıdaki gerçek OST/PST ve iki boyut fixture'ıyla aynı.

## Sınırlar

Küçük yapay veri, değerlendirme lisansı. Her kaynak klasöründeki 50 öğe sınırı tüm iş için korunur. Boyuta göre ilk uygulama parçaları yeniden yazabilir ve kaynağı tekrar okuyabilir; 100 GB üretim performansı kanıtı değildir. Native dosya/klasör penceresi otomatik kullanılmaz; testte yalnız izole TestingHost seçicisi kullanılır. Yarım kalan işlere otomatik devam yoktur. Bağımsız libpff tüm özel MAPI alanlarını ölçmez; CID ayrıca ürünün MAPI doğrulamasına bağlıdır.
