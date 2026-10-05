# Dosya ↔ Posta Hesabı Köprüsü (File ↔ Account Bridge) — Nihai Kabul ve Doğrulama Kaydı

**Görev Kimliği**: TASK-017  
**Nihai Durum**: **TAMAMLANDI (DONE)** — ASTRA / Root ve SOL Tarafından Kabul Edildi  
**Doğrulama Ortamı**: Yerel Dovecot IMAP (`127.0.0.1:5143`), Kestrel TestingHost (`127.0.0.1:6175`), Kestrel LocalHost (`127.0.0.1:6174`), Frontend Kökeni (`http://127.0.0.1:5173`)  

---

## 1. Yönetici Özeti ve Nihai Doğrulama Metrikleri

- **Backend Derleme**: `dotnet build -c Release` (LocalHost ve TestingHost) -> **0 Uyarı, 0 Hata**.
- **Backend Testleri**: `dotnet test -c Release engine\BitigMail.Engine.Tests\BitigMail.Engine.Tests.csproj` -> **369 / 369 PASS** (0 Hata, 0 Atlanan).
- **Frontend Kalite Kapıları**:
  - TypeScript tip kontrolü (`typecheck`): **PASS**.
  - ESLint kod analizi (`lint`): **PASS**.
  - Vitest testleri: **110 / 110 PASS** (8 dosya).
  - Production derlemesi (`build`): **PASS** (yalnızca ~585 kB boyutunda non-blocking uyarı).
- **Genişletilmiş Kabul Koşumları**:
  - `ext-f4da216137`: **540 / 540 KONTROL PASS** (%100).
  - `mbox-03bded8cb8`: **78 / 78 KONTROL PASS** (%100).
  - **Toplam Bağımsız Kontrol**: **618 / 618 PASS** (%100).
- **Kök Bağımsız MBOX Ayrıştırması**: 5 ileti / 3 ek exact raw + headers + decoded MIME parts tam eşitlik ile **PASS**.
- **Kök Nihai Arayüz Kabulü**: 1660×948 masaüstü ve 390×844 mobil pencerelerinde sıfır yatay taşma, sıfır konsol hatası/uyarısı ile **PASS**.

---

## 2. Kök Kritik Bayt Politikası ve Kalıcılık Sözleşmesi

Kök mülkiyetindeki dosyalara (`BridgeMimeBytePolicy.cs`, `BridgeMimeBytePolicyCriticalTests.cs`, `BridgeJournalCriticalTests.cs`) kesinlikle dokunulmamış, halka açık API'leri üretim kodu tarafından eksiksiz tüketilmiştir:
- **BridgeMimeBytePolicy (18 Test PASS)**:
  - Ham (raw) kaynak baytlarının korunması; yalnızca tekil (bare) LF karakterlerinin CRLF'e dönüştürülmesi.
  - Önceden CRLF olan satırların korunması; eksik son satır sonunun (terminal newline) tespit edilip raporlanması.
  - Bare CR, NUL ve bozuk MIME başlıklarının kesinlikle reddedilmesi.
  - `mboxrd` From kaçış (`>From `) derinliği, sondaki boş satırların (trailing blank lines) korunması ve aynı içeriğe sahip fiziksel kopyaların eksiksiz saklanması.
  - Özgün 12 EML'nin MimeKit/MailKit serileştirmesi kanonik baytlarla birebirdir.
- **BridgeTransferJournal (9 Test PASS)**:
  - Disk veya işlem arızasında doğrulanmamış geçişlerin yayınlanmaması.
  - Süreç yeniden başlatıldığında kayıtların sıfırlanmaması (persistence).
  - Arayan taraf mutasyon izolasyonu (caller mutation isolation).
  - Dışa aktarım kaynak kimliğinin değiştirilememesi ve tahrif edilmiş günlüklerin tespiti.
  - Dizin geçişi veya geçersiz sembol içeren kayıt kimliklerinin engellenmesi.

---

## 3. Bağımsız Başlangıç Kaynakları ve Doğrulama Kahini

Doğrulamalar, ürün kodundan tamamen bağımsız Python standart kitaplığı (`imaplib`, `email.parser`) kullanan [`verify_bridge.py`](file:///C:/Users/Eddiz/Documents/ChatGPT/Mail%20Manager/lab/task017/verify_bridge.py) üzerinden yürütülmüştür:
- **Baseline İletileri**:
  - EML Baseline: 12 fiziksel ileti, 4 ek.
  - MBOXRD Baseline: 12 fiziksel ileti, 4 ek.
  - *MBOXRD Trailing Line Farkı*: MBOX kayıtlarında biçim standardı gereği fazladan bir son boş satır bulunur; bu nedenle EML ile MBOX ham baytları doğrudan karşılaştırılmaz. İçe aktarımlar kendi özgün baseline'ına göre değerlendirilir.
- **Kaynak Değişmezliği (Source Invariance)**:
  - Tüm test fazları öncesinde ve sonrasında kaynak Dovecot IMAP hesabı salt okunur (`EXAMINE`, `BODY.PEEK`) taranmış; kaynak UID, UIDVALIDITY, raw SHA-256, bayraklar ve INTERNALDATE değerlerinin %100 değişmeden korunduğu kanıtlanmıştır.

---

## 4. Gerçek API ve Genişletilmiş Kabul Kanıtları

### 4.1 İlk Ürün API Koşumu (`api-a6473db107`, 190/190 PASS)
- Yetkili TestingHost (`127.0.0.1:6175`) üzerinde gerçek endpointler ve izole lab hesapları ile yürütüldü.
- EML ve MBOX içe/dışa aktarım 12/4 multiset doğrulaması, yan manifest doğrulaması (12/12 metadata ve özgün klasör eşlemesi) başarıyla tamamlandı.

### 4.2 Genişletilmiş Kabul Koşumu (`ext-f4da216137`, 540/540 PASS)
1. **Vendor-Free Bridge-Over50 Kabulü**:
   - Kaynak: 72 fiziksel EML dosyası ve 24 ek (`over50-fixture.json`).
   - Hedef IMAP klasörüne 72 ileti ve 24 ek eksiksiz aktarıldı.
   - Ürün raporuna dayanmaksızın doğrudan Python IMAP ayrıştırıcısı ile taranmış; hiçbir Aspose filigranı veya değerlendirme kısıtlaması bulunmadığı kanıtlanmıştır.
2. **Filtrelenmiş İstanbul Aktarımı (3 İleti / 1 Ek)**:
   - `2024-01-01` - `2024-02-28` tarih filtresi uygulanarak 4 iletili klasörden tam 3 uygun ileti ve 1 ek filtrelenerek aktarıldı.
3. **EML Dışa Aktarım -> İçe Aktarım Döngüsü (Roundtrip)**:
   - 12 ileti dışa aktarılıp yeni bir klasöre geri aktarıldı; aynı Message-ID'ye fakat farklı gövdeye sahip kopyalar dahil sıfır kayıpla multiset eşitliği sağlandı.
4. **Gerçek Süreç Çökmesi ve Sıfır Yinelenen Doğrulaması (İçe Aktarım)**:
   - `PauseAfterAppendBeforeReturn` dikişi tetiklendi.
   - PID `59516` (Release `BitigMail.TestingHost.dll`, LISTEN 6175) `Stop-Process -Id 59516 -Force` ile öldürüldü ve portun kapandığı teyit edildi.
   - PID `55888` boş `FileHandleRegistry` ile başlatıldı.
   - Günlüğe dokunulmadan `resume` çağrıldı; Dovecot üzerinde rastgele 128-bitlik `bitigmail_*` anahtar kelimesi arandı, 1 adet eşleşme bulunarak ikinci bir APPEND yapılmadan uzlaştırıldı. Kalan 11 ileti tamamlandı (tam 12 ileti, sıfır mükerrer).
5. **Gerçek Süreç Çökmesi ve Çıktı Kökü Bağlama (Dışa Aktarım - EML)**:
   - `PauseAfterExportItemPersisted` dikişi ile PID `55888` öldürüldü; yeni PID `57252` ile başlatılarak çıktı dizini kimliği korunarak 5/5 tamamlandı.
6. **Lost-Response ve Anahtar Kelime Uzlaşımı**:
   - APPEND sonrası bağlantı kopması simülasyonunda anahtar kelime eşleşmesi ile sıfır yinelenen sağlandı.
7. **Doğal Sıfır Eşleşme Negatifi (`PauseBeforeAppend` ile Keyword0)**:
   - Sunucuya henüz gitmeyen ileti için devam ettirmede 0 eşleşme bulundu; yinelenen ekleme riskini önlemek için fail-closed olarak durdu (`NeedsAttention`), hedefte ileti sayısı 1 kaldı.
8. **Güvenlik ve Negatif Sınırlar**:
   - Bozuk MBOX (junk/empty/malformed), kaynakta kayma (drift), sibling dizin tahrifatı ve dizin geçişi denemeleri fail-closed engellendi.

### 4.3 Özel MBOXRD Dışa Aktarım Çökme / Yeniden Başlatma Kabulü (`mbox-03bded8cb8`, 78/78 PASS)
- **Hedef Format**: `mboxrd`, 5 `INBOX` iletisi ve 3 ek.
- **Dikiş**: `PauseAfterExportItemPersisted` (Ordinal 2). İleti 1 staging dosyası (`.staging/fld_001/msg_00000001.raw`, 1044 bayt) diske yazıldıktan sonra dikiş tetiklendi.
- **Süreç Öldürme**: PID `60584` (`CreationDate: 2026-09-14T01:56:01.560253+03:00`, LISTEN 6175) `Stop-Process -Id 60584 -Force` ile sonlandırıldı, portun kapandığı doğrulandı.
- **Süreç Yeniden Başlatma**: Gizli pencere (`CREATE_NO_WINDOW`) ile PID `4484` başlatıldı (`CreationDate: 2026-09-14T01:56:11.31421+03:00`, PID 4484 != 60584, boş `FileHandleRegistry`).
- **Devam Ettirme (Resume)**: Günlüğe dokunulmadan `/api/transfer/bridge/export/resume` çağrıldı; körü körüne üzerine yazma/ekleme yapılmadan 5/5 tamamlandı.
- **Staging Temizliği ve MBOX Dosyası**: `.staging/fld_001` dizini temizlendi (0 `.raw` dosya); `fld_001.mbox` (6173 bayt) üretildi.
- **Bağımsız Ayrıştırma**: Kök bağımsız MBOX okuyucusu tam 5 ileti / 3 ek (exact raw + headers + decoded parts) doğruladı.
- **Manifest ve Kaynak Değişmezliği**: 5/5 manifest eşleşmesi (`mappingPass: True`) ve kaynak INBOX öncesi/sonrası exact UID/raw/flags/internaldate tam eşitliği teyit edildi.

---

## 5. Gerçek Arayüz ve Regresyon Kabulü

Kök tarafından Playwright ile yürütülen bağımsız UI doğrulamaları (`root-ui/report.json`):
- **1660×948 Masaüstü Görünümü**:
  - EML→IMAP (12 ileti) ve IMAP→MBOX (12 ileti) tam akışları tamamlandı.
  - Yatay taşma sıfır, konsol hata/uyarısı sıfır.
- **390×844 Mobil Görünümü**:
  - UTC+03 tarih filtresiyle EML→IMAP (3 ileti) ve IMAP→EML (3 ileti) tam akışları tamamlandı.
  - Doküman yatay taşması sıfır; form alanları ve gerçek düğme etkileşimleri doğrulandı. Fiziksel telefon erişimi ve tüm düğmeler için minimum dokunma alanı ölçümü yapılmadı.
- **Yön Değişimi ve Form Sıfırlama**:
  - Üst sekmede import ↔ export yönü değiştirildiğinde eski form ve önizleme verileri eksiksiz temizlenmektedir (explicit direction exit).
- **JobCenter Durum Geri Yükleme**:
  - Tamamlanmış dışa aktarım işi `job-e3b7b9bd4d09` geri yüklenip 12 ileti ve rapor başarıyla görüntülendi.
  - Başarısız/duraklatılmış içe aktarım işi `job-2fe1ff9c295c` geri yüklenip dondurulmuş TASK017 firma/proje bağlamı ve resume kontrolleri doğrulandı.
- **Tam Regresyon**: Dört tamamlanmış gerçek iş (`f79ec8b3ab02`, `3b6cab7b2f7c`, `bb1b4e5c4b63`, `bb68b02f7f14`) ile masaüstü 12/12, mobil 3/3 kesintisiz çalışmıştır.

---

## 6. Mimari Sınırlar, Lisans ve Güvenlik Güvenceleri

1. **Vendor-Free Kapsamı**:
   - Yalnızca **EML/mboxrd ↔ IMAP** köprü yolu vendor-free çalışmakta olup hiçbir harici değerlendirme lisansı veya filigranı içermez; 72 iletiyi başarıyla aktarmıştır.
   - **PST/OST** dönüştürme ve dışa aktarım işlemleri mevcut Aspose lisans/değerlendirme politikalarına ve klasör başına 50 ileti sınırlarına tabi kalmaya devam eder.
2. **Hata İletileri ve Yol Güvenliği**:
   - Hata ve istisna yakalama bloklarında hiçbir beklenmedik ham istisna mesajı, yığın izi (stack trace), dahili sunucu yolu veya gizli kimlik bilgisi (şifre, oturum anahtarı) istemciye sızdırılmaz; genel generic Türkçe hata yanıtları korunur.
   - Yetkili kullanıcı işlemleri kapsamında dizin seçici (`/api/picker/*`) ve tamamlanan iş raporlarında döndürülen meşru hedef çıktı yolları sözleşme gereği amaçlandığı şekilde sağlanmaktadır.
3. **Bellek ve Süreç İzolasyonu**:
   - Worker'lar diskteki dondurulmuş plan ve atomik günlük üzerinden çalıştığı için süreç çökmeleri bellek kayıplarından etkilenmez; yeni süreç boş `FileHandleRegistry` ile başlasa dahi iş aynı çıktı ve hedef üzerinde güvenle sürdürülür.

Kabul küçük sentetik kaynaklarla sınırlıdır. 100 GB PST/OST performansı, bozuk dosya kurtarma, gerçek Google/Microsoft sağlayıcı pilotları veya sidecar metadata'nın yeniden içe alınırken geri yüklenmesi bu sonuçlarla doğrulanmış değildir. Kaynak/hedef değişmişse veya kopya kimliği belirsizse devam otomatik başarıya zorlanmaz; kullanıcı kontrolü gerektiren durumda durur. Azure kaydı ve silmesiz gerçek Outlook pilotu sonraki yerel aşamalardan sonra sohbet içinde hatırlatılacaktır; kullanıcı isteğiyle zamanlayıcı kapalıdır.
