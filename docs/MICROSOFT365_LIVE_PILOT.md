# Microsoft 365 canlı pilot yürütme kaydı

Durum: **KİŞİSEL HESAP DOĞRULANDI / UYGULAMA KAYDI VE CANLI GİRİŞ BEKLENİYOR**.

2026-09-14 güncel durum: kullanıcı hesabın kişisel olduğunu doğruladı. TASK-016 kişisel Outlook bağlantısı yerel kabulü tamamladı; [doğrulama kaydı](OUTLOOK_PERSONAL_VALIDATION.md). Kullanıcının hesabına giriş yapılmadı ve hiçbir posta okunmadı/değiştirilmedi/silinmedi. BitigMail adına kişisel hesapları destekleyen uygulama kaydı henüz mevcut değil. Aşağıdaki kurumsal pilot koşulları ayrıca geçerlidir; kişisel pilot kurumsal sağlayıcı kabulünün yerine geçmez.

Son kullanıcı yönlendirmesi: kendi Outlook hesabını denemeye sunabileceğini belirtti; **hiçbir posta silinmemesi** açık koşuldur. Kişisel Outlook.com/Hotmail mi, kurumsal Microsoft 365 mi olduğu henüz bilinmiyor. Hesap bilgisi alınmadı ve giriş yapılmadı. Gelecek pilot mevcut iletileri değiştirmeden, ayrı test klasöründeki sentetik iletilerle kopyalama olarak tasarlanacak; test iletileri dahil otomatik silme/temizleme yapılmayacak. Kişisel hesap doğrulanırsa mevcut single-tenant Microsoft 365 bağlantısına doğrudan uygun varsayılmaz; önce kişisel hesap OAuth desteği ve uygulama kaydı hazırlanır. Outlook.com kabulü kurumsal Exchange Online kabulü sayılmaz.

TASK-015 yerel kabulü geçerlidir. Bu kayıt canlı hesabın bağlandığını veya Microsoft aktarımının kabul edildiğini göstermez. Kullanıcı test hesabının şu anda bulunmadığını ve ortamı ajanın hazırlayıp hazırlayamayacağını bildirdi. Hesap açılmadı, abonelik başlatılmadı.

Resmî seçenekler 2026-09-13 tarihinde kontrol edildi: [Microsoft 365 Developer Program](https://learn.microsoft.com/en-us/office/developer-program/microsoft-365-developer-program-faq) uygun üyelere Exchange Online içeren ücretsiz sandbox sağlar; programa katılmak tek başına sandbox hakkı vermez. Uygun Visual Studio aboneliği veya Microsoft iş ortağı/destek programı gibi koşullar vardır. [Microsoft 365 iş denemeleri](https://learn.microsoft.com/en-us/microsoft-365/commerce/try-or-buy-microsoft-365?view=o365-worldwide) için 30 günlük seçenek ve kart gereksinimi belgelenmiştir. Kullanıcının uygunluğu ve mevcut teklifin kayıt koşulları doğrulanmadan ücretsiz ortam garantisi verilmez. Hesap doğrulama ve abonelik onayı kullanıcı katılımı gerektirebilir; yerel testler gerçek Exchange Online yerine geçmez.

## Denemeye giriş koşulları

- Yalnız sentetik deneme postaları içeren, kullanıcının erişim yetkisi bulunan Exchange Online posta kutusu belirlenir.
- Entra kaydı ve kullanıcı girişi [kurulum kılavuzuna](MICROSOFT365_SETUP.md) göre tamamlanır. Parola, MFA kodu veya token sohbet/dokümanlara yazılmaz.
- Giriş motorun çalıştığı bilgisayarda yapılır. Telefon adresi yalnız arayüz önizlemesidir.
- İlk bağlantıda tenant, posta kutusu kimliği ve IMAP bağlantısı doğrulanır. Bu başarılı olmadan aktarım başlatılmaz.
- Karşı uç olarak kullanılacak IMAP test hesabında üretim bağlantı politikasına uygun TLS bulunmalıdır. Mevcut Dovecot `127.0.0.1:5143` düz bağlantı laboratuvarı tek başına bu koşulu karşılamaz. Normal motorun TLS kontrolü kapatılmaz; sahte OAuth sağlayıcılı test hostu canlı Microsoft doğrulaması yerine kullanılmaz.

## Küçük aktarım sırası

1. Kaynak ve hedef şirket/proje/hesap kimlikleri, seçilen klasörler ve tarih aralığı kayda alınır. Her deneme için ayrı hedef klasörü kullanılır.
2. Aktarımdan önce seçili iletilerin içerik, ek, tarih, durum bilgileri ve adetleri bağımsız olarak kaydedilir. Eski kabul raporu yeni canlı kaynağın envanteri sayılmaz.
3. IMAP → Microsoft yönünde küçük bir seçim önizlenir. Önizlemenin hedefte boş klasör oluşturabileceği dikkate alınır; kaynak postalar silinmez.
4. Kopyalanan iletiler hedefte bağımsız okunur. Eksik/fazla öğe, içerik/ek farkı, tarih, klasör ve desteklenen durum bilgileri karşılaştırılır; kaynak değişmemelidir.
5. Microsoft → IMAP yönü ayrı hedef klasöründe aynı ölçütlerle denenir. İki yönün sonucu ayrı kaydedilir.
6. Token yenileme veya sağlayıcı keyword/APPEND davranışı bir yönü engelliyorsa o yön kabul edilmez. İletiyi yeniden yazarak veya bütünlük kontrolünü gevşeterek başarı üretilmez.

## Kabul kaydı

| Adım | Durum |
|---|---|
| Test hesabı / Entra erişimi | Kullanıcı mevcut olmadığını bildirdi; hesap edinimi bekleniyor |
| Gerçek giriş ve kimlik eşleşmesi | Çalıştırılmadı |
| Microsoft klasör okuma | Çalıştırılmadı |
| TLS kullanan karşı IMAP test ucu | Hazırlanacak / belirlenecek |
| IMAP → Microsoft bağımsız karşılaştırma | Çalıştırılmadı |
| Microsoft → IMAP bağımsız karşılaştırma | Çalıştırılmadı |

Kabul raporuna çalışma kimlikleri, seçim özeti, fark sayıları ve sınırlamalar yazılır; sırlar eklenmez. Pilot bitiminde test postaları otomatik temizlenmez. Büyük kutu, Google, paylaşılan posta kutusu ve şirket içi Exchange desteği bu küçük deneyden çıkarılamaz.
