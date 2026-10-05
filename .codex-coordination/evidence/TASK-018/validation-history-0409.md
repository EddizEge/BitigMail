# Gerçek yerel arşiv ve arama — kabul kaydı

TASK-018 ACTIVE, 2026-09-14. Üretim arşiv/arama akışı henüz kabul edilmedi. Bu belge ara kanıtları ve kalan kabul işlerini ayırır.

**Güncel durum (03:49): BACKEND_ACCEPTED / UI_RETEST_PENDING.** Kaynak kurtarma düzeltmesinden sonra gerçek partial-copy crash/resume203/203 (`api-8ce9f2e3dc`) geçti:72 iletinin2'si kalıcı yazılmışken doğrulanmış süreç HANDLE'ı üzerinden motor sonlandırıldı, yeni süreç aynı işi/arşivi72 tam fiziksel iletiyle bitirdi; kaynak ve ham SHA korunuyor. Güncel normal işlev API649/649 (`api-f952de49c9`), önceki idle restart/indeks onarımı83/83 ayrı kanıtlardır. Son normal motor testi434/434, arşiv65/65; test uyarısı temizliği takip ediliyor. Gerçek1660px/390px UI denemelerinin owner-adı/katalog-yenileme/gerçek-klasör/footer/mobil-boşluk düzeltmeleri yapılıyor ve yeniden kabul bekliyor. `TASK-018-root-ui-findings.md` somut UI bulgularını içeriyor. Task henüz DONE değildir; eski başarısız kanıtlar korunur.

## Root sorgu ve kapsam sınırı

`ArchiveSearchPolicy.cs` ile `ArchiveSearchPolicyCriticalTests.cs` root tarafından yazıldı. `.codex-coordination/task018-policy-tests/Task018.PolicyTests.csproj` üretimdeki aynı politika kaynaklarını doğrudan derler; çalışan LocalHost DLL kilitlerinden bağımsızdır. İlk koşu **38/38 PASS,0skip**. Tam ürün API/indeks testinin yerine geçmez.

- Gerçek Microsoft.Data.Sqlite10.0.12/FTS5: konu/gövde/ek alanı ayrımı, Türkçe ve İngilizce I eşlemesi, NFC birleşik karakterleri, literal OR/asterisk/kolon/çift tırnak girdileri ve fiziksel duplicate satırları. `IĞDIR/ığdır` eşleşir, ASCII `igdir` ile `ğ` ayrımı korunur.
- Seçimlerin immutable snapshotı, boş seçimin önizleme erişimi vermemesi, company/project/archive üçlüsünün birebir karşılaştırılması, yol gibi kullanılabilecek arşiv kimliği/duplicate/101 arşiv reddi.
- UTC+03 dahil gün aralığı, eksik MIME tarihinin açık davranışı; bozuk, ters ve UTC'ye çevrilemeyen tarihlerin kontrollü reddi; sayfa sınırları ve offset taşması.
- Kontrol karakterleri, bozuk Unicode, aşırı uzun veya çok terimli sorgu ve tanımsız alan sessizce temizlenmez; güvenli doğrulama hatası döner.

Önceki izole SQLite uyumluluk kapısı10/10PASS. Özgün sentetik12/4 verinin bağımsız Python arama beklentileri `.codex-coordination/evidence/TASK-018/expected-fixture-search.json` içinde kayıtlıdır; ürün sonucu değildir.

## Kalan kabul

İki şirket/en az üç arşiv gerçek ingest/search/preview, kaynak ve yönetilen raw SHA/decoded MIME karşılaştırması, aynı Message-ID/hash fiziksel kopyaları, kayıtlı sahiplik+seçili filtrelerin gerçek API'de uygulanması, HTML-only metin/uzak içerik engeli, eksik indeks/bozulma/şema/kesinti/reindex, kaynak kaldırıldıktan sonra managed raw ile yeniden indeksleme, idempotency/resume, tam regresyon ve gerçek masaüstü/telefon görünümü etkileşimleri. TASK017başlangıcı369backend/110frontend; yeni38root testi buna eklenecek.

Ardından kademeli ölçek/dayanıklılık deneyi yapılır. 100GB PST/OST veya gerçek Microsoft/Google kabulü bu aşamanın küçük sentetik testlerinden çıkarılamaz. Azure kaydı ve silmesiz Outlook pilotu için kullanıcı zamanlayıcı istemiyor; sonraki aşama notu ve sohbet hatırlatması korunur.

Root actual API hazırlığı: Temp/bitigmail-task017-qa/archive-api.py sözdizimi kontrolünden geçti; henüz ürün API'sinde çalıştırılmadı. EML12/MBOX12/edge4 ve tamamlanmış TASK017 mboxrd export12 kaynaklarını ayrı arşivlere alma, raw SHA fiziksel multiset, bağımsız sorgu beklentileri, kapsam/filtre önizleme negatifleri, literal sorgu ve geçersiz alan/tarih/sayfa, Unicode güvenli gövde tavanı ve yeniden indeksleme içerir. HTML-only/eksik tarih/text attachment/512KiB emoji sınırı edge fixture manifesti evidence/TASK-018/edge-fixture.json içinde; kaynaklar root-owned ve sentetiktir.

02:51 backend ara doğrulama: SOL54/54 archive ve423/423 tam Release testi PASS0skip bildirdi. Önceki gerçek test hang'i JobManager'ın kilit altında async worker sonucunu beklemesiyle doğrulandı ve start/resume/reindex worker çağrıları kilit dışına alınarak düzeltildi; hang sequence/dump korunuyor. Parametresiz eski MIME okuyucularına yeni limitin yayılması sözleşme sapması olarak SOL tarafından tutuldu; archive-specific bounded yol düzeltmesi devam ediyor. Bu sayı ve backend checkpoint dosyası ürünün tümünün tamamlandığı anlamına gelmez: root actual API, ham içerik/kapsam/bozulma ve UI kabulü hâlâ bekliyor.

03:06 root actual checkpoint: api-9313ac85f2 ilk343PASS ardından public ileti kimliği çakışmasıFAIL. SQL sayfalama24 fiziksel satırı getiriyor; public MessageId arşivler arasında aynı olduğundan ikinci arşivin önizlemesi birinciyi açıyor. Ek negatif22/26PASS; unknownfield200, değiştirilmiş managed raw200 ve bridgeMBOX container/record hash karışıklığı da actualFAIL. Ayrıntı .codex-coordination/results/TASK-018-root-api-findings.md. Bağımsız recovery diagnostic api-896468f3c3 gerçek TestingHost restart + missing/corrupt DB repair80/80PASS, sahiplik/katalog korunuyor, sağlıklı0fallback yok, raw/manifest hashleri aynı. Genel API kabulü başarısız olmaya devam ediyor; recovery ayrı alt testtir. 6175PID38988/picker SOL'a iade; hedefli Gemini düzeltmeleri ve frontend sürüyor.

03:20 root corrected actual API PASS: evidence/TASK-018/api-f420241575 full646/646, dört yeni arşiv40ileti, exact raw multiset ve filtreler, her birleşik kapsam satırının doğru public identity ile önizlenmesi, reindex sonrası ID stabilitesi, undefined field reddi, changed managed raw409, completedbridgeMBOX12 ve ayrı INTERNALDATE korunması. Final recovery api-18a523d2d7 83/83PASS: gerçek yeni süreç, missing/corruptDB rebuild, ID/owner/rawmanifest stabil. Önceki başarısız kanıtlar tutuldu. Backend actual delta kabul edildi; Gemini davranış regresyon testleri ve gerçek frontend/UI kabulü bekliyor. 6175PID55648/picker SOL'a iade edildi.
