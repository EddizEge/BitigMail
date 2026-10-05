# BitigMail — 20 Eylül 2026 aktif geliştirme kaydı

**Durum: AKTİF.** Kullanıcı, 19 Eylül'de kaldığımız yerden 9. aşama dahil devam edilmesini istedi. 15 Eylül'deki kota duraklaması sona erdi. Kalan kota %5'e yaklaşırsa toparlayıp durma kuralı korunuyor; son ölçümde %92 kalmıştı. Azure kurulumu ve gerçek Outlook hesabında silmeden aktarım testi 10. aşamada. Zamanlayıcı yok.

## Kabul edilmiş son nokta

1–2. aşamalar tamamlandı. 3. aşamanın yerel servis altyapısı kabul edildi. 4. aşamada TASK032 EMLX ve TASK033 PST/OST/OLM → EML dönüşümü, belirtilen sınırlamalarla kabul edildi. TASK033 son motor kontrolü 531/531, son kritik testleri 13/13, arayüz kontrolü 145/145 geçti. OLM tarih anlamı halen doğrulanmış değil; özgün XML ve tarih metinleri korunuyor. Tüm alanların birebir korunması veya lisanslı çıktı kabulü verilmedi.

## Aktif iş

**TASK037 — Aşama 7 gelişmiş yönetim.** Gelişmiş filtrelerin akışlara entegrasyonu, seçili arama sonuçlarından iş oluşturma, klasör eşleme, şablonlar, bekleyen iş önceliği ve açık yinelenme politikası. SOL'a gönderildi; henüz kabul edilmedi.

**Son kabul:** Aşama6 yerel kurtarma kabulü tamamlandı. Root, SOL'un dört kapasite kesintisinden sonra kritik entegrasyonu ve kabulü tamamladı. Motor639/639, arayüz148/148, gerçek tarayıcı akışı üç boyutta3/3. Kontrollü blok hasarında4ileti ve1başarısız sınır; içerik/tarih/ek baytları doğrulandı. Tamamen okunamayan kaynak ayrıca test edildi. Bütün kaynak özetleri sabit. Ayrıntı: [kurtarma kabulü](DAMAGED_STORE_RECOVERY_VALIDATION.md).

Aşama5 yerel kabulü tamamlandı; ticari SDK lisansı ve gerçek büyük PST/OST testleri açık. Aşama4 yerel format matrisi kabul edildi. Aşama9 tamamlanmadı.

## Sonraki işler için hazır çekirdekler

- **TASK034B:** `NormalizedSourceQualificationReader`, 19/19 test. Gerçek OLM'den 22 çıktı ve tek EML seçimi dahil. Kayıt, hash, yol, eksik/fazla öğe ve yan dosya doğrulaması; tarih kısıtlarının korunması. 034B ile aktarım, arşiv, PST ve önizleme planlarına bağlandı; yerel kabul kapandı.
- **TASK036:** `OwnedWorkerProcessRunner`, 6/6 gerçek Windows süreç testi. Yalnız kendi başlattığı süreci durdurma, iptal, zaman aşımı ve sınırlı bellekle çıktı okuma. Henüz hasarlı dosya kurtarma işçisine bağlanmadı.
- **TASK037:** `AdvancedMailFilter`, 13/13 test. Sürümlü VE/VEYA kuralları, UTC tarihleri, Unicode metinler, bilinmeyen metadata ve değişmez filtre özeti. Henüz mevcut kaynaklara ve ekranlara bağlanmadı.

Bunlar tamamlanmış aşamalar değildir. Kullanım sözleşmeleri ilgili görev dosyalarına eklendi.

## Kalan sıra ve dış bağımlılıklar

036 kurtarma → 037 gelişmiş yönetim → 038 yerel yetkilendirme → 039 yedek/denetim → 040 Windows kabuğu → 041 kurulum ve kabul.

Üretim SDK lisansı yok. Kullanıcı güvenilir kod imzalama sertifikası olmadığını doğruladı; iç test paketi imzasız hazırlanacak. WebView2 kurulu; Windows SDK içinde imzalama ve paketleme araçları bulundu. Temiz Windows sanal makine envanteri `VirtualizationException` nedeniyle doğrulanamadı. Geliştirme bilgisayarındaki kontroller temiz Windows kabulü yerine geçmez. 9. aşama henüz tamamlanmadı.

## Koordinasyon

Mevcut SOL görevi: `01a0955d-110b-7612-a8d5-ed1eef6a4f0c`. Doğrudan uygulama istisnası etkin; yeni ajan veya Antigravity arayüzü kullanılmıyor. Astra kritik mimari, veri bütünlüğü ve kabulü; SOL görevlerin uygulanması ve testlerini yürütüyor. Ayrıntılı sonuçlar `.codex-coordination/results` altında. Aktif talimat, `EXECUTION_OVERRIDE.md` dosyasındaki son yeniden başlatma kaydıdır.

Önizleme, 19 Eylül'de boş portlardan güncel kaynaklarla başlatıldı. Sonraki özellikler ayrı test motorunda doğrulanıyor.


## Güncel durum — 20 Eylül
4. aşamanın yerel kabulü tamamlandı (034B son kapsam kontrolü 47/47; önceki genel 584/584). TASK035 ilk uygulaması 588/588 motor ve 147/147 arayüz testini geçti. Kabul incelemesinde korumalı lisans seçimi, anlaşılır durum aktarımı ve bağımsız ölçek doğrulama kapsamı için tamamlama istendi; 5. aşama henüz kapanmadı. Sonraki sıra 036–041. Kullanıcı imzalama sertifikası olmadığını doğruladı; iç test paketi imzasız olacak, ticari imza kabulü açık. Temiz Windows ortamı yanıtı bekleniyor.

TASK035 son lisans deposu koruması9/9 geçti, yerel kabul kapandı. Aktif görev artık TASK036: hasarlı PST/OST için sahip olunan ayrı işçi ve doğrulanmış yeni çıktı. Azure/gerçekOutlook Aşama10 olarak kapsam dışında kalıyor.

## Aşama6 kabul kapıları — son inceleme
Worker/API/UI ilk uygulama ve13odaktest+148arayüztesti mevcut, fakat036kabulü henüzverilmedi. Ortak kalıcıJobManagerkuyruğu, donmuşkaynakhash/sahiplik, uygulamarestartındakorunan belirsizişçikilidi, ana süreç açılışlisansbağlamının özelkanallaalt süreceiletimi, klasör/öğe/nonmail/qualificationmetadata ve güvenliyayın/cleanup tamamlanmalı. Başka runtimeprofilindelisansarama ve yapılandırmahatasınısessizyutma reddedildi. Belirsizişçidurumundastaging silinmez. Oracle yalnız sağlıklısayım+okunamayantruncationolmamalı: bilinenklasör/tarih/ekbytehash ve hasardanenaz1doğrulanmışkısmiöğe gerekir.
Root sonucdoğrulayıcıhardening11/11geçti. Stage8AuthenticatedSessionRegistryçekirdeği8/8hazır; mevcutanonimSessionManagerhenüzdeğişmedi. Stage9PackagePayloadValidator16/16hazır; kurulumhenüzdeğil.
SOL hizmeti iki kapasitekesintisinden sonra aynımodel/saklıkod üzerindenyenidenbaşlatıldı; değişikliklerkorundu. Sonkotakontrolü87%kalan,5%rezervkuralısürüyor. YeniagentveyaAntigravityUIyok.

## Root kritik düzeltmeler sonrası son durum
SOL üçüncü kapasite hatası sonrası root kritik036lifecycle düzeltmelerini doğrudan tamamladı. OwnedWorkerProcessRunner stdin aktarımını da deadline/kendiçocuğunukapatma kapsamına aldı; startup kaynaklisans aynı tekboundedokumadanhemanaSDKhemRecoverySdkBootstrap için snapshotlandı. Child artık stdin özelkanalınıokuyor, başka profil/çevre değişkenindeyeni lisansaramıyor. RecoveryService kalıcıJobManager.GetJob üzerindenviewoluşturuyor, kuyruktaki işiiptalediyor, hedefdiskte özelstagingkullanıyor, failures/unresolved için özyinelemelisilmeyapmıyor. Kaynak/çıktıyolreparse, kapasite ve hashkontrolü mevcut. LocalJobRecord explicit RecoveryOutcome/OriginalTotal/FailedBoundaryCount/WorkerUnresolved alanları ve klonlama eklendi; recoveryconverting kaydındayenidenaçılış muhafazakârunresolvedkilit yaratır. Kaynakownerobjesi derinkopyalanır. Normal6174değişmedi.
Kanıt: root036-bootstrap-deadline17/17, root036-lifecycle-integrated19/19, root036-durable-lifecycle-final4/4. Bunlar036genelkabulüdeğil.
SOL tekrar devrede: kalanworkerklasöradı/öğekimliği/nonmail/qualificationmetadata, downstreamrecognition, gerçekhasaroracle veAPI/UI/raporentegrasyonunu tamamlıyor. Rootcore dosyalarınıyenidenyazmamasıistendi. Sonwaitcursor6211d603-b76c-4419-a3de-656568842a7f:11; turn01a0bbbf-c01e-79d0-8b98-1b0283c50636 active.

Root Stage7 API capability guard: IMAP/Bridge/POP and Archive request DTOs now reject unmapped JSON properties. Unsupported advanced filters or misspelled selection policies cannot silently become unfiltered operations. Focused request binding tests8/8 PASS (root037-request-capabilities.trx). This does not implement those route adapters; Stage7 remains active. Root Stage9 strict package manifest reader13/13 is ready, installer not yet implemented.

IMAP→IMAP gelişmiş filtre motoru eklendi;44/44odak kontrol geçti. Tam MIME içeriğiyle önizleme/çalıştırma tutarlılığı, HTML görünür metni, bilinmeyen tarih, değiştirilmiş filtre ve hedefe gerçekten yalnız seçilen test iletisinin yazılması doğrulandı. Harici Outlook hesabı kullanılmadı. Ekran entegrasyonu ve Aşama7 genel kabulü açık.

EML/mboxrd↔IMAP gelişmiş filtre motorları tamamlandı. Genel ilgili regresyon122/122 ve yeni3gerçekdosya/aktarımı testi geçti;300bin karakterlik gövdenin sonundaki eşleşme, yalnızseçilenileti, EML/mboxçıktısında birebirbayt ve tarih kısıtları doğrulandı. Ekranentegrasyonu halenbekliyor.

TASK037B bounded actual selected-archive export accepted with follow-on037C hardening recorded:683/683 backend (before latestrootBridge tests),148/148frontend,1realbrowserflow passed. ActualverifiedEMLpublication andqualificationmanifest propagation nowimplemented. Root requested extra multiarchive sameitemID filename/mapping collision coverage and freezing fullsource metadata/qualification+queryfingerprint atdispatch as037C boundary work. Stage7notyetclosed. TASK037C active: Turkishfilterbuilder,templates/priority,fullrawarchiveAST andremaininglocalMIME policies. RootBridge compilerduplicateusing warningsalreadyremoved.
