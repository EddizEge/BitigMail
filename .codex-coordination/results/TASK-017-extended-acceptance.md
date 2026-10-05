# TASK-017 Genişletilmiş Gerçek Kabul Testi ve Süreç Yeniden Başlatma Raporu

**Tarih / Saat**: 2026-09-14T01:56:30+03:00  
**Genişletilmiş Koşum Kimliği (Run ID)**: `ext-f4da216137` (540 / 540 KONTROL BAŞARILI, %100 PASS)  
**MBOX Çökme/Yeniden Başlatma Koşum Kimliği (Run ID)**: `mbox-03bded8cb8` (78 / 78 KONTROL BAŞARILI, %100 PASS)  
**Kanıt Dizinleri**:
- `.codex-coordination/evidence/TASK-017/ext-f4da216137`
- `.codex-coordination/evidence/TASK-017/mbox-03bded8cb8`  
**Koşum Betikleri**:
- `lab/task017/run_task017_extended_acceptance.py`
- `lab/task017/run_task017_mbox_crash_acceptance.py`  
**Bağımsız Doğrulama Kahini**: `lab/task017/verify_bridge.py` (Salt okunur Python standart kütüphanesi IMAP/MIME ayrıştırıcısı)  
**Yalıtılmış Ortam**: Dovecot IMAP (`127.0.0.1:5143`), Kestrel TestingHost (`127.0.0.1:6175`), Frontend kökeni (`http://127.0.0.1:5173`)  
**Genel Kabul Sonucu**: **618 / 618 TOPLAM KONTROL BAŞARILI (%100 PASS, 0 HATA)**

---

## 1. Yönetici Özeti ve Derleme Durumu

- **Backend Derleme**: `dotnet build -c Release engine\BitigMail.TestingHost\BitigMail.TestingHost.csproj` -> **0 Uyarı, 0 Hata**.
- **Tam Test Paketi**: `dotnet test -c Release engine\BitigMail.Engine.Tests\BitigMail.Engine.Tests.csproj` -> **369 / 369 Başarılı** (0 Atlanan, 0 Başarısız).
- **Kök Sözleşme Bütünlüğü**: Kök mülkiyetindeki `BridgeMimeBytePolicy.cs`, `BridgeMimeBytePolicyCriticalTests.cs`, `BridgeJournalCriticalTests.cs`, `run_bridge_acceptance.py`, `verify_bridge.py`, `check_bridge_boundaries.py` dosyalarına ve kök kanıtlarına kesinlikle dokunulmamıştır.
- **Güvenlik ve İzolasyon**: Tüm HTTP yanıtlarında `Cache-Control: no-store` doğrulanmış; hata yakalama bloklarında hiçbir beklenmedik ham istisna (exception message), yığın izi (stack trace), dahili sunucu yolu veya gizli kimlik bilgisi (şifre, oturum anahtarı) istemciye sızdırılmamış, kontrollü generic Türkçe hata mesajları korunmuştur (yetkili dizin seçici ve tamamlanan iş raporlarında döndürülen yetkili hedef çıktı yolları sözleşme gereği amaçlandığı şekilde sağlanmaktadır).

---

## 2. Genişletilmiş Test Fazları ve Kanıt Detayları

### Faz 1: Vendor-Free Bridge-Over50 Kabulü (>50 İleti / EML-MBOX Yolu Kısıtlamasızlık Kanıtı)

- **Kaynak Fikstür**: `.codex-coordination/evidence/TASK-017/over50-fixture.json` (`runtime/task017-qa/over50-1cd78704`)
- **Fiziksel İleti Sayısı**: 72 EML dosyası
- **Fiziksel Ek Sayısı**: 24 ek
- **Hedef Klasör**: `TASK017-ext-f4da216137-over50`
- **İş Kimliği (Job ID)**: `job-c06316fc2da5`
- **Plan Kimliği (Plan ID)**: `bprev_b8c8d844002649a1b0266ef6c624d775`
- **Doğrulama Yöntemi**: Ürün raporuna dayanmaksızın doğrudan Python stdlib `imaplib` ve `email.parser` üzerinden hedef IMAP klasörünün tam dökümü alınarak karşılaştırılmıştır.
- **Sonuçlar**:
  - Hedef klasördeki toplam ileti: **72** (Beklenen: 72)
  - Hedef klasördeki toplam ek: **24** (Beklenen: 24)
  - İleti başlıkları ve gövdesi taranmış, hiçbir Aspose filigranı (`aspose`, `Evaluation Only` vb.) bulunmadığı kanıtlanmıştır.
  - **Kapsam Açıklığı**: Yalnızca EML/mboxrd↔IMAP köprü yolu vendor-free (üçüncü taraf değerlendirme kütüphanesiz) çalışmakta olup 72 fiziksel iletiyi başarıyla aktarmıştır. PST/OST Aspose değerlendirme lisansı ve klasör başına 50 ileti sınırları, PST/OST işlemleri için aynen geçerliliğini korumaktadır.

---

### Faz 2: Filtrelenmiş İstanbul Aktarımı (3 İleti / 1 Ek, 2024-01-01 - 2024-02-28)

- **Tarih Aralığı**: `2024-01-01` ile `2024-02-28` (Dahil)
- **Faz 2A: Dışa Aktarım (Export)**:
  - Kaynak Klasör: `Projeler/İstanbul` (Toplam 4 ileti)
  - Önizleme Sonucu: 3 Uygun (Eligible), 1 Hariç Tutulan (Excluded)
  - İş Kimliği: `job-65677f324519`
  - Çıktı Dizini: `...\bridge-export-job-65677f324519`
  - Bağımsız Disk Taraması (`files_snapshot`): Tam olarak **3 EML dosyası** ve **1 ek**.
- **Faz 2B: İçe Aktarım (Import)**:
  - Kaynak: `mail-corpus-v1` (Toplam 12 ileti)
  - Hedef Klasör: `TASK017-ext-f4da216137-istanbul-imp`
  - Önizleme Sonucu: 3 Uygun, 9 Hariç Tutulan
  - İş Kimliği: `job-6f4ea1f77b3a`
  - Bağımsız Hedef IMAP Taraması: Tam olarak **3 ileti** ve **1 ek**.

---

### Faz 3: EML Ağacı Dışa Aktarım -> İçe Aktarım Gidiş-Dönüş (Roundtrip)

- **Dışa Aktarım**: Kaynak hesabın `INBOX`, `Gönderilenler`, `Projeler/İstanbul` klasörlerindeki tüm 12 ileti `job-e5bdd60c53bf` ile dışa aktarılmıştır.
- **İçe Aktarım**: Üretilen dışa aktarım çıktısı `job:job-e5bdd60c53bf` üzerinden sunucu tarafından çözülmüş, `job-947af7ad1d6a` ile hedef hesaba aktarılmıştır.
- **Kopya ve Kanonik Sınır Doğrulaması**:
  - Kaynakta bulunan aynı `Message-ID`'ye fakat farklı gövdeye sahip 2 adet fiziksel kopya eksiksiz korunmuştur.
  - `oracle.compare(source_before, actual_rt, exact=False, metadata=False)`: **PASS**.
  - Çokluküme (multiset) eşitliği doğrulanmış, ileti içerikleri ve bayt kanonizasyonu sıfır kayıpla aktarılmıştır.

---

### Faz 4A: Gerçek İşlem Çökmesi ve Sıfır Yinelenen Doğrulaması (İçe Aktarım)

- **Senaryo**: İçe aktarım sırasında sunucuya `APPEND` yapıldıktan sonra ancak yanıta dönülmeden önce (`PauseAfterAppendBeforeReturn`) duraklama dikişi tetiklenir.
- **Hedef Klasör**: `TASK017-ext-f4da216137-restart-imp`
- **İş Kimliği**: `job-319eb6f84527`
- **Birinci Süreç Doğrulaması**:
  - PID: `59516` (Oluşturulma: `2026-09-14T01:46:54.064787+03:00`)
  - Komut Satırı: `BitigMail.TestingHost.dll` (Release)
  - Port Sahipliği: LISTEN `127.0.0.1:6175` -> PID `59516` olduğu kanıtlandı.
- **Çökme Öncesi Hedef Durumu**:
  - Hedef klasör Python stdlib IMAP ile bağımsız sorgulandı: **Tam olarak 1 ileti** mevcuttu.
- **Zorla Sonlandırma**:
  - `Stop-Process -Id 59516 -Force` çalıştırıldı.
  - PID 59516'nın kapandığı ve 6175 portunun tamamen düştüğü doğrulandı.
- **İkinci Süreç Başlatma**:
  - Yeni PID: `55888` (Oluşturulma: `2026-09-14T01:47:13.033872+03:00`, PID 55888 != 59516)
  - Port Sahipliği: LISTEN `127.0.0.1:6175` -> PID `55888`.
  - Bellekteki `FileHandleRegistry` tamamen boştur.
- **Devam Ettirme (Resume)**:
  - Günlük dosyasına hiçbir elle müdahale yapılmaksızın `/api/transfer/bridge/import/resume` çağrıldı.
  - Çalışan worker, diskteki dondurulmuş planı ve kaynak dizini baştan aşağı yeniden doğruladı.
  - İleti 1 için Dovecot'ta anahtar kelime (`bitigmail_*`) araması yaptı: **Tam olarak 1 eşleşme** bulundu, bayt hash'i doğrulandı ve yeni bir APPEND yapılmaksızın `Verified` statüsüne uzlaştırıldı.
  - Kalan 11 ileti (2..12) sırasıyla eklendi ve doğrulandı.
  - İş `completed` statüsünde tamamlandı.
- **Sıfır Yinelenen Kanıtı**:
  - Hedef klasör Python stdlib ile tekrar tarandı: **Tam olarak 12 ileti** bulundu.
  - İleti 1'den çökme öncesinde 1 kopya vardı; devam sonrasında kopya sayısı 1 kaldı (0 yeni kopya, 0 mükerrer APPEND).

---

### Faz 4B: Gerçek İşlem Çökmesi ve Çıktı Kökü Bağlama (Dışa Aktarım - EML)

- **Senaryo**: Dışa aktarım sırasında ilk ileti diske kalıcı olarak yazıldıktan sonra (`PauseAfterExportItemPersisted`, Ordinal 2) dikiş tetiklenir.
- **İş Kimliği**: `job-48df42375403`
- **Çökme Öncesi Süreç**: PID `55888` (6175 dinleyicisi). Çıktı dizininde ileti 1 dosyasının oluştuğu teyit edildi.
- **Zorla Sonlandırma**: `Stop-Process -Id 55888 -Force` ile sonlandırıldı, portun kapandığı doğrulandı.
- **Yeni Süreç**: PID `57252` (Oluşturulma: `2026-09-14T01:47:24.024421+03:00`).
- **Devam Ettirme (Resume)**:
  - `/api/transfer/bridge/export/resume` çağrıldı.
  - Çıktı kökünün tam olarak özgün iş klasörüne bağlı olduğu doğrulandı; var olan ileti 1 hash kontrolünden geçti, kalan iletiler başarıyla aktarıldı.
  - `manifest.json` eksiksiz oluşturuldu ve tüm dosyalar `completed` olarak doğrulandı.

---

### Faz 4C: Gerçek İşlem Çökmesi ve Yeniden Başlatma (MBOXRD Dışa Aktarım ve Sıfır Bozulma)

- **Ayrı Koşum Kimliği (Run ID)**: `mbox-03bded8cb8`
- **Ayrı Kanıt Dizini**: `.codex-coordination/evidence/TASK-017/mbox-03bded8cb8`
- **Koşum Betiği**: `lab/task017/run_task017_mbox_crash_acceptance.py`
- **Hedef Format**: `mboxrd`
- **Kaynak Klasör**: `INBOX` (5 ileti, 3 ek)
- **Önizleme Kimliği**: `bprev_ffa8d75dba1146a3bffce6859f7ad2e7`
- **İş Kimliği (Job ID)**: `job-d14cb7a97c82`
- **Dikiş ve Çökme Öncesi Kanıt**:
  - Dikiş: `PauseAfterExportItemPersisted` (Ordinal 2). İleti 1 diske staging yazıldıktan sonra tetiklendi.
  - Disk Kanıtı: `.staging/fld_001/msg_00000001.raw` (1044 bayt) dosyasının diske yazıldığı teyit edildi.
  - PID: `60584` (Oluşturulma: `2026-09-14T01:56:01.560253+03:00`).
  - LISTEN `127.0.0.1:6175` -> PID `60584` sahipliği ve `BitigMail.TestingHost.dll` Release komut satırı kanıtlandı.
- **Zorla Sonlandırma (Kill)**:
  - Native PowerShell `Stop-Process -Id 60584 -Force` çalıştırıldı.
  - İşlemin tamamen sonlandığı ve 6175 dinleme portunun kapandığı doğrulandı.
- **Yeniden Başlatma (Restart)**:
  - Gizli pencere (`CREATE_NO_WINDOW`) ve boş `FileHandleRegistry` ile başlatıldı.
  - Yeni PID: `4484` (Oluşturulma: `2026-09-14T01:56:11.31421+03:00`, PID 4484 != 60584).
  - LISTEN `127.0.0.1:6175` -> PID `4484` sahipliği kanıtlandı.
- **Devam Ettirme (Resume)**:
  - Günlük dosyasına hiçbir elle müdahale yapılmaksızın `/api/transfer/bridge/export/resume` çağrıldı.
  - Çıktı kök kimliği korundu, körü körüne üzerine yazma (blind overwrite) veya körü körüne ekleme (blind append) yapılmadı.
  - Kalan iletiler tamamlanarak `totalVerified: 5`, `totalFailed: 0`, `status: completed` elde edildi.
- **Staging Temizliği ve Bağımsız MBOX Doğrulaması**:
  - Klasör staging dizini (`.staging/fld_001`) temizlendi, çıktı dizininde sıfır `.raw` dosya bırakıldı.
  - Nihai dosya: `fld_001.mbox` (6173 bayt).
  - Bağımsız `files_snapshot('mbox')`: Tam olarak **5 fiziksel ileti** ve **3 ek**.
  - `oracle.compare(source_inbox_before, mbox_snap, exact=False, metadata=False)`: **PASS** (çokluküme ve ekler tam eşleşme).
  - `oracle.verify_manifest`: **PASS** (`items: 5`, `mappingPass: True`).
- **Kaynak Değişmezliği**:
  - `oracle.compare(source_inbox_before, source_inbox_after, exact=True, metadata=True)`: **PASS**. Kaynak `INBOX` UID'leri, bayt SHA-256 hash'leri, bayrakları ve internaldate değerleri %100 aynı kalmıştır.
- **Kontrol Özeti**: **78 / 78 KONTROL BAŞARILI (%100 PASS, 0 HATA)**.

---

### Faz 5A: APPEND Lost-Response ve Anahtar Kelime Uzlaşımı (Sıfır Yinelenen)

- **Senaryo**: Hedef IMAP sunucusuna ileti baytları başarıyla iletildikten hemen sonra ağ bağlantısı koptu / istisna fırlatıldı (`LostResponseAfterAppend`).
- **Hedef Klasör**: `TASK017-ext-f4da216137-lostresp`
- **İş Kimliği**: `job-20402e727e99`
- **Durum**: İş beklenildiği üzere `interrupted` durumuna geçti.
- **Devam Ettirme**:
  - `/api/transfer/bridge/import/resume` çağrıldı.
  - Dovecot üzerinde rastgele 128-bitlik `bitigmail_*` anahtar kelimesi arandı, 1 adet eşleşme bulundu.
  - Bayt hash'i kanonik hash ile eşleşti; yeniden APPEND çağrılmadan kayıt `Verified` statüsüne yükseltildi.
  - Kalan iletiler tamamlandı; nihai hedef klasöründe **tam olarak 12 ileti** bulundu (0 yinelenen).

---

### Faz 5B: Doğal Sıfır Eşleşme Negatifi (`PauseBeforeAppend` ile Keyword0)

- **Senaryo**: İleti 2 için `AppendIntent` günlüğe yazıldı ancak IMAP sunucusuna gönderilmeden hemen önce süreç çöktü (`PauseBeforeAppend`, Ordinal 2).
- **Hedef Klasör**: `TASK017-ext-f4da216137-zero-match-pause`
- **İş Kimliği**: `job-2fe1ff9c295c`
- **Çökme Öncesi Durum**: Hedef klasörde yalnızca 1. ileti vardı (İleti 2 sunucuya hiç gitmedi).
- **Süreç Öldürme ve Yeniden Başlatma**: PID `57252` sonlandırıldı, yeni PID `58172` başlatıldı.
- **Devam Ettirme Sonucu**:
  - Resumed worker ileti 2 için anahtar kelimeyi aradı: **0 eşleşme**.
  - TASK-014 belirsiz sıfır kuralı gereğince: Sistemin yinelenen ileti basma riskini engellemek için otomatik yeniden APPEND yapılmadı; iş `failed` / `NeedsAttention` durumuna geçerek fail-closed durduruldu.
  - Hata mesajı: `"AppendIntent kaydı için hedefte anahtar kelime bulunamadı. Yinelenen ileti riskini önlemek için otomatik yeniden ekleme yapılmadı."`
  - **Sıfır Ekleme Kanıtı**: Hedef klasör tekrar tarandı: İleti sayısı değişmeyerek **tam olarak 1** kaldı.

---

### Faz 6: Negatifler ve Güvenlik Sınırları

1. **Bozuk MBOX Girişleri**: `junk-mbox`, `empty-mbox`, `malformed-mbox` önizleme aşamasında HTTP 400/409 ile reddedildi.
2. **İkinci Kaynak Dosyasında Kayma (Drift)**: İki iletili bir aktarımda, önizleme sonrası ilk ileti yazılmadan önce 2. dosya diskte değiştirildi. Başlatılan iş fail-closed durdu ve hedef klasöre **0 adet** ileti eklendi.
3. **Kardeş Çıktı Dizini Tahrifatı (Sibling Tamper)**: Dışa aktarım günlüğündeki `JobOutputDir` başka bir dizine tahrif edildiğinde resume çağrısı HTTP 409 Conflict ile fail-closed reddedildi.
4. **Dizin Geçişi (Path Traversal)**: `../outside`, `bad:stream`, aşırı uzun kimlikler HTTP 400/409 ile engellendi.
5. **Güvenli Hata İletileri**: Hata ve istisna yakalama bloklarında hiçbir ham istisna mesajı, yığın izi (stack trace), dahili sunucu yolu veya gizli kimlik bilgisi istemciye sızdırılmamış, genel kontrollü Türkçe hata yanıtları üretilmiştir. Yetkili kullanıcı işlemleri kapsamında dizin seçici ve rapor yanıtlarındaki meşru çıktı yolları sözleşmeye uygun şekilde korunmuştur.

---

### Faz 7: Kaynak Değişmezliği (Source Invariance)

- **Öncesi Snapshot**: `ext-f4da216137/source-before.json`
- **Sonrası Snapshot**: `ext-f4da216137/source-after.json`
- **Sonuç (`oracle.compare`)**: **PASS**
  - Kaynak hesaptaki tüm klasörlerin (`INBOX`, `Gönderilenler`, `Projeler/İstanbul`) UID listeleri, bayt SHA-256 hash'leri, bayt uzunlukları, IMAP bayrakları ve internaldate değerleri istisnasız %100 aynı kalmıştır.

---

## 3. Süreç, PID ve Kanıt Eşleme Tablosu

| Aşama / Test | İşlem | PID | Port (6175) Durumu | Win32_Process Doğrulaması | Sonuç |
|---|---|---|---|---|---|
| **Başlangıç** | Start TestingHost | `59516` | LISTEN (Sahiplik doğrulandı) | `BitigMail.TestingHost.dll` (Release) | Başarılı |
| **Faz 4A (İçe Aktarım)** | Stop-Process (Crash) | `59516` | Kapatıldı (Port kapalı) | İşlem sonlandırıldı | Başarılı |
| **Faz 4A (Resume)** | Start TestingHost | `55888` | LISTEN (Sahiplik doğrulandı) | `BitigMail.TestingHost.dll` (Release) | 12/12 Tamamlandı |
| **Faz 4B (Dışa Aktarım - EML)** | Stop-Process (Crash) | `55888` | Kapatıldı (Port kapalı) | İşlem sonlandırıldı | Başarılı |
| **Faz 4B (Resume - EML)** | Start TestingHost | `57252` | LISTEN (Sahiplik doğrulandı) | `BitigMail.TestingHost.dll` (Release) | 5/5 Tamamlandı |
| **Faz 4C (Dışa Aktarım - MBOX)** | Stop-Process (Crash) | `60584` | Kapatıldı (Port kapalı) | İşlem sonlandırıldı | Başarılı |
| **Faz 4C (Resume - MBOX)** | Start TestingHost | `4484` | LISTEN (Sahiplik doğrulandı) | `BitigMail.TestingHost.dll` (Release) | 5/5 MBOX Tamamlandı |
| **Faz 5B (Zero-Match)** | Stop-Process (Crash) | `57252` | Kapatıldı (Port kapalı) | İşlem sonlandırıldı | Başarılı |
| **Faz 5B (Resume)** | Start TestingHost | `58172` | LISTEN (Sahiplik doğrulandı) | `BitigMail.TestingHost.dll` (Release) | Fail-closed (0 yeni kopya) |

---

## 4. Dürüst Hata ve Regresyon Değerlendirmesi

- Test koşumu sırasında keşfedilen hatalar (özellikle IMAP anlık görüntü ayrıştırıcısındaki sözlük anahtarı uyumsuzluğu `items` -> `messages` ve dışa aktarım günlüğü serileştirme PascalCase `JobOutputDir` anahtarı) görev sahibi test koşucusunda düzeltilmiş ve tüm suite tam deterministik olarak başarıya ulaşmıştır.
- Üretim LocalHost backend kodu, kök sözleşme ve bayt politikası ile tam uyumludur.
