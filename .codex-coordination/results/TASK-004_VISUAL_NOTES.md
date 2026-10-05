# TASK-004 — Ara görsel bulguları (Astra)

2026-09-12. Bu kayıt ara sürüme aittir; son teslim sonucu değildir. SOL'a aynı E2E düzeltme turunda iletildi.

- İlk qa/workspace.png: 1680×33314; bütün ileti satırları sayfayı uzatıyor, önizleme/plan çok aşağıda. Çalışma alanı içinde kaydırma/sayfalama ve küçük görünümde erişilebilir panel geçişi gerekli.
- İlk qa/preflight.png: üst menü sayfanın ortasında uyarı satırını örtüyor. Sticky header/scroll yaşam döngüsü ve test ekran görüntüsü konumu düzeltilmeli.
- Viewportlar aynı dosya adını ezmiş; reference/laptop/mobile isimleriyle ayrı kayıt gerekli. Referans karşılaştırması viewport görüntüsüyle yapılmalı, tam sayfa görüntüsü gerçek düzen hatasını gizlememeli.
- İş merkezi açık menüsü ayrıntı başlığını örtüyor; kapalı menü kanıtı ve uygun açılır menü yerleşimi gerekli.
- 1660×948 referans, 1366×768 laptop, 390×844 dar görünüm kontrolleri ve en az beş tasarım karşılaştırma noktası bekleniyor.

İlk kalite kapıları SOL tarafından PASS bildirildi: build/typecheck/lint, 14 birim testi, npm audit 0. Headless akışta ön kontrol/aktarım ve dar görünüm test hataları henüz çözülmemişti. P1 tamamlandı sayılmaz.
