# BitigMail — Claude için proje devir notu

Hazırlanma: **24 Eylül 2026**. Son geliştirme/kabul: **21 Eylül 2026, TASK043**.
Bu belge, mevcut kaynak kodu ve tarihli kabul kayıtlarına dayanır. Yeni bir test çalıştırması değildir.

## 1. Önce bunu bil

BitigMail sıfırdan başlanacak bir fikir değil: çalışan React arayüzü, .NET posta motoru, yerel yetkilendirme, arşiv/arama, aktarım/kurtarma akışları ve Windows kurulum paketi mevcut.

**En son durum: 0.9.3 kaynak kodu ve Windows paketi hazır, yerel testleri geçti. Kullanıcı bilgisayarındaki etkin kurulum hâlâ 0.9.2.** `active-version.txt` 24 Eylül tarihinde salt okunur kontrol edildi ve `0.9.2` görüldü. 0.9.3'ün kullanıcı profiline kurulup açılması tamamlanmadı. Kullanıcıdan eski uygulamanın sistem tepsisinden normal çıkışla kapatılması istenmişti; bu konuşmada henüz kapanış teyidi yok. Eski PID kayıtlarını güncel süreç kimliği sanma.

Kullanıcının ChatGPT Pro 20x aboneliği bitmiş, Plus'a geçmiş; projeyi Claude'a devretmek için bu dosyayı istedi. Bu belgeyi üretmek için uygulama veya kullanıcı verisi değiştirilmedi. Eski Codex/Astra/SOL iş bölümü Claude'un çalışması için gerekli bir bağımlılık değildir.

**Tam ticari 1.0 veya 9. aşamanın bütün dış kabul koşulları tamamlanmış değil.** İmza sertifikası, temiz Windows testi, ticari SDK lisansı, temsilî büyük/hasarlı dosyalar ve canlı sağlayıcı kabulü açık. Azure ve gerçek Outlook pilotu özellikle **10. aşamaya** ertelendi.

## 2. Ürün amacı ve kullanıcı tercihleri

- Ad: **BitigMail**. Eski Türkçe “bitig” kökünden gelen posta kimliği; marka/alan adı/hukuki kullanım kesinleşmiş sayılmaz.
- Hedef: müşteri şirketlere hizmet veren BT firmaları. Çok şirket, çok proje, çok posta kutusu ve arşiv.
- İlk platform Windows; Mac'ten gelen arşivlerin Windows'ta işlenmesi önemli. Yerel cihazda çalışma; merkezi ekip sunucusu henüz ürünün mevcut mimarisi değil.
- Temel problem: OST/PST/EML/MBOX/OLM/Apple Mail dosyaları ve IMAP/POP, Google Workspace, Microsoft posta altyapıları arasında filtreli taşıma, dönüştürme, arşivleme ve arama.
- Tek yönlü taşıma ürünü değil; her kaynak/hedef yönü ayrı uygulanır ve doğrulanır. “Tüm formatları her formata dönüştürür” iddiası yok.
- Kullanıcı özellikle büyük PST'lerin bölünmesini, bozuk posta depolarının okunabilen kısmının kurtarılmasını, tarih ve başka alanlarla seçim yapmayı istedi. 100 GB bir hedef; doğrulanmış kapasite değil.
- Görsel kimlik: **sarı, turuncu, beyaz**; mavi ağırlıklı tasarım istemiyor. MailStore Home ilk işlevsel referanslardan biri.
- Müşteriler ekranı şirket/proje/hesap yönetimi içindir. Aktarım/dönüştürme ayrı sekmededir. Arşiv ve arama birden fazla şirket/proje/arşiv seçebilmelidir.
- Kaynak postalar silinmeyecek. Kullanıcı kişisel Outlook hesabıyla test önermişti; silmesiz şartı geçerli.
- Türkçe, anlaşılır, teknik ayrıntıyla boğmayan iletişim ister. Rutin adımlarda sürekli onay istemeden somut işi tamamlamayı tercih eder.
- Azure hatırlatması yol haritasında/sonraki aşama konuşmasında olacak; zamanlayıcı veya otomasyon istemiyor.
- Önceki kota kuralı %5 kalınca toparlayıp durmaktı. Bu eski Codex kota durumunu Claude'un güncel kotası gibi yorumlama.

## 3. Belgelerde doğruluk sırası ve eskimiş bilgiler

Önce bu belgeyi, sonra `.codex-coordination/PROJECT_STATE.md` içindeki **Current authoritative state** bölümünü ve TASK043 sonuçlarını oku. Ardından ilgili kodu ve yeni tarihli kabul belgesini incele.

Önemli: README.md, ROADMAP.md ve bazı workflow belgeleri eski tarihli cümleler içeriyor. Örneğin README arşiv/aramayı hâlâ demo, kurtarmayı yapılmamış ve üretimde TLS'yi koşulsuz zorunlu anlatabiliyor. Bunlar son kodun doğru özeti değil. Bazı mimari belgeler “henüz uygulanmadı” diye başlayan tasarım notlarını koruyor; sonradan yazılan kabul belgeleri ve kod bunu aşmış durumda. Eski Azure “3. aşama” notları da güncel 10. aşama kararının gerisinde.

- Güncel yol haritası: `docs/FULL_RELEASE_ROADMAP.md`
- Son iş: `.codex-coordination/results/TASK-043.md`
- Son motor değişiklikleri: `.codex-coordination/results/TASK-043-root-transport.md`
- Son birleşik kanıt: `.codex-coordination/evidence/TASK-043/sol043-final.json`
- Windows kabulü/sınırları: `docs/WINDOWS_RELEASE_ACCEPTANCE.md`
- Format/yön gerçek kapsamı: `docs/FORMAT_DIRECTION_MATRIX.md`
- Aşama geçmişi: `docs/STAGES_3_9_EXECUTION.md`

Geliştirme kabulü, gerçek sağlayıcı kabulü, lisanslı çıktı kabulü ve ticari yayın kabulü aynı şey değildir. Test sayıları tarihli kanıttır; hedefli kümeleri toplam test sayısına ekleme.

## 4. Dosya düzeni ve teknoloji

Proje kökü: `C:\Users\Eddiz\Documents\ChatGPT\Mail Manager`

| Konum | Sorumluluk |
|---|---|
| `prototype/` | React/TypeScript uygulaması; adı prototype olsa da Windows paketinin gerçek arayüzü burada |
| `engine/BitigMail.Engine/` | Formatlar, MIME, arşiv/arama, planlama, veri bütünlüğü, depolama ve dağıtım çekirdeği |
| `engine/BitigMail.LocalHost/` | ASP.NET Core yerel HTTP API, kimlik/yetki, dosya seçiciler, hesaplar, OAuth, işler ve servis orkestrasyonu |
| `engine/BitigMail.Desktop/` | WinForms + WebView2 Windows kabuğu; sahip olduğu motor sürecini başlatır ve kapatır |
| `engine/BitigMail.RecoveryWorker/` | Takılabilen SDK kurtarma çağrılarını ayrı süreçte yalıtan işçi |
| `engine/BitigMail.Setup/` | Kullanıcı başına kurulum, güncelleme, geri dönüş, kaldırma ve başlatıcı |
| `engine/BitigMail.TestingHost/` | Ayrı profil/port ve sentetik dosya seçicilerle entegrasyon/E2E sunucusu |
| `engine/BitigMail.Engine.Tests/` | xUnit motor, bütünlük, güvenlik ve yaşam döngüsü testleri |
| `prototype/src/tests/`, `prototype/tests/e2e/` | Vitest ve Playwright testleri |
| `fixtures/`, `lab/` | Test verileri, OST deneyleri, posta laboratuvarı; sır içeren dosyalar olabilir |
| `runtime/` | Geliştirme/test çalışma verileri; kaynak kod değildir, topluca silme |
| `artifacts/` | Masaüstü ve sürümlü dağıtım çıktıları |
| `scripts/` | Başlatma/durdurma, derleme ve sentetik veri üretim yardımcıları |
| `.codex-coordination/` | Tarihli görevler, sonuçlar, kanıtlar, mimari/yürütme kararları |
| `docs/`, `design/` | Ürün, marka, teknik sözleşme, kabul ve tasarım notları |

Kaynakta görülen bağımlılıklar:
- React 18.3.1, TypeScript ^5.7.0, Vite ^8.3.0; Vitest ^5.0.0, Playwright ^1.63.0. Kesin çözülmüş sürümler için package-lock.json.
- .NET 8; Windows uygulama/host projelerinde `net8.0-windows`, çekirdekte `net8.0`.
- Aspose.Email 24.8.0, MimeKit/MailKit 4.17.0, Microsoft.Data.Sqlite 10.0.12, System.Formats.Asn1 8.0.1.
- MSAL Microsoft.Identity.Client 4.89.0; Google.Apis.Auth 1.76.0; Identity PasswordHasher için Microsoft.Extensions.Identity.Core 8.0.20.
- Yerel SDK: `.tools/dotnet/dotnet.exe`. Önceki çalışmada 8.0.425 kullanıldı. Global `dotnet` yalnız runtime olabildiği için proje SDK yolunu kullan.
- `prototype/package.json` sürümünün 0.1.0 olması Windows yayın sürümünün 0.1.0 olduğu anlamına gelmez. Dağıtım manifestindeki sürüm belirleyicidir.

## 5. Çalışma mimarisi

```text
Windows kurulum başlatıcısı
  → etkin sürümde BitigMail.Desktop (WinForms/WebView2)
      → kendi BitigMail.LocalHost süreci (rastgele loopback port)
          → aynı origin üzerinden paketlenmiş React UI + /api
          → IdentityCatalog / oturum / yetki / hesaplar / JobManager
          → Engine: dosya dönüşümü, MIME, arşiv, SQLite FTS5
          → MailKit: IMAP/POP; MSAL/Google: OAuth
          → gerekli kurtarma işlemi için ayrı RecoveryWorker
```

Son kullanıcı uygulaması Node/Vite çalıştırmaz; React üretim çıktısı statik paketlenir. .NET runtime dağıtımda self-contained gelir; WebView2 Runtime gerekir.

Geliştirme Vite portu 5173, doğrudan LocalHost varsayılan portu 6174, TestingHost 6175. **Masaüstü paketinin portu sabit 6174 değildir.** Önce mevcut süreç ve port sahipliğini kontrol et; eski notlardaki PID'lere işlem yapma.

### Arayüz

- `prototype/src/App.tsx`: IdentityGate altında AuthenticatedApp, ana sekmeler ve yetkili katalog aktarımı.
- `components/auth/IdentityGate.tsx`: ilk yönetici, giriş, hesap/çalışma alanı yönetim paneli.
- `components/layout/Header.tsx`, `StatusBar.tsx`; tema `src/styles/index.css`.
- `state/useAppState.ts`: sayfa/iş planı/dizin durumu; demo saklama `state/storage.ts`. Yetkili katalog çalışma anında eşitlenir, demo depoya otomatik aktarılmaz.
- `api/localEngineClient.ts`, `types/localEngine.ts`: HTTP istemci ve DTO'lar.
- `components/clients/ProjectImapAccounts.tsx`: IMAP/Microsoft/Google hesap ekleme, düzenleme, test/klasör keşfi.
- `components/transfer/`: IMAP, dosya-hesap köprüsü, POP ve işlem seçimi.
- `components/convert/`, `archive/`, `search/`, `recovery/`, `jobs/`, `reports/`, `filters/`, `settings/`: ilgili akışlar.

### API ve servisler

- `LocalHost/Program.cs`: bağımlılık kaydı, profil, SDK başlangıcı, loopback port, sahip olunan süreç yaşam döngüsü.
- `LocalEngineApiEndpoints*.cs`, `IdentityApiEndpoints.cs`: uç nokta grupları.
- `LocalHost/Security/`: yerel kimlik kataloğu, oturum, Origin/Host kontrolleri, sır şifreleme, kurulum kanıtı ve kaynak sahipliği.
- `LocalHost/Jobs/`: kalıcı iş durumu, kuyruk, ilerleme, dispatch ve devam.
- `LocalHost/Imap/Transfer/`, `Bridge/Transfer/`, `Pop/`: önizleme, sabit plan, worker, günlük/rapor ve MailKit bağlantıları.
- `Engine/Archive/`: yönetilen ham MIME arşivi ve yeniden kurulabilir SQLite FTS5 indeksi.
- `Engine/Planning/`, `LocalHost/Planning/`: sürümlü filtreler/şablonlar ve seçim politikaları.
- `Engine/Distribution/` ve Setup: paket doğrulama, veri şeması, kurulum kilitleri.

## 6. Desteklenen işler ve sınırlar

| Kaynak → hedef | Durum / önemli sınır |
|---|---|
| OST → yeni Unicode PST | Gerçek akış; klasör/tarih seçimi, önizleme, doğrulama ve rapor. SDK deneme sınırları geçerli |
| PST/OST → yıl/boyut bazlı PST parçaları | Yeni çıktılar; kapanmış dosyanın gerçek boyutu kontrol edilir; tek öğe sınırı aşarsa hata |
| EML dosyaları/ağacı, mboxrd → PST | Gerçek; klasörler ve seçili fiziksel iletiler korunur; Aspose deneme işaretleri raporlanır |
| EML/mboxrd → IMAP | Gerçek laboratuvar kabulü; plan, filtre, devam ve hedef doğrulama |
| IMAP → EML/mboxrd | Gerçek; metadata manifestte; kaynak salt okunur |
| IMAP → IMAP | Kopyalama; klasör eşleme, filtre, sabit plan, doğrulama ve desteklenen kesintilerden devam |
| POP → EML | UIDL/LIST anlık görüntüsüne bağlı, kaynakta DELE yok; POP hedefi yok |
| PST/OST/OLM → EML | Nitelikli posta normalizasyonu; sağlıklı okuma ile kurtarma ayrı |
| Apple Mail EMLX → EML | Tam/self-contained paket ve sidecar doğrulaması; tarih/işaret belirsizlikleri korunur |
| EML/mboxrd → yönetilen arşiv | Ham içerik, manifest, indeks; çok şirket/proje/arşiv kapsamında arama |
| Arşivde seçili sonuçlar → EML | Sabit seçim, hash ve yetki doğrulaması; eşleme/yinelenme politikası |
| Hasarlı PST/OST → kurtarılan EML ağacı | Ayrı işçi, kısmi/okunamayan sonuç ayrımı; geniş üretim kurtarma garantisi değil |

MBOX yalnız açık **mboxrd** kabulüdür; mboxo/mboxcl/mboxcl2 uygulanmış sayılmaz. Apple Mail `.mbox` klasörü düz mboxrd dosyası değildir.

OLM/EMLX özgün tarih anlamı çözülmemiş durumlarda sonraki tarih filtreleri engellenir. Normalizasyon manifestleri bu uyarıları EML'e, arşive, önizlemeye ve devam planına taşır; dosya biçimini değiştirerek kısıt aşılmaz. Kişi/takvim/görev desteği mevcut posta kapsamından çıkarılamaz.

Microsoft kurumsal ve kişisel OAuth, Google OAuth bağlantı altyapısı yerel kabul aldı. Gerçek sağlayıcı/tenant/kota davranışları doğrulanmış değil. Şirket içi Exchange yalnız yapılandırılmış genel IMAP düzeyinde; native Exchange/Graph/EWS desteği vaat etme.

## 7. Değiştirilmemesi gereken veri bütünlüğü ilkeleri

1. Kaynak dosya ve posta korunur. Kaynakta silme, POP DELE veya IMAP EXPUNGE ekleme. Arayüzde “taşıma” denmesi kaynak silme yetkisi değildir.
2. Kaynak fingerprint/hash, hesap sürümü, kapsam, filtre/eşleme/yinelenme tercihi önizleme planına bağlanır. Değişen seçim yeni önizleme gerektirir.
3. Dosya oluşması başarı değildir: hash, MIME/ek, fiziksel öğe ve gerekli metadata doğrulaması yapılır. SDK deneme farkları açıkça nitelendirilir.
4. Mevcut hedefin üzerine yazma. Yeni çalışma alanında üret, tam doğrulamadan yayımlama; atomik/same-volume yayın ve kesinti günlüğü kurallarını koru.
5. Aynı Message-ID fiziksel kopyaların aynı öğe olduğunu kanıtlamaz. Varsayılan kopyaları korur; yinelenme politikası açık seçimdir.
6. Kaynak/hata/uyarı anlamını sonraki aşamada kaybetme. Bilinmeyen toplam için uydurma başarı/kurtarma yüzdesi gösterme.
7. HTML posta önizlemesi ayrıcalıklı uygulama içinde ham HTML/script çalıştırmaz veya uzak izleyici yüklemez.
8. Disk önkontrolü tahmindir; alan rezervasyonu/kapasite garantisi değildir. Yetersiz/bilinmeyen alanda fail-closed davranışları koru.
9. Arşiv indeksini kaynak verinin yerine koyma. Ham MIME+manifest korunur; indeks yeniden kurulabilir.
10. Kesinti sonrası her akış otomatik devam etmiyor. Desteklenen plan/journal ile açık devam yapılır; kuyruk restartta kendiliğinden başlamaz.

Kuyruk: tek etkin iş, en fazla 32 bekleyen iş. Öncelik bekleyen sırayı değiştirir, çalışan işi kesmez. İş merkezi sayfalıdır; raporlar/arşive ekle seçicisinin tam geçmiş okuma ölçek sınırı hâlâ dikkate alınmalı.

## 8. Kimlik, sırlar ve bağlantı güvenliği

- Yerel yönetici/operatör; sunucuda şirket/proje izinleri. İstemcinin companyId/projectId göndermesi tek başına izin değildir.
- İlk yönetici özel yerel kanaldan gelen tek kullanımlık kısa ömürlü kanıtla oluşturulur. Production anonim `/api/session` ile admin yaratmaz.
- İlk yönetici hatası TASK042'de giderildi: kurulum kanıtı bekleme/reload sonrası eskiyordu. WebView `BITIGMAIL_REFRESH_SETUP` ile native kanalı kullanır; özel stdin `SETUP_PROOF` ve HMAC READY yanıtı. HTTP üzerinden yeni kanıt üretme, süreyi sınırsız yapma veya URL/loga sır koyma.
- Uygulama parolaları Identity PasswordHasher ile; posta parolaları/OAuth cache Windows DPAPI ile korunur. Public DTO/log/raporda sır gösterilmez.
- Oturum, kullanıcı, handle/önizleme/OAuth işlemi ve kayıtlı iş kapsamları ayrı denetlenir. Dispatch/resume yetkiyi yeniden kontrol eder. Son aktif yöneticinin kaldırılması engellenir.
- API route/method envanteri `Security/ApiRoutePolicy.cs` içinde sabit hash ile doğrulanır. Yeni uç eklerken yetki politikasını ve envanter farkını incele; sadece startup geçsin diye kontrolü kapatma.
- Exact Host/Origin, mutation JSON ve token kontrollerini koru; wildcard CORS veya genel Origin bypass çözüm değildir.
- OAuth sağlayıcı uç noktaları sabit TLS kullanır, token parola overload'una gönderilmez.
- TASK043: parola tabanlı IMAP/POP için `allowUnencryptedConnection` açık seçim alanı eklendi. Varsayılan SSL/TLS; alternatif zorunlu STARTTLS; `none` ancak açık seçimle. IMAP sunucu/port değişince onay yenilenir, TLS'ye geçince temizlenir. Otomatik downgrade veya sertifika doğrulama bypass yok.
- İlgili kod: `ImapConnectionPolicy.cs`, `ImapAccountRecord.cs`, `ImapAccountStore.cs`, `ImapCredentialResolver.cs`, `ImapClientService.cs`, `MailKitTransferClient.cs`, `PopAccountStore.cs`.
- TASK043 gerçek proje API'si: `POST /api/catalog/companies/{companyId}/projects`, gövde `{ "name": "..." }`; admin-only. Yeni proje operatöre otomatik izin kazandırmaz. `IdentityCatalog.Projects.cs`.

Yerel rol modeli, aynı Windows kullanıcısı yetkisiyle disk okuyabilen programa karşı OS yalıtımı değildir. Denetim hash zinciri mutlak/tahrif edilemez hukuki kayıt değildir. Arşiv yedekleri DPAPI sırlarını taşınabilir hesap kimlik bilgisi olarak içermez.

## 9. Son iki düzeltme: TASK042 ve TASK043

### TASK042 — ilk yönetici oluşturulamadı
Kurulum kanıtının 5 dakika sonra/reload ile geçersizleşmesi giderildi. Native kanaldan her gönderimde yenileme ve kimliği doğrulanmış cevap; süre veya HTTP güvenliği gevşetilmedi. 0.9.2 kullanıcı kurulumuna geçti. Kullanıcı daha sonra oturum açmış yönetici ekranını paylaştı; kimliği sıfırlamak gerekmez.

### TASK043 — tema, karmaşık aktarım ve hesap bağlantıları
Kullanıcının son şikâyetleri: global ham yönetici formları, okunmayan aktarım/dönüştürme ekranı, sonradan eklenen menülerin temaya uymaması ve SSL'siz bağlantının olmaması.

Yapılanlar:
- Ham userId/form çubuğu kaldırıldı; küçük hesap düğmesi ve temalı yönetim paneli.
- İşlem seçimi: posta aktarımı / dosya dönüşümü / arşiv-bölme / kurtarma. Yalnız seçilen akış gösterilir.
- Yinelenen aktarım yönü ve üretimde örnek mod kontrolleri kaldırıldı; gelişmiş filtre/açıklamalar açılır, temalı kontroller oldu.
- Gerçek kimlik kataloğu App/dizin/aktarım ekranlarına bağlandı. Sahte `comp-Date.now` türü demo kimlikleriyle gerçek hesap oluşturma engellendi.
- Müşteri/proje oluşturma gerçek API'ye bağlandı; mevcut müşteriye proje ekleme eklendi.
- Üretimde demo kaynak/iş sayaçları ve demo reset yolu ayrıldı. Boş katalogda sahte proje adı kaldırıldı.
- SSL/TLS, STARTTLS ve açık seçimli şifresiz IMAP/POP; port varsayılanları ve sunucu değişiminde onay sıfırlama.
- Dar Windows penceresinde nav kırpılması ve eksik `.btn-orange` tema karşılığı düzeltildi.

Tamamlandı ifadesi bu değişikliklerin kod/paket/test kabulüdür; yeni sürümün gerçek kullanıcı kurulumuna geçişi bekliyor. Gerçek müşteri posta sunucusunda yeni şifresiz bağlantı test edilmedi.

## 10. Test kanıtı ve bilinen kapasite

En son kayıtlar (21 Eylül):
- **892/892 motor testi**: `root043-backend-final.trx`.
- **151/151 arayüz testi**; typecheck/lint/build PASS.
- Gerçek production-security API ile ilk yönetici/giriş, mevcut şirkete proje ekleme, operatörün yetkili projeye IMAP hesabı kaydetmesi.
- Yerel gerçek socket testleri: IMAP şifresiz açık seçimle bağlanır; onaysız bağlanmaz; STARTTLS olmayan sunucuda şifresiz devam etmez. POP 12 ileti ve 4 ek kaynakta silinmeden, hash karşılaştırmasıyla indirildi.
- Masaüstü 1440×900 ve telefon 390×844; bağımsız kontrol 1263×900/390×900: boş ekran/hata overlay/konsol hatası/belge yatay taşması yok.
- Ayrı kurulmuş sentetik profilde gerçek Windows WebView2 açılışı, yönetici kurulumu/reload ve ekran görüntüsü doğrulandı.
- Paket manifesti 1112 payload dosyası; SHA256SUMS toplam 1118 dosya; uyumsuzluk yok. Hash yayıncı imzası değildir.
- Kanıt klasörü: `.codex-coordination/evidence/TASK-043/`.
- PNG'ler Temp altında olabilir; kalıcı olduklarını varsayma. `sol043-final.json` konumlarını gösterir. Bazı erken ekran görüntüleri son düzeltmeden öncedir; final kanıtını seç.

Önceki ölçek kabulü: yaklaşık 1 GiB, 8192 ileti/2464 ek. İçe aktarım 35dk22sn, MBOX 21dk03sn, EML 12dk24sn, arşiv 1dk38sn; ölçülen tepe süreç belleği yaklaşık 3.37 GB. Bunlar tek yerel veri seti ölçümleri, genel hız/bellek garantisi veya 100 GB PST/OST kabulü değil.

Kurtarma kontrollü küçük hasar kopyalarıyla doğrulandı. Yerinde tamir, silinmiş blok tarama/undelete vaadi yok; `healthy_extraction`, `partial_recovered`, `unreadable_source`, `cancelled`, `failed` farklı sonuçlar.

## 11. Aşamaların gerçek durumu

| Aşama | Durum |
|---|---|
| 1 — İş deneyimi | TASK021 tamamlandı; ilerleme/son doğrulama ve desteklenen devam |
| 2 — Performans/kuyruk | TASK022–029 yerel kabul; 1 GiB ölçüm ve bütünlük, büyük ölçek açık |
| 3 — Servis bağlantıları | TASK031 OAuth/sağlayıcı altyapısı yerel kabul; canlı sağlayıcı kabulü açık |
| 4 — Formatlar | Mac normalizasyonu, Outlook→EML, POP ve uyarı aktarımı yerel kabul; format/date sınırları mevcut |
| 5 — Üretim SDK/ölçek | Lisans doğrulama altyapısı var; ticari lisans ve temsilî büyük dosya kabulü açık |
| 6 — Kurtarma | Kontrollü hasar/işçi/rapor yerel kabul; gerçek hasarlı geniş corpus açık |
| 7 — Gelişmiş yönetim | TASK037 ailesi; VE/VEYA filtre, eşleme, şablon, seçili arşiv çıktısı, öncelik/yinelenme yerel kabul |
| 8 — Kurumsal işletim | TASK038/039; yerel yetki, audit, yedek/restore, saklama önizlemesi, teslim raporu yerel kabul |
| 9 — Windows ürünü | Kabuk/kurulum/update/rollback/uninstall yerel kabul; imza ve temiz Windows kapıları açık; 0.9.3 kullanıcı update bekliyor |
| 10 — Pilot/ticari 1.0 | Özellikle ertelendi. Azure ve gerçek kişisel Outlook silmesiz pilotu, diğer canlı sağlayıcılar, BT firması pilotları, lisans/fiyat/marka/yayın kabulü |

Üretim SDK lisansı yok; kullanıcı deneme çıktılarıyla geliştirmeyi onayladı. Filigran kaldırma/deneme sınırı aşma yok. Kod imzalama sertifikası da yok; self-signed ile ticari imzalı sayma. Temiz Windows kabulü yapılmadı; geliştirme bilgisayarında repo dışında test etmek bunun yerine geçmez.

## 12. Çalıştırma ve doğrulama rehberi

PowerShell, proje kökünden. Bu komutlar rehberdir; devir belgesi hazırlanırken çalıştırılmadılar.

```powershell
# Motor testleri; çalışan normal build çıktısını kilitlememek için ayrı artifacts dizini
& ./.tools/dotnet/dotnet.exe test engine/BitigMail.Engine.Tests/BitigMail.Engine.Tests.csproj --artifacts-path .codex-coordination/build/claude-check --verbosity minimal

# Arayüz (prototype klasöründe)
Set-Location prototype
npm ci                 # yalnız bağımlılıklar yoksa veya kilitli temiz kurulum gerekiyorsa
npm run typecheck
npm run lint
npm test
npm run build
npm run dev -- --host 127.0.0.1 --port 5173 --strictPort
```

Production kimlik ekranı için Vite + boş LocalHost'u başlatmak ilk yönetici kanıtı üretmez. Normal kullanıcı deneyimini paketli masaüstü ile, E2E'yi TestingHost ve ilgili testin güvenli bootstrap yöntemiyle doğrula. Güvenliği kapatarak giriş hatasını çözme.

- Geliştirme yardımcıları: `scripts/start-local-engine.ps1`, `start-testing-engine.ps1`; durdurucuları kullanmadan sahipliği ve aktif işi kontrol et. Eski README'deki `runtime/local-engine` PID yolu gerçek production profilinin konumu değildir.
- TestingHost production-security testi: `prototype/tests/e2e/task038-production-identity.spec.ts`. Test bazı test profillerini yeniden oluşturur; production kullanıcı profiline yönlendirme.
- Playwright: `prototype/playwright.config.ts`; 5173, desktop/mobile projeleri. Bütün E2E'yi körlemesine çalıştırma: bazıları özel fixture/host ister. İlgili testi ve hazırlığını oku.
- Kaynak kod build: `scripts/build-desktop.ps1`.
- Sürümlü paket: `scripts/build-windows-release.ps1 -Version 0.9.3` (sonraki değişiklikte uygun yeni sürüm seç).
- Build betikleri hedef artifacts dizinlerini yeniden oluşturur. Çalışan/incelemeye alınmış paketin üstüne bilinçsizce build yapma; kaynak ve profil dizinlerini silme.
- Native screenshot/test seçenekleri Desktop/Program.cs içinde. Sentetik ilk yönetici testi yalnız açık Temp profili + capture ile kullanılmalı; gerçek kullanıcı profiline uygulama.

### 0.9.3 gerçek kuruluma geçiş

Önce kullanıcı uygulamasının normal çıkışını ve profil kilidinin bırakılmasını doğrula. `X` pencereyi tepsiye alabilir; tepsi → Çıkış. Sırları/kimlik kataloğunu sıfırlama, kullanıcıyı yeniden oluşturma.

```powershell
# Kök dizinden, uygulama normal kapandıktan ve paket doğrulandıktan sonra
& './artifacts/BitigMail-Internal-0.9.3/BitigMail.Setup.exe' update --package './artifacts/BitigMail-Internal-0.9.3' --allow-unsigned-internal
& "$env:LOCALAPPDATA/Programs/BitigMail/BitigMail.Setup.exe" launch
```

Bu ortamda launcher'ı `Start-Process -Wait` ile beklemek çocuk uygulama yüzünden asılı kalabilir. Gerekirse `-PassThru` ile yalnız launcher sürecinin `WaitForExit` sonucunu izle; açılan kullanıcı uygulamasını kapatma. BitigMail'in güncelleme kilidini kaldırmak için rastgele process kill kullanma.

## 13. Kullanıcı verisi ve korunacak yollar

- Kurulum: `%LOCALAPPDATA%\Programs\BitigMail`
- Etkin sürüm: `active-version.txt`; paketler `versions\<version>` altında.
- Masaüstü veri profili: `%LOCALAPPDATA%\BitigMail\desktop`
- Doğrudan LocalHost varsayılan profili: `%LOCALAPPDATA%\BitigMail\production` (kodun --profile seçimi değiştirir).
- WebView2 verisi: `%LOCALAPPDATA%\BitigMail\WebView2`
- Geliştirme/test verileri: repo `runtime/` ve açıkça ayrılmış Temp fixture dizinleri.
- Gerçek OST girdileri: `lab/ost-spike/input/`; kullanıcı kaynaklarını değiştirme. İlk örnekte yalnız Gelen Kutusu vardı; yeniden eşitleme sonrası tam örnek istendi. Klasör sayısını orijinal beklentiyle karıştırma.
- `lab/ost-spike/local-credentials.json` gibi yerel sır dosyalarını devir dosyasına, prompta veya loga kopyalama. Parola/token/lisans içeriği isteme veya yayımlama.
- Yedek/restore mevcut arşivin üzerine yazmaz. Kaldırma uygulama dosyalarıyla veriyi ayırır; veri korunur.
- Profil şeması/kilit/receipt/manifest doğrulamasını elle dosya silerek aşma. Gelecek şema veya bilinmeyen dolu profil otomatik kabul edilmez.
- Daha önce güvenlik incelemesince reddedilen eski PID17200 sonlandırmasını tekrar denememe notu vardı. Bu PID bugün aynı süreç demek değildir; genel kural mevcut sahipliği doğrulamak ve yabancı süreci öldürmemektir.

## 14. Claude için önerilen devam sırası

1. Bu dosya + TASK043 sonucu + güncel kodu oku. Kullanıcı artık hangi işi istediğini belirtmişse onu öne al; eski kesintisiz geliştirme talimatını her şeyi yeniden başlatma izni sanma.
2. Kaldığımız somut teslim işi: 0.9.3'ü mevcut profili koruyarak kurup açmak. Eski uygulama hâlâ açıksa normal çıkış iste; hazır paket yeniden yazılmak zorunda değil.
3. Kullanıcıyla yeni görünüm revizyonlarını değerlendir. Gerekirse gerçek hesap bağlama testini kullanıcının belirttiği sunucuyla yap; henüz belirli şifresiz sunucu yok, sadece seçeneğin bulunması istendi.
4. Bir sonraki sürümde yalnız ilgili akışı değiştir, hedefli testleri ve gerekiyorsa gerçek API/native görünümü doğrula. Kaynak/hedef veri bütünlüğü ve yetki testlerini koru.
5. 9. aşama dış kapılarını açık tut: codesigning ve temiz Windows. Lisans ve büyük/bozuk corpus kabulü ayrı planlanmalı.
6. Kullanıcı 10. aşamayı başlattığında Azure uygulama kaydı ve gerçek kişisel Outlook silmesiz pilotunu hatırlat. Önceki Entra/Azure hesap açma problemi tamamlanmadı; varmış gibi davranma. Sonra Google/kurumsal pilotlar ve 1.0 kabulü.
7. Her iş sonunda kısa sonuç, test kanıtı, sınırlama ve bir sonraki adımı kalıcı dosyaya yaz. Eski tarihli durumu sessizce güncel başarı gibi sunma.

## 15. Ayrıntı gerektiğinde okuma haritası

| Konu | Başlangıç belgeleri |
|---|---|
| Ürün/kimlik | `docs/PRODUCT_BRIEF.md`, `PRODUCT_BLUEPRINT.md`, `BRAND_IDENTITY.md`, `VISUAL_DESIGN_V1.md` |
| Dönüşüm/SDK | `OST_ENGINE_DECISION.md`, `MIME_IMPORT_WORKFLOW.md`, `PST_SPLIT_WORKFLOW.md`, `SDK_LICENSE_AND_SCALE_ACCEPTANCE.md` |
| IMAP/köprü | `IMAP_TRANSFER_WORKFLOW.md`, `FILE_ACCOUNT_BRIDGE_WORKFLOW.md` ve ilgili VALIDATION dosyaları |
| Mac/POP | `FORMAT_DIRECTION_MATRIX.md`, `OUTLOOK_TO_EML_WORKFLOW.md`, `EMLX_NORMALIZATION_WORKFLOW.md`, `POP_SOURCE_TO_EML_WORKFLOW.md` |
| Arşiv/arama | `LOCAL_ARCHIVE_SEARCH_WORKFLOW.md`, `LOCAL_ARCHIVE_SEARCH_VALIDATION.md` (ilk taslakları son kabulden ayır) |
| Kurtarma | `DAMAGED_STORE_RECOVERY.md`, `DAMAGED_STORE_RECOVERY_VALIDATION.md` |
| Filtre/yönetim | `STAGE7_ADVANCED_MANAGEMENT_ACCEPTANCE.md` |
| Güvenlik/yedek | `STAGE8_ACCEPTANCE.md`, `STAGE8_SECURITY_VERIFICATION.md`, `STAGE8_AUTHORIZATION_BOUNDARIES.md` |
| Ölçek/kuyruk | `SCALE_AND_RESILIENCE_VALIDATION.md`, `STAGE2_QUEUE_ACCEPTANCE.md`, `DISK_CAPACITY_SCOPE.md` |
| Windows | `WINDOWS_RELEASE_ACCEPTANCE.md`, `WINDOWS_QUICK_START_TR.md`, `STAGES_5_9_ARCHITECTURE.md` |
| Azure/Outlook | `MICROSOFT365_SETUP.md`, `MICROSOFT365_LIVE_PILOT.md`, `OUTLOOK_PERSONAL_WORKFLOW.md` |
| Google | `GOOGLE_CONNECTION_WORKFLOW.md` |

Bu tabloda ilk satır dışındaki kısa dosya adları da `docs/` altındadır.

## 16. Önceki ajan düzeni hakkında

Eski kayıtlar Astra yönetici → mevcut SOL görevi → Antigravity/Gemini zincirini anlatır. Antigravity kullanılamadığı için `.codex-coordination/EXECUTION_OVERRIDE.md` ile SOL DIRECT uygulanmış; kritik mimari/güvenlik/veri bütünlüğü Astra tarafından yapılmıştı. Son işlerin Gemini tarafından yapıldığı söylenmemeli. Eski ajan/task kimliklerine erişim Claude için önkoşul değildir. Kullanıcı Gemini'ye bilgisayar kontrolüyle yazı yazılmasını istememiş, arka planda terminal kullanımını tercih etmişti.

Bu belge bir proje devir özeti ve mevcut tercihler kaydıdır; Claude'un kendi araçlarını, üst düzey talimatlarını veya yeni kullanıcı talimatlarını geçersiz kılan bir sistem promptu değildir. Kullanıcı onayı olmadan yeni ücretli servis/abonelik, dış paylaşım veya ticari yayın başlatma. Mevcut geliştirmeyi sıfırdan yazmak yerine çalışan mimariyi ve kanıtlanmış sınırları koru.
