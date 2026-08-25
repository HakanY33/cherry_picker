using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Tests;

public class FirmIsolationTests
{
    private static AppDbContext CreateContext(string dbName, ICurrentUser currentUser)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options, currentUser);
    }

    private static async Task SeedAsync(string dbName)
    {
        await using var db = CreateContext(dbName, new FakeCurrentUser());

        db.Firms.AddRange(
            new Firm { FirmId = 1, Code = "FIRMA-A", Title = "Firma A", CreatedAt = DateTime.UtcNow },
            new Firm { FirmId = 2, Code = "FIRMA-B", Title = "Firma B", CreatedAt = DateTime.UtcNow });

        db.Users.AddRange(
            new User { UserId = 1, UserName = "giren.kullanici", FullName = "Giren Kullanıcı", FirmId = 1, CreatedAt = DateTime.UtcNow },
            new User { UserId = 2, UserName = "diger.kullanici", FullName = "Diğer Kullanıcı", FirmId = 2, CreatedAt = DateTime.UtcNow },
            new User { UserId = 3, UserName = "mip.personeli", FullName = "MIP Personeli", FirmId = null, CreatedAt = DateTime.UtcNow });

        db.Periods.Add(new Period { PeriodId = 1, Year = 2026, Month = 1, Status = PeriodStatus.OPEN });

        db.Contracts.AddRange(
            new Contract { ContractId = 1, FirmId = 1, ContractNo = "C-A", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), CreatedAt = DateTime.UtcNow },
            new Contract { ContractId = 2, FirmId = 2, ContractNo = "C-B", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), CreatedAt = DateTime.UtcNow });

        db.WorkRecords.AddRange(
            new WorkRecord { WorkRecordId = 1, DocumentNo = "WR-1", FirmId = 1, ContractId = 1, PeriodId = 1, WorkDate = new DateOnly(2026, 1, 10), EnteredByUserId = 1, CreatedAt = DateTime.UtcNow },
            new WorkRecord { WorkRecordId = 2, DocumentNo = "WR-2", FirmId = 2, ContractId = 2, PeriodId = 1, WorkDate = new DateOnly(2026, 1, 10), EnteredByUserId = 1, CreatedAt = DateTime.UtcNow });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task FirmUser_OnlySeesOwnFirmWorkRecords()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAsync(dbName);

        await using var db = CreateContext(dbName, new FakeCurrentUser { FirmId = 1 });

        var records = await db.WorkRecords.ToListAsync();

        Assert.Single(records);
        Assert.All(records, r => Assert.Equal(1, r.FirmId));
    }

    [Fact]
    public async Task MipStaff_SeesAllFirmsWorkRecords()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAsync(dbName);

        await using var db = CreateContext(dbName, new FakeCurrentUser());

        var records = await db.WorkRecords.ToListAsync();

        Assert.Equal(2, records.Count);
    }

    [Fact]
    public async Task FirmUser_CannotFetchOtherFirmsContractById()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAsync(dbName);

        await using var db = CreateContext(dbName, new FakeCurrentUser { FirmId = 1 });

        var otherFirmsContract = await db.Contracts.FirstOrDefaultAsync(c => c.ContractId == 2);

        Assert.Null(otherFirmsContract);
    }

    [Fact]
    public async Task FirmUser_OnlySeesOwnFirmUsers()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAsync(dbName);

        await using var db = CreateContext(dbName, new FakeCurrentUser { FirmId = 1 });

        var users = await db.Users.ToListAsync();

        Assert.Single(users);
        Assert.All(users, u => Assert.Equal(1, u.FirmId));
    }

    [Fact]
    public async Task MipStaff_SeesAllUsers()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAsync(dbName);

        await using var db = CreateContext(dbName, new FakeCurrentUser());

        var users = await db.Users.ToListAsync();

        Assert.Equal(3, users.Count);
    }

    [Fact]
    public async Task LoginFlow_WithNullFirmId_CanQueryAllUsers()
    {
        // Login akışında kullanıcı henüz kimliksiz olduğu için FirmId claim'i yoktur.
        // CurrentUser.FirmId null döner → filtre tüm kullanıcıları geçirir.
        var dbName = Guid.NewGuid().ToString();
        await SeedAsync(dbName);

        // UserId=0 (kimliksiz), FirmId=null — login bağlamını simüle eder
        await using var db = CreateContext(dbName, new FakeCurrentUser { UserId = 0, FirmId = null });

        var user = await db.Users.SingleOrDefaultAsync(u => u.UserName == "giren.kullanici");

        Assert.NotNull(user);
        Assert.Equal("giren.kullanici", user!.UserName);
    }
}

