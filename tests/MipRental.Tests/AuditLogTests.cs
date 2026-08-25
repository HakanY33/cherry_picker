using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Interceptors;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;

namespace MipRental.Tests;

public class AuditLogTests
{
    private static AppDbContext CreateContext(string dbName, ICurrentUser currentUser)
    {
        var interceptor = new AuditSaveChangesInterceptor(currentUser, new NoOpHttpContextAccessor());
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(interceptor)
            .Options;
        return new AppDbContext(options, currentUser);
    }

    [Fact]
    public async Task UpdatingFirmTitle_LogsSingleFieldChangeWithCorrectValues()
    {
        var dbName = Guid.NewGuid().ToString();
        var currentUser = new FakeCurrentUser { UserId = 1 };

        await using (var db = CreateContext(dbName, currentUser))
        {
            db.Firms.Add(new Firm { FirmId = 1, Code = "FIRMA-A", Title = "Eski Unvan", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(dbName, currentUser))
        {
            var firm = await db.Firms.SingleAsync(f => f.FirmId == 1);
            firm.Title = "Yeni Unvan";
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(dbName, currentUser))
        {
            var updateLogs = await db.AuditLogs
                .Where(a => a.TableName == "Firms" && a.RecordId == 1 && a.Action == Domain.Enums.AuditAction.UPDATE)
                .ToListAsync();

            var entry = Assert.Single(updateLogs);
            Assert.Equal("Title", entry.FieldName);
            Assert.Equal("Eski Unvan", entry.OldValue);
            Assert.Equal("Yeni Unvan", entry.NewValue);
            Assert.Equal(1, entry.UserId);
        }
    }

    [Fact]
    public async Task PasswordHashChange_IsNeverLoggedInPlainText()
    {
        var dbName = Guid.NewGuid().ToString();
        var currentUser = new FakeCurrentUser { UserId = 1 };

        await using (var db = CreateContext(dbName, currentUser))
        {
            db.Users.Add(new User
            {
                UserId = 1,
                UserName = "test.user",
                FullName = "Test Kullanıcı",
                PasswordHash = "ESKI_GERCEK_HASH_DEGERI",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(dbName, currentUser))
        {
            var user = await db.Users.SingleAsync(u => u.UserId == 1);
            user.PasswordHash = "YENI_GERCEK_HASH_DEGERI";
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(dbName, currentUser))
        {
            var passwordLogs = await db.AuditLogs
                .Where(a => a.TableName == "Users" && a.FieldName == "PasswordHash")
                .ToListAsync();

            Assert.NotEmpty(passwordLogs);
            Assert.All(passwordLogs, log =>
            {
                Assert.DoesNotContain("GERCEK_HASH_DEGERI", log.OldValue ?? string.Empty);
                Assert.DoesNotContain("GERCEK_HASH_DEGERI", log.NewValue ?? string.Empty);
                if (log.NewValue is not null)
                {
                    Assert.Equal("***", log.NewValue);
                }
            });
        }
    }

    [Fact]
    public async Task InsertingFirm_LogsFieldsWithoutRecursingOnAuditLogItself()
    {
        var dbName = Guid.NewGuid().ToString();
        var currentUser = new FakeCurrentUser { UserId = 1 };

        await using var db = CreateContext(dbName, currentUser);
        db.Firms.Add(new Firm { FirmId = 1, Code = "FIRMA-A", Title = "Firma A", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var insertLogs = await db.AuditLogs
            .Where(a => a.TableName == "Firms" && a.RecordId == 1 && a.Action == Domain.Enums.AuditAction.INSERT)
            .ToListAsync();

        Assert.Contains(insertLogs, l => l.FieldName == "Title" && l.NewValue == "Firma A");
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), l => l.TableName == "AuditLog");
    }

    private sealed class NoOpHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
