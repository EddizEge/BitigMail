# Aşama 2 — disk alanı kontrolü kapsamı

TASK-023: IMAP -> EML/mboxrd için TAMAMLANDI ve kabul edildi (2026-09-14). Backend454/454, aktarım ekranı21/21 test PASS; typecheck/lint/build PASS. Gerçek 12 iletilik EML aktarımı eksiksiz; kaynak12/4 korunmuştur. Kanıt: .codex-coordination/results/TASK-023.md.

- Önizlemede yalnız seçilen iletilerin gerçek ham boyutundan planlama tahmini.
- MBOX için staging ve son çıktı birlikte hesaba katılır; EML için yeni dosya ve metadata payı.
- Önizleme ve çalıştırma başlangıcında seçilen dizinin kullanıcıya kullanılabilir alanı sorgulanır.
- Sorgu başarısızlığı "0 bayt" olarak sunulmaz. Yeni planlarda açıklayıcı engel gösterilir.
- Bu bir alan rezervasyonu değildir; başka uygulamalar daha sonra diski doldurabilir.
- Eski tahminsiz planların kabul edilmiş devam davranışı korunur; bunlara yeni kontrol yapılmış gibi davranılmaz.

TASK-024: Kalan dört yerel çıktı yolu için kapasite kontrolü eklendi (2026-09-15).

- OST -> PST ve EML/mboxrd -> PST: değişmez kaynağın tamamı boyut temeli alınır. Planlama payı `4 × kaynak + 64 KiB/öğe + 256 MiB` değeridir; PST genişlemesi ile geçici/son çıktı birlikteliğini muhafazakâr biçimde kapsar.
- PST/OST bölümleme: `4 × kaynak + 64 KiB/seçili öğe + 1 MiB/tahmini parça + 256 MiB`. Yıl modunda planlanan yıl grupları; boyut modunda `ceil((2 × kaynak)/parça sınırı)` kullanılır. Tahmini parça sayısı kesin sonuç değildir.
- Yönetilen arşiv: mevcut dağıtımda ham arşiv, staging, SQLite indeks ve WAL aynı `runtime/archives` tabanındadır. Aynı hacimde birleşik planlama payı `4 × ham boyut + 64 KiB/öğe + 256 MiB` olarak bir kez sorgulanır. Beklenmedik ayrı dizin yapılandırması güvenli biçimde reddedilir.
- Kapasite sorguları ortak `Engine.Storage` Windows probunu kullanır; UNC/mounted yollar için gerçek hedef veya en yakın mevcut üst dizin sorgulanır. Sorgu hatası sıfır alan ya da yeterli alan sayılmaz.
- Bunlar alan rezervasyonu veya kanıtlanmış azami çıktı boyutu değildir. Eşzamanlı disk tüketimi ve SDK çıktı genişlemesi çalışma sırasında yine hata doğurabilir; mevcut çalışma zamanı hata yolları korunur.

Güncel kapsam:

| Yol | Mevcut bulgu | Kalan iş |
| --- | --- | --- |
| OST -> PST | TASK-024 kapsamında ortak prob, bütün-kaynak tahmini ve ilk yazımdan önce yürütme kontrolü var | Gerçek lisanslı üretim SDK çıktı büyümesi ileri ölçüm konusu |
| PST/OST bölme | TASK-024 kapsamında toplam parçalar/geçici çıktı payı ve ilk yazımdan önce kontrol var | Tahmini parça sayısı gerçek SDK genişlemesine göre farklılaşabilir |
| EML/MBOX -> PST | TASK-024 kapsamında bütün-kaynak tahmini ve `.partial` oluşturulmadan önce kontrol var | Gerçek lisanslı üretim SDK çıktı büyümesi ileri ölçüm konusu |
| Yönetilen arşiv | TASK-024 kapsamında aynı hacimde raw+staging+SQLite/WAL birleşik kontrolü var | Ayrı arşiv/indeks hedefi desteklenmez; ileride ayrı tasarım gerekir |
| IMAP/hedef servis | Yerel disk kontrolü uzak sunucunun kotasını temsil etmez | Sağlayıcı kota hataları ve varsa güvenilir kota sorguları ayrı kabul |

Azure/gerçek Outlook: Aşama 3. Zamanlayıcı yok.
