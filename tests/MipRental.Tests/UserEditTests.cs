using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Web.Controllers;
using MipRental.Web.Models.Users;

namespace MipRental.Tests;

/// <summary>
/// Kullanıcı düzenleme ekranı.
///
/// Buradaki asıl regresyon: kullanıcı adı ekranda DEĞİŞTİRİLEMEZ olduğu için form
/// onu POST ETMEZ, değeri veritabanından okunur. Ama model bağlama forma bakıp
/// [Required] doğrulamasını çoktan yapmış ve ModelState'e hata yazmıştır. Controller
/// alanı sonradan atasa bile bu hata kalır ve ekran HİÇBİR ZAMAN kaydedilemezdi —
/// rol değişikliği dahil hiçbir düzenleme kaydedilemiyordu.
/// </summary>
public class UserEditTests
{
    private const int BudgetRoleId = 1;
    private const int AdminRoleId = 2;

    private static AppDbContext CreateContext(string dbName, ICurrentUser currentUser)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options, currentUser);
    }

    /// <summary>BÜTÇE + ADMIN rollerine sahip bir MIP kullanıcısı.</summary>
    private static async Task SeedAdminWithTwoRolesAsync(string dbName)
    {
        await using var db = CreateContext(dbName, new FakeCurrentUser());

        db.Roles.AddRange(
            new Role { RoleId = BudgetRoleId, Code = "BUDGET", Name = "Bütçe", Scope = RoleScope.INTERNAL },
            new Role { RoleId = AdminRoleId, Code = "ADMIN", Name = "Sistem Yöneticisi", Scope = RoleScope.INTERNAL });

        db.Users.Add(new User
        {
            UserId = 1,
            UserName = "admin",
            FullName = "Sistem Yöneticisi",
            FirmId = null,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        db.UserRoles.AddRange(
            new UserRole { UserId = 1, RoleId = BudgetRoleId },
            new UserRole { UserId = 1, RoleId = AdminRoleId });

        await db.SaveChangesAsync();
    }

    private static UsersController CreateController(AppDbContext db, ICurrentUser currentUser) =>
        new(db, currentUser, new PasswordHasher<User>())
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new ApprovalTestFactory.NoOpTempDataProvider())
        };

    /// <summary>
    /// Gerçek POST'un taklidi: form kullanıcı adını GÖNDERMEZ, bu yüzden model
    /// bağlama [Required] hatasını ModelState'e yazmış olur. Controller bu hatayı
    /// düşürüp kaydı yazabilmelidir.
    /// </summary>
    private static UserFormViewModel BuildPostedModel(params int[] selectedRoleIds)
    {
        return new UserFormViewModel
        {
            UserId = 1,
            UserName = string.Empty,   // form bu alanı göndermez
            FullName = "Sistem Yöneticisi",
            IsActive = true,
            SelectedRoleIds = selectedRoleIds.ToList()
        };
    }

    private static void SimulateMissingUserNameBindingError(UsersController controller) =>
        controller.ModelState.AddModelError(nameof(UserFormViewModel.UserName), "Kullanıcı adı zorunludur.");

    [Fact]
    public async Task Edit_SavesEvenThoughFormDoesNotPostUserName()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAdminWithTwoRolesAsync(dbName);

        var currentUser = new FakeCurrentUser { UserId = 1 };
        await using var db = CreateContext(dbName, currentUser);
        var controller = CreateController(db, currentUser);
        SimulateMissingUserNameBindingError(controller);

        var result = await controller.Edit(1, BuildPostedModel(AdminRoleId));

        // Kaydedilebilmeli: doğrulama hatasıyla form geri gösterilmemeli.
        Assert.IsType<RedirectToActionResult>(result);
    }

    /// <summary>
    /// Görevin somut hâli: admin kullanıcısından BÜTÇE rolü kaldırılır,
    /// ADMIN rolü kalır.
    /// </summary>
    [Fact]
    public async Task Edit_RemovesUncheckedRoleAndKeepsCheckedOne()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAdminWithTwoRolesAsync(dbName);

        var currentUser = new FakeCurrentUser { UserId = 1 };
        await using var db = CreateContext(dbName, currentUser);
        var controller = CreateController(db, currentUser);
        SimulateMissingUserNameBindingError(controller);

        await controller.Edit(1, BuildPostedModel(AdminRoleId));

        var roleIds = await db.UserRoles
            .Where(ur => ur.UserId == 1)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        Assert.Equal([AdminRoleId], roleIds);
    }

    /// <summary>
    /// Kullanıcı adı formdan gelmediği için veritabanındaki değer KORUNUR —
    /// boş string ile ezilmemeli.
    /// </summary>
    [Fact]
    public async Task Edit_KeepsUserNameFromDatabase()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAdminWithTwoRolesAsync(dbName);

        var currentUser = new FakeCurrentUser { UserId = 1 };
        await using var db = CreateContext(dbName, currentUser);
        var controller = CreateController(db, currentUser);
        SimulateMissingUserNameBindingError(controller);

        await controller.Edit(1, BuildPostedModel(AdminRoleId));

        var user = await db.Users.SingleAsync(u => u.UserId == 1);
        Assert.Equal("admin", user.UserName);
    }

    /// <summary>
    /// Kullanıcı adı hatası düşürülürken GERÇEK doğrulama hataları düşürülmemeli;
    /// ad soyad boşsa kayıt yine reddedilir.
    /// </summary>
    [Fact]
    public async Task Edit_StillRejectsGenuineValidationErrors()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAdminWithTwoRolesAsync(dbName);

        var currentUser = new FakeCurrentUser { UserId = 1 };
        await using var db = CreateContext(dbName, currentUser);
        var controller = CreateController(db, currentUser);
        SimulateMissingUserNameBindingError(controller);
        controller.ModelState.AddModelError(nameof(UserFormViewModel.FullName), "Ad soyad zorunludur.");

        var model = BuildPostedModel(AdminRoleId);
        model.FullName = string.Empty;

        var result = await controller.Edit(1, model);

        Assert.IsType<ViewResult>(result);

        // Roller değişmemeli — kayıt reddedildi.
        var roleIds = await db.UserRoles.Where(ur => ur.UserId == 1).Select(ur => ur.RoleId).ToListAsync();
        Assert.Equal(2, roleIds.Count);
    }
}
