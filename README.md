# MipRental — Hizmet & Kiralama Yönetim Sistemi

Bir işletmenin alt yüklenici hizmet kiralamasını uçtan uca yöneten kurum içi web
uygulamasıdır. Faz 1 kapsamı mobil vinç / cherry picker kiralamasıdır. Bugün
kâğıt fişle yürüyen döngü — talep, planlama, işin yapılması, onay, hakediş — tek
bir sistemde toplanır; tutar, kaydın girildiği tarihe değil **işin yapıldığı
tarihe** göre seçilen sözleşme satırından otomatik hesaplanır.

Akış şöyledir: kurum içinde bir birim talep açar (CPR-YYYY-NNNNN), Ekipman
Müdürlüğü talebi değerlendirir, alt yüklenici firma kabul edip operatör ve plaka
atar, operatör "başladım / bitirdim" der, talebi açan kişi gerçekleşen süreyi teyit
eder. Teyit edilen talepten çalışma kaydı (WR-YYYY-NNNNN) **otomatik türer** —
çalışma kaydı elle girilmez. Kayıt sözleşmedeki birim fiyat, yuvarlama kuralı,
asgari faturalanabilir miktar, gün eşiği ve mobilizasyon bedeli ile fiyatlanır ve
`ApprovalFlowSteps` tablosundan okunan onay zincirine girer. Onaylanan kayıtlar
aylık icmalde ve hakedişte toplanır; PDF ve Excel çıktıları karekodlu doğrulama
koduyla üretilir.

Sistem üç şeyi pazarlık konusu yapmaz. **Onaylanmış mali kayıt değiştirilmez** —
düzeltme yeni bir versiyondur (`RevisionOfId` + gerekçe), eskisi
`IsSuperseded = 1` olur. **Geçmiş fiyat donar** — birim fiyat ve fiyatlandırma
kuralı hesaplama anında satıra kopyalanır (`UnitPriceSnapshot`,
`PricingRuleSnapshot`), sözleşme sonradan değişse bile geçmiş kayıt değişmez.
**Alt yüklenici yalnızca kendi firmasının verisini görür** — bu kontrol UI'da
gizleme ile değil, her sorguya uygulanan global query filter ile yapılır. Bu üç
kural ve nasıl uygulandıkları `docs/DEVIR-TESLIM.md` içinde anlatılır.

---

## Gereksinimler

| Bileşen | Sürüm |
|---|---|
| .NET SDK | 10.0 (`dotnet --version` → 10.0.4xx) |
| SQL Server | 2019 veya üzeri (Developer/Express yeterli) |
| Visual Studio | 2026 (veya `dotnet` CLI + herhangi bir editör) |

Ek bir kurulum yok. PDF (QuestPDF), Excel (ClosedXML) ve karekod (QRCoder)
NuGet paketleriyle gelir; harici bir servis ya da lisans anahtarı gerekmez.

---

## Kurulum

### 1. Klonla

```
git clone <repo-url>
cd cherry_picker
```

### 2. Bağlantı dizesini gir

Bağlantı dizesi **repoda yoktur ve olmayacaktır**:
`src/MipRental.Web/appsettings.Development.json` `.gitignore` içindedir.
Bu dosyayı kendin oluştur:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MipRental;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

### 3. EF araçlarını ve veritabanını kur

```
dotnet tool restore
dotnet ef database update --project src\MipRental.Data --startup-project src\MipRental.Web
```

`--project` ve `--startup-project` zorunludur: migration'lar `MipRental.Data`
projesinde, bağlantı dizesi `MipRental.Web` projesindedir.

Bu komut şemayı kurar **ve demo başlangıç verisini yükler**: Test Vinç firması,
2026–2027 sözleşmesi + 30T/60T fiyat satırları, lokasyon ağacı, 2026 ve
2027 dönemleri, onay zinciri ve aşağıdaki test hesapları. Sıfırdan kurulan bir
veritabanında demo akışı doğrudan yürür.

### 4. Çalıştır

```
dotnet run --project src\MipRental.Web --launch-profile https
```

→ https://localhost:7169

### ⚠️ `--launch-profile https` ATLANMAZ

Profil verilmezse uygulama **Production** ortamında açılır,
`appsettings.Development.json` okunmaz ve bağlantı dizesi boş kalır. Ekranda
tarayıcıda anlamsız bir hata, konsolda ise şu görünür:

```
info: Microsoft.Hosting.Lifetime[0]
      Hosting environment: Production
fail: Microsoft.EntityFrameworkCore.Database.Connection[20004]
      An error occurred using the connection to database '' on server ''.
      System.InvalidOperationException: The ConnectionString property has not been initialized.
```

Bu bir veritabanı arızası değildir; yanlış ortamda açılmıştır. Çözüm profili
vermektir. Visual Studio'da çalıştırma profili olarak **https** seçilmelidir.
`http` profili de Development'tır ve çalışır (http://localhost:5093), yalnızca
TLS yoktur.

İlk çalıştırmada geliştirme sertifikası uyarısı gelirse:

```
dotnet dev-certs https --trust
```

---

## Test hesapları

Şifrelerin hepsi **geliştirme/demo amaçlıdır** ve migration ile veritabanına
hash'lenerek yazılır (`PasswordHasher<User>`; düz metin şifre veritabanında
durmaz). **Canlıya alınmadan önce hepsi değiştirilmeli veya pasife
çekilmelidir** — bkz. `docs/DEVIR-TESLIM.md`, "Devreye alma öncesi kontrol
listesi".

| Kullanıcı | Şifre | Rol | Ne yapar |
|---|---|---|---|
| `talep1` | `Talep!2345` | REQUESTER | Talep açar, gerçekleşen süreyi teyit eder ya da itiraz eder. Tutar GÖRMEZ. |
| `ekipman1` | `Ekipman!2345` | EQUIPMENT_MANAGER | Talebi onaylar/reddeder, süre itirazının hakemidir, çalışma kaydı onay zincirinin 1. adımıdır. Tutar GÖRMEZ. |
| `ekipman2` | `Ekipman!2345` | EQUIPMENT_VIEWER | Ekipman Müdürlüğü ekranlarını salt okur; karar veremez, tutar görmez. |
| `firma1` | `Firma!2345` | FIRM_MANAGER | Alt yüklenici yetkilisi: talebi kabul eder, operatör/plaka atar, çalışma kaydını onaya gönderir. Tutar GÖRMEZ. |
| `operator1` | `Operator!2345` | FIRM_OPERATOR | Sahadaki operatör: "başladım / bitirdim". Onaya gönderemez (ADR-028). |
| `butce` | `Butce!2345` | BUDGET | Sözleşme ve birim fiyat yönetir, dönem kapatır/açar, hakediş hazırlar. Tutar GÖRÜR. |
| `depthead` | `Mudur!2345` | BUDGET_MANAGER | Onay zincirinin 2. adımı; hakedişi onaylar. Tutar GÖRÜR. |
| `muhasebe` | `Muhasebe!2345` | ACCOUNTING | Tutarları görür, birim fiyatı yönetmez. |
| `admin` | `Admin!2345` | ADMIN | Ana veri (firma, kullanıcı, lokasyon, hizmet) yönetimi. Tutar GÖRÜR. |

`depthead` adı Adım 10'daki rol yeniden adlandırmasından (DEPT_HEAD →
BUDGET_MANAGER) kalmıştır; hesabın rolü **Bütçe Yöneticisi**'dir.
`supervisor` (eski SUPERVISOR) ve `testvinc` (geçiş rolü FIRM_USER) eski
adımlardan kalan hesaplardır, demo akışında kullanılmazlar.

### Demoyu hangi sırayla göstermeli

`talep1` talep açar → `ekipman1` onaylar → `firma1` kabul edip operatör/plaka
atar → `operator1` başlar ve bitirir → `talep1` süreyi teyit eder (çalışma kaydı
burada otomatik doğar) → `firma1` kaydı onaya gönderir → `ekipman1` ve
`depthead` onaylar → `butce` dönemi kapatıp hakedişi hazırlar → `depthead`
hakedişi onaylar.

Adım 18'in çıktısı bu zincirin **yanında** durur: `ekipman1` ile
**Ekipman Müdürlüğü → Aylık Operasyon Tablosu** ekranından dönem seçilir ve
kurumun kendi formunun doldurulmuş hâli Excel olarak indirilir. Bu tablo
hakedişin yerine geçmez: fiyat içermez, toplamı yoktur, yalnızca onaylanmış ve
kilitlenmiş kayıtları listeler.

Fiyat gizliliğini göstermek için aynı çalışma kaydını `firma1` ve `butce` ile
açmak yeterlidir: aynı ekran, birinde tutar sütunu yok.

---

## Test çalıştırma

```
dotnet test
```

867 test, hepsi veritabanısız çalışır (EF InMemory + SQLite in-memory).
Ayrı bir veritabanı ya da yapılandırma gerekmez.

---

## Proje yapısı

```
src/
  MipRental.Domain/   Varlıklar, enum'lar, durum makineleri, fiyatlandırma çekirdeği.
                      Hiçbir şeye bağımlı DEĞİL — veritabanı, HTTP, oturum yok.
  MipRental.Data/     EF Core: DbContext, konfigürasyonlar, migration'lar,
                      interceptor'lar, servisler (türetme, onay, hakediş, mail kuyruğu).
                      Domain'e bağımlı.
  MipRental.Web/      ASP.NET Core MVC: controller'lar, Razor view'lar, yetki
                      politikaları, PDF/Excel üretimi. Domain + Data'ya bağımlı.
                      TEK çalıştırılabilir proje; ayrı API katmanı YOK.
tests/
  MipRental.Tests/    xUnit. Üç projeye de bağımlı.
docs/                 Belgeler (aşağı bakınız).
```

Bağımlılık yönü tek yönlüdür: `Web → Data → Domain`. Domain hiçbir şeyi bilmez.

---

## Belgeler

| Dosya | İçerik |
|---|---|
| `docs/DEVIR-TESLIM.md` | **Devralan ekibin okuyacağı belge.** Mimari özet, beş kritik mekanizma ve dosyaları, şema/rol/hizmet ekleme, bilinen sınırlar, devreye alma kontrol listesi, sık karşılaşılan sorunlar. |
| `docs/EMAIL-SETUP.md` | SMTP yapılandırması, mail ile onay (magic link), kuyruk ve hatırlatma ayarları. |
| `docs/spec.md` | Faz 1 iş gereksinimleri. |
| `docs/schema.sql` | Şemanın okunabilir referansı. Kaynak DEĞİLDİR — gerçek şema migration'lardır. |
| `docs/00-kurulum-ve-ilk-prompt.md` | Projenin başlangıç notları. |
| `CLAUDE.md` | Kod konvansiyonları ve değişmez iş kuralları. |

Mimari kararların gerekçeleri (ADR'ler) repo dışında, ayrı bir Obsidian
vault'unda tutulur; yolu `CLAUDE.md` içinde yazılıdır.
