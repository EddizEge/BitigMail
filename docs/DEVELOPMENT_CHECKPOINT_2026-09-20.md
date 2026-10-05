# BitigMail — 20 Eylül 2026 aktif geliştirme kaydı

**AKTİF: 9. aşama dahil çalışmaya devam ediliyor.** Azure kurulumu ve gerçek Outlook hesabında silmeden aktarım testi10. aşamada, bu çalışmanın dışında. Zamanlayıcı yok. Kalan kota%5'e yaklaşınca toparlayıp durma kuralı korunuyor; son ölçüm%84kalan.

## Tamamlanan yerel kabul

| Aşama | Durum ve kanıt |
|---|---|
|1–2|Önceden tamamlandı: iş deneyimi, temel kuyruk, yaklaşık1GiB laboratuvar kabulü.100GB kabulü değildir.|
|3|Servis/OAuth altyapısı yerel kabul; gerçek sağlayıcı pilotu bekliyor.|
|4|EMLX, PST/OST/OLM ve POP→EML; sonraki işlemlerde kaynak uyarıları/tarih kısıtları korunuyor. TASK033–034B.|
|5|SDK başlangıç/lisans ve ölçek altyapısı:607/607motor,147/147arayüz; son lisansdeposu9/9. Ticari lisans ve10–100GB gerçek dosya kabulü açık.|
|6|Hasarlı dosya kurtarma:639/639motor,148/148arayüz,3/3gerçek tarayıcı. Kontrollü hasarda4doğrulanmışileti+1başarısızsınır; bilinmeyen toplam açıkça belirtilir. Kaynaklar değişmedi.|

## 7. aşama — yerel kabul tamamlandı

- TASK037B: arşiv arama sonuçlarından seçili gerçek EML işi; klasör eşleme, açık yinelenme politikası, aynı diskte doğrulama sonrası yayın, kaynak tarih/uyarı/kısmi durumunun yeniden kullanılabilir çıktıda korunması.683/683motor,148/148arayüz,1/1gerçek tarayıcı akışı. Sonraki köşe kontrolleri: çok arşivde aynı öğe kimliği/klasör; kuyrukta metadata/qualification ve sorgu değişimi.
- Root: IMAP→IMAP gelişmiş filtre API/plan/çalıştırma/rapor entegrasyonu44/44kontrolü geçti. EML/mboxrd↔IMAP aynı filtreyi kullanıyor; ilgili regresyon122/122ve3gerçekçıktı testi geçti. Tam MIME gövdesi, HTML görünür metni, bilinmeyen metadata ve değişmez filtre özeti kullanılıyor. Harici hesap testi değildir.
- SOL: teknik JSON girişi yerine Türkçe alan/koşul/değer ve VE/VEYA düzenleyicisi, IMAP/Bridge ekranları, şablon ve bekleyen iş önceliği; tam ham içerik üzerinden arşiv gelişmiş arama; kalan yerel MIME eşleme/yinelenme işleri.
- Desteklenmeyen istek alanları sessizce yok sayılmaz. POP ve Outlook/Apple dosyaları gerektiğinde önce doğrulanmış EML'e normalleştirilir; tarih kısıtları korunur.
- 7. aşama kabul edildi. Ayrıntı: [kabul kaydı](STAGE7_ADVANCED_MANAGEMENT_ACCEPTANCE.md).

## Aktif ve sıradaki işler

**Aktif: 8. aşama, TASK038.** Onaylanan güvenlik sözleşmesiyle mevcut SOL görevi çalışıyor.

8. aşama: TASK038 gerçek yerel kullanıcı/rol/şirket yetkileri, ardından TASK039 denetim/yedek/yeni arşive geri yükleme/silmeden saklama önizlemesi/müşteri raporu.

9. aşama: TASK040 Windows kabuğu, TASK041 gerçek kurulum/güncelleme/geri dönüş/kaldırma ve dağıtılabilir paket. Hazırlanan çekirdekler henüz kurulum ürünü değildir: oturum8/8, ilk-kurulum kanıtı7/7, paket dosya doğrulama19/19, katı paket JSON okuyucu13/13.

**Dış kabul sınırları:** Aspose ticari lisansı yok. Kullanıcı kod imzalama sertifikası olmadığını doğruladı; iç test paketi imzasız hazırlanacak. Temiz Windows ortamı doğrulanmadı; geliştirme bilgisayarı temiz makine kabulü sayılmaz. Bu bağımlılıklar bağımsız geliştirmeyi durdurmaz, tamamlandı diye işaretlenmez.

## Çalışma ortamı

Mevcut SOL görevi `01a0955d-110b-7612-a8d5-ed1eef6a4f0c`; doğrudan uygulama istisnası etkin. Yeni ajan/Antigravity arayüzü yok. Astra kritik mimari/veri bütünlüğü ve kabulü yönetiyor.

Normal önizleme5173/6174korundu;6174motoru son yeni API'leri henüz içermeyebilir. Yeni özellikler ayrı6175TestingHost ile doğrulanıp ilgili sahipli test süreci kapatılıyor. SDK yolu `.tools/dotnet/dotnet.exe`; ayrı derleme dizinleri kullanılıyor. Kişisel kimlik bilgisi dosyası okunmuyor.

Kanıtlar `.codex-coordination/results` ve `.codex-coordination/evidence` altında. Önceki bu-günlük ayrıntılı zaman çizelgesi `.codex-coordination/evidence/checkpoint-2026-09-20-before-current-summary.md` içinde korundu. Tam sıra [yol haritasında](FULL_RELEASE_ROADMAP.md).

Son root kontrolleri: IMAP/Bridge gelişmiş aktarım125/125geçti (root037-transfer-advanced-final.trx); kota%83kalan. Masaüstü gezinme güvenlik kararları30/30hazır (root040-navigation-policy.trx), henüz gerçekWebViewkabuğuna bağlanmadı.

Root MIME→PST eşleme/tekilleştirme motoru tamamlandı:75/75ilgilitest ve16/16istek/politika sınırıkontrolü geçti. GerçekPSTyenidenaçılarak 3/1/2politika sayımları, klasörler veekler doğrulandı; kuyruktaseçim değişiklikleri çıktıyı değiştirmedi. SOL037Cekranentegrasyonu sürüyor. TASK038güvenliksözleşmesi root tarafındanonaylandı;037kabulündenönce038uygulamasınabaşlanmayacak.

## Son durum — Aşama 8 aktif

Aşama7 yerel kabulü tamamlandı: genel725/725motor,151/151arayüz, gerçek tarayıcı akışları; son dört arşiv bütünlüğü testi ayrıca başarılı. Ayrıntı STAGE7_ADVANCED_MANAGEMENT_ACCEPTANCE.md içinde. TASK038 mevcut SOL görevine gönderildi; onaylanan güvenlik sözleşmesiyle gerçek kullanıcı/rol/şirket yetkilendirmesi uygulanıyor. Bundan önceki TASK037C aktif ifadeleri geçmiş durumdur. Sonraki sıra039→040→041;9. aşama henüz hazır değil.

## Stage 8 account security — current root verification
IdentityCatalog hardened with strict bounded catalog loading, duplicate/missing-field rejection, single-snapshot permission decisions, cross-process write lease, durable atomic save, immutable grant copies and bounded password operations. UpdateUserAsync increments security version on actual changes, protects the last active admin, rejects stale concurrent changes and invalidates old credentials/sessions. Focused catalog/session/bootstrap/lease tests: 38/38 PASS, `.codex-coordination/evidence/STAGE8/root038-catalog-security-final.trx`. Production route/ownership/UI integration remains TASK038 in progress; this is not full Stage8 acceptance.
Audit chain consistency core for TASK039: 10/10 PASS. Expected head detects truncation; privileged rewrite of both chain and local head remains outside integrity guarantee. Storage/audit UI integration pending.

Stage8 root dispatch gate: 11/11 targeted tests PASS (`root038-dispatch-auth-final.trx`). Initial idle and queued launch both revalidate actor security version and frozen required scopes. Revocation/corrupt catalog prevents worker invocation and persists visible failure. Failed security-refusal persistence latches further dispatch, including after restart. Job-derived archive sources retain their stored scopes; existing idempotent job returns enforce actor and scope. Source-selected archive service and route integration remain SOL TASK038 work. Login throttle4/4 and admin races2/2 also passed; no full-stage completion claim yet.


## Güncel devam noktası: TASK039 ACTIVE
TASK038 kullanıcı/rol/şirket ve oturum sahipliği kabul edildi. Son genel motor808/808, arayüz151/151, typecheck/lint/build PASS; root gerçekTestingHost ile4UI/HTTPtesti (2responsiveHTTP tekrarı bilinçli atlandı). İkiarşiv normal ingest API ile oluşturuldu; gerçekaramada mixedscope ve archiveIDscopeikamesi404, operatorlogout tümekranlarda geçerli. Kanıt root038-production-http-and-ui.json. Şimdi TASK039 audit/backup/restore/retentionpreview/report uygulanıyor. Root AuditChain10/10 veZIPpreflight/payload19/19 hazır. Stage8 bütünü veStage9 henüz tamamlanmadı.

PC shutdown resume: user resumed through9. TASK039 active; existing SOL implementation resumed. Root fresh restore publication5/5 PASS; existing target preservation and foreign staging/identity rejection verified. Quota79% remaining. Stage10 excluded.

Fresh publication final6/6 PASS, including real Windows junction rejection. Receipt contract dispatched to SOL: durable prepublication IDs+manifest hashes, explicit state, idempotent same-ID index repair, fresh authorization, partial completion visible. TASK039 still active.

Root039 verification: VerifiedStreamCopy6/6; AuditLogStoreBoundary6/6; RestoreCrashBoundary4/4 PASS. Restore faults injected before/after publication and before/after indexing followed by two new service/catalog instances repairing same ID; original/restored bytes exact,2total archives. This is exception fault injection, not OS kill proof; catch writes failed receipt. TASK039 not yet accepted; SOL implementing UI/API/retention/report and remaining negative matrix.

Actual process interruption probe PASS: Environment.Exit73 inside AfterPublish skips catch/finally before Published receipt/index update; two separate fresh processes repair same ID, original/restored bytes exact,2archives. Evidence STAGE8/root039-abrupt-process-repair.json; source .codex-coordination/probes/RestoreCrashProbe. This verifies abrupt process exit, not physical power-loss storage durability.

TASK039 acceptance hold: SOL initial resultDONE not accepted pending statednegativechecks and realUI execution. Current buttonvisibility-only Playwright insufficient. Need multi-companybackup/restore+malicious/tampered/null/duplicate/forbiddenrole/scope+retention/reportnondelete/privacy. CustomerDeliveryReport initial projection misses source/target/filter/date/attachment summary; hash omitted displayed qualificationfields; rawwarnings need fixed publicsafe mapping. Retention legalhold unsupported must be explicit. Backup needs freshauthorization at finalpublication. Root sent bounded corrections; SOL stillactive. Do not dispatch040 until039accepted.

039 second claimedDONE not accepted: actualretentionUI passed, but realUIbackup/restore,2companyroundtrip,malicious/null/duplicatepackage,HTTProle/scopenegatives,andreportprivacy/hash tests missing. Root explicitly resumed idleSOL for these5bounded checks. Currentstate039IN_PROGRESS,040WAIT.

Root GovernanceReportBoundary2/2 PASS: privatewarnings/path/token/exception/name omitted, visibledateblock and attachmentcount each changeevidencehash, retentioncandidate leavesraw+manifestbyteexact. SOL subsequent turns repeatedlyidle without boundedpackage tests; root requested explicit executionfailure diagnosis. Quota78% remains, notreserve threshold.039notaccepted.

Root039 critical late fixes: RestorePackageBoundary first found duplicateJSONaccepted and nullarchivesNRE; fixed strict top-level duplicate/null/schema/ID/pathrelationship checks. Addedsecondarchivecorruption test foundfirstarchivepublishedbeforelatercorruption. Restore now verifies/extractsALLarchives to staging beforeanypublication. Durable receipt PayloadValidated=false untilentirepackageverified; Repair rejectsfalse preventingpartialstagingbypass. Fresh auth nowinsidepublicationlease andafterBeforeIndexhook. Combinedpackage6+crash4=10/10 PASS root039-package-crash-final.trx. Root owns/released ArchiveRestoreService. Prior full832predatesthesefixes; finalregressionpending.039UIbackuprestore+HTTPoperator/foreignscope stillpending; SOL silent-idle recurring, dispatchednarrowUIremaining.

21September userresumed through9 andrequestedcompiledappopenedaftercompletion. Quota76%remain. SOL039UI/HTTPtest nowactivelyimplemented, acceptancepending. Root040prep: DesktopInstanceLease4/4inclseparateprocess; ForDesktoporigin8new+42existing=50/50PASS. Noactualdesktoppackageyet;040WAITuntil039accepted.

21September TASK040 ACTIVE. Root critical cores added: DesktopInstanceLease4/4; desktoporigin8+legacy42=50/50; JobManagerDesktopLifecycle3+dispatch6=9/9; DesktopReadyProof6+DesktopOperationGate2=8/8; DesktopProtocolReader3/3. CoreevidenceSTAGE9/root040*.trx. JobManagerdrain blocksnew/queued jobs, persistsqueue and waitsactive; restartedqueued jobs interrupted+NeverStartedQueued. Gate countsallinflightHTTPandrejectsnewrequestsafterdrain. Readiness HMACbindsownedchildproof+port, preventsforeignportHTTP200race. Boundedprotocolreader handlesEOF/cancel. SOLownsDesktop/Program.cs+LocalHostProgram integration, rootownscoresnowreleased. Corrections dispatched: keepstdinopen; EOFsameassafeEXIT; continuousstdoutpump; no5secondkillactivejobs; guardedtopframe/exactoriginproofinjection; isolateddesktopprofile vslegacyproduction6174; explicitWebViewuserdata; asyncstartupcatch; visiblefailedshutdownstate. NativeWebViewsmoke/screenshotrequested.040notyetaccepted,041WAIT. Userrequiresfinalcompiledappopenedforrevisionafter9.

Root040 actualcompiledproductionhost boundary PASS9checks, isolatedtempprofile, outsidecwd, privateproofstdin+HMACready, sameoriginstatic/CSP/nosniff, wronghost/originrejection, nativeproofadminsetup, API404notSPA, duplicateprofiledenied, parentEOFexit0, freshprocessreopen/EXIT0. EvidenceSTAGE9/root040-native-host-boundary.json. Initialprobe attempts usedoldcustomreadyprefix build; freshcurrentcompilePASS. Normal6174notmodified. Finallease-release requestedApplicationStopped ratherthanStopping; shellWebViewsmoke+041stillpending.
