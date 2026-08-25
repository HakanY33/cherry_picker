using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Web.Controllers;
using MipRental.Web.Models.Locations;
using MipRental.Web.Models.Users;

namespace MipRental.Tests;

public class MasterDataAuthorizationTests
{
    private static AppDbContext CreateContext(string dbName, ICurrentUser currentUser)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options, currentUser);
    }

    private static async Task SeedTwoFirmsAsync(string dbName)
    {
        await using var db = CreateContext(dbName, new FakeCurrentUser());
        db.Firms.AddRange(
            new Firm { FirmId = 1, Code = "FIRMA-1", Title = "Firma 1", CreatedAt = DateTime.UtcNow },
            new Firm { FirmId = 2, Code = "FIRMA-2", Title = "Firma 2", CreatedAt = DateTime.UtcNow });
        db.Users.Add(new User { UserId = 1, UserName = "firma1.admin", FullName = "Firma 1 Admin", FirmId = 1, IsFirmAdmin = true, CreatedAt = DateTime.UtcNow });
        db.Users.Add(new User { UserId = 2, UserName = "firma2.kullanici", FullName = "Firma 2 Kullanıcı", FirmId = 2, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task FirmAdmin_CannotViewAnotherFirmsUser()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedTwoFirmsAsync(dbName);

        var firmAdmin = new FakeCurrentUser { UserId = 1, FirmId = 1 };
        await using var db = CreateContext(dbName, firmAdmin);
        var controller = new UsersController(db, firmAdmin, new PasswordHasher<User>());

        var result = await controller.Edit(2); // Firma 2'nin kullanıcısı, Id ile erişim denemesi

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task FirmAdmin_CannotCreateUserForAnotherFirm_EvenWithTamperedFirmIdInPost()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedTwoFirmsAsync(dbName);

        var firmAdmin = new FakeCurrentUser { UserId = 1, FirmId = 1 };
        await using var db = CreateContext(dbName, firmAdmin);
        var controller = new UsersController(db, firmAdmin, new PasswordHasher<User>())
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new NoOpTempDataProvider())
        };

        var model = new UserFormViewModel
        {
            UserName = "yeni.kullanici",
            FullName = "Yeni Kullanıcı",
            FirmId = 2 // tampered: firma admini kendi firması olmayan bir FirmId gönderiyor
        };

        await controller.Create(model);

        var createdUser = await db.Users.SingleAsync(u => u.UserName == "yeni.kullanici");
        Assert.Equal(1, createdUser.FirmId); // sunucu tarafında firma admininin kendi firmasına sabitlenmiş olmalı
    }

    [Fact]
    public async Task Location_CannotBecomeChildOfItsOwnDescendant()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateContext(dbName, new FakeCurrentUser());

        db.Locations.AddRange(
            new Location { LocationId = 1, Name = "Liman", IsActive = true },
            new Location { LocationId = 2, Name = "Rıhtım", ParentLocationId = 1, IsActive = true });
        await db.SaveChangesAsync();

        var controller = new LocationsController(db);
        var model = new LocationFormViewModel
        {
            LocationId = 1,
            Name = "Liman",
            ParentLocationId = 2 // 2, 1'in kendi alt lokasyonu -> döngü
        };

        var result = await controller.Edit(1, model);

        Assert.False(controller.ModelState.IsValid);
        Assert.IsType<ViewResult>(result);

        var unchanged = await db.Locations.AsNoTracking().SingleAsync(l => l.LocationId == 1);
        Assert.Null(unchanged.ParentLocationId);
    }

    private sealed class NoOpTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
