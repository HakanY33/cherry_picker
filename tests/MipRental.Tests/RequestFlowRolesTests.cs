using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MipRental.Data;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Security;
using MipRental.Web.Security;

namespace MipRental.Tests;

/// <summary>
/// ADIM 10 — rol yeniden adlandırması ve talep tarafının veri seviyesi.
///
/// Yeniden adlandırmanın can alıcı noktası: RoleId DEĞİŞMEDİ. UserRoles ve
/// ApprovalFlowSteps satırları RoleId ile bağlı olduğundan mevcut atamalar
/// taşınmadan korunur. Bu testler tam olarak bunu kanıtlar.
/// </summary>
public class RequestFlowRolesTests
{
    private const int EquipmentManagerRoleId = 2;   // eski SUPERVISOR
    private const int BudgetManagerRoleId = 3;      // eski DEPT_HEAD
    private const int FirmUserRoleId = 6;

    private static AppDbContext CreateContext(SqliteConnection connection, ICurrentUser user) =>
        new SqliteTestContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options,
            user);

    private static async Task<SqliteConnection> CreateSeededConnectionAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var db = CreateContext(connection, new FakeCurrentUser());
        await db.Database.EnsureCreatedAsync();
        return connection;
    }

    // ---------------------------------------------------------------
    // 1) Seed: kodlar yeni, RoleId'ler eski
    // ---------------------------------------------------------------

    [Fact]
    public async Task RoleRename_KeepsRoleIds()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await using var db = CreateContext(connection, new FakeCurrentUser());

        var byId = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.RoleId);

        Assert.Equal(RoleCodes.EquipmentManager, byId[EquipmentManagerRoleId].Code);
        Assert.Equal("Ekipman Müdürlüğü Yöneticisi", byId[EquipmentManagerRoleId].Name);

        Assert.Equal(RoleCodes.BudgetManager, byId[BudgetManagerRoleId].Code);
        Assert.Equal("Bütçe Yöneticisi", byId[BudgetManagerRoleId].Name);

        // Eski kodlar artık HİÇBİR satırda yok.
        Assert.DoesNotContain(byId.Values, r => r.Code == "SUPERVISOR");
        Assert.DoesNotContain(byId.Values, r => r.Code == "DEPT_HEAD");
    }

    [Fact]
    public async Task NewRoles_AreSeededWithCorrectScope()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await using var db = CreateContext(connection, new FakeCurrentUser());

        var byCode = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Code);

        Assert.Equal(RoleScope.INTERNAL, byCode[RoleCodes.EquipmentViewer].Scope);
        Assert.Equal("Ekipman Müdürlüğü Kullanıcısı", byCode[RoleCodes.EquipmentViewer].Name);

        Assert.Equal(RoleScope.EXTERNAL, byCode[RoleCodes.FirmManager].Scope);
        Assert.Equal("Firma Yetkilisi", byCode[RoleCodes.FirmManager].Name);

        Assert.Equal(RoleScope.EXTERNAL, byCode[RoleCodes.FirmOperator].Scope);
        Assert.Equal("Firma Operatörü", byCode[RoleCodes.FirmOperator].Name);
    }

    /// <summary>FIRM_USER kaldırılmadı: geçiş için duruyor.</summary>
    [Fact]
    public async Task LegacyFirmUserRole_StillExists()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await using var db = CreateContext(connection, new FakeCurrentUser());

        var firmUser = await db.Roles.AsNoTracking().SingleAsync(r => r.RoleId == FirmUserRoleId);

        Assert.Equal(RoleCodes.FirmUser, firmUser.Code);
    }

    // ---------------------------------------------------------------
    // 2) Mevcut kullanıcı atamaları korunuyor
    // ---------------------------------------------------------------

    /// <summary>
    /// Adım 10 ÖNCESİ SUPERVISOR rolüne atanmış bir kullanıcı: UserRoles satırı
    /// (UserId, RoleId = 2) hiç dokunulmadan durur ve artık EQUIPMENT_MANAGER
    /// olarak okunur. Rol ataması "taşınmadı" — taşınmasına gerek kalmadı.
    /// </summary>
    [Fact]
    public async Task ExistingRoleAssignment_SurvivesRename()
    {
        await using var connection = await CreateSeededConnectionAsync();

        await using (var seed = CreateContext(connection, new FakeCurrentUser()))
        {
            seed.Users.Add(new User
            {
                UserId = 100,
                UserName = "eski.amir",
                FullName = "Adım 10 Öncesi Atanmış Kullanıcı",
                CreatedAt = DateTime.UtcNow
            });
            // Atama RoleId ile yapılır — rol KODU değişse bile bu satır değişmez.
            seed.UserRoles.Add(new UserRole { UserId = 100, RoleId = EquipmentManagerRoleId });
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext(connection, new FakeCurrentUser());

        var codes = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == 100)
            .Select(ur => ur.Role.Code)
            .ToListAsync();

        Assert.Equal(new[] { RoleCodes.EquipmentManager }, codes);
    }

    /// <summary>
    /// Yetkinin gerçekten korunduğu: eski atamadan gelen rol kodu CanApprove
    /// politikasını hâlâ geçiyor.
    /// </summary>
    [Fact]
    public async Task ExistingApprover_KeepsApprovalPermission()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await using var db = CreateContext(connection, new FakeCurrentUser());

        var roleCode = await db.Roles.AsNoTracking()
            .Where(r => r.RoleId == EquipmentManagerRoleId)
            .Select(r => r.Code)
            .SingleAsync();

        var auth = BuildAuthorizationService();

        Assert.True((await auth.AuthorizeAsync(Principal(null, roleCode), null, PolicyNames.CanApprove)).Succeeded);
    }

    [Fact]
    public async Task ApprovalFlowSteps_StillPointAtTheRenamedRoles()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await using var db = CreateContext(connection, new FakeCurrentUser());

        var stepRoleCodes = await db.ApprovalFlowSteps.AsNoTracking()
            .OrderBy(s => s.StepNo)
            .Select(s => s.Role.Code)
            .ToListAsync();

        // Zincirin kendisi değişmedi; yalnızca adımların rol KODLARI yeni adları taşıyor.
        Assert.Equal(new[] { RoleCodes.EquipmentManager, RoleCodes.BudgetManager }, stepRoleCodes);
    }

    // ---------------------------------------------------------------
    // 3) Policy'ler: yeni roller doğru tarafta
    // ---------------------------------------------------------------

    private static IAuthorizationService BuildAuthorizationService() =>
        new ServiceCollection()
            .AddLogging()
            .AddAuthorization(AuthorizationPolicies.AddAppPolicies)
            .BuildServiceProvider()
            .GetRequiredService<IAuthorizationService>();

    private static ClaimsPrincipal Principal(int? firmId, params string[] roles)
    {
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r)).ToList();
        if (firmId is int id)
        {
            claims.Add(new Claim(AppClaimTypes.FirmId, id.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test", ClaimTypes.Name, ClaimTypes.Role));
    }

    /// <summary>
    /// ADIM 9 KURALI YENİ ROLDE DE GEÇERLİ: Ekipman Müdürlüğü onaylar ama
    /// parayı GÖRMEZ. Rol adı değişti, kural değişmedi.
    /// </summary>
    [Fact]
    public async Task EquipmentManager_CanApprove_ButCannotSeePricing()
    {
        var auth = BuildAuthorizationService();
        var actor = Principal(null, RoleNames.EquipmentManager);

        Assert.True((await auth.AuthorizeAsync(actor, null, PolicyNames.CanApprove)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(actor, null, PolicyNames.CanSeePricing)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(actor, null, PolicyNames.CanManageContract)).Succeeded);

        // Servis katmanındaki bayrak da aynı cevabı vermeli (Adım 9: iki kaynak tek liste).
        Assert.False(new FakeCurrentUser { UserId = 1, Roles = { RoleNames.EquipmentManager } }.CanSeePricing);
    }

    /// <summary>EQUIPMENT_VIEWER salt okur: ne onaylar ne fiyat görür.</summary>
    [Fact]
    public async Task EquipmentViewer_CanNeitherApproveNorSeePricing()
    {
        var auth = BuildAuthorizationService();
        var actor = Principal(null, RoleNames.EquipmentViewer);

        Assert.False((await auth.AuthorizeAsync(actor, null, PolicyNames.CanApprove)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(actor, null, PolicyNames.CanSeePricing)).Succeeded);
        Assert.False(new FakeCurrentUser { UserId = 1, Roles = { RoleNames.EquipmentViewer } }.CanSeePricing);
    }

    [Fact]
    public async Task BudgetManager_KeepsApprovalAndPricing()
    {
        var auth = BuildAuthorizationService();
        var actor = Principal(null, RoleNames.BudgetManager);

        Assert.True((await auth.AuthorizeAsync(actor, null, PolicyNames.CanApprove)).Succeeded);
        Assert.True((await auth.AuthorizeAsync(actor, null, PolicyNames.CanSeePricing)).Succeeded);
        Assert.True(new FakeCurrentUser { UserId = 1, Roles = { RoleNames.BudgetManager } }.CanSeePricing);
    }

    [Theory]
    [InlineData(RoleNames.FirmManager)]
    [InlineData(RoleNames.FirmOperator)]
    [InlineData(RoleNames.Requester)]
    public async Task NewFlowRoles_CannotSeePricing(string role)
    {
        var auth = BuildAuthorizationService();

        Assert.False((await auth.AuthorizeAsync(Principal(null, role), null, PolicyNames.CanSeePricing)).Succeeded);
        Assert.False(new FakeCurrentUser { UserId = 1, Roles = { role } }.CanSeePricing);
    }

    // ---------------------------------------------------------------
    // 4) Firma izolasyonu: talepler (CLAUDE.md kural 7)
    // ---------------------------------------------------------------

    private static async Task SeedTwoFirmsRequestsAsync(SqliteConnection connection)
    {
        await using var db = CreateContext(connection, new FakeCurrentUser());

        db.Firms.AddRange(
            new Firm { FirmId = 1, Code = "FIRMA-A", Title = "Firma A", CreatedAt = DateTime.UtcNow },
            new Firm { FirmId = 2, Code = "FIRMA-B", Title = "Firma B", CreatedAt = DateTime.UtcNow });
        db.Departments.Add(new Department { DepartmentId = 1, Code = "OPS", Name = "Operasyon" });
        db.Users.Add(new User { UserId = 1, UserName = "talep.eden", FullName = "Talep Eden", CreatedAt = DateTime.UtcNow });

        db.Requests.AddRange(
            new Request
            {
                RequestId = 1, DocumentNo = "CPR-2026-00001", Status = RequestStatus.PENDING_FIRM,
                RequestedByUserId = 1, DepartmentId = 1, FirmId = 1,
                IssueDate = new DateOnly(2026, 3, 1), RequestedDate = new DateOnly(2026, 3, 10),
                CreatedAt = DateTime.UtcNow
            },
            new Request
            {
                RequestId = 2, DocumentNo = "CPR-2026-00002", Status = RequestStatus.PENDING_FIRM,
                RequestedByUserId = 1, DepartmentId = 1, FirmId = 2,
                IssueDate = new DateOnly(2026, 3, 1), RequestedDate = new DateOnly(2026, 3, 10),
                CreatedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task FirmUser_OnlySeesOwnFirmRequests()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await SeedTwoFirmsRequestsAsync(connection);

        await using var db = CreateContext(connection, new FakeCurrentUser { UserId = 5, FirmId = 1 });

        var requests = await db.Requests.AsNoTracking().ToListAsync();

        Assert.Single(requests);
        Assert.Equal(1, requests[0].FirmId);
    }

    /// <summary>
    /// Id'yi elle yazmak da işe yaramaz: filtre sorgunun kendisinde, ekranda değil.
    /// </summary>
    [Fact]
    public async Task FirmUser_CannotFetchOtherFirmsRequestById()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await SeedTwoFirmsRequestsAsync(connection);

        await using var db = CreateContext(connection, new FakeCurrentUser { UserId = 5, FirmId = 1 });

        Assert.Null(await db.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.RequestId == 2));
        Assert.NotNull(await db.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.RequestId == 1));
    }

    [Fact]
    public async Task MipStaff_SeesAllFirmsRequests()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await SeedTwoFirmsRequestsAsync(connection);

        await using var db = CreateContext(connection, new FakeCurrentUser { UserId = 5, FirmId = null });

        Assert.Equal(2, await db.Requests.AsNoTracking().CountAsync());
    }

    /// <summary>
    /// Yeni durumlar veritabanına METİN olarak yazılıyor (ADR-009): SSMS'te
    /// "PENDING_FIRM" görünür, sayı değil.
    /// </summary>
    [Fact]
    public async Task RequestStatus_IsPersistedAsString()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await SeedTwoFirmsRequestsAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status FROM Requests WHERE RequestId = 1";
        var stored = (string?)await command.ExecuteScalarAsync();

        Assert.Equal("PENDING_FIRM", stored);
    }
}
