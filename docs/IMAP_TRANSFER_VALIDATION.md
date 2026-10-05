# TASK-014 — Doğrulama kaydı

Durum: DONE; gerçek API, bağımsız posta oracle'ı, arayüz, resume ve tam regresyon kabul edildi.

## Gerçek aktarım API kabulü

13 Eylül 2026: ASTRA bağımsız gerçek HTTP ve Python IMAP/MIME oracle kontrolleri 151/151 PASS. Dovecot üzerinde filtreli İstanbul aktarımı 3 ileti/1 ek, tam aktarım 12 ileti/4 ek ve ters yön aktarımı 3 ileti/1 ek olarak doğrulandı. Bağımsız karşılaştırmada ham bayt/header/decoded MIME part/tarih/flag multiset için eksik 0, fazla 0 ve kaynak değişmezliği PASS. Boş seçimli engellenmiş önizlemenin başlatılamaması, dondurulmuş önizlemenin yeniden yüklenmesi, scope reddi, exact-plan idempotency ve tamamlanmış işin tekrar resume edilmemesi de gerçek wire üzerinden geçti.

İlk kanıt: `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task014-qa/transfer-a6bb7fe5f715/acceptance.json`. Final gerçek API kabulü `transfer-ec286119519a` altında 155/155 PASS oldu ve rapor tarihleri/kaynak toplamı kontrollerini de kapsadı. Final regresyon: backend 229/229; frontend 65/65; typecheck/lint/build 0 hata.

Final Playwright kabulü 1660px ve 390px görünümde PASS: dondurulmuş müşteri/proje adları, tarih filtreleri, rapor indirme ve reload doğrulandı. Resume kabulünde eksik journal 409 verdi; dayanıklı kayıp-yanıt snapshot'ı 2 Verified + 1 AppendIntent durumundan aynı 3 ileti/1 ek sonucuna 0 yeni kopyayla tamamlandı. Kanıt dizinleri: `jobs800ad6c5ba14`, `d136206adf1a`, `resume-c49b3b20d4`.

Normal yeniden derlenmiş Debug host 6174 üzerinde eski güvenlik sınırları 20/20 ve yeni IMAP host sınırları 4/4 PASS: test-only plaintext normal hostta reddedildi, session zorunlu kaldı ve TestingHost hesap deposu normal hosttan ayrıldı. Güncel LAN 5176 build'i desktop 1660px ve mobile 390px yükleme/ayrıntılar akışında 2/2 PASS. Fiziksel telefondan erişim kullanıcı tarafından doğrulanmadı.

## Gerçek hesap API/UI kabulü

13 Eylül 2026: ASTRA bağımsız HTTP kontrolleri 120/120 PASS. Dovecot kaynak/hedef hesaplarına gerçek bağlantı; yanlış parola güvenli 400; klasör sayıları INBOX 5, Gönderilenler 3, Projeler/İstanbul 4; yabancı şirket kapsamı reddi; eksik ve eski expectedVersion reddi; metadata güncellemesinden sonra parola korunması; yalnız yerel hesap kaydı silme; disk üzerinde DPAPI ciphertext ve public yanıtlarda secret bulunmaması. İlk kontrol scriptindeki yanlış parola için 200 beklentisi gerçek 400 sözleşmesine düzeltildi; ürün hatası değildir.

Gerçek Playwright akışı 1660 ve 390 genişliklerde 2/2 PASS: hesap listesi, bağlantı düğmesi, gerçek klasör sayıları, iptalden sonra boş parola alanı, TLS formunda yalnız ssl/starttls. JavaScript exception ve sayfa taşması yok. Mobil ekran görselinde klasör sayısı sütunu yatayda dışarıda kaldığı için okunabilirlik düzeltmesi istendi; bu kontrol bütün mobil arayüzün kabulü değildir. Aktarım worker/journal ve kalıcı rapor kabulü hâlâ bekliyor.

Kanıt: `Temp/bitigmail-task014-qa/api-accounts-report.json`, `api-accounts-context.json` (yalnız public bilgiler), `ui-accounts-report.json`, `accounts-*.png`. Root test servisi PID44348 doğrulanıp durduruldu; 6175 tekrar SOL kullanımında.

## ASTRA bağımsız hazırlık — tarihsel geliştirme kayıtları

- Root kritik journal uygulaması: `ImapTransferJournal.cs` kalıcı dosyadan ayrık snapshot okur; bozuk kayıt yokmuş gibi yorumlanmaz; güvenli kayıt kimlikleri dizin dışına çıkamaz; plan ve mevcut journal baştan yazılarak sıfırlanamaz; Flush(true) ve atomik replace tamamlanmadan geçiş görünmez. `ImapJournalCriticalTests` 8/8 PASS (aynı kaynakları linkleyen izole test projesi). Normal backend test koşusu yeni aktarım adaptörünün derleme hataları nedeniyle henüz bu checkpoint'te çalışmadı; bütünleşik kabul değildir.
- Journal ve lifecycle kritik testleri; ilk/final disk hatası, restart idempotency, belirsiz token ve değiştirilmiş hedef kanıtı dahil final backend regresyonunda geçti.

- Parola/TLS sınırı: ImapSecurityTests 30/30 PASS (Release, 13 Eylül 2026). DPAPI CurrentUser farklı örnekle yeniden açma, yanlış hesap entropy ve bozulmuş şifreli verinin reddi; üretimde plaintext/downgrade reddi; kontrol karakterleri/scope sınırı. İlk GreenMail deneyinde istisna 127.0.0.1:4143 idi; Dovecot geçişinden sonra güncel istisna yalnız 127.0.0.1:5143 oldu ve 30 test tekrar geçti. Credential protector için yeni NuGet paketi gerekmedi (WindowsDesktop runtime).
- Python stdlib ile kaynak read-only IMAP BODY.PEEK snapshot: 12 fiziksel ileti, 4 ek. Sunucunun klasör ayıracı `.`, klasörler INBOX, Gönderilenler, Projeler.İstanbul.
- Özgün EML fixture'ları ile gerçek sunucu bayt karşılaştırması PASS. Yalnız IMAP yüklemesinin açık CRLF kanonikleştirmesi uygulandı; trim/boş satır silme/MIME yeniden yazımı yok. Eksik 0, fazla 0; bütün 12 fixture'ın özgün manifest hashleri aynı.
- Kanıt dizini: `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task014-qa/`. `source-before.json`, `source-fixture-report.json`, `imap_oracle.py` ve `check_fixture_source.py`. Snapshot raporları secret içermez.

## Gate A — gerçek aktarım

Root public MailKit APPEND deneyi Dovecot üzerinde PASS: 12/12 kaynak FETCH ile gerçek gönderim serialization baytı eşit (DOS + EnsureNewLine=true, HiddenHeaders boş). Hedef hash, INTERNALDATE, sistem flag'leri ve özel keyword'ler korundu. Her token tek öğe buluyor, bağlantı kapatılıp açılınca aynı UID/UIDVALIDITY/hash doğrulanıyor; kaynak aynı iş boyunca değişmedi.

Kalıcı deney: `lab/task014/harness/Program.cs`, çalıştırma `dotnet run ... -- --dovecot`. Önceki Gemini keşif kaynağı `.cs.txt` olarak korunur. Üretim API'si ve worker henüz bu deneyin yerine geçmez.

Kanıt: `Temp/bitigmail-task014-qa/gate-a/run-2ec7880655b446f498eb78d1cf2a83cc/report.json`. Bundan bağımsız Python stdlib BODY.PEEK ve MIME parser kontrolü `gate-a-independent-report.json` PASS: tam bayt/header/decoded part/INTERNALDATE multiset eksik 0, fazla 0, kaynak flag'leri korundu; 12 ileti/4 ek. `dovecot-source-fixture-report.json` özgün fixture karşılaştırması PASS.

İlk GreenMail deneyi tarih farkı nedeniyle FAIL olarak korunur: öğlen 12:00 UTC APPEND sonrası 00:00 UTC oldu. Bu [GreenMail issue #211](https://github.com/greenmail-mail-test/greenmail/issues/211) ile örtüşür. Eşitlik toleransı veya uygulamada tarih kaydırma eklenmedi. Eski konteyner/veriler korundu, ikinci Dovecot 2.3.19.1 laboratuvarı kuruldu. Yalnız `127.0.0.1:5143` üzerinde, ayrı kalıcı mail volume ile çalışır. Test politikası artık yalnız bu adreste plaintext izni verir; güvenlik testleri değişiklik sonrası 30/30 tekrar PASS.

İlk GreenMail kaynak snapshot'ı hazırlık sırasındaki sunucu yeniden başlatmasından önceydi (UIDVALIDITY farklı); sonraki işlerin değişmez kaynak kanıtı olarak kullanılmaz. Dovecot baseline ayrı kaydedildi.

Lite → MimeKit 4.17 geçişi sonrası eski MIME testleri 30/30 PASS; NuGet transitive güvenlik denetiminde bildirilen açık yok. MailKit CreateAppendOptions EnsureNewLine=true zorlar; önizleme de bu gerçek ayarı kullanmalıdır.

## Doğrulanmış sınırlar ve açık kapsam

Kabul sentetik 12 iletilik pilotla sınırlıdır. Büyük kutu veya çok sayıda özel keyword desteği kanıtlanmadı; [Dovecot Maildir](https://doc.dovecot.org/2.3/admin_manual/mailbox_formats/maildir/) 26'dan sonraki keyword'ler için index saklamasına dayanır, geniş ölçek ayrı kontrol gerektirir. Bu sürüm kullanıcı adı/parola ve zorunlu TLS içindir; OAuth, Google/Exchange'e özel modeller ve 100 GB kabulü kapsam dışıdır. Kaybolan/çoklu token tekrar yazılmadan inceleme durumuna geçer. Önizleme gerekirse hedefte boş klasör oluşturabilir; iletiler yalnız aktarım başlatılınca kopyalanır.

## Önizleme

Kullanıcı aynı Wi-Fi'daki telefondan mevcut ekranları incelemek istedi. Kabul edilmiş `prototype/dist` için `http://192.168.1.51:5176/` üzerinde ayrı Vite preview açıldı; yerel HTTP 200 doğrulandı. Bilgisayar içi geliştirme adresi `http://127.0.0.1:5173/`. Yerel posta motoru yalnız `127.0.0.1:6174` üzerinde kalır; LAN önizlemesi posta motoruna uzaktan erişim sağlamaz. Telefonun kendisinden erişim henüz kullanıcı tarafından doğrulanmadı.

Ek bağımsız kök neden kanıtı: Temp/bitigmail-task014-qa/noon-probe-report.json. Python stdlib doğrudan APPEND (MailKit kullanılmadan), ayrı yeni hedef klasörlerinde iki saat: GreenMail12:00UTC->00:00UTC,13:00->13:00; Dovecot12:00->12:00,13:00->13:00. Böylece öğlen hatası aktarım motorundan bağımsız üretildi. Kaynaklar değiştirilmedi.

Hesap deposu kritik hata kabulü: ImapAccountCriticalTests 4/4 PASS. Gerçek Windows dosya kilidiyle update/delete hataları; eski metadata/parola/sürüm disk ve bellekte korunur, silme hatası başarıya çevrilmez. Bağlantı snapshot dıştan değiştirilemez; eşzamanlı aynı sürümde iki güncellemeden tek kazanan ve restart sonrası yeni parolayla tutarlı sürüm doğrulandı. İlk taslaktaki 3/3 FAIL kanıtı üzerine Gemini depo düzeltmesi yapıldı; testler kaldırılmadı. Root yalnız bu kritik sınırı kontrol etti.
Telefon önizlemesi yerel headless Playwright 1660 ve390 genişlikte yükleme + müşteri Ayrıntılar geçişi PASS, overlay/JS exception yok. Görseller/kanıt Temp/task014-qa/preview-smoke.json. Fiziksel telefondan erişim hâlâ kullanıcı bildirimiyle doğrulanabilir.

## Kalıcı kabul paketi

Son kabulün 43 rapor/görseli proje içinde .codex-coordination/evidence/TASK-014/ dizinine kopyalandı. acceptance-summary.json sonuçları, SHA256.json dosya özetlerini içerir. Son gerçek aktarım kanıtı transfer-ec286119519a; UI ve kesintiden devam kanıtı resume-c49b3b20d4 dizinindedir. Paket parola veya yerel kimlik bilgisi dosyası içermez.

Son çalışma durumu: normal motor 127.0.0.1:6174 (PID 66208), geliştirme 127.0.0.1:5173 (PID 27820), LAN arayüz önizlemesi 192.168.1.51:5176 (PID 43176). Kök ajanın test motoru 6175 durduruldu. Telefonun fiziksel erişimi kullanıcı tarafından henüz doğrulanmadı; LAN adresi bilgisayardan ve iki ekran genişliğinde kontrol edildi.

