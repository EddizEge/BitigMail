# Mail Corpus v1 — Sentetik E-posta Test Kümesi

**Sürüm:** v1  
**Tarih:** 2026-09-12  
**Formatlar:** MIME RFC 5322 (`.eml`), Unix MBOX (`corpus.mbox`), Doğrulama Manifestosu (`manifest.json`)  
**Laboratuvar / Canlı Ortam Statüsü:** **LAB: NOT RUN** (Canlı posta istemcisi, hesap veya Outlook erişimi yoktur).

---

## 1. Kapsam ve Kritik Sınırlar (Scope & Limitations)

1. **Sentetik MIME Biçimi (MIME Synthetic Only):** Bu dizindeki veri seti, standart kütüphane araçlarıyla üretilmiş RFC 5322 (`.eml`) ve Unix MBOX (`corpus.mbox`) formatında sentetik e-posta iletilerinden oluşmaktadır.
2. **Geçerli OST veya PST Değildir (NOT a Valid OST/PST):** Bu dosyalar doğrudan Outlook Çevrimdışı Veri Dosyası (`.ost`) veya Kişisel Klasör Dosyası (`.pst`) **değildir**. İkili B-Tree, MAPI sayfalama yapısı veya 4K DEFLATE blokları içermez.
3. **OST Dönüşüm/Kurtarma Kanıtı Teşkil Etmez:** Bu kümenin varlığı, BitigMail'in bağımsız/yetim (orphaned) modern OST dosyalarını PST'ye dönüştürme yeteneğini veya 100 GB büyük veri işleme kapasitesini kanıtlamaz.
4. **Gerçek OST Armatürü Hâlâ Zorunludur:** OST ayrıştırma ve PST yazma spike deneyi (TASK-006) için gerçek masaüstü Outlook tarafından üretilmiş, hesaptan koparılmış küçük bir yapay OST armatürü temin edilmelidir.

---

## 2. Gelecek Outlook Yetim OST Üretim Planı (Future OST Isolation Plan)

Bu sentetik posta kümesinin ileride kontrollü bir yapay yetim OST armatürüne dönüştürülmesi için planlanan aşamalar şunlardır (bu aşamalar **yalnızca bir plandır**, şu an hiçbir hesap açılmamış veya Outlook çalıştırılmamıştır):

```
+---------------------------+       +-------------------------+       +-------------------------+
| fixtures/mail-corpus-v1   | ----> | Test Posta Kutusu       | ----> | Masaüstü Outlook        |
| (12 EML / corpus.mbox)    |       | (İzole IMAP/Exchange)   |       | (Senkronizasyon/Önbellek)|
+---------------------------+       +-------------------------+       +-------------------------+
                                                                                   |
                                                                                   v
+---------------------------+       +-------------------------+       +-------------------------+
| İzole Test Ortamı         | <---- | Kapalı Dosya Kopyası    | <---- | Outlook Tam Kapatılır   |
| (Hesap/Profil Olmayan)    |       | (Closed-Copy .OST)      |       | (Tüm kilitler serbest)  |
+---------------------------+       +-------------------------+       +-------------------------+
```

1. **İçeri Aktarım (Import):** `corpus.mbox` veya EML iletileri geçici, izole bir test e-posta hesabına (ör. yerel test IMAP/Exchange sunucusu) aktarılır.
2. **Senkronizasyon (Sync):** Temiz bir Windows sanal makinesinde Outlook açılarak test hesabına bağlanır ve tüm klasörler tam olarak senkronize edilir.
3. **Kapatma (Close Outlook):** Senkronizasyon bittikten sonra Outlook normal biçimde kapatılır ve dosya yazımının/işlemin sona ermesi beklenir. Kopya almak için istemci zorla sonlandırılmaz.
4. **Kapalı Kopya Alma (Closed-Copy):** `%LOCALAPPDATA%\Microsoft\Outlook\` altındaki üretilen `.ost` dosyası kopyalanır ve SHA-256 özeti alınır.
5. **Yetimleştirme (Orphan Fixture):** Kopyalanan OST dosyası, orijinal hesabın ve MAPI profilinin bulunmadığı temiz bir ortamda [güncel aday deneyinde](../../docs/OST_ENGINE_DECISION.md) girdi olarak kullanılır. SDK ile öğe çıkarma/yeni PST yazma ve alternatif okuyucu-yazıcı yolları ayrı değerlendirilir; henüz motor seçilmedi.

---

## 3. MBOX Yazma, Okuma ve Kaçış Semantiği (From_ Escaping Semantics)

Unix MBOX formatında iletiler satır başındaki `From ` (From-space) zarf ayracı ile birbirinden ayrılır. İleti gövdesinde satır başında `From ` bulunan metinlerin ileti ayracı olarak algılanmasını önlemek için standart kaçış kuralları uygulanır:

### `mboxo` vs `mboxrd` Diyalektleri ve Açık Dönüştürme:
- **Python Standart Kütüphanesi `mboxo` Davranışı:** Python stdlib `mailbox.mbox` (ve `email.generator.BytesGenerator` içindeki `mangle_from_=True`), varsayılan olarak yalnızca satır başında `^From ` olan ifadeleri `>From ` olarak kaçırır (`mboxo`). Zaten `>From ` ile başlayan bir satıra dokunmaz. Bu durum, orijinali `>From ` olan bir satır ile kaçırılmış bir `From ` satırının birbirine karışmasına ve geri dönüşümsüz bilgi kaybına yol açar.
- **Açıkça Uygulanan `mboxrd` Dönüştürmesi ve Satır Sonu Ayrımı:** Bu kaybı önlemek ve iki ayrı satır tipini de korumak için `scripts/generate_mail_corpus.py`, EML başlık/gövde sınırını (`\r\n\r\n` veya `\n\n`) güvenilir biçimde tespit ettikten sonra gövde satırlarını `splitlines(keepends=True)` ile işler. Böylece karma CRLF/LF satır sonu temsillerinden bağımsız olarak, gövdede yer alan ve satır başında sıfır veya daha fazla `>` karakterini takip eden her fiziksel `From ` ifadesinin (`^>*From `) başına bir `>` eklenir:
  * `From ` -> `>From `
  * `>From ` -> `>>From `
  * `>>From ` -> `>>>From `
- **Kayıpsız Geri Dönüşüm (Tek Seferlik Unescaping):** Okuma sırasında istemcinin veya `mailbox.mbox` modülünün `From_` kaçışını otomatik çözüp çözmediği denetlenir. Eğer çözülmemişse `^>(>*From )` şablonuna uyan satırlardan en baştaki tek bir `>` silinir; otomatik çözülmüşse ikinci kez çözme (double-unescape) yapılmaz. Böylece hem orijinal `From ` hem de orijinal `>From ` satırları tam olarak korunur.

### `msg-08` Örneği ve İki Aşamalı Doğrulama:
`msg-08.eml` gövdesinde iki özgün satır yer alır:
```text
From Ahmet Yılmaz <ahmet.yilmaz@posta.example>
Tarih: 2024-08-24 10:00

>From Canan Özkan <canan.ozkan@muhendislik.example>
>Tarih: 2024-08-23 15:30
```
1. **Aşama 1 — Ham Bayt Düzeyinde Denetim:** `corpus.mbox` içerisine `mboxrd` kurallarıyla yazıldığında, ham MBOX baytları ayrıştırılmadan önce doğrudan taranır:
   * `>From Ahmet Yılmaz <ahmet.yilmaz@posta.example>` satırının varlığı doğrulanır.
   * `>>From Canan Özkan <canan.ozkan@muhendislik.example>` satırının varlığı doğrulanır.
   * Gövdede kaçırılmamış `From Ahmet Yılmaz` bulunmadığı, dolayısıyla MBOX ayracını 13'e bozmadığı kanıtlanır.
2. **Aşama 2 — Semantik Ayrıştırma ve Çözme:** `mailbox.mbox` ile tam 12 ileti ayrıştırılır; `msg-08` gövdesi tek seferlik çözme kuralıyla doğrulanır ve hem `'From Ahmet Yılmaz...'` hem de `'>From Canan Özkan...'` özgün satırlarının korunduğu kanıtlanır.

### Bütünlük İlkesi ve Ham Hash Durumu:
> **MBOX Serileştirmesi Sonrası Bütünlük İlkesi:** MBOX serileştirmesinden sonra ham EML hash eşliği **garanti edilmez** (zarf ayraç satırları `From <sender> <date>`, `From ` kaçış önekleri ve satır sonu normalizasyonları nedeniyle bayt dizilimi farklılaşabilir). Bu nedenle doğrulama işlemi ham EML bayt hash'i zorlamak yerine; **semantik gövde metni (unescaped body text), ileti başlıkları ve ayrıştırılan eklerin SHA-256 özetleri (hem EML hem MBOX üzerinden)** doğrulanarak gerçekleştirilir.

---

## 4. Veri Kümesi Yapısı ve İçerik Haritası

Küme tam **12 fiziksel EML kaydı** içerir:
- **11 Özgün Ham İçerik:** `msg-01.eml` ve `msg-02.eml` bayt düzeyinde birbirinin kopyası olan bir çift oluşturur (`isDuplicate: true`).
- **Aynı Message-ID / Farklı İçerik Çifti:** `msg-03.eml` ve `msg-04.eml` aynı RFC `Message-ID` değerine (`<ortak-kimlik-2024-revizyon@projeler.example>`) sahiptir; ancak farklı tarih, konu, gövde ve ham hash değerleri taşır. Yalnızca Message-ID üzerinden tekilleştirme yapmanın hatalı olduğunu kanıtlamak için tasarlanmıştır.

### Mantıksal Klasör Dağılımı:
| Mantıksal Klasör | Kayıt Sayısı | Dahil Olan Fixture ID'leri |
| :--- | :--- | :--- |
| `Gelen Kutusu` | 5 | `msg-01`, `msg-02`, `msg-05`, `msg-06`, `msg-07` |
| `Gönderilenler` | 3 | `msg-08`, `msg-09`, `msg-10` |
| `Projeler/İstanbul` | 4 | `msg-03`, `msg-04`, `msg-11`, `msg-12` |

### Ekler (Attachments):
1. **`şartname_özeti_2024.txt` (`msg-05`):** UTF-8 Türkçe dosya adı taşıyan ve RFC 2231 parametre kodlamasıyla iletilen metin eki.
2. **`sistem_parametreleri.bin` (`msg-06`):** `0x00` - `0xFF` arası 256 baytlık deterministik ikili veri eki.
3. **`rapor_grafik.png` (`msg-07`):** Standart kütüphane (`struct` + `zlib`) ile bellek içinde üretilmiş geçerli küçük RGB PNG dosyası.
4. **`logo.png` (`msg-11`):** HTML gövdesi içinde `<img src="cid:proje_logo_cid">` ile çağrılan `multipart/related` gömülü (inline) PNG görseli.

### Sabit Filtre Orakılları (Filter Oracles):
Manifestoda ve generator içinde tasarım aşamasında sabitlenmiş doğrulanabilir beklenen listeler:
1. **UTC Tarih Aralığı Orakılı `[2024-01-01T00:00:00Z, 2025-01-01T00:00:00Z)`:**  
   - **Tasarım Sınır Vakası (`msg-11`):** `msg-11` kaydı yerel zaman damgası olarak `2024-01-01 00:30:00 +0300` (yerel yıl 2024) taşır; ancak UTC anına dönüştürüldüğünde `2023-12-31 21:30:00Z` (UTC yıl 2023) anına denk gelir. Bu nedenle UTC yarı-açık aralık kuralı gereğince bu orakıldan **kesinlikle hariç tutulmuştur**.
   - **Ürün/Arayüz Zaman Dilimi Tercihi Ayrımı:** Orakıl burada açıkça **UTC** standardında etiketlenmiştir. İleride BitigMail kullanıcı arayüzünde (UI) aramaların/filtrelerin kullanıcının yerel işletim sistemi saat dilimine göre mi yoksa UTC deposuna göre mi filtreleneceği konusu bağımsız bir ürün tercihidir (product preference) ve bu veri kümesinin temel UTC bütünlük testini etkilemez.
   - **Beklenen Fixture ID Listesi:** `['msg-03', 'msg-04', 'msg-05', 'msg-06', 'msg-07', 'msg-08', 'msg-12']` (7 ileti).
2. **Ek İçeren İletiler Orakılı (Attachment-Bearing Messages):**  
   - **Beklenen Fixture ID Listesi:** `['msg-05', 'msg-06', 'msg-07', 'msg-11']` (4 ileti: metin, ikili, PNG dosya eki ve inline CID PNG).

---

## 5. Üretim ve Doğrulama Talimatı

Kümeyi yeniden üretmek ve doğrulamak için:
```bash
python scripts/generate_mail_corpus.py --output-dir fixtures/mail-corpus-v1
```
Geçici bir dizinde bağımsız test etmek için:
```bash
python scripts/generate_mail_corpus.py --output-dir /path/to/temp_dir
```
Script; 12 EML ayrıştırmasını (defects == 0), 11 özgün hash'i, çift kayıtları, ek bütünlüğünü, UTC filtre orakıllarını, ham MBOX bayt kaçışını, 12 mbox ileti ayrıştırmasını, mboxrd tekil geri dönüşüm semantiğini ve MBOX ek özetlerini denetleyerek 13 kontrolün tamamı geçtiğinde çıkış kodu `0` ile tamamlanır.
