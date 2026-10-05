# Gmail / Google Workspace bağlantısı

Durum: **LOCAL_READY / LIVE_GOOGLE_PILOT_PENDING**.

BitigMail yalnız kullanıcının kendi Gmail veya Google Workspace posta kutusu için Google masaüstü OAuth istemcisi, Authorization Code + S256 PKCE ve resmi `Google.Apis.Auth` 1.76.0 akışını kullanır. Yetkilendirme `accounts.google.com/o/oauth2/v2/auth`, dönüş yalnız dinamik `127.0.0.1` loopback, kapsamlar yalnız `https://mail.google.com/`, `openid` ve `email`dir. ID token imzası, audience, issuer ve süre resmi SDK ile doğrulanır; doğrulanmış e-posta istenen kutuyla, subject yeniden bağlantıda kalıcı kimlikle eşleşir.

IMAP uç noktası sabit `imap.gmail.com:993` SSL ve XOAUTH2'dir. Başarılı gerçek IMAP kimlik doğrulamasından önce hesap kalıcılaştırılmaz. Token/cache ve masaüstü istemci secret değeri hesap kimliğine bağlı DPAPI zarfında tutulur; API/DTO/UI/loglara dönmez. Servis hesabı, domain-wide delegation, özel OAuth uç noktası, SMTP/POP, otomatik APPEND retry ve posta silme yoktur.

Google Cloud Console'da Desktop app OAuth client oluşturulup Client ID ve Client Secret yerel forma girilmelidir. Canlı kimlik bilgileri henüz sağlanmadığından gerçek giriş, Workspace yönetici politikası, kota/throttling ve APPEND/flags/keywords davranışı kabul edilmiş değildir.

## Sağlayıcı yetenek matrisi

| Profil | Kimlik | Kaynak | Hedef | Destek düzeyi | Açık sınırlar |
|---|---|---:|---:|---|---|
| Genel IMAP | Parola | Evet | Evet | Yerel/gerçek IMAP kabulü | Sağlayıcıya özgü OAuth yok |
| Exchange Online | Microsoft delegated OAuth | Evet | Evet | LOCAL_READY | Canlı pilot Aşama 10 |
| Outlook.com / Hotmail | Microsoft consumers OAuth | Evet | Evet | LOCAL_READY | Canlı pilot Aşama 10 |
| Gmail / Workspace | Google delegated OAuth | Evet | Evet | LOCAL_READY | LIVE_GOOGLE_PILOT_PENDING |
| Şirket içi Exchange | Yalnız yapılandırılmış genel IMAP | Koşullu | Koşullu | Doğrulanmamış | EWS/MAPI, NTLM/Kerberos, autodiscover ve hybrid OAuth desteklenmez |

Yerel sahte sağlayıcı testleri canlı Google kabulü değildir.
