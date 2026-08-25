using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Interceptors;
using MipRental.Data.Services;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Approvals;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;

namespace MipRental.Tests;

/// <summary>
/// Dönem kapanışının kayıtlara yansıması (APPROVED -> LOCKED) ve kilitli kaydın
/// dokunulmazlığı. Gerçek transaction gerektiği için SQLite kullanılıyor.
/// </summary>
public class PeriodLockTests
{
    private const int FirmId = 1;
    private const int ServiceId = 1;
    private const int PeriodId = 3;   // 2026 / Mart, OPEN — model seed'inden gelir
    private const int BudgetUserId = 1;

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
        db.Users.Add(new User { UserId = BudgetUserId, UserName = "butce", FullName = "Bütçe Sorumlusu", CreatedAt = DateTime.UtcNow });
        db.Users.Add(new User { UserId = 2, UserName = "firma1", FullName = "Firma Kullanıcısı", FirmId = FirmId, CreatedAt = DateTime.UtcNow });
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
        await db.SaveChangesAsync();
        return connection;
    }

    private static async Task AddRecordAsync(SqliteConnection connection, int id, WorkRecordStatus status)
    {
        await using var db = CreateContext(connection, new FakeCurrentUser());
        var record = new WorkRecord
        {
            WorkRecordId = id,
            DocumentNo = $"WR-2026-{id:00000}",
            Status = status,
            FirmId = FirmId,
            ContractId = 1,
            PeriodId = PeriodId,
            WorkDate = new DateOnly(2026, 3, 10),
            EnteredByUserId = 2,
            TotalAmount = 400m,
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
            UnitPriceSnapshot = 100m,
            LineAmount = 400m,
            Currency = "TRY"
        });
        db.WorkRecords.Add(record);
        await db.SaveChangesAsync();
    }

    private static async Task<Period> LoadPeriodAsync(AppDbContext db) =>
        await db.Periods.SingleAsync(p => p.PeriodId == PeriodId);

    // ---------------------------------------------------------------
    // Kapanış: APPROVED -> LOCKED, tek transaction
    // ---------------------------------------------------------------

    [Fact]
    public async Task ClosingPeriod_LocksApprovedRecordsOnly()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED);
        await AddRecordAsync(connection, 2, WorkRecordStatus.APPROVED);
        await AddRecordAsync(connection, 3, WorkRecordStatus.DRAFT);
        await AddRecordAsync(connection, 4, WorkRecordStatus.REJECTED);
        await AddRecordAsync(connection, 5, WorkRecordStatus.PENDING);

        await using (var db = CreateContext(connection, new FakeCurrentUser { UserId = BudgetUserId }))
        {
            var locked = await new PeriodLockService(db).CloseAsync(await LoadPeriodAsync(db), BudgetUserId);
            Assert.Equal(2, locked);
        }

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var records = await db.WorkRecords.AsNoTracking().OrderBy(w => w.WorkRecordId).ToListAsync();
            Assert.Equal(WorkRecordStatus.LOCKED, records[0].Status);
            Assert.Equal(WorkRecordStatus.LOCKED, records[1].Status);
            Assert.Equal(WorkRecordStatus.DRAFT, records[2].Status);
            Assert.Equal(WorkRecordStatus.REJECTED, records[3].Status);
            Assert.Equal(WorkRecordStatus.PENDING, records[4].Status);

            var period = await db.Periods.AsNoTracking().SingleAsync(p => p.PeriodId == PeriodId);
            Assert.Equal(PeriodStatus.CLOSED, period.Status);
            Assert.Equal(BudgetUserId, period.ClosedBy);
            Assert.NotNull(period.ClosedAt);
        }
    }

    // ---------------------------------------------------------------
    // Kilitli kayıt DEĞİŞTİRİLEMEZ (görevin ana testi)
    // ---------------------------------------------------------------

    /// <summary>
    /// Kapalı dönemdeki kilitli kayıt HİÇBİR ŞEKİLDE değiştirilemez.
    ///
    /// Burada iki bağımsız koruma birden devrededir (dönem kapalı + kayıt kilitli),
    /// bu yüzden hangi guard'ın önce ateşlediğine bağlanmıyoruz; önemli olan
    /// SaveChanges'in reddedilmesi ve verinin diskte hiç değişmemesidir.
    /// Kilidin TEK BAŞINA koruduğu ise bir sonraki testte kanıtlanıyor.
    /// </summary>
    [Fact]
    public async Task LockedRecordInClosedPeriod_CannotBeModified()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED);

        await using (var db = CreateContext(connection, new FakeCurrentUser { UserId = BudgetUserId }))
        {
            await new PeriodLockService(db).CloseAsync(await LoadPeriodAsync(db), BudgetUserId);
        }

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var record = await db.WorkRecords.SingleAsync(w => w.WorkRecordId == 1);
            record.TotalAmount = 99_999m;

            await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
        }

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var line = await db.WorkRecordLines.FirstAsync(l => l.WorkRecordId == 1);
            line.LineAmount = 12_345m;

            await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
        }

        await using (var verify = CreateContext(connection, new FakeCurrentUser()))
        {
            var record = await verify.WorkRecords.AsNoTracking().SingleAsync(w => w.WorkRecordId == 1);
            Assert.Equal(400m, record.TotalAmount);
            Assert.Equal(WorkRecordStatus.LOCKED, record.Status);
            Assert.Equal(400m, (await verify.WorkRecordLines.AsNoTracking().FirstAsync(l => l.WorkRecordId == 1)).LineAmount);
        }
    }

    /// <summary>
    /// Kilidin KENDİ BAŞINA koruduğunu kanıtlar.
    ///
    /// Dönem yeniden açılır (PeriodGuard artık ateşlemez) ama kaydın kilidi
    /// açılmaz. Kayıt hâlâ LOCKED olduğu için ImmutabilityGuard tek başına
    /// değişikliği reddetmelidir — yani "kilitli kayıt değiştirilemez" kuralı
    /// dönem kuralının gölgesi değil, bağımsız bir korumadır.
    /// </summary>
    [Fact]
    public async Task LockedRecord_CannotBeModified_EvenWhenPeriodIsReopened()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED);

        await using (var db = CreateContext(connection, new FakeCurrentUser { UserId = BudgetUserId }))
        {
            await new PeriodLockService(db).CloseAsync(await LoadPeriodAsync(db), BudgetUserId);
        }

        // Dönemi aç ama kaydı LOCKED bırak (PeriodLockService.ReopenAsync yerine
        // sadece dönemi güncelliyoruz).
        await using (var db = CreateContext(connection, new FakeCurrentUser { UserId = BudgetUserId }))
        {
            var period = await LoadPeriodAsync(db);
            period.Status = PeriodStatus.REOPENED;
            period.ReopenReason = "test";
            period.ReopenedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var record = await db.WorkRecords.SingleAsync(w => w.WorkRecordId == 1);
            record.TotalAmount = 99_999m;

            var exception = await Assert.ThrowsAsync<ImmutabilityViolationException>(() => db.SaveChangesAsync());
            Assert.Contains("kilitli", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var line = await db.WorkRecordLines.FirstAsync(l => l.WorkRecordId == 1);
            line.LineAmount = 12_345m;

            var exception = await Assert.ThrowsAsync<ImmutabilityViolationException>(() => db.SaveChangesAsync());
            Assert.Contains("kilitli", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        await using (var verify = CreateContext(connection, new FakeCurrentUser()))
        {
            var record = await verify.WorkRecords.AsNoTracking().SingleAsync(w => w.WorkRecordId == 1);
            Assert.Equal(400m, record.TotalAmount);
            Assert.Equal(WorkRecordStatus.LOCKED, record.Status);
        }
    }

    /// <summary>
    /// Kilidi açmak, kaydı düzenlemenin arka kapısı olamaz: Status'la BİRLİKTE
    /// başka bir alan değiştirilirse SaveChanges reddedilir.
    /// </summary>
    [Fact]
    public async Task UnlockAttempt_WithOtherFieldChange_IsRejected()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED);

        await using (var db = CreateContext(connection, new FakeCurrentUser { UserId = BudgetUserId }))
        {
            await new PeriodLockService(db).CloseAsync(await LoadPeriodAsync(db), BudgetUserId);
        }

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var period = await LoadPeriodAsync(db);
            period.Status = PeriodStatus.REOPENED;
            period.ReopenReason = "test";
            period.ReopenedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var record = await db.WorkRecords.SingleAsync(w => w.WorkRecordId == 1);
            record.Status = WorkRecordStatus.APPROVED;
            record.TotalAmount = 1m;   // kilit açılırken tutar da değiştirilmek isteniyor

            var exception = await Assert.ThrowsAsync<ImmutabilityViolationException>(() => db.SaveChangesAsync());
            Assert.Contains("TotalAmount", exception.Message, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------
    // Yeniden açma: LOCKED -> APPROVED
    // ---------------------------------------------------------------

    [Fact]
    public async Task ReopeningPeriod_UnlocksRecordsAndKeepsReason()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED);
        await AddRecordAsync(connection, 2, WorkRecordStatus.DRAFT);

        await using (var db = CreateContext(connection, new FakeCurrentUser { UserId = BudgetUserId }))
        {
            await new PeriodLockService(db).CloseAsync(await LoadPeriodAsync(db), BudgetUserId);
        }

        await using (var db = CreateContext(connection, new FakeCurrentUser { UserId = BudgetUserId }))
        {
            var unlocked = await new PeriodLockService(db)
                .ReopenAsync(await LoadPeriodAsync(db), BudgetUserId, "Fatura düzeltmesi için açıldı");
            Assert.Equal(1, unlocked);
        }

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var records = await db.WorkRecords.AsNoTracking().OrderBy(w => w.WorkRecordId).ToListAsync();
            Assert.Equal(WorkRecordStatus.APPROVED, records[0].Status);
            Assert.Equal(WorkRecordStatus.DRAFT, records[1].Status);

            var period = await db.Periods.AsNoTracking().SingleAsync(p => p.PeriodId == PeriodId);
            Assert.Equal(PeriodStatus.REOPENED, period.Status);
            // Kilit açmanın gerekçesi kayıt bazında değil, dönemde tutulur.
            Assert.Equal("Fatura düzeltmesi için açıldı", period.ReopenReason);
        }
    }

    [Fact]
    public async Task Reopen_WithoutReason_IsRejected()
    {
        await using var connection = await CreateSeededConnectionAsync();

        await using var db = CreateContext(connection, new FakeCurrentUser { UserId = BudgetUserId });
        var period = await LoadPeriodAsync(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => new PeriodLockService(db).ReopenAsync(period, BudgetUserId, "   "));
    }

    /// <summary>
    /// Kilidi açmanın tek meşru yolu dönemin yeniden açılmasıdır. Dönem hâlâ
    /// kapalıyken durum makinesi kilidi açmayı reddeder.
    /// </summary>
    [Fact]
    public async Task Unlock_WhilePeriodStillClosed_IsRejected()
    {
        var record = new WorkRecord
        {
            WorkRecordId = 1,
            DocumentNo = "WR-2026-00001",
            Status = WorkRecordStatus.LOCKED,
            FirmId = FirmId,
            ContractId = 1,
            PeriodId = PeriodId,
            WorkDate = new DateOnly(2026, 3, 10),
            EnteredByUserId = 2
        };
        var closedPeriod = new Period { PeriodId = PeriodId, Year = 2026, Month = 3, Status = PeriodStatus.CLOSED };

        var exception = Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.UnlockForPeriodReopen(record, closedPeriod));

        Assert.Contains("yeniden açılmadan", exception.Message, StringComparison.Ordinal);
        Assert.Equal(WorkRecordStatus.LOCKED, record.Status);
    }

    /// <summary>
    /// LOCKED terminaldir: onay akışının hiçbir metodu kilitli kaydı hareket
    /// ettiremez. Kilitten çıkışın TEK yolu dönemin yeniden açılmasıdır.
    /// </summary>
    [Fact]
    public void LockedRecord_CannotBeMovedByAnyApprovalTransition()
    {
        var period = new Period { PeriodId = PeriodId, Year = 2026, Month = 3, Status = PeriodStatus.OPEN };
        var actor = TransitionActor.From(
            new FakeCurrentUser { UserId = 9, FirmId = null }, new[] { "SUPERVISOR" });
        var firmActor = TransitionActor.From(
            new FakeCurrentUser { UserId = 2, FirmId = FirmId }, new[] { "FIRM_USER" });

        WorkRecord Locked() => new()
        {
            WorkRecordId = 1,
            DocumentNo = "WR-2026-00001",
            Status = WorkRecordStatus.LOCKED,
            FirmId = FirmId,
            ContractId = 1,
            PeriodId = PeriodId,
            WorkDate = new DateOnly(2026, 3, 10),
            EnteredByUserId = 2
        };

        Assert.Throws<WorkRecordStateTransitionException>(() => WorkRecordStateMachine.Submit(Locked(), period, firmActor));
        Assert.Throws<WorkRecordStateTransitionException>(() => WorkRecordStateMachine.SendToApproval(Locked(), period, firmActor));
        Assert.Throws<WorkRecordStateTransitionException>(() => WorkRecordStateMachine.Approve(Locked(), period, actor, "SUPERVISOR", "Amir"));
        Assert.Throws<WorkRecordStateTransitionException>(() => WorkRecordStateMachine.Reject(Locked(), period, actor, "SUPERVISOR", "Amir", "gerekçe"));
        Assert.Throws<WorkRecordStateTransitionException>(() => WorkRecordStateMachine.RequestRevision(Locked(), period, actor, "SUPERVISOR", "Amir", "gerekçe"));
        Assert.Throws<WorkRecordStateTransitionException>(() => WorkRecordStateMachine.Cancel(Locked(), period, firmActor));
        Assert.Throws<WorkRecordStateTransitionException>(() => WorkRecordStateMachine.EnsureCanCreateRevision(Locked(), period, firmActor));
        Assert.Throws<WorkRecordStateTransitionException>(() => WorkRecordStateMachine.LockForPeriodClose(Locked(), period));
    }

    /// <summary>
    /// Kilitli kayda müdahale edilmek istendiğinde kullanıcıya YAPILABİLECEK
    /// olan söylenmeli. "Yeni versiyon oluşturun" LOCKED için yanlış tavsiyedir:
    /// dönem kapalıyken yeni versiyon da açılamaz.
    /// </summary>
    [Fact]
    public void LockedRecord_ErrorMessagePointsToReopeningThePeriod()
    {
        var period = new Period { PeriodId = PeriodId, Year = 2026, Month = 3, Status = PeriodStatus.OPEN };
        var firmActor = TransitionActor.From(
            new FakeCurrentUser { UserId = 2, FirmId = FirmId }, new[] { "FIRM_USER" });
        var locked = new WorkRecord
        {
            WorkRecordId = 1,
            DocumentNo = "WR-2026-00001",
            Status = WorkRecordStatus.LOCKED,
            FirmId = FirmId,
            ContractId = 1,
            PeriodId = PeriodId,
            WorkDate = new DateOnly(2026, 3, 10),
            EnteredByUserId = 2
        };

        var exception = Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.EnsureCanCreateRevision(locked, period, firmActor));

        Assert.Contains("dönemin yeniden açılması", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("yeni versiyon oluşturulmalıdır", exception.Message, StringComparison.Ordinal);

        // REJECTED gibi diğer terminal durumlarda eski tavsiye geçerli kalmalı.
        var rejected = new WorkRecord
        {
            WorkRecordId = 2,
            DocumentNo = "WR-2026-00002",
            Status = WorkRecordStatus.REJECTED,
            FirmId = FirmId,
            ContractId = 1,
            PeriodId = PeriodId,
            WorkDate = new DateOnly(2026, 3, 10),
            EnteredByUserId = 2
        };

        var rejectedException = Assert.Throws<WorkRecordStateTransitionException>(
            () => WorkRecordStateMachine.EnsureCanCreateRevision(rejected, period, firmActor));
        Assert.Contains("yeni versiyon oluşturulmalıdır", rejectedException.Message, StringComparison.Ordinal);
    }

    /// <summary>Onaylanmamış bir kayıt dönem kapanışında kilitlenemez.</summary>
    [Fact]
    public void LockForPeriodClose_RejectsNonApprovedRecord()
    {
        var period = new Period { PeriodId = PeriodId, Year = 2026, Month = 3, Status = PeriodStatus.OPEN };
        var draft = new WorkRecord
        {
            WorkRecordId = 1,
            DocumentNo = "WR-2026-00001",
            Status = WorkRecordStatus.DRAFT,
            FirmId = FirmId,
            ContractId = 1,
            PeriodId = PeriodId,
            WorkDate = new DateOnly(2026, 3, 10),
            EnteredByUserId = 2
        };

        Assert.Throws<WorkRecordStateTransitionException>(() => WorkRecordStateMachine.LockForPeriodClose(draft, period));
        Assert.Equal(WorkRecordStatus.DRAFT, draft.Status);
    }
}
