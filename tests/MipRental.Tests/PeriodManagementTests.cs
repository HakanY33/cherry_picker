using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MipRental.Data;
using MipRental.Data.Services;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Web.Controllers;
using MipRental.Web.Models.Periods;
using MipRental.Web.Security;

namespace MipRental.Tests;

public class PeriodManagementTests
{
    private static AppDbContext CreateContext(string dbName, FakeCurrentUser currentUser)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options, currentUser);
    }

    private static PeriodsController CreateController(AppDbContext db, FakeCurrentUser currentUser) =>
        new(db, currentUser, new PeriodLockService(db))
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new NoOpTempDataProvider())
        };

    [Fact]
    public async Task Reopen_WithoutReason_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var budgetUser = new FakeCurrentUser { UserId = 1, Roles = { RoleNames.Budget } };

        await using (var db = CreateContext(dbName, budgetUser))
        {
            db.Periods.Add(new Period { PeriodId = 1, Year = 2026, Month = 1, Status = PeriodStatus.CLOSED, ClosedAt = DateTime.UtcNow, ClosedBy = 1 });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(dbName, budgetUser))
        {
            var controller = CreateController(db, budgetUser);
            var model = new PeriodReopenViewModel { PeriodId = 1, ReopenReason = "" };

            var result = await controller.Reopen(1, model);

            Assert.False(controller.ModelState.IsValid);
            Assert.IsType<ViewResult>(result);
        }

        await using (var db = CreateContext(dbName, budgetUser))
        {
            var period = await db.Periods.AsNoTracking().SingleAsync(p => p.PeriodId == 1);
            Assert.Equal(PeriodStatus.CLOSED, period.Status);
            Assert.Null(period.ReopenReason);
        }
    }

    [Fact]
    public async Task Reopen_WithReason_Succeeds()
    {
        var dbName = Guid.NewGuid().ToString();
        var budgetUser = new FakeCurrentUser { UserId = 1, Roles = { RoleNames.Budget } };

        await using (var db = CreateContext(dbName, budgetUser))
        {
            db.Periods.Add(new Period { PeriodId = 1, Year = 2026, Month = 1, Status = PeriodStatus.CLOSED, ClosedAt = DateTime.UtcNow, ClosedBy = 1 });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(dbName, budgetUser))
        {
            var controller = CreateController(db, budgetUser);
            var model = new PeriodReopenViewModel { PeriodId = 1, ReopenReason = "Geriye dönük düzeltme gerekiyor." };

            var result = await controller.Reopen(1, model);
            Assert.IsType<RedirectToActionResult>(result);
        }

        await using (var db = CreateContext(dbName, budgetUser))
        {
            var period = await db.Periods.AsNoTracking().SingleAsync(p => p.PeriodId == 1);
            Assert.Equal(PeriodStatus.REOPENED, period.Status);
            Assert.Equal("Geriye dönük düzeltme gerekiyor.", period.ReopenReason);
            Assert.Equal(1, period.ReopenedBy);
        }
    }

    [Fact]
    public async Task GenerateNextYear_CreatesTwelveOpenPeriods()
    {
        var dbName = Guid.NewGuid().ToString();
        var budgetUser = new FakeCurrentUser { UserId = 1, Roles = { RoleNames.Budget } };

        await using (var db = CreateContext(dbName, budgetUser))
        {
            db.Periods.Add(new Period { PeriodId = 1, Year = 2026, Month = 1, Status = PeriodStatus.OPEN });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(dbName, budgetUser))
        {
            var controller = CreateController(db, budgetUser);
            var result = await controller.GenerateNextYear();
            Assert.IsType<RedirectToActionResult>(result);
        }

        await using (var db = CreateContext(dbName, budgetUser))
        {
            var nextYearPeriods = await db.Periods.Where(p => p.Year == 2027).ToListAsync();
            Assert.Equal(12, nextYearPeriods.Count);
            Assert.All(nextYearPeriods, p => Assert.Equal(PeriodStatus.OPEN, p.Status));
        }
    }

    /// <summary>
    /// PeriodsController'ın [Authorize(Policy = CanClosePeriod)] ile koruduğu erişimi,
    /// Program.cs'teki gerçek politika kurulumunu (RequireRole(BUDGET)) çoğaltarak doğrular.
    /// Controller metotları doğrudan çağrıldığında [Authorize] filtresi devreye girmez;
    /// bu yüzden asıl korumayı sağlayan AuthorizationPolicy burada ayrıca test edilir.
    /// </summary>
    [Fact]
    public async Task NonBudgetRole_CannotAuthorizeForClosePeriodPolicy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(PolicyNames.CanClosePeriod, policy => policy.RequireRole(RoleNames.Budget));
        });
        await using var provider = services.BuildServiceProvider();
        var authService = provider.GetRequiredService<IAuthorizationService>();

        var nonBudgetUser = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, RoleNames.Admin) }, "TestAuth"));
        var budgetUser = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, RoleNames.Budget) }, "TestAuth"));

        var nonBudgetResult = await authService.AuthorizeAsync(nonBudgetUser, PolicyNames.CanClosePeriod);
        var budgetResult = await authService.AuthorizeAsync(budgetUser, PolicyNames.CanClosePeriod);

        Assert.False(nonBudgetResult.Succeeded);
        Assert.True(budgetResult.Succeeded);
    }

    private sealed class NoOpTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
