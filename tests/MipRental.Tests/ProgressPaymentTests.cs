using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MipRental.Data;
using MipRental.Data.Interceptors;
using MipRental.Data.Reporting;
using MipRental.Data.Services;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;
using MipRental.Web.Security;

namespace MipRental.Tests;

/// <summary>
/// ADIM 14 BÖLÜM A — HAKEDİŞ.
///
/// Testlerin duruşu diğer adımlarla aynı: "servis hata verdi" yetmez,
/// VERİTABANINA bakılır — ikinci hakediş yazıldı mı, dondurulmuş liste büyüdü
/// mü, kayıt gerçekten değiştirilemiyor mu.
/// </summary>
public partial class ProgressPaymentTests
{
    private const int FirmId = 1;
    private const int OtherFirmId = 2;
    private const int ServiceId = 1;   // Mobil Vinç / HOUR — model seed'i
    private const int PeriodId = 3;    // 2026 / Mart, OPEN — model seed'i

    private const int BudgetUserId = 10;
    private const int BudgetManagerUserId = 11;
    private const int FirmUserId = 12;

    // RoleConfiguration seed'i: 3 = BUDGET_MANAGER, 4 = BUDGET, 6 = FIRM_USER.
    private const int BudgetManagerRoleId = 3;
    private const int BudgetRoleId = 4;
    private const int FirmUserRoleId = 6;

    private static AppDbContext CreateContext(SqliteConnection connection, ICurrentUser user) =>
        new SqliteTestContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new PeriodGuardInterceptor(), new ImmutabilityGuardInterceptor())
                .Options,
            user);

    private static FakeCurrentUser Budget() =>
        new() { UserId = BudgetUserId, FullName = "Bütçe", Roles = { RoleNames.Budget } };

    private static FakeCurrentUser BudgetManager() =>
        new() { UserId = BudgetManagerUserId, FullName = "Bütçe Yöneticisi", Roles = { RoleNames.BudgetManager } };

    private static FakeCurrentUser FirmUser() =>
        new() { UserId = FirmUserId, FirmId = FirmId, Roles = { RoleNames.FirmManager } };

    private static ProgressPaymentService CreateService(AppDbContext db, ICurrentUser user) =>
        new(db, new MonthlySummaryService(db, user), ApprovalTestFactory.CreateApprovalService(db, user),
            new ApprovalTokenService(db), new NotificationQueue(db));

    /// <summary>Testlerde mail bağlantısı: gerçek adres yerine sabit bir kalıp.</summary>
    internal static string ApprovalUrl(string rawToken) => $"https://mip.test/Onay/{rawToken}";

    private static async Task<SqliteConnection> CreateSeededConnectionAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var db = CreateContext(connection, new FakeCurrentUser());
        await db.Database.EnsureCreatedAsync();

        db.Firms.AddRange(
            new Firm { FirmId = FirmId, Code = "FIRMA-1", Title = "Firma 1", CreatedAt = DateTime.UtcNow },
            new Firm { FirmId = OtherFirmId, Code = "FIRMA-2", Title = "Firma 2", CreatedAt = DateTime.UtcNow });
        db.Users.AddRange(
            new User { UserId = BudgetUserId, UserName = "butce", FullName = "Bütçe", CreatedAt = DateTime.UtcNow },
            new User { UserId = BudgetManagerUserId, UserName = "butce.yonetici", FullName = "Bütçe Yöneticisi", CreatedAt = DateTime.UtcNow },
            new User { UserId = FirmUserId, UserName = "firma1", FullName = "Firma Kullanıcısı", FirmId = FirmId, CreatedAt = DateTime.UtcNow });

        // Aktörün rolleri veritabanından okunur (ApprovalService.GetActorAsync).
        db.UserRoles.AddRange(
            new UserRole { UserId = BudgetUserId, RoleId = BudgetRoleId },
            new UserRole { UserId = BudgetManagerUserId, RoleId = BudgetManagerRoleId },
            new UserRole { UserId = FirmUserId, RoleId = FirmUserRoleId });

        foreach (var (contractId, firmId) in new[] { (1, FirmId), (2, OtherFirmId) })
        {
            db.Contracts.Add(new Contract
            {
                ContractId = contractId,
                FirmId = firmId,
                ContractNo = $"SOZ-{contractId}",
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2026, 12, 31),
                Currency = "TRY",
                Status = ContractStatus.ACTIVE,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        return connection;
    }

    /// <summary>Tek satırlı çalışma kaydı; onaylı kayıtlar tek INSERT'te yazılır.</summary>
    private static async Task<int> AddRecordAsync(
        SqliteConnection connection, int workRecordId, WorkRecordStatus status, decimal lineAmount,
        int firmId = FirmId, int contractId = 1)
    {
        await using var db = CreateContext(connection, new FakeCurrentUser());

        var record = new WorkRecord
        {
            WorkRecordId = workRecordId,
            DocumentNo = $"WR-2026-{workRecordId:00000}",
            Status = status,
            FirmId = firmId,
            ContractId = contractId,
            PeriodId = PeriodId,
            WorkDate = new DateOnly(2026, 3, 10),
            EnteredByUserId = FirmUserId,
            TotalAmount = lineAmount,
            Currency = "TRY",
            CreatedAt = DateTime.UtcNow
        };

        record.WorkRecordLines.Add(new WorkRecordLine
        {
            LineNo = 1,
            ServiceId = ServiceId,
            RawQuantity = 4m,
            BillableQuantity = 4m,
            Unit = ServiceUnit.HOUR,
            UnitPriceSnapshot = lineAmount / 4m,
            LineAmount = lineAmount,
            Currency = "TRY"
        });

        db.WorkRecords.Add(record);
        await db.SaveChangesAsync();
        return workRecordId;
    }

    private static async Task<ProgressPayment> CreatePaymentAsync(SqliteConnection connection, int firmId = FirmId)
    {
        var budget = Budget();
        await using var db = CreateContext(connection, budget);
        return await CreateService(db, budget).CreateAsync(PeriodId, firmId);
    }

    // ---------------------------------------------------------------
    // 1) Aynı dönem + firma için İKİNCİ hakediş yok
    // ---------------------------------------------------------------

    /// <summary>
    /// Garanti veritabanında (UQ_ProgressPayments_Period_Firm): ikinci çağrı
    /// hata alır ve tabloda tek satır kalır.
    /// </summary>
    [Fact]
    public async Task Create_SecondPaymentForSamePeriodAndFirm_IsRejected()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 1000m);

        await CreatePaymentAsync(connection);

        var ex = await Assert.ThrowsAsync<ProgressPaymentStateTransitionException>(
            () => CreatePaymentAsync(connection));
        Assert.Contains("zaten oluşturulmuş", ex.Message);

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        Assert.Equal(1, await verify.ProgressPayments.CountAsync());
    }

    // ---------------------------------------------------------------
    // 2) Hakedişe yalnızca APPROVED ve LOCKED girer
    // ---------------------------------------------------------------

    [Fact]
    public async Task Create_IncludesOnlyApprovedAndLockedRecords()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 1000m);
        await AddRecordAsync(connection, 2, WorkRecordStatus.LOCKED, 500m);
        await AddRecordAsync(connection, 3, WorkRecordStatus.PENDING, 999m);
        await AddRecordAsync(connection, 4, WorkRecordStatus.DRAFT, 888m);
        await AddRecordAsync(connection, 5, WorkRecordStatus.REJECTED, 777m);

        var payment = await CreatePaymentAsync(connection);

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        var includedIds = await verify.ProgressPaymentRecords.AsNoTracking()
            .Where(r => r.ProgressPaymentId == payment.ProgressPaymentId)
            .Select(r => r.WorkRecordId)
            .OrderBy(id => id)
            .ToListAsync();

        Assert.Equal(new[] { 1, 2 }, includedIds);
        Assert.Equal(1500m, payment.TotalAmount);
        Assert.Equal(2, payment.RecordCount);

        // Onay bekleyen kayıtlar hakedişe girmedi ama SAYILDI: uyarı buradan çıkar.
        Assert.Equal(2, payment.PendingRecordCountAtCreation);   // PENDING + DRAFT
    }

    // ---------------------------------------------------------------
    // 3) Hakediş DONDURULMUŞTUR
    // ---------------------------------------------------------------

    /// <summary>
    /// Hakediş oluştuktan sonra aynı dönemde yeni bir kayıt onaylanırsa icmal
    /// büyür, hakediş büyümez. Aksi halde "onaylanan tutar" ile "ödenen tutar"
    /// ayrışırdı — kaydın anlık görüntü olmasının bütün sebebi bu.
    /// </summary>
    [Fact]
    public async Task Create_ThenNewlyApprovedRecord_DoesNotChangePayment()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 1000m);

        var payment = await CreatePaymentAsync(connection);

        // Hakedişten SONRA onaylanan kayıt.
        await AddRecordAsync(connection, 2, WorkRecordStatus.APPROVED, 2500m);

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        var stored = await verify.ProgressPayments.AsNoTracking()
            .SingleAsync(p => p.ProgressPaymentId == payment.ProgressPaymentId);
        var includedIds = await verify.ProgressPaymentRecords.AsNoTracking()
            .Where(r => r.ProgressPaymentId == payment.ProgressPaymentId)
            .Select(r => r.WorkRecordId)
            .ToListAsync();

        Assert.Equal(1000m, stored.TotalAmount);
        Assert.Equal(1, stored.RecordCount);
        Assert.Equal(new[] { 1 }, includedIds);

        // İcmal ise gerçekten büyümüş: dondurulan şey hakediş, icmal değil.
        var budget = Budget();
        await using var summaryDb = CreateContext(connection, budget);
        var summary = await new MonthlySummaryService(summaryDb, budget).BuildAsync(PeriodId, FirmId);
        Assert.Equal(3500m, summary.GrandTotal);
    }

    // ---------------------------------------------------------------
    // 4) Hakedişe dahil kayıt değiştirilemez / revize edilemez
    // ---------------------------------------------------------------

    [Fact]
    public async Task ApprovedPayment_BlocksRevisionOfIncludedRecord()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 1000m);
        var payment = await CreatePaymentAsync(connection);
        await ApproveThroughManagerAsync(connection, payment.ProgressPaymentId);

        // Revizyon: selefi UPDATE etmeden yeni bir satır olarak doğar.
        await using var db = CreateContext(connection, FirmUser());
        db.WorkRecords.Add(new WorkRecord
        {
            WorkRecordId = 99,
            DocumentNo = "WR-2026-00001-R2",
            Status = WorkRecordStatus.DRAFT,
            RevisionOfId = 1,
            FirmId = FirmId,
            ContractId = 1,
            PeriodId = PeriodId,
            WorkDate = new DateOnly(2026, 3, 10),
            EnteredByUserId = FirmUserId,
            TotalAmount = 1m,
            Currency = "TRY",
            CreatedAt = DateTime.UtcNow
        });

        var ex = await Assert.ThrowsAsync<ImmutabilityViolationException>(() => db.SaveChangesAsync());
        Assert.Equal("Bu kayıt Mart 2026 hakedişine dahil edilmiştir, değiştirilemez.", ex.Message);
    }

    /// <summary>Doğrudan UPDATE de aynı mesajla düşer.</summary>
    [Fact]
    public async Task ApprovedPayment_BlocksUpdateOfIncludedRecord()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 1000m);
        var payment = await CreatePaymentAsync(connection);
        await ApproveThroughManagerAsync(connection, payment.ProgressPaymentId);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var record = await db.WorkRecords.SingleAsync(w => w.WorkRecordId == 1);
        record.TotalAmount = 5m;

        var ex = await Assert.ThrowsAsync<ImmutabilityViolationException>(() => db.SaveChangesAsync());
        Assert.Contains("hakedişine dahil edilmiştir", ex.Message);
    }

    // ---------------------------------------------------------------
    // 5) Gerekçesiz red reddedilir
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reject_WithoutReason_IsRejected_AndPaymentStaysPending(string? reason)
    {
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 1000m);
        var payment = await CreatePaymentAsync(connection);
        await SendToManagerAsync(connection, payment.ProgressPaymentId);

        var manager = BudgetManager();
        await using (var db = CreateContext(connection, manager))
        {
            var tracked = await db.ProgressPayments.SingleAsync(p => p.ProgressPaymentId == payment.ProgressPaymentId);
            await Assert.ThrowsAsync<ProgressPaymentStateTransitionException>(
                () => CreateService(db, manager).RejectAsync(tracked, reason));
        }

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        Assert.Equal(ProgressPaymentStatus.PENDING_BUDGET_MANAGER,
            (await verify.ProgressPayments.AsNoTracking().SingleAsync()).Status);
    }

    // ---------------------------------------------------------------
    // 6) BUDGET olmayan kullanıcı hakediş oluşturamaz
    // ---------------------------------------------------------------

    /// <summary>
    /// Policy ekranın kapısını tutar; bu test servisin kendisinin de tuttuğunu
    /// gösterir. Bütçe Yöneticisi hakedişi ONAYLAR, kurmaz.
    /// </summary>
    [Fact]
    public async Task Create_ByNonBudgetUser_IsRejected()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 1000m);

        var manager = BudgetManager();
        await using (var db = CreateContext(connection, manager))
        {
            await Assert.ThrowsAsync<ApprovalAuthorizationException>(
                () => CreateService(db, manager).CreateAsync(PeriodId, FirmId));
        }

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        Assert.False(await verify.ProgressPayments.AnyAsync());
    }

    // ---------------------------------------------------------------
    // 7) Firma kullanıcısı hakediş ekranını göremez
    // ---------------------------------------------------------------

    /// <summary>
    /// İki katman: (a) policy firma kullanıcısını hiç geçirmez, (b) geçse bile
    /// firma izolasyon filtresi hakediş satırını döndürmez.
    /// </summary>
    [Fact]
    public async Task FirmUser_CannotSeeProgressPayments()
    {
        var authorization = new ServiceCollection()
            .AddLogging()
            .AddAuthorization(AuthorizationPolicies.AddAppPolicies)
            .BuildServiceProvider()
            .GetRequiredService<IAuthorizationService>();

        var firmPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, RoleNames.FirmManager), new Claim(AppClaimTypes.FirmId, FirmId.ToString()) },
            "Test", ClaimTypes.Name, ClaimTypes.Role));

        Assert.False((await authorization.AuthorizeAsync(firmPrincipal, null, PolicyNames.CanViewProgressPayments)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(firmPrincipal, null, PolicyNames.CanManageProgressPayment)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(firmPrincipal, null, PolicyNames.CanApproveProgressPayment)).Succeeded);

        // Veri katmanı: firma kullanıcısı BAŞKA firmanın hakedişini hiç görmez.
        // Kendi firmasınınkini görebilmesi kural 7'ye aykırı değil — ekranı kapatan
        // yukarıdaki policy'dir, filtre ikinci katmandır.
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 1000m, OtherFirmId, contractId: 2);
        await CreatePaymentAsync(connection, OtherFirmId);

        await using var firmDb = CreateContext(connection, FirmUser());
        Assert.Empty(await firmDb.ProgressPayments.AsNoTracking().ToListAsync());
    }

    // ---------------------------------------------------------------

    private static async Task SendToManagerAsync(SqliteConnection connection, int paymentId, string? note = null)
    {
        var budget = Budget();
        await using var db = CreateContext(connection, budget);
        var payment = await db.ProgressPayments.SingleAsync(p => p.ProgressPaymentId == paymentId);
        await CreateService(db, budget).SendToManagerAsync(payment, note, ApprovalUrl);
        await db.SaveChangesAsync();
    }

    private static async Task ApproveThroughManagerAsync(SqliteConnection connection, int paymentId)
    {
        await SendToManagerAsync(connection, paymentId);

        var manager = BudgetManager();
        await using var db = CreateContext(connection, manager);
        var payment = await db.ProgressPayments.SingleAsync(p => p.ProgressPaymentId == paymentId);
        await CreateService(db, manager).ApproveAsync(payment, note: null);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Kilit hâlâ çalışır: hakediş onaylandıktan sonra dönem kapatılabilir ve
    /// kayıtlar APPROVED -> LOCKED olur. Hakediş kaydı dondurur, dönem kapanışını
    /// engellemez — ikisi ters sırada da yapılabilmeli.
    /// </summary>
    [Fact]
    public async Task ApprovedPayment_StillAllowsPeriodLockTransition()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 1000m);
        var payment = await CreatePaymentAsync(connection);
        await ApproveThroughManagerAsync(connection, payment.ProgressPaymentId);

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var record = await db.WorkRecords.SingleAsync(w => w.WorkRecordId == 1);
            record.Status = WorkRecordStatus.LOCKED;
            await db.SaveChangesAsync();
        }

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        Assert.Equal(WorkRecordStatus.LOCKED,
            (await verify.WorkRecords.AsNoTracking().SingleAsync(w => w.WorkRecordId == 1)).Status);
    }
}
