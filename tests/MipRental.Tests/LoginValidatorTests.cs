using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Entities;
using MipRental.Web.Security;

namespace MipRental.Tests;

public class LoginValidatorTests
{
    private static async Task<AppDbContext> CreateSeededContextAsync(string dbName, bool isActive)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var db = new AppDbContext(options, new FakeCurrentUser());

        var hasher = new PasswordHasher<User>();
        db.Users.Add(new User
        {
            UserId = 1,
            UserName = "test.user",
            FullName = "Test Kullanıcı",
            PasswordHash = hasher.HashPassword(new User(), "DogruSifre!1"),
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return db;
    }

    [Fact]
    public async Task InactiveUser_CannotLogin()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = await CreateSeededContextAsync(dbName, isActive: false);
        var validator = new LoginValidator(db, new PasswordHasher<User>());

        var result = await validator.ValidateAsync("test.user", "DogruSifre!1");

        Assert.Null(result);
    }

    [Fact]
    public async Task WrongPassword_LoginFails()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = await CreateSeededContextAsync(dbName, isActive: true);
        var validator = new LoginValidator(db, new PasswordHasher<User>());

        var result = await validator.ValidateAsync("test.user", "YanlisSifre!1");

        Assert.Null(result);
    }

    [Fact]
    public async Task CorrectCredentials_LoginSucceeds()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = await CreateSeededContextAsync(dbName, isActive: true);
        var validator = new LoginValidator(db, new PasswordHasher<User>());

        var result = await validator.ValidateAsync("test.user", "DogruSifre!1");

        Assert.NotNull(result);
        Assert.Equal("test.user", result!.UserName);
    }
}
