using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Pricing;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Pricing;

namespace MipRental.Tests;

public class ContractLineResolverTests
{
    private static AppDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options, new FakeCurrentUser());
    }

    private static async Task<int> SeedFirmAndServiceAsync(AppDbContext db)
    {
        db.Firms.Add(new Firm { FirmId = 1, Code = "TESTVINC", Title = "Test Vinç A.Ş.", CreatedAt = DateTime.UtcNow });
        db.ServiceCategories.Add(new ServiceCategory { ServiceId = 1, Code = "VINC", Name = "Mobil Vinç", Unit = ServiceUnit.HOUR, IsActive = true });
        db.ServiceVariants.Add(new ServiceVariant { VariantId = 1, ServiceId = 1, Code = "60T", Name = "60 Ton Sepetli", IsActive = true });
        await db.SaveChangesAsync();
        return 1;
    }

    private static async Task<int> AddActiveContractAsync(AppDbContext db, DateOnly start, DateOnly end)
    {
        var contract = new Contract
        {
            FirmId = 1,
            ContractNo = $"SOZ-{Guid.NewGuid():N}",
            StartDate = start,
            EndDate = end,
            Currency = "TRY",
            Status = ContractStatus.ACTIVE,
            CreatedAt = DateTime.UtcNow
        };
        db.Contracts.Add(contract);
        await db.SaveChangesAsync();
        return contract.ContractId;
    }

    [Fact]
    public async Task ResolveAsync_OnValidFromDay_Matches()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateContext(dbName);
        await SeedFirmAndServiceAsync(db);
        var contractId = await AddActiveContractAsync(db, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        db.ContractLines.Add(new ContractLine
        {
            ContractId = contractId,
            ServiceId = 1,
            VariantId = 1,
            UnitPrice = 100m,
            Currency = "TRY",
            ValidFrom = new DateOnly(2026, 3, 1),
            ValidTo = new DateOnly(2026, 3, 31),
            IsActive = true
        });
        await db.SaveChangesAsync();

        var resolver = new ContractLineResolver(db);
        var line = await resolver.ResolveAsync(1, 1, 1, new DateOnly(2026, 3, 1));

        Assert.Equal(100m, line.UnitPrice);
    }

    [Fact]
    public async Task ResolveAsync_OnValidToDay_Matches()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateContext(dbName);
        await SeedFirmAndServiceAsync(db);
        var contractId = await AddActiveContractAsync(db, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        db.ContractLines.Add(new ContractLine
        {
            ContractId = contractId,
            ServiceId = 1,
            VariantId = 1,
            UnitPrice = 100m,
            Currency = "TRY",
            ValidFrom = new DateOnly(2026, 3, 1),
            ValidTo = new DateOnly(2026, 3, 31),
            IsActive = true
        });
        await db.SaveChangesAsync();

        var resolver = new ContractLineResolver(db);
        var line = await resolver.ResolveAsync(1, 1, 1, new DateOnly(2026, 3, 31));

        Assert.Equal(100m, line.UnitPrice);
    }

    [Fact]
    public async Task ResolveAsync_DayAfterValidTo_ThrowsNotFound()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateContext(dbName);
        await SeedFirmAndServiceAsync(db);
        var contractId = await AddActiveContractAsync(db, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        db.ContractLines.Add(new ContractLine
        {
            ContractId = contractId,
            ServiceId = 1,
            VariantId = 1,
            UnitPrice = 100m,
            Currency = "TRY",
            ValidFrom = new DateOnly(2026, 3, 1),
            ValidTo = new DateOnly(2026, 3, 31),
            IsActive = true
        });
        await db.SaveChangesAsync();

        var resolver = new ContractLineResolver(db);
        var ex = await Assert.ThrowsAsync<PricingException>(
            () => resolver.ResolveAsync(1, 1, 1, new DateOnly(2026, 4, 1)));

        Assert.Contains("tanımlı değil", ex.Message);
        Assert.Contains("Test Vinç A.Ş.", ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_PriceChangedOnMarch1_February28UsesOldPrice()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateContext(dbName);
        await SeedFirmAndServiceAsync(db);
        var contractId = await AddActiveContractAsync(db, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        db.ContractLines.AddRange(
            new ContractLine
            {
                ContractId = contractId,
                ServiceId = 1,
                VariantId = 1,
                UnitPrice = 100m,
                Currency = "TRY",
                ValidFrom = new DateOnly(2026, 1, 1),
                ValidTo = new DateOnly(2026, 2, 28),
                IsActive = true
            },
            new ContractLine
            {
                ContractId = contractId,
                ServiceId = 1,
                VariantId = 1,
                UnitPrice = 150m,
                Currency = "TRY",
                ValidFrom = new DateOnly(2026, 3, 1),
                ValidTo = null,
                IsActive = true
            });
        await db.SaveChangesAsync();

        var resolver = new ContractLineResolver(db);
        var line = await resolver.ResolveAsync(1, 1, 1, new DateOnly(2026, 2, 28));

        Assert.Equal(100m, line.UnitPrice); // Mart zammı Şubat işini etkilemiyor
    }

    [Fact]
    public async Task ResolveAsync_NoMatchingLine_ThrowsWithTurkishMessage()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateContext(dbName);
        await SeedFirmAndServiceAsync(db);
        await AddActiveContractAsync(db, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        // Hiç ContractLine eklenmedi.

        var resolver = new ContractLineResolver(db);
        var ex = await Assert.ThrowsAsync<PricingException>(
            () => resolver.ResolveAsync(1, 1, 1, new DateOnly(2026, 3, 1)));

        Assert.Contains("60 Ton Sepetli", ex.Message);
        Assert.Contains("tanımlı değil", ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_MultipleMatchingLines_ThrowsDataInconsistencyError()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateContext(dbName);
        await SeedFirmAndServiceAsync(db);
        var contractId = await AddActiveContractAsync(db, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        db.ContractLines.AddRange(
            new ContractLine { ContractId = contractId, ServiceId = 1, VariantId = 1, UnitPrice = 100m, Currency = "TRY", ValidFrom = new DateOnly(2026, 1, 1), ValidTo = null, IsActive = true },
            new ContractLine { ContractId = contractId, ServiceId = 1, VariantId = 1, UnitPrice = 110m, Currency = "TRY", ValidFrom = new DateOnly(2026, 1, 1), ValidTo = null, IsActive = true });
        await db.SaveChangesAsync();

        var resolver = new ContractLineResolver(db);
        var ex = await Assert.ThrowsAsync<PricingException>(
            () => resolver.ResolveAsync(1, 1, 1, new DateOnly(2026, 6, 1)));

        Assert.Contains("birden fazla", ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_ContractNotActive_Throws()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateContext(dbName);
        await SeedFirmAndServiceAsync(db);
        var contract = new Contract
        {
            FirmId = 1,
            ContractNo = "SOZ-DRAFT",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Currency = "TRY",
            Status = ContractStatus.DRAFT,
            CreatedAt = DateTime.UtcNow
        };
        db.Contracts.Add(contract);
        await db.SaveChangesAsync();
        db.ContractLines.Add(new ContractLine
        {
            ContractId = contract.ContractId,
            ServiceId = 1,
            VariantId = 1,
            UnitPrice = 100m,
            Currency = "TRY",
            ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = null,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var resolver = new ContractLineResolver(db);
        var ex = await Assert.ThrowsAsync<PricingException>(
            () => resolver.ResolveAsync(1, 1, 1, new DateOnly(2026, 6, 1)));

        Assert.Contains("aktif değil", ex.Message);
    }
}
