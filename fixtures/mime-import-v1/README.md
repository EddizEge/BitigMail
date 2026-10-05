# MIME import bağımsız beklenen sonuçları

`expected-mime.json`, mevcut `../mail-corpus-v1` EML ve MBOX dosyalarının bağımsız Python stdlib `email` okumasından üretilmiş sabit test beklentisidir. MimeKit/Aspose çıktısından üretilmemiştir. Testler kaynak dosya hash'lerini bu beklentiyle doğrulamalı; ayrıştırıcı hatasına uyum sağlamak için beklenen içerik yeniden yazılmamalıdır.

- 12 fiziksel ileti, dört ek, bir özgün CID; aynı ham içerikli iki dosya ayrı iletilerdir.
- `messages`: özgün EML başlıkları, düz/HTML gövdeleri, ek boyut/hash/CID ve test amaçlı klasör eşlemesi.
- `mboxMessages`: mboxrd gövde kaçışı fiziksel satırlarda tam bir kez çözüldükten sonraki özgün MBOX beklentisi. MBOX'un kendisinde klasör ağacı yoktur; tek `corpus` klasörü kullanılır.
- `preExistingSerializationDifferences`: msg-09 ve msg-10 MBOX gövdelerindeki önceden mevcut tek ek LF. EML ve MBOX ayrı kaynak beklentilerine sahiptir; metinler genel `Trim` ile eşitlenmez.
- Gövde hash'leri yalnız CRLF/CR → LF dönüşümü sonrası UTF-8 üzerinden hesaplanır. Değerlendirme işaretleri beklentilere eklenmez.
- Gerçek klasör ağacı sınaması gerektiğinde test kendi benzersiz geçici dizinine `messages[].mappedFolder` üzerinden kaynak EML kopyalarını oluşturabilir. Özgün corpus değişmez.

İlk üretim kanıtı: `C:/Users/Eddiz/AppData/Local/Temp/bitigmail-task013-qa/oracle-ce4c5897/report.json`. Bu geçici yol test çalışması için gerekli değildir; kalıcı beklenti burada ve kaynak dosyalar projededir.

`escape-edges/` içindeki üç küçük örnek raw, quoted-printable ve base64 gövdelerde `From`, `>From` ve `>>From` satırlarını içerir. MBOX kaçışı ham kayıt üzerinde, MIME aktarım kodlaması çözülmeden önce tam bir kez kaldırılır. `expected.json` özgün EML ile MBOX metinlerini ayrı tutar; QP ve raw MBOX örneklerinin önceden mevcut ek LF'si atılmaz.
