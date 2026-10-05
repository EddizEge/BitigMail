# Microsoft 365 pilot hazırlığı

Durum: bağlantı altyapısı yerel kabulü tamamladı (**LOCAL_READY / LIVE_PILOT_PENDING**). Gerçek Microsoft hesabıyla giriş ve aktarım kabulü henüz yapılmadı.

## Gerekenler

- Yalnız deneme iletileri içeren bir Exchange Online posta kutusu.
- Bu kurumun Entra uygulama kaydını oluşturabilecek erişim; kurum politikası gerektiriyorsa yetkili yöneticinin izin onayı.
- İlk pilotta giriş yapılan kullanıcıya ait posta kutusu. Paylaşılan kutu, başka kullanıcı adına erişim ve şirket içi Exchange sonraki ayrı doğrulamaların konusudur.

## Entra uygulama kaydı

1. Microsoft Entra yönetim merkezinde doğru kurumu seçin. **Entra ID → App registrations → New registration** yolunu açın.
2. Adı **BitigMail Pilot** yapın ve yalnız bu kurumdaki hesaplara açık (single tenant) kayıt oluşturun.
3. Overview ekranındaki **Application (client) ID** ile **Directory (tenant) ID** değerlerini kaydedin. Bunlar BitigMail formuna girilecek tanımlayıcılardır; parola veya client secret değildir.
4. Authentication altında **Mobile and desktop applications** platformuna `http://localhost` dönüş adresini ekleyin. Giriş sırasında MSAL boş bir yerel port seçer. Vite arayüz adresini veya bilgisayarın LAN IP'sini dönüş adresi olarak kullanmayın.
5. Exchange Online için delegated **IMAP.AccessAsUser.All** iznini yapılandırın. BitigMail'in istediği tam kapsam `https://outlook.office.com/IMAP.AccessAsUser.All` olur. Bu akış client secret veya application/app-only izni kullanmaz. Microsoft Graph, SMTP ve POP izni bu pilot için gerekli değildir.
6. İzin onayını kurumun politikasına uygun yürütün. Yönetici onayı veya IMAP erişim engeli varsa kurum yöneticisi değerlendirmelidir; BitigMail bunu aşmaya çalışmaz.

Uygulama kaydı adımları [Microsoft Entra kayıt kılavuzuna](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app), loopback tarayıcı akışı [MSAL tarayıcı belgesine](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/using-web-browsers) dayanır. IMAP kapsamı ve yetkilendirme biçimi [Microsoft Exchange OAuth belgesinde](https://learn.microsoft.com/en-us/exchange/client-developer/legacy-protocols/how-to-authenticate-an-imap-pop-smtp-application-by-using-oauth) açıklanır. Belgeler 2026-09-13 tarihinde kontrol edildi.

## BitigMail'de bağlantı

Hedeflenen akış: Müşteriler → ilgili şirket ve proje → Hesap ekle → Microsoft 365. Görünen ad, posta kutusu adresi ve iki kayıt kimliği girilir. BitigMail'in ürettiği Microsoft giriş bağlantısı **BitigMail motorunun çalıştığı bilgisayardaki tarayıcıda** açılır; kullanıcı giriş ve gerekiyorsa MFA işlemini Microsoft ekranında tamamlar.

Telefon için LAN bağlantısı arayüz önizlemesidir. Bu pilotun localhost dönüş akışı farklı cihazdaki tarayıcıda tamamlanmaz.

BitigMail giriş kimliğini kurum ve istenen posta kutusuyla eşleştirir, ardından IMAP bağlantısını sınar. Sunucu `outlook.office365.com`, port `993`, SSL/TLS kullanılır. [Microsoft Exchange bağlantı ayarları](https://learn.microsoft.com/en-us/exchange/clients-and-mobile-in-exchange-online/pop3-and-imap4/pop3-and-imap4).

Yetkilendirme önbelleği bilgisayardaki Windows kullanıcısına ve BitigMail hesap kimliğine bağlı korumayla saklanır. Yerel bağlantıyı kaldırmak BitigMail'deki kaydı kaldırır; Microsoft tarafındaki verilmiş izinleri iptal ettiği anlamına gelmez.

## Canlı aktarım pilotunun kabulü

Küçük sentetik veriyle tarih/klasör filtresi uygulanır. Önce IMAP → Microsoft, ardından Microsoft → deneme IMAP hesabı denenir. Sonuç yalnız toplam ileti sayısıyla değerlendirilmez: özgün içerik ve ekler, tarihler, klasörler ve durum bilgileri bağımsız karşılaştırılır; kaynak değişmemelidir.

Mevcut aktarım motoru hedefte kalıcı öğe işaretleri ve tam içerik doğrulaması ister. Microsoft hedefi bu koşullardan birini karşılamazsa ilgili yön desteklenmiş sayılmaz; engel rapora yazılır ve ayrı sağlayıcı uyarlaması değerlendirilir. Yerel sahte sağlayıcı testleri canlı kabulün yerine geçmez.
