using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Interceptors;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;

namespace MipRental.Tests;

public class ImmutabilityGuardInterceptorTests
{
    private static AppDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new ImmutabilityGuardInterceptor())
            .Options;
        return new AppDbContext(options, new FakeCurrentUser());
    }

    private static async Task<int> SeedApprovedWorkRecordAsync(string dbName, bool withLine = false)
    {
        await using var db = CreateContext(dbName);
        db.Firms.Add(new Firm { FirmId = 1, Code = "FIRMA-1", Title = "Firma 1", CreatedAt = DateTime.UtcNow });
        db.Users.Add(new User { UserId = 1, UserName = "test.user", FullName = "Test Kullanıcı", CreatedAt = DateTime.UtcNow });
        db.Contracts.Add(new Contract
        {
            ContractId = 1,
            FirmId = 1,
            ContractNo = "SOZ-1",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Currency = "TRY",
            Status = ContractStatus.ACTIVE,
            CreatedAt = DateTime.UtcNow
        });
        db.Periods.Add(new Period { PeriodId = 1, Year = 2026, Month = 3, Status = PeriodStatus.OPEN });
        db.ServiceCategories.Add(new ServiceCategory { ServiceId = 1, Code = "VINC", Name = "Mobil Vinç", Unit = ServiceUnit.HOUR, IsActive = true });

        var record = new WorkRecord
        {
            DocumentNo = "WR-2026-00001",
            FirmId = 1,
            ContractId = 1,
            PeriodId = 1,
            WorkDate = new DateOnly(2026, 3, 10),
            EnteredByUserId = 1,
            Status = WorkRecordStatus.APPROVED
        };

        if (withLine)
        {
            record.WorkRecordLines.Add(new WorkRecordLine
            {
                WorkRecordLineId = 1,
                ServiceId = 1,
                RawQuantity = 4,
                BillableQuantity = 4,
                Unit = ServiceUnit.HOUR,
                UnitPriceSnapshot = 100m,
                LineAmount = 400m,
                Currency = "TRY"
            });
        }

        db.WorkRecords.Add(record);
        await db.SaveChangesAsync();
        return record.WorkRecordId;
    }

    [Fact]
    public async Task Update_ApprovedWorkRecord_NonIntegrationField_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var workRecordId = await SeedApprovedWorkRecordAsync(dbName);

        await using var db = CreateContext(dbName);
        var record = await db.WorkRecords.SingleAsync(w => w.WorkRecordId == workRecordId);
        record.WorkDescription = "Sonradan eklenen açıklama";

        var ex = await Assert.ThrowsAsync<ImmutabilityViolationException>(() => db.SaveChangesAsync());
        Assert.Contains("Onaylanmış", ex.Message);
    }

    [Fact]
    public async Task Update_ApprovedWorkRecord_IntegrationStatusOnly_Succeeds()
    {
        var dbName = Guid.NewGuid().ToString();
        var workRecordId = await SeedApprovedWorkRecordAsync(dbName);

        await using (var db = CreateContext(dbName))
        {
            var record = await db.WorkRecords.SingleAsync(w => w.WorkRecordId == workRecordId);
            record.IntegrationStatus = WorkRecordIntegrationStatus.SENT;
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(dbName))
        {
            var record = await db.WorkRecords.AsNoTracking().SingleAsync(w => w.WorkRecordId == workRecordId);
            Assert.Equal(WorkRecordIntegrationStatus.SENT, record.IntegrationStatus);
        }
    }

    [Fact]
    public async Task Update_LineOfApprovedWorkRecord_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedApprovedWorkRecordAsync(dbName, withLine: true);

        await using var db = CreateContext(dbName);
        var line = await db.WorkRecordLines.SingleAsync();
        line.BillableQuantity = 999m;

        var ex = await Assert.ThrowsAsync<ImmutabilityViolationException>(() => db.SaveChangesAsync());
        Assert.Contains("satırları", ex.Message);
    }

    [Fact]
    public async Task AddLine_ToApprovedWorkRecord_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var workRecordId = await SeedApprovedWorkRecordAsync(dbName);

        await using var db = CreateContext(dbName);
        db.WorkRecordLines.Add(new WorkRecordLine
        {
            WorkRecordId = workRecordId,
            ServiceId = 1,
            RawQuantity = 2,
            BillableQuantity = 2,
            Unit = ServiceUnit.HOUR,
            UnitPriceSnapshot = 100m,
            LineAmount = 200m,
            Currency = "TRY"
        });

        await Assert.ThrowsAsync<ImmutabilityViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Update_DraftWorkRecord_Succeeds()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateContext(dbName);
        db.Firms.Add(new Firm { FirmId = 1, Code = "FIRMA-1", Title = "Firma 1", CreatedAt = DateTime.UtcNow });
        db.Users.Add(new User { UserId = 1, UserName = "test.user", FullName = "Test Kullanıcı", CreatedAt = DateTime.UtcNow });
        db.Contracts.Add(new Contract
        {
            ContractId = 1, FirmId = 1, ContractNo = "SOZ-1",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Currency = "TRY", Status = ContractStatus.ACTIVE, CreatedAt = DateTime.UtcNow
        });
        db.Periods.Add(new Period { PeriodId = 1, Year = 2026, Month = 3, Status = PeriodStatus.OPEN });

        var record = new WorkRecord
        {
            DocumentNo = "WR-DRAFT-1", FirmId = 1, ContractId = 1, PeriodId = 1,
            WorkDate = new DateOnly(2026, 3, 10), EnteredByUserId = 1, Status = WorkRecordStatus.DRAFT
        };
        db.WorkRecords.Add(record);
        await db.SaveChangesAsync();

        record.WorkDescription = "Taslakken serbestçe düzenlenebilir";
        await db.SaveChangesAsync();

        var reloaded = await db.WorkRecords.AsNoTracking().SingleAsync(w => w.WorkRecordId == record.WorkRecordId);
        Assert.Equal("Taslakken serbestçe düzenlenebilir", reloaded.WorkDescription);
    }

    [Fact]
    public async Task Delete_AnyEntity_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateContext(dbName);
        db.Firms.Add(new Firm { FirmId = 1, Code = "FIRMA-1", Title = "Firma 1", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var firm = await db.Firms.SingleAsync();
        db.Firms.Remove(firm);

        var ex = await Assert.ThrowsAsync<ImmutabilityViolationException>(() => db.SaveChangesAsync());
        Assert.Contains("silinemez", ex.Message);
    }

    /// <summary>
    /// Silme yasağının TEK istisnası: kullanıcı-rol eşlemesi. CLAUDE.md kural 1
    /// "onaylanmış MALİ KAYIT" der; UserRole mali kayıt değil, erişim eşlemesidir
    /// ve rol geri alınabilmelidir (kullanıcı görev değiştirir / ayrılır).
    /// </summary>
    [Fact]
    public async Task Delete_UserRole_IsAllowed()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateContext(dbName);

        db.Users.Add(new User { UserId = 1, UserName = "test.user", FullName = "Test Kullanıcı", CreatedAt = DateTime.UtcNow });
        db.Roles.AddRange(
            new Role { RoleId = 1, Code = "BUDGET", Name = "Bütçe", Scope = RoleScope.INTERNAL },
            new Role { RoleId = 2, Code = "ADMIN", Name = "Sistem Yöneticisi", Scope = RoleScope.INTERNAL });
        db.UserRoles.AddRange(
            new UserRole { UserId = 1, RoleId = 1 },
            new UserRole { UserId = 1, RoleId = 2 });
        await db.SaveChangesAsync();

        db.UserRoles.Remove(await db.UserRoles.SingleAsync(ur => ur.RoleId == 1));
        await db.SaveChangesAsync();   // fırlatmamalı

        var remaining = await db.UserRoles.Select(ur => ur.RoleId).ToListAsync();
        Assert.Equal([2], remaining);
    }

    /// <summary>
    /// İstisna DAR olmalı: kullanıcının kendisi hâlâ silinemez. Muafiyet yalnızca
    /// eşleme tablosunu kapsar, ona bağlı entity'leri değil.
    /// </summary>
    [Fact]
    public async Task Delete_User_IsStillRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateContext(dbName);
        db.Users.Add(new User { UserId = 1, UserName = "test.user", FullName = "Test Kullanıcı", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        db.Users.Remove(await db.Users.SingleAsync());

        var ex = await Assert.ThrowsAsync<ImmutabilityViolationException>(() => db.SaveChangesAsync());
        Assert.Contains("silinemez", ex.Message);
    }

    /// <summary>
    /// Muafiyet bir ARKA KAPI olmamalı: aynı SaveChanges içinde bir UserRole silinip
    /// yanında bir mali kayıt da silinmeye çalışılırsa, işlem yine reddedilir.
    /// </summary>
    [Fact]
    public async Task Delete_UserRoleTogetherWithFinancialRecord_IsStillRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var workRecordId = await SeedApprovedWorkRecordAsync(dbName);

        await using var db = CreateContext(dbName);
        db.Roles.Add(new Role { RoleId = 1, Code = "BUDGET", Name = "Bütçe", Scope = RoleScope.INTERNAL });
        db.UserRoles.Add(new UserRole { UserId = 1, RoleId = 1 });
        await db.SaveChangesAsync();

        db.UserRoles.Remove(await db.UserRoles.SingleAsync());
        db.WorkRecords.Remove(await db.WorkRecords.SingleAsync(w => w.WorkRecordId == workRecordId));

        var ex = await Assert.ThrowsAsync<ImmutabilityViolationException>(() => db.SaveChangesAsync());
        Assert.Contains("silinemez", ex.Message);
    }
}
