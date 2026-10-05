# Biçim Fizibilitesi Raporu: OST ve PST İşleme Stratejisi

Güncelleme: Bu TASK-003 ilk araştırma kaydıdır. Bağımsız OST gereksinimi kesinleşti. Güncel aday/deney kararı [OST_ENGINE_DECISION.md](OST_ENGINE_DECISION.md) içindedir. Özellikle doğrudan SaveAs dönüşüm kısıtı, modern OST öğe okuma/çıkarma ve yeni PST'ye yazmanın bütünüyle desteklenmediği anlamına gelmez; bu yollar ayrı deneyle ölçülecektir. Bu belgedeki destek beyanları laboratuvar başarısı sayılmaz.

**Tarih:** 2026-09-12  
**Kapsam:** Windows ortamında OST/PST okuma, yazma, kurtarma ve 100 GB ölçeklenebilirlik fizibilitesi.  
**Referans Doküman:** `docs/PRODUCT_BRIEF.md`

---

## 1. Yönetici Özeti ve Yetenek Matrisi

BitigMail'in kurumsal posta geçişi ve arşivleme hedeflerinde OST ve PST biçimleri kritik rol oynamaktadır. Ancak bu iki biçimin veri yapıları, kullanım senaryoları ve yazılım kütüphaneleriyle etkileşimi simetrik değildir:
- **OST (Offline Storage Table):** Bu araştırmada kaynak okuma/dönüştürme senaryosu ele alınmıştır. İncelenen yaklaşımlarla bağımsız OST hedefi doğrulanmadı; bu, bütün olası kütüphaneler için evrensel bir imkânsızlık iddiası değildir.
- **PST (Personal Storage Table):** Taşınabilir arşiv biçimidir; hem okuma hem yazma hedefi olarak kullanılabilir.

### Yetenek ve Sınır Karşılaştırma Matrisi

| Kriter / Yetenek | 1. Outlook COM / PIA & MAPI | 2. Açık Kaynak: libpff | 3. Ticari SDK: Aspose.Email |
| :--- | :--- | :--- | :--- |
| **Geliştirme / Çalışma Bağımlılığı** | İstemcide kurulu ve yapılandırılmış Outlook gerektirir | Outlook'tan bağımsız C kütüphanesi; derleme/dağıtım bağımlılıkları ayrıca incelenecek | Bağımsız (.NET / Java kütüphanesi) |
| **OST Okuma (Sağlıklı)** | Yalnızca bağlı hesap/profil içinden | Desteklenir (Doğrudan dosya/akış) | Desteklenir (Doğrudan dosya/akış) |
| **Bağımsız / Yetim (Orphaned) OST** | **Desteklenmez** (Orijinal profil şarttır) | Desteklenir | Desteklenir [Satıcı Beyanı] |
| **PST Yazma / Oluşturma** | Desteklenir (`AddStoreEx`: Unicode/ANSI) | **Desteklenmez** (Yalnızca okuma) | Desteklenir (`PersonalStorage.Create`: Yalnızca Unicode) [Satıcı Beyanı] |
| **Bozuk Dosya / Kurtarma** | `ScanPST.exe` (Bozuk satırları silebilir) | Ayrıştırma ve silinmiş öğe tarama | BTI/klasör düzeyinde tolerans [Satıcı Beyanı] |
| **Biçim ve Sürüm Sınırları** | ANSI (2 GB), Unicode (Varsayılan 50 GB) | 32-bit ANSI, 64-bit Unicode, 4K sayfa OST | Outlook 2013+ OST dönüştürme **desteklenmez** [Resmi Satıcı Dokümanı] |
| **Lisans / Dağıtım Durumu** | Microsoft Office lisansı ve yerel kurulum | LGPL-3.0-or-later (Alfa statüsünde) | Ticari lisanslı; deneme sürümünde 50 e-posta sınırı |
| **Doğrulama Statüsü** | Resmi Microsoft Dokümantasyonu | Resmi Kaynak Kodu & Biçim Dokümanı | Satıcı Beyanı (Bağımsız test edilmedi) |

---

## 2. Üç Temel Yaklaşımın İncelenmesi

### Yaklaşım 1: Microsoft Outlook Object Model / PIA ve MAPI Bağımlılığı
- **Kaynaklar:** [Microsoft Outlook API Seçimi](https://learn.microsoft.com/en-us/office/client-developer/outlook/selecting-an-api-or-technology-for-developing-solutions-for-outlook), [Outlook NameSpace.AddStoreEx](https://learn.microsoft.com/en-us/office/vba/api/outlook.namespace.addstoreex), [ScanPST Onarım Rehberi](https://learn.microsoft.com/en-us/troubleshoot/outlook/data-files/how-to-repair-personal-folder-file), [Outlook Veri Dosyası Boyut Sınırları](https://learn.microsoft.com/en-us/microsoft-365-apps/outlook/data-files/configure-size-limit-outlook-data-files).
- **Yetenekler:** `AddStoreEx` API'si ile geçerli profilde yeni Unicode (`olStoreUnicode`) veya ANSI (`olStoreANSI`) PST dosyaları oluşturulabilir.
- **Kritik Kısıtlar:** Outlook'un hedef makinede kurulu ve kullanıcı profiliyle yapılandırılmış olması zorunludur. Orijinal Exchange/IMAP profili silinmiş "yetim" (orphaned) OST dosyaları Outlook nesne modeli üzerinden açılamaz. MAPI oturum yönetimi ve arka plan servis senaryolarında iş mantığı ve kararlılık riskleri barındırır.
- **Hasarlı Dosyalar:** Microsoft'un resmi onarım aracı olan `ScanPST`, dosya yapısını düzeltirken kurtarılamayan bozuk dizin bloklarını ve iletileri kalıcı olarak silebilir. Bu durum veri kaybı riskini beraberinde getirdiği için mutlaka işlem öncesi tam yedek gerektirir.

### Yaklaşım 2: Açık Kaynak Ayrıştırıcı (libpff)
- **Kaynaklar:** [libpff Resmi Deposu](https://github.com/libyal/libpff), [PFF/OFF Biçim Dokümantasyonu](https://github.com/libyal/libpff/blob/main/documentation/Personal%20Folder%20File%20%28PFF%29%20format.asciidoc).
- **Yetenekler:** Harici uygulamaya veya Outlook profiline ihtiyaç duymadan doğrudan PFF/OFF tabanlı dosyaları (PST ve OST) bayt düzeyinde okuyabilir. 32-bit ANSI, 64-bit Unicode ve Outlook 2013+ ile gelen 64-bit 4K-sayfa sıkıştırılmış OST yapılarını destekler. Hasarlı veya silinmiş öğeleri kurtarma listelemesi sunar.
- **Kritik Kısıtlar:** Kütüphane yalnızca **okuma** odaklıdır; PST yazma desteği bulunmamaktadır. Proje "alfa" geliştirme aşamasındadır ve çoklu iş parçacığı (multi-threading) desteği henüz tamamlanmamıştır.
- **Lisans:** LGPL-3.0-or-later lisansı altındadır; dinamik kütüphane bağlama koşulları ve dağıtım gereksinimleri değerlendirilmelidir.

### Yaklaşım 3: Bağımsız Ticari SDK (Örnek: Aspose.Email)
- **Kaynaklar:** [Aspose.Email Outlook Dosyaları](https://docs.aspose.com/email/net/reading-and-converting-outlook-files/), [PersonalStorage.Create Referansı](https://reference.aspose.com/email/net/aspose.email.storage.pst/personalstorage/create/), [Lisans ve Değerlendirme Kısıtları](https://docs.aspose.com/email/net/licensing-and-limitations/).
- **Yetenekler:** Dosyadan veya akıştan (stream) doğrudan OST/PST okuyabilir; `PersonalStorage.Create` ile bağımsız Unicode PST dosyaları oluşturabilir.
- **Kritik Sürüm Engeli [Satıcı Dokümantasyonu]:** Aspose, resmi belgelerinde Outlook 2013, 2016, 2019, 2021 ve sonraki sürümlere ait OST dosyalarının PST'ye dönüştürülmesini **desteklemediğini** açıkça belirtmektedir. Ayrıca PST'den OST'ye dönüştürme kesinlikle desteklenmemektedir.
- **Değerlendirme Sınırları:** Lisanssız deneme modunda her PST klasöründen en fazla 50 e-posta çıkarılabilir. *(Not: Bu yetenekler satıcı beyanı olup laboratuvar ortamında bağımsız olarak doğrulanmamıştır; ürün mimarisinde doğrudan taahhüt sayılamaz.)*

---

## 3. Format ve Protokol Yönlerinin Bağımsız Doğrulanması

Her format veya protokolün iki yönlü (hem kaynak hem hedef) çalışabileceği varsayılmamalıdır:
- **OST:** Bu aşamada kaynak olarak ele alınması öneriliyor. İncelenen motorlarla yazılabilir hedef desteği doğrulanmadı.
- **PST:** Hem kaynak hem hedef olabilir (Unicode formatında yazılabilir).
- **OLM:** Bu OST/PST araştırmasında motor/API ve yazma desteği yeterince incelenmedi. Kaynak ve hedef desteği ayrı bir fizibilitede doğrulanmalı; burada zorunlu uygulama yöntemi veya kesin destek kararı çıkarılmamalı.
- **POP3:** Protokol gereği yalnızca gelen kutusundan ileti çeker; sunucuya posta yükleme veya klasör hiyerarşisi oluşturma yeteneği yoktur (hedef olamaz).
- **IMAP / Exchange / Google Workspace:** Uygun izinler ve API'lerle çift yönlü çalışabilir; ancak servis kotaları ve protokol yetenekleri bağımsız doğrulanmalıdır.

---

## 4. 100 GB Boyutundaki Verilerin Doğrulama Stratejisi

Microsoft, 64-bit Unicode dosyalarda boyut sınırının kayıt defteriyle yükseltilebileceğini, ancak dosya boyutu arttıkça performansta ciddi düşüşler yaşanabileceğini resmi olarak belgelemektedir. ANSI biçimi ise kesin 2 GB sınırına tabidir. 100 GB girdi hedefi için performans garantisi verilmeden uygulanacak mühendislik adımları:

1. **Sınırlı Bellek (Bounded Memory):** Dosya belleğe toplu yüklenmeyecek; B-Tree düğümleri ve ileti gövdeleri akış (streaming) veya sayfalama (paging) yoluyla işlenecektir.
2. **Disk Çalışma Alanı (Disk Workspace):** Gereken alan; seçilen çıktı, indeks, geçici dosya ve varsa koruma kopyası üzerinden hesaplanacak. Kaynak boyutuna sabit bir katsayı bu araştırmayla doğrulanmadı; gerçek motor ölçümleriyle ön kontrol modeli geliştirilecek.
3. **Kayıtlı İlerleme Noktası ve Devam (Checkpoint-Resume):** Kesinti durumunda işlemin baştan başlamaması için işlenen klasör ve ileti kimlikleri yerel durum veritabanında saklanacaktır.
4. **Çıktı Bütünlüğü Doğrulaması:** Okunan ve hedefe yazılan öğelerin sayıları, ileti boyutları ve hata kayıtları karşılaştırılacak; okunamayan bozuk öğeler dürüstçe raporlanacaktır.
5. **Temsili Test Veri Setleri:** Sentetik ve gerçekçi 10 GB, 50 GB ve 100 GB test armatürleri oluşturulmadan hız veya kesin süre taahhüdü verilmeyecektir.

---

## 5. Astra İçin 3 Temel Mimari Karar

1. **Çalışma Bağımlılığı Tercihi:** İstemci makinede yerel Outlook kurulumunu şart koşan MAPI/PIA yaklaşımı mı benimsenecek, yoksa kurulu Outlook gerektirmeyen bağımsız bir motor mu hedeflenecek?
2. **Yetim (Orphaned) OST ve Sürüm Desteği:** Modern OST dosyaları için incelenen SDK belgesindeki kısıt, ürün sürümü ve gerçek örneklerle doğrulanmalı. Bağımsız okuyucu+yazıcı birleşimi ile diğer uygun SDK seçenekleri değerlendirilmeden özel ayrıştırıcı geliştirme zorunluluğu sonucu çıkarılmamalı.
3. **Format Rol Kapsamı:** İlk sürümde OST ve OLM formatlarının yalnızca "kaynak" (salt okunur); PST, MBOX ve EML formatlarının ise hem "kaynak" hem "hedef" olarak sınırlandırılması onaylanıyor mu?

---

## 6. Sınırlandırılmış Gelecek Teknik Deneme (Spike)

- **Spike Konusu:** Temiz bir Windows sanal makinesinde (Outlook kurulu olmayan), bağımsız bir Outlook 2016/2019 OST test armatürü (10-20 GB) üzerinden `libpff` C/C# sarmalayıcısı ile veri okuma ve eşzamanlı olarak açık/bağımsız bir yöntemle Unicode PST oluşturma fizibilitesinin ölçülmesi.
- **Sınırlar:** TASK-003 yalnız araştırmadır; bu deneme henüz başlatılmadı. Gelecekte küçük bir teknik prototip görevi olarak ayrıca planlanmalı; seçilen okuyucu ve somut PST yazıcısı belirlenmeli, sınırlı deneme koduyla bellek tüketimi, yetim dosya okuma başarısı ve çıktı bütünlüğü ölçülmelidir. Ürün özellikleri geliştirme kapsamı buna dahil değildir.

