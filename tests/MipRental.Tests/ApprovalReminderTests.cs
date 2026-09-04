using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Email;
using MipRental.Data.Interceptors;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Tests;

/// <summary>
/// ADIM 15 — HATIRLATMA VE ESKALASYON TETİKLEYİCİSİ.
///
/// Model seed'i: FlowStep 1 = "Amir Onayı" (RoleId 2, hatırlatma 24s, eskalasyon
/// 48s), FlowStep 2 = "Bütçe Yöneticisi Onayı" (RoleId 3). Eskalasyon bir sonraki
/// adımın rolüne gider.
/// </summary>
public class ApprovalReminderTests
{
    private const int EquipmentManagerRoleId = 2;
    private const int BudgetManagerRoleId = 3;
    private const int EquipmentManagerUserId = 20;
    private const int BudgetManagerUserId = 21;
    private const int FirmId = 1;

    private static readonly DateTime AssignedAt = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static AppDbContext CreateContext(SqliteConnection connection) =>
        new SqliteTestContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new PeriodGuardInterceptor(), new ImmutabilityGuardInterceptor())
                .Options,
            new FakeCurrentUser());

    private static async Task<SqliteConnection> CreateSeededConnectionAsync(ApprovalDecision? decision = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();

        db.Firms.Add(new Firm { FirmId = FirmId, Code = "FIRMA-1", Title = "Firma 1", CreatedAt = DateTime.UtcNow });
        db.Users.AddRange(
            new User { UserId = EquipmentManagerUserId, UserName = "ekipman", FullName = "Ekipman Müdürü", Email = "ekipman@mip.com.tr", CreatedAt = DateTime.UtcNow },
            new User { UserId = BudgetManagerUserId, UserName = "mudur", FullName = "Bütçe Yöneticisi", Email = "mudur@mip.com.tr", CreatedAt = DateTime.UtcNow });
        db.UserRoles.AddRange(
            new UserRole { UserId = EquipmentManagerUserId, RoleId = EquipmentManagerRoleId },
            new UserRole { UserId = BudgetManagerUserId, RoleId = BudgetManagerRoleId });

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

        db.WorkRecords.Add(new WorkRecord
        {
            WorkRecordId = 1,
            DocumentNo = "WR-2026-00001",
            Status = WorkRecordStatus.PENDING,
            FirmId = FirmId,
            ContractId = 1,
            PeriodId = 9,
            WorkDate = new DateOnly(2026, 9, 1),
            EnteredByUserId = EquipmentManagerUserId,
            TotalAmount = 9999m,
            Currency = "TRY",
            CreatedAt = DateTime.UtcNow
        });

        db.Approvals.Add(new Approval
        {
            ApprovalId = 1,
            DocumentType = DocumentType.WORK_RECORD,
            DocumentId = 1,
            FlowStepId = 1,
            StepNo = 1,
            AssignedToRoleId = EquipmentManagerRoleId,
            Decision = decision,
            DecidedAt = decision is null ? null : AssignedAt.AddHours(1),
            AssignedAt = AssignedAt
        });

        await db.SaveChangesAsync();
        return connection;
    }

    private static async Task<int> RunAsync(SqliteConnection connection, DateTime utcNow)
    {
        await using var db = CreateContext(connection);
        return await new ApprovalReminderScheduler(db).RunAsync(utcNow);
    }

    // ---------------------------------------------------------------
    // Hatırlatma adım başına BİR KEZ
    // ---------------------------------------------------------------

    [Fact]
    public async Task Reminder_IsQueuedOnce_PerStep()
    {
        await using var connection = await CreateSeededConnectionAsync();

        // 24 saat dolmadan hiçbir şey üretilmez.
        Assert.Equal(0, await RunAsync(connection, AssignedAt.AddHours(23)));

        // Süre dolunca adımın rolündeki kişiye bir hatırlatma düşer.
        Assert.Equal(1, await RunAsync(connection, AssignedAt.AddHours(25)));

        // Sonraki turlar tekrar üretmez.
        Assert.Equal(0, await RunAsync(connection, AssignedAt.AddHours(26)));
        Assert.Equal(0, await RunAsync(connection, AssignedAt.AddHours(30)));

        await using var verify = CreateContext(connection);
        var reminders = await verify.Notifications.AsNoTracking()
            .Where(n => n.TemplateCode == ApprovalReminderScheduler.ReminderTemplate)
            .ToListAsync();

        var reminder = Assert.Single(reminders);
        Assert.Equal(EquipmentManagerUserId, reminder.UserId);
        Assert.Equal("ekipman@mip.com.tr", reminder.Email);
        Assert.Contains("WR-2026-00001", reminder.Subject);
        Assert.Equal(NotificationStatus.QUEUED, reminder.Status);

        // Damga kondu: bir daha üretilmeyeceğinin kaydı.
        Assert.NotNull((await verify.Approvals.IgnoreQueryFilters().AsNoTracking().SingleAsync()).ReminderSentAt);
    }

    // ---------------------------------------------------------------
    // Karar verilmiş adıma bildirim gitmez
    // ---------------------------------------------------------------

    [Fact]
    public async Task DecidedApproval_GetsNoReminderOrEscalation()
    {
        await using var connection = await CreateSeededConnectionAsync(ApprovalDecision.APPROVED);

        // Hatırlatma ve eskalasyon sürelerinin ikisi de fazlasıyla geçti.
        Assert.Equal(0, await RunAsync(connection, AssignedAt.AddDays(10)));

        await using var verify = CreateContext(connection);
        Assert.False(await verify.Notifications.AnyAsync());

        var approval = await verify.Approvals.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        Assert.Null(approval.ReminderSentAt);
        Assert.Null(approval.EscalationSentAt);
    }

    // ---------------------------------------------------------------
    // Eskalasyon: bir kez ve BİR ÜST adımın rolüne
    // ---------------------------------------------------------------

    [Fact]
    public async Task Escalation_IsQueuedOnce_ToNextStepRole()
    {
        await using var connection = await CreateSeededConnectionAsync();

        // 49. saat: hem hatırlatma hem eskalasyon zamanı gelmiş olur.
        var queued = await RunAsync(connection, AssignedAt.AddHours(49));
        Assert.Equal(2, queued);

        // Sonraki turlarda ikisi de tekrar üretilmez.
        Assert.Equal(0, await RunAsync(connection, AssignedAt.AddHours(72)));

        await using var verify = CreateContext(connection);
        var escalation = Assert.Single(await verify.Notifications.AsNoTracking()
            .Where(n => n.TemplateCode == ApprovalReminderScheduler.EscalationTemplate)
            .ToListAsync());

        // Bir sonraki adımın rolü: Bütçe Yöneticisi.
        Assert.Equal(BudgetManagerUserId, escalation.UserId);
        Assert.Contains("Eskalasyon", escalation.Subject);
        Assert.NotNull((await verify.Approvals.IgnoreQueryFilters().AsNoTracking().SingleAsync()).EscalationSentAt);
    }

    /// <summary>
    /// Hatırlatma ve eskalasyon gövdelerinde TUTAR geçmez: alıcı adımın rolündeki
    /// kişidir ve o rol fiyat görmez (ADR-016).
    /// </summary>
    [Fact]
    public async Task ReminderAndEscalation_ContainNoAmount()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await RunAsync(connection, AssignedAt.AddHours(49));

        await using var verify = CreateContext(connection);
        var bodies = await verify.Notifications.AsNoTracking().Select(n => n.Body! + " " + n.Subject).ToListAsync();

        Assert.Equal(2, bodies.Count);
        Assert.All(bodies, body =>
        {
            Assert.DoesNotContain("9999", body);
            Assert.DoesNotContain("TRY", body);
            Assert.DoesNotContain("Tutar", body);
        });
    }
}
