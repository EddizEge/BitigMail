TASK_ID: TASK-007
STATUS: DONE — synthetic EML/MBOX corpus verified; genuine OST/PST still not produced
FROM: ASTRA HIGH
TO: SOL 5.6 LIMITED

USER AUTHORIZATION:
Kullanıcı küçük OST örneği bulunmadığını ve 'deneme verisi hazırlayalım' dedi. Gerçek müşteri verisi yerine yapay içerikli bilinen sonuçlu küçük veri seti şimdi hazırlanacak. Yeni hesap açma, istemci kurma veya müşteri verisi erişimi yok.

GOAL:
fixtures/mail-corpus-v1/ altında tekrar üretilebilir küçük EML + MBOX veri seti, bağımsız beklenen özellik/ek hash manifesti ve README üret. scripts/generate_mail_corpus.py stdlib Python ile yeterlidir; runtime gerekirse mevcut bundled Python'u kullan. Gemini uygulasın, SOL terminalde üretip doğrulasın. prototype/ değişmez.

SPEC:
- Tam 12 fiziksel EML kayıt; bunlardan ikisi byte-identical duplicate pair olsun (11 özgün ham içerik). Ayrı bir ikili Message-ID aynı ama içerik farklı olsun; yalnız Message-ID ile tekilleştirmenin yanlışlığını göstermek için.
- 3 mantıksal klasör: Gelen Kutusu, Gönderilenler, Projeler/İstanbul; toplam kayıt dağılımı manifestte belirtilsin. Örnek gönderen/alıcı alan adları yalnız .example kullanır; gerçek kişisel adres/içerik yok.
- 2022–2025 arasında sabit tarihler, en az 2 açık saat dilimi offset'i; Türkçe Unicode konu/gövde ve klasör; düz metin ve text+HTML alternatif gövde; güvenli sabit HTML (script, uzak resim/url tracking yok).
- En az 3 ek: UTF-8 Türkçe dosya adı olan metin, deterministik binary bytes, küçük generated in-memory PNG (geçerli sabit fixturebytes; ağdan indirme yok). SHA256/byte uzunluğu manifestte. Ek içeriğinde gerçek veri yok. Bir multipart-related inline PNG/CID vakası olsun.
- Bir gövdede satır başında 'From ' ve '>From ' örnekleri olsun; MBOX yazma/okuma escaping semantiğini README açıkça belirlesin. MBOX unescape farkını EML ham hash eşliği zorlamayla gizleme; semantik gövde ve ek hash kontrolü yap.
- Her kayıtta stable fixtureId, relative EML path, folder path, Message-ID, ISO date+offset, expected subject/from/to, body text, rawSHA256 ve byte size; ekler filename/contentType/SHA256/size. Tam aynı raw duplicate iki ayrı fiziksel öğe olarak manifestte işaretli. SameMessageID farklıiçerik ikilisi ayrı işaretli.
- En az 2 filtre oracle: tarih aralığı [2024-01-01T00:00Z,2025-01-01T00:00Z) ve ekli iletiler; manifest içinde beklenen fixtureId listeleri açık ve doğrulanabilir. Hepsini çalışma sırasında aynı filtre fonksiyonunun çıktısı diye türetmek yerine beklenen vakalar tasarımda sabitlensin.
- Veriler küçük (<1MB toplam), standart kütüphane, sabit boundary/date/content/hash ile deterministik. Dosya adları Windows'a uygun.
- README açıkça EML/MBOX corpus olduğunu, geçerli OST/PST henüz üretilmediğini, OST dönüşüm/kurtarma/100GB desteğini kanıtlamadığını yazsın. Outlook OST'ye dönüştürme için ileride test posta kutusuna bu corpusun aktarılması→sync→Outlook kapalıyken dosya kopyası→orijinal hesabın olmadığı izole test ortamı akışını yalnız plan olarak anlat. Yeni bağlantı/hesap oluşturma yapılmaz.

VERIFICATION:
Python runtime ile generator çalıştır; iki ayrı geçici task-owned dizine tekrar üretip file hash listesinin eşitliğini kontrol et. Son corpus MIME parse defects olmadan okunur; manifestteki 12 fiziksel kayıt, 11 özgün raw içerik, duplicate/same-ID ayrımı, ek hashleri, beklenen filtre ID'leri ve MBOX'tan 12mesaj + From satırları/gövde/ek roundtrip doğrulansın. Mimari beyanı yeterli değil gerçek komut/sonuç kaydı olsun. Mail app/auth/API/Outlook deneyi NOT RUN.

CONSTRAINTS:
Mevcut SOL→official agy headlessGemini; yeni Codex ajanı/görevi yok; no computer-use. Stdlib script, fixtures ve taskresult harici edit yok. Dosyaları internete yükleme, yeni paket/SDK kurma, mevcut kullanıcı mail dosyalarını arama. Geçici kendi doğrulama dizinlerini güvenli sınır içinde temizleyebilirsin; kullanıcı dosyalarına dokunma. ROADMAP/ürün belgeleri Astra'da.

DELIVERY:
fixtures/mail-corpus-v1/README.md ve manifest.json; EML dosyaları, corpus.mbox; scripts/generate_mail_corpus.py; .codex-coordination/results/TASK-007.md komutlar/kanıt/sınırlamalar. Sonuçta gerçek OST örneğinin hâlâ hazırlanması gerektiği net olsun.
