# BitigMail — Tasarım Sistemi (Design System)

Bu belge, BitigMail P1 tıklanabilir prototipi için kabul edilen konsept görsellerinden (`design/concepts/bitigmail-workspace-v1.png`, `bitigmail-job-center-v1.png`, `bitigmail-preflight-v1.png`) ve resmi marka varlığından (`design/assets/bitigmail-mark-v1.png`) türetilmiş tasarım belirteçlerini (tokens) ve bileşen ailesini tanımlar.

---

## 1. Tasarım İlkeleri

1. **Saf Beyaz Yüzeyler:** Profesyonel kurumsal masaüstü hissi için ana çalışma alanı ve paneller saf beyaz (`#ffffff`) ve çok açık gri (`#f8fafc`) arka planlar kullanır.
2. **Turuncu Birincil Eylem:** Ana eylemler (Ön kontrolü çalıştır, Aktarımı başlat, Yeni iş) kabul edilen konsepte yaklaştırılmış turuncu (`#e97c24`) ile vurgulanır.
3. **Sarı Etkin / Seçim Vurgusu:** Aktif sekmeler ve seçili tablo satırları açık sarı / amber tonları (`#fef3c7` zemin, `#f3c544` vurgu) ile gösterilir.
4. **Koyu Okunaklı Metin:** Ana metin kabul edilen konseptteki sıcak antrasit tona (`#292824`) çekilmiştir.
5. **Kod-Yerel Arayüz:** Ekran görüntüleri arayüz olarak kullanılmaz; tüm metinler, tablolar, butonlar ve form elemanları yerel React bileşenleridir.

---

## 2. Renk Belirteçleri (Color Tokens)

### 2.1. Zemin ve Yüzeyler
| Belirteç | Değer | Kullanım Alanı |
|---|---|---|
| `--bg-white` | `#ffffff` | Kartlar, tablolar, paneller, üst menü |
| `--bg-subtle` | `#f8fafc` | Alt durum çubuğu, tablo başlıkları, pasif alanlar |
| `--bg-hover` | `#f1f5f9` | Liste/tablo üzerine gelme (hover) |
| `--border-light` | `#e2e8f0` | Panel sınırları, tablo ayraçları |
| `--border-mid` | `#cbd5e1` | Giriş kutuları, buton sınırları |

### 2.2. Metin Renkleri
| Belirteç | Değer | Kullanım Alanı |
|---|---|---|
| `--text-main` | `#292824` | Başlıklar, birincil içerikler, satır metinleri |
| `--text-muted` | `#66645f` | İkincil açıklamalar, tarihler, ek bilgisi |
| `--text-light` | `#94a3b8` | Placeholder, pasif ikonlar, ayraçlar |

### 2.3. Marka Renkleri
| Belirteç | Değer | Kullanım Alanı |
|---|---|---|
| `--brand-orange` | `#e97c24` | Birincil butonlar, ana eylemler |
| `--brand-orange-hover` | `#f29243` | Birincil buton hover durumu |
| `--brand-orange-light` | `#fff7ed` | Turuncu buton hover zemini, hafif uyarılar |
| `--brand-yellow` | `#f3c544` | Aktif sekme alt çizgisi, durum rozetleri |
| `--brand-yellow-light` | `#fef3c7` | Seçili satır zemini, uyarı kutuları |
| `--brand-yellow-border` | `#fde68a` | Uyarı kutusu kenarlığı |

### 2.4. Durum Renkleri
| Belirteç | Değer | Kullanım Alanı |
|---|---|---|
| `--status-success` | `#16a34a` | Başarılı ön kontrol, tamamlanan aktarım |
| `--status-success-bg` | `#ecfdf5` | Başarı kutusu zemini |
| `--status-warning` | `#d97706` | Boyut sınırı uyarısı, karar bekleyen öğe |
| `--status-warning-bg` | `#fffbeb` | Uyarı kutusu zemini |
| `--status-error` | `#dc2626` | Başarısız öğeler, sıfır kapsam engeli |
| `--status-error-bg` | `#fef2f2` | Hata kutusu zemini |

---

## 3. Tipografi (Typography)

Sistem fontu `Segoe UI, -apple-system, BlinkMacSystemFont, Roboto, sans-serif` kullanılır. Ek ağ fontu indirilmez.

| Kademe | Boyut | Ağırlık | Satır Yüksekliği | Örnek Kullanım |
|---|---|---|---|---|
| H1 (Sayfa Başlığı) | `1.5rem` (24px) | 700 (Bold) | 1.25 | "Posta geçişi", "İş merkezi", "Ön kontrol" |
| H2 (Bölüm Başlığı) | `1.25rem` (20px) | 700 (Bold) | 1.3 | "Başlatmadan önce çözülmeli", "İş detayları" |
| H3 (Alt Başlık) | `1.125rem` (18px) | 700 (Bold) | 1.4 | "Proje teslim belgeleri" (Önizleme) |
| Panel Başlığı | `1.05rem` (16.8px) | 700 (Bold) | 1.4 | "Kaynaklar", "İletiler", "Aktarım planı" |
| Gövde Metni (Body) | `0.875rem` (14px) | 400 (Regular) | 1.5 | İleti gövdesi, tablo hücreleri, liste metinleri |
| Alan Etiketi (Label) | `0.8125rem` (13px) | 600 (Semi-bold) | 1.4 | "KAYNAK", "HEDEF", "KLASÖR EŞLEMESİ" |
| Küçük / Yardımcı | `0.75rem` (12px) | 400 / 600 | 1.4 | Alt durum çubuğu, rozetler, dosya boyutu |

---

## 4. Logo ve Marka Varlığı

- **Kaynak Belge:** `design/assets/bitigmail-mark-v1.png`.
- **Uygulama:** İki katlanmış belgeyle 'B' harfini simgeleyen sarı ve turuncu raster logo.
- **Kullanım Yolu:** Kod içinde `/bitigmail-mark-v1.png` üzerinden doğrudan çağrılır. Beyaz zemin üzerinde 32×32 piksel olarak sol üst başlıkta konumlandırılır.

---

## 5. Bileşen Ailesi

1. **Header (Üst Menü):**
   - Sabit 56px yükseklik, beyaz zemin, alt sınır çizgisi.
   - Logo + "BitigMail" marka başlığı.
   - 5 sekme: İş merkezi (`nav-tab-jobs`), Müşteriler (`nav-tab-clients`), Aktarım ve dönüşüm (`nav-tab-transfers`), Arşiv ve arama (`nav-tab-search`), Raporlar (`nav-tab-reports`).
   - Aktif sekmede 3px sarı (`#f3c544`) alt vurgu çizgisi.
   - Sağda ayarlar butonu.
2. **Müşteriler Dizini (Client Directory View):**
   - Şirket kartları grid düzeni (şirket kodu, proje & kaynak sayıları, açıklama, iletişim).
   - Şirket detay görünümü: Projeler ve projeye bağlı kaynaklar tablosu (Posta kutusu vs Dosya arşivi rozetleri).
   - "İşlem başlat" aksiyonu ile bağlam taşıma.
   - Modal formlar: Yeni şirket, yeni proje, yeni kaynak ekleme (sm: 440px, md: 520px, lg: 680px, xl: 840px).
3. **Aktarım ve Dönüşüm Sekmesi (Transfers Tab View):**
   - Üst bağlam çubuğu (`transfer-context-bar`): Müşteri, Proje, Kaynak, Hedef seçicileri ve 4 operasyon butonu (Taşıma, Dönüştürme, Arşivleme, Kurtarma).
   - Taşıma seçildiğinde uçtan uca çalışma alanı, ön kontrol ve simülasyon akışını barındırır.
4. **Breadcrumbs (Ekmek Kırıntısı):**
   - Hiyerarşi: Müşteriler / [Müşteri Adı] / [İş Adı].
5. **Üç Panelli Çalışma Alanı (3-Column Workspace):**
   - Sol Panel (260px): Kaynak ağacı (klasörler, hesaplar, '+' butonu).
   - Orta Panel (Esnek flex-1): Arama, tarih/ek filtreleri, ileti listesi tablosu ve altında ileti önizleme penceresi.
   - Sağ Panel (320px): Aktarım planı (kaynak, hedef, eşleme, kapsam, yinelenen kuralı, turuncu ön kontrol butonu).
   - Masaüstünde kabuk `100dvh` içinde sınırlandırılır; ileti tablosu kendi içinde kayar, önizleme ile sağ panel eylemi ilk viewport içinde kalır.
6. **Ön Kontrol Stepper:**
   - 4 adımlı yatay süreç: 1. Kaynak ve hedef (✓) → 2. Kapsam (✓) → 3. Ön kontrol (Aktif 3) → 4. Aktarım (4).
7. **İki Kolonlu Ön Kontrol Ekranı:**
   - Sol Bölüm: Kontrol listesi ve 38 MB boyut engeli çözüm kutusu.
   - Sağ Bölüm: Aktarım özeti ve nihai kapsam göstergesi.
   - Sıfır kapsam guard'ı (`scope_guard`): 0 ileti olan kaynakta aktarımı engeller.
   - Alt Butonlar: Sol altta "Plana dön", sağ altta "Aktarımı başlat".
   - Gövde gerektiğinde kendi içinde kayar; alt eylem şeridi belgeyi kaydırmadan görünür kalır.
8. **Aktarım Ekranı (Simulation View):**
   - İlerleme çubuğu (%42 / %100).
   - Tekil öğe sayım kartları: Toplam, Aktarılan, Atlanan, Başarısız, Bekleyen.
   - Duraklat/Devam et ve Yeniden dene butonları.
   - JSON / CSV indirme aksiyonları.
9. **Arşiv ve Arama (Archive Search View):**
   - Sol Panel (300px): Konum seçim hiyerarşi ağacı (Şirket → Proje → Kaynak).
   - Tümünü seç / Temizle / İndeterminate kısmi seçim desteği.
   - Sağ Panel: Arama çubuğu, klasör filtresi, birleşik sonuçlar tablosu (konum rozetleriyle) ve ileti önizleme.
10. **Alt Durum Çubuğu (Status Bar):**
    - Sabit 32px yükseklik, klasör ikonu + güncel kapsam ve sekme durumu + "v0.2 · Tasarım taslağı".
