TASK_ID: TASK-015
STATUS: DONE — LOCAL_READY / LIVE_PILOT_PENDING
EXECUTOR: GEMINI
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- engine/BitigMail.Engine/Imap/ImapAccountDtos.cs
- engine/BitigMail.Engine/Imap/ImapAccountPublicDto.cs
- engine/BitigMail.Engine/Imap/OAuth/MicrosoftOAuthDtos.cs
- engine/BitigMail.LocalHost/BitigMail.LocalHost.csproj
- engine/BitigMail.LocalHost/Imap/ImapAccountRecord.cs
- engine/BitigMail.LocalHost/Imap/ImapAccountStore.cs
- engine/BitigMail.LocalHost/Security/IImapCredentialResolver.cs
- engine/BitigMail.LocalHost/Security/ImapCredentialResolver.cs
- engine/BitigMail.LocalHost/Security/IMicrosoftAuthProvider.cs
- engine/BitigMail.LocalHost/Security/MsalMicrosoftAuthProvider.cs
- engine/BitigMail.LocalHost/Security/IImapOAuthConnectionTester.cs
- engine/BitigMail.LocalHost/OAuth/MicrosoftOAuthOperationManager.cs
- engine/BitigMail.LocalHost/Imap/ImapClientService.cs
- engine/BitigMail.LocalHost/Imap/Transfer/MailKitTransferClient.cs
- engine/BitigMail.LocalHost/Imap/Transfer/ImapTransferPreviewService.cs
- engine/BitigMail.LocalHost/Imap/Transfer/ImapTransferWorker.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.OAuth.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.cs
- engine/BitigMail.LocalHost/Program.cs
- engine/BitigMail.TestingHost/Security/FakeOAuthServices.cs
- engine/BitigMail.TestingHost/Program.cs
- engine/BitigMail.Engine.Tests/MicrosoftOAuthTests.cs
- engine/BitigMail.Engine.Tests/Microsoft365PolicyTests.cs
- engine/BitigMail.Engine.Tests/Microsoft365LifecycleCriticalTests.cs
- prototype/src/types/localEngine.ts
- prototype/src/api/localEngineClient.ts
- prototype/src/hooks/useMicrosoftOAuth.ts
- prototype/src/hooks/useImapAccounts.ts
- prototype/src/components/clients/ProjectImapAccounts.tsx
- prototype/src/components/transfer/ImapTransferWorkflow.tsx
- prototype/src/tests/microsoftOAuth.test.tsx
- docs/MICROSOFT365_SETUP.md
- docs/MICROSOFT365_CONNECTION_WORKFLOW.md
- THIRD-PARTY-NOTICES.md
SUMMARY:
- Microsoft.Identity.Client 4.89.0 tabanlı Microsoft 365 delegated OAuth authorization-code + PKCE akışı, dinamik localhost callback, hesap-bağlı DPAPI MSAL cache ve Exchange Online IMAP XOAUTH2 entegrasyonu eklendi.
- Üretim hostu gerçek MSAL/IMAP servislerini kullanır; deterministik sahte sağlayıcılar yalnız TestingHost içindedir.
- Microsoft 365 hesap ekleme, güvenli giriş bağlantısı, durum/iptal/yeniden bağlanma, hesap seçicileri ve HTTP 409 reauthorization_required yönlendirmesi frontend'e eklendi.
- İptal ile eşzamanlı bağlantı tamamlanması yarışında backend terminal durumu otorite kabul edilir; geç yanıtlar yeni kapsamı veya işlemi ezmez.
VERIFICATION:
- Backend: 278/278 test PASS, 0 skip; Debug build 0 warning, 0 error.
- Gerçek MSAL içeren korumalı API sınaması: 138/138 PASS; tarayıcı veya gerçek giriş başlatılmadı.
- Account API regresyonu: 120/120 PASS.
- IMAP transfer regresyonu: 155/155 PASS.
- Frontend: typecheck PASS; lint PASS; Vitest 7/7 dosya ve 83/83 test PASS; production build PASS.
- Masaüstü ve 390px mobil salt-okunur form/yardım incelemesi PASS; OAuth başlatılmadı.
RISKS:
- Gerçek Microsoft 365 tenant/test hesabıyla giriş, MFA/tenant policy, klasör okuma ve iki yönlü küçük transfer pilotu henüz çalıştırılmadı.
- Production frontend bundle 500 kB uyarı eşiğini aşıyor (531.98 kB); build başarılıdır.
- Paylaşılan posta kutusu, app-only/tenant-wide izin, Graph, sovereign cloud ve 100 GB ölçek bu kabul kapsamında değildir.
UNCERTAINTIES:
- Canlı tenant politikası, Conditional Access/MFA davranışı, Exchange Online APPEND/flag/keyword izinleri ve sağlayıcı tarafı kabul sonuçları LIVE_PILOT_PENDING durumundadır.
