using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Interceptors;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;

namespace MipRental.Tests;

public class PeriodGuardInterceptorTests
{
    private static AppDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new PeriodGuardInterceptor())
            .Options;
        return new AppDbContext(options, new FakeCurrentUser());
    }

    private static async Task<(int FirmId, int ContractId)> SeedBaseDataAsync(string dbName)
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
        db.Periods.Add(new Period { PeriodId = 1, Year = 2026, Month = 2, Status = PeriodStatus.CLOSED });
        db.Periods.Add(new Period { PeriodId = 2, Year = 2026, Month = 3, Status = PeriodStatus.OPEN });
        await db.SaveChangesAsync();
        return (1, 1);
    }

    private static WorkRecord BuildWorkRecord(int firmId, int contractId, int periodId, DateOnly workDate) => new()
    {
        DocumentNo = $"WR-DRAFT-{Guid.NewGuid():N}",
        FirmId = firmId,
        ContractId = contractId,
        PeriodId = periodId,
        WorkDate = workDate,
        EnteredByUserId = 1,
        Status = WorkRecordStatus.DRAFT
    };

    [Fact]
    public async Task Insert_IntoClosedPeriod_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var (firmId, contractId) = await SeedBaseDataAsync(dbName);

        await using var db = CreateContext(dbName);
        db.WorkRecords.Add(BuildWorkRecord(firmId, contractId, periodId: 1, workDate: new DateOnly(2026, 2, 15)));

        var ex = await Assert.ThrowsAsync<PeriodGuardException>(() => db.SaveChangesAsync());
        Assert.Contains("Şubat 2026", ex.Message);
        Assert.Contains("kapalıdır", ex.Message);
    }

    [Fact]
    public async Task Insert_IntoOpenPeriod_Succeeds()
    {
        var dbName = Guid.NewGuid().ToString();
        var (firmId, contractId) = await SeedBaseDataAsync(dbName);

        await using var db = CreateContext(dbName);
        db.WorkRecords.Add(BuildWorkRecord(firmId, contractId, periodId: 2, workDate: new DateOnly(2026, 3, 10)));

        await db.SaveChangesAsync();

        Assert.Equal(1, await db.WorkRecords.CountAsync());
    }

    [Fact]
    public async Task Update_ExistingRecordInNowClosedPeriod_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var (firmId, contractId) = await SeedBaseDataAsync(dbName);

        int workRecordId;
        await using (var db = CreateContext(dbName))
        {
            var record = BuildWorkRecord(firmId, contractId, periodId: 2, workDate: new DateOnly(2026, 3, 10));
            db.WorkRecords.Add(record);
            await db.SaveChangesAsync();
            workRecordId = record.WorkRecordId;
        }

        // Dönem sonradan kapatılıyor (interceptor'ı atlayarak, doğrudan durum değişikliği).
        await using (var db = CreateContext(dbName))
        {
            var period = await db.Periods.SingleAsync(p => p.PeriodId == 2);
            period.Status = PeriodStatus.CLOSED;
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(dbName))
        {
            var record = await db.WorkRecords.SingleAsync(w => w.WorkRecordId == workRecordId);
            record.WorkDescription = "Güncellenmiş açıklama";

            var ex = await Assert.ThrowsAsync<PeriodGuardException>(() => db.SaveChangesAsync());
            Assert.Contains("Mart 2026", ex.Message);
        }
    }

    [Fact]
    public async Task InsertLine_UnderRecordInClosedPeriod_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var (firmId, contractId) = await SeedBaseDataAsync(dbName);

        await using var db = CreateContext(dbName);
        db.ServiceCategories.Add(new ServiceCategory { ServiceId = 1, Code = "VINC", Name = "Mobil Vinç", Unit = ServiceUnit.HOUR, IsActive = true });

        var record = BuildWorkRecord(firmId, contractId, periodId: 1, workDate: new DateOnly(2026, 2, 15));
        record.WorkRecordLines.Add(new WorkRecordLine
        {
            ServiceId = 1,
            RawQuantity = 4,
            BillableQuantity = 4,
            Unit = ServiceUnit.HOUR,
            UnitPriceSnapshot = 100m,
            LineAmount = 400m,
            Currency = "TRY"
        });
        db.WorkRecords.Add(record);

        await Assert.ThrowsAsync<PeriodGuardException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task WorkDate_OutsidePeriodMonth_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var (firmId, contractId) = await SeedBaseDataAsync(dbName);

        await using var db = CreateContext(dbName);
        // Period 2 = Mart 2026, ama iş tarihi 2025 (gerçek formlarda görülen hata).
        db.WorkRecords.Add(BuildWorkRecord(firmId, contractId, periodId: 2, workDate: new DateOnly(2025, 3, 19)));

        var ex = await Assert.ThrowsAsync<PeriodGuardException>(() => db.SaveChangesAsync());
        Assert.Contains("tarih aralığı dışında", ex.Message);
    }

    [Fact]
    public async Task WorkDate_OnFirstAndLastDayOfPeriodMonth_Succeeds()
    {
        var dbName = Guid.NewGuid().ToString();
        var (firmId, contractId) = await SeedBaseDataAsync(dbName);

        await using (var db = CreateContext(dbName))
        {
            db.WorkRecords.Add(BuildWorkRecord(firmId, contractId, periodId: 2, workDate: new DateOnly(2026, 3, 1)));
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(dbName))
        {
            db.WorkRecords.Add(BuildWorkRecord(firmId, contractId, periodId: 2, workDate: new DateOnly(2026, 3, 31)));
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(dbName))
        {
            Assert.Equal(2, await db.WorkRecords.CountAsync());
        }
    }
}
