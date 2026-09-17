using System.Security.Claims;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MipRental.Data;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Web.Controllers;
using MipRental.Web.Documents;
using MipRental.Web.Models.EquipmentOperations;
using MipRental.Web.Security;

namespace MipRental.Tests;

/// <summary>
/// ADIM 18 — Ekipman Müdürlüğü aylık operasyon tablosu.
///
/// Bu testlerin varlık sebebi tek cümle: bu çıktı MIP'in ELİNDEKİ bir formun
/// yerine geçiyor. Sütun adı ya da sırası kayarsa formu okuyan kişi yanlış
/// sütunu okur ve kimse hata almaz. Beklenen başlıklar bu dosyada ELLE yazılı;
/// builder'ın kendi dizisinden okunsaydı test yalnızca "kod kendisiyle tutarlı"
/// derdi, "MIP'in formuyla tutarlı" demezdi.
/// </summary>
public class EquipmentOperationTableTests
{
    // MIP'in Excel formundaki dokuz sütun, o sırayla.
    private static readonly string[] ExpectedHeaders =
    [
        "İşin Dönemi/Period (Ay ve Yıl)",
        "Yapılan İşin Tarihi/Date of Work",
        "İşin Yapıldığı Yer/Location of Work",
        "Yapılan İşin Tanımı/Description of Work",
        "Talep Eden Müdürlük",
        "Tonajı / Vinç Türü",
        "İşin Başlama Saati/Time to Start Work",
        "İşin Bitiş Saati/Time to Finish Work",
        "Fiş No"
    ];

    private const int HeaderRow = EquipmentOperationExcelBuilder.HeaderRow;

    private static EquipmentOperationTable BuildTable(params EquipmentOperationRow[] rows) =>
        new()
        {
            PeriodId = 1,
            Year = 2026,
            Month = 1,
            Header = new EquipmentOperationFormHeader
            {
                FullName = "Şükrü Çağlayan",
                Position = "Ekipman Müdürü",
                DepartmentName = "Ekipman Bakım Müdürlüğü",
                IssueDate = new DateOnly(2026, 2, 3)
            },
            Rows = rows.Length > 0 ? rows.ToList() : [SampleRow()]
        };

    private static EquipmentOperationRow SampleRow(string? receiptNo = "FS-00417") =>
        new()
        {
            PeriodLabel = "2026 / 01",
            WorkDate = new DateOnly(2026, 1, 19),
            Location = "11 Nolu Rıhtım",
            WorkDescription = "Gemi bordasında aydınlatma direği değişimi",
            DepartmentName = "Ekipman Bakım Müdürlüğü",
            VariantNames = "60 Ton / Kancalı",
            StartTime = new TimeOnly(8, 30),
            EndTime = new TimeOnly(16, 45),
            ReceiptNo = receiptNo
        };

    private static IXLWorksheet Reopen(EquipmentOperationTable table)
    {
        var stream = new MemoryStream(EquipmentOperationExcelBuilder.Build(table));
        return new XLWorkbook(stream).Worksheet(1);
    }

    // ---------------------------------------------------------------
    // 1) Sütun sırası ve başlıkları birebir doğru
    // ---------------------------------------------------------------

    [Fact]
    public void Headers_MatchTheMipFormExactly()
    {
        var sheet = Reopen(BuildTable());

        for (var i = 0; i < ExpectedHeaders.Length; i++)
        {
            Assert.Equal(ExpectedHeaders[i], sheet.Cell(HeaderRow, i + 1).GetString());
        }

        // Onuncu sütun HİÇ AÇILMAZ: form dokuz sütundur, fazlası "buraya bir
        // şey daha eklenebilir" demektir.
        Assert.True(sheet.Cell(HeaderRow, ExpectedHeaders.Length + 1).IsEmpty());
    }

    /// <summary>Üst blok: form başlığı ve formu hazırlayanın dört satırı.</summary>
    [Fact]
    public void FormHeaderBlock_IsWrittenAboveTheTable()
    {
        var sheet = Reopen(BuildTable());

        Assert.Equal(
            "Cherrypicker Rental Approval Form / Çekici Kiralama Onay Formu",
            sheet.Cell(1, 1).GetString());
        Assert.Equal("Formu hazırlayanın bilgileri:", sheet.Cell(2, 1).GetString());

        Assert.Equal("Full Name / İsim Soyad", sheet.Cell(3, 1).GetString());
        Assert.Equal("Şükrü Çağlayan", sheet.Cell(3, 2).GetString());
        Assert.Equal("Position / Görevi", sheet.Cell(4, 1).GetString());
        Assert.Equal("Ekipman Müdürü", sheet.Cell(4, 2).GetString());
        Assert.Equal("Department / Bölüm", sheet.Cell(5, 1).GetString());
        Assert.Equal("Ekipman Bakım Müdürlüğü", sheet.Cell(5, 2).GetString());
        Assert.Equal("Date of Issue / Düzenleme Tarihi", sheet.Cell(6, 1).GetString());
        Assert.Equal(new DateTime(2026, 2, 3), sheet.Cell(6, 2).GetDateTime());
    }

    // ---------------------------------------------------------------
    // 2) Hiçbir hücrede tutar yok
    // ---------------------------------------------------------------

    /// <summary>
    /// Para sütunu "yanlışlıkla" eklenemez: bu tabloda SAYI hücresi diye bir şey
    /// yoktur. Tarih ve saat de sayıdır ama Excel onları ayrı tiplerle işaretler;
    /// geriye kalan her sayı ancak bir tutar ya da miktar olabilir.
    /// </summary>
    [Fact]
    public void NoCellCarriesANumber_SoNoCellCanCarryAnAmount()
    {
        var sheet = Reopen(BuildTable());

        var numericCells = sheet.CellsUsed()
            .Where(c => c.DataType == XLDataType.Number)
            .Select(c => c.Address.ToString())
            .ToList();

        Assert.Empty(numericCells);
    }

    [Fact]
    public void NoHeaderMentionsMoney()
    {
        var sheet = Reopen(BuildTable());
        string[] forbidden = ["Tutar", "Fiyat", "Bedel", "TL", "Toplam", "Birim Fiyat"];

        var headers = Enumerable.Range(1, ExpectedHeaders.Length)
            .Select(c => sheet.Cell(HeaderRow, c).GetString())
            .ToList();

        Assert.DoesNotContain(headers, h => forbidden.Any(f => h.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Toplam satırı YOK: son veri satırından sonra hiçbir şey yazılmaz.</summary>
    [Fact]
    public void NoTotalRow_IsAppended()
    {
        var table = BuildTable(SampleRow(), SampleRow("FS-00418"));
        var sheet = Reopen(table);

        Assert.Equal(HeaderRow + table.Rows.Count, sheet.LastRowUsed()!.RowNumber());
    }

    // ---------------------------------------------------------------
    // 3) Tarih ve saat hücreleri gerçek tipte
    // ---------------------------------------------------------------

    [Fact]
    public void WorkDate_IsARealDateCell()
    {
        var cell = Reopen(BuildTable()).Cell(HeaderRow + 1, 2);

        Assert.Equal(XLDataType.DateTime, cell.DataType);
        Assert.Equal(new DateTime(2026, 1, 19), cell.GetDateTime());
    }

    /// <summary>
    /// Saat METİN değil: "16:45" - "08:30" çıkarması Excel'de çalışmalı, yoksa
    /// tabloyu alan kişi süreleri elle sayar.
    /// </summary>
    [Fact]
    public void StartAndEndTime_AreRealTimeCells()
    {
        var sheet = Reopen(BuildTable());

        var start = sheet.Cell(HeaderRow + 1, 7);
        var end = sheet.Cell(HeaderRow + 1, 8);

        Assert.Equal(XLDataType.TimeSpan, start.DataType);
        Assert.Equal(XLDataType.TimeSpan, end.DataType);
        Assert.Equal(new TimeSpan(8, 30, 0), start.GetTimeSpan());
        Assert.Equal(new TimeSpan(16, 45, 0), end.GetTimeSpan());
    }

    // ---------------------------------------------------------------
    // 4) Dönem biçimi "YYYY / MM"
    // ---------------------------------------------------------------

    [Fact]
    public void PeriodColumn_IsYearSlashTwoDigitMonth()
    {
        Assert.Equal("2026 / 01", EquipmentOperationsController.FormatPeriod(2026, 1));
        Assert.Equal("2026 / 12", EquipmentOperationsController.FormatPeriod(2026, 12));

        Assert.Equal("2026 / 01", Reopen(BuildTable()).Cell(HeaderRow + 1, 1).GetString());
    }

    /// <summary>
    /// Fiş numarası olmayan kayıtta hücre BOŞ kalır, "-" yazılmaz: "-" dolu bir
    /// hücredir ve fişsiz işleri sayan basit bir COUNTA'yı yanıltır.
    /// </summary>
    [Fact]
    public void MissingReceiptNo_LeavesTheCellEmpty()
    {
        var cell = Reopen(BuildTable(SampleRow(receiptNo: null))).Cell(HeaderRow + 1, 9);

        Assert.True(cell.IsEmpty());
    }

    // ---------------------------------------------------------------
    // 5) Firma kullanıcısı bu ekrana giremiyor
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(RoleNames.FirmManager)]
    [InlineData(RoleNames.FirmUser)]
    [InlineData(RoleNames.FirmOperator)]
    public async Task FirmRoles_CannotOpenTheOperationsScreen(string role)
    {
        // Tablo BÜTÜN firmaların işlerini tek listede gösteriyor; bir alt
        // yüklenicinin rakibinin iş hacmini görmesi kural 7'nin ihlalidir.
        var result = await Authorize(Principal(firmId: 1, role), PolicyNames.CanViewEquipmentOperations);
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(RoleNames.EquipmentManager)]
    [InlineData(RoleNames.EquipmentViewer)]
    [InlineData(RoleNames.Admin)]
    public async Task EquipmentAndAdminRoles_CanOpenTheOperationsScreen(string role)
    {
        var result = await Authorize(Principal(firmId: null, role), PolicyNames.CanViewEquipmentOperations);
        Assert.True(result.Succeeded);
    }

    /// <summary>Talep açan ve Bütçe'nin de işi değil: ekran Ekipman Müdürlüğü'nündür.</summary>
    [Theory]
    [InlineData(RoleNames.Requester)]
    [InlineData(RoleNames.Budget)]
    [InlineData(RoleNames.BudgetManager)]
    [InlineData(RoleNames.Accounting)]
    public async Task OtherMipRoles_CannotOpenTheOperationsScreen(string role)
    {
        var result = await Authorize(Principal(firmId: null, role), PolicyNames.CanViewEquipmentOperations);
        Assert.False(result.Succeeded);
    }

    // ---------------------------------------------------------------
    // 6) Sadece APPROVED / LOCKED kayıtlar geliyor
    // ---------------------------------------------------------------

    [Fact]
    public async Task OnlyApprovedAndLockedRecords_EnterTheTable()
    {
        await using var connection = await SeedAsync();
        var controller = CreateController(connection);

        var model = Assert.IsType<EquipmentOperationViewModel>(
            Assert.IsType<ViewResult>(await controller.Index(PeriodId, null, null)).Model);

        var table = Assert.IsAssignableFrom<EquipmentOperationTable>(model.Table);

        Assert.Equal(
            ["WR-APPROVED", "WR-LOCKED"],
            table.Rows.Select(r => r.ReceiptNo!).Order().ToArray());

        // Karara bağlanmamış kayıtlar ENGELLEMEZ, uyarır: DRAFT + SUBMITTED.
        Assert.Equal(2, table.PendingCount);
    }

    /// <summary>
    /// Yerine yeni versiyon geçmiş kayıt tabloda İKİ KEZ görünmez — hakediş
    /// icmalindeki kuralın aynısı.
    /// </summary>
    [Fact]
    public async Task SupersededRecord_IsNotListed()
    {
        await using var connection = await SeedAsync(withSupersededApproved: true);
        var controller = CreateController(connection);

        var model = Assert.IsType<EquipmentOperationViewModel>(
            Assert.IsType<ViewResult>(await controller.Index(PeriodId, null, null)).Model);

        Assert.DoesNotContain(model.Table!.Rows, r => r.ReceiptNo == "WR-SUPERSEDED");
    }

    /// <summary>
    /// "Talep Eden Müdürlük" kaydın departmanından gelir; talepsiz elle girilen
    /// kayıtta kaydı GİRDİREN MIP kullanıcısının departmanına düşer.
    /// </summary>
    [Fact]
    public async Task DepartmentFallsBackToTheRequestingUsersDepartment()
    {
        await using var connection = await SeedAsync();
        var controller = CreateController(connection);

        var model = Assert.IsType<EquipmentOperationViewModel>(
            Assert.IsType<ViewResult>(await controller.Index(PeriodId, null, null)).Model);

        var approved = model.Table!.Rows.Single(r => r.ReceiptNo == "WR-APPROVED");
        var locked = model.Table.Rows.Single(r => r.ReceiptNo == "WR-LOCKED");

        Assert.Equal("Ekipman Vardiya Servis Müdürlüğü", approved.DepartmentName);
        Assert.Equal("Ekipman Bakım Müdürlüğü", locked.DepartmentName);
    }

    [Fact]
    public async Task VariantFilter_NarrowsTheTable()
    {
        await using var connection = await SeedAsync();
        var controller = CreateController(connection);

        var model = Assert.IsType<EquipmentOperationViewModel>(
            Assert.IsType<ViewResult>(await controller.Index(PeriodId, null, VariantId)).Model);

        Assert.Equal(["WR-APPROVED"], model.Table!.Rows.Select(r => r.ReceiptNo!).ToArray());
    }

    // ---------------------------------------------------------------
    // Yardımcılar
    // ---------------------------------------------------------------

    private const int PeriodId = 1;
    private const int FirmId = 1;
    private const int ContractId = 1;
    private const int ServiceId = 1;
    private const int VariantId = 1;
    private const int OtherVariantId = 2;
    private const int EquipmentUserId = 1;
    private const int RequesterUserId = 2;

    private static AppDbContext CreateContext(SqliteConnection connection, ICurrentUser user) =>
        new SqliteTestContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options, user);

    private static EquipmentOperationsController CreateController(SqliteConnection connection)
    {
        var user = new FakeCurrentUser { UserId = EquipmentUserId, FullName = "Ekipman Müdürü" };
        return new EquipmentOperationsController(CreateContext(connection, user), user);
    }

    private static async Task<SqliteConnection> SeedAsync(bool withSupersededApproved = false)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var db = CreateContext(connection, new FakeCurrentUser());
        await db.Database.EnsureCreatedAsync();

        // Dönemler, hizmet kategorisi ve iki varyant AppDbContext'in HasData'sıyla
        // ZATEN gelir (PeriodId 1 = 2026/01, ServiceId 1 = CHERRY_PICKER,
        // VariantId 1/2 = 30T/60T Sepetli). Tekrar eklemek UNIQUE ihlali olur.
        db.Firms.Add(new Firm { FirmId = FirmId, Code = "TESTVINC", Title = "Test Vinç", CreatedAt = DateTime.UtcNow });
        db.Contracts.Add(new Contract
        {
            ContractId = ContractId,
            FirmId = FirmId,
            ContractNo = "SOZ-DEMO-001",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            CreatedAt = DateTime.UtcNow
        });
        db.Departments.AddRange(
            new Department { DepartmentId = 1, Code = "EKIPMAN_BAKIM", Name = "Ekipman Bakım Müdürlüğü" },
            new Department { DepartmentId = 2, Code = "EKIPMAN_VARDIYA", Name = "Ekipman Vardiya Servis Müdürlüğü" });
        db.Users.AddRange(
            new User { UserId = EquipmentUserId, UserName = "ekipman1", FullName = "Ekipman Müdürü", DepartmentId = 1, CreatedAt = DateTime.UtcNow },
            new User { UserId = RequesterUserId, UserName = "talep1", FullName = "Saha Sorumlusu", DepartmentId = 2, CreatedAt = DateTime.UtcNow });
        db.Locations.Add(new Location { LocationId = 1, Code = "RIH-11", Name = "11 Nolu Rıhtım", FullPath = "Rıhtımlar > 11 Nolu Rıhtım" });

        // Tabloya GİRECEK ikisi: APPROVED (talepten türemiş, departmanı kendinde)
        // ve LOCKED (talepsiz, departmanı talebi açan kullanıcıdan düşecek).
        db.WorkRecords.Add(NewRecord("WR-APPROVED", WorkRecordStatus.APPROVED, departmentId: 2, variantId: VariantId));
        db.WorkRecords.Add(NewRecord("WR-LOCKED", WorkRecordStatus.LOCKED, departmentId: null, variantId: OtherVariantId,
            requestedByUserId: EquipmentUserId));

        // GİRMEYECEKLER.
        db.WorkRecords.Add(NewRecord("WR-DRAFT", WorkRecordStatus.DRAFT, departmentId: 2, variantId: VariantId));
        db.WorkRecords.Add(NewRecord("WR-SUBMITTED", WorkRecordStatus.SUBMITTED, departmentId: 2, variantId: VariantId));
        db.WorkRecords.Add(NewRecord("WR-REJECTED", WorkRecordStatus.REJECTED, departmentId: 2, variantId: VariantId));
        db.WorkRecords.Add(NewRecord("WR-CANCELLED", WorkRecordStatus.CANCELLED, departmentId: 2, variantId: VariantId));

        if (withSupersededApproved)
        {
            var superseded = NewRecord("WR-SUPERSEDED", WorkRecordStatus.APPROVED, departmentId: 2, variantId: VariantId);
            superseded.IsSuperseded = true;
            db.WorkRecords.Add(superseded);
        }

        await db.SaveChangesAsync();
        return connection;
    }

    private static int _documentNo;

    private static WorkRecord NewRecord(
        string receiptNo, WorkRecordStatus status, int? departmentId, int variantId,
        int requestedByUserId = RequesterUserId)
    {
        var record = new WorkRecord
        {
            DocumentNo = $"WR-2026-{++_documentNo:00000}",
            Status = status,
            FirmId = FirmId,
            ContractId = ContractId,
            PeriodId = PeriodId,
            WorkDate = new DateOnly(2026, 1, 19),
            StartTime = new TimeOnly(8, 30),
            EndTime = new TimeOnly(16, 45),
            LocationId = 1,
            WorkDescription = "Aydınlatma direği değişimi",
            DepartmentId = departmentId,
            RequestedByUserId = requestedByUserId,
            ExternalReceiptNo = receiptNo,
            EnteredByUserId = EquipmentUserId,
            CreatedAt = DateTime.UtcNow
        };

        record.WorkRecordLines.Add(new WorkRecordLine
        {
            LineNo = 1,
            ServiceId = ServiceId,
            VariantId = variantId,
            RawQuantity = 8.25m,
            BillableQuantity = 8.5m,
            Unit = ServiceUnit.HOUR
        });

        return record;
    }

    private static async Task<AuthorizationResult> Authorize(ClaimsPrincipal principal, string policy) =>
        await new ServiceCollection()
            .AddLogging()
            .AddAuthorization(AuthorizationPolicies.AddAppPolicies)
            .BuildServiceProvider()
            .GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(principal, policy);

    private static ClaimsPrincipal Principal(int? firmId, params string[] roles)
    {
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r)).ToList();
        if (firmId is int id)
        {
            claims.Add(new Claim(AppClaimTypes.FirmId, id.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test", ClaimTypes.Name, ClaimTypes.Role));
    }
}
