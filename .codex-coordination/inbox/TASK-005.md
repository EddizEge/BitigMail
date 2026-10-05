TASK_ID: TASK-005
STATUS: READY
FROM: ASTRA HIGH
TO: SOL 5.6 LIMITED

GOAL:
Kullanıcının geri bildirimini Gemini ile uygula: Müşteriler şirket/proje/kaynak dizini olsun; taşıma/dönüşüm ayrı ana sekmeye taşınsın; Arşiv ve arama şirket/proje/çoklu posta kutusu ve dosya arşivi seçsin. Tam sözleşme docs/PROTOTYPE_ITERATION_2.md.

ACCEPTANCE:
Sözleşmenin tüm davranış ve doğrulama maddeleri. Özellikle ortak kaynak kimlikleri, seçim birleşimi, şirketler arası sonuç bağlamı, boş seçim/boş kaynak ve kapsam dışı önizleme. Mevcut uçtan uca simülasyon ve masaüstü geometri testlerini koru. Yeni ekranlar için 3 boyutta render ve kontrol raporu.

CONSTRAINTS:
Normal implementation Gemini üzerinden resmi agy headless CLI ile. Yeni Codex görevi/alt ajan yok; PC/browser computer-use yok. Terminal headless Playwright uygundur. Gemini doğrudan dosya düzenlesin, SOL komutları çalıştırıp denetlesin. prototype/ ve yerel coordination sonuçları dışında edit yok; ROADMAP/ürün belgeleri Astra'da. Gerçek mail motoru/auth/cloud yok. Mevcut sarı/turuncu/beyaz tasarımı ve root'un son masaüstü grid/scroll/CTA düzeltmelerini koru. Tarayıcıda açık 5173 önizleme sunucusunu koru. frontend/react/testing becerilerinin ilgili kurallarını uygula; terminal-only kullanıcı tercihi geçerli.

VERIFICATION:
build, typecheck, lint, unit, E2E; testleri yeni sekme akışına uyarlayabilirsin fakat eski assertionları sırf geçsin diye kaldırma. Yeni davranış testleri, masaüstü ve mobil renderlar, prototype/VISUAL_QA.md ve README güncellemesi. Aynı veri bütünlüğü hatası tekrarında hedefli düzeltme ve kanıt; gerçek engelde escalation. Deterministic sonuçlar yeterliyse tam LLM review yapma.

DELIVERY:
.codex-coordination/results/TASK-005.md: gerçek Gemini çağrıları, değişiklik özeti, kontrol sonuçları, bilinen sınırlar ve render yolları. Tamamlanana veya gerçek engel oluşana kadar sürdür. Astra wait_threads ile takip edecek. Önizlemeyi erişilebilir bırak.
