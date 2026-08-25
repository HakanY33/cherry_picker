using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Interceptors;
using MipRental.Data.Reporting;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Tests;

/// <summary>
/// Aylık icmalin içeriği ve toplamları.
/// </summary>
public class MonthlySummaryTests
{
    private const int FirmId = 1;
    private const int OtherFirmId = 2;
    private const int ServiceId = 1;   // Mobil Vinç / HOUR — model seed'inden gelir
    private const int PeriodId = 3;    // 2026 / Mart, OPEN — model seed'inden gelir

    private static DbContextOptions<AppDbContext> SqliteOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new PeriodGuardInterceptor(), new ImmutabilityGuardInterceptor())
            .Options;

    private static AppDbContext CreateContext(SqliteConnection connection, ICurrentUser currentUser) =>
        new SqliteTestContext(SqliteOptions(connection), currentUser);

    private static async Task<SqliteConnection> CreateSeededConnectionAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var db = CreateContext(connection, new FakeCurrentUser());
        await db.Database.EnsureCreatedAsync();

        db.Firms.Add(new Firm { FirmId = FirmId, Code = "FIRMA-1", Title = "Firma 1", CreatedAt = DateTime.UtcNow });
        db.Firms.Add(new Firm { FirmId = OtherFirmId, Code = "FIRMA-2", Title = "Firma 2", CreatedAt = DateTime.UtcNow });
        db.Users.Add(new User { UserId = 1, UserName = "mip", FullName = "MIP Personeli", CreatedAt = DateTime.UtcNow });
        db.Users.Add(new User { UserId = 2, UserName = "firma1", FullName = "Firma 1 Kullanıcısı", FirmId = FirmId, CreatedAt = DateTime.UtcNow });
        db.Users.Add(new User { UserId = 3, UserName = "firma2", FullName = "Firma 2 Kullanıcısı", FirmId = OtherFirmId, CreatedAt = DateTime.UtcNow });

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

    /// <summary>
    /// Belirtilen durumda, tek satırlı bir çalışma kaydı ekler.
    /// Onaylı/kilitli kayıtlar ImmutabilityGuard'a takılmasın diye tek INSERT'te yazılır.
    /// </summary>
    private static async Task<int> AddRecordAsync(
        SqliteConnection connection,
        int workRecordId,
        WorkRecordStatus status,
        decimal lineAmount,
        decimal? mobilizationFee = null,
        int firmId = FirmId,
        int contractId = 1,
        bool isSuperseded = false)
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
            EnteredByUserId = 2,
            MobilizationFee = mobilizationFee,
            TotalAmount = lineAmount + (mobilizationFee ?? 0m),
            Currency = "TRY",
            IsSuperseded = isSuperseded,
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

    private static MonthlySummaryService CreateService(SqliteConnection connection, ICurrentUser user) =>
        new(CreateContext(connection, user), user);

    // ---------------------------------------------------------------
    // 1) İcmale yalnızca APPROVED ve LOCKED girer
    // ---------------------------------------------------------------

    [Fact]
    public async Task Summary_IncludesOnlyApprovedAndLockedRecords()
    {
        await using var connection = await CreateSeededConnectionAsync();

        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 400m);
        await AddRecordAsync(connection, 2, WorkRecordStatus.LOCKED, 800m);
        await AddRecordAsync(connection, 3, WorkRecordStatus.DRAFT, 1000m);
        await AddRecordAsync(connection, 4, WorkRecordStatus.PENDING, 1000m);
        await AddRecordAsync(connection, 5, WorkRecordStatus.REJECTED, 1000m);
        await AddRecordAsync(connection, 6, WorkRecordStatus.CANCELLED, 1000m);
        await AddRecordAsync(connection, 7, WorkRecordStatus.SUBMITTED, 1000m);
        await AddRecordAsync(connection, 8, WorkRecordStatus.REVISION_REQUESTED, 1000m);

        var mipUser = new FakeCurrentUser { UserId = 1 };
        var summary = await CreateService(connection, mipUser).BuildAsync(PeriodId, FirmId);

        Assert.Equal(2, summary.RecordCount);
        Assert.Equal(1200m, summary.LinesTotal);

        var statuses = summary.ServiceGroups.SelectMany(g => g.Lines).Select(l => l.Status).Distinct().ToList();
        Assert.All(statuses, s => Assert.Contains(s, new[] { WorkRecordStatus.APPROVED, WorkRecordStatus.LOCKED }));
    }

    [Fact]
    public async Task Summary_CountsPendingRecordsButExcludesThemFromTotals()
    {
        await using var connection = await CreateSeededConnectionAsync();

        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 400m);
        await AddRecordAsync(connection, 2, WorkRecordStatus.PENDING, 999m);
        await AddRecordAsync(connection, 3, WorkRecordStatus.SUBMITTED, 999m);
        await AddRecordAsync(connection, 4, WorkRecordStatus.DRAFT, 999m);

        var summary = await CreateService(connection, new FakeCurrentUser { UserId = 1 }).BuildAsync(PeriodId, FirmId);

        Assert.Equal(3, summary.PendingRecordCount);
        Assert.Equal(400m, summary.GrandTotal);
    }

    [Fact]
    public async Task Summary_ExcludesSupersededRecords()
    {
        await using var connection = await CreateSeededConnectionAsync();

        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 400m);
        await AddRecordAsync(connection, 2, WorkRecordStatus.APPROVED, 400m, isSuperseded: true);

        var summary = await CreateService(connection, new FakeCurrentUser { UserId = 1 }).BuildAsync(PeriodId, FirmId);

        Assert.Equal(1, summary.RecordCount);
        Assert.Equal(400m, summary.LinesTotal);
    }

    // ---------------------------------------------------------------
    // 2) Firma izolasyonu (CLAUDE.md kural 7)
    // ---------------------------------------------------------------

    [Fact]
    public async Task FirmUser_CannotBuildAnotherFirmsSummary()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 400m, firmId: OtherFirmId, contractId: 2);

        var firmUser = new FakeCurrentUser { UserId = 2, FirmId = FirmId };
        var service = CreateService(connection, firmUser);

        // Boş sonuç DEĞİL, açık yetki hatası bekliyoruz.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.BuildAsync(PeriodId, OtherFirmId));
    }

    [Fact]
    public async Task FirmUser_CanBuildOwnSummary()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 400m);

        var firmUser = new FakeCurrentUser { UserId = 2, FirmId = FirmId };
        var summary = await CreateService(connection, firmUser).BuildAsync(PeriodId, FirmId);

        Assert.Equal(1, summary.RecordCount);
        Assert.Equal(400m, summary.LinesTotal);
    }

    [Fact]
    public async Task MipStaff_CanBuildAnyFirmsSummary()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 400m, firmId: OtherFirmId, contractId: 2);

        var summary = await CreateService(connection, new FakeCurrentUser { UserId = 1 }).BuildAsync(PeriodId, OtherFirmId);

        Assert.Equal(1, summary.RecordCount);
    }

    /// <summary>
    /// Yetki kontrolü dışında, global query filter da ikinci bir bariyerdir:
    /// firma kullanıcısının bağlamında başka firmanın kaydı hiç görünmez.
    /// </summary>
    [Fact]
    public async Task FirmUser_SummaryDoesNotLeakOtherFirmRecords()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 400m);
        await AddRecordAsync(connection, 2, WorkRecordStatus.APPROVED, 5000m, firmId: OtherFirmId, contractId: 2);

        var firmUser = new FakeCurrentUser { UserId = 2, FirmId = FirmId };
        var summary = await CreateService(connection, firmUser).BuildAsync(PeriodId, FirmId);

        Assert.Equal(1, summary.RecordCount);
        Assert.Equal(400m, summary.LinesTotal);
        Assert.DoesNotContain(summary.ServiceGroups.SelectMany(g => g.Lines), l => l.LineAmount == 5000m);
    }

    // ---------------------------------------------------------------
    // 3) Mobilizasyon: kayıt başına BİR KEZ, satır tutarına dahil DEĞİL
    // ---------------------------------------------------------------

    [Fact]
    public async Task Mobilization_IsCountedOncePerRecordEvenWithMultipleLines()
    {
        await using var connection = await CreateSeededConnectionAsync();

        // Üç satırlı tek kayıt: mobilizasyon KAYIT seviyesinde, bir kez.
        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var record = new WorkRecord
            {
                WorkRecordId = 1,
                DocumentNo = "WR-2026-00001",
                Status = WorkRecordStatus.APPROVED,
                FirmId = FirmId,
                ContractId = 1,
                PeriodId = PeriodId,
                WorkDate = new DateOnly(2026, 3, 10),
                EnteredByUserId = 2,
                MobilizationFee = 250m,
                TotalAmount = 900m,
                Currency = "TRY",
                CreatedAt = DateTime.UtcNow
            };

            for (var lineNo = 1; lineNo <= 3; lineNo++)
            {
                record.WorkRecordLines.Add(new WorkRecordLine
                {
                    LineNo = lineNo,
                    ServiceId = ServiceId,
                    RawQuantity = 2m,
                    BillableQuantity = 2m,
                    Unit = ServiceUnit.HOUR,
                    UnitPriceSnapshot = 100m,
                    LineAmount = 200m,
                    Currency = "TRY"
                });
            }

            db.WorkRecords.Add(record);
            await db.SaveChangesAsync();
        }

        var summary = await CreateService(connection, new FakeCurrentUser { UserId = 1 }).BuildAsync(PeriodId, FirmId);

        Assert.Single(summary.Mobilizations);
        Assert.Equal(250m, summary.MobilizationTotal);
        Assert.Equal(600m, summary.LinesTotal);   // 3 x 200, mobilizasyon dahil DEĞİL
        Assert.Equal(850m, summary.GrandTotal);
    }

    [Fact]
    public async Task Mobilization_IsNotIncludedInServiceSubtotals()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 400m, mobilizationFee: 150m);

        var summary = await CreateService(connection, new FakeCurrentUser { UserId = 1 }).BuildAsync(PeriodId, FirmId);

        var group = Assert.Single(summary.ServiceGroups);
        Assert.Equal(400m, group.SubtotalAmount);
        Assert.Equal(150m, summary.MobilizationTotal);
    }

    // ---------------------------------------------------------------
    // 4) İcmal toplamı = satır toplamları + mobilizasyon
    // ---------------------------------------------------------------

    [Fact]
    public async Task GrandTotal_EqualsLineSubtotalsPlusMobilization()
    {
        await using var connection = await CreateSeededConnectionAsync();

        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 400m, mobilizationFee: 100m);
        await AddRecordAsync(connection, 2, WorkRecordStatus.LOCKED, 250m, mobilizationFee: 50m);
        await AddRecordAsync(connection, 3, WorkRecordStatus.APPROVED, 175.5m);

        var summary = await CreateService(connection, new FakeCurrentUser { UserId = 1 }).BuildAsync(PeriodId, FirmId);

        var subtotalSum = summary.ServiceGroups.Sum(g => g.SubtotalAmount);
        var mobilizationSum = summary.Mobilizations.Sum(m => m.Amount);

        Assert.Equal(825.5m, subtotalSum);
        Assert.Equal(150m, mobilizationSum);
        Assert.Equal(subtotalSum, summary.LinesTotal);
        Assert.Equal(mobilizationSum, summary.MobilizationTotal);
        Assert.Equal(subtotalSum + mobilizationSum, summary.GrandTotal);
        Assert.Equal(975.5m, summary.GrandTotal);
    }

    [Fact]
    public async Task ServiceFilter_LimitsSummaryToThatService()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 400m, mobilizationFee: 100m);

        // ServiceId 2 model seed'inde var ama bu dönemde satırı yok.
        var summary = await CreateService(connection, new FakeCurrentUser { UserId = 1 })
            .BuildAsync(PeriodId, FirmId, serviceId: 2);

        Assert.Equal(0, summary.RecordCount);
        Assert.Empty(summary.ServiceGroups);

        // Kayıt icmale girmediyse mobilizasyonu da girmemeli.
        Assert.Empty(summary.Mobilizations);
        Assert.Equal(0m, summary.GrandTotal);
    }
}
