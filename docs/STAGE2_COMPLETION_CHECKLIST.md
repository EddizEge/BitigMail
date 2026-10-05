# Aşama 2 kapanış kontrol listesi

2026-09-15: Kullanıcı, Aşama2 bitene kadar ara onay istemeden devam edilmesini istedi. Azure/gerçekOutlook Aşama3; zamanlayıcı yok. Yeni ajan açılmaz; mevcut SOL uygulama katmanı, root mimari/kabul. AGY arızası istisnası devam eder.

Kabul edilmiş: TASK022 gereksiz indirme; TASK023/024 disk önkontrolü; TASK025 hash tamponları/MIME yaşam süresi. Bunlar tek başına Aşama2 tamamlanması değildir.

- [x] TASK026: her hedef ileti son doğrulamasında tek yeni salt-okunur klasör açılışıyla birleşik kontrol; tüm epoch/hash/tarih/işaret/anahtar/tekillik kontrolleri korunur; gerçek ölçüm, hata ve regresyon kabulü.
- [x] TASK027: IMAP önizleme/aktarım/son kaynak doğrulamasında bütün klasör ham içeriklerini bellekte tutan üretim yollarını kaldır; seçili öğe bazlı sınırlandırılmış içerik işleme, sabit kaynak planı ve kaynak koruma kabulü.
- [x] TASK028: büyük iş listelerinde sınırlı render/sayfalama ve temel sıralı iş yürütme; tek aktif worker, bekleyen işlerin görünür durumu, idempotency, hata/iptal/yeniden açılış davranışı testleri. Gelişmiş toplu iş şablonları/öncelik/schedule Aşama7 kapsamıdır; temel kuyruk bunun yerine geçmez.
- [x] TASK029: güncel motorla eksik yaklaşık1GiB faz ölçümlerini ayrı/kalıcı dosyalara kaydet; zaman/süre/CPU/bellek ve kesinti checkpointleri; kaynak+çıktı raw/MIME/metadata bağımsız oracle; kayıptelemetri0sayılmaz. Tamamlanan faz sırf rapor hatasıyla tekrar çalıştırılmaz.
- [x] Root kapanış: ilgili tüm testler ve çalışır yerel önizleme; raporlar/roadmap güncel; gerçekte kalan kabul ölçütü varsa Aşama2 açık tutulur.

100GB/PST üretim lisansı/bozukdosya kurtarma/gerçek sağlayıcılar başka aşamalardadır. Ölçümler garantili azami kapasite veya evrensel hız iddiası değildir.

TASK026 kabul: backend462/462, hedefli27/27, gerçek12ileti/source12/4PASS. 12ileti finalfazı100ms altında olduğundan bellek örneği0; fazbelleği iddiası yok, Bu boşluk TASK029 büyük faz ölçümüyle kapatıldı.

2026-09-15 — AŞAMA 2 TAMAMLANDI. TASK028 backend469/469, frontend143/143, üç ekran boyutu ve gerçek iki OST->PST işi sıralı13/13PASS. TASK029 tek koşuda8192ileti/2464ek ve1070776928ham bayt; MBOX/EML/arşiv/arama, kaynak değişmezliği ve faztelemetrisiPASS.20fazCSV/45797örnek, ölçüm özetinde eksik kabul kalemi yok. Ayrıntılar .codex-coordination/results/TASK-028.md ve TASK-029.md. Raporlar/Arşive ekle tam geçmişi tek seferde okumaya devam eder; İş merkezi ve etkin iş sorguları sınırlıdır. Sıradaki Aşama3 Azure+kişiselOutlook silmesizpilot; zamanlayıcı yok.
