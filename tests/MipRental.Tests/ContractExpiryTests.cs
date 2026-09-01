using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Web.Controllers;

namespace MipRental.Tests;

/// <summary>
/// Sözleşme "süresi doldu" (EXPIRED) kuralı.
///
/// Bulunan hata: bitiş tarihi 31.12.2026 olan bir sözleşme, 25.08.2026'da
/// "Süresi Doldu" rozetiyle görünüyordu. Otomatik bir süre dolma mantığı YOK;
/// durum elle işaretlenmişti — ve Expire eylemi bitiş tarihine HİÇ bakmıyordu.
///
/// Süre dolması bir KARAR değil, takvimin sonucudur. Sözleşmeyi vaktinden önce
/// bitirmenin yolu FESHET'tir.
/// </summary>
public class ContractExpiryTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static AppDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options, new FakeCurrentUser());
    }

    private static ContractsController CreateController(AppDbContext db) =>
        new(db)
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new ApprovalTestFactory.NoOpTempDataProvider())
        };

    private static async Task<int> SeedContractAsync(string dbName, ContractStatus status, DateOnly endDate, bool withLine = true)
    {
        await using var db = CreateContext(dbName);

        db.Firms.Add(new Firm { FirmId = 1, Code = "FIRMA-1", Title = "Test Vinç Ltd. Şti.", CreatedAt = DateTime.UtcNow });
        db.ServiceCategories.Add(new ServiceCategory { ServiceId = 1, Code = "MOBIL-VINC", Name = "Mobil Vinç", Unit = ServiceUnit.HOUR, IsActive = true });

        var contract = new Contract
        {
            ContractId = 1,
            FirmId = 1,
            ContractNo = "SOZ-2026-001",
            Title = "Mobil Vinç Kiralama Sözleşmesi",
            StartDate = endDate.AddYears(-1),
            EndDate = endDate,
            Currency = "TRY",
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
        db.Contracts.Add(contract);

        if (withLine)
        {
            db.ContractLines.Add(new ContractLine
            {
                ContractLineId = 1,
                ContractId = 1,
                ServiceId = 1,
                UnitPrice = 650m,
                Currency = "TRY",
                RoundingRule = RoundingRule.NONE,
                ValidFrom = contract.StartDate
            });
        }

        await db.SaveChangesAsync();
        return contract.ContractId;
    }

    private static async Task<ContractStatus> StatusOfAsync(string dbName)
    {
        await using var db = CreateContext(dbName);
        return (await db.Contracts.SingleAsync()).Status;
    }

    /// <summary>
    /// Asıl regresyon: bitiş tarihi GELECEKTE olan aktif sözleşme
    /// "süresi doldu" olarak işaretlenemez.
    /// </summary>
    [Fact]
    public async Task Expire_IsRejected_WhenEndDateHasNotPassed()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedContractAsync(dbName, ContractStatus.ACTIVE, Today.AddMonths(4));

        await using var db = CreateContext(dbName);
        await CreateController(db).Expire(1);

        Assert.Equal(ContractStatus.ACTIVE, await StatusOfAsync(dbName));
    }

    /// <summary>Bitiş tarihi BUGÜN olan sözleşmenin süresi henüz dolmamıştır.</summary>
    [Fact]
    public async Task Expire_IsRejected_OnTheEndDateItself()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedContractAsync(dbName, ContractStatus.ACTIVE, Today);

        await using var db = CreateContext(dbName);
        await CreateController(db).Expire(1);

        Assert.Equal(ContractStatus.ACTIVE, await StatusOfAsync(dbName));
    }

    /// <summary>Bitiş tarihi GEÇMİŞSE işaretlenebilir — kuralın asıl amacı bu.</summary>
    [Fact]
    public async Task Expire_Succeeds_WhenEndDateHasPassed()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedContractAsync(dbName, ContractStatus.ACTIVE, Today.AddDays(-1));

        await using var db = CreateContext(dbName);
        await CreateController(db).Expire(1);

        Assert.Equal(ContractStatus.EXPIRED, await StatusOfAsync(dbName));
    }

    /// <summary>Erken sonlandırma yolu kapanmadı: Feshet tarihe bakmaz.</summary>
    [Fact]
    public async Task Terminate_StillWorks_WhileContractIsStillValid()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedContractAsync(dbName, ContractStatus.ACTIVE, Today.AddMonths(4));

        await using var db = CreateContext(dbName);
        await CreateController(db).Terminate(1);

        Assert.Equal(ContractStatus.TERMINATED, await StatusOfAsync(dbName));
    }

    /// <summary>
    /// Onarım yolu: eskiden yanlışlıkla EXPIRED yapılmış ama süresi HENÜZ dolmamış
    /// sözleşme yeniden aktifleştirilebilir.
    /// </summary>
    [Fact]
    public async Task Activate_RepairsWronglyExpiredContract()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedContractAsync(dbName, ContractStatus.EXPIRED, Today.AddMonths(4));

        await using var db = CreateContext(dbName);
        await CreateController(db).Activate(1);

        Assert.Equal(ContractStatus.ACTIVE, await StatusOfAsync(dbName));
    }

    /// <summary>
    /// Onarım yolu bir arka kapı değil: süresi GERÇEKTEN dolmuş sözleşme
    /// yeniden aktifleştirilemez.
    /// </summary>
    [Fact]
    public async Task Activate_IsRejected_ForGenuinelyExpiredContract()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedContractAsync(dbName, ContractStatus.EXPIRED, Today.AddDays(-1));

        await using var db = CreateContext(dbName);
        await CreateController(db).Activate(1);

        Assert.Equal(ContractStatus.EXPIRED, await StatusOfAsync(dbName));
    }

    /// <summary>Feshedilmiş sözleşme onarım yoluyla geri açılamaz.</summary>
    [Fact]
    public async Task Activate_IsRejected_ForTerminatedContract()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedContractAsync(dbName, ContractStatus.TERMINATED, Today.AddMonths(4));

        await using var db = CreateContext(dbName);
        await CreateController(db).Activate(1);

        Assert.Equal(ContractStatus.TERMINATED, await StatusOfAsync(dbName));
    }
}
