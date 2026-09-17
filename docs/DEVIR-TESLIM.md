# Devir-Teslim Dokümanı — MipRental

Bu belge sistemi devralan ekip içindir. Kurulum ve çalıştırma `README.md`'de;
burada **sistem nasıl çalışıyor, nereye dokunulur, neye dokunulmaz** anlatılır.

Tek cümlelik özet: MipRental, alt yüklenici hizmet kiralamasının mali kaydını
tutar. Bu yüzden tasarımın her yerinde tek bir öncelik vardır — **onaylanmış bir
tutar bir daha değişmez, ve her değişikliğin izi kalır.** Aşağıdaki mekanizmaların
çoğu bu tek cümlenin uygulamasıdır.

---

## 1. Mimari özet

### Dört proje, tek yönlü bağımlılık

```
MipRental.Web  ──►  MipRental.Data  ──►  MipRental.Domain
      │                    │                    ▲
      └────────────────────┴────────────────────┘
                                                │
                            MipRental.Tests ────┘  (üçüne de bağımlı)
```

| Proje | İçerik | Neyi BİLMEZ |
|---|---|---|
| **MipRental.Domain** | Varlıklar, enum'lar, üç durum makinesi, fiyatlandırma çekirdeği, rol kodları, istisnalar. | Veritabanı, HTTP, oturum, `DateTime.UtcNow`. Hiçbir NuGet paketi yok. |
| **MipRental.Data** | `AppDbContext`, EF konfigürasyonları, migration'lar, üç interceptor, servisler (türetme, onay, hakediş, belge, mail kuyruğu), `ContractLineResolver`, `ApprovalFlowResolver`. | HTTP, view, yetki politikaları. |
| **MipRental.Web** | Controller'lar, Razor view'lar, yetki politikaları, `CurrentUser`, PDF (QuestPDF) ve Excel (ClosedXML) üretimi. | — (en dış katman) |
| **MipRental.Tests** | xUnit, 844 test. EF InMemory + SQLite in-memory; gerçek veritabanı gerekmez. | — |

**Ayrı bir API katmanı YOKTUR.** Tek çalıştırılabilir proje `MipRental.Web`'dir;
dinamik UI ihtiyaçları htmx ile karşılanır. SPA framework'ü yoktur ve
eklenmemelidir.

### Domain neden bu kadar yalın

Durum makineleri ve fiyat hesabı **saf**tır: karar için gereken her şey (kayıt,
dönem, aktör, "şu an") parametre olarak gelir. Bunun bedeli birkaç fazla
parametre, karşılığı şudur: tüm geçiş matrisi ve tüm fiyat kuralları veritabanı
kurmadan test edilebilir. 844 testin ayağa kalkma süresinin ~19 saniye olmasının
sebebi budur. Bu ayrımı bozmayın — Domain'e `DbContext` ya da `IHttpContextAccessor`
girerse test paketi yavaşlar ve zamanla güvenilmez hâle gelir.

---

## 2. Beş kritik mekanizma

Sistemin iş kurallarını koruyan beş yer. Bir hata araştırırken ya da yeni bir
özellik eklerken **önce bunları okuyun** — neredeyse her kural bu beşinden
birine dayanır.

### 2.1 Firma izolasyonu — global query filter

**Dosya:** `src/MipRental.Data/AppDbContext.cs:74` (`ApplyFirmIsolationFilters`)

Alt yüklenici yalnızca kendi firmasının verisini görür (CLAUDE.md kural 7).
Kontrol **controller'da değil, DbContext seviyesinde**dir:

```csharp
modelBuilder.Entity<WorkRecord>()
    .HasQueryFilter(x => _currentUser.FirmId == null || x.FirmId == _currentUser.FirmId);
```

`FirmId == null` MIP personeli demektir; onlara her şey görünür. Firma
kullanıcısının açtığı **her** sorgu — liste, detay, sayım, PDF üretimi, rapor —
otomatik olarak filtrelenir.

Filtre uygulanan varlıklar: `WorkRecord`, `Request`, `Contract`, `Equipment`,
`User`, `ProgressPayment` (doğrudan `FirmId` ile) ve `WorkRecordLine`,
`RequestLine`, `ContractLine`, `ProgressPaymentRecord`, `ContractLineSurcharge`
(üst kayıt üzerinden). `Approval` polimorfiktir (`DocumentType` + `DocumentId`)
ve filtresi, ilgili belgenin kendi filtresine dayanır.

**Kural:** Yeni bir varlık firmaya ait veri taşıyorsa filtresi BURAYA eklenir.
`.IgnoreQueryFilters()` yazan bir kod görürseniz gerekçesini sorgulayın — bu,
firma izolasyonunu kapatan tek anahtardır.

**Testi:** `tests/MipRental.Tests/FirmIsolationTests.cs`

### 2.2 Üç interceptor — kurallar SaveChanges seviyesinde

**Dosyalar:** `src/MipRental.Data/Interceptors/`
**Kayıt yeri:** `src/MipRental.Web/Program.cs:95` (`AddInterceptors`)

Üçü de `SaveChanges` seviyesinde çalışır. Sebebi tek cümleyle: **hangi
ekrandan, hangi servisten, hangi migration'dan gelirse gelsin devre dışı
bırakılamasın.** Controller'a konan bir kontrol, ikinci bir controller
yazıldığında unutulur.

| Interceptor | Koruduğu kural | Ne yapar |
|---|---|---|
| `ImmutabilityGuardInterceptor` | Kural 1 — onaylanmış mali kayıt değişmez | `EntityState.Deleted`'ı tümden reddeder (tek istisna `UserRole`). `APPROVED`/`LOCKED` çalışma kaydı ve satırlarını güncellemeye/eklemeye izin vermez. İki dar istisna: `IntegrationStatus` alanı (Faz 2 Oracle) ve dönem kapanış/açılışının `APPROVED ↔ LOCKED` geçişi (yalnızca `Status` alanı). |
| `PeriodGuardInterceptor` | Kural 4 — kapalı döneme yazılmaz | `Period.Status = CLOSED` olan döneme kayıt girilmesini/değiştirilmesini engeller. Ayrıca `WorkRecord.WorkDate`'in bağlı olduğu dönemin yıl/ay aralığında olmasını zorlar. |
| `AuditSaveChangesInterceptor` | Kural 1/2 — her değişikliğin izi kalır | Her INSERT/UPDATE için **alan bazlı** `AuditLog` satırı üretir. INSERT audit'i asıl kayıtla **aynı transaction** içinde yazılır: audit yazılamazsa asıl kayıt da geri döner. `PasswordHash` maskelenir. `CreatedAt`/`UpdatedAt` hariç tutulur. |

`ImmutabilityGuardInterceptor` ve `PeriodGuardInterceptor` stateless olduğu için
singleton, `AuditSaveChangesInterceptor` oturum bilgisine ihtiyaç duyduğu için
scoped kayıtlıdır.

**Sistemde fiziksel silme yoktur.** Bir şeyi "silmek" isteyen bir talep gelirse
doğru cevap `IsActive = 0` ya da yeni versiyondur — interceptor'ı gevşetmek
değil.

**Testleri:** `ImmutabilityGuardInterceptorTests.cs`, `PeriodGuardInterceptorTests.cs`,
`AuditLogTests.cs`, `AuditAtomicityTests.cs`

### 2.3 Fiyat motoru — iki parça, bilinçli olarak ayrı

| Dosya | Sorumluluk |
|---|---|
| `src/MipRental.Data/Pricing/ContractLineResolver.cs` | **Hangi** fiyat satırı? (veritabanı gerekir) |
| `src/MipRental.Domain/Pricing/PricingCalculator.cs` | **Kaç para?** (saf hesap, veritabanı yok) |
| `src/MipRental.Domain/Pricing/RecordTotalCalculator.cs` | Satır tutarları + mobilizasyon → kayıt toplamı |

**Doğru satır nasıl seçilir** (`ResolveAsync`, `ContractLineResolver.cs:19`):
firma + hizmet + **varyant** + **işin yapıldığı tarih**. Dört koşulun hepsi
tam eşleşmedir:

```csharp
l.ServiceId == serviceId && l.VariantId == variantId   // varyant TAM eşleşir
&& l.ValidFrom <= workDate && (l.ValidTo == null || l.ValidTo >= workDate)
```

> **En sık düşülen tuzak:** `VariantId` eşleşmesi tamdır. Talep "60 Ton Sepetli"
> seçtiyse, varyantı `NULL` olan bir fiyat satırı **eşleşmez** ve
> `"… fiyatı tanımlı değil"` hatası alınır. Her varyant için ayrı fiyat satırı
> açılmalıdır.

Tarih `workDate`'tir — kaydın girildiği tarih değil (CLAUDE.md kural 3). Birden
fazla satır eşleşirse hata fırlatılır; keyfî seçim yapılmaz.

**Fiyatlandırma parametriktir, formül motoru DEĞİLDİR** (ADR-006). Kullanılabilen
parametreler: `UnitPrice`, `RoundingRule` (NONE / UP_15 / UP_30 / UP_60 /
NEAREST_15 / NEAREST_30 / NEAREST_60), `MinBillableQuantity`,
`DayThresholdHours` + `DailyPrice` (eşik aşılırsa günlük tarifeye geçer),
`MaxQuantityPerRecord`, `MobilizationFee` ve `ContractLineSurcharges`
(çarpan veya sabit tutar). Serbest formül yazılabilen bir kural motoru
kurulmayacaktır.

**Mobilizasyon bedeli satır değil KAYIT seviyesindedir** (ADR-014): çok satırlı
bir kayıtta aynı sefer için iki kez faturalanmaması gerekir. `PricingCalculator`
onu `LineAmount`'a EKLEMEZ, yalnızca taşır; kayıt toplamına
`RecordTotalCalculator` bir kez ekler.

**Geçmiş fiyat donar** (kural 2): hesaplama anında `UnitPriceSnapshot` ve
`PricingRuleSnapshot` (açıklama metinleri dahil) satıra kopyalanır. Onay ekranı
bu snapshot'ı okur, yeniden hesaplamaz.

**Testleri:** `ContractLineResolverTests.cs`, `PricingCalculatorTests.cs`,
`RecordTotalCalculatorTests.cs`, `ContractPricingTests.cs`,
`ContractLineSurchargeTests.cs`, `ContractExpiryTests.cs`

### 2.4 Üç durum makinesi — `Status`'a doğrudan atama yok

**Dosyalar:** `src/MipRental.Domain/Approvals/`

| Makine | Belge | Kontrol ettiği |
|---|---|---|
| `RequestStateMachine.cs` | Talep (CPR) | Geçiş izinli mi + rol uygun mu + dönem açık mı |
| `WorkRecordStateMachine.cs` | Çalışma kaydı (WR) | Geçiş izinli mi + yetki var mı + dönem açık mı |
| `ProgressPaymentStateMachine.cs` | Hakediş | Geçiş izinli mi + yetki var mı (**dönem kontrolü YOK** — ADR-031: hakediş kapanmış dönemin ödeme belgesidir, kapandıktan sonra da onaylanabilmelidir) |

**Değişmez kural:** hiçbir controller veya servis `Status` alanına doğrudan
atama yapmaz. Her geçiş ilgili makinedeki bir metottan geçer; ihlalde Türkçe
mesajlı exception fırlar.

Talep ve çalışma kaydı **ayrı tablolar ve ayrı makinelerdir** (ADR-011): talep iş
ÖNCESİ, çalışma kaydı iş SONRASI yaşar. Birleştirmek iki akışı birbirine
kilitlerdi.

Zinciri **kod değil veri** belirler (kural 6). `ApprovalFlowResolver`
(`src/MipRental.Data/Approvals/ApprovalFlowResolver.cs`) önce hizmete özel akış
(`ApprovalFlows.ServiceId` dolu) arar, yoksa varsayılan akışı (`ServiceId = null`)
kullanır; adımlar `ApprovalFlowSteps`'ten `StepNo` sırasıyla okunur.
`AmountThreshold` dolu olan adımlar yalnızca tutar eşiği **aşılıyorsa** devreye
girer. Onay adımı sırası veya rolleri değişecekse **veri değişir, kod değişmez.**

**Otomatik onay yoktur** (kural 5, ADR-005). Eşik yüzünden uygulanabilir adım
kalmazsa kayıt sessizce onaylanmaz — hata fırlatılır. Onay gelmezse hatırlatma
ve eskalasyon çalışır (`ApprovalReminderScheduler`).

**Testleri:** `RequestStateMachineTests.cs`, `WorkRecordStateMachineTests.cs`,
`ApprovalFlowTests.cs`, `ApprovalFlowResolverTests.cs`, `ProgressPaymentTests.cs`

### 2.5 Fiyat gizliliği — servis katmanında, view'da değil

| Dosya | Rol |
|---|---|
| `src/MipRental.Web/Security/AuthorizationPolicies.cs:43` | `CanSeePricing` policy'si — ekranın/POST'un kapısı |
| `src/MipRental.Web/Security/CurrentUser.cs:44` | `CanSeePricing` bayrağı — sorgunun para kolonunu hiç seçmemesi |
| `src/MipRental.Web/Security/PricingFields.cs` | Hangi kolonlar "para" sayılır — denetim izi maskelemesinin dayanağı |

Fiyat gören roller: **BUDGET, BUDGET_MANAGER, ADMIN, ACCOUNTING.**
Görmeyenler: Ekipman Müdürlüğü'nün her iki rolü, talep açan, tüm firma rolleri.

**Miktar herkese görünür, tutar görünmez.** Firma "7,5 saat faturalanacak"
bilgisini görür, "9.375 TL" bilgisini görmez. Açıklama satırları da ikiye
ayrılmıştır: `QuantityExplanation` herkese, `AmountExplanation` yalnızca
yetkiliye gider.

Uygulama yeri **view değildir.** View model'lerde para alanları ayrı nullable
nesnelerde toplanır (`Model.Pricing`, `line.Pricing`) ve yetkisiz kullanıcıda o
nesne **hiç kurulmaz** (ADR-018). Testler "view'da gizlendi mi" diye bakmaz;
modele dönen veride alanın **bulunmadığını** doğrular.

İki nokta özellikle dikkat ister, çünkü ikisi de sonradan sızıntı olarak
bulundu:

- **Onaylamak ≠ fiyat görmek.** `CanApprove` Ekipman Müdürlüğü'nü de geçirir;
  "Onayımı Bekleyenler" ekranı bir dönem her satırda tutar gösteriyordu. *Ne
  yapabilir* ile *neyi görebilir* ayrı eksenlerdir (ADR-025).
- **PDF arşivi.** Belge bir kez üretilip arşivlenseydi, fiyatlı sürümü yetkili
  biri ürettiğinde yetkisiz kullanıcı sonradan o dosyayı indirebilirdi. Bu yüzden
  belge **her indirişte yeniden üretilir** ve yetki kontrolü üretim anında
  uygulanır; aynı URL yetkiye göre fiyatlı ya da `-Fiyatsiz` sürümü verir
  (ADR-020). Eski arşiv kaydı silinmez — o kâğıdın doğrulama kodu çalışmaya
  devam eder.

Anonim doğrulama sayfası (`/Dogrula/{kod}`) belgenin **gerçekliğini** doğrular,
içeriğini ifşa etmez: `VerificationViewModel` içinde para alanı hiç yoktur.
Denetim izinde ise ekran kapatılmaz, **değer maskelenir** (`•••`) — olay görünür
kalır, rakam görünmez (ADR-019).

**Testi:** `tests/MipRental.Tests/PricingPrivacyTests.cs`

---

## 3. Şema değişikliği nasıl yapılır

Migration'lar `MipRental.Data`'da, bağlantı dizesi `MipRental.Web`'de olduğu
için **her komutta iki proje de verilir**:

```
dotnet ef migrations add <Ad> --project src\MipRental.Data --startup-project src\MipRental.Web
dotnet ef database update      --project src\MipRental.Data --startup-project src\MipRental.Web
```

Diğer faydalı komutlar:

```
dotnet ef migrations list      --project src\MipRental.Data --startup-project src\MipRental.Web
dotnet ef migrations remove    --project src\MipRental.Data --startup-project src\MipRental.Web   # SON, henüz uygulanmamış migration'ı geri alır
dotnet ef migrations script    --project src\MipRental.Data --startup-project src\MipRental.Web -o deploy.sql
```

Canlı ortamda `database update` yerine **`migrations script` ile üretilen SQL
gözden geçirilip çalıştırılmalıdır.** Uygulama başlangıcında otomatik
`Migrate()` çağrısı YOKTUR ve eklenmemelidir; şema değişikliğinin ne zaman
uygulanacağı operasyonun kararıdır.

### Kurallar

1. **Varlık → konfigürasyon → migration** sırası. Yeni bir varlık eklerken
   `src/MipRental.Data/Configurations/` altına `IEntityTypeConfiguration<T>`
   yazın; `ApplyConfigurationsFromAssembly` onu otomatik bulur.
2. **Para alanı `decimal(18,4)`.** `ConfigureConventions` bunu global olarak
   uygular; `double`/`float` asla kullanılmaz.
3. **Enum'lar string yazılır** (ADR-009): `.HasConversion<string>()` +
   `HasMaxLength` + gerekiyorsa `HasCheckConstraint`. Veritabanına bakan
   birinin `3` değil `APPROVED` görmesi içindir.
   - **Tuzak:** enum'un `0` değeri CLR varsayılanıyla çakışır. `Status = DRAFT`
     yazan bir kayıt, `HasDefaultValue` yüzünden sessizce `ACTIVE` olarak
     INSERT edilebilir. Çözüm `ContractConfiguration`'da: `.HasSentinel((ContractStatus)(-1))`.
4. **Cascade delete kapalıdır** (ADR-010). Tüm FK'lar `DeleteBehavior.Restrict`.
   Bu bilinçlidir; bir migration `Down`'ı FK yüzünden kilitleniyorsa çözüm
   cascade açmak değil, o satıra dokunmamaktır.
5. **Tarih/saat veritabanında UTC**, ekranda yerel. `DateOnly`/`TimeOnly`
   kullanılan yerlerde (iş tarihi, vardiya saati) bu dönüşüm zaten yapılmıştır.
6. **Yeni bir para kolonu eklediyseniz** `PricingFields.cs` listesine ekleyin —
   yoksa denetim izinde maskelenmeden görünür.
7. **Firmaya ait yeni bir varlık eklediyseniz** query filter'ını
   `AppDbContext.ApplyFirmIsolationFilters`'a ekleyin.

### Veri seed etme deseni

Seed migration'ları **sabit id yazmaz.** Tablolar IDENTITY'dir ve geliştirme
veritabanlarına elle eklenmiş satırlar araya girmiş olabilir; sabit id çakışır
ve migration'ı kilitler. Bunun yerine doğal anahtar üzerinden "yoksa ekle":

```sql
IF NOT EXISTS (SELECT 1 FROM Users WHERE UserName = N'ekipman2')
BEGIN
    INSERT INTO Users (...) VALUES (...);
END;
```

Örnekler: `20260916134558_AddDemoSeedData.cs` (demo başlangıç verisi),
`20260902080742_AddRequestScreenSeedUsers.cs`. Şifreler `PasswordHasher<User>`
ile hash'lenir; düz metin şifre veritabanına asla yazılmaz.

Değişmeyen referans verisi (roller, varsayılan onay akışı, hizmet kategorisi,
varyantlar, dönemler, belge serileri) ise migration'da değil
`Configurations/*.cs` içinde `HasData` ile durur.

---

## 4. Yeni hizmet tipi nasıl eklenir

Çoğu durumda **kod değişikliği gerekmez** — ana veri ekranlarından yapılır:

1. **Hizmet kategorisi:** `/ServiceCategories` → Kod, Ad, Birim
   (HOUR / DAY / SHIFT / METER / PIECE), `RequiresTimeTracking`,
   `RequiresVehicle`. (ADMIN)
2. **Varyantlar:** `/ServiceVariants` → örn. 30T / 60T. (ADMIN)
3. **Fiyat satırları:** `/Contracts` → ilgili sözleşme → fiyat satırları.
   **Her varyant için ayrı satır** açın — resolver varyantı tam eşleştirir
   (bkz. 2.3). Gerekiyorsa ek ücretleri (`ContractLineSurcharges`) tanımlayın.
4. **Ayrı onay zinciri gerekiyorsa:** `ApprovalFlows` tablosuna `ServiceId` dolu
   bir satır + `ApprovalFlowSteps` adımları. Gerekmiyorsa varsayılan
   `WR-DEFAULT` akışı kendiliğinden geçerlidir.
   - Dikkat: bir çalışma kaydının satırları **farklı** hizmetlere aitse ve her
     biri farklı bir akışa işaret ediyorsa `ApprovalFlowResolver` keyfî seçim
     yapmaz, hata fırlatır.

Yeni bir **birim** (`ServiceUnit`) gerekiyorsa bu kod değişikliğidir:
`ServiceUnit` enum'u + `CK_Service_Unit` check constraint'i + `PricingCalculator`
içindeki `UnitLabels` ve miktar hesabı birlikte güncellenir.

## 5. Yeni rol / yetki nasıl eklenir

Rol kodlarının tek kaynağı `src/MipRental.Domain/Security/RoleCodes.cs`'tir;
`src/MipRental.Web/Security/RoleNames.cs` yalnızca ileri sarar. İki ayrı liste
tutulmaz.

**Yeni rol:**

1. `RoleCodes.cs` → yeni sabit.
2. `RoleNames.cs` → ileri sarma satırı.
3. `RoleConfiguration.cs` → `HasData` ile yeni `Role` (sabit `RoleId`,
   `RoleScope.INTERNAL` veya `EXTERNAL`) + migration.
4. İlgili policy'lere ekleyin (aşağı bakınız).
5. `StartController.Index` → rolün günlük işi nerede başlıyorsa oraya
   yönlendirin; sıralama önemlidir (ilk eşleşen kazanır).
6. Rol bir onay adımı olacaksa `ApprovalFlowSteps`'e satır ekleyin — kod değil.

> Rolün **kodu** değişse bile `RoleId` değiştirilmez. `UserRoles` ve
> `ApprovalFlowSteps` `RoleId` ile bağlıdır; Adım 10'daki
> SUPERVISOR → EQUIPMENT_MANAGER ve DEPT_HEAD → BUDGET_MANAGER yeniden
> adlandırması bu yüzden mevcut yetkileri ve onay zincirini bozmadı.

**Yeni yetki (policy):**

1. `src/MipRental.Web/Security/PolicyNames.cs` → sabit.
2. `src/MipRental.Web/Security/AuthorizationPolicies.cs` → tanım.
3. Controller/action üzerine `[Authorize(Policy = PolicyNames.X)]`.

İki nokta:

- **Görmek, yapmak ve karar vermek ayrı eksenlerdir** (ADR-025). Aynı ekranda
  butonu gizlemek yetmez; POST action'ı da kapalı olmalıdır. Desen için
  `CanViewEquipmentRequests` / `CanDecideEquipmentRequest` ikilisine bakın.
- **Policy, durum makinesinin yerine geçmez.** Policy ekranın kapısını tutar;
  makine kaydın kendisini korur (yanlış durumda, yanlış firmada, kapalı dönemde
  işlem yapılamaz). İkisi de gereklidir ve aynı rolleri istemelidir.

Varsayılan olarak **her şey `[Authorize]`**'dır (`Program.cs`'te global
`AuthorizeFilter`); `[AllowAnonymous]` yalnızca giriş, hata ve doğrulama
sayfalarındadır.

Fiyat gören rol listesi değişecekse `AuthorizationPolicies.CanSeePricing` **ve**
`CurrentUser.CanSeePricing` **birlikte** güncellenir — ikisi aynı listeyi
kullanır ve ayrışırlarsa sızıntı olur.

---

## 6. Bilinen sınırlar

Bunlar hata değil, bilinçli kapsam kararlarıdır. Devralan ekibin bilmesi gerekir.

| Sınır | Ayrıntı |
|---|---|
| **Saat dilimi = sunucunun saat dilimi** | Yerel saat `ToLocalTime()` ile, yani **sunucunun** saat dilimiyle hesaplanır (`TrFormat.DateTimeLocal`, `RequestToWorkRecordService:86`). İşin tarihi ve dönemi buradan türer: gece 01:00'de biten bir iş UTC'ye bakılırsa bir önceki güne düşer. Sunucu Türkiye dışına taşınırsa (veya UTC'ye kurulursa) **sabit bir `TimeZoneInfo`'ya geçilmelidir.** Kodda bu nokta `ponytail:` yorumuyla işaretlidir. |
| **Oracle entegrasyonu yok** | `IntegrationQueue` tablosu ve `WorkRecord.IntegrationStatus` alanı hazırdır, **gönderim kodu yazılmamıştır** (Faz 2). `ImmutabilityGuardInterceptor` `IntegrationStatus` alanını onaylanmış kayıtta güncellenebilir tutar — tam da bu entegrasyon için. |
| **Offline / mobil çalışma yok** | ADR-001: mobil uygulama yazılmayacak. Saha kullanımı tarayıcı üzerindendir ve **çevrimiçi bağlantı gerektirir.** Operatör ekranı bu varsayımla tasarlanmıştır. |
| **Fiş fotoğrafı / görsel saklanmaz** | ADR-003. `Attachments` tablosu şemada vardır ama görsel saklama altyapısı kurulmamıştır ve kurulmayacaktır. |
| **QR/NFC donanım zorunluluğu yok** | ADR-002. Üretilen PDF'lerdeki karekod yalnızca doğrulama sayfasına götürür; sahada okutma zorunluluğu getirilmez. |
| **Islak imza kaldırılmadı** | ADR-004. Sistem kâğıt sürecin yerini almaz, yanında yürür. |
| **Mail opsiyoneldir** | SMTP yapılandırılmamışsa `NoOpEmailSender` bağlanır, uygulama sorunsuz çalışır, bildirimler `Notifications` tablosunda `QUEUED` bekler. Yapılandırma sonrası kuyruk kaldığı yerden işler. Bkz. `docs/EMAIL-SETUP.md`. |
| **Belge deposu dosya sistemidir** | Üretilen PDF'ler `App_Data/documents` altında saklanır (`DocumentStorage:RootPath` ile değiştirilebilir). `wwwroot` ALTINDA DEĞİLDİR — statik dosya olarak servis edilmemelidirler. Birden fazla sunucuya yayılırsa paylaşımlı bir yol gerekir. |
| **Faz 1 = mobil vinç** | Şema başka hizmet tiplerini taşıyabilir (bkz. bölüm 4) ama saha doğrulaması yalnızca mobil vinç için yapılmıştır. |

---

## 7. Devreye alma öncesi kontrol listesi

**Güvenlik**

- [ ] **Tüm demo hesaplarının şifresi değiştirildi veya hesaplar pasife çekildi.**
      `README.md`'deki tablodaki dokuz hesabın hepsi geliştirme amaçlıdır ve
      şifreleri repoda yazılıdır.
- [ ] Eski/geçiş hesapları (`supervisor`, `depthead`, `testvinc`) gözden geçirildi;
      gerçek kullanıcılar tanımlandıktan sonra pasife çekildi.
- [ ] Gerçek bağlantı dizesi ortam değişkeni ya da secret store'dan veriliyor;
      `appsettings.json`'a **yazılmadı**.
- [ ] Uygulamanın veritabanı kullanıcısına `db_owner` değil, yalnızca gereken
      haklar verildi.
- [ ] HTTPS zorunlu, geçerli sertifika kurulu (`UseHsts` Development dışında
      zaten aktif).

**Veri**

- [ ] Gerçek firmalar, sözleşmeler ve **fiyat satırları** girildi — her hizmet ve
      **her varyant** için (bkz. 2.3 tuzağı).
- [ ] Sözleşme tarih aralığı ve fiyat satırlarının `ValidFrom`/`ValidTo`
      aralıkları canlıya alınacak dönemi kapsıyor.
- [ ] Gerçek lokasyon ağacı girildi; demo lokasyonları (`LIMAN`, `RIH-*`,
      `KNT*`, `AMBAR`, `ATOLYE`) kaldırıldı ya da düzeltildi.
- [ ] Demo sözleşmesi `SOZ-DEMO-001` pasife alındı (`Status`), silinmedi.
- [ ] `RemoveDemoTransactionData` migration'ı uygulandı — demo hareket verisini
      (talep, çalışma kaydı, onay, bildirim, hakediş, token) siler, `AuditLog` ve
      `GeneratedDocuments`'a dokunmaz, `supervisor`/`testvinc` hesaplarını pasife
      çeker. **Geri alınamaz**; canlıda `migrations script` çıktısı gözden
      geçirilerek uygulanır.
- [ ] İçinde bulunulan ve gelecek yılın **dönemleri** açıldı (`/Periods`).
- [ ] Onay zinciri (`ApprovalFlowSteps`) gerçek onay sırasına göre güncellendi;
      gerekiyorsa `AmountThreshold` girildi.
- [ ] Departmanlar girildi — **talep açan kullanıcının departmanı olmak
      zorundadır**, aksi hâlde talep açamaz.

**Yapılandırma**

- [ ] SMTP ayarları girildi ve `Email:Enabled = true` (bkz. `docs/EMAIL-SETUP.md`).
- [ ] `Email:AppBaseUrl` gerçek adrese ayarlandı — mail gövdesindeki onay
      bağlantıları buradan üretilir.
- [ ] `Email:AllowExternalRecipients` ve `TestModeRecipient` canlı için doğru
      ayarlandı (test modunda tüm mailler tek adrese gider).
- [ ] `DocumentStorage:RootPath` yedeklenen bir dizini gösteriyor.
- [ ] Log seviyesi ayarlandı; `Microsoft.EntityFrameworkCore.Database.Command`
      Information seviyesinde bırakılmadı (her SQL loglanır).

**Doğrulama**

- [ ] `dotnet build` → 0 uyarı, 0 hata.
- [ ] `dotnet test` → hepsi yeşil.
- [ ] Her rolle giriş yapıldı, başlangıç ekranları açıldı.
- [ ] Uçtan uca bir tur yapıldı: talep → onay → kabul → operatör → teyit →
      çalışma kaydı → onay zinciri → dönem kapatma → hakediş.
- [ ] Bir PDF üretildi, karekodu okutuldu, doğrulama sayfası çalıştı.
- [ ] Aynı çalışma kaydı fiyat gören ve görmeyen bir rolle açıldı; tutarın
      görünmediği doğrulandı.
- [ ] Veritabanı yedekleme planı kuruldu. **Sistemde fiziksel silme olmadığı
      için yedek, veri kaybına karşı tek gerçek savunmadır.**

---

## 8. Sık karşılaşılan sorunlar

### "The ConnectionString property has not been initialized."

Uygulama **Production** ortamında açılmıştır; `appsettings.Development.json`
okunmamıştır. Konsolda `Hosting environment: Production` yazar.

**Çözüm:** `dotnet run --project src\MipRental.Web --launch-profile https`
(Visual Studio'da profil olarak `https` seçin). Canlıda ise gerçek bağlantı
dizesini ortam değişkeniyle verin:
`ConnectionStrings__DefaultConnection=...`

### "… firmasının … fiyatı tanımlı değil."

`ContractLineResolver` eşleşen fiyat satırı bulamamıştır. Sırayla kontrol edin:

1. Firmanın **işin yapıldığı tarihte** geçerli (`StartDate ≤ workDate ≤ EndDate`)
   ve `Status = ACTIVE` bir sözleşmesi var mı?
2. O sözleşmede **doğru varyant** için fiyat satırı var mı? `VariantId` tam
   eşleşir — varyantsız satır, varyantlı talebi karşılamaz.
3. Satırın `ValidFrom`/`ValidTo` aralığı `workDate`'i kapsıyor mu?
4. Satır `IsActive = 1` mi?

Hata çalışma kaydı **türetilirken** çıkar; talep `COMPLETED`/`CONFIRMED` kalır,
kayıt oluşmaz ve `REQ_DERIVE_FAILED` bildirimi kuyruğa yazılır. Fiyat satırı
tanımlandıktan sonra türetme tekrar denenebilir — yarım kayıt kalmaz.

### "… dönemi tanımlı değil" / "Kapalı döneme kayıt girilemez."

`Periods` tablosunda o yıl-ay yok ya da `Status = CLOSED`. `/Periods`
ekranından açın (BUDGET rolü). Dönem **işin yapıldığı** tarihe göre seçilir;
talebin açıldığı tarihe göre değil.

### "Birden fazla sözleşme fiyat satırı eşleşti"

Aynı firma + hizmet + varyant için tarih aralıkları çakışan iki aktif satır var.
Sistem keyfî seçim yapmaz. Eski satırın `ValidTo`'sunu kapatın ya da
`IsActive = 0` yapın.

### Onaylanmış kayıt güncellenmiyor

Beklenen davranıştır (kural 1). Düzeltme = **yeni versiyon**:
`WorkRecordRevisionService` üzerinden revizyon oluşturulur (`RevisionOfId` +
gerekçe), eski kayıt `IsSuperseded = 1` olur ve belge numarası `-R2` eki alır
(ADR-012). Interceptor'ı gevşetmeyin.

### Firma kullanıcısı verisini göremiyor / boş liste görüyor

Kullanıcının `FirmId`'si doğru mu? `FirmId = null` MIP personeli demektir;
dolu olan bir kullanıcı **yalnızca** o firmanın verisini görür. Beklenen kayıt
başka firmaya aitse görünmemesi doğrudur.

### Bildirim gitmiyor, `Notifications` tablosu `QUEUED` dolu

Mail yapılandırılmamıştır. Başlangıç logunda şu satır görünür:
`Mail yapılandırması yok/kapalı: kuyruk işleyici beklemede, bildirimler QUEUED kalacak.`
Yapılandırma girildiğinde bekleyen kuyruk işlenmeye başlar. Durum için
`/EmailHealth` ekranına bakın (ADMIN). Ayrıntı: `docs/EMAIL-SETUP.md`.

### Talep açan "departmanınız tanımlı değil" hatası alıyor

`Users.DepartmentId` boştur. Talep açan kişinin departmanı oturumdan okunur ve
departmansız kullanıcı talep açamaz. `/Users` ekranından departman atayın.

### `dotnet ef` komutu bulunamıyor

```
dotnet tool restore
```

Araç repo kökündeki `dotnet-tools.json` manifestinden gelir (dotnet-ef 10.0.11).

---

## 9. Açık teknik borç

Bu bölümde iki madde vardı — EF query filter uyarıları ve şablondan kalan boş
test — ikisi de kapatıldı.

`UserRole` ve `ApprovalToken`, `User`'ınkiyle **eşleşen** birer query filter
aldı (`AppDbContext.ApplyFirmIsolationFilters`). Uyarı susturulmadı; asimetri
giderildi. Oturumsuz akışlarda (login, mail onayı) `FirmId` null olduğu için
filtre geçirgendir ve akış bozulmaz — doğrulaması `FirmIsolationTests.cs`
içindedir.

Kalan bilinçli sınırlar için bölüm 6'ya bakın.

---

## 10. Mimari kararlar

Bu depoda **neden** sorusunun cevabı yok; **ne** ve **nasıl** var. Kararların
kendisi ayrı bir pakettedir: `miprental-kararlar.zip`. İçinde 36 ADR
(Architecture Decision Record) ve 17 kavram notu bulunur.

Neden ayrı: kararlar koddan bağımsız yaşar. Bir ADR, hiç kod yazılmadan önce
alınmış olabilir (ADR-001 "Mobil uygulama yazılmayacak") ya da yazılan kodun
tamamı değiştiğinde bile geçerli kalabilir. Repoya gömülselerdi her kod
değişikliğinde "bunu da güncelleyelim mi" sorusu çıkardı; cevap çoğu zaman
hayırdır ve o soru zamanla notları çürütür.

### Nasıl okunur

Notların hepsi düz **Markdown** dosyasıdır. Herhangi bir metin editörüyle —
Notepad, VS Code, GitHub önizlemesi — açılıp okunur, hiçbir araç gerekmez.

Klasörü **Obsidian** ile bir kasa (vault) olarak açarsanız notlar arasındaki
`[[çift köşeli ayraç]]` bağları tıklanabilir hâle gelir ve kararlar arasında
gezinebilirsiniz. Asıl değer burada: kararlar tek başına değil, birbirine
dayanarak duruyor. ADR-035 neden var sorusunun cevabı ADR-030'da, ADR-033'ün
zemini ADR-032'de.

### Giriş noktası

**`Kararlar MOC.md`** — bütün kararların konu başlıklarına göre gruplanmış
dizinidir (fiyat gizliliği, talep ekranları, hakediş, süre teyidi, firma
izolasyonu...). Nereden başlayacağınızı bilmiyorsanız oradan başlayın.
`MipRental MOC.md` ise kavram notlarının üst dizinidir.

### Kod okumadan önce

**Bir dosyayı değiştirmeye oturmadan önce ilgili ADR'yi okuyun.** Bu depodaki
"tuhaf" görünen kararların çoğunun arkasında yazılı bir gerekçe ve — daha
önemlisi — **reddedilmiş bir alternatif** vardır. Her ADR'de "Reddedilen
alternatif" başlığı bulunur; genellikle aradığınız "neden şöyle yapmamışlar"
sorusunun cevabı tam olarak orada durur.

Pratik eşleştirme:

| Dokunacağınız yer | Önce okunacak |
|---|---|
| Fiyat hesabı, sözleşme satırı | ADR-006, ADR-014, `Fiyatlandırma Motoru.md` |
| Tutarın kime görüneceği | ADR-016, ADR-019, ADR-021, `Fiyat Gizliliği.md` |
| Firma verisinin izolasyonu | ADR-036, `Firma İzolasyonu.md` |
| Onay zinciri, mail ile onay | ADR-005, ADR-015, ADR-030, ADR-034, ADR-035 |
| Talep → çalışma kaydı akışı | ADR-011, ADR-026, ADR-027, ADR-032, ADR-033 |
| Revizyon, belge numarası, denetim izi | ADR-012, ADR-022, `Denetim İzi.md` |

Koddaki uzun XML yorumları bu notların kısaltılmış hâlidir; çelişirlerse
**ADR asıldır** — yorum eskimiş demektir, düzeltilmesi gerekir.

---

## 11. Nereden devam edilir

| Soru | Cevabın yeri |
|---|---|
| Kurulum, çalıştırma, test hesapları | `README.md` |
| Kod konvansiyonları, değişmez kurallar | `CLAUDE.md` |
| Mail / magic link yapılandırması | `docs/EMAIL-SETUP.md` |
| Faz 1 iş gereksinimleri | `docs/spec.md` |
| Şemanın okunabilir referansı | `docs/schema.sql` (kaynak değil — gerçek şema migration'lardır) |
| **"Bu neden böyle yapılmış?"** | `miprental-kararlar.zip` — 36 ADR ve 17 kavram notu. Giriş: `Kararlar MOC.md`. Ayrıntı: bölüm 10. |

Koddaki uzun XML yorumları da kasıtlıdır: her kritik sınıfın başında **neden o
şekilde yazıldığı** anlatılır. Bir dosyayı değiştirmeden önce başındaki yorumu
okuyun — çoğu "acaba neden böyle" sorusunun cevabı oradadır.
