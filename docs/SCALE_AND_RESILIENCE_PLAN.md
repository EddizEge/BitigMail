# Kademeli ölçek ve dayanıklılık — onaylı sonraki aşama

2026-09-14, PLAN. TASK017 dosya/hesap köprüsü ve gerçek arşiv/arama kabulünden sonra ayrı görevle yürütülecek. Bu belge ölçüm sonucu veya 100 GB desteği değildir.

## Deney sırası

1. TASK017 içinde 72 fiziksel ileti/24 ek: Aspose kullanılmayan EML/MBOX yolunun 50 ileti sınırına yanlışlıkla takılmaması, aynı içeriğin fiziksel kopyalarının korunması.
2. Yaklaşık 128 MiB yapay veri ve en az 1.000 ileti: dosya→IMAP, IMAP→dosya, arşiv indeksleme/arama; metadata ve gövde hacminin ayrı etkisi. Tam üretim manifesti ve seed sabiti kaydedilir.
3. İkinci kademe kabul edilirse yaklaşık 1 GiB: tek büyük ileti yerine küçük/büyük ek, uzun gövde, çok klasör, Türkçe ad, duplicate Message-ID ve tarih sınırı karışımı. Önce diskte çıktı+staging+lab+indeks için alan hesaplanır; en az 10 GiB kullanıcı disk rezervi korunur. Kaynak ve çıktıların toplam bellek yerine akışla işlendiği ölçülür.

Tekil ileti ve indeksleme sınırları test başlamadan açık seçilir. Kaynağın kapasiteyi aşması görünür ön kontrol hatası olmalı; sessiz truncation veya kısmi başarı yok. Fiziksel duplicate kaydı içerik bazlı kaybolmamalı.

## Ölçülecekler

- Gerçek kaynak/çıktı boyutları, süre, ileti/saniye ve MiB/saniye; aynı boyut tanımı raporda belirtilir.
- Test motoru PID'sinin tepe çalışma belleği ve CPU süresi; ölçüm örnekleme aralığı. Sınırlı örnekleme ile kısa tepe değerlerinin kaçabileceği açıklanır.
- Arama ilk sorgu/tekrar sorgu süreleri, çok şirketli kapsam sayımları ve gerçek sonuç doğruluğu. İndeksli içerik ile kapsam dışında bırakılan öğeler ayrı sayılır.
- Kaynak/çıktı hash veya fiziksel multiset, decoded gövde/ek/CID ve IMAP metadata, kayıp/fazla ileti sayıları.
- Duraklat/iptal, ağ bağlantısı kaybı, disk yazım arızası ve sadece sahipliği doğrulanmış TestingHost sürecinin beklenmedik kapanması. Yeni süreçle aynı iş kimliğinde resume; sıfır mükerrer yazı. Kullanıcının uygulamaları/Outlook'u kapatılmaz.

## Kaynak ve ortam sınırları

2026-09-14 ön okumasında C: üzerinde yaklaşık 205 milyar byte boş alan bulundu; bu yalnız o anki okumadır, deney öncesi yeniden ölçülür. 100 GB kaynak+çıktı+staging+indeks için yeterli alan varsayılmaz. Büyük dosyalar çalışma alanında otomatik üretilmez; bu ilk kademe en fazla yaklaşık 1 GiB yapay kaynakla sınırlandırılır.

TASK017 kabulünden sonraki hazırlık okuması: C: boş alan158,69GiB; fiziksel RAM63,93GiB, o anda kullanılabilir34,67GiB. Bunlar kapasite kabulü değil anlık ortam değerleridir. Gerçek128MiB/1GiB deneyinden hemen önce alan ve bellek tekrar okunur; eski boş alan değeriyle tahsis yapılmaz.

PST/OST yaklaşık 100 GB ve bozuk dosya kurtarma ayrı üretim kabulüdür. Mevcut Aspose değerlendirme sürümünün sınırları kaldırılmaz; lisans ve temsilî sağlıklı/hasarlı veri eksikleri ayrı kaydedilir. Küçük/orta EML/IMAP ölçümü PST/OST 100 GB kanıtı diye sunulmaz.

Bu aşamanın sonunda Azure uygulama kaydı ve ayrı BitigMail-Test klasöründe silmesiz gerçek Outlook pilotuna dönülür. ROADMAP aşama notu bu bağımlılığı korur; kullanıcı isteğiyle zamanlayıcı kullanılmaz, yönetici aşama geçişinde sohbet içinde hatırlatır.

## ROOT acceptance — 2026-09-14 TASK019 128MiB
128MiB stage accepted. Independent root-audit-fe577ca27e passed3141/3141: single-folder MBOX job-280f867cb075 and EML job-8c0f78e599bb full source/IMAP/output MIME and raw comparisons, sidecar metadata, managed archive arc_ab5ec017b1d54a0c physical raw and paged search identities, original12/4 invariant. Separate scale-cont-753a4197/root-resilience-audit.json confirms actual IMAP physical MIME multisets400 crash +150 response-loss, no missing/extra copies. Crash observed30 then persisted57 is expected progress between observation and verified termination, not an exact30 crash claim. Lost-response target used legacy TASK017-scale-cont-753a4197-LostResp name: isolated synthetic target proven by job/evidence; namespace deviation retained, no rename/delete.
1GiB remains NOT RUN and gated. Root architecture decision: before1GiB replace whole-folder staged RawBytes retention with metadata/path entries, read/hash-validate/write one message at a time through unchanged BridgeMimeBytePolicy writer. Preserve durable journal/retry/create-only plans, cancellation/resume and output hashes. Measured797.8MiB sampled process WS is not allocation attribution or proof of1GiB OOM. Separate bounded implementation handoff required; current production code frozen. No100GB/provider acceptance. Azure and real personal Outlook remain manual next-stage reminder, no timer.

## ROOT acceptance — TASK020 1GiB / 2026-09-14
Integrity gate ACCEPTED, performance measurement QUALIFIED/PARTIAL. Independent root-audit-4bd5f02a8a24858/24858 PASS: fresh full8192 source/IMAP/MBOX/EML physical message raw/MIME multisets2464attachments, sidecar UID/date/flags/keywords, managedarchive raw8192 + pagedpublicIDs/decodedsubjects, original12/4 invariant. Jobs37141333f3b0(MBOX),2b10f9032e58(EML), archive a682b2f758ca4298. Root1gprovenance exact8x physicalhash/length accepted128source,1070776928bytes; no missing/extra. FullRelease445PASS preserved.
MBOX retention change stores paths/metadata and loads one message before hash/write; metadata still scales with item count. 128MiB actual staging interruption/resume accepted; no claim for all assembly/publication crash windows. Large import completed8192 after parent-timeout recovery; phase timestamps/counters must not be treated as uninterrupted end-to-end timing. MBOX elapsed1194.3848s, EML765.69998s are harness export windows; archive97.858285s is durablejob window. Previews are separate and exceeded initial180sec harness deadline. Initial import1800sec value was hardcoded, not measured. 1GiB MBOX-phase memory telemetry unavailable/overwritten; no0MiB, boundedprocess-memory or complete1GiB performance acceptance claim.
Root repaired only test harness/oracle after repeatedAGY workspace-tool failures: completed import and archive checkpoint restoration, explicitpreviewtimeouts, rawSubjecttoken/fullID oracle, globalSQLite read-onlyscopedchecks, datetime shadow. Failed evidence retained. Production changes limited to bounded MBOX staging andtests.
Next work: performance/preview and finalverification progress visibility, reliable perphase telemetry; Azure app registration and personal Outlook pilot in isolatedtestfolder with nooriginalmaildeletion is a manual milestone reminder, NO TIMER. LocalDovecot1GiB success does not establish provider,100GBPST/OST or repair support.
