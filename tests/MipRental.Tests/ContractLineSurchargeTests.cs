using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Web.Controllers;
using MipRental.Web.Models.Contracts;

namespace MipRental.Tests;

public class ContractLineSurchargeTests
{
    private static AppDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options, new FakeCurrentUser());
    }

    private static ContractLineSurchargesController CreateController(AppDbContext db) => new(db)
    {
        TempData = new TempDataDictionary(new DefaultHttpContext(), new NoOpTempDataProvider())
    };

    private static async Task<int> SeedContractLineAsync(string dbName)
    {
        await using var db = CreateContext(dbName);
        db.Firms.Add(new Firm { FirmId = 1, Code = "FIRMA-1", Title = "Firma 1", CreatedAt = DateTime.UtcNow });
        db.ServiceCategories.Add(new ServiceCategory { ServiceId = 1, Code = "VINC", Name = "Mobil Vinç", Unit = ServiceUnit.HOUR, IsActive = true });
        db.Contracts.Add(new Contract
        {
            ContractId = 1,
            FirmId = 1,
            ContractNo = "SOZ-1",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Currency = "TRY",
            Status = ContractStatus.DRAFT,
            CreatedAt = DateTime.UtcNow
        });
        var line = new ContractLine
        {
            ContractId = 1,
            ServiceId = 1,
            UnitPrice = 100m,
            Currency = "TRY",
            ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = null,
            IsActive = true
        };
        db.ContractLines.Add(line);
        await db.SaveChangesAsync();
        return line.ContractLineId;
    }

    [Fact]
    public async Task Create_WithoutMultiplierOrFixedAmount_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var lineId = await SeedContractLineAsync(dbName);

        await using var db = CreateContext(dbName);
        var controller = CreateController(db);
        var model = new ContractLineSurchargeFormViewModel
        {
            ContractLineId = lineId,
            SurchargeType = SurchargeType.NIGHT
        };

        var result = await controller.Create(model);

        Assert.False(controller.ModelState.IsValid);
        Assert.IsType<ViewResult>(result);
        Assert.Equal(0, await db.ContractLineSurcharges.CountAsync());
    }

    [Fact]
    public async Task Create_WithMultiplier_Succeeds()
    {
        var dbName = Guid.NewGuid().ToString();
        var lineId = await SeedContractLineAsync(dbName);

        await using (var db = CreateContext(dbName))
        {
            var controller = CreateController(db);
            var model = new ContractLineSurchargeFormViewModel
            {
                ContractLineId = lineId,
                SurchargeType = SurchargeType.OVERTIME,
                Multiplier = 1.5m
            };

            var result = await controller.Create(model);
            Assert.IsType<RedirectToActionResult>(result);
        }

        await using (var db = CreateContext(dbName))
        {
            var surcharge = await db.ContractLineSurcharges.SingleAsync();
            Assert.Equal(lineId, surcharge.ContractLineId);
            Assert.Equal(SurchargeType.OVERTIME, surcharge.SurchargeType);
            Assert.Equal(1.5m, surcharge.Multiplier);
        }
    }

    [Fact]
    public async Task Edit_UpdatesExistingSurcharge()
    {
        var dbName = Guid.NewGuid().ToString();
        var lineId = await SeedContractLineAsync(dbName);

        int surchargeId;
        await using (var db = CreateContext(dbName))
        {
            var surcharge = new ContractLineSurcharge
            {
                ContractLineId = lineId,
                SurchargeType = SurchargeType.WEEKEND,
                Multiplier = 1.25m,
                IsActive = true
            };
            db.ContractLineSurcharges.Add(surcharge);
            await db.SaveChangesAsync();
            surchargeId = surcharge.SurchargeId;
        }

        await using (var db = CreateContext(dbName))
        {
            var controller = CreateController(db);
            var model = new ContractLineSurchargeFormViewModel
            {
                SurchargeId = surchargeId,
                ContractLineId = lineId,
                SurchargeType = SurchargeType.WEEKEND,
                Multiplier = 1.75m,
                IsActive = false
            };

            var result = await controller.Edit(surchargeId, model);
            Assert.IsType<RedirectToActionResult>(result);
        }

        await using (var db = CreateContext(dbName))
        {
            var updated = await db.ContractLineSurcharges.AsNoTracking().SingleAsync(s => s.SurchargeId == surchargeId);
            Assert.Equal(1.75m, updated.Multiplier);
            Assert.False(updated.IsActive);
        }
    }

    private sealed class NoOpTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
