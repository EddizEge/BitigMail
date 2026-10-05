# 8. aşama — Yerel kurumsal işletim kabulü

21 Eylül 2026. TASK038 ve TASK039 yerel kabulü tamamlandı. Sıradaki çalışma 9. aşamadaki Windows uygulaması ve kurulum paketidir.

## Tamamlananlar

- İlk yönetici kurulumu, yerel kullanıcılar, yönetici/operatör rolleri ve şirket/proje izinleri.
- Oturum ve işlem sahipliği; kaynak, hedef ve arşiv kapsamlarının sunucuda denetlenmesi.
- İçerik ve parola taşımayan işlem günlüğü; bozulma ve eksik kayıt denetimi.
- Birden fazla şirketin arşivlerini tek pakette yedekleme; bütün paketi doğruladıktan sonra yeni arşiv kimlikleriyle seçilen şirkete geri yükleme.
- Mevcut arşivlerin korunması, kesinti sonrası aynı kimlikle onarım ve yeniden indeksleme.
- Hiçbir iletiyi veya arşivi silmeyen saklama önizlemesi; paylaşılabilir müşteri teslim raporu.

## Kanıtlar

- Son genel motor testi: **852/852 başarılı**. Arayüz: **151/151 başarılı**; tür kontrolü, lint ve üretim derlemesi başarılı.
- Gerçek arayüz akışı: 12 iletilik arşiv oluşturma, yedek alma, boş hedef şirkete geri yükleme ve hedef kayıt sayısını doğrulama. Operatör için yönetici işlemleri HTTP 404 ile reddedildi.
- İki şirketin arşivleri üçüncü şirkete özgün baytları korunarak geri yüklendi. Fazladan dosya, bozulmuş içerik, yinelenen veya eksik manifest alanları reddedildi.
- İkinci arşiv bozuksa ilk arşiv de yayımlanmaz. Tam doğrulanmamış paketin onarım yolu bu kuralı aşamaz.
- Yayımlama sonrasında işlem gerçekten sonlandırıldı; iki ayrı yeni süreç aynı arşivi ikinci kopya üretmeden onardı.
- Rapor özel dosya yollarını, serbest hata metinlerini ve hassas uyarı içeriklerini dışarı vermedi. Saklama önizlemesinden sonra arşiv dosyalarının baytları değişmedi.

Test kümeleri örtüşür; hedefli test sayıları genel toplama ayrıca eklenmez. Ayrıntılar `.codex-coordination/results/TASK-039.md`, motor `TestResults/task039-acceptance-final.trx` ve `.codex-coordination/evidence/STAGE8/` altındadır.

## Sınırlar

Denetim zinciri yerel dosya tutarlılığını kontrol eder; hem kayıtları hem zincir başlığını değiştirebilen bir yöneticiye karşı değişmezlik sağlamaz. Yedek özetleri yayıncı kimliğini doğrulamaz. Hesap parolaları ve cihaz bağlı sırlar yedeğe dahil değildir; başka cihazda hesap bağlantıları yeniden kurulur.

Saklama işlevi yalnız önizlemedir; otomatik silme ve hukuki koruma politikası uygulanmaz. Gerçek 100 GB veri, lisanslı SDK çıktısı ve harici sağlayıcı kabulü bu aşamanın testleri değildir. Gerçek süreç sonlandırma denemesi fiziksel elektrik kesintisi veya disk denetleyicisi dayanıklılığı iddiası değildir.

Windows kurulum ve yaşam döngüsü 9. aşamada; Azure ve gerçek Outlook testi 10. aşamadadır.
