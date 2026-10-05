# Aspose ilk kullanım eşzamanlılık deneyi

20 Eylül 2026. Üretim motoru değiştirilmeden Aspose.Email 24.8.0 ile yapılan yerel deney.

Bir genel testte, `MapiMessage.SetProperty(KnownPropertyList.Body, ...)` çağrısında SDK içindeki `KnownPropertyList` sözlüğüne yinelenen anahtar ekleme hatası gözlendi. Tekrar denemede geçmesi çözüm olarak kabul edilmedi.

`lab/stage5-sdk-initialization` programı, her yeni süreçte 32 eşzamanlı sentetik MAPI gövde/karakter kodu yazımı çalıştırır. Kullanıcı postası veya hesabı kullanılmaz.

| Başlangıç | Yeni süreç sayısı | Hatalı süreç | Toplam hatalı işlem |
| --- | ---: | ---: | ---: |
| Doğrudan eşzamanlı ilk kullanım | 3 | 2 | 2 |
| Önce tek sıralı sentetik MAPI yazımı | 3 | 0 | 0 |

Kanıt: `.codex-coordination/evidence/STAGE5/sdk-cold-start-probe.json`.

Bu küçük deney ilk kullanım yarışını yeniden üretti; bütün SDK işlemlerinin eşzamanlı güvenli olduğunu kanıtlamaz. TASK035'te lisans yüklemesinden sonra ve istek kabulünden önce tek seferlik sıralı başlangıç uygulanmalıdır. Ayrı kurtarma işçileri de aynı sırayı kullanmalıdır. Başlangıç başarısızsa motor hazır görünmemeli; lisans veya başlangıç hatası açık raporlanmalıdır. Genel testleri yalnız sıralı çalıştırarak hata gizlenmemelidir.
