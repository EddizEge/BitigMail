# Gerçek yerel arşiv ve arama — kabul kaydı

2026-09-14, TASK-018. DONE — Backend ve gerçek masaüstü/mobil arayüz kabul edildi. Önceki ara kayıt `evidence/TASK-018/validation-history-0409.md` içinde korunur.

## Kapsam

Yönetilen EML/mboxrd ve tamamlanmış IMAP dışa aktarım arşivleri; değişmez şirket/proje sahipliği; birden fazla şirket/proje/arşivde arama; konu/gövde/gönderen/alıcı/ek adı, klasör, ek durumu ve Türkiye takvim tarihi filtreleri; güvenli düz metin önizleme; kesintiden devam ve yeniden indeksleme.

## Bağımsız gerçek motor kanıtı

Tüm yollar `.codex-coordination/evidence/TASK-018/` altındadır.

| Deney | Sonuç | Kanıt |
|---|---|---|
| Gerçek HTTP/API, dört arşiv40 fiziksel ileti | 649/649 PASS | api-f952de49c9/report.json |
| Süreç yeniden başlatma, kayıp/bozuk indeks onarımı | 83/83 PASS | api-18a523d2d7/recovery-report.json |
| EML yazımı2/72 iken gerçek süreç kesintisi ve aynı işe devam | 203/203 PASS | api-8ce9f2e3dc/crash-report.json |
| MBOX yazımı2/12 iken gerçek süreç kesintisi ve aynı işe devam | 75/75 PASS | api-84170fe6c1/crash-report.json |
| Root arama/kapsam politikası | 38/38 PASS, tam motora dahil | root-policy-checkpoint.json |

Kaynak ve yönetilen ham MIME SHA256 fiziksel çoklukları, duplicate Message-ID/hash kopyaları, metadata/sahiplik, birleşik kapsam ve sayfalama kimlikleri doğrulandı. Her sonuç kendi arşivindeki iletiyi açıyor; önizleme güncel tam kapsam ve filtreleri sunucuda doğruluyor. Yönetilen raw değiştirilirse önizleme kontrollü reddediliyor. IMAP INTERNALDATE, özgün MIME tarihinden ayrı korunuyor. HTML-only gövde metin olarak ayrıştırılıyor; script/style/head ve ek içerikleri aranabilir ana gövdeye karışmıyor.

## Arayüz ve derleme

- Normal Release motor testleri434/434; arşiv alt grubu65/65. Son test uyarısı düzeltmesi11/11 ve0derleme uyarısı; üretim motoru değişmedi.
- Arayüz129/129,9dosya. Typecheck/lint/build geçti. Bazı birim testlerinde React act uyarıları ve yaklaşık631kB üretim paketi boyut uyarısı var; gerçek tarayıcıda uygulama hatası gözlenmedi.
- Gerçek1660px ve390px: şirket ağacı/ilk kapama, üç durumlu çoklu seçim, gerçek manifest klasörleri, tarih/ek/metin filtreleri, doğru sahipli önizleme, seçim değişince eski önizlemenin temizlenmesi, zararsız HTML metni ve sıfır uzak içerik isteği; gerçek dosya seçici önizleme/başlatma/tamamlanma ve otomatik katalog görünürlüğü; İş Merkezi üzerinden kayıtlı arşiv bağlamını açma geçti.
- İş Merkezi ve aktarım ekranından tamamlanmış MBOX12 dışa aktarımını tek tıkla arşive alma iki boyutta geçti. Kaynak iş kimliği yazmak gerekmiyor; şirket/proje bağlamı kilitli.
- Görsel kontrolde mobil sorgu boşluğu giderildi, sayfa genişliği390/1660 sınırında kaldı. Alt durum çubuğu gerçek seçili arşiv sayısını gösteriyor; ek gerçek tarayıcı kontrolü geçti. Son UI kanıtı: ui-final/report.json; aktarım girişleri: ui-bridge-final/report.json.

## Sınırlar ve açık aşamalar

Ham ileti64MiB; indekslenen/önizlenen gövde512KiB UTF-8 ve Unicode karakter sınırı. Kesilen gövde açık uyarıyla gösterilir, ham ileti korunur. Arşiv MBOX okuyucusunda ek16MiB satır sınırı vardır. Bu sınırlar eski parametresiz MIME okuyucularına yayılmaz. Arama512karakter/32terim/100arşiv, sayfa en fazla100ileti. Yerel kapsam kontrolü kurumsal kullanıcı/rol yetkilendirmesi değildir.

100GB PST/OST, bozuk PST kurtarma,1GiB ölçek ve canlı Microsoft/Google pilotu bu küçük sentetik testlerle kabul edilmiş sayılmaz. Aspose değerlendirme sınırları ve işaretleri devam eder.

TASK-019 kademeli128MiB ve ardından koşullu1GiB ölçek/dayanıklılık deneyidir; TASK-018 kabulünden sonra başlar. Ardından Azure uygulama kaydı ve özgün iletileri silmeden kişisel Outlook pilotu sohbet içinde hatırlatılır. Kullanıcının isteğiyle zamanlayıcı yoktur; mevcut otomasyon PAUSED.

Uygulama: Gemini; denetim: SOL. Root arama/güvenlik/veri bütünlüğü politikası, bağımsız kabul ve tekrarlayan AGY araç hatalarından sonra dar arayüz düzeltmesi: Astra. UI escalation kaydı `results/TASK-018-ui-escalation.md`. Başarısız denemeler kanıtta korunur; başarılı deneme gibi sunulmaz.

