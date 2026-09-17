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

        db.Roles.AddRange(
            new Role { RoleId = 1, Code = "FIRM_MANAGER", Name = "Firma Yetkilisi", Scope = RoleScope.EXTERNAL },
            new Role { RoleId = 2, Code = "BUDGET_MANAGER", Name = "Bütçe Yöneticisi", Scope = RoleScope.INTERNAL });

        // Her kullanıcıya rol: filtreli/filtresiz sonucun eksik mi tutarlı mı
        // döndüğü ancak rol satırı varken görünür.
        db.UserRoles.AddRange(
            new UserRole { UserId = 1, RoleId = 1 },
            new UserRole { UserId = 2, RoleId = 1 },
            new UserRole { UserId = 3, RoleId = 2 });

        db.Periods.Add(new Period { PeriodId = 1, Year = 2026, Month = 1, Status = PeriodStatus.OPEN });

        db.Contracts.AddRange(
            new Contract { ContractId = 1, FirmId = 1, ContractNo = "C-A", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), CreatedAt = DateTime.UtcNow },
            new Contract { ContractId = 2, FirmId = 2, ContractNo = "C-B", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), CreatedAt = DateTime.UtcNow });

        db.WorkRecords.AddRange(
            new WorkRecord { WorkRecordId = 1, DocumentNo = "WR-1", FirmId = 1, ContractId = 1, PeriodId = 1, WorkDate = new DateOnly(2026, 1, 10), EnteredByUserId = 1, CreatedAt = DateTime.UtcNow },
            new WorkRecord { WorkRecordId = 2, DocumentNo = "WR-2", FirmId = 2, ContractId = 2, PeriodId = 1, WorkDate = new DateOnly(2026, 1, 10), EnteredByUserId = 1, CreatedAt = DateTime.UtcNow });

        db.ProgressPayments.Add(new ProgressPayment
        {
            ProgressPaymentId = 1, PeriodId = 1, FirmId = 1, CreatedByUserId = 3, CreatedAt = DateTime.UtcNow
        });

        // Mail onayı token'ı MIP personeline (FirmId = null) kesilir.
        db.ApprovalTokens.Add(new ApprovalToken
        {
            ApprovalTokenId = 1,
            ProgressPaymentId = 1,
            IssuedToUserId = 3,
            TokenHash = [1, 2, 3],
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });

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

    // --- UserRole / ApprovalToken filtreleri (EF 10622 asimetrisi) ---

    /// <summary>
    /// Uyarının işaret ettiği asıl risk: User filtreli, UserRole filtresizken
    /// Include sessizce EKSİK sonuç döndürebilir. Beklenen, tutarlılık —
    /// görünen her kullanıcının rolleri de görünür.
    /// </summary>
    [Fact]
    public async Task FirmUser_UsersWithUserRolesInclude_IsConsistent()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAsync(dbName);

        await using var db = CreateContext(dbName, new FakeCurrentUser { FirmId = 1 });

        var users = await db.Users.Include(u => u.UserRoles).ToListAsync();

        Assert.Single(users);
        Assert.Equal(1, users[0].UserId);
        Assert.Equal([1], users[0].UserRoles.Select(ur => ur.RoleId));
    }

    [Fact]
    public async Task FirmUser_OnlySeesOwnFirmUserRoles()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAsync(dbName);

        await using var db = CreateContext(dbName, new FakeCurrentUser { FirmId = 1 });

        var userIds = await db.UserRoles.Select(ur => ur.UserId).ToListAsync();

        Assert.Equal([1], userIds);
    }

    [Fact]
    public async Task MipStaff_SeesAllUserRoles()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAsync(dbName);

        await using var db = CreateContext(dbName, new FakeCurrentUser());

        Assert.Equal(3, await db.UserRoles.CountAsync());
    }

    /// <summary>
    /// Login akışı: kullanıcı henüz kimliksiz, FirmId claim'i yok. Rol kodları
    /// AccountController'da UserRoles üzerinden okunur — filtre geçirgen
    /// olmasaydı firma kullanıcısı rolsüz giriş yapardı.
    /// </summary>
    [Fact]
    public async Task LoginFlow_WithNullFirmId_CanReadFirmUsersRoles()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAsync(dbName);

        await using var db = CreateContext(dbName, new FakeCurrentUser { UserId = 0, FirmId = null });

        var roleCodes = await db.UserRoles
            .Where(ur => ur.UserId == 1)
            .Select(ur => ur.Role.Code)
            .ToListAsync();

        Assert.Equal(["FIRM_MANAGER"], roleCodes);
    }

    /// <summary>
    /// Oturum açmış firma kullanıcısı kendi rollerini okuyabilmeye devam eder
    /// (ApprovalService.GetActorAsync bunu her onay kararında yapar).
    /// </summary>
    [Fact]
    public async Task FirmUser_CanReadOwnRoles()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAsync(dbName);

        await using var db = CreateContext(dbName, new FakeCurrentUser { UserId = 1, FirmId = 1 });

        var roleCodes = await db.UserRoles
            .Where(ur => ur.UserId == 1)
            .Select(ur => ur.Role.Code)
            .ToListAsync();

        Assert.Equal(["FIRM_MANAGER"], roleCodes);
    }

    /// <summary>
    /// Mail onayı oturumsuzdur (ADR-015): FirmId null → token bulunabilir.
    /// Filtre burada kapanırsa mailden onay akışı tümden ölür.
    /// </summary>
    [Fact]
    public async Task MailApproval_WithoutSession_CanResolveToken()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAsync(dbName);

        await using var db = CreateContext(dbName, new FakeCurrentUser { UserId = 0, FirmId = null });

        var token = await db.ApprovalTokens.FirstOrDefaultAsync(t => t.TokenHash == new byte[] { 1, 2, 3 });

        Assert.NotNull(token);
    }
}

