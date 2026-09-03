using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Interceptors;
using MipRental.Data.Pricing;
using MipRental.Data.Services;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Web.Controllers;
using MipRental.Web.Models.WorkRecords;

namespace MipRental.Tests;

// Submit() gerçek bir transaction (Database.BeginTransactionAsync) ve raw SQL
// (DocumentNumberService) kullanıyor; InMemory provider transaction desteklemez,
// bu yüzden burada SQLite kullanılıyor (AuditAtomicityTests'teki ile aynı yaklaşım).
public class WorkRecordSubmissionTests
{
    private const int FirmId = 1;
    private const int OtherFirmId = 2;
    private const int MipUserId = 1;
    private const int FirmUserId = 2;
    private const int OtherFirmUserId = 3;

    // RoleConfiguration seed'i: 6 = FIRM_USER.
    private const int FirmUserRoleId = 6;
    // ServiceId=1 (Mobil Vinç / HOUR) ve PeriodId=3 (2026 / Mart, OPEN) zaten
    // model HasData seed'i ile geliyor (ServiceCategoryConfiguration, PeriodConfiguration);
    // EnsureCreatedAsync bunları otomatik oluşturur, burada TEKRAR eklenmemeli.
    private const int ServiceId = 1;
    private const int PeriodId = 3; // 2026 / Mart

    private static DbContextOptions<AppDbContext> SqliteOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new PeriodGuardInterceptor(), new ImmutabilityGuardInterceptor())
            .Options;

    private static AppDbContext CreateContext(SqliteConnection connection, ICurrentUser currentUser) =>
        new SqliteTestContext(SqliteOptions(connection), currentUser);

    private static async Task<SqliteConnection> CreateSeededConnectionAsync(decimal? mobilizationFee = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            await db.Database.EnsureCreatedAsync();

            db.Firms.Add(new Firm { FirmId = FirmId, Code = "FIRMA-1", Title = "Firma 1", CreatedAt = DateTime.UtcNow });
            db.Firms.Add(new Firm { FirmId = OtherFirmId, Code = "FIRMA-2", Title = "Firma 2", CreatedAt = DateTime.UtcNow });
            db.Users.Add(new User { UserId = MipUserId, UserName = "mip.staff", FullName = "MIP Personeli", CreatedAt = DateTime.UtcNow });
            db.Users.Add(new User { UserId = FirmUserId, UserName = "firma1.kullanici", FullName = "Firma 1 Kullanıcısı", FirmId = FirmId, CreatedAt = DateTime.UtcNow });
            db.Users.Add(new User { UserId = OtherFirmUserId, UserName = "firma2.kullanici", FullName = "Firma 2 Kullanıcısı", FirmId = OtherFirmId, CreatedAt = DateTime.UtcNow });

            // Gönderim artık rol de ister (ADR-028): FIRM_USER, FIRM_MANAGER'a
            // eşdeğer geçiş rolüdür. Aktör rolleri veritabanından okunur, bu
            // yüzden eşleme burada kurulmalı.
            db.UserRoles.Add(new UserRole { UserId = FirmUserId, RoleId = FirmUserRoleId });
            db.UserRoles.Add(new UserRole { UserId = OtherFirmUserId, RoleId = FirmUserRoleId });
            db.Contracts.Add(new Contract
            {
                ContractId = 1,
                FirmId = FirmId,
                ContractNo = "SOZ-1",
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2026, 12, 31),
                Currency = "TRY",
                Status = ContractStatus.ACTIVE,
                CreatedAt = DateTime.UtcNow
            });
            db.ContractLines.Add(new ContractLine
            {
                ContractLineId = 1,
                ContractId = 1,
                ServiceId = ServiceId,
                UnitPrice = 100m,
                MobilizationFee = mobilizationFee,
                Currency = "TRY",
                ValidFrom = new DateOnly(2026, 1, 1),
                ValidTo = null,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        return connection;
    }

    private static WorkRecordsController CreateController(AppDbContext db, ICurrentUser currentUser) =>
        ApprovalTestFactory.CreateWorkRecordsController(db, currentUser);

    private static WorkRecordFormViewModel ValidDraftModel(int serviceId = ServiceId) => new()
    {
        PeriodId = PeriodId,
        WorkDate = new DateOnly(2026, 3, 10),
        StartTime = new TimeOnly(8, 0),
        EndTime = new TimeOnly(12, 0),
        LocationText = "Rıhtım 3",
        WorkDescription = "Konteyner indirme",
        RequestedByUserId = MipUserId,
        WitnessedByUserId = MipUserId,
        OperatorName = "Ahmet Yılmaz",
        LicensePlate = "34ABC34",
        PersonnelCount = 2,
        ExternalReceiptNo = "0078",
        ExternalReceiptDate = new DateOnly(2026, 3, 10),
        Lines = new List<WorkRecordLineFormViewModel> { new() { Index = 0, ServiceId = serviceId } }
    };

    [Fact]
    public async Task Submit_FillsSnapshotFields()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var firmUser = new FakeCurrentUser { UserId = FirmUserId, FirmId = FirmId };

        int workRecordId;
        await using (var db = CreateContext(connection, firmUser))
        {
            var controller = CreateController(db, firmUser);
            var result = await controller.Create(ValidDraftModel());
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            workRecordId = (int)redirect.RouteValues!["id"]!;
        }

        await using (var db = CreateContext(connection, firmUser))
        {
            var controller = CreateController(db, firmUser);
            var result = await controller.Submit(workRecordId);
            Assert.IsType<RedirectToActionResult>(result);
        }

        await using (var db = CreateContext(connection, firmUser))
        {
            var record = await db.WorkRecords.Include(w => w.WorkRecordLines).SingleAsync(w => w.WorkRecordId == workRecordId);
            // Gonderim ilk onay adimini da acar: kayit dogrudan PENDING olur (Adim 7).
            Assert.Equal(WorkRecordStatus.PENDING, record.Status);
            Assert.StartsWith("WR-2026-", record.DocumentNo);
            Assert.Equal(400m, record.TotalAmount); // 4 saat x 100

            var line = Assert.Single(record.WorkRecordLines);
            Assert.Equal(1, line.ContractLineId);
            Assert.Equal(100m, line.UnitPriceSnapshot);
            Assert.Equal(4m, line.BillableQuantity);
            Assert.Equal(400m, line.LineAmount);
            Assert.False(string.IsNullOrWhiteSpace(line.PricingRuleSnapshot));
            Assert.Contains("\"unitPrice\":100", line.PricingRuleSnapshot);
        }
    }

    // --- Mobilizasyon (sefer başı nakliye) bedeli ---

    // Bir çalışma kaydı = araç/ekibin sahaya bir kez gelmesi. Kayıtta kaç hizmet satırı
    // olursa olsun nakliye BİR kez yapılmıştır ve bir kez faturalanır.
    [Fact]
    public async Task Submit_ThreeLines_MobilizationFeeEntersTotalOnlyOnce()
    {
        await using var connection = await CreateSeededConnectionAsync(mobilizationFee: 300m);
        var firmUser = new FakeCurrentUser { UserId = FirmUserId, FirmId = FirmId };

        int workRecordId;
        await using (var db = CreateContext(connection, firmUser))
        {
            var controller = CreateController(db, firmUser);
            var model = ValidDraftModel();
            model.Lines = new List<WorkRecordLineFormViewModel>
            {
                new() { Index = 0, ServiceId = ServiceId },
                new() { Index = 1, ServiceId = ServiceId },
                new() { Index = 2, ServiceId = ServiceId }
            };

            var created = await controller.Create(model);
            workRecordId = (int)((RedirectToActionResult)created).RouteValues!["id"]!;
        }

        await using (var db = CreateContext(connection, firmUser))
        {
            var controller = CreateController(db, firmUser);
            var result = await controller.Submit(workRecordId);
            Assert.IsType<RedirectToActionResult>(result);
        }

        await using (var db = CreateContext(connection, firmUser))
        {
            var record = await db.WorkRecords.Include(w => w.WorkRecordLines).SingleAsync(w => w.WorkRecordId == workRecordId);
            Assert.Equal(3, record.WorkRecordLines.Count);

            // Satır tutarına nakliye bedeli GİRMEZ: her satır sadece 4 saat x 100.
            Assert.All(record.WorkRecordLines, l => Assert.Equal(400m, l.LineAmount));
            Assert.Equal(1200m, record.WorkRecordLines.Sum(l => l.LineAmount));

            // Kayıt toplamına bir kez girer. Eski (hatalı) davranış 3 x 300 = 900 ekleyip
            // 2100 üretiyordu; doğru toplam 1200 + 300 = 1500.
            Assert.Equal(300m, record.MobilizationFee);
            Assert.Equal(1500m, record.TotalAmount);
        }
    }

    // Bedelin tamamen düşmediğini de doğrula: tek satırlı kayıtta da bir kez uygulanır.
    [Fact]
    public async Task Submit_SingleLine_MobilizationFeeStillAppliedOnce()
    {
        await using var connection = await CreateSeededConnectionAsync(mobilizationFee: 300m);
        var firmUser = new FakeCurrentUser { UserId = FirmUserId, FirmId = FirmId };

        int workRecordId;
        await using (var db = CreateContext(connection, firmUser))
        {
            var controller = CreateController(db, firmUser);
            var created = await controller.Create(ValidDraftModel());
            workRecordId = (int)((RedirectToActionResult)created).RouteValues!["id"]!;
            await controller.Submit(workRecordId);
        }

        await using (var db = CreateContext(connection, firmUser))
        {
            var record = await db.WorkRecords.Include(w => w.WorkRecordLines).SingleAsync(w => w.WorkRecordId == workRecordId);
            Assert.Equal(400m, Assert.Single(record.WorkRecordLines).LineAmount);
            Assert.Equal(300m, record.MobilizationFee);
            Assert.Equal(700m, record.TotalAmount);
        }
    }

    // Sözleşme satırında bedel tanımlı değilse kayda hiçbir şey eklenmez.
    [Fact]
    public async Task Submit_NoMobilizationFeeOnContractLine_AddsNothing()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var firmUser = new FakeCurrentUser { UserId = FirmUserId, FirmId = FirmId };

        int workRecordId;
        await using (var db = CreateContext(connection, firmUser))
        {
            var controller = CreateController(db, firmUser);
            var model = ValidDraftModel();
            model.Lines = new List<WorkRecordLineFormViewModel>
            {
                new() { Index = 0, ServiceId = ServiceId },
                new() { Index = 1, ServiceId = ServiceId }
            };
            var created = await controller.Create(model);
            workRecordId = (int)((RedirectToActionResult)created).RouteValues!["id"]!;
            await controller.Submit(workRecordId);
        }

        await using (var db = CreateContext(connection, firmUser))
        {
            var record = await db.WorkRecords.SingleAsync(w => w.WorkRecordId == workRecordId);
            Assert.Equal(0m, record.MobilizationFee);
            Assert.Equal(800m, record.TotalAmount); // 2 x (4 saat x 100)
        }
    }

    [Fact]
    public async Task Submit_NoMatchingContractLine_FailsAndStaysDraft()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var firmUser = new FakeCurrentUser { UserId = FirmUserId, FirmId = FirmId };

        // Sözleşme fiyat satırı olmayan ikinci bir hizmet.
        int workRecordId;
        await using (var db = CreateContext(connection, firmUser))
        {
            db.ServiceCategories.Add(new ServiceCategory { ServiceId = 2, Code = "FIBER", Name = "Fiber", Unit = ServiceUnit.METER, IsActive = true });
            await db.SaveChangesAsync();

            var controller = CreateController(db, firmUser);
            var model = ValidDraftModel(serviceId: 2);
            model.Lines[0].Quantity = 10;
            var result = await controller.Create(model);
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            workRecordId = (int)redirect.RouteValues!["id"]!;
        }

        await using (var db = CreateContext(connection, firmUser))
        {
            var controller = CreateController(db, firmUser);
            var result = await controller.Submit(workRecordId);
            Assert.IsType<RedirectToActionResult>(result);
            Assert.NotNull(controller.TempData[MipRental.Web.Common.TempDataKeys.ErrorMessage]);
        }

        await using (var db = CreateContext(connection, firmUser))
        {
            var record = await db.WorkRecords.SingleAsync(w => w.WorkRecordId == workRecordId);
            Assert.Equal(WorkRecordStatus.DRAFT, record.Status);
            Assert.StartsWith("DRAFT-", record.DocumentNo);
        }
    }

    [Fact]
    public async Task Submit_MissingRequiredField_IsRejected()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var firmUser = new FakeCurrentUser { UserId = FirmUserId, FirmId = FirmId };

        int workRecordId;
        await using (var db = CreateContext(connection, firmUser))
        {
            var controller = CreateController(db, firmUser);
            var model = ValidDraftModel();
            model.OperatorName = null; // zorunlu alan eksik

            var result = await controller.Create(model);
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            workRecordId = (int)redirect.RouteValues!["id"]!;
        }

        await using (var db = CreateContext(connection, firmUser))
        {
            var controller = CreateController(db, firmUser);
            await controller.Submit(workRecordId);
            var error = controller.TempData[MipRental.Web.Common.TempDataKeys.ErrorMessage] as string;
            Assert.Contains("Operatör Adı", error);
        }

        await using (var db = CreateContext(connection, firmUser))
        {
            var record = await db.WorkRecords.SingleAsync(w => w.WorkRecordId == workRecordId);
            Assert.Equal(WorkRecordStatus.DRAFT, record.Status);
        }
    }

    [Fact]
    public async Task Submit_DuplicateDetected_RequiresConfirmation_ThenLogsToAudit()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var firmUser = new FakeCurrentUser { UserId = FirmUserId, FirmId = FirmId };

        // İlk kayıt: normal şekilde gönderilir.
        await using (var db = CreateContext(connection, firmUser))
        {
            var controller = CreateController(db, firmUser);
            var first = await controller.Create(ValidDraftModel());
            var firstId = (int)((RedirectToActionResult)first).RouteValues!["id"]!;
            await controller.Submit(firstId);
        }

        // İkinci kayıt: aynı (FirmId, WorkDate, LicensePlate, StartTime).
        int secondId;
        await using (var db = CreateContext(connection, firmUser))
        {
            var controller = CreateController(db, firmUser);
            var second = await controller.Create(ValidDraftModel());
            secondId = (int)((RedirectToActionResult)second).RouteValues!["id"]!;
        }

        // Onaysız gönderim: engellenmez ama uyarı ekranına düşer, kayıt DRAFT kalır.
        await using (var db = CreateContext(connection, firmUser))
        {
            var controller = CreateController(db, firmUser);
            var result = await controller.Submit(secondId);
            var view = Assert.IsType<ViewResult>(result);
            Assert.Equal("ConfirmDuplicate", view.ViewName);
        }

        await using (var db = CreateContext(connection, firmUser))
        {
            var record = await db.WorkRecords.SingleAsync(w => w.WorkRecordId == secondId);
            Assert.Equal(WorkRecordStatus.DRAFT, record.Status);
        }

        // Onaylı gönderim: geçer ve AuditLog'a düşer.
        await using (var db = CreateContext(connection, firmUser))
        {
            var controller = CreateController(db, firmUser);
            var result = await controller.Submit(secondId, confirmDuplicate: true);
            Assert.IsType<RedirectToActionResult>(result);
        }

        await using (var db = CreateContext(connection, firmUser))
        {
            var record = await db.WorkRecords.SingleAsync(w => w.WorkRecordId == secondId);
            // Gonderim ilk onay adimini da acar: kayit dogrudan PENDING olur (Adim 7).
            Assert.Equal(WorkRecordStatus.PENDING, record.Status);

            var auditEntry = await db.AuditLogs.SingleOrDefaultAsync(a =>
                a.TableName == "WorkRecords" && a.RecordId == secondId && a.FieldName == "DuplicateWarningConfirmed");
            Assert.NotNull(auditEntry);
        }
    }

    [Fact]
    public async Task Create_FirmUser_CannotCreateForAnotherFirm()
    {
        // WorkRecordFormViewModel'de FirmId alanı YOK — model binder'a "tampered"
        // bir FirmId POST edilse bile bağlanacağı bir property olmadığı için hiçbir
        // etkisi olmaz. Sunucu FirmId'yi HER ZAMAN ICurrentUser'dan alır. Bu test,
        // Firma 1 kullanıcısının oluşturduğu kaydın gerçekten Firma 1'e ait
        // kaydedildiğini (Firma 2'ye asla değil) doğrular.
        await using var connection = await CreateSeededConnectionAsync();
        var firmUser = new FakeCurrentUser { UserId = FirmUserId, FirmId = FirmId };

        int workRecordId;
        await using (var db = CreateContext(connection, firmUser))
        {
            var controller = CreateController(db, firmUser);
            var result = await controller.Create(ValidDraftModel());
            workRecordId = (int)((RedirectToActionResult)result).RouteValues!["id"]!;
        }

        await using (var db = CreateContext(connection, firmUser))
        {
            var record = await db.WorkRecords.IgnoreQueryFilters().SingleAsync(w => w.WorkRecordId == workRecordId);
            Assert.Equal(FirmId, record.FirmId);
            Assert.NotEqual(OtherFirmId, record.FirmId);
        }
    }

    [Fact]
    public async Task Submit_AlreadySubmittedRecord_CannotBeSubmittedAgain()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var firmUser = new FakeCurrentUser { UserId = FirmUserId, FirmId = FirmId };

        int workRecordId;
        string firstDocumentNo;
        await using (var db = CreateContext(connection, firmUser))
        {
            var controller = CreateController(db, firmUser);
            var created = await controller.Create(ValidDraftModel());
            workRecordId = (int)((RedirectToActionResult)created).RouteValues!["id"]!;
            await controller.Submit(workRecordId);
        }

        await using (var db = CreateContext(connection, firmUser))
        {
            firstDocumentNo = (await db.WorkRecords.SingleAsync(w => w.WorkRecordId == workRecordId)).DocumentNo;
        }

        // İkinci Submit çağrısı: "geri DRAFT'a dönme" ya da tekrar işlenme yok.
        await using (var db = CreateContext(connection, firmUser))
        {
            var controller = CreateController(db, firmUser);
            var result = await controller.Submit(workRecordId);
            Assert.IsType<RedirectToActionResult>(result);
            Assert.NotNull(controller.TempData[MipRental.Web.Common.TempDataKeys.ErrorMessage]);
        }

        await using (var db = CreateContext(connection, firmUser))
        {
            var record = await db.WorkRecords.SingleAsync(w => w.WorkRecordId == workRecordId);
            // Gonderim ilk onay adimini da acar: kayit dogrudan PENDING olur (Adim 7).
            Assert.Equal(WorkRecordStatus.PENDING, record.Status);
            Assert.Equal(firstDocumentNo, record.DocumentNo); // numara tekrar üretilmedi
        }
    }

    [Fact]
    public async Task Create_WorkDateOutsidePeriodRange_IsRejected()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var firmUser = new FakeCurrentUser { UserId = FirmUserId, FirmId = FirmId };

        await using var db = CreateContext(connection, firmUser);
        var controller = CreateController(db, firmUser);

        var model = ValidDraftModel();
        model.WorkDate = new DateOnly(2025, 3, 19); // Period 2026/Mart, iş tarihi 2025 (gerçek formlardaki hata)

        var result = await controller.Create(model);

        Assert.False(controller.ModelState.IsValid);
        Assert.IsType<ViewResult>(result);
        Assert.Equal(0, await db.WorkRecords.CountAsync());
    }

    // --- IgnoreQueryFilters() bypass'larının firma izolasyonunu bozmadığı (kural 7) ---

    // WorkRecordsController.PopulateOptionsAsync, "talep eden / saha yetkilisi" listesi
    // için User filtresini bypass eder. Bu listeye yalnızca MIP personeli (FirmId = null)
    // girebilmeli — ne başka bir firmanın kullanıcısı, ne de kullanıcının kendi firmasının
    // (MIP personeli olmayan) kullanıcıları.
    [Fact]
    public async Task Create_MipStaffDropdown_ExcludesOtherFirmsUsers()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var firmUser = new FakeCurrentUser { UserId = FirmUserId, FirmId = FirmId };

        await using var db = CreateContext(connection, firmUser);
        var controller = CreateController(db, firmUser);

        var view = Assert.IsType<ViewResult>(await controller.Create());
        var model = Assert.IsType<WorkRecordFormViewModel>(view.Model);

        foreach (var options in new[] { model.RequestedByOptions, model.WitnessedByOptions })
        {
            var name = Assert.Single(options).Text;
            Assert.Equal("MIP Personeli", name);
        }

        var allTexts = model.RequestedByOptions.Concat(model.WitnessedByOptions).Select(o => o.Text).ToList();
        Assert.DoesNotContain("Firma 2 Kullanıcısı", allTexts); // başka firma
        Assert.DoesNotContain("Firma 1 Kullanıcısı", allTexts); // kendi firması ama MIP personeli değil
    }

    // WorkRecordsController.Details, "talep eden / saha yetkilisi" ADINI çözmek için
    // aynı filtreyi bypass eder. Kayıttaki alan (bozuk veri vb. nedeniyle) başka bir
    // firmanın kullanıcısını gösteriyorsa o ad DÖNMEMELİ.
    [Fact]
    public async Task Details_RequestedByPointingAtOtherFirmsUser_NameIsNotLeaked()
    {
        await using var connection = await CreateSeededConnectionAsync();

        // Kaydı MIP bağlamında doğrudan yazıyoruz: firma kullanıcısının UI üzerinden
        // üretemeyeceği bir veri durumunu (başka firmanın kullanıcısına işaret eden alan)
        // kasıtlı olarak kuruyoruz.
        int workRecordId;
        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var record = new WorkRecord
            {
                DocumentNo = "WR-BOZUK-1",
                Status = WorkRecordStatus.DRAFT,
                FirmId = FirmId,
                ContractId = 1,
                PeriodId = PeriodId,
                WorkDate = new DateOnly(2026, 3, 10),
                RequestedByUserId = OtherFirmUserId,
                WitnessedByUserId = MipUserId,
                EnteredByUserId = FirmUserId,
                CreatedAt = DateTime.UtcNow
            };
            db.WorkRecords.Add(record);
            await db.SaveChangesAsync();
            workRecordId = record.WorkRecordId;
        }

        var firmUser = new FakeCurrentUser { UserId = FirmUserId, FirmId = FirmId };
        await using (var db = CreateContext(connection, firmUser))
        {
            var controller = CreateController(db, firmUser);

            var view = Assert.IsType<ViewResult>(await controller.Details(workRecordId));
            var model = Assert.IsType<WorkRecordDetailsViewModel>(view.Model);

            // MIP personelinin adı görünür (bypass'ın meşru amacı)...
            Assert.Equal("MIP Personeli", model.WitnessedByName);
            // ...ama başka firmanın kullanıcısının adı asla.
            Assert.Null(model.RequestedByName);
        }
    }

}
