using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Interceptors;
using MipRental.Data.Reporting;
using MipRental.Data.Services;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Web.Documents;
using QuestPDF.Infrastructure;

namespace MipRental.Tests;

/// <summary>
/// PDF üretimi, belge arşivi (GeneratedDocuments), doğrulama kodu ve CSV çıktısı.
/// </summary>
public class GeneratedDocumentTests
{
    private const int FirmId = 1;
    private const int ServiceId = 1;
    private const int PeriodId = 3;   // 2026 / Mart
    private const int MipUserId = 1;

    static GeneratedDocumentTests()
    {
        // Program.cs'te de kuruluyor; testler Program.cs'i çalıştırmadığı için burada da gerekli.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static DbContextOptions<AppDbContext> SqliteOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new PeriodGuardInterceptor(), new ImmutabilityGuardInterceptor())
            .Options;

    private static AppDbContext CreateContext(SqliteConnection connection, ICurrentUser currentUser) =>
        new SqliteTestContext(SqliteOptions(connection), currentUser);

    private static string VerificationUrl(string code) => $"https://miprental.test/Dogrula/{code}";

    private static async Task<SqliteConnection> CreateSeededConnectionAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var db = CreateContext(connection, new FakeCurrentUser());
        await db.Database.EnsureCreatedAsync();

        // Türkçe karakterli veri: PDF/CSV çıktısında bozulmadığını görmek için bilinçli.
        db.Firms.Add(new Firm
        {
            FirmId = FirmId,
            Code = "TESTVINC",
            Title = "Şişli Vinç ve Ağır Nakliyat Ltd. Şti.",
            CreatedAt = DateTime.UtcNow
        });
        db.Users.Add(new User { UserId = MipUserId, UserName = "mip", FullName = "Sistem Yöneticisi", CreatedAt = DateTime.UtcNow });
        db.Users.Add(new User { UserId = 2, UserName = "firma", FullName = "Firma Kullanıcısı", FirmId = FirmId, CreatedAt = DateTime.UtcNow });
        db.Contracts.Add(new Contract
        {
            ContractId = 1,
            FirmId = FirmId,
            ContractNo = "SÖZ-2026-001",
            Title = "Mobil Vinç Kiralama Sözleşmesi",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Currency = "TRY",
            Status = ContractStatus.ACTIVE,
            CreatedAt = DateTime.UtcNow
        });

        var record = new WorkRecord
        {
            WorkRecordId = 1,
            DocumentNo = "WR-2026-00001",
            Status = WorkRecordStatus.APPROVED,
            FirmId = FirmId,
            ContractId = 1,
            PeriodId = PeriodId,
            WorkDate = new DateOnly(2026, 3, 19),
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(15, 30),
            LocationText = "İskele 3 — Güney Rıhtım",
            WorkDescription = "Konteyner köşe kilidi değişimi için yükseltme çalışması",
            OperatorName = "Şükrü Çağlayan",
            LicensePlate = "33 ABÇ 123",
            EnteredByUserId = 2,
            MobilizationFee = 250m,
            TotalAmount = 1_000m,
            Currency = "TRY",
            ApprovedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        record.WorkRecordLines.Add(new WorkRecordLine
        {
            LineNo = 1,
            ServiceId = ServiceId,
            RawQuantity = 7.5m,
            BillableQuantity = 7.5m,
            Unit = ServiceUnit.HOUR,
            UnitPriceSnapshot = 100m,
            LineAmount = 750m,
            Currency = "TRY",
            PricingRuleSnapshot = """{"explanation":["Ham süre 7,5 saat","Yuvarlama uygulanmadı","Birim fiyat 100,00 TL"]}"""
        });
        db.WorkRecords.Add(record);

        await db.SaveChangesAsync();
        return connection;
    }

    private static DocumentGenerator CreateGenerator(
        SqliteConnection connection, ICurrentUser user, InMemoryDocumentStorage storage) =>
        new(CreateContext(connection, user), new GeneratedDocumentService(CreateContext(connection, user), user, storage));

    // ---------------------------------------------------------------
    // PDF üretimi + GeneratedDocuments kaydı
    // ---------------------------------------------------------------

    [Fact]
    public async Task WorkRecordPdf_IsGeneratedAndArchived()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var storage = new InMemoryDocumentStorage();
        var user = new FakeCurrentUser { UserId = MipUserId };

        var result = await CreateGenerator(connection, user, storage).GenerateWorkRecordFormAsync(1, VerificationUrl, includePricing: true);

        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(result.Content, 0, 4), StringComparison.Ordinal);
        Assert.True(result.Content.Length > 1000);
        Assert.Single(storage.Files);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var document = await db.GeneratedDocuments.AsNoTracking().SingleAsync();

        Assert.Equal(DocumentType.WORK_RECORD, document.DocumentType);
        Assert.Equal(1, document.DocumentId);
        Assert.Equal(GeneratedDocumentKind.FORM_PDF, document.Kind);
        Assert.Equal(FirmId, document.FirmId);
        Assert.Equal(1_000m, document.TotalAmount);
        Assert.Equal("TRY", document.Currency);
        Assert.Equal(MipUserId, document.GeneratedBy);
        Assert.Equal(DocumentTheme.TemplateVersion, document.TemplateVersion);
        Assert.False(string.IsNullOrWhiteSpace(document.VerificationCode));

        // Hash gerçekten dosyanın baytlarından hesaplanmış olmalı.
        Assert.Equal(GeneratedDocumentService.ComputeHash(result.Content), document.ContentHash);
        Assert.Equal(64, document.ContentHash.Length);   // SHA-256, hex
    }

    /// <summary>
    /// Aynı kayıt için PDF yeniden üretilirse ESKİ KAYIT SİLİNMEZ, yeni sürüm eklenir.
    /// İki sürümün hash'i farklı OLABİLİR (PDF içinde üretim zamanı vardır); test bunu
    /// zorunlu kılmaz, sadece iki ayrı kayıt ve iki ayrı doğrulama kodu olduğunu doğrular.
    /// </summary>
    [Fact]
    public async Task RegeneratingPdf_AddsNewVersionWithoutRemovingOld()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var storage = new InMemoryDocumentStorage();
        var user = new FakeCurrentUser { UserId = MipUserId };

        var first = await CreateGenerator(connection, user, storage).GenerateWorkRecordFormAsync(1, VerificationUrl, includePricing: true);
        var second = await CreateGenerator(connection, user, storage).GenerateWorkRecordFormAsync(1, VerificationUrl, includePricing: true);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var documents = await db.GeneratedDocuments.AsNoTracking().OrderBy(d => d.GeneratedDocumentId).ToListAsync();

        Assert.Equal(2, documents.Count);
        Assert.NotEqual(documents[0].VerificationCode, documents[1].VerificationCode);

        // Eski dosya diskte duruyor: yeni sürüm üzerine yazmadı.
        Assert.Equal(2, storage.Files.Count);
        Assert.NotEqual(documents[0].StoragePath, documents[1].StoragePath);

        // Her kaydın hash'i KENDİ dosyasının hash'i.
        Assert.Equal(GeneratedDocumentService.ComputeHash(first.Content), documents[0].ContentHash);
        Assert.Equal(GeneratedDocumentService.ComputeHash(second.Content), documents[1].ContentHash);
    }

    [Fact]
    public async Task MonthlySummaryPdf_IsGeneratedAndArchived()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var storage = new InMemoryDocumentStorage();
        var user = new FakeCurrentUser { UserId = MipUserId };

        var summary = await new MonthlySummaryService(CreateContext(connection, user), user).BuildAsync(PeriodId, FirmId);
        var result = await CreateGenerator(connection, user, storage).GenerateMonthlySummaryAsync(summary, VerificationUrl);

        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(result.Content, 0, 4), StringComparison.Ordinal);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var document = await db.GeneratedDocuments.AsNoTracking().SingleAsync();

        Assert.Equal(DocumentType.PERIOD, document.DocumentType);
        Assert.Equal(PeriodId, document.DocumentId);
        Assert.Equal(GeneratedDocumentKind.MONTHLY_SUMMARY_PDF, document.Kind);
        Assert.Equal(FirmId, document.FirmId);
        Assert.Equal(summary.GrandTotal, document.TotalAmount);
    }

    /// <summary>
    /// Türkçe karakterler için GÖMÜLÜ font kullanılıyor: PDF'in içinde Lato'nun
    /// gömülü font tanımı bulunmalı. Lato QuestPDF paketinin içinden gelir, işletim
    /// sisteminden değil — sunucuda font kurulu olmasa da çıktı aynıdır.
    /// </summary>
    [Fact]
    public async Task Pdf_EmbedsLatoFontInsteadOfRelyingOnSystemFonts()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var storage = new InMemoryDocumentStorage();
        var user = new FakeCurrentUser { UserId = MipUserId };

        var result = await CreateGenerator(connection, user, storage).GenerateWorkRecordFormAsync(1, VerificationUrl, includePricing: true);

        // Font adları PDF'te sıkıştırılmamış nesne adları olarak geçer.
        var raw = Encoding.Latin1.GetString(result.Content);
        Assert.Contains("Lato", raw, StringComparison.Ordinal);
        Assert.Contains("FontFile", raw, StringComparison.Ordinal);   // font gerçekten GÖMÜLÜ
    }

    // ---------------------------------------------------------------
    // Doğrulama kodu tahmin edilemez olmalı
    // ---------------------------------------------------------------

    [Fact]
    public void VerificationCode_IsNotSequentialAndNotGuessable()
    {
        var codes = Enumerable.Range(0, 500).Select(_ => GeneratedDocumentService.NewVerificationCode()).ToList();

        // Hepsi benzersiz.
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());

        // Sabit uzunluk, sadece büyük harf hex.
        Assert.All(codes, c => Assert.Equal(32, c.Length));
        Assert.All(codes, c => Assert.Matches("^[0-9A-F]{32}$", c));

        // ARTAN SAYI DEĞİL: ardışık kodlar sıralı olsaydı neredeyse hepsi
        // bir öncekinden büyük çıkardı. Rastgele üretimde bu oran ~%50'dir.
        var ascending = codes.Zip(codes.Skip(1), (a, b) => string.CompareOrdinal(b, a) > 0).Count(x => x);
        Assert.InRange(ascending, codes.Count * 0.3, codes.Count * 0.7);

        // Ardışık iki kod arasında ortak önek olmamalı (sayaç davranışının izi).
        var sharedPrefixes = codes.Zip(codes.Skip(1), (a, b) => a[..8] == b[..8]).Count(x => x);
        Assert.Equal(0, sharedPrefixes);
    }

    // ---------------------------------------------------------------
    // Doğrulama sayfası: kişisel veri YOK
    // ---------------------------------------------------------------

    [Fact]
    public async Task VerificationResult_ContainsNoPersonalData()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var storage = new InMemoryDocumentStorage();
        var user = new FakeCurrentUser { UserId = MipUserId };

        var generated = await CreateGenerator(connection, user, storage).GenerateWorkRecordFormAsync(1, VerificationUrl, includePricing: true);
        var code = generated.Document.VerificationCode!;

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var result = await new DocumentVerificationService(db).VerifyAsync(code);

        Assert.NotNull(result);

        // Gösterilmesi GEREKENLER
        Assert.Equal("WR-2026-00001", result!.DocumentNo);
        Assert.Equal("Şişli Vinç ve Ağır Nakliyat Ltd. Şti.", result.FirmTitle);
        Assert.Equal(2026, result.Year);
        Assert.Equal(3, result.Month);
        Assert.Equal(WorkRecordStatus.APPROVED, result.RecordStatus);

        // ADIM 9: TUTAR ARTIK DONMEZ. Sayfa anonim erisilebilir; karekodu goren
        // herkes tutari gorebiliyordu. Alan modelden tamamen kaldirildi.
        Assert.DoesNotContain(typeof(DocumentVerificationResult).GetProperties(),
            p => p.Name is "TotalAmount" or "Currency");

        // Gösterilmemesi GEREKENLER: sonucun HİÇBİR alanında kişisel/operasyonel
        // veri geçmemeli. Alanları tek tek adlandırmak yerine tüm string
        // alanları tarıyoruz — modele ileride yeni bir alan eklenirse bu test de yakalar.
        var allText = string.Join("|", typeof(DocumentVerificationResult)
            .GetProperties()
            .Select(p => p.GetValue(result)?.ToString())
            .Where(v => v is not null));

        foreach (var forbidden in new[]
                 {
                     "Şükrü Çağlayan",       // operatör adı
                     "33 ABÇ 123",            // plaka
                     "İskele 3",              // lokasyon
                     "Konteyner köşe kilidi", // iş tanımı
                     "Sistem Yöneticisi",     // belgeyi üreten kullanıcı
                     "Firma Kullanıcısı"      // kaydı giren kullanıcı
                 })
        {
            Assert.DoesNotContain(forbidden, allText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Verification_ReturnsNullForUnknownOrEmptyCode()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await using var db = CreateContext(connection, new FakeCurrentUser());
        var service = new DocumentVerificationService(db);

        Assert.Null(await service.VerifyAsync("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"));
        Assert.Null(await service.VerifyAsync(""));
        Assert.Null(await service.VerifyAsync(null));
    }

    [Fact]
    public async Task Verification_FlagsThatANewerVersionExists()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var storage = new InMemoryDocumentStorage();
        var user = new FakeCurrentUser { UserId = MipUserId };

        var first = await CreateGenerator(connection, user, storage).GenerateWorkRecordFormAsync(1, VerificationUrl, includePricing: true);
        await Task.Delay(5);   // GeneratedAt farklı olsun
        var second = await CreateGenerator(connection, user, storage).GenerateWorkRecordFormAsync(1, VerificationUrl, includePricing: true);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var service = new DocumentVerificationService(db);

        // Eski kâğıt hâlâ doğrulanır ama "daha yenisi var" der.
        var oldResult = await service.VerifyAsync(first.Document.VerificationCode!);
        Assert.NotNull(oldResult);
        Assert.True(oldResult!.HasNewerVersion);

        var newResult = await service.VerifyAsync(second.Document.VerificationCode!);
        Assert.False(newResult!.HasNewerVersion);
    }
}
