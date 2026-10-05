# BitigMail — Outlook ile gerçek test OST'si oluşturma

Test sunucusu hazır: 12 temel ileti, 3 klasör ve satır içi görsel dahil 4 ek doğrulandı. Bu adım, Outlook'un bu sentetik kutudan gerçek bir OST oluşturmasını sağlar. Dönüştürme daha sonra dosyanın kopyasında çalışacak.

## 1. Ayrı test profilini aç

Klasik Outlook kapalıyken **Win + R** ile `outlook.exe /profiles` çalıştır. **Seçenekler → Yeni** yolundan **BitigMail-Lab** profilini oluştur. Mevcut varsayılan profil ayarını değiştirme.

Hesap eklerken **El ile kurulum → POP veya IMAP** seç. Yeni sihirbaz görünürse **Gelişmiş seçenekler → Hesabımı kendim ayarlamak istiyorum → IMAP** yolunu kullan.

## 2. Bağlantı bilgilerini gir

| Alan | Değer |
|---|---|
| Ad | BitigMail Lab |
| E-posta adresi | `lab@bitigmail.example` |
| Kullanıcı adı | `lab` |
| Parola | Aynı klasördeki `local-credentials.json` dosyasının `password` değeri |
| Gelen sunucu / port | `127.0.0.1` / **3143** |
| Giden sunucu / port | `127.0.0.1` / **3025** |
| Her iki sunucuda şifreleme | **Yok / None** |
| SMTP kimlik doğrulama | Açık; gelen sunucuyla aynı kullanıcı adı ve parola |
| Güvenli Parola Kimlik Doğrulaması (SPA) | Kapalı |

Parola dosyası: `C:\Users\Eddiz\Documents\ChatGPT\Mail Manager\lab\ost-spike\local-credentials.json`. Parolayı sohbete göndermene gerek yok.

Bu posta bağlantıları yalnız bilgisayardaki test sunucusuna gider. SMTP sunucusu iletileri yerelde yakalar. Şifrelemesiz ayarlar yalnız bu yerel deneme içindir.

## 3. İletileri eşitle

**Gönder/Al** işlemini tamamla; yalnız başlıkların değil, ileti içeriklerinin ve eklerin de indirildiğini kontrol et. Klasörler görünmüyorsa hesap üzerindeki **IMAP Klasörleri → Sorgula** bölümünden test klasörlerine abone ol.

12 Eylül ilk denemesinde test klasörlerinin sunucu abonelikleri eksikti; yalnız Gelen Kutusu OST'ye indi. Abonelikler düzeltildi ve hazırlama betiğine eklendi. Bu denemeyi yenilerken Gelen Kutusu'nda Outlook sınama iletisiyle birlikte **6**, Gönderilenler'de **3**, Projeler → İstanbul'da **4** öğe görünmesini bekle. İlk kopyayı korumak için yeni OST kopyasını `input/bitigmail-lab-full.ost` adıyla kaydet.

| Klasör | Temel ileti sayısı |
|---|---:|
| Gelen Kutusu (INBOX) | 5 |
| Gönderilenler | 3 |
| Projeler → İstanbul | 4 |

Sunucunun klasör ayıracı `.`; alt klasör sunucuda `Projeler.İstanbul` olarak tutuluyor. Outlook'ta hiyerarşik görünebilir. Outlook hesap sınaması ek bir test iletisi oluşturursa onu silme; temel 12 iletiden ayrı kaydedeceğiz. Ek boş sistem klasörleri normaldir.

## 4. Yalnız test hesabının OST'sini al

1. Outlook'ta **Dosya → Hesap Ayarları → Hesap Ayarları → Veri Dosyaları** bölümünde yalnız `lab@bitigmail.example` hesabını seçip dosya konumunu not et.
2. Outlook'u **Dosya → Çıkış** ile normal kapat; kapanması bitene kadar bekle. Zorla sonlandırma.
3. Test hesabına ait OST dosyasını aşağıdaki konuma **kopyala**:

   `C:\Users\Eddiz\Documents\ChatGPT\Mail Manager\lab\ost-spike\input\bitigmail-lab.ost`

4. Sohbete **“Test OST'si hazır”** yaz. Kopyalama yerine yalnız test OST'sinin tam dosya yolunu da paylaşabilirsin.

Kopya geldikten sonra kaynak hash kontrolü, ileti çıkarma, yeni PST üretimi ve kayıp/fark raporu çalıştırılacak. Bu adımdan önce gerçek OST → PST başarısı doğrulanmış sayılmaz.

Kaynaklar: [Microsoft: Outlook profili oluşturma](https://support.microsoft.com/en-us/outlook/create-an-outlook-profile), [Microsoft: Windows için Outlook'a hesap ekleme](https://support.microsoft.com/en-us/outlook/getstarted/add-an-email-account-to-outlook-for-windows).
