# Aşama 7 — Yerel kabul, 20 Eylül 2026

Gelişmiş yönetim aşamasının belirtilen yerel kapsamı kabul edildi. Gerçek sağlayıcı hesabı ve ticari SDK lisansı kabulü değildir.

- Ortak sürümlü VE/VEYA kuralları: konu, tam gövde, gönderen/alıcı adresi, ek adı/varlığı, boyut ve açık saat dilimli özgün MIME tarihi. Önizleme, kalıcı plan ve çalıştırma aynı kuralları kullanır. Bilinmeyen metadata ayrı sayılır.
- EML/mboxrd→PST, EML/mboxrd↔IMAP ve IMAP→IMAP yollarında gerçek filtre uygulaması; arşiv aramasında ham içerik doğrulaması filtreleme/sayfalama öncesindedir.
- Arşiv sonuçlarından seçili yeni EML işi; çok arşivde aynı öğe kimliği ayrılır, klasörler açıkça eşlenir. Kaynak/sorgu/metadata değişirse iş yayımlanmaz. Kaynağın tarih ve kısmi kurtarma uyarıları sonraki işlemlere taşınır.
- PST oluştururken fiziksel iletileri koruma, yalnız aynı içerik veya içerik+mevcut metadata ile tekilleştirme seçenekleri uygulanır. İlk fiziksel sıra kazananı belirler; atlanan kimlikler raporlanır. Kaynak dosyalar değişmez.
- Türkçe filtre ekranı, kaydet/uygula şablonları ve yeniden önizleme; yalnız bekleyen işlerde öncelik, eşit öncelikte sıra korunması.

## Kanıt

SOL sonuçları: TASK-037B.md ve TASK-037C.md. Genel Release motor testi 725/725; arayüz 151/151; typecheck/lint/build başarılı. Gerçek TestingHost tarayıcı akışları: filtre/eşleme/tekilleştirme/şablon/PST 1/1, seçili arşiv çıktısı 1/1, öncelik 1/1, POP 2/2.

Root odak kontrolleri: IMAP/Bridge 125/125; MIME 75/75; MIME istek/politika 16/16; son arşiv bütünlük sınırları 4/4. Son dört test genel 725 testlik koşudan sonra eklenip ayrıca çalıştırıldı; 729 testlik yeni genel koşu iddia edilmiyor. Gerçek PST yeniden açılarak 3/1/2 politika sonuçları, klasörler ve ekler kontrol edildi. EML/mbox çıktısı yeniden açıldı ve baytları karşılaştırıldı.

## Açık sınırlar

- Ekran düzenleyicisi tek düzey VE/VEYA oluşturur. İç içe AST/şablon motor tarafından desteklenir; ekran bu kuralları sessizce düzleştirmez, açık temizleme yapılana kadar düzenlemeyi engeller.
- POP için önce doğrulanmış EML anlık görüntüsü alınır, ardından desteklenen yol üzerinden filtrelenir. Doğrudan POP indirme-öncesi tam gövde filtresi yoktur.
- PST/OST/OLM/EMLX için gelişmiş kurallar gerektiğinde nitelikli EML normalleştirmesi üzerinden kullanılır. OLM/kurtarma tarih belirsizliği ortadan kalkmış sayılmaz.
- İlk ticari kapsam yalnız maildir; kişi/takvim/görev aktarımı vaat edilmez.
- 64 MiB tek ileti sınırı ve mevcut kaynak/SDK sınırları sürer; 100 GB ölçek veya dış sağlayıcı kabulü değildir.
- Şirket/proje seçimi henüz gerçek kullanıcı yetkilendirmesi değildi. Bu sınır, sonraki TASK038'in konusudur.

Normal6174 önizleme motoru korunmuştur; yeni özellikler ayrı6175 üzerinde doğrulanmıştır. Aşama8 aktif, Aşama9 henüz tamamlanmadı. Azure ve gerçek Outlook pilotu10. aşamada.
