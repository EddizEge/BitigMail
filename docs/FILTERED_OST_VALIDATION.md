# Filtreli OST dönüşümü — doğrulama kaydı

2026-09-12, TASK-011. Status: DONE / ASTRA ACCEPTED. Normal uygulama Gemini, deterministik denetim SOL; Astra mimari, kritik kaynak/tarih/ilerleme kabulü, bağımsız API/UI/çıktı doğrulamasını yönetti.

## Beklenen sonuçlar

Kaynak: `lab/ost-spike/input/bitigmail-lab-full.ost`, SHA-256 `B0801758A2E61D4CE6E86799701A81A7A60C38401F73B13C993D94C03A2EE57A`.

Kaynakta 13 fiziksel ileti ve dört ek bulunuyor. Astra salt okunur doğrudan MAPI envanterini `lab/ost-spike/output/task011-astra-source-oracle.json` dosyasına kaydetti. Ayrı libpff kaynak dışa aktarımındaki başlık tarihleri de ana senaryonun aynı üç iletisini seçiyor; uygulamanın önizlemesi oracle olarak kullanılmadı.

| Seçim | Beklenen ileti | Ek | Kapsam dışı |
|---|---:|---:|---:|
| Projeler/İstanbul, 2024-01-01–2024-02-16 dahil | 3 | 1 | 10 |
| Tüm klasörler, 2024 yılı | 8 | 4 | 5 |
| Gelen Kutusu, yalnız 2023-04-10 | 2 fiziksel kopya | 0 | 11 |
| Projeler/İstanbul, yalnız 2024-01-01 | 1 | 1 | 12 |
| Projeler üst klasörünün kendi iletileri | 0 | 0 | 13 |
| Tüm seçim kaldırılmış | 0 | 0 | 13 |
| Tüm klasörler, tarih sınırı yok | 13 | 4 | 0 |

Ana seçimin konuları: İstanbul Saha Raporu - İlk Taslak; İstanbul Saha Raporu - Güncellenmiş İkinci Taslak; İstanbul Projesi Kurumsal Kimlik ve Amblem. İlk iki taslak aynı Message-ID taşır fakat ayrı fiziksel iletilerdir. Görselli ileti UTC'de 2023-12-31 21:30, Türkiye saatinde 2024-01-01 00:30'dur; 2024 seçimine dahil olmalıdır.

## Kabul kapıları

- Üretim seçim kodunun tarih sınırları/DateTime.Kind, boş/geçersiz seçim, kaynak değişmezliği ve idempotency testleri: SOL backend 67/67 PASS. Trial dışındaki tüm kaynak ön kontrol hataları da filtreli işi engeller. Astra ilerleme callback'inin senkron kalmasını geri yükledi.
- Gerçek HTTP önizleme negatif/pozitif kontrolleri: Astra 16/16 PASS, TestingHost6175. Tarih dahil sınırları/açık uçlar, tüm 2024, fiziksel kopyalar, yılbaşı CID, üst klasörün kendi kapsamı, boş seçim, geçersiz tarih/kimlik, yanlış kaynak ve sıfır-ileti başlatma reddi; reddedilen işte PST oluşmadı. Tüm cevaplar no-store. Kanıt: `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task011-qa/api-filter-report.json`.
- Gerçek UI filtreli PST üretimi, yenileme sonrası rapor, masaüstü/dar ekran: Astra üç görünümde PASS (1660×948, 1366×768, 390×844). Her birinde filtreler ekrandan seçildi, 3/1/10 önizleme sonrası yeni PST üretildi, aynı iş kimliği ve 3/3 dönüştürülen/seçilen özeti yenileme sonrası korundu. Tam klasör yolu raporda görünür. JS hatası 0, DOM scrollWidth=viewport. Masaüstünde eski sıfır-sonuç sunucu yanıtı kontrollü 1800ms geciktirildi; yeni üç-iletili seçimi ezmedi. Örnek iş: `job-fccfa980da57`. Kanıt ve görüntüler: `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task011-qa/ui-filter-report.json`, `filters-*.png`, `report-*.png`. Native seçici otomatik kullanılmadı.
- Üretilen filtreli PST'nin bağımsız libpff ile başlık/klasör/ek çoklu-küme ve ortak gövde hash karşılaştırması: PASS. Gerçek UI işi `job-51f339161f01`, PST `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-qa/headless-test-c6921daf9b194c15860d794fd08dba34.pst`, SHA-256 `2b0a515c0e5e0110f97a728dd97014446f8cd09e4a513b836fe731d0af841946`. 13 kaynak / 3 seçili / 10 hariç / 3 çıktı / 1 ek; eksik/fazla çoklu-küme öğesi 0, ortak gövde farkı 0. Çıktıda üç ek düz metin gösterimi var; önceki gövde politikası kapsamında. Kanıt: `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task011-qa/independent-7b62251f/report.json`. Ağsız konteyner, girdi salt okunur, Aspose'dan bağımsız libpff 20180714 okuyucusu.
- Önceki filtresiz dönüşüm ve güvenlik regresyonları; frontend tip/lint/derleme: PASS. SOL son frontend typecheck/lint/build ve 32/32 birim testi; gerçek filtreli HTTP/UI matrisi 6/6 (üç genişlik). Astra son hedefli `Direct HTTP E2E:` koşusu 2/2: eski filtresiz gerçek 13/13 dönüşüm ile yeni filtreli 3/1/10 dönüşüm geçti. Normal hizmet6174 üzerinde bağımsız 20/20 güvenlik kontrolü PASS (session/status token gerektirir, Host/Origin/CORS sırası, no-store, çoklu oturum, test endpoint'i normal hizmette yok); native picker çağrılmadı. Kanıt: `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task011-qa/astra-api-check.json`.

Son kaynak SHA-256 başlangıç değeriyle aynı. Teslimde Vite5173 ve normal LocalHost6174 açık, TestingHost6175 kapalı. Son metin sadeleştirmesi: “Seçim özeti”. Test sırasında düzeltilen geçici fixture/tarih/başlık/mesaj-önceliği hataları son başarılı koşularla kapandı; bütün proje E2E paketi bu görevde yeniden çalıştırıldığı iddia edilmez.

## Sınırlar

Yalnız küçük yapay gerçek OST. Vendor klasör başına 50 öğe değerlendirme engeli, filtre seçimine bakılmaksızın korunmalıdır. Native Windows seçici otomatik kullanılmaz; gerçek HTTP/UI testinde yalnız ayrı test seçicisi kullanılır. Bu çalışma 100 GB, bozuk dosya kurtarma, kesintiden devam, PST bölümleme veya tüm MAPI alanları için doğrulama değildir. Bağımsız libpff dışa aktarımı CID MAPI özelliğini sunmaz; CID ayrıca ürünün MAPI kontrolüyle ölçülür.
