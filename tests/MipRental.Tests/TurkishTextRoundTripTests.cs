using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Entities;

namespace MipRental.Tests;

/// <summary>
/// Bu testler, "Sistem Y%F6neticisi" gibi Windows-1254 ile URL-kodlanmış
/// değerlerin UYGULAMADAN GELMEDİĞİNİ kanıtlar (bkz. FixCp1254UrlEncodedText
/// migration'ının açıklaması).
///
/// İki şey ayrı ayrı doğrulanıyor:
///   1. Form binding: UTF-8 gövdeyle gelen Türkçe metin, model'e bozulmadan ulaşır.
///   2. Kalıcılık: Türkçe metin veritabanına yazılıp aynen geri okunur.
///
/// Yani sistemin kendisi hiçbir yerde URL kodlaması yapmaz/çözmez; bozuk veri
/// artık var olmayan bir dış duman testi betiğinden gelmiştir.
/// </summary>
public class TurkishTextRoundTripTests
{
    private const string TurkishSample = "Sistem Yöneticisi — Şişli Vinç, ağır yük, ölçüm İĞÜŞÇÖ ığüşçö";

    /// <summary>
    /// ASP.NET Core form gövdesini UTF-8 çözer. Bu test, uygulamanın gördüğü
    /// değerin ham metin olduğunu ve hiçbir yerde yüzde-kodlamaya dönüşmediğini
    /// gösterir.
    /// </summary>
    [Fact]
    public async Task FormBinding_KeepsTurkishCharactersIntact()
    {
        var body = $"FullName={Uri.EscapeDataString(TurkishSample)}";

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.ContentLength = context.Request.Body.Length;

        var form = await context.Request.ReadFormAsync();

        Assert.Equal(TurkishSample, form["FullName"].ToString());
        Assert.DoesNotContain("%", form["FullName"].ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Bozuk kayıtların NASIL oluştuğunu birebir yeniden üretir.
    ///
    /// İstemci, metni Windows-1254 ile URL-kodlayıp gövdeye koyduğunda ("+" boşluk,
    /// "%F6" ise ö) ASP.NET Core:
    ///   - "+" işaretini boşluğa çevirir,
    ///   - "%F6" kaçışını UTF-8 olarak ÇÖZEMEDİĞİ için OLDUĞU GİBİ BIRAKIR.
    /// Sonuç tam olarak veritabanında bulduğumuz değerdir: "Sistem Y%F6neticisi".
    ///
    /// Yani uygulama hiçbir kodlama yapmadı; gelen bozuk metni sadakatle sakladı.
    /// Kaynak, artık var olmayan bir dış duman testi betiğidir. Buradaki asıl
    /// koruma bir üstteki testtir: DOĞRU (UTF-8) gönderilen Türkçe metin bozulmaz.
    /// </summary>
    [Fact]
    public async Task FormBinding_LeavesNonUtf8PercentEscapesUntouched_ReproducingTheLegacyDefect()
    {
        // Windows-1254: ö = 0xF6. Bu gövdeyi üreten şey ölçüldü: Windows'taki curl,
        // komut satırı ARGÜMANI olarak verilen Türkçe metni ANSI kod sayfasından
        // (bu makinede 1254) geçirip öyle yüzde-kodluyor. Aynı değer dosyadan
        // okutulduğunda doğru şekilde %C3%B6 gidiyor.
        var cp1254EncodedBody = "FullName=Sistem+Y%F6neticisi";

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Body = new MemoryStream(Encoding.ASCII.GetBytes(cp1254EncodedBody));
        context.Request.ContentLength = context.Request.Body.Length;

        var value = (await context.Request.ReadFormAsync())["FullName"].ToString();

        // Veritabanında bulduğumuz değerin birebir aynısı.
        Assert.Equal("Sistem Y%F6neticisi", value);
    }

    /// <summary>Türkçe metin veritabanına yazılıp aynen geri okunuyor.</summary>
    [Fact]
    public async Task Database_RoundTripsTurkishCharacters()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;

        await using (var db = new SqliteTestContext(options, new FakeCurrentUser()))
        {
            await db.Database.EnsureCreatedAsync();
            db.Firms.Add(new Firm
            {
                FirmId = 1,
                Code = "ŞİŞLİ",
                Title = TurkishSample,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new SqliteTestContext(options, new FakeCurrentUser()))
        {
            var firm = await db.Firms.AsNoTracking().SingleAsync();

            Assert.Equal(TurkishSample, firm.Title);
            Assert.Equal("ŞİŞLİ", firm.Code);
            Assert.DoesNotContain("%", firm.Title, StringComparison.Ordinal);
        }
    }
}
