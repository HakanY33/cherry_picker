using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Reporting;
using MipRental.Data.Services;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Web.Controllers;
using MipRental.Web.Documents;
using MipRental.Web.Models.Summaries;

namespace MipRental.Tests;

/// <summary>
/// İcmal ekranının yetki davranışı. Servis katmanı testleri
/// (MonthlySummaryTests) izolasyonu zaten doğruluyor; buradaki testler
/// CONTROLLER'ın o kuralı doğru ilettiğini — istekleri sessizce kendi firmasına
/// ÇEVİRMEDİĞİNİ — kanıtlıyor.
/// </summary>
public class SummariesControllerAuthorizationTests
{
    private const int FirmId = 1;
    private const int OtherFirmId = 2;
    private const int PeriodId = 3;

    private static AppDbContext CreateContext(SqliteConnection connection, ICurrentUser user) =>
        new SqliteTestContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options, user);

    private static async Task<SqliteConnection> CreateSeededConnectionAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var db = CreateContext(connection, new FakeCurrentUser());
        await db.Database.EnsureCreatedAsync();
        db.Firms.Add(new Firm { FirmId = FirmId, Code = "FIRMA-1", Title = "Firma 1", CreatedAt = DateTime.UtcNow });
        db.Firms.Add(new Firm { FirmId = OtherFirmId, Code = "FIRMA-2", Title = "Firma 2", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return connection;
    }

    private static SummariesController CreateController(SqliteConnection connection, ICurrentUser user)
    {
        var db = CreateContext(connection, user);
        var summaries = new MonthlySummaryService(db, user);
        var generator = new DocumentGenerator(
            db, new GeneratedDocumentService(db, user, new InMemoryDocumentStorage()));
        return new SummariesController(db, user, summaries, generator);
    }

    [Fact]
    public async Task FirmUser_RequestingAnotherFirm_GetsForbidden()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var firmUser = new FakeCurrentUser { UserId = 2, FirmId = FirmId };

        var index = await CreateController(connection, firmUser).Index(PeriodId, OtherFirmId, null);
        Assert.IsType<ForbidResult>(index);

        var pdf = await CreateController(connection, firmUser).Pdf(PeriodId, OtherFirmId, null);
        Assert.IsType<ForbidResult>(pdf);

        var excel = await CreateController(connection, firmUser).Excel(PeriodId, OtherFirmId, null);
        Assert.IsType<ForbidResult>(excel);
    }

    /// <summary>
    /// Firma kullanıcısı firma belirtmezse kendi firması varsayılır ve firma
    /// seçim kutusu HİÇ gösterilmez.
    /// </summary>
    [Fact]
    public async Task FirmUser_WithoutFirmParameter_GetsOwnFirmAndNoFirmPicker()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var firmUser = new FakeCurrentUser { UserId = 2, FirmId = FirmId };

        var result = Assert.IsType<ViewResult>(await CreateController(connection, firmUser).Index(PeriodId, null, null));
        var model = Assert.IsType<MonthlySummaryViewModel>(result.Model);

        Assert.Equal(FirmId, model.FirmId);
        Assert.False(model.CanChooseFirm);
        Assert.Empty(model.FirmOptions);
        Assert.NotNull(model.Summary);
        Assert.Equal(FirmId, model.Summary!.FirmId);
    }

    [Fact]
    public async Task MipStaff_CanChooseAnyFirm()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var mipUser = new FakeCurrentUser { UserId = 1 };

        var result = Assert.IsType<ViewResult>(await CreateController(connection, mipUser).Index(PeriodId, OtherFirmId, null));
        var model = Assert.IsType<MonthlySummaryViewModel>(result.Model);

        Assert.True(model.CanChooseFirm);
        Assert.Equal(OtherFirmId, model.Summary!.FirmId);
    }

    /// <summary>MIP personeli firma seçmeden icmal göremez; ekran filtre bekler.</summary>
    [Fact]
    public async Task MipStaff_WithoutFirm_GetsEmptyFilterScreen()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var mipUser = new FakeCurrentUser { UserId = 1 };

        var result = Assert.IsType<ViewResult>(await CreateController(connection, mipUser).Index(PeriodId, null, null));
        var model = Assert.IsType<MonthlySummaryViewModel>(result.Model);

        Assert.Null(model.Summary);
    }
}
