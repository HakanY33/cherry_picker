using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Services;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Enums;

namespace MipRental.Tests;

public class DocumentNumberServiceTests
{
    [Fact]
    public async Task IssueNumberAsync_ForExistingSeries_ProducesFormattedNumber()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await CreateSchemaAsync(connection);

        await using var db = new SqliteCompatibleContext(SqliteOptions(connection), new FakeCurrentUser());
        var service = new DocumentNumberService(db);

        var number = await service.IssueNumberAsync(DocumentType.WORK_RECORD, 2026);

        Assert.Equal("WR-2026-00001", number);

        await connection.CloseAsync();
    }

    [Fact]
    public async Task IssueNumberAsync_SequentialCalls_Increment()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await CreateSchemaAsync(connection);

        await using var db = new SqliteCompatibleContext(SqliteOptions(connection), new FakeCurrentUser());
        var service = new DocumentNumberService(db);

        var first = await service.IssueNumberAsync(DocumentType.WORK_RECORD, 2026);
        var second = await service.IssueNumberAsync(DocumentType.WORK_RECORD, 2026);

        Assert.Equal("WR-2026-00001", first);
        Assert.Equal("WR-2026-00002", second);

        await connection.CloseAsync();
    }

    [Fact]
    public async Task IssueNumberAsync_MissingSeriesForYear_AutoCreatesAndIssuesFirstNumber()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await CreateSchemaAsync(connection);

        await using var db = new SqliteCompatibleContext(SqliteOptions(connection), new FakeCurrentUser());
        var service = new DocumentNumberService(db);

        // 2027 serisi migration'da seed edilmedi; otomatik oluşmalı.
        var number = await service.IssueNumberAsync(DocumentType.WORK_RECORD, 2027);

        Assert.Equal("WR-2027-00001", number);

        await connection.CloseAsync();
    }

    /// <summary>
    /// A1 gereksinimi: eşzamanlılık garantisi SQLite/InMemory'de değil, gerçek
    /// SQL Server'a karşı doğrulanmalı — satır kilidi davranışı provider'a özgüdür.
    /// Bu ortamda "Server=localhost" ile Trusted Connection erişilebilir bir
    /// SQL Server 2025 Developer Edition çalıştığı doğrulanmıştır.
    /// </summary>
    [Fact]
    public async Task IssueNumberAsync_TwentyParallelCallers_AgainstRealSqlServer_AllUnique_NoGaps()
    {
        var dbName = $"MipRentalTests_{Guid.NewGuid():N}";
        var connectionString = $"Server=localhost;Database={dbName};Trusted_Connection=True;TrustServerCertificate=True;";
        var setupOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;

        await using (var setupDb = new AppDbContext(setupOptions, new FakeCurrentUser()))
        {
            await setupDb.Database.EnsureCreatedAsync();
        }

        try
        {
            const int callerCount = 20;
            var tasks = new Task<string>[callerCount];
            for (var i = 0; i < callerCount; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
                    await using var db = new AppDbContext(options, new FakeCurrentUser());
                    var service = new DocumentNumberService(db);
                    return await service.IssueNumberAsync(DocumentType.WORK_RECORD, 2026);
                });
            }

            var numbers = await Task.WhenAll(tasks);

            Assert.Equal(callerCount, numbers.Distinct().Count());

            await using var verifyDb = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options, new FakeCurrentUser());
            var series = await verifyDb.DocumentSeries.AsNoTracking()
                .SingleAsync(s => s.DocumentType == DocumentType.WORK_RECORD && s.Year == 2026);

            // Boşluksuz: tam olarak callerCount kadar arttı, ne eksik ne fazla.
            Assert.Equal(callerCount, series.LastNumber);
        }
        finally
        {
            await using var cleanupDb = new AppDbContext(setupOptions, new FakeCurrentUser());
            await cleanupDb.Database.EnsureDeletedAsync();
        }
    }

    private static DbContextOptions<AppDbContext> SqliteOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;

    private static async Task CreateSchemaAsync(SqliteConnection connection)
    {
        await using var db = new SqliteCompatibleContext(SqliteOptions(connection), new FakeCurrentUser());
        await db.Database.EnsureCreatedAsync();
    }

    /// <summary>
    /// EF Core model'ini SQLite ile uyumlu hale getiren DbContext alt sınıfı
    /// (AuditAtomicityTests'teki ile aynı yaklaşım): nvarchar(max) gibi SQL
    /// Server'a özgü kolon tiplerini kaldırır.
    /// </summary>
    private sealed class SqliteCompatibleContext : AppDbContext
    {
        public SqliteCompatibleContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
            : base(options, currentUser) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entity.GetProperties())
                {
                    var columnType = property.GetColumnType();
                    if (columnType is not null && columnType.Contains("max", StringComparison.OrdinalIgnoreCase))
                    {
                        property.SetColumnType(null);
                    }
                }
            }
        }
    }
}
