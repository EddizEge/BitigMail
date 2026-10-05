TASK_ID: TASK-016
STATUS: DONE — LOCAL_READY / LIVE_PERSONAL_PILOT_PENDING
EXECUTOR: GEMINI
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.LocalHost/Security/Microsoft365Policy.cs (ASTRA/root exclusive)
- engine/BitigMail.LocalHost/Security/MsalMicrosoftAuthProvider.cs
- engine/BitigMail.TestingHost/Security/FakeOAuthServices.cs
- engine/BitigMail.Engine.Tests/OutlookPersonalCriticalTests.cs (ASTRA/root exclusive)
- engine/BitigMail.Engine.Tests/MicrosoftOAuthTests.cs
- prototype/src/api/localEngineClient.ts
- prototype/src/hooks/useMicrosoftOAuth.ts
- prototype/src/styles/index.css
- prototype/src/components/clients/ProjectImapAccounts.tsx
- prototype/src/components/transfer/ImapTransferWorkflow.tsx
- prototype/src/tests/microsoftOAuth.test.tsx
- docs/OUTLOOK_PERSONAL_WORKFLOW.md
SUMMARY:
- Mevcut authKind=microsoft365 uyumluluğu korunarak kişisel Outlook.com/Hotmail modu eklendi; authority seçicisi yalnız tam consumers değeridir ve gerçek MSA tenant kimliği 9188040d-6c67-4c5b-b112-36a304b66dad ile doğrulanır.
- Silent ve interactive MSAL akışları aynı strict authority/kimlik politikasını kullanır; eksik kimlik yerine istenen değerler konmaz. MSAL 4.89.0 ve mevcut DPAPI/cache/authGeneration/IMAP TLS sınırları değişmedi.
- Arayüz kurumsal ve kişisel modu açıkça ayırır. Kişisel form yalnız görünen ad, posta kutusu ve Client ID ister; tenant ve parola alanı yoktur, backend'e consumers gönderir.
- Tür değişiminde aktif OAuth işlemi iptal/temizlenir ve eski URL/yanıtlar generation korumasıyla elenir. Kayıtlı hesap, reconnect ve iki transfer seçicisinde kişisel hesap etiketi görünür.
- Kişisel pilot metni ayrı BitigMail-Test klasörü, yapay iletiler ve hiçbir mevcut postayı silmeme/erişmeme sınırını açıklar.
VERIFICATION:
- Backend: 305/305 test PASS, 0 skip; Debug LocalHost build 0 warning, 0 error.
- Root policy kritik testleri: 18 PASS.
- Gerçek MSAL consumers URL/state/cancel-denied probe: 3/3 PASS; tarayıcı/giriş/token işlemi yok.
- Normal host kişisel consumer API: 147 PASS; kurumsal regresyon: 129 PASS.
- Frontend: typecheck PASS; lint PASS; Vitest 7/7 dosya ve 90/90 test PASS; production build PASS.
- Gerçek render: 390px mobil ve 1660px masaüstü PASS; kişisel etiket kesilmedi, mobil düğme 40px, yatay taşma yok, kişisel formda parola/tenant alanı 0, konsol error/warn 0.
RISKS:
- BitigMail için kişisel Microsoft hesaplarını destekleyen gerçek uygulama kaydı ve Client ID henüz yoktur.
- Gerçek kullanıcı girişi, MFA, Outlook IMAP ayarı, klasör okuma veya kopyalama pilotu çalıştırılmadı.
- Production frontend bundle 500 kB uyarı eşiğini aşıyor (537.86 kB); build başarılıdır.
UNCERTAINTIES:
- Canlı kişisel hesap authority/consent davranışı, Outlook.com IMAP erişimi ve APPEND/flag/keyword sağlayıcı kabulü LIVE_PERSONAL_PILOT_PENDING durumundadır.
- Kurumsal Microsoft 365 canlı pilotu ayrı olarak LIVE_PILOT_PENDING kalır.
