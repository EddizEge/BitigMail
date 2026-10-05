# Aşama 4 — Dosya ve hesap köprüsü kararları

2026-09-15, ASTRA HIGH. Bu dosya uygulama/kabul planıdır; destek beyanı değildir.

## Tek ortak köprü
Yeni kaynaklar doğrulanmış, salt okunur kaynak anlık görüntüsünden yönetilen bir EML ağacına çıkarılır. Her ileti için kaynak dosya hash'i, fiziksel öğe kimliği, özgün klasör, çıktı hash'i, tarih/işaret farkları ve ek kontrolü manifestte tutulur. EML ağacı mevcut arşiv, IMAP ve PST yollarına bağlanır. Ara aşama sonuçları görünür; uçtan uca dönüşüm kaynak formatın baytlarını koruduğu anlamına gelmez.

- PST/OST: mevcut bağımsız Aspose okuyucu ile EML dışa aktarım; öğe bazlı MAPI/MIME alan ve ek karşılaştırması, deneme etkileri raporu. OST hedef yazma vaat edilmez.
- OLM: kurulu Aspose 24.8 API'sini yerinde doğrula; klasör ve öğe bazlı çıkarım. Yeni sürüm dokümanı kurulu sürüm desteği sayılmaz. Gerçek/temsilî OLM fixture yoksa OLM kabulü açık tutulur.
- EMLX: ilk ASCII satırdaki pozitif bayt sayısı kadar ham iletiyi aynen çıkar; kalan Apple metadata baytlarını yan dosyada sakla. Satır sonu/Unicode dönüştürme yapma. Metadata henüz yorumlanmıyorsa tarih/işaret korunması iddiası yok. `.partial.emlx` ve dış ek bileşenleri desteklenmeden eksiksiz diye işleme.
- Apple Mail `.mbox` paketleri: dosya ile dizin ayrımını açık yap; içteki biçimi belirle, uzantıyla otomatik mboxrd varsayma.
- MBOX: mboxrd kabulünü koru; mboxo/mboxcl varyantlarını açık seçim ve bağımsız fixture ile ekle. Belirsiz `From ` sınırlarında sessiz bölme yok.
- POP: yalnız kaynak. UIDL tabanlı anlık liste + RETR ham içerik; DELE hiçbir zaman çağrılmaz. UIDL bulunmadığında güvenli yeniden başlatma garantisi verme. Çıktı yönetilen EML ağacı; POP hedef veya POP klasör hiyerarşisi yok.

## Kaynak koruma ve yeniden başlatma
Yeni çıktılar ayrı iş dizinine, create-only ve atomik yayın ile yazılır. Yol traversal, reparse point, çakışan adlar ve yinelenen fiziksel iletiler test edilir. Önizleme manifesti iş başlamadan yeniden doğrulanır. Kaynak/çıktı eksikliği başarısız/kısmi sonucu oluşturur; işlenemeyen öğeler rapordan kaybolmaz. Yeni format okuması tek ileti belleği ve sınırlı metadata kullanır. Dosya-kutu köprüsü mevcut APPEND journal ve son doğrulama kurallarını değiştirmez.

## İlk çekirdek kanıtı
`EmlxRecordReader` + `EmlxRecordReaderTests`: 17/17 test geçti. UTF-8 bayt sayısı, karışık satır sonları, metadata birebir korunması, bozuk/taşan/eksik uzunluk, kısa stream okumaları, iptal ve partial dosya reddi. Bu yalnız okuyucu çekirdeğidir; UI/aktarım entegrasyonu ve gerçek Mac arşivi kabulü değildir.

`OlmRawAttachmentCatalog`: özgün ZIP/XML ek kaydı üzerinden çıkarım, gömülü iletileri yeniden serileştirmeden korur. 11/11 test: fiziksel kopyalar, kayıp ek reddi, yol/duplicate/XML DTD/boyut/iptal kontrolleri ve gerçek vendor fixture22email/38ek. Python XML/ZIP oracle'dan bağımsız kaydedilen38SHA256 ile .NET çıkarımı birebir eşleşti. Birleşik okuyucu testleri28/28PASS; kanıt `engine/BitigMail.Engine.Tests/TestResults/mac-reader-28.trx`. Bu henüz gövde/klasör/SDK öğe eşleştirme veya UI dönüşüm kabulü değildir. SDK BinaryData/Save yollarında5gömülü e-postanın biçim değişikliği gözlendi; ham katalog bu özgün baytları erişilebilir kılar. SDK ile öğe eşlemesi kesinleşmeden aktarımda kullanılmaz.

OLM eşleme deneyi: bu örnekte22XML email kaydı ile22SDK IPM.Note, tam klasör yolu+InternetMessageId birleşiminde22/22eşleşiyor; birleşik anahtar çakışması0. Message-ID tek başına16benzersiz değer ve iki4kopyalı grup içeriyor; tekbaşına eşleme yapılamaz. Kanıt `olm-folder-probe.json`. Entegrasyonda hem kaynakhemSDK birleşik anahtar sayıları doğrulanmalı, belirsiz/eksikfazla eşlemede durulmalı; farklı hesap kökü veya aynıklasörde yinelenenID için bu küçükörnekten destek çıkarılamaz. Gövde/adres/tarih alanları ayrıca oracle ile kontrol edilecek.

## Kaynaklar
### OLM alan ve kaynak kanıtı — 2026-09-15

`olm-representation-audit.json`: 22 iletide XML `CopyBody` alanı HTML alanıyla aynı; düz metin gövde oracle'ı olarak kullanılamaz. SDK konularının22/22'si özgün konu+değerlendirme eki, HTML görünür metni22/22 özgün metni içeriyor. Bu karşılaştırma HTML yapısı/CID/adreslerin tam kabulü değildir; deneme ekleri çıktıda korunur.

Tarih XML değerleri saat dilimi taşımıyor. Yalnız tanılama amacıyla UTC varsayımı altında18/22 SDK anıyla eşleşiyor; aynı iletinin4 fiziksel kopyasında -43200 saniye (12 saat) fark var. Önceki DST olasılığı bu ölçümle desteklenmedi. Kaynağın öğlen12:16:53 değeri SDK'da yerel03:16:53+03:00 oluyor. Sessiz düzeltme veya kesin UTC yorumu yapılmaz. Deney için yeni ZIP türevi üretildi, fakat SDK bu türevi açarken XML kökü hatası verdi; bu başarısız deney saat ayrıştırma nedenini kanıtlamaz. Özgün fixture değişmedi.

Ham katalog artık özgün tarih metinleri, Content-ID ve XML SHA256 taşır; `ReadSourceXml` özgün XML dosyasını hash doğrulamasıyla yan dosya için döndürür. Ek baytları, CID, değişmeyen tarih metinleri, XML baytları ve yanlış hash reddi:12/12 PASS (`olm-provenance-12.trx`). Entegrasyon özgün XML'i saklamalı; tarih farkını açık raporlamalı ve tam alan koruma kabulünü çözülene kadar açık tutmalıdır.

İkinci kontrollü tarih deneyi özgün ZIP64 düzenini koruyan yeni test kopyasında yapıldı; kaynak değiştirilmedi, standart ZIP CRC'leri doğrulandı. Saat00–21 için22 öğe:21 SDK sonucu tanılama UTC varsayımıyla eşleşiyor, yalnız12:16:53 değeri00:16:53 olarak dönüyor (-12 saat). Kanıt `olm-hour-parser-audit.json`; deney, öğlen saatine bağlı SDK davranışını daraltır ama OLM saat dilimi sözleşmesini kanıtlamaz. Ayrı laboratuvar projesi Aspose26.7.0 ile aynı fixture'ı okudu:24.8 ile22/22 gönderim tarihi aynı, null BinaryData sayısı yine3. Bu hata için üretim SDK yükseltmesi yapılmadı. Paket kaynağı https://www.nuget.org/packages/Aspose.Email/26.7.0 ; probe `lab/stage4-olm-next`, kanıt `olm-next267-probe.json`/`olm-next267-hours-probe.json`.

Gömülü ileti yerel MIME deneyi: MimePart(message/rfc822), ham MimeContent ve binary transfer kodlaması ile7 özgün vendor eki+1 karışık satır sonlu Türkçe sentetik ekte8/8 özgün bayt dizisi çıktıda aynen kaldı. Tekrar okuma MessagePart döndürüyor. Kanıt `embedded-binary-probe.json`, `lab/stage4-mime-embedded`. Bu yalnız yerel EML yazımı; sağlayıcı kabulü değil. MessagePart tekrar yazılarak hesaplanan hash özgün ham ek hash'i sayılamaz. İlgili sayım akışlarında OfType<MimePart> gömülü e-postayı dışarıda bırakabilir; TASK033 downstream sayım/filtre entegrasyonunda düzeltilmesi istendi. MIME gövdesi içine yeniden kodlanmış ek taşırken tam sınır/ham içerik doğrulaması gerekir.

- OLM ilk bağımsız deney: kamuya açık MIT örneği `fixtures/vendor-olm`, sabit commit/hash ve lisans kaydı mevcut. Kurulu 24.8 okuyucuda 25 öğe/38 ek; XML'de 22 email öğesi. Ek BinaryData yolu 33/38 fiziksel içerikle eşleşti, 3 null ve 2 farklı içerik bulundu. `olm-attachment-audit.json` sonucu FIDELITY_GAP. Farklar çözülmeden OLM dönüştürme kabulü verilmeyecek; bu sonuç bütün SDK çıkarım yollarının başarısız olduğu anlamına gelmez.

- https://www.loc.gov/preservation/digital/formats/fdd/fdd000615.shtml
- https://www.nationalarchives.gov.uk/PRONOM/Format/proFormatSearch.aspx?id=3930&status=detailReport
- https://docs.aspose.com/email/net/reading-and-extracting-olm-messages/
- https://reference.aspose.com/email/net/aspose.email.storage.olm/olmstorage/enumeratemessages/
