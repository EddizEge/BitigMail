# TASK-016 — Kişisel Outlook bağlantısı

Durum: DONE — LOCAL_READY / LIVE_PERSONAL_PILOT_PENDING, 2026-09-14. Yerel uygulama ve deterministik kabul tamamlandı. Hiçbir posta silinmedi; gerçek uygulama kaydı, kullanıcı girişi ve kişisel posta kutusu pilotu henüz yapılmadı.

## Sabit mimari

- Mevcut `authKind=microsoft365` korunur. `tenantId` alanı geriye uyumlu authority seçicisidir: kurumsalda mevcut canonical nonzero GUID, kişiselde yalnız tam `consumers` sabiti. Kişisel seçim UI tarafından açık yapılır; e-posta uzantısından otomatik çıkarılmaz. `common`, `organizations`, URL, boş/benzer değer kabul edilmez.
- Kişisel authority `https://login.microsoftonline.com/consumers`; gerçek oturumun tenant kimliği tam `9188040d-6c67-4c5b-b112-36a304b66dad` olmalıdır. Root policy `GetExpectedTenantId(tenantId)` bu eşlemeyi sağlar. MSAL interactive ve silent aynı authority ile kurulur. Silent sonuç tenant karşılaştırması bu metoda göre yapılır; HomeAccountId tam eşleşir.
- Kişisel kimliğin tenant değeri boşsa veya organizasyona aitse reddet. Beklenen posta kutusu ile MSAL username eşleşmesi ve ardından gerçek IMAP auth zorunlu. Kimlik eksikse istenen e-postayı/tenantı yerine koyma. İlk pilot alias eşleşmesini genişletmez; farklı alias açık hata verir. HomeAccountId kalıcı bağdır; yeniden bağlama aynı kimlik olmalıdır.
- Policy yalnız seçilen authority path'ini kabul eder; kişisel `/consumers/oauth2/v2.0/authorize`, kurumsal `/{guid}/oauth2/v2.0/authorize`. PKCE S256, form_post, sabit IMAP scope ve MSAL-owned localhost callback korunur. Gerçek SDK farklı bir kanonik path üretirse root'a kanıtla ilet; allowlist'i genişletme.
- ClientId nonzero canonical GUID olarak zorunlu. Başka uygulamaların clientId değerleri kullanılmaz. Personal destekleyen BitigMail Entra uygulama kaydı gerekir; kayıt henüz yok. Kişisel posta hesabı olması tek başına uygulama kaydı sağlamaz.
- DPAPI, hesap/kapsam izolasyonu, authGeneration, atomic cache commit, cancel/timeout/connected yarışları, reauthorization_required, resume ve tüm TASK-015 sınırları korunur.
- Aynı Outlook IMAP endpoint ve TLS kullanılır. Posta silme/EXPUNGE/MOVE işlemi eklenmez. Kaynak read-only kalır. Otomatik pilot, test iletisi yazımı veya kullanıcı kutusunun taranması bu yerel geliştirme kapsamında yapılmaz.

## Arayüz

Microsoft bağlantısında açık `Kurumsal Microsoft 365` ve `Kişisel Outlook.com / Hotmail` seçimi. Kişisel form: görünen ad, posta kutusu adresi, Application Client ID. Tenant ve parola alanı yok; backend'e consumers gönderilir. Tür değişiminde aktif OAuth iptal/temizleme ve stale generation koruması çalışır. Reconnect sabit client/authority/mailbox kimliğini korur. Saved hesap ve transfer seçeneklerinde kişisel/kurumsal ayrımı görünür.

Kişisel kurulum yardımında personal hesapları destekleyen app registration, localhost desktop redirect, delegated Exchange IMAP scope, client secret gerekmemesi, bilgisayarda kullanıcı girişi ve Outlook ayarlarında gerektiğinde IMAP erişimi açıklanır. Kullanıcıya gerçek kişisel posta içeriğinin gerektiği izlenimi verilmez; canlı pilot ayrı BitigMail-Test klasöründeki yapay maillerle kopyalama olacaktır.

## Kabul sınırı ve kaynaklar

Yerel uygulama/test kabulü canlı kişisel hesap kabulü değildir. Outlook.com pilotu kurumsal Exchange Online tenant/MFA/APPEND kabulünün yerine geçmez. TLS laboratuvarı hazırlığı ve gerçek uygulama kaydı ayrı canlı pilot önkoşullarıdır.

2026-09-13 resmî kaynak kontrolü:
- [MSAL authority ve consumers audience](https://learn.microsoft.com/en-us/entra/identity-platform/msal-client-application-configuration)
- [MSA tenant kimliği](https://learn.microsoft.com/en-us/entra/identity-platform/id-token-claims-reference)
- [Outlook.com ve Microsoft365 IMAP OAuth](https://learn.microsoft.com/en-us/exchange/client-developer/legacy-protocols/how-to-authenticate-an-imap-pop-smtp-application-by-using-oauth)
