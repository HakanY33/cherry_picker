using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Interceptors;
using MipRental.Data.Services;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;
using MipRental.Web.Common;
using MipRental.Web.Models.WorkRecords;

namespace MipRental.Tests;

/// <summary>
/// Onay akışının uçtan uca davranışı: iki adımlı zincir, rol kontrolü, satır
/// bazlı itiraz, revizyon, toplu onay, tutar eşiği, kapalı dönem.
///
/// Onay akışı (ApprovalFlows / ApprovalFlowSteps) model seed'i ile geldiği için
/// EnsureCreatedAsync onu da oluşturur — testler kendi akışlarını UYDURMAZ,
/// üretimdeki veriyle çalışır (CLAUDE.md kural 6).
/// </summary>
public class ApprovalFlowTests
{
    private const int FirmId = 1;
    private const int OtherFirmId = 2;

    private const int FirmUserId = 2;
    private const int SupervisorUserId = 3;
    private const int DeptHeadUserId = 4;
    private const int OtherFirmUserId = 5;

    private const int SupervisorRoleId = 2; // RoleConfiguration.HasData
    private const int DeptHeadRoleId = 3;

    private const int ServiceId = 1;   // Mobil Vinç / HOUR (seed)
    private const int PeriodId = 3;    // 2026 / Mart (seed, OPEN)
    private const int MarchLineId = 1;

    private static readonly DateOnly WorkDate = new(2026, 3, 10);

    // ---------------------------------------------------------------
    // Kurulum
    // ---------------------------------------------------------------

    private static DbContextOptions<AppDbContext> SqliteOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new PeriodGuardInterceptor(), new ImmutabilityGuardInterceptor())
            .Options;

    private static AppDbContext Context(SqliteConnection connection, ICurrentUser currentUser) =>
        new SqliteTestContext(SqliteOptions(connection), currentUser);

    private static FakeCurrentUser FirmUser() => new() { UserId = FirmUserId, FirmId = FirmId };
    private static FakeCurrentUser Supervisor() => new() { UserId = SupervisorUserId, FirmId = null };
    private static FakeCurrentUser DeptHead() => new() { UserId = DeptHeadUserId, FirmId = null };

    private static async Task<SqliteConnection> SeedAsync(decimal unitPrice = 100m)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var db = Context(connection, new FakeCurrentUser());
        await db.Database.EnsureCreatedAsync();

        db.Firms.AddRange(
            new Firm { FirmId = FirmId, Code = "FIRMA-1", Title = "Test Vinç", CreatedAt = DateTime.UtcNow },
            new Firm { FirmId = OtherFirmId, Code = "FIRMA-2", Title = "Diğer Firma", CreatedAt = DateTime.UtcNow });

        db.Users.AddRange(
            new User { UserId = FirmUserId, UserName = "testvinc", FullName = "Firma Kullanıcısı", FirmId = FirmId, CreatedAt = DateTime.UtcNow },
            new User { UserId = SupervisorUserId, UserName = "supervisor", FullName = "Saha Amiri", CreatedAt = DateTime.UtcNow },
            new User { UserId = DeptHeadUserId, UserName = "depthead", FullName = "Departman Müdürü", CreatedAt = DateTime.UtcNow },
            new User { UserId = OtherFirmUserId, UserName = "diger", FullName = "Diğer Firma Kullanıcısı", FirmId = OtherFirmId, CreatedAt = DateTime.UtcNow });

        db.UserRoles.AddRange(
            new UserRole { UserId = FirmUserId, RoleId = 6 },              // FIRM_USER
            new UserRole { UserId = SupervisorUserId, RoleId = SupervisorRoleId },
            new UserRole { UserId = DeptHeadUserId, RoleId = DeptHeadRoleId });

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

        // Mart'ta geçerli fiyat satırı. 31 Mart'ta kapanır; Nisan'dan itibaren
        // farklı fiyatlı ikinci satır devreye girer (kural 3 testi için).
        db.ContractLines.Add(new ContractLine
        {
            ContractLineId = MarchLineId,
            ContractId = 1,
            ServiceId = ServiceId,
            UnitPrice = unitPrice,
            Currency = "TRY",
            ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = new DateOnly(2026, 3, 31),
            IsActive = true
        });

        await db.SaveChangesAsync();
        return connection;
    }

    private static WorkRecordFormViewModel DraftModel(int lineCount = 1, string licensePlate = "34ABC34") => new()
    {
        PeriodId = PeriodId,
        WorkDate = WorkDate,
        StartTime = new TimeOnly(8, 0),
        EndTime = new TimeOnly(12, 0),
        LocationText = "Rıhtım 3",
        WorkDescription = "Konteyner indirme",
        RequestedByUserId = SupervisorUserId,
        WitnessedByUserId = SupervisorUserId,
        OperatorName = "Ahmet Yılmaz",
        LicensePlate = licensePlate,
        PersonnelCount = 2,
        ExternalReceiptNo = "0078",
        ExternalReceiptDate = WorkDate,
        Lines = Enumerable.Range(0, lineCount)
            .Select(i => new WorkRecordLineFormViewModel { Index = i, ServiceId = ServiceId })
            .ToList()
    };

    /// <summary>Taslak oluşturup gönderir; kayıt PENDING (1. adım) döner.</summary>
    private static async Task<int> CreateAndSubmitAsync(SqliteConnection connection, string licensePlate = "34ABC34", int lineCount = 1)
    {
        var firmUser = FirmUser();

        int id;
        await using (var db = Context(connection, firmUser))
        {
            var controller = ApprovalTestFactory.CreateWorkRecordsController(db, firmUser);
            var created = await controller.Create(DraftModel(lineCount, licensePlate));
            id = (int)((RedirectToActionResult)created).RouteValues!["id"]!;
        }

        await using (var db = Context(connection, firmUser))
        {
            var controller = ApprovalTestFactory.CreateWorkRecordsController(db, firmUser);
            // Mükerrer uyarısını atlıyoruz: aynı gün/plaka birden çok kayıt üreten
            // testlerde uyarı ekranı akışı kesmesin.
            await controller.Submit(id, confirmDuplicate: true);
        }

        return id;
    }

    private static async Task<WorkRecord> LoadAsync(SqliteConnection connection, int id)
    {
        await using var db = Context(connection, new FakeCurrentUser());
        return await db.WorkRecords.AsNoTracking()
            .Include(w => w.WorkRecordLines)
            .SingleAsync(w => w.WorkRecordId == id);
    }

    // ---------------------------------------------------------------
    // 1) İki adımlı akış
    // ---------------------------------------------------------------

    [Fact]
    public async Task Submit_OpensFirstStepAndSetsPending()
    {
        await using var connection = await SeedAsync();
        var id = await CreateAndSubmitAsync(connection);

        var record = await LoadAsync(connection, id);
        Assert.Equal(WorkRecordStatus.PENDING, record.Status);

        await using var db = Context(connection, new FakeCurrentUser());
        var approval = Assert.Single(await db.Approvals.AsNoTracking()
            .Where(a => a.DocumentId == id).ToListAsync());

        Assert.Equal(1, approval.StepNo);
        Assert.Equal(SupervisorRoleId, approval.AssignedToRoleId);
        Assert.Null(approval.Decision);
    }

    [Fact]
    public async Task TwoStepFlow_StaysPendingAfterFirstApproval_ApprovedAfterSecond()
    {
        await using var connection = await SeedAsync();
        var id = await CreateAndSubmitAsync(connection);

        // 1. adım: Amir onaylar -> kayıt PENDING kalır, 2. adım açılır.
        await using (var db = Context(connection, Supervisor()))
        {
            var controller = ApprovalTestFactory.CreateApprovalsController(db, Supervisor());
            await controller.Approve(id, "amir uygundur");
        }

        var afterFirst = await LoadAsync(connection, id);
        Assert.Equal(WorkRecordStatus.PENDING, afterFirst.Status);
        Assert.Null(afterFirst.ApprovedAt);

        await using (var db = Context(connection, new FakeCurrentUser()))
        {
            var approvals = await db.Approvals.AsNoTracking().Where(a => a.DocumentId == id).OrderBy(a => a.StepNo).ToListAsync();
            Assert.Equal(2, approvals.Count);
            Assert.Equal(ApprovalDecision.APPROVED, approvals[0].Decision);
            Assert.Equal(SupervisorUserId, approvals[0].DecidedByUserId);
            Assert.Null(approvals[1].Decision);
            Assert.Equal(DeptHeadRoleId, approvals[1].AssignedToRoleId);
        }

        // 2. adım: Departman Müdürü onaylar -> APPROVED.
        await using (var db = Context(connection, DeptHead()))
        {
            var controller = ApprovalTestFactory.CreateApprovalsController(db, DeptHead());
            await controller.Approve(id, "müdür uygundur");
        }

        var afterSecond = await LoadAsync(connection, id);
        Assert.Equal(WorkRecordStatus.APPROVED, afterSecond.Status);
        Assert.NotNull(afterSecond.ApprovedAt);
    }

    // ---------------------------------------------------------------
    // 2) Yetki
    // ---------------------------------------------------------------

    [Fact]
    public async Task WrongRole_CannotApproveStep()
    {
        await using var connection = await SeedAsync();
        var id = await CreateAndSubmitAsync(connection);

        // 1. adım SUPERVISOR'a ait; DEPT_HEAD onaylamaya çalışıyor.
        await using (var db = Context(connection, DeptHead()))
        {
            var service = ApprovalTestFactory.CreateApprovalService(db, DeptHead());
            var ex = await Assert.ThrowsAsync<ApprovalAuthorizationException>(() => service.ApproveAsync(id, null));
            Assert.Contains("Amir", ex.Message);
        }

        Assert.Equal(WorkRecordStatus.PENDING, (await LoadAsync(connection, id)).Status);
    }

    [Fact]
    public async Task Subcontractor_CannotApproveOwnRecord()
    {
        await using var connection = await SeedAsync();
        var id = await CreateAndSubmitAsync(connection);

        await using (var db = Context(connection, FirmUser()))
        {
            var service = ApprovalTestFactory.CreateApprovalService(db, FirmUser());
            var ex = await Assert.ThrowsAsync<ApprovalAuthorizationException>(() => service.ApproveAsync(id, null));
            Assert.Contains("alt yüklenici", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(WorkRecordStatus.PENDING, (await LoadAsync(connection, id)).Status);
    }

    [Fact]
    public async Task FirmUser_PendingApprovalQueueIsEmpty()
    {
        await using var connection = await SeedAsync();
        await CreateAndSubmitAsync(connection);

        await using var db = Context(connection, FirmUser());
        var service = ApprovalTestFactory.CreateApprovalService(db, FirmUser());

        Assert.Empty(await service.GetPendingForCurrentUserAsync());
    }

    [Fact]
    public async Task PendingQueue_ShowsOnlyStepsForUsersOwnRole()
    {
        await using var connection = await SeedAsync();
        var id = await CreateAndSubmitAsync(connection);

        // 1. adım SUPERVISOR'da: amir görür, müdür görmez.
        await using (var db = Context(connection, Supervisor()))
        {
            var service = ApprovalTestFactory.CreateApprovalService(db, Supervisor());
            var pending = await service.GetPendingForCurrentUserAsync();
            Assert.Equal(id, Assert.Single(pending).DocumentId);
        }

        await using (var db = Context(connection, DeptHead()))
        {
            var service = ApprovalTestFactory.CreateApprovalService(db, DeptHead());
            Assert.Empty(await service.GetPendingForCurrentUserAsync());
        }
    }

    // ---------------------------------------------------------------
    // 3) Gerekçe zorunluluğu
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reject_WithoutReason_IsRefusedAndRecordStaysPending(string? reason)
    {
        await using var connection = await SeedAsync();
        var id = await CreateAndSubmitAsync(connection);

        await using (var db = Context(connection, Supervisor()))
        {
            var service = ApprovalTestFactory.CreateApprovalService(db, Supervisor());
            await Assert.ThrowsAsync<WorkRecordStateTransitionException>(() => service.RejectAsync(id, reason));
        }

        Assert.Equal(WorkRecordStatus.PENDING, (await LoadAsync(connection, id)).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task RequestRevision_WithoutReason_IsRefused(string? reason)
    {
        await using var connection = await SeedAsync();
        var id = await CreateAndSubmitAsync(connection);

        await using (var db = Context(connection, Supervisor()))
        {
            var service = ApprovalTestFactory.CreateApprovalService(db, Supervisor());
            await Assert.ThrowsAsync<WorkRecordStateTransitionException>(() => service.RequestRevisionAsync(id, reason));
        }

        Assert.Equal(WorkRecordStatus.PENDING, (await LoadAsync(connection, id)).Status);
    }

    [Fact]
    public async Task ObjectToLine_WithoutReason_IsRefused()
    {
        await using var connection = await SeedAsync();
        var id = await CreateAndSubmitAsync(connection);
        var lineId = (await LoadAsync(connection, id)).WorkRecordLines.Single().WorkRecordLineId;

        await using (var db = Context(connection, Supervisor()))
        {
            var service = ApprovalTestFactory.CreateApprovalService(db, Supervisor());
            await Assert.ThrowsAsync<WorkRecordStateTransitionException>(() => service.ObjectToLineAsync(id, lineId, "  "));
        }

        var record = await LoadAsync(connection, id);
        Assert.Equal(WorkRecordStatus.PENDING, record.Status);
        Assert.False(record.WorkRecordLines.Single().IsObjected);
    }

    // ---------------------------------------------------------------
    // 4) Satır bazlı itiraz
    // ---------------------------------------------------------------

    [Fact]
    public async Task ObjectToSingleLine_SetsRecordToRevisionRequested_AndMarksOnlyThatLine()
    {
        await using var connection = await SeedAsync();
        var id = await CreateAndSubmitAsync(connection, lineCount: 3);

        var lines = (await LoadAsync(connection, id)).WorkRecordLines.OrderBy(l => l.LineNo).ToList();
        var targetLine = lines[1]; // ortadaki satır

        await using (var db = Context(connection, Supervisor()))
        {
            var controller = ApprovalTestFactory.CreateApprovalsController(db, Supervisor());
            await controller.ObjectToLine(id, targetLine.WorkRecordLineId, "Bu satırdaki süre saha kaydıyla uyuşmuyor");
        }

        var record = await LoadAsync(connection, id);
        Assert.Equal(WorkRecordStatus.REVISION_REQUESTED, record.Status);

        var objected = record.WorkRecordLines.Where(l => l.IsObjected).ToList();
        var single = Assert.Single(objected);
        Assert.Equal(targetLine.WorkRecordLineId, single.WorkRecordLineId);
        Assert.Equal("Bu satırdaki süre saha kaydıyla uyuşmuyor", single.ObjectionReason);
        Assert.Equal(SupervisorUserId, single.ObjectedByUserId);

        // Diğer iki satır dokunulmadan kalır.
        Assert.Equal(2, record.WorkRecordLines.Count(l => !l.IsObjected));
    }

    [Fact]
    public async Task ObjectToLine_QueuesNotificationForSubcontractor()
    {
        await using var connection = await SeedAsync();
        var id = await CreateAndSubmitAsync(connection);
        var lineId = (await LoadAsync(connection, id)).WorkRecordLines.Single().WorkRecordLineId;

        await using (var db = Context(connection, Supervisor()))
        {
            var controller = ApprovalTestFactory.CreateApprovalsController(db, Supervisor());
            await controller.ObjectToLine(id, lineId, "miktar hatalı");
        }

        await using (var db = Context(connection, new FakeCurrentUser()))
        {
            var notification = await db.Notifications.AsNoTracking()
                .Where(n => n.DocumentId == id && n.TemplateCode == NotificationQueue.Templates.LineObjected)
                .SingleAsync();

            Assert.Equal(FirmUserId, notification.UserId);
            Assert.Equal(NotificationStatus.QUEUED, notification.Status); // gönderilmedi, kuyrukta
            Assert.Contains("miktar hatalı", notification.Body);
        }
    }

    // ---------------------------------------------------------------
    // 5) Revizyon = yeni versiyon
    // ---------------------------------------------------------------

    [Fact]
    public async Task Revise_CreatesNewVersion_AndLeavesOriginalUntouched()
    {
        await using var connection = await SeedAsync();
        var id = await CreateAndSubmitAsync(connection);

        var beforeRevision = await LoadAsync(connection, id);
        var originalDocumentNo = beforeRevision.DocumentNo;
        var originalTotal = beforeRevision.TotalAmount;
        var originalLineAmount = beforeRevision.WorkRecordLines.Single().LineAmount;
        var originalLineCount = beforeRevision.WorkRecordLines.Count;

        await using (var db = Context(connection, Supervisor()))
        {
            var controller = ApprovalTestFactory.CreateApprovalsController(db, Supervisor());
            await controller.RequestRevision(id, "Dış fiş numarası eksik");
        }

        int revisionId;
        await using (var db = Context(connection, FirmUser()))
        {
            var controller = ApprovalTestFactory.CreateWorkRecordsController(db, FirmUser());
            var result = await controller.Revise(id);
            revisionId = (int)((RedirectToActionResult)result).RouteValues!["id"]!;
        }

        Assert.NotEqual(id, revisionId);

        // Yeni kayıt
        var revision = await LoadAsync(connection, revisionId);
        Assert.Equal(WorkRecordStatus.DRAFT, revision.Status);
        Assert.Equal(id, revision.RevisionOfId);
        Assert.False(revision.IsSuperseded);
        Assert.Equal($"{originalDocumentNo}-R2", revision.DocumentNo);
        Assert.Equal(originalLineCount, revision.WorkRecordLines.Count);
        Assert.Null(revision.TotalAmount); // fiyat gönderimde yeniden hesaplanacak
        Assert.Equal("Dış fiş numarası eksik", revision.RevisionReason);

        // ESKİ kayıt: IsSuperseded dışında hiçbir şey değişmedi.
        var original = await LoadAsync(connection, id);
        Assert.True(original.IsSuperseded);
        Assert.Equal(WorkRecordStatus.REVISION_REQUESTED, original.Status);
        Assert.Equal(originalDocumentNo, original.DocumentNo);
        Assert.Equal(originalTotal, original.TotalAmount);
        Assert.Equal(originalLineAmount, original.WorkRecordLines.Single().LineAmount);
        Assert.Equal(originalLineCount, original.WorkRecordLines.Count);
    }

    [Fact]
    public async Task Revise_ResubmittedRevision_KeepsDocumentNumberAndPricesByWorkDate()
    {
        // Mart fiyatı 100. Nisan'dan itibaren aynı hizmetin fiyatı 500 oluyor.
        // Revizyon BUGÜNE göre değil İŞ TARİHİNE (Mart) göre fiyatlanmalı (kural 3).
        await using var connection = await SeedAsync();

        await using (var db = Context(connection, new FakeCurrentUser()))
        {
            db.ContractLines.Add(new ContractLine
            {
                ContractLineId = 2,
                ContractId = 1,
                ServiceId = ServiceId,
                UnitPrice = 500m,
                Currency = "TRY",
                ValidFrom = new DateOnly(2026, 4, 1),
                ValidTo = null,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var id = await CreateAndSubmitAsync(connection);
        var originalDocumentNo = (await LoadAsync(connection, id)).DocumentNo;

        await using (var db = Context(connection, Supervisor()))
        {
            var controller = ApprovalTestFactory.CreateApprovalsController(db, Supervisor());
            await controller.RequestRevision(id, "miktar düzeltilecek");
        }

        int revisionId;
        await using (var db = Context(connection, FirmUser()))
        {
            var controller = ApprovalTestFactory.CreateWorkRecordsController(db, FirmUser());
            revisionId = (int)((RedirectToActionResult)await controller.Revise(id)).RouteValues!["id"]!;
        }

        await using (var db = Context(connection, FirmUser()))
        {
            var controller = ApprovalTestFactory.CreateWorkRecordsController(db, FirmUser());
            await controller.Submit(revisionId, confirmDuplicate: true);
        }

        var resubmitted = await LoadAsync(connection, revisionId);

        Assert.Equal(WorkRecordStatus.PENDING, resubmitted.Status);
        // Belge numarası: yeni seri numarası DEĞİL, sürüm eki.
        Assert.Equal($"{originalDocumentNo}-R2", resubmitted.DocumentNo);
        // Fiyat Mart satırından (100), Nisan satırından (500) değil.
        Assert.Equal(MarchLineId, resubmitted.WorkRecordLines.Single().ContractLineId);
        Assert.Equal(100m, resubmitted.WorkRecordLines.Single().UnitPriceSnapshot);
        Assert.Equal(400m, resubmitted.TotalAmount);
    }

    [Fact]
    public async Task Revise_FullCycle_RevisionCanBeApproved()
    {
        await using var connection = await SeedAsync();
        var id = await CreateAndSubmitAsync(connection);
        var lineId = (await LoadAsync(connection, id)).WorkRecordLines.Single().WorkRecordLineId;

        await using (var db = Context(connection, Supervisor()))
        {
            var controller = ApprovalTestFactory.CreateApprovalsController(db, Supervisor());
            await controller.ObjectToLine(id, lineId, "süre hatalı");
        }

        int revisionId;
        await using (var db = Context(connection, FirmUser()))
        {
            var controller = ApprovalTestFactory.CreateWorkRecordsController(db, FirmUser());
            revisionId = (int)((RedirectToActionResult)await controller.Revise(id)).RouteValues!["id"]!;
        }

        await using (var db = Context(connection, FirmUser()))
        {
            var controller = ApprovalTestFactory.CreateWorkRecordsController(db, FirmUser());
            await controller.Submit(revisionId, confirmDuplicate: true);
        }

        await using (var db = Context(connection, Supervisor()))
        {
            await ApprovalTestFactory.CreateApprovalsController(db, Supervisor()).Approve(revisionId, null);
        }

        await using (var db = Context(connection, DeptHead()))
        {
            await ApprovalTestFactory.CreateApprovalsController(db, DeptHead()).Approve(revisionId, null);
        }

        Assert.Equal(WorkRecordStatus.APPROVED, (await LoadAsync(connection, revisionId)).Status);
        // Eski kayıt hâlâ REVISION_REQUESTED ve superseded.
        var original = await LoadAsync(connection, id);
        Assert.Equal(WorkRecordStatus.REVISION_REQUESTED, original.Status);
        Assert.True(original.IsSuperseded);
    }

    [Fact]
    public async Task Revise_SecondRevisionGetsR3()
    {
        await using var connection = await SeedAsync();
        var id = await CreateAndSubmitAsync(connection);
        var baseDocumentNo = (await LoadAsync(connection, id)).DocumentNo;

        var currentId = id;
        for (var expectedVersion = 2; expectedVersion <= 3; expectedVersion++)
        {
            await using (var db = Context(connection, Supervisor()))
            {
                await ApprovalTestFactory.CreateApprovalsController(db, Supervisor()).RequestRevision(currentId, "tekrar düzeltilecek");
            }

            await using (var db = Context(connection, FirmUser()))
            {
                var controller = ApprovalTestFactory.CreateWorkRecordsController(db, FirmUser());
                currentId = (int)((RedirectToActionResult)await controller.Revise(currentId)).RouteValues!["id"]!;
            }

            Assert.Equal($"{baseDocumentNo}-R{expectedVersion}", (await LoadAsync(connection, currentId)).DocumentNo);

            await using (var db = Context(connection, FirmUser()))
            {
                await ApprovalTestFactory.CreateWorkRecordsController(db, FirmUser()).Submit(currentId, confirmDuplicate: true);
            }
        }
    }

    // ---------------------------------------------------------------
    // 6) APPROVED kayıt değiştirilemez
    // ---------------------------------------------------------------

    [Fact]
    public async Task ApprovedRecord_CannotBeApprovedOrRevisedAgain()
    {
        await using var connection = await SeedAsync();
        var id = await CreateAndSubmitAsync(connection);

        await using (var db = Context(connection, Supervisor()))
        {
            await ApprovalTestFactory.CreateApprovalsController(db, Supervisor()).Approve(id, null);
        }

        await using (var db = Context(connection, DeptHead()))
        {
            await ApprovalTestFactory.CreateApprovalsController(db, DeptHead()).Approve(id, null);
        }

        Assert.Equal(WorkRecordStatus.APPROVED, (await LoadAsync(connection, id)).Status);

        // Tekrar onay: açık adım kalmadı.
        await using (var db = Context(connection, DeptHead()))
        {
            var service = ApprovalTestFactory.CreateApprovalService(db, DeptHead());
            await Assert.ThrowsAsync<WorkRecordStateTransitionException>(() => service.ApproveAsync(id, null));
        }

        // Revizyon: APPROVED terminal.
        await using (var db = Context(connection, FirmUser()))
        {
            var service = new WorkRecordRevisionService(db, FirmUser());
            await Assert.ThrowsAsync<WorkRecordStateTransitionException>(() => service.CreateRevisionAsync(id));
        }

        var final = await LoadAsync(connection, id);
        Assert.Equal(WorkRecordStatus.APPROVED, final.Status);
        Assert.False(final.IsSuperseded);
    }

    [Fact]
    public async Task ApprovedRecord_CannotBeMutatedThroughDbContext()
    {
        await using var connection = await SeedAsync();
        var id = await CreateAndSubmitAsync(connection);

        await using (var db = Context(connection, Supervisor()))
        {
            await ApprovalTestFactory.CreateApprovalsController(db, Supervisor()).Approve(id, null);
        }

        await using (var db = Context(connection, DeptHead()))
        {
            await ApprovalTestFactory.CreateApprovalsController(db, DeptHead()).Approve(id, null);
        }

        // Durum makinesini tamamen atlayıp doğrudan alan değiştirmek de engellenir
        // (ImmutabilityGuardInterceptor, SaveChanges seviyesinde).
        await using (var db = Context(connection, new FakeCurrentUser()))
        {
            var record = await db.WorkRecords.SingleAsync(w => w.WorkRecordId == id);
            record.TotalAmount = 999999m;

            await Assert.ThrowsAsync<ImmutabilityViolationException>(() => db.SaveChangesAsync());
        }

        Assert.Equal(400m, (await LoadAsync(connection, id)).TotalAmount);
    }

    // ---------------------------------------------------------------
    // 7) Kapalı dönem
    // ---------------------------------------------------------------

    [Fact]
    public async Task ClosedPeriod_ApprovalIsRefused()
    {
        await using var connection = await SeedAsync();
        var id = await CreateAndSubmitAsync(connection);

        await using (var db = Context(connection, new FakeCurrentUser()))
        {
            var period = await db.Periods.SingleAsync(p => p.PeriodId == PeriodId);
            period.Status = PeriodStatus.CLOSED;
            await db.SaveChangesAsync();
        }

        await using (var db = Context(connection, Supervisor()))
        {
            var service = ApprovalTestFactory.CreateApprovalService(db, Supervisor());
            var ex = await Assert.ThrowsAsync<WorkRecordStateTransitionException>(() => service.ApproveAsync(id, null));
            Assert.Contains("kapalıdır", ex.Message);
        }

        Assert.Equal(WorkRecordStatus.PENDING, (await LoadAsync(connection, id)).Status);
    }

    // ---------------------------------------------------------------
    // 8) AmountThreshold
    // ---------------------------------------------------------------

    private static async Task SetSecondStepThresholdAsync(SqliteConnection connection, decimal threshold)
    {
        await using var db = Context(connection, new FakeCurrentUser());
        var step = await db.ApprovalFlowSteps.SingleAsync(s => s.FlowId == 1 && s.StepNo == 2);
        step.AmountThreshold = threshold;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AmountThreshold_StepBelowThresholdIsSkipped_SingleApprovalCompletesRecord()
    {
        await using var connection = await SeedAsync(); // 4 saat x 100 = 400
        await SetSecondStepThresholdAsync(connection, 10_000m);

        var id = await CreateAndSubmitAsync(connection);
        Assert.Equal(400m, (await LoadAsync(connection, id)).TotalAmount);

        // 400 < 10.000 -> 2. adım devreye girmez; tek onay kaydı bitirir.
        await using (var db = Context(connection, Supervisor()))
        {
            await ApprovalTestFactory.CreateApprovalsController(db, Supervisor()).Approve(id, null);
        }

        Assert.Equal(WorkRecordStatus.APPROVED, (await LoadAsync(connection, id)).Status);

        await using (var db = Context(connection, new FakeCurrentUser()))
        {
            var approvals = await db.Approvals.AsNoTracking().Where(a => a.DocumentId == id).ToListAsync();
            Assert.Single(approvals); // 2. adım hiç açılmadı
        }
    }

    [Fact]
    public async Task AmountThreshold_StepAboveThresholdApplies_SecondApprovalRequired()
    {
        await using var connection = await SeedAsync(unitPrice: 5_000m); // 4 saat x 5.000 = 20.000
        await SetSecondStepThresholdAsync(connection, 10_000m);

        var id = await CreateAndSubmitAsync(connection);
        Assert.Equal(20_000m, (await LoadAsync(connection, id)).TotalAmount);

        await using (var db = Context(connection, Supervisor()))
        {
            await ApprovalTestFactory.CreateApprovalsController(db, Supervisor()).Approve(id, null);
        }

        // 20.000 > 10.000 -> 2. adım devrede, kayıt hâlâ bekliyor.
        Assert.Equal(WorkRecordStatus.PENDING, (await LoadAsync(connection, id)).Status);

        await using (var db = Context(connection, DeptHead()))
        {
            await ApprovalTestFactory.CreateApprovalsController(db, DeptHead()).Approve(id, null);
        }

        Assert.Equal(WorkRecordStatus.APPROVED, (await LoadAsync(connection, id)).Status);
    }

    // ---------------------------------------------------------------
    // 9) Toplu onay — kısmi başarı
    // ---------------------------------------------------------------

    [Fact]
    public async Task BulkApprove_OneFailure_DoesNotAffectTheOthers()
    {
        await using var connection = await SeedAsync();

        var first = await CreateAndSubmitAsync(connection, licensePlate: "34AAA01");
        var second = await CreateAndSubmitAsync(connection, licensePlate: "34AAA02");
        var alreadyDecided = await CreateAndSubmitAsync(connection, licensePlate: "34AAA03");

        // Üçüncü kayıt araya giren başka bir kararla tamamen onaylanıyor:
        // toplu onay listesi ekrandayken durumun değişmesi gerçekçi senaryo.
        await using (var db = Context(connection, Supervisor()))
        {
            await ApprovalTestFactory.CreateApprovalsController(db, Supervisor()).Approve(alreadyDecided, null);
        }

        await using (var db = Context(connection, DeptHead()))
        {
            await ApprovalTestFactory.CreateApprovalsController(db, DeptHead()).Approve(alreadyDecided, null);
        }

        // Toplu onay: üçü birden. Üçüncüsü hata verecek.
        await using (var db = Context(connection, Supervisor()))
        {
            var controller = ApprovalTestFactory.CreateApprovalsController(db, Supervisor());
            await controller.BulkApprove(new[] { first, second, alreadyDecided }, "toplu onay");

            Assert.NotNull(controller.TempData[TempDataKeys.SuccessMessage]);
            var error = Assert.IsType<string>(controller.TempData[TempDataKeys.ErrorMessage]);
            Assert.Contains("diğerleri etkilenmedi", error);
        }

        // İlk ikisi 1. adımı geçti, 2. adımı bekliyor — hata onları etkilemedi.
        foreach (var id in new[] { first, second })
        {
            Assert.Equal(WorkRecordStatus.PENDING, (await LoadAsync(connection, id)).Status);

            await using var db = Context(connection, new FakeCurrentUser());
            var approvals = await db.Approvals.AsNoTracking().Where(a => a.DocumentId == id).OrderBy(a => a.StepNo).ToListAsync();
            Assert.Equal(2, approvals.Count);
            Assert.Equal(ApprovalDecision.APPROVED, approvals[0].Decision);
            Assert.Null(approvals[1].Decision);
        }

        Assert.Equal(WorkRecordStatus.APPROVED, (await LoadAsync(connection, alreadyDecided)).Status);
    }

    [Fact]
    public async Task BulkApprove_AllValid_ApprovesEachOne()
    {
        await using var connection = await SeedAsync();
        var first = await CreateAndSubmitAsync(connection, licensePlate: "34BBB01");
        var second = await CreateAndSubmitAsync(connection, licensePlate: "34BBB02");

        await using (var db = Context(connection, Supervisor()))
        {
            var controller = ApprovalTestFactory.CreateApprovalsController(db, Supervisor());
            await controller.BulkApprove(new[] { first, second }, null);
            Assert.Null(controller.TempData[TempDataKeys.ErrorMessage]);
        }

        await using (var db = Context(connection, DeptHead()))
        {
            var controller = ApprovalTestFactory.CreateApprovalsController(db, DeptHead());
            await controller.BulkApprove(new[] { first, second }, null);
            Assert.Null(controller.TempData[TempDataKeys.ErrorMessage]);
        }

        Assert.Equal(WorkRecordStatus.APPROVED, (await LoadAsync(connection, first)).Status);
        Assert.Equal(WorkRecordStatus.APPROVED, (await LoadAsync(connection, second)).Status);
    }

    // ---------------------------------------------------------------
    // 10) Bildirimler kuyruğa yazılır (gönderilmez)
    // ---------------------------------------------------------------

    [Fact]
    public async Task Submit_QueuesNotificationForApproverRoleOnly()
    {
        await using var connection = await SeedAsync();
        var id = await CreateAndSubmitAsync(connection);

        await using var db = Context(connection, new FakeCurrentUser());
        var notifications = await db.Notifications.AsNoTracking()
            .Where(n => n.DocumentId == id && n.TemplateCode == NotificationQueue.Templates.ApprovalPending)
            .ToListAsync();

        // 1. adım SUPERVISOR: sadece amire düşer, müdüre değil.
        var notification = Assert.Single(notifications);
        Assert.Equal(SupervisorUserId, notification.UserId);
        Assert.Equal(NotificationStatus.QUEUED, notification.Status);
        Assert.Null(notification.SentAt);
    }

    [Fact]
    public async Task Rejection_QueuesNotificationForSubcontractorWithReason()
    {
        await using var connection = await SeedAsync();
        var id = await CreateAndSubmitAsync(connection);

        await using (var db = Context(connection, Supervisor()))
        {
            await ApprovalTestFactory.CreateApprovalsController(db, Supervisor()).Reject(id, "Dış fiş numarası yanlış");
        }

        Assert.Equal(WorkRecordStatus.REJECTED, (await LoadAsync(connection, id)).Status);

        await using var readDb = Context(connection, new FakeCurrentUser());
        var notification = await readDb.Notifications.AsNoTracking()
            .SingleAsync(n => n.DocumentId == id && n.TemplateCode == NotificationQueue.Templates.Rejected);

        Assert.Equal(FirmUserId, notification.UserId);
        Assert.Contains("Dış fiş numarası yanlış", notification.Body);
    }
}
