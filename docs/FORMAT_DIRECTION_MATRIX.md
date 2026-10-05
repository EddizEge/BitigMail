# Format and direction matrix

This matrix describes implemented routes only. It is not an all-formats conversion promise.

| Source | Accepted interpretation | Implemented targets | Qualification / blocker |
|---|---|---|---|
| EML file or EML tree | RFC MIME files | IMAP, managed local archive, PST through the existing qualified MIME import | A BitigMail normalization manifest is verified when present; its warnings, partial status and date-filter restriction remain frozen through preview, plan, resume and reports. Ordinary EML without that manifest is not presumed to be OLM/EMLX-derived. |
| mboxrd file | Explicit `mboxrd` selection only | IMAP, managed local archive, PST through the existing qualified MIME import | Strict mboxrd validation and exactly-one unescape. Filename extension does not prove a dialect. |
| mboxo | Not accepted as mboxrd | None | Classic escaping is ambiguous for a source line already beginning `>From`; unsupported rather than advertised as reversible. |
| mboxcl / mboxcl2 | Unsupported | None | A `Content-Length` header alone does not identify the dialect; bounded-byte semantics are not implemented. |
| PST / OST | Healthy Outlook extraction (recovery is a separate route) | Qualified EML tree and existing PST split/convert routes | No PST/OST output fidelity claim beyond the route-specific reports. |
| OLM | Qualified mail-only normalization | Verified EML tree, then only existing EML downstream routes | Schema-1 manifest freezes original XML SHA-256. Original date semantics remain unresolved, so downstream date filters are blocked. Contacts, calendar and tasks are outside this mail-only output. |
| EMLX file / complete self-contained Apple Mail package | Qualified EML normalization | Verified EML tree, then only existing EML downstream routes | Sidecar metadata hash is verified. Original date/flag semantics remain unresolved, so downstream date filters are blocked. A `.mbox` directory is not treated as a flat mboxrd file. |
| POP account | Immutable UIDL/LIST source snapshot | Verified EML tree | Source-only. No POP target, DELE, folder, internal-date or read-flag claim. |
| IMAP account | Selected folders/messages | Verified EML tree or folder-per-file mboxrd export | Server internal dates/flags/keywords are retained in the sidecar manifest; file containers do not natively encode them. |
| IMAP account | Selected folders/messages | Another configured IMAP account | Existing IMAP→IMAP copy route; immutable preview and route-specific verification apply. |

Generated EML trees are reusable only after `NormalizedSourceQualificationReader` verifies bounded paths, hashes and recognized manifests. Date restrictions and fidelity warnings are not converted into permissions and are not discarded by archive ingestion or later search.

## Gelişmiş filtre durumu — 21 Eylül 2026, Aşama 7 yerel kabulü tamamlandı

| Akış | Motor durumu | Kullanıcı ekranı / sınır |
|---|---|---|
| EML/mboxrd→PST | Sürümlü VE/VEYA filtresi tam MIME üzerinden; kayıtlı fiziksel seçim | Türkçe düzenleyici, şablon, klasör eşleme ve yinelenme politikası bağlandı; gerçek PST çıktısı yeniden açıldı |
| IMAP→IMAP | Konu/gövde/gönderen/alıcı/ek adı/ek varlığı/boyut/UTC MIME Date; önizleme, kayıtlı plan, yazma öncesi kontrol, rapor | Ekran bağlı; gerçek Outlook pilotu 10. aşamada |
| EML/mboxrd→IMAP | Aynı kurallar; kaynağın tarih kısıtı korunur; önizleme ve çalıştırma aynı tam MIME girdisini kullanır | Ekran bağlı; politika değişikliği yeni önizleme gerektirir |
| IMAP→EML/mboxrd | Aynı kurallar; yalnız seçilen iletiler, gerçek çıktı yeniden açılarak test edildi | Ekran bağlı |
| Arşiv→seçili EML çıktısı | Gerçek arama öğeleri, kaynak/kapsam/özet kontrolü, klasör eşleme ve açık yinelenme politikası; doğrulama sonrası yayın | Tam ve hash doğrulanmış MIME üzerinden arama; çok arşivli seçim ve önizleme tutarlılığı doğrulandı |
| POP | Sunucudaki iletileri silmeden doğrulanmış EML anlık görüntüsü | Doğrudan gelişmiş filtre yok; önce EML'e alıp desteklenen akışta filtrele |
| PST/OST/OLM/EMLX | Kaynağı nitelikli EML'e normalleştirip desteklenen akışta filtreleme | Doğrudan gelişmiş AST desteği varsayılmaz; kaynak tarih uyarıları EML'e geçince kaybolmaz |
| Hasarlı PST/OST | Ayrı salt-okunur işçi→doğrulanmış EML, bilinen/kısmi/okunamayan sonuç ayrımı | Kurtarma manifesti sonraki akışta doğrulanır; bilinmeyen toplam ve tarih kısıtı korunur |

Filtrede `date`, kaynak MIME Date başlığının açık saat dilimli anlamıdır; IMAP sunucusunun internal received date'i veya POP'ta bulunmayan metadata yerine geçirilmez. Bilinmeyen alan değerlendirmesi ayrı sayılır. Gövde filtresi ekran/indeks metninin kesilmiş sürümünü kullanamaz; HTML-only iletide görünür metin/token çözümleme kullanılır, script/style içeriği eşleşme sayılmaz. Filtrede gövde kuralı yoksa gereksiz büyük gövde çözümlemesi yapılmaz.

7. aşama kabulü: TASK037C kapanışında 725 motor ve 151 arayüz testi; gerçek MIME filtre/eşleme/şablon/PST raporu, seçili arşiv çıktısı ve kuyruk önceliği ekran kontrolleri geçti. Yerel MIME politikası testlerinde gerçek PST çıktıları 3/1/2 fiziksel iletiyle yeniden açıldı. Bunlar harici Google/Outlook kabulü veya 100 GB hız/bellek garantisi değildir. Kaynak uyarıları, SDK deneme işaretleri ve tarih sınırlamaları sürer.
