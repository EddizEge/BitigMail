# TASK-005 — Astra bağımsız görsel ara kontrolü

Bu belge ara bulguları ve final kabulünü kaydeder. Son durum: tüm aşağıdaki düzeltmeler giderildi; TASK-005 DONE. Kanıt: TASK-005.md ve prototype/VISUAL_QA.md.

Yöntem: terminalde Playwright Chromium headless, temiz yeni context; kullanıcının tarayıcısı/masaüstü kontrol edilmedi. İnceleme boyutları 1660×948 ve 390×844.

## Görülen doğru davranışlar

- Temiz ilk açılış Müşteriler başlığını ve iki şirket kartını gösterir; tek bir kişinin posta kutusuna yönlenmez.
- Aktarım ve dönüşüm ayrı ana sekmedir. Müşteri/proje/kaynak/hedef seçicileri üstte görünür.
- Arşiv ve aramada şirket/proje/kaynak ağacı, tümünü seç/temizle ve seçili kutu/arşiv sayımı bulunur.
- 1660 ekranında mevcut çalışma masasının ileti önizlemesi ve ön kontrol düğmesi ekranda kalır.
- Beyaz yüzey, sarı seçim vurgusu, turuncu eylemler, BitigMail logosu ve okunaklı koyu metin korunur.
- Şirket dizini 390 ekranında tek sütuna geçer; şirket ekleme, arama ve ayrıntı eylemleri erişilebilirdir.

## SOL/Gemini'ye iletilen düzeltmeler

1. Üstte yeni ortak kaynak seçilirken soldaki eski sabit SourceTree görünüyordu. Proje kaynaklarına bağlanmalı; '+' ortak kayıt eklemeli; klasör/kaynak seçimi gerçek planı güncellemelidir.
2. Arama sonuçlarında yalnız şirket ve kaynak etiketi görünüyordu. Proje ve gerçek hesap/dosya adıyla belirsizlik giderilmelidir.
3. Native checkbox mavisi marka rengiyle uyarlanmalıdır.
4. 390 ekranda arama ağacı 300px genişlikte kalıp sonuçları ~90px şeride sıkıştırıyordu. Sayfa genişliği 390 olduğundan taşan içeriğe sayfa kaydırmasıyla erişilemiyordu. Dikey düzen veya konum çekmecesi gerekir; sorgu/klasör/sonuç/önizleme etkileşimi doğrulanmalıdır.

## Kabul notları

- Şirket dizini ve genel masaüstü yerleşimi için yeniden tasarım gerekmiyor.
- Mobil ana menünün yatay kaydırması bu yineleme için kabul; aramanın ana içerik alanındaki erişim kaybı kabul değil.
- Testler arası localStorage sızıntısı varsayımı kanıtsızdı; mevcut standart test context'lerinde ilk açılış ayrı doğrulanmalıdır. Astra'nın bağımsız temiz context'i şirket listesini gösterdi.
- Normal uygulama Gemini'de, deterministic doğrulama SOL'da. Astra bu tur uygulama kodu yazmadı; mimari ve görsel entegrasyon bulgularını aktardı.

## Final kabul

- Ortak kaynak ağacı son operations-desktop-reference/compact renderlarında doğrulandı; eski sabit ağaç kaldırıldı.
- Arama şirket/proje/kaynak/hesap bilgileri ve marka checkboxları son multi-search renderında görüldü.
- 390×844 bağımsız etkileşimde tüm konumları seç → teslim sorgusu → sonuç seç → önizleme akışı çalıştı. Dikey arama düzeni kabul.
- Önizlemedeki kaynak satırının sıkışması son renderda giderildi; satır yüksekliği ekran testinde de sınanıyor.
- 19 unit, 24 yeni E2E ve 22 mevcut akış/geometri testi geçti; 2 bilinçli mobil geometri skip. HTTP200 root tarafından tekrar doğrulandı.
- Dokuz kanonik yeni render korunur. Root'un ara kontrol görüntüleri teslim klasöründe çoğaltılmaz.
