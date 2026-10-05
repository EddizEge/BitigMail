# Aşama 8 kullanıcı ve yetki kabulü — TASK038

21 Eylül 2026. TASK038 ve TASK039 yerel kabulü tamamlandı. Güncel toplam ve kapsam [8. aşama kabul kaydında](STAGE8_ACCEPTANCE.md). Aşağıdaki hedefli test notları çalışma geçmişini gösterir.

## Doğrulanan çekirdekler

- Hesap kataloğu, oturum, kurulum kanıtı ve profil kilidi: 38/38 hedefli test. Sonraki iki eşzamanlı yönetici testi de geçti. Tek ilk yönetici; son yöneticiyi koruma; parola/rol/kapsam değişiminde eski oturumun geçersizliği; bozuk katalogda kapalı davranış; şirket içi proje ayrımı; sınırlı, atomik ve tutarlı katalog okuma/yazma.
- Giriş denemeleri: 4/4. Kullanıcı ve profil çapında gecikme, sınırlı bellek, zaman aşımıyla temizleme, duvar saati değişimine dayanıklılık. Süreç içi korumadır; tek profil/tek üretim süreci masaüstü katmanında tamamlanmalıdır.
- İş başlatma: 11/11 ilgili test. İlk boş kuyruk ve bekleyen iş için güncel kullanıcı/sürüm/tüm kayıtlı kapsam doğrulaması; yetki kaldırılınca çalıştırmama; bozuk katalogda görünür başarısızlık; güvenlik reddi kalıcı yazılamazsa yeni işleri durdurma; yeniden başlatmada durdurma kaydını koruma.
- Microsoft/Google OAuth: 33/33 ilgili test, bunların sekizi yeni oturum sahipliği senaryosu. Başka kullanıcı ve aynı kullanıcının yeni oturumu işlemi okuyamaz/iptal edemez. Çıkış veya hesap yetkisi değişikliği sonrasında geciken sağlayıcı cevabı kalıcı hesap yaratamaz. Yalnız test sağlayıcıları kullanıldı.

Kanıtlar `.codex-coordination/evidence/STAGE8/` içinde: `root038-catalog-security-final.trx`, `root038-admin-races.trx`, `root038-login-throttle.trx`, `root038-dispatch-auth-final.trx`, `root038-oauth-authorization-final.trx`. Test kümeleri örtüşür; sayılar toplanarak genel test sayısı elde edilmez.

## Kapanış doğrulaması ve sonraki iş

- Bütün aktarım/POP/arşiv/kurtarma önizleme ve plan kimlikleri gerçek oluşturan kullanıcı ve oturuma bağlanmalı. İşten devam etme, kalıcı işin kayıtlı sahibini ve kaynak/hedef kapsamlarını kullanmalı.
- Hesap/iş/arşiv/rapor kimliği değiştirme, farklı şirket ve aynı şirket içindeki farklı kullanıcı, çok kaynaklı karışık yetki, oturum değişimi ve silinmiş yetki negatif matrisi üretim yapılandırmasında geçmeli.
- Uç nokta yöntem/şablon envanteri yeni sınıflandırılmamış yolları kapalı karşılamalı; yalnız arayüzde düğme gizleme yetkilendirme sayılmaz.
- Güncel genel regresyon ve gerçek tarayıcı akışları bütün son değişikliklerle yeniden koşulmalı.
- TASK039 denetim günlüğü/yedekleme/geri yükleme ve saklama önizlemesi henüz kabul edilmedi. AuditChain çekirdeğinin 10/10 testi yalnız kayıt tutarlılığına ilişkindir; ayrı kayıt başlığı da değiştirilebiliyorsa yöneticiye karşı değişmezlik sağlamaz.

Windows dağıtımı 9. aşama; Azure ve gerçek Outlook pilotu 10. aşamadadır. İmzalama sertifikası yok: iç deneme paketi imzasız olarak etiketlenir, güvenilir yayıncı doğrulaması sağlanmış sayılmaz.


### TASK038 kapanış
Son genel kontrol: motor808/808, arayüz151/151, typecheck/lint/build PASS. Gerçek üretim güvenlik profilli TestingHost ile3viewport giriş/çıkış ve1HTTP sahiplik/arşivmatrisi geçti (4PASS, tekrarlanmayanHTTP için2intentional skip). Matris gerçek normal API ile ikişirket arşivi oluşturdu, ownerpreview/start ve ownsearch başarılı; farklıuser/newsession/karışıkşirket/kimlikikamesi404. Kanıt: `.codex-coordination/evidence/STAGE8/root038-production-http-and-ui.json`; sonuç `.codex-coordination/results/TASK-038.md`. Yukarıdaki entegrasyon maddeleri038için doğrulandı; TASK039 veWindowsdağıtımı ayrı kabul kapılarıdır.

TASK039 devam kontrolü: fresh archive publication6/6, streaming byte/hash copy6/6 ve audit persistent-boundary6/6 geçti. Ayrı hedefli kümelerdir; genel regresyon sayısı değildir. Audit testi önceki başlıktan geçerli kuyruk toparlama, bozuk/aşırı/çift alanlı kayıt uyarısı ve iki store örneğinin12eşzamanlı yazısını doğruladı. TASK039 uçtan uca kabulü henüz verilmedi.
