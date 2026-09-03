using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Interceptors;
using MipRental.Data.Pricing;
using MipRental.Data.Services;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;
using MipRental.Domain.Pricing;
using MipRental.Web.Security;

namespace MipRental.Tests;

/// <summary>
/// ADIM 12 BÖLÜM A — talepten çalışma kaydı türetme.
///
/// Testlerin duruşu: "servis hata verdi" yetmez, VERİTABANINDA ne olduğuna
/// bakılır — kayıt oluşmadı mı, talep COMPLETED kaldı mı, snapshot doldu mu.
///
/// Saat kurulumu: gerçekleşen saatler veritabanında UTC durur ama iş tarihi ve
/// dönem YEREL saate göre belirlenir. Testler bu yüzden istenen YEREL saatten
/// yola çıkıp UTC'ye çevirir; böylece hangi saat diliminde koşarsa koşsun aynı
/// iş gününü ve aynı süreyi ifade ederler.
/// </summary>
public class RequestToWorkRecordTests
{
    private const int ContractFirmId = 1;      // AKTİF sözleşmesi ve fiyat satırı var
    private const int NoContractFirmId = 2;    // hiç sözleşmesi yok
    private const int DepartmentId = 1;
    private const int ServiceId = 1;           // seed: Mobil Vinç, birim HOUR
    private const int VariantId = 1;
    private const int OtherVariantId = 2;   // seed'de var, sözleşmede fiyat satırı YOK
    private const int LocationId = 1;

    private const int RequesterId = 10;
    private const int FirmOperatorId = 30;
    private const int OtherFirmOperatorId = 31;

    // Eylül 2026 dönemi seed'den gelir (PeriodConfiguration.HasData, PeriodId = ay).
    private const int SeptemberPeriodId = 9;

    // ---------------------------------------------------------------
    // A6.1 — türetme tüm alanları doğru aktarıyor
    // ---------------------------------------------------------------

    [Fact]
    public async Task Derive_CopiesRequestFieldsToWorkRecord()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedCompletedRequestAsync(connection,
            startLocal: Local(2026, 9, 15, 8, 0), endLocal: Local(2026, 9, 15, 15, 30));

        var record = await DeriveAsync(connection, requestId);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var saved = await db.WorkRecords.AsNoTracking()
            .Include(w => w.WorkRecordLines)
            .SingleAsync(w => w.WorkRecordId == record.WorkRecordId);
        var request = await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId);

        Assert.Equal(requestId, saved.RequestId);
        Assert.Equal(request.FirmId, saved.FirmId);
        Assert.Equal(request.LocationId, saved.LocationId);
        Assert.Equal(request.WorkDescription, saved.WorkDescription);
        Assert.Equal(request.RequestedByUserId, saved.RequestedByUserId);
        Assert.Equal(request.DepartmentId, saved.DepartmentId);
        Assert.Equal(request.AssignedOperatorName, saved.OperatorName);
        Assert.Equal(request.AssignedLicensePlate, saved.LicensePlate);

        // İş tarihi ve saatler GERÇEKLEŞEN (yerel) zamandan gelir.
        Assert.Equal(new DateOnly(2026, 9, 15), saved.WorkDate);
        Assert.Equal(new TimeOnly(8, 0), saved.StartTime);
        Assert.Equal(new TimeOnly(15, 30), saved.EndTime);
        Assert.False(saved.SpansMidnight);

        // Satır talebin hizmet satırından türer.
        var line = Assert.Single(saved.WorkRecordLines);
        Assert.Equal(ServiceId, line.ServiceId);
        Assert.Equal(VariantId, line.VariantId);
        Assert.Equal(7.5m, line.RawQuantity);
    }

    /// <summary>
    /// A5 KARARI: türetilen kayıt DRAFT doğar, doğrudan SUBMITTED değil.
    /// Gerçekleşen süreyi henüz kimse teyit etmedi; kaydı onay zincirine
    /// kendiliğinden sokmak, CLAUDE.md kural 5'in ("otomatik onay yok") gönderim
    /// tarafındaki karşılığını delerdi. Belge numarası da gönderimde verilir.
    /// </summary>
    [Fact]
    public async Task Derive_CreatesDraftRecord_WithoutDocumentNumber()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedCompletedRequestAsync(connection,
            startLocal: Local(2026, 9, 15, 8, 0), endLocal: Local(2026, 9, 15, 12, 0));

        var record = await DeriveAsync(connection, requestId);

        Assert.Equal(WorkRecordStatus.DRAFT, record.Status);
        Assert.StartsWith("DRAFT-", record.DocumentNo);
        Assert.Null(record.SubmittedAt);
    }

    // ---------------------------------------------------------------
    // A6.2 — aynı talepten iki kayıt oluşmuyor
    // ---------------------------------------------------------------

    [Fact]
    public async Task Derive_CalledTwice_ReturnsSameRecord_AndCreatesOnlyOne()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedCompletedRequestAsync(connection,
            startLocal: Local(2026, 9, 15, 8, 0), endLocal: Local(2026, 9, 15, 12, 0));

        var first = await DeriveAsync(connection, requestId);
        var second = await DeriveAsync(connection, requestId);

        Assert.Equal(first.WorkRecordId, second.WorkRecordId);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        Assert.Equal(1, await db.WorkRecords.CountAsync(w => w.RequestId == requestId));
    }

    /// <summary>
    /// A2 gereksinimi: tek kayıt garantisi UYGULAMA KATMANINDA doğrulanamaz —
    /// on paralel çağrının onu da "kayıt yok" görebilir. Garantiyi veren filtreli
    /// UNIQUE index'tir ve index davranışı gerçek SQL Server'a karşı sınanır
    /// (DocumentNumberServiceTests ile aynı yaklaşım).
    /// </summary>
    [Fact]
    public async Task Derive_TenParallelCallers_AgainstRealSqlServer_CreatesExactlyOneRecord()
    {
        var dbName = $"MipRentalTests_{Guid.NewGuid():N}";
        var connectionString = $"Server=localhost;Database={dbName};Trusted_Connection=True;TrustServerCertificate=True;";
        var setupOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;

        int requestId;
        FakeCurrentUser actor;

        // SQL Server'da anahtarlar IDENTITY: SQLite testlerindeki gibi sabit id
        // verilemez, üretilen id'ler okunup kullanılır.
        await using (var setupDb = new AppDbContext(setupOptions, new FakeCurrentUser()))
        {
            await setupDb.Database.EnsureCreatedAsync();

            var firm = new Firm { Code = "TESTVINC", Title = "Test Vinç Ltd. Şti.", CreatedAt = DateTime.UtcNow };
            var department = new Department { Code = "OPS", Name = "Operasyon" };
            var location = new Location { Name = "İskele 3", FullPath = "Liman > İskele 3" };
            setupDb.AddRange(firm, department, location);
            await setupDb.SaveChangesAsync();

            var requester = new User { UserName = "talep1", FullName = "Talep Eden", DepartmentId = department.DepartmentId, CreatedAt = DateTime.UtcNow };
            var firmOperator = new User { UserName = "operator1", FullName = "Operatör", FirmId = firm.FirmId, CreatedAt = DateTime.UtcNow };
            var contract = new Contract
            {
                FirmId = firm.FirmId,
                ContractNo = "SÖZ-2026-001",
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2026, 12, 31),
                Status = ContractStatus.ACTIVE,
                CreatedAt = DateTime.UtcNow
            };
            setupDb.AddRange(requester, firmOperator, contract);
            await setupDb.SaveChangesAsync();

            setupDb.ContractLines.Add(new ContractLine
            {
                ContractId = contract.ContractId,
                ServiceId = ServiceId,
                VariantId = VariantId,
                UnitPrice = 1250m,
                ValidFrom = new DateOnly(2026, 1, 1),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await setupDb.SaveChangesAsync();

            requestId = await AddCompletedRequestAsync(setupDb,
                Local(2026, 9, 15, 8, 0), Local(2026, 9, 15, 12, 0), firm.FirmId,
                requestedByUserId: requester.UserId, departmentId: department.DepartmentId,
                locationId: location.LocationId);

            actor = new FakeCurrentUser
            {
                UserId = firmOperator.UserId,
                FirmId = firm.FirmId,
                Roles = { RoleNames.FirmOperator }
            };
        }

        try
        {
            const int callerCount = 10;
            var tasks = new Task<int>[callerCount];
            for (var i = 0; i < callerCount; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
                    await using var db = new AppDbContext(options, actor);
                    var service = new RequestToWorkRecordService(db, new ContractLineResolver(db), actor);
                    var record = await service.DeriveAsync(requestId);
                    return record.WorkRecordId;
                });
            }

            var ids = await Task.WhenAll(tasks);

            // On çağrının onu da AYNI kaydı döner ve veritabanında tek satır vardır.
            Assert.Single(ids.Distinct());

            await using var verifyDb = new AppDbContext(setupOptions, new FakeCurrentUser());
            Assert.Equal(1, await verifyDb.WorkRecords.CountAsync(w => w.RequestId == requestId));
            Assert.Equal(1, await verifyDb.WorkRecordLines.CountAsync(l => l.WorkRecord.RequestId == requestId));
        }
        finally
        {
            await using var cleanupDb = new AppDbContext(setupOptions, new FakeCurrentUser());
            await cleanupDb.Database.EnsureDeletedAsync();
        }
    }

    // ---------------------------------------------------------------
    // A6.3 — kapalı dönemde türetme başarısız, talep COMPLETED kalıyor
    // ---------------------------------------------------------------

    [Fact]
    public async Task Derive_WhenPeriodClosed_Throws_AndRequestStaysCompleted_AndNoRecordCreated()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedCompletedRequestAsync(connection,
            startLocal: Local(2026, 9, 15, 8, 0), endLocal: Local(2026, 9, 15, 12, 0));
        await ClosePeriodAsync(connection, SeptemberPeriodId);

        var ex = await Assert.ThrowsAsync<PeriodGuardException>(() => DeriveAsync(connection, requestId));
        Assert.Contains("Eylül 2026", ex.Message);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        Assert.Equal(RequestStatus.COMPLETED,
            (await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId)).Status);
        Assert.Empty(await db.WorkRecords.AsNoTracking().ToListAsync());
    }

    /// <summary>
    /// A3 — dönem TALEP tarihine değil işin GERÇEKLEŞTİĞİ tarihe göre seçilir:
    /// talep 31 Ağustos'a açılmış, iş 1 Eylül'e sarkmış. Kayıt Eylül'e yazılır;
    /// Ağustos kapalı olsa bile türetme çalışır.
    /// </summary>
    [Fact]
    public async Task Derive_WorkSpillingIntoNextMonth_UsesActualDatePeriod_NotRequestedDate()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedCompletedRequestAsync(connection,
            startLocal: Local(2026, 9, 1, 1, 0), endLocal: Local(2026, 9, 1, 6, 0),
            requestedDate: new DateOnly(2026, 8, 31));
        await ClosePeriodAsync(connection, periodId: 8); // Ağustos kapalı

        var record = await DeriveAsync(connection, requestId);

        Assert.Equal(SeptemberPeriodId, record.PeriodId);
        Assert.Equal(new DateOnly(2026, 9, 1), record.WorkDate);
    }

    // ---------------------------------------------------------------
    // A6.4 — fiyat bulunamayınca türetme başarısız, kayıt oluşmuyor
    // ---------------------------------------------------------------

    [Fact]
    public async Task Derive_WhenNoContractPrice_Throws_AndNoRecordCreated()
    {
        await using var connection = await CreateSeededConnectionAsync();

        // Sözleşmede 60 tonluk varyantın fiyat satırı YOK; talep onunla açılmış.
        var requestId = await SeedCompletedRequestAsync(connection,
            startLocal: Local(2026, 9, 15, 8, 0), endLocal: Local(2026, 9, 15, 12, 0),
            variantId: OtherVariantId);

        var ex = await Assert.ThrowsAsync<PricingException>(() => DeriveAsync(connection, requestId));
        Assert.Contains("fiyatı tanımlı değil", ex.Message);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        Assert.Empty(await db.WorkRecords.AsNoTracking().ToListAsync());
        Assert.Equal(RequestStatus.COMPLETED,
            (await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId)).Status);
    }

    /// <summary>Sözleşme süresi işin tarihinde bitmişse de sessizce sıfır tutar yazılmaz.</summary>
    [Fact]
    public async Task Derive_WhenContractExpiredBeforeWorkDate_Throws_AndNoRecordCreated()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await using (var setup = CreateContext(connection, new FakeCurrentUser()))
        {
            var contract = await setup.Contracts.SingleAsync(c => c.ContractId == 1);
            contract.EndDate = new DateOnly(2026, 8, 31);
            await setup.SaveChangesAsync();
        }

        var requestId = await SeedCompletedRequestAsync(connection,
            startLocal: Local(2026, 9, 15, 8, 0), endLocal: Local(2026, 9, 15, 12, 0));

        await Assert.ThrowsAsync<PricingException>(() => DeriveAsync(connection, requestId));

        await using var db = CreateContext(connection, new FakeCurrentUser());
        Assert.Empty(await db.WorkRecords.AsNoTracking().ToListAsync());
    }

    // ---------------------------------------------------------------
    // A6.5 — gece yarısını geçen iş
    // ---------------------------------------------------------------

    [Fact]
    public async Task Derive_OvernightWork_MarksSpansMidnight_AndComputesQuantityAcrossMidnight()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedCompletedRequestAsync(connection,
            startLocal: Local(2026, 9, 15, 20, 0), endLocal: Local(2026, 9, 16, 2, 30));

        var record = await DeriveAsync(connection, requestId);

        Assert.True(record.SpansMidnight);
        Assert.Equal(new DateOnly(2026, 9, 15), record.WorkDate);   // işin BAŞLADIĞI gün
        Assert.Equal(new TimeOnly(20, 0), record.StartTime);
        Assert.Equal(new TimeOnly(2, 30), record.EndTime);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var line = await db.WorkRecordLines.AsNoTracking().SingleAsync(l => l.WorkRecordId == record.WorkRecordId);

        // 20:00 -> 02:30 = 6,5 saat. Saat farkı çıkarılsaydı 17,5 saat çıkardı.
        Assert.Equal(6.5m, line.RawQuantity);
        Assert.Equal(6.5m, line.BillableQuantity);
        Assert.Equal(6.5m * 1250m, record.TotalAmount);
    }

    // ---------------------------------------------------------------
    // A6.6 — snapshot alanları dolu (CLAUDE.md kural 2)
    // ---------------------------------------------------------------

    [Fact]
    public async Task Derive_FillsPricingSnapshots()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedCompletedRequestAsync(connection,
            startLocal: Local(2026, 9, 15, 8, 0), endLocal: Local(2026, 9, 15, 12, 0));

        var record = await DeriveAsync(connection, requestId);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var line = await db.WorkRecordLines.AsNoTracking().SingleAsync(l => l.WorkRecordId == record.WorkRecordId);

        Assert.Equal(1250m, line.UnitPriceSnapshot);
        Assert.False(string.IsNullOrWhiteSpace(line.PricingRuleSnapshot));
        Assert.Equal(1, line.ContractLineId);
        Assert.Equal(4m, line.BillableQuantity);
        Assert.Equal(5000m, line.LineAmount);
        Assert.Equal("TRY", line.Currency);

        Assert.Equal(5000m, record.TotalAmount);
        Assert.Equal("TRY", record.Currency);
        Assert.Equal(1, record.ContractId);
    }

    /// <summary>Tamamlanmamış talepten kayıt türemez — akış sırası atlanamaz.</summary>
    [Theory]
    [InlineData(RequestStatus.SCHEDULED)]
    [InlineData(RequestStatus.IN_PROGRESS)]
    [InlineData(RequestStatus.CANCELLED)]
    public async Task Derive_WhenRequestNotCompleted_Throws(RequestStatus status)
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedCompletedRequestAsync(connection,
            startLocal: Local(2026, 9, 15, 8, 0), endLocal: Local(2026, 9, 15, 12, 0), status: status);

        await Assert.ThrowsAsync<RequestStateTransitionException>(() => DeriveAsync(connection, requestId));

        await using var db = CreateContext(connection, new FakeCurrentUser());
        Assert.Empty(await db.WorkRecords.AsNoTracking().ToListAsync());
    }

    // ---------------------------------------------------------------
    // Kurulum
    // ---------------------------------------------------------------

    private static FakeCurrentUser Operator() =>
        new() { UserId = FirmOperatorId, FirmId = ContractFirmId, Roles = { RoleNames.FirmOperator } };

    /// <summary>İstenen YEREL saati, veritabanına yazılacak UTC damgaya çevirir.</summary>
    private static DateTime Local(int year, int month, int day, int hour, int minute) =>
        new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local).ToUniversalTime();

    private static async Task<WorkRecord> DeriveAsync(SqliteConnection connection, int requestId)
    {
        var user = Operator();
        await using var db = CreateContext(connection, user);
        var service = new RequestToWorkRecordService(db, new ContractLineResolver(db), user);
        return await service.DeriveAsync(requestId);
    }

    private static AppDbContext CreateContext(SqliteConnection connection, ICurrentUser user) =>
        new SqliteTestContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new PeriodGuardInterceptor(), new ImmutabilityGuardInterceptor())
                .Options,
            user);

    private static async Task<SqliteConnection> CreateSeededConnectionAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var db = CreateContext(connection, new FakeCurrentUser());
        await db.Database.EnsureCreatedAsync();
        SeedMasterData(db);
        await db.SaveChangesAsync();

        return connection;
    }

    /// <summary>
    /// Hizmet (1), varyantlar ve 2026'nın 12 dönemi model seed'inden (HasData)
    /// gelir; burada tekrar eklenmez.
    /// </summary>
    private static void SeedMasterData(AppDbContext db)
    {
        db.Firms.AddRange(
            new Firm { FirmId = ContractFirmId, Code = "TESTVINC", Title = "Test Vinç Ltd. Şti.", CreatedAt = DateTime.UtcNow },
            new Firm { FirmId = NoContractFirmId, Code = "SOZLESMESIZ", Title = "Sözleşmesiz Firma", CreatedAt = DateTime.UtcNow });

        db.Departments.Add(new Department { DepartmentId = DepartmentId, Code = "OPS", Name = "Operasyon" });

        db.Users.AddRange(
            new User { UserId = RequesterId, UserName = "talep1", FullName = "Talep Eden", DepartmentId = DepartmentId, CreatedAt = DateTime.UtcNow },
            new User { UserId = FirmOperatorId, UserName = "operator1", FullName = "Operatör", FirmId = ContractFirmId, CreatedAt = DateTime.UtcNow },
            new User { UserId = OtherFirmOperatorId, UserName = "operator2", FullName = "Diğer Operatör", FirmId = NoContractFirmId, CreatedAt = DateTime.UtcNow });

        db.Locations.Add(new Location { LocationId = LocationId, Name = "İskele 3", FullPath = "Liman > İskele 3" });

        db.Contracts.Add(new Contract
        {
            ContractId = 1,
            FirmId = ContractFirmId,
            ContractNo = "SÖZ-2026-001",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = ContractStatus.ACTIVE,
            CreatedAt = DateTime.UtcNow
        });
        db.ContractLines.Add(new ContractLine
        {
            ContractLineId = 1,
            ContractId = 1,
            ServiceId = ServiceId,
            VariantId = VariantId,
            UnitPrice = 1250m,
            ValidFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static async Task<int> SeedCompletedRequestAsync(
        SqliteConnection connection, DateTime startLocal, DateTime endLocal,
        DateOnly? requestedDate = null, int firmId = ContractFirmId,
        RequestStatus status = RequestStatus.COMPLETED, int variantId = VariantId)
    {
        await using var db = CreateContext(connection, new FakeCurrentUser());
        return await AddCompletedRequestAsync(db, startLocal, endLocal, firmId, requestedDate, status, variantId: variantId);
    }

    private static async Task<int> AddCompletedRequestAsync(
        AppDbContext db, DateTime startUtc, DateTime endUtc, int firmId,
        DateOnly? requestedDate = null, RequestStatus status = RequestStatus.COMPLETED,
        int requestedByUserId = RequesterId, int departmentId = DepartmentId, int locationId = LocationId,
        int variantId = VariantId)
    {
        var request = new Request
        {
            DocumentNo = $"CPR-2026-{Guid.NewGuid():N}"[..20],
            Status = status,
            RequestedByUserId = requestedByUserId,
            DepartmentId = departmentId,
            FirmId = firmId,
            IssueDate = new DateOnly(2026, 9, 1),
            RequestedDate = requestedDate ?? new DateOnly(2026, 9, 15),
            RequestedStartTime = new TimeOnly(8, 0),
            RequestedEndTime = new TimeOnly(12, 0),
            LocationId = locationId,
            WorkDescription = "Konteyner taşıma",
            AssignedOperatorName = "Ahmet Yılmaz",
            AssignedLicensePlate = "33 ABC 123",
            ActualStartTime = startUtc,
            ActualEndTime = endUtc,
            CreatedAt = DateTime.UtcNow
        };
        request.RequestLines.Add(new RequestLine { LineNo = 1, ServiceId = ServiceId, VariantId = variantId });

        db.Requests.Add(request);
        await db.SaveChangesAsync();
        return request.RequestId;
    }

    private static async Task ClosePeriodAsync(SqliteConnection connection, int periodId)
    {
        await using var db = CreateContext(connection, new FakeCurrentUser());
        var period = await db.Periods.SingleAsync(p => p.PeriodId == periodId);
        period.Status = PeriodStatus.CLOSED;
        await db.SaveChangesAsync();
    }
}
