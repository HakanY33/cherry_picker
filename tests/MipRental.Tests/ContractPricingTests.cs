using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Web.Common;
using MipRental.Web.Controllers;
using MipRental.Web.Models.Contracts;

namespace MipRental.Tests;

public class ContractPricingTests
{
    private static AppDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options, new FakeCurrentUser());
    }

    private static async Task<(int FirmId, int ContractId, int ServiceId)> SeedContractAsync(string dbName)
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
        await db.SaveChangesAsync();
        return (1, 1, 1);
    }

    private static ContractLinesController CreateLinesController(AppDbContext db) => new(db)
    {
        TempData = new TempDataDictionary(new DefaultHttpContext(), new NoOpTempDataProvider())
    };

    private static ContractsController CreateContractsController(AppDbContext db) => new(db)
    {
        TempData = new TempDataDictionary(new DefaultHttpContext(), new NoOpTempDataProvider())
    };

    [Fact]
    public async Task UpdatePrice_ClosesOldLine_OpensNewLine_OldUnitPriceUnchanged()
    {
        var dbName = Guid.NewGuid().ToString();
        var (_, contractId, serviceId) = await SeedContractAsync(dbName);

        int oldLineId;
        await using (var db = CreateContext(dbName))
        {
            var line = new ContractLine
            {
                ContractId = contractId,
                ServiceId = serviceId,
                UnitPrice = 100m,
                Currency = "TRY",
                RoundingRule = RoundingRule.NONE,
                ValidFrom = new DateOnly(2026, 1, 1),
                ValidTo = null,
                IsActive = true
            };
            db.ContractLines.Add(line);
            await db.SaveChangesAsync();
            oldLineId = line.ContractLineId;
        }

        await using (var db = CreateContext(dbName))
        {
            var controller = CreateLinesController(db);
            var model = new ContractLineUpdatePriceViewModel
            {
                ContractLineId = oldLineId,
                NewUnitPrice = 150m,
                NewValidFrom = new DateOnly(2026, 3, 1)
            };

            var result = await controller.UpdatePrice(oldLineId, model);
            Assert.IsType<RedirectToActionResult>(result);
        }

        await using (var db = CreateContext(dbName))
        {
            var lines = await db.ContractLines.Where(l => l.ContractId == contractId).OrderBy(l => l.ValidFrom).ToListAsync();
            Assert.Equal(2, lines.Count);

            var oldLine = lines.Single(l => l.ContractLineId == oldLineId);
            Assert.Equal(100m, oldLine.UnitPrice); // eski satırın fiyatı DEĞİŞMEMİŞ
            Assert.Equal(new DateOnly(2026, 2, 28), oldLine.ValidTo); // yeni başlangıçtan bir gün önce kapanmış

            var newLine = lines.Single(l => l.ContractLineId != oldLineId);
            Assert.Equal(150m, newLine.UnitPrice);
            Assert.Equal(new DateOnly(2026, 3, 1), newLine.ValidFrom);
            Assert.Null(newLine.ValidTo);
        }
    }

    [Fact]
    public async Task Create_OverlappingDateRange_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var (_, contractId, serviceId) = await SeedContractAsync(dbName);

        await using (var db = CreateContext(dbName))
        {
            db.ContractLines.Add(new ContractLine
            {
                ContractId = contractId,
                ServiceId = serviceId,
                UnitPrice = 100m,
                Currency = "TRY",
                ValidFrom = new DateOnly(2026, 1, 1),
                ValidTo = new DateOnly(2026, 6, 30),
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(dbName))
        {
            var controller = CreateLinesController(db);
            var model = new ContractLineFormViewModel
            {
                ContractId = contractId,
                ServiceId = serviceId,
                UnitPrice = 120m,
                Currency = "TRY",
                ValidFrom = new DateOnly(2026, 6, 1), // mevcut satırla (…-06-30'a kadar) çakışıyor
                ValidTo = new DateOnly(2026, 12, 31)
            };

            var result = await controller.Create(model);

            Assert.False(controller.ModelState.IsValid);
            Assert.IsType<ViewResult>(result);

            var count = await db.ContractLines.CountAsync();
            Assert.Equal(1, count); // yeni satır eklenmedi
        }
    }

    [Fact]
    public async Task Correct_BlockedWhenWorkRecordLineExists()
    {
        var dbName = Guid.NewGuid().ToString();
        var (firmId, contractId, serviceId) = await SeedContractAsync(dbName);

        int lineId;
        await using (var db = CreateContext(dbName))
        {
            var line = new ContractLine
            {
                ContractId = contractId,
                ServiceId = serviceId,
                UnitPrice = 100m,
                Currency = "TRY",
                ValidFrom = new DateOnly(2026, 1, 1),
                ValidTo = null,
                IsActive = true
            };
            db.ContractLines.Add(line);
            db.Users.Add(new User { UserId = 1, UserName = "test.user", FullName = "Test Kullanıcı", CreatedAt = DateTime.UtcNow });
            db.Periods.Add(new Period { PeriodId = 1, Year = 2026, Month = 1, Status = PeriodStatus.OPEN });
            await db.SaveChangesAsync();
            lineId = line.ContractLineId;

            db.WorkRecords.Add(new WorkRecord
            {
                WorkRecordId = 1,
                DocumentNo = "WR-1",
                FirmId = firmId,
                ContractId = contractId,
                PeriodId = 1,
                WorkDate = new DateOnly(2026, 1, 15),
                EnteredByUserId = 1,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            db.WorkRecordLines.Add(new WorkRecordLine
            {
                WorkRecordId = 1,
                ServiceId = serviceId,
                ContractLineId = lineId,
                RawQuantity = 5,
                BillableQuantity = 5,
                Unit = ServiceUnit.HOUR,
                UnitPriceSnapshot = 100m,
                LineAmount = 500m,
                Currency = "TRY"
            });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(dbName))
        {
            var controller = CreateLinesController(db);
            var result = await controller.Correct(lineId);

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Bu fiyata bağlı çalışma kaydı var, düzeltilemez; fiyat güncellemesi yapın.", controller.TempData[TempDataKeys.ErrorMessage]);
        }

        await using (var db = CreateContext(dbName))
        {
            var unchanged = await db.ContractLines.AsNoTracking().SingleAsync(l => l.ContractLineId == lineId);
            Assert.Equal(100m, unchanged.UnitPrice);
        }
    }

    [Fact]
    public async Task PriceOnDate_ReturnsCorrectLine_IncludingBoundaryDates()
    {
        var dbName = Guid.NewGuid().ToString();
        var (_, contractId, serviceId) = await SeedContractAsync(dbName);

        await using (var db = CreateContext(dbName))
        {
            db.ContractLines.AddRange(
                new ContractLine { ContractId = contractId, ServiceId = serviceId, UnitPrice = 100m, Currency = "TRY", ValidFrom = new DateOnly(2026, 1, 1), ValidTo = new DateOnly(2026, 2, 28), IsActive = true },
                new ContractLine { ContractId = contractId, ServiceId = serviceId, UnitPrice = 150m, Currency = "TRY", ValidFrom = new DateOnly(2026, 3, 1), ValidTo = null, IsActive = true });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(dbName))
        {
            var controller = CreateContractsController(db);

            var beforeSwitch = Assert.IsType<PartialViewResult>(await controller.PriceOnDate(contractId, new DateOnly(2026, 1, 1)));
            var lines1 = Assert.IsType<PriceOnDateViewModel>(beforeSwitch.ViewData!.Model).Lines;
            Assert.Single(lines1);
            Assert.Equal(100m, lines1[0].UnitPrice);

            // ValidTo günü hâlâ eski satır geçerli olmalı (sınır dahil)
            var onValidToBoundary = Assert.IsType<PartialViewResult>(await controller.PriceOnDate(contractId, new DateOnly(2026, 2, 28)));
            var lines2 = Assert.IsType<PriceOnDateViewModel>(onValidToBoundary.ViewData!.Model).Lines;
            Assert.Single(lines2);
            Assert.Equal(100m, lines2[0].UnitPrice);

            // ValidFrom günü yeni satır geçerli olmalı (sınır dahil)
            var onNewValidFrom = Assert.IsType<PartialViewResult>(await controller.PriceOnDate(contractId, new DateOnly(2026, 3, 1)));
            var lines3 = Assert.IsType<PriceOnDateViewModel>(onNewValidFrom.ViewData!.Model).Lines;
            Assert.Single(lines3);
            Assert.Equal(150m, lines3[0].UnitPrice);
        }
    }

    [Fact]
    public async Task Activate_WithoutContractLines_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var (_, contractId, _) = await SeedContractAsync(dbName);

        await using var db = CreateContext(dbName);
        var controller = CreateContractsController(db);

        var result = await controller.Activate(contractId);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Sözleşme en az bir fiyat satırı olmadan aktifleştirilemez.", controller.TempData[TempDataKeys.ErrorMessage]);

        var contract = await db.Contracts.AsNoTracking().SingleAsync(c => c.ContractId == contractId);
        Assert.Equal(ContractStatus.DRAFT, contract.Status);
    }

    [Fact]
    public async Task Activate_WithContractLine_Succeeds()
    {
        var dbName = Guid.NewGuid().ToString();
        var (_, contractId, serviceId) = await SeedContractAsync(dbName);

        await using (var db = CreateContext(dbName))
        {
            db.ContractLines.Add(new ContractLine
            {
                ContractId = contractId,
                ServiceId = serviceId,
                UnitPrice = 100m,
                Currency = "TRY",
                ValidFrom = new DateOnly(2026, 1, 1),
                ValidTo = null,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(dbName))
        {
            var controller = CreateContractsController(db);
            var result = await controller.Activate(contractId);
            Assert.IsType<RedirectToActionResult>(result);
        }

        await using (var db = CreateContext(dbName))
        {
            var contract = await db.Contracts.AsNoTracking().SingleAsync(c => c.ContractId == contractId);
            Assert.Equal(ContractStatus.ACTIVE, contract.Status);
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
