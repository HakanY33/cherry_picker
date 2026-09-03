using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MipRental.Data;
using MipRental.Data.Interceptors;
using MipRental.Data.Reporting;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Pricing;
using MipRental.Web.Controllers;
using MipRental.Web.Documents;
using MipRental.Web.Models.WorkRecords;
using MipRental.Web.Security;
using QuestPDF.Infrastructure;

namespace MipRental.Tests;

/// <summary>
/// ADIM 9 — FİYAT GİZLİLİĞİ.
///
/// Kural: para bilgisi SADECE BUDGET, BUDGET_MANAGER, ADMIN ve ACCOUNTING rollerine
/// görünür. Firma kullanıcıları ve Ekipman Müdürlüğü görmez. Miktar HERKESE görünür.
///
/// Bu testler "view'da gizlendi mi" diye BAKMAZ — modele/servise dönen VERİDE
/// para alanının HİÇ BULUNMADIĞINI doğrular. View'da gizlemek yetersizdir;
/// gizlenen bir alan yine de gönderilmiş demektir.
/// </summary>
public class PricingPrivacyTests
{
    private const int FirmId = 1;
    private const int OtherFirmId = 2;
    private const int ServiceId = 1;
    private const int PeriodId = 3;
    private const int MipUserId = 1;
    private const int FirmUserId = 2;
    private const int WorkRecordId = 1;

    private const decimal UnitPrice = 100m;
    private const decimal LineAmount = 750m;
    private const decimal MobilizationFee = 250m;
    private const decimal TotalAmount = 1_000m;

    // Snapshot Adım 9 formatında: miktar ve tutar açıklaması AYRI dizilerde.
    private const string Snapshot = """
        {"unitPrice":100.0,
         "quantityExplanation":["Ham süre: 7 saat 30 dakika","30 dakikaya yuvarlandı: 7,5 saat"],
         "amountExplanation":["7,5 × 100,00 = 750,00 TRY","Satır tutarı: 750,00 TRY"]}
        """;

    static PricingPrivacyTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static FakeCurrentUser FirmUser() => new() { UserId = FirmUserId, FirmId = FirmId };

    private static FakeCurrentUser BudgetUser() =>
        new() { UserId = MipUserId, Roles = { RoleNames.Budget } };

    /// <summary>Muhasebe: onay zincirinde değil ama e-fatura kontrolü için parayı GÖRÜR.</summary>
    private static FakeCurrentUser AccountingUser() =>
        new() { UserId = 4, Roles = { RoleNames.Accounting } };

    /// <summary>Ekipman Müdürlüğü: onaylayabilir ama parayı GÖREMEZ.</summary>
    private static FakeCurrentUser EquipmentManager() =>
        new() { UserId = 3, Roles = { RoleNames.EquipmentManager } };

    private static AppDbContext CreateContext(SqliteConnection connection, ICurrentUser user) =>
        new SqliteTestContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new PeriodGuardInterceptor(), new ImmutabilityGuardInterceptor())
                .Options,
            user);

    private static async Task<SqliteConnection> CreateSeededConnectionAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var db = CreateContext(connection, new FakeCurrentUser());
        await db.Database.EnsureCreatedAsync();

        db.Firms.Add(new Firm { FirmId = FirmId, Code = "TESTVINC", Title = "Test Vinç Ltd. Şti.", CreatedAt = DateTime.UtcNow });
        db.Firms.Add(new Firm { FirmId = OtherFirmId, Code = "DIGER", Title = "Diğer Firma", CreatedAt = DateTime.UtcNow });
        db.Users.Add(new User { UserId = MipUserId, UserName = "butce", FullName = "Bütçe Kullanıcısı", CreatedAt = DateTime.UtcNow });
        db.Users.Add(new User { UserId = FirmUserId, UserName = "testvinc", FullName = "Firma Kullanıcısı", FirmId = FirmId, CreatedAt = DateTime.UtcNow });
        db.Contracts.Add(new Contract
        {
            ContractId = 1,
            FirmId = FirmId,
            ContractNo = "SÖZ-2026-001",
            Title = "Mobil Vinç Kiralama",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Currency = "TRY",
            Status = ContractStatus.ACTIVE,
            CreatedAt = DateTime.UtcNow
        });

        var record = new WorkRecord
        {
            WorkRecordId = WorkRecordId,
            DocumentNo = "WR-2026-00001",
            Status = WorkRecordStatus.APPROVED,
            FirmId = FirmId,
            ContractId = 1,
            PeriodId = PeriodId,
            WorkDate = new DateOnly(2026, 3, 19),
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(15, 30),
            LocationText = "İskele 3",
            OperatorName = "Şükrü Çağlayan",
            EnteredByUserId = FirmUserId,
            MobilizationFee = MobilizationFee,
            TotalAmount = TotalAmount,
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
            UnitPriceSnapshot = UnitPrice,
            LineAmount = LineAmount,
            Currency = "TRY",
            PricingRuleSnapshot = Snapshot
        });
        db.WorkRecords.Add(record);

        await db.SaveChangesAsync();
        return connection;
    }

    /// <summary>
    /// Pdf action'i dogrulama adresini Url.Action + Request.Scheme ile kurar;
    /// unit testte ikisi de yok. UrlHelper null dondurunce controller kendi
    /// fallback'ine duser, o da HttpContext ister.
    /// </summary>
    private static WorkRecordsController WorkRecordsControllerWithUrls(SqliteConnection connection, ICurrentUser user)
    {
        var controller = ApprovalTestFactory.CreateWorkRecordsController(CreateContext(connection, user), user);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("miprental.test");
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.Url = new NullUrlHelper(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));
        return controller;
    }

    private sealed class NullUrlHelper(ActionContext actionContext) : IUrlHelper
    {
        public ActionContext ActionContext { get; } = actionContext;
        public string? Action(UrlActionContext actionContext) => null;
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }

    private static async Task<WorkRecordDetailsViewModel> DetailsAsync(SqliteConnection connection, ICurrentUser user)
    {
        var controller = ApprovalTestFactory.CreateWorkRecordsController(CreateContext(connection, user), user);
        var result = Assert.IsType<ViewResult>(await controller.Details(WorkRecordId));
        return Assert.IsType<WorkRecordDetailsViewModel>(result.Model);
    }

    // ---------------------------------------------------------------
    // 1) Firma kullanıcısına dönen veride TUTAR YOK
    // ---------------------------------------------------------------

    [Fact]
    public async Task FirmUser_WorkRecordDetails_CarriesNoAmounts()
    {
        await using var connection = await CreateSeededConnectionAsync();

        var model = await DetailsAsync(connection, FirmUser());

        // Alanlar "boş" değil, HİÇ YOK: para nesneleri kurulmamış.
        Assert.Null(model.Pricing);
        Assert.All(model.Lines, l => Assert.Null(l.Pricing));
    }

    /// <summary>
    /// Aşırı kısıtlama YAPILMADIĞININ kanıtı: firma "kaç saat faturalanacak"
    /// bilgisini ve gerekçesini görmeye devam eder.
    /// </summary>
    [Fact]
    public async Task FirmUser_WorkRecordDetails_StillSeesQuantities()
    {
        await using var connection = await CreateSeededConnectionAsync();

        var model = await DetailsAsync(connection, FirmUser());
        var line = Assert.Single(model.Lines);

        Assert.Equal(7.5m, line.RawQuantity);
        Assert.Equal(7.5m, line.BillableQuantity);
        Assert.Equal(ServiceUnit.HOUR, line.Unit);

        // "Neden 7,5 saat" cevabı duruyor.
        Assert.Contains(line.QuantityExplanation, e => e.Contains("yuvarlandı"));
    }

    /// <summary>
    /// PricingRuleSnapshot içinde birim fiyat geçer; yetkisiz kullanıcıya HİÇ
    /// dönmemeli. Ham JSON'un modelin HİÇBİR yerinde bulunmadığını doğruluyoruz.
    /// </summary>
    [Fact]
    public async Task FirmUser_PricingRuleSnapshot_IsNeverReturned()
    {
        await using var connection = await CreateSeededConnectionAsync();

        var model = await DetailsAsync(connection, FirmUser());

        Assert.All(model.Lines, l => Assert.Null(l.Pricing?.RawSnapshot));

        var everyString = model.Lines
            .SelectMany(l => l.QuantityExplanation)
            .Concat(model.Lines.Select(l => l.ServiceName))
            .Concat(model.AuditEntries.SelectMany(a => new[] { a.FieldName, a.OldValue, a.NewValue, a.Reason }))
            .Where(s => s is not null)
            .ToList();

        Assert.DoesNotContain(everyString, s => s!.Contains("unitPrice", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(everyString, s => s!.Contains("amountExplanation", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Denetim izinde para alanının DEĞERİ maskeli; alan adı ve zaman görünür.</summary>
    [Fact]
    public async Task FirmUser_AuditTrail_MasksMoneyValuesButKeepsTheEvent()
    {
        await using var connection = await CreateSeededConnectionAsync();

        await using (var db = CreateContext(connection, BudgetUser()))
        {
            db.AuditLogs.Add(new AuditLog
            {
                TableName = "WorkRecords",
                RecordId = WorkRecordId,
                Action = AuditAction.UPDATE,
                FieldName = "TotalAmount",
                OldValue = "900.0000",
                NewValue = "1000.0000",
                OccurredAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var firmModel = await DetailsAsync(connection, FirmUser());
        var firmEntry = Assert.Single(firmModel.AuditEntries, a => a.FieldName == "TotalAmount");

        // Olayın kendisi görünür (ne değişti, ne zaman) — rakam görünmez.
        Assert.Equal(PricingFields.MaskedValue, firmEntry.OldValue);
        Assert.Equal(PricingFields.MaskedValue, firmEntry.NewValue);
        Assert.Equal("TotalAmount", firmEntry.FieldName);

        var budgetModel = await DetailsAsync(connection, BudgetUser());
        var budgetEntry = Assert.Single(budgetModel.AuditEntries, a => a.FieldName == "TotalAmount");
        Assert.Equal("1000.0000", budgetEntry.NewValue);
    }

    // ---------------------------------------------------------------
    // 2) BUDGET her şeyi görür — kural aşırı geniş uygulanmıyor
    // ---------------------------------------------------------------

    [Fact]
    public async Task BudgetUser_WorkRecordDetails_SeesEverything()
    {
        await using var connection = await CreateSeededConnectionAsync();

        var model = await DetailsAsync(connection, BudgetUser());

        Assert.NotNull(model.Pricing);
        Assert.Equal(TotalAmount, model.Pricing!.TotalAmount);
        Assert.Equal(MobilizationFee, model.Pricing.MobilizationFee);

        var line = Assert.Single(model.Lines);
        Assert.NotNull(line.Pricing);
        Assert.Equal(UnitPrice, line.Pricing!.UnitPrice);
        Assert.Equal(LineAmount, line.Pricing.LineAmount);
        Assert.False(string.IsNullOrWhiteSpace(line.Pricing.RawSnapshot));
    }

    /// <summary>
    /// Muhasebe alt yüklenici e-faturasını maliyet tablosuyla karşılaştırır;
    /// tutarı göremeyen muhasebe iş yapamaz.
    /// </summary>
    [Fact]
    public async Task AccountingUser_WorkRecordDetails_SeesAmounts()
    {
        await using var connection = await CreateSeededConnectionAsync();

        var model = await DetailsAsync(connection, AccountingUser());

        Assert.NotNull(model.Pricing);
        Assert.Equal(TotalAmount, model.Pricing!.TotalAmount);
        Assert.Equal(LineAmount, Assert.Single(model.Lines).Pricing!.LineAmount);
    }

    /// <summary>
    /// Ekipman Müdürlüğü onaylayabilir ama parayı göremez: "ne yapabilir" ile
    /// "neyi görebilir" ayrı eksenlerdir.
    /// </summary>
    [Fact]
    public async Task EquipmentManager_CanApproveButSeesNoAmounts()
    {
        await using var connection = await CreateSeededConnectionAsync();

        var model = await DetailsAsync(connection, EquipmentManager());

        Assert.Null(model.Pricing);
        Assert.All(model.Lines, l => Assert.Null(l.Pricing));
        Assert.NotEmpty(model.Lines); // kaydı görüyor, sadece tutarını görmüyor
    }

    // ---------------------------------------------------------------
    // 3) Liste ekranı
    // ---------------------------------------------------------------

    [Fact]
    public async Task WorkRecordIndex_ShowsAmountOnlyToAuthorized()
    {
        await using var connection = await CreateSeededConnectionAsync();

        var firmUser = FirmUser();
        var firmResult = Assert.IsType<ViewResult>(
            await ApprovalTestFactory.CreateWorkRecordsController(CreateContext(connection, firmUser), firmUser).Index());
        var firmModel = Assert.IsType<WorkRecordIndexViewModel>(firmResult.Model);

        Assert.False(firmModel.ShowPricing);
        Assert.All(firmModel.Items, i => Assert.Null(i.Pricing));

        var budgetUser = BudgetUser();
        var budgetResult = Assert.IsType<ViewResult>(
            await ApprovalTestFactory.CreateWorkRecordsController(CreateContext(connection, budgetUser), budgetUser).Index());
        var budgetModel = Assert.IsType<WorkRecordIndexViewModel>(budgetResult.Model);

        Assert.True(budgetModel.ShowPricing);
        Assert.Equal(TotalAmount, Assert.Single(budgetModel.Items).Pricing!.TotalAmount);
    }

    // ---------------------------------------------------------------
    // 4) PDF: yetkisiz kullanıcı fiyatlı sürümü URL'i elle yazarak da alamaz
    // ---------------------------------------------------------------

    [Fact]
    public async Task FirmUser_RequestingPdfDirectly_GetsUnpricedVersion()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var firmUser = FirmUser();

        // Aynı adres (/WorkRecords/Pdf/1). Yetki belge ÜRETİLİRKEN uygulanır,
        // ayrı bir URL yok — elle yazarak fiyatlı sürüme geçilemez.
        var result = Assert.IsType<FileContentResult>(
            await WorkRecordsControllerWithUrls(connection, firmUser).Pdf(WorkRecordId));

        Assert.EndsWith("-Fiyatsiz.pdf", result.FileDownloadName);
    }

    [Fact]
    public async Task BudgetUser_RequestingPdf_GetsPricedVersion()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var budgetUser = BudgetUser();

        var result = Assert.IsType<FileContentResult>(
            await WorkRecordsControllerWithUrls(connection, budgetUser).Pdf(WorkRecordId));

        Assert.DoesNotContain("Fiyatsiz", result.FileDownloadName);
    }

    /// <summary>Fiyatsız PDF modelinde para tarafı hiç kurulmaz; miktar durur.</summary>
    [Fact]
    public async Task UnpricedWorkRecordForm_HasQuantitiesButNoAmounts()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var firmUser = FirmUser();
        var generator = ApprovalTestFactory.CreateDocumentGenerator(CreateContext(connection, firmUser), firmUser);

        var model = await generator.BuildWorkRecordFormModelAsync(
            WorkRecordId, code => $"https://test/Dogrula/{code}", includePricing: false);

        Assert.Null(model.Pricing);
        Assert.All(model.Lines, l => Assert.Null(l.Pricing));
        Assert.Equal(7.5m, Assert.Single(model.Lines).BillableQuantity);
        Assert.Contains(model.QuantityExplanation, e => e.Contains("yuvarlandı"));
    }

    // ---------------------------------------------------------------
    // 5) Aylık icmal
    // ---------------------------------------------------------------

    [Fact]
    public async Task FirmUser_MonthlySummary_HasQuantitiesButNoAmounts()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var firmUser = FirmUser();

        var summary = await new MonthlySummaryService(CreateContext(connection, firmUser), firmUser)
            .BuildAsync(PeriodId, FirmId);

        Assert.False(summary.IncludesPricing);
        Assert.Null(summary.LinesTotal);
        Assert.Null(summary.MobilizationTotal);
        Assert.Null(summary.GrandTotal);
        Assert.Empty(summary.Mobilizations);
        Assert.All(summary.ServiceGroups, g => Assert.Null(g.SubtotalAmount));
        Assert.All(summary.ServiceGroups.SelectMany(g => g.Lines), l => Assert.Null(l.Pricing));

        // Miktar tarafı duruyor.
        Assert.Equal(1, summary.RecordCount);
        Assert.Equal(7.5m, Assert.Single(summary.QuantityTotals).TotalBillableQuantity);
    }

    [Fact]
    public async Task BudgetUser_MonthlySummary_HasAmounts()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var budgetUser = BudgetUser();

        var summary = await new MonthlySummaryService(CreateContext(connection, budgetUser), budgetUser)
            .BuildAsync(PeriodId, FirmId);

        Assert.True(summary.IncludesPricing);
        Assert.Equal(LineAmount, summary.LinesTotal);
        Assert.Equal(MobilizationFee, summary.MobilizationTotal);
        Assert.Equal(LineAmount + MobilizationFee, summary.GrandTotal);
        Assert.Equal(UnitPrice, Assert.Single(summary.ServiceGroups.SelectMany(g => g.Lines)).Pricing!.UnitPrice);
    }

    // ---------------------------------------------------------------
    // 6) Açıklama satırlarının ayrımı (PricingCalculator)
    // ---------------------------------------------------------------

    [Fact]
    public void PricingCalculator_SplitsQuantityAndAmountExplanations()
    {
        var contractLine = new ContractLine
        {
            ContractLineId = 1,
            ContractId = 1,
            ServiceId = ServiceId,
            UnitPrice = 1_250m,
            Currency = "TRY",
            RoundingRule = RoundingRule.UP_30,
            MinBillableQuantity = 4m,
            ValidFrom = new DateOnly(2026, 1, 1),
            ServiceCategory = new ServiceCategory { ServiceId = ServiceId, Name = "Mobil Vinç", Unit = ServiceUnit.HOUR }
        };

        var result = PricingCalculator.Calculate(new PricingRequest
        {
            ContractLine = contractLine,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(15, 10)
        });

        // Miktar tarafı: "neden 7,5 saat".
        Assert.Contains(result.QuantityExplanation, e => e.Contains("Ham süre"));
        Assert.Contains(result.QuantityExplanation, e => e.Contains("yuvarlandı"));

        // Tutar tarafı: birim fiyat ve satır tutarı.
        Assert.Contains(result.AmountExplanation, e => e.Contains("1.250,00"));
        Assert.Contains(result.AmountExplanation, e => e.Contains("Satır tutarı"));

        // KRİTİK: miktar açıklamasında para GEÇMEZ. Firma bu listeyi görür.
        Assert.DoesNotContain(result.QuantityExplanation, e => e.Contains("1.250,00"));
        Assert.DoesNotContain(result.QuantityExplanation, e => e.Contains("TRY"));
    }

    // ---------------------------------------------------------------
    // 7) Doğrulama sayfası (anonim erişim)
    // ---------------------------------------------------------------

    /// <summary>
    /// /Dogrula/{kod} anonim erişilebilir: karekodu gören herkes döneni görür.
    /// Sonuç nesnesinde PARASAL bir alan HİÇ OLMAMALI — model boş dönmüyor,
    /// alan hiç yok. Tek tek alan adı yazmak yerine tipin tamamını tarıyoruz;
    /// ileride biri decimal bir alan eklerse bu test yakalar.
    /// </summary>
    [Fact]
    public void VerificationResult_HasNoMonetaryField()
    {
        var moneyLike = typeof(MipRental.Data.Services.DocumentVerificationResult)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(decimal) || p.PropertyType == typeof(decimal?))
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(moneyLike);

        Assert.DoesNotContain(typeof(MipRental.Data.Services.DocumentVerificationResult).GetProperties(),
            p => p.Name is "TotalAmount" or "Currency");
    }

    [Fact]
    public async Task AnonymousVerification_ReturnsNoAmount()
    {
        await using var connection = await CreateSeededConnectionAsync();

        // Anonim kullanıcı: ne firma ne MIP personeli; hiçbir rolü yok.
        var anonymous = new FakeCurrentUser();
        await using var db = CreateContext(connection, anonymous);

        db.GeneratedDocuments.Add(new GeneratedDocument
        {
            DocumentType = DocumentType.WORK_RECORD,
            DocumentId = WorkRecordId,
            Kind = GeneratedDocumentKind.FORM_PDF,
            FirmId = FirmId,
            FileName = "form.pdf",
            StoragePath = "2026/03/form.pdf",
            ContentHash = new string('a', 64),
            VerificationCode = "TESTKOD123",
            TotalAmount = TotalAmount,
            Currency = "TRY",
            GeneratedAt = DateTime.UtcNow,
            GeneratedBy = MipUserId
        });
        await db.SaveChangesAsync();

        var result = await new MipRental.Data.Services.DocumentVerificationService(db).VerifyAsync("TESTKOD123");

        Assert.NotNull(result);
        Assert.Equal("WR-2026-00001", result!.DocumentNo);

        // Arşivde tutar duruyor (mali gerçek), ama doğrulama sonucuna GEÇMİYOR.
        var returnedValues = typeof(MipRental.Data.Services.DocumentVerificationResult)
            .GetProperties()
            .Select(p => p.GetValue(result)?.ToString())
            .Where(v => v is not null)
            .ToList();

        Assert.DoesNotContain(returnedValues, v => v!.Contains("1000"));
    }

    // ---------------------------------------------------------------
    // 8) Politikalar: firma kullanıcısı sözleşme ekranlarına giremez
    // ---------------------------------------------------------------

    private static IAuthorizationService BuildAuthorizationService() =>
        new ServiceCollection()
            .AddLogging()
            .AddAuthorization(AuthorizationPolicies.AddAppPolicies)
            .BuildServiceProvider()
            .GetRequiredService<IAuthorizationService>();

    private static ClaimsPrincipal Principal(int? firmId, params string[] roles)
    {
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r)).ToList();
        if (firmId is int id)
        {
            claims.Add(new Claim(AppClaimTypes.FirmId, id.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test", ClaimTypes.Name, ClaimTypes.Role));
    }

    [Theory]
    [InlineData(PolicyNames.CanManageContract)]
    [InlineData(PolicyNames.CanSeePricing)]
    public async Task FirmUser_FailsPricingPolicies(string policy)
    {
        var auth = BuildAuthorizationService();
        var firmUser = Principal(FirmId, RoleNames.FirmUser);

        Assert.False((await auth.AuthorizeAsync(firmUser, null, policy)).Succeeded);
    }

    /// <summary>
    /// Ekipman Müdürlüğü onay verebilir (CanApprove) ama ne sözleşme ekranına
    /// girebilir ne de fiyat görebilir.
    /// </summary>
    [Fact]
    public async Task EquipmentManager_CanApprove_ButNotSeePricing()
    {
        var auth = BuildAuthorizationService();
        var supervisor = Principal(null, RoleNames.EquipmentManager);

        Assert.True((await auth.AuthorizeAsync(supervisor, null, PolicyNames.CanApprove)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(supervisor, null, PolicyNames.CanSeePricing)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(supervisor, null, PolicyNames.CanManageContract)).Succeeded);
    }

    [Theory]
    [InlineData(RoleNames.Budget)]
    [InlineData(RoleNames.BudgetManager)]
    [InlineData(RoleNames.Admin)]
    [InlineData(RoleNames.Accounting)]
    public async Task PricingRoles_PassCanSeePricing(string role)
    {
        var auth = BuildAuthorizationService();

        Assert.True((await auth.AuthorizeAsync(Principal(null, role), null, PolicyNames.CanSeePricing)).Succeeded);
    }

    /// <summary>
    /// Sözleşme ekranları birim fiyat gösterir; üçü de fiyat yetkisiyle kapalı
    /// olmalı (Adım 9, kapatılan açık 4.1).
    /// </summary>
    [Theory]
    [InlineData(typeof(ContractsController))]
    [InlineData(typeof(ContractLinesController))]
    [InlineData(typeof(ContractLineSurchargesController))]
    public void ContractControllers_RequireContractPolicy(Type controller)
    {
        var attribute = Assert.Single(
            controller.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>());

        Assert.Equal(PolicyNames.CanManageContract, attribute.Policy);
    }
}
