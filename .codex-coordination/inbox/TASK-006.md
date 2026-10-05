TASK_ID: TASK-006
STATUS: DONE — research only; Astra-corrected candidate note accepted, real OST/PST LAB NOT RUN
FROM: ASTRA HIGH
TO: SOL 5.6 LIMITED

GOAL:
Kullanıcı bağımsız/yetim OST dosyasını eski hesap/orijinal Outlook profili olmadan PST'ye dönüştürmeyi kesin doğruladı. P2 başlangıcında mevcut docs/FORMAT_FEASIBILITY.md üzerinden hedefli resmi kaynak güncellemesi ve somut küçük dosya deneyi planı hazırla. Çıktı docs/OST_ENGINE_DECISION.md. Bu görev araştırma/deney tasarımıdır; motor implementasyonu veya gerçek veri işleme değildir.

BOUNDED SCOPE:
- Eski dokümandaki Aspose.Email modern OST (2013+) kısıtını güncel resmi belgede teyit et; eski sürüm/sayfa/belirsizlik ayrımını açık tut.
- libpff bağımsız okuma/4K modern OST, bozuk veri taraması, Windows dağıtımı ve read-only sınırını resmi repo/docs ile teyit et. 'Dosya okunur' beyanı belirli dosyada bütün iletiler kurtarılır sonucuna dönüşmesin.
- Somut PST yazıcısı: Aspose.Email Unicode PST yazma API'si, değerlendirme sınırları ve ticari son kullanıcıya dağıtım/OEM lisansının teyit edilmesi gereken koşulları. Okuyucu libpff + yazıcı SDK bileşimi ancak deney adayı olarak değerlendirilsin; uyumluluk kanıtı denmesin.
- Gerekliyse yalnız bir ek headless SDK adayı araştır; GUI-only converter veya CLI yokken ekran otomasyonunu motor diye önerme. Kısa listeyi en fazla 3 yaklaşımla sınırla.
- Adayların modern OST okuma, eski profil ihtiyacı, yerel Outlook kurulum ihtiyacı, PST yazma, kurtarma, lisans/eval, Windows dağıtımı ve 100GB ölçülmüş/ölçülmemiş durumunu kompakt tabloda göster. Güncel fiyatı kanıtsız uydurma; gereksiz satın alma araştırması yapma.
- Somut deney: önce küçük bilinen içerikli sağlıklı OST, yeni Outlook formatı örneği, orijinal hesabı olmayan ortam; çıkarılan özgün MIME/MAPI alanları, ek hash'leri, klasörler/tarihler ve yeni PST'den bağımsız tekrar okuma kontrolü. Kaynak hash before/after, kayıp/bozuk öğe raporu, cancel/resume/checkpoint, ölçülen RAM/geçici disk ve başarısızlık kriterleri. 100GB ilk deneme değil; küçük başarıdan sonra kademeli ölçüm. MIME'e indirgenince kaybolabilecek MAPI alanlarını açık risk olarak kaydet.
- Test dosyaları sağlanmadıysa LAB: NOT RUN; synthetic EML üretip OST kanıtı diye sunma. Kullanıcıdan gerekecek dosya/izin bilgisini en fazla 2 somut maddeyle yaz; müşterinin gerçek verisi yerine yapay içerikli Outlook'ta üretilmiş OST tercih edilir.

WORKFLOW / CONSTRAINTS:
Mevcut resmi agy CLI headless Gemini; yeni Codex görevi/ajanı yok, masaüstü/browser UI otomasyonu yok. SOL doğrudan resmi kaynakları okuyabilir ve Gemini'ye doğrulanmış içerik sağlayabilir; agy shell reddi veya web aracı yokluğu araştırmayı tamamen durdurmasın. İlgili live-web becerilerini kullan; sadece resmi primary sources. Belirsiz satıcı beyanlarını kesin destek diye yazma. Toplam 4–8 temel resmi kaynak çoğu durumda yeterli; bütün pazarı tarama veya aynı işi bağımsız iki modele analiz ettirme.
prototype/ ve ROADMAP/PRODUCT_BRIEF değiştirme. docs/OST_ENGINE_DECISION.md ve yerel coordination sonuçları edit kapsamı. Kurulum, VM oluşturma, SDK indirme/satın alma, mail hesabı erişimi, gerçek dosya arama veya dışarı dosya yükleme yok. Shell -p değişkenini ayrı argüman ver; önceki literal-placeholder hatasını tekrarlama.

DELIVERY / ACCEPTANCE:
Kaynak tarih/linkleriyle kompakt karar notu; önerilen ilk deney adayı ve nedenleri, belirsizlikler, go/no-go kriterleri, sonraki uygulanabilir görev. Karar statüsü aday olmalı; gerçek deney olmadan production engine seçildi denmesin. Sonuç .codex-coordination/results/TASK-006.md. Kaynak erişimi engellenirse kısmi kanıtı ve eksik noktayı dürüstçe bildir; sonsuz araç tekrarına girme. Derleme/test bu doküman görevine uygulanmaz; sahte PASS yazma. Astra wait_threads ile izleyecek.
