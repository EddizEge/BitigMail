TASK_ID:
TASK-004

STATUS:
READY

FROM:
ASTRA HIGH

TO:
SOL 5.6 LIMITED

GOAL:
Gemini ile BitigMail'in örnek verili tıklanabilir prototipini prototype/ altında uygula ve doğrula. Kullanıcı açıkça başlamamızı istedi. Tam sözleşme docs/PROTOTYPE_SPEC.md; ana plan ROADMAP.md.

ACCEPTANCE_CRITERIA:
- Sözleşmedeki ana taşıma akışı uçtan uca yerel örnek veriyle çalışır; görünür kontroller işlevli, örnek veri etiketi açık.
- Kabul edilen üç ekran ve ayrı logo varlığına sadık gerçek React bileşenleri; ekran görüntüsü UI olarak kullanılmaz.
- Filtre, ön kontrol engeli/çözümü, değişmez plan, duraklat/devam, tutarlı sonuç ve rapor indirme doğrulanır.
- build/typecheck/lint/test ve headless akış doğrulaması geçer; gerçek komut/sonuç raporu vardır.
- Üç son render ve VISUAL_QA.md; erişilebilir yerel önizleme, README.

KNOWN_RELEVANT_FILES:
- docs/PROTOTYPE_SPEC.md
- docs/VISUAL_DESIGN_V1.md
- design/concepts/bitigmail-workspace-v1.png
- design/concepts/bitigmail-job-center-v1.png
- design/concepts/bitigmail-preflight-v1.png
- design/assets/bitigmail-mark-v1.png (Astra oluşturuyor; mevcutsa kullan, yoksa Astra'ya bildir)
- ROADMAP.md
- results/TERMINAL_CAPABILITY.md

CONSTRAINTS:
- Mevcut resmi agy CLI + Gemini. Yeni Codex görevi/alt ajan yok; masaüstüne veya tarayıcıya yazarak çalıştırma yok.
- Gemini normal uygulamayı yapar; SOL terminalden kurulum/doğrulama ve hedefli düzeltme döngülerini denetler. Headless Playwright terminalde uygundur; kullanıcının açık tarayıcısını/PC'sini kontrol etme.
- prototype/, gerekirse .gitignore ve yerel koordinasyon sonuçları dışında değişiklik yok. ROADMAP.md ve ürün belgelerini Astra yönetir.
- Gerçek e-posta erişimi, dosya motoru, kimlik doğrulama, yayınlama veya bulut servisi ekleme yok. React/Vite/TypeScript yalnız prototip seçimi.
- Normal hafif bağımlılıklar kurulabilir; ticari SDK/sistem kurulumu yok. Yerel sunucu loopback, arka plan yardımcıları gizli pencereyle.
- Gemini görselleri okusun; frontend-app-builder ve react-best-practices becerilerinin ilgili kurallarını uygulasın. Tasarım konseptleri zaten kabul edildi, yeniden tasarım istemiyoruz. Kullanıcının terminal-only talebi becerinin IAB önceliğinin yerine geçer.

RISK:
LOW (yalnız sentetik veri), sayım/durum kuralları özellikle doğrulanmalı.

VERIFICATION:
docs/PROTOTYPE_SPEC.md içindeki test ve görsel teslim ölçütleri. Makul odaklı düzeltmeler yap; gerçek engelde kısa kanıtla escalasyon. CLI SUCCESS tek başına yeterli değil.

NOTES:
Tamamlanana veya gerçek engel oluşana kadar sürdür. Sonuç .codex-coordination/results/TASK-004.md. Yerel önizleme URL'si, dosyalar ve kontrolleri raporla; Astra'ya başka göreve mesajla işi geri tetiklemeye gerek yok, wait_threads ile izleyecek. Sunucuyu erişilebilir bırak ve oturum/başlatma bilgisini kaydet.
