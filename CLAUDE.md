# MipRental — MIP Hizmet & Kiralama Yönetim Sistemi

## Bilgi tabanı

Mimari kararlar, iş kuralları ve kavramlar repo dışında bir Obsidian vault'unda
tutulur:
C:\Users\Hakan\Desktop\Projects\ObsidianVault\cherry_picker

Bir mimari karara, iş kuralına veya "bu neden böyle" sorusuna ihtiyacın olduğunda
iki adım izle:

1. graphify ile hangi notun ilgili olduğunu bul (graf yapı döndürür, metin değil)
2. Bulduğun notu doğrudan oku

Vault'ta kaba grep yapma. Önce graphify'a sor, sonra sadece işaret ettiği
dosyaları oku.

Cevabı bulamazsan VARSAYIM YAPMA. Hangi bilgiye ihtiyacın olduğunu söyle.

Yeni bir mimari karar alındığında bana bildir; vault'a ADR olarak eklenecek.

graphify update . komutunu REPO dizininde çalıştırma — vault'un not grafını
kod grafıyla ezer. Vault dizininde (ObsidianVault\cherry_picker) çalıştırmak
serbesttir ve notlar değiştiğinde gereklidir.

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

## graphify

Bilgi tabanı grafiği bu repoda DEĞİL, vault'ta:
`C:/Users/Hakan/Desktop/Projects/ObsidianVault/cherry_picker/graphify-out/graph.json`
Default yol tutmaz, her komutta `--graph` ver:

- `graphify explain "<kavram>" --graph <yol>` — notun başlıkları + bağlantıları
- `graphify query "<soru>" --graph <yol>` — soruya göre alt-graf
- `graphify path "<A>" "<B>" --graph <yol>` — iki kavram arası ilişki

Graf sadece YAPI tutar: başlık adı, dosya:satır, kenarlar. Not METNİ grafta yok.
Metin gerekiyorsa iste — varsayım yapma.

`graphify update .` ÇALIŞTIRMA. Bu repoda graf yok; vault'unkini bozar.
