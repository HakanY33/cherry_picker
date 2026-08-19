# Kurulum ve İlk Claude Code Promptu

---

## 1. Kurulacaklar (sırayla)

| # | Ne | Nereden / nasıl |
|---|---|---|
| 1 | **SQL Server 2022 Developer Edition** | Ücretsiz, tam özellikli. LocalDB'yi tercih etme — LocalDB'de eksik özellikler var ve ilerde gerçek sunucuya taşırken sürpriz çıkar. |
| 2 | **SQL Server Management Studio (SSMS)** | Veriyi gözle görmek için. EF migration'ı çalıştırdıktan sonra tabloların gerçekten oluştuğunu buradan doğrularsın. |
| 3 | **.NET 10 SDK** | LTS, Kasım 2028'e kadar destekli. |
| 4 | **Visual Studio 2026** | .NET 10 ile aynı anda çıktı. Community sürümü yeterli. Workload: *ASP.NET and web development* + *Data storage and processing*. |
| 5 | **Claude Code** | PowerShell'de: `irm https://claude.ai/install.ps1 \| iex` — sonra terminali kapat/aç. `claude` tanınmıyorsa `%USERPROFILE%\.local\bin` PATH'te mi diye bak. |
| 6 | **Git for Windows** | Zorunlu değil ama kur; Claude Code shell komutlarını Git Bash üzerinden çalıştırır. |

### Kurulum sonrası doğrulama

```powershell
dotnet --version          # 10.x görmeli
claude doctor             # Claude Code sağlık kontrolü
```

SQL Server bağlantısını SSMS'te `.\SQLEXPRESS` veya `localhost` ile test et; hangisi olduğunu not al, connection string'e o gidecek.

---

## 2. Klasör yapısı

```
C:\dev\MipRental\          <- burada hem VS 2026 hem Claude Code açık olacak
```

Claude Code'u bu klasörde başlat:

```powershell
cd C:\dev\MipRental
claude
```

İlk iş olarak `/init` çalıştır — repo için `CLAUDE.md` üretir. Sonra aşağıdaki içerikle üzerine yaz.

---

## 3. CLAUDE.md (projeye koy)

```markdown
# MipRental — MIP Hizmet & Kiralama Yönetim Sistemi

## Proje nedir
Mersin Uluslararası Liman için alt yüklenici hizmet kiralama (Faz 1: mobil vinç /
cherry picker) yönetim sistemi. Alt yüklenici çalışmasını girer, MIP onaylar,
tutar sözleşmedeki birim fiyata göre otomatik hesaplanır, aylık icmal üretilir.

## Teknoloji
- .NET 10, ASP.NET Core MVC (Razor Views). TEK proje — ayrı API katmanı YOK.
- Entity Framework Core 10, Code First, SQL Server
- Dinamik UI ihtiyaçları için htmx. React/Vue/Angular veya başka SPA framework'ü YOK.
- PDF: QuestPDF

## Kod konvansiyonları
- Sınıf, property, tablo, kolon adları: İNGİLİZCE
- Kullanıcıya görünen tüm metinler: TÜRKÇE
- Tarih/saat: veritabanında UTC, ekranda yerel saat
- Para: decimal(18,4). double/float ASLA kullanma.
- Nullable reference types açık

## Değişmez kurallar (business invariants)
1. Onaylanmış hiçbir mali kayıt UPDATE veya DELETE edilmez.
   Düzeltme = yeni versiyon (RevisionOfId) + gerekçe. Eskisi IsSuperseded = 1.
2. Birim fiyat ve fiyatlandırma kuralı, hesaplama anında satıra kopyalanır
   (UnitPriceSnapshot + PricingRuleSnapshot). Sonradan sözleşme değişse bile
   geçmiş kayıt asla değişmez.
3. Doğru sözleşme satırı, kaydın GİRİLDİĞİ tarihe göre değil,
   İŞİN YAPILDIĞI tarihe (WorkDate) göre seçilir.
4. Kapalı döneme (Periods.Status = CLOSED) kayıt girilemez, mevcut kayıt değiştirilemez.
5. Otomatik onay YOKTUR. Onay gelmezse hatırlatma + eskalasyon olur.
6. Onay zinciri ApprovalFlowSteps tablosundan okunur, kodda sabit değildir.
7. Alt yüklenici SADECE kendi firmasının verisini görebilir. Bu kontrol her
   sorguda uygulanır, sadece UI'da gizlemek yeterli değildir.

## Yapma
- Serbest formül yazılabilen kural motoru kurma. Fiyatlandırma parametriktir.
- EAV / key-value "dinamik alan" tablosu kurma.
- Fiş fotoğrafı / görsel saklama altyapısı kurma.
- İstenmeden yeni NuGet paketi ekleme.

## Komutlar
dotnet build
dotnet run
dotnet ef migrations add <Ad>
dotnet ef database update
dotnet test
```

---

## 4. İlk Claude Code promptu

> Aşağıdakini olduğu gibi yapıştır. `schema.sql` dosyasını önce proje klasörüne
> `docs/schema.sql` olarak kopyala.

```
MIP (Mersin Uluslararası Liman) için bir hizmet kiralama yönetim sistemi kuruyoruz.
Bu ilk adımda SADECE proje iskeleti + veri katmanı + ilk migration istiyorum.
UI, controller, iş mantığı YAZMA.

Referans: docs/schema.sql dosyasını oku. Hedef veri modeli orada tanımlı.
Ayrıca CLAUDE.md dosyasındaki kuralları oku ve hepsine uy.

## İstediklerim

### 1. Solution ve proje yapısı
- .NET 10, ASP.NET Core MVC
- Solution adı: MipRental
- Projeler:
  - src/MipRental.Web        (ASP.NET Core MVC — startup projesi)
  - src/MipRental.Domain     (entity sınıfları, enum'lar — bağımlılığı olmayan katman)
  - src/MipRental.Data       (DbContext, Fluent API konfigürasyonları, migration'lar)
  - tests/MipRental.Tests    (xUnit)
- NuGet: Microsoft.EntityFrameworkCore.SqlServer, Microsoft.EntityFrameworkCore.Design,
  Microsoft.EntityFrameworkCore.Tools. Başka paket ekleme.

### 2. Entity sınıfları (MipRental.Domain)
docs/schema.sql içindeki 25 tablonun TAMAMINI entity olarak oluştur.
Tablo isimlerini ve kolon isimlerini birebir koru.

Gruplar:
- Ana veriler: Firm, Department, User, Role, UserRole, Location
- Hizmet: ServiceCategory, ServiceVariant, Equipment
- Sözleşme: Contract, ContractLine, ContractLineSurcharge
- Dönem/numara: Period, DocumentSeries
- Talep: Request, RequestLine
- Çalışma kaydı: WorkRecord, WorkRecordLine
- Onay: ApprovalFlow, ApprovalFlowStep, Approval
- Çıktı/iz: GeneratedDocument, Attachment, AuditLog, Notification
- Entegrasyon: IntegrationQueue

Kurallar:
- schema.sql'de CHECK constraint ile sınırlanmış metin alanları (Unit, Status,
  RoundingRule, Decision, IntegrationStatus vb.) C# enum olarak tanımlansın,
  veritabanına STRING olarak yazılsın (HasConversion<string>()). int olarak yazma —
  veritabanına elle bakıldığında okunabilir olmalı.
- WorkRecord.Status ve WorkRecord.IntegrationStatus AYRI enum'lar, birleştirme.
- Navigation property'leri iki yönlü kur.
- Location ve Department self-referencing (ParentId).

### 3. DbContext ve konfigürasyon (MipRental.Data)
- AppDbContext, her entity için ayrı IEntityTypeConfiguration<T> sınıfı.
  Hepsini tek dosyaya yığma.
- Tüm decimal alanlar: HasPrecision(18, 4)
- Cascade delete KAPALI olsun (DeleteBehavior.Restrict). AuditLog, Approval ve
  WorkRecordLine'da özellikle önemli — hiçbir mali kayıt zincirleme silinmemeli.
- schema.sql'deki tüm index'leri Fluent API ile tanımla.
- Unique constraint'ler: Firm.Code, Contract(FirmId, ContractNo),
  Period(Year, Month), WorkRecord.DocumentNo, Request.DocumentNo
- PricingRuleSnapshot alanı nvarchar(max), JSON tutacak.
- SaveChanges override: CreatedAt/UpdatedAt otomatik doldurulsun (UTC).

### 4. Seed data
Migration ile birlikte şu başlangıç verisi gelsin:
- Roller: REQUESTER, SUPERVISOR, DEPT_HEAD, BUDGET, ACCOUNTING, FIRM_USER, ADMIN
- ServiceCategory: CHERRY_PICKER (Mobil Vinç, birim HOUR,
  RequiresTimeTracking=true, RequiresVehicle=true)
- ServiceVariant: 30T_SEPETLI (30 Ton Sepetli), 60T_SEPETLI (60 Ton Sepetli)
- İçinde bulunulan yıl için 12 aylık Period kaydı, hepsi OPEN
- DocumentSeries: WORK_RECORD ve REQUEST için, içinde bulunulan yıl

### 5. Connection string ve migration
- appsettings.Development.json'a connection string ekle (LocalDB DEĞİL,
  localhost SQL Server Developer Edition).
- Veritabanı adı: MipRental
- Collation: Turkish_100_CI_AS
- İlk migration'ı oluştur, adı: InitialCreate

### 6. Doğrulama
- dotnet build hatasız geçsin
- dotnet ef migrations add InitialCreate çalışsın
- Üretilen migration dosyasını oku ve 25 tablonun da içinde olduğunu doğrula
- Bana kısa bir özet ver: kaç entity, kaç tablo, eksik/atladığın bir şey var mı

## Yapma
- Controller, View, Razor sayfası yazma
- İş mantığı, servis katmanı, repository yazma
- Fiyat hesaplama kodu yazma (sonraki adımda)
- Identity/authentication kurma (sonraki adımda)
- Ekstra NuGet paketi ekleme
```

---

## 5. Bu adımdan sonra

Sıra şöyle ilerleyecek:

1. **Veri katmanı** ← şu an burası
2. Kimlik doğrulama ve yetkilendirme (MIP hesabı + firma hesabı, satır bazlı yetki)
3. Tanım ekranları (firma, hizmet, lokasyon, sözleşme, fiyat)
4. **Fiyatlandırma motoru + birim testleri** — buraya gelince dur, testler geçmeden ilerleme
5. Çalışma kaydı girişi (alt yüklenici ekranı)
6. Onay akışı + bildirimler
7. PDF, aylık icmal, Excel çıktısı

4. adım projenin en kritik yeri. Oraya gelmeden önce toplantıdan ücretlendirme
kuralları (birim, minimum süre, yuvarlama, çarpanlar) gelmiş olmalı.
