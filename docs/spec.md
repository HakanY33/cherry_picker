# MIP Hizmet & Kiralama Yönetim Sistemi — Tasarım Dokümanı

**Faz 1:** Cherry Picker (mobil vinç) kiralama
**Faz 2:** Genel hizmet kiralama + Oracle entegrasyonu + muhasebe akışı

---

## 1. Sistemin özeti

Bugün aynı veri en az 5 kez yazılıyor: talep Excel'i → alt yüklenici fişi → form üzerine saatler → Ekipman Dept aylık Excel'i → Bütçe hakediş tablosu. Sistemin amacı bu zinciri **tek girişe** indirmek ve mutabakatı otomatikleştirmek.

**Temel model — vinç de fiber de aynı yapı:**

```
Sözleşme → Hizmet kalemi (birim + birim fiyat + kural)
        → Çalışma kaydı → Kalem miktarları → Tutar
```

Vinç, bu modelin birimi `HOUR` olan özel hâlidir. Fiber kablolama `METER`, sonlandırma `PIECE`. Yeni hizmet eklemek **veri girişidir, kod değişikliği değildir.**

---

## 2. Teknoloji kararları

| Katman | Seçim | Gerekçe |
|---|---|---|
| Backend | C# / ASP.NET Core Web API | MIP IT'nin mevcut ekosistemi, AD/Entra SSO, Oracle sürücüsü, devir teslim kolaylığı |
| Frontend | React + TypeScript, responsive tek uygulama | Saha = telefon tarayıcısı, ofis = masaüstü. Uygulama indirme zorunluluğu yok. |
| Veritabanı | SQL Server | Kurumsal standart |
| PDF | QuestPDF (server-side) | Lisans ücretsiz, HTML→PDF dönüşüm derdi yok |
| Auth | MIP: AD/Entra SSO — Firma: firma admini + alt kullanıcılar | |

**Mobil uygulama yok.** Alt yüklenici cihazına APK kurdurmak saha gerçekliğine aykırı; PWA/responsive web yeterli.

---

## 3. İki ayrı belge, iki ayrı yaşam döngüsü

Bunları tek tabloda birleştirmek en kritik hata olur.

| | **Request (Talep)** | **WorkRecord (Çalışma Kaydı)** |
|---|---|---|
| Ne zaman | İş **öncesi** | İş **sonrası** |
| Kim açar | MIP'li talep eden | Alt yüklenici (veya MIP) |
| Ne taşır | İhtiyaç, tahmini süre | Gerçekleşen miktar |
| Sonucu | Onay / iş emri | Hakediş |

İlişki **opsiyonel**: `WorkRecords.RequestId` NULL olabilir. Fiber örneğinde talep açılmadan doğrudan aylık çizelge ile gelen işler var.

---

## 4. Durum makinesi (State Machine)

### 4.1 Belge durumu (`Status`)

```
DRAFT ──submit──> SUBMITTED ──> PENDING(step 1..n)
                                   │
                    ┌──────────────┼──────────────┐
                    │              │              │
                 APPROVED      REJECTED    REVISION_REQUESTED
                    │                             │
                    │                             └──> DRAFT (yeni versiyon)
                    │
              (dönem kapanır)
                    │
                 LOCKED
```

Ek durum: `CANCELLED` — iş yapılmadan iptal.

**Kurallar:**
- `DRAFT` dışında hiçbir durumda doğrudan düzenleme yapılamaz. Düzeltme = `RevisionOfId` ile yeni kayıt, eskisi `IsSuperseded = 1`.
- `APPROVED` olan kayıt silinemez. Hiçbir kayıt fiziksel olarak silinmez.
- Dönem kapalıysa (`Periods.Status = CLOSED`) o döneme kayıt girilemez, mevcutlar düzenlenemez. Kilidi açmak `ReopenReason` zorunlu ve loglanır.
- Onay zinciri `ApprovalFlowSteps`'ten okunur, kodda sabit değildir.
- Onay gelmezse **otomatik onay yoktur**. `ReminderAfterHours` → hatırlatma, `EscalateAfterHours` → üst amire eskalasyon.

### 4.2 Entegrasyon durumu (`IntegrationStatus`) — ayrı alan

```
NOT_SENT → SENT → CONFIRMED
              ↓
            FAILED → (retry) → SENT
```

Oracle çöktüğünde belgenin onay durumu bozulmasın diye ayrı tutulur. Faz 1'de hep `NOT_SENT` kalır.

---

## 5. Fiyatlandırma motoru

**İlke: parametrik, formül motoru değil.** Kullanıcı serbest formül yazmaz; `ContractLines` üzerindeki alanları doldurur, motor uygular. Test edilebilir, denetlenebilir, 2 yıl sonra da anlaşılır kalır.

### 5.1 Hesap sırası

```
1. Ham miktar (RawQuantity)
   HOUR ise: EndTime - StartTime (gece vardiyasında +1 gün)
   Diğer birimlerde: kullanıcının girdiği miktar

2. Yuvarlama (RoundingRule)
   UP_30 → 7h10dk = 7.5h    NEAREST_60 → 7h10dk = 7h

3. Minimum (MinBillableQuantity)
   min 4 saat kuralında 2 saatlik iş 4 saat faturalanır

4. Gün eşiği (DayThresholdHours + DailyPrice)
   8 saati aşarsa saatlik yerine günlük tarife uygulanır

5. Tutar
   LineAmount = BillableQuantity × UnitPriceSnapshot

6. Ek ücretler (ContractLineSurcharges)
   Mesai / gece / hafta sonu çarpanı → SurchargeAmount

7. Sabit bedel (MobilizationFee) eklenir
```

### 5.2 Doğru sözleşme satırını bulma

```sql
WHERE ContractId = @contractId
  AND ServiceId  = @serviceId
  AND (VariantId = @variantId OR (VariantId IS NULL AND @variantId IS NULL))
  AND ValidFrom <= @workDate
  AND (ValidTo IS NULL OR ValidTo >= @workDate)
  AND IsActive = 1
```

**Tarih kriteri `WorkDate`'tir**, kaydın girildiği tarih değil. Mart'ta zam yapıldıysa Şubat işi Şubat fiyatından hesaplanır.

### 5.3 Snapshot zorunluluğu

Hesap yapıldığı anda `UnitPriceSnapshot` ve `PricingRuleSnapshot` (JSON) satıra kopyalanır:

```json
{
  "contractLineId": 42,
  "unitPrice": 1250.00,
  "roundingRule": "UP_30",
  "minBillableQuantity": 4,
  "dayThresholdHours": 8,
  "rawQuantity": 7.17,
  "afterRounding": 7.5,
  "afterMinimum": 7.5,
  "appliedTariff": "HOURLY"
}
```

Bu olmadan "bu 8 saat neden bu tutar" sorusu 6 ay sonra cevaplanamaz.

### 5.4 Fiyat değişikliği

Fiyat **güncellenmez**. Eski satırın `ValidTo`'su kapatılır, yeni satır açılır. Geçmiş hakediş asla değişmez.

---

## 6. Kullanıcı arayüzleri

### 6.1 Firma yüzü (External Portal)
- Sadece kendi firmasının verisi görünür (satır bazlı yetki, uygulama katmanında zorunlu)
- Excel'e benzeyen satır girişi ekranı: tarih, lokasyon, hizmet, miktar
- Kendi kayıtlarının durumu ve aylık toplamı
- İtiraz / revizyon talebi
- Firma admini kendi alt kullanıcılarını açar → **kim ne girdi görünür**, MIP IT'ye yük binmez

### 6.2 MIP yüzü (Internal Dashboard)
- Onay kutusu: bekleyen belgeler, "Onayla / Reddet / Revize İste"
- Toplu onay (satır bazlı itiraz mümkün — 40 satırdan 1'ine itiraz tüm ayı bekletmez)
- Aylık icmal, otomatik tutar, Excel/PDF export
- Anomali ekranı: vardiya sınırını aşan kayıtlar, çakışan araç kullanımı, mükerrer şüphesi
- Sözleşme ve fiyat yönetimi (yetkili roller)
- Dönem kapatma / açma

### 6.3 Ortak
- Türkçe varsayılan, i18n altyapısı hazır
- Responsive: sahada telefon, ofiste masaüstü
- Saat bilgisi **sunucudan**, istemci saati asla yazılmaz

---

## 7. PDF ve resmiyet

- Onaylanan belge PDF olarak üretilir, mevcut kâğıt formun görünümüne yakın
- Üzerinde: belge no + doğrulama kodu/QR
- İsteyen çıktı alıp mühürler — kimse alışkanlığından vazgeçmek zorunda kalmaz
- Üretilen PDF `GeneratedDocuments`'a hash'iyle kaydedilir; şablon sonradan değişse bile eski belge değişmiş görünmez

**Fiş fotoğrafı saklanmaz.** Dış fişin sadece dijital ikizi tutulur: `ExternalReceiptNo` + `ExternalReceiptDate` + operatör adı. İhtilafta "0078 numaralı fişiniz" denebilir, sunucu şişmez.

---

## 8. Mevcut formlardan çıkan doğrulama kuralları

Eldeki kâğıtlarda tespit edilen gerçek hatalar, sistemde engellenecek:

| Bulgu | Kural |
|---|---|
| Dönem 2026/02, iş tarihi 2/19/**2025** yazılmış | `WorkDate` dönem aralığı dışındaysa kabul edilmez |
| Aynı form iki kez taranmış | `(Firma, Tarih, Plaka, Başlangıç)` çakışmasında **uyarı** (blok değil) |
| Hizmet veren tarafın tüm alanları boş, form yine akmış | Gönderim öncesi zorunlu alan kontrolü |
| Sıra no 1,2,3,4,**4**,5 | Satır numarası sistem tarafından üretilir |
| Bitiş saati başlangıçtan küçük | Gece vardiyası onayı (`SpansMidnight`) istenir |

---

## 9. Faz planı

**Faz 1 — Cherry Picker MVP**
1. Veritabanı + master data ekranları (firma, hizmet, lokasyon, sözleşme)
2. Fiyatlandırma motoru + birim testleri
3. Çalışma kaydı girişi (firma yüzü)
4. Onay akışı + bildirimler (MIP yüzü)
5. PDF üretimi + aylık icmal export
6. Pilot: tek alt yüklenici, 1 ay, kâğıtla paralel yürütme + fark raporu

**Faz 2**
- Diğer hizmet tipleri (fiber, CAT6, genel kiralama)
- Oracle entegrasyonu (`IntegrationQueue` üzerinden)
- Muhasebe / e-fatura eşleştirme
- Raporlama ve dashboard
- Talep (Request) akışının tam devreye alınması

---

## 10. Açık kalan konular

Bunlar şemayı **değiştirmez**, sadece konfigürasyon değeri olarak doldurulacak:

1. Onay zinciri kaç adım, hangi roller — `ApprovalFlowSteps` verisi
2. Dönem kapanış günü ve yetkilisi — `Periods` verisi
3. Belge numarası serisi (mevcut seriye devam mı, yeni seri mi) — `DocumentSeries` verisi
4. Mesai/gece/hafta sonu farkı var mı — `ContractLineSurcharges` verisi (yoksa boş kalır)
5. Sözleşmede ıslak imzalı fiş şartı var mı — süreç kararı, sistem her iki durumda da çalışır
