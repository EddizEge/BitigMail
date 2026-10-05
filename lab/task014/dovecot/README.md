# TASK-014 Dovecot kabul laboratuvarı

Bu ikinci laboratuvar GreenMail'in öğlen 12:00 INTERNALDATE farkını uygulamaya taşımadan gerçek IMAP aktarımını sınamak için kuruldu. GreenMail 4143 ve eski Outlook laboratuvarı değiştirilmez.

- Adres: yalnız `127.0.0.1:5143`; SMTP servisi yok.
- Konteyner: `bitigmail-task014-dovecot`; veri volume: `bitigmail-task014-dovecot-data`.
- Kullanıcılar: `source`, `target`; secret değerleri yerel `local-credentials.json` ve Docker'a salt okunur bağlanan `users` dosyasındadır. Bu dosyalar Git ve Docker build context'inden hariçtir, terminale basılmaz.
- Debian bookworm tabanı SHA256 `88200866dfff7ea7f5cbcb6ec7c8a701889efe6fe859fe64d6990e4b07ea4171`.
- Kurulan Dovecot: Debian `1:2.3.19.1+dfsg1-2.1+deb12u6`.
- Kabulde kullanılan yerel image ID: `sha256:838d90983c27688591a4a905694a798462543d9a328ed1858309bd2a8bd4ab3a`. Start betiği bu image'a sabitlenmiştir. Yeniden build sonucu farklıysa eski image/veri silinmeden yeniden kabul gerekir.
- Klasör ayıracı `/`: INBOX, Gönderilenler, Projeler/İstanbul. Kaynak 12 fiziksel ileti, 4 ek, 1 inline CID. İki aynı bayt kopya ve iki aynı Message-ID farklı içerik korunur.

Proje kökünden:

```powershell
pwsh -NoProfile -File lab/task014/dovecot/start.ps1
python -X utf8 lab/task014/scripts/seed_and_verify_task014.py --credentials lab/task014/dovecot/local-credentials.json --port 5143 --output-seed-manifest lab/task014/dovecot/seed-manifest.json
.tools/dotnet/dotnet.exe run --project lab/task014/harness/BitigMail.Task014GateAHarness.csproj -c Release -- --dovecot
```

Seed yalnız boş kaynağa yazar; dolu ve tam kaynakta doğrular, kısmi/farklı kaynağı temizlemez. Hedef asla bu seed betiğiyle doldurulmaz veya silinmez. Gate A her çalıştırmada yeni rastgele hedef kökü oluşturur; eski deney çıktıları korunur. Konteynerin durdurulması/yeniden başlatılması gerekiyorsa aktif aktarım olmadığını ve konteyner sahiplik etiketini doğrula; volume veya kaynakları silme.

Root Gate A ve bağımsız Python kontrolü 12/12 + 4 ek + tarih/flag/hash PASS. Detay: [IMAP_TRANSFER_VALIDATION](../../../docs/IMAP_TRANSFER_VALIDATION.md). Bu küçük laboratuvar deneyi büyük kutu veya sağlayıcı entegrasyonu desteği anlamına gelmez.
