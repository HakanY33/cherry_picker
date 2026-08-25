using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MipRental.Data;
using MipRental.Data.Interceptors;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;

namespace MipRental.Tests;

public class AuditAtomicityTests
{
    /// <summary>
    /// Audit yazımını bilerek patlatıp asıl kaydın da veritabanına yazılmadığını doğrular.
    /// InMemoryDatabase transaction desteklemediği için SQLite in-memory kullanılır.
    /// </summary>
    [Fact]
    public async Task InsertAuditFailure_RollsBackOriginalRecord()
    {
        // SQLite in-memory: bağlantı açık kaldığı sürece DB yaşar.
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var currentUser = new FakeCurrentUser { UserId = 1 };
        var auditInterceptor = new AuditSaveChangesInterceptor(currentUser, new NoOpHttpContextAccessor());

        // İkinci SaveChanges çağrısını (audit flush) patlatacak interceptor.
        var bombInterceptor = new BombOnSecondSaveInterceptor();

        // Şemayı interceptor'sız oluştur ve FK bağımlılığı olan User kaydını ekle
        await CreateSchemaAsync(connection, currentUser);
        await SeedUserAsync(connection, currentUser);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(bombInterceptor, auditInterceptor)
            .Options;

        // İkinci save çağrısında (audit flush) patlat
        bombInterceptor.Arm();

        // Firma eklemeyi dene — audit patlamalı ve asıl kayıt da geri dönmeli
        await using (var db = new AppDbContext(options, currentUser))
        {
            db.Firms.Add(new Firm { Code = "FIRMA-X", Title = "Test Firma", CreatedAt = DateTime.UtcNow });

            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        // Asıl kaydın da veritabanına yazılmadığını doğrula
        bombInterceptor.Disarm();
        await using (var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options, currentUser))
        {
            var firmCount = await db.Firms.IgnoreQueryFilters().CountAsync();
            Assert.Equal(0, firmCount);
        }

        await connection.CloseAsync();
    }

    /// <summary>
    /// Normal akışta (hata yokken) INSERT + audit'in birlikte commitlendiğini doğrular.
    /// </summary>
    [Fact]
    public async Task InsertWithAudit_BothCommittedOnSuccess()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var currentUser = new FakeCurrentUser { UserId = 1 };
        var interceptor = new AuditSaveChangesInterceptor(currentUser, new NoOpHttpContextAccessor());

        await CreateSchemaAsync(connection, currentUser);
        await SeedUserAsync(connection, currentUser);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;

        await using (var db = new AppDbContext(options, currentUser))
        {
            db.Firms.Add(new Firm { Code = "FIRMA-Y", Title = "Başarılı Firma", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options, currentUser))
        {
            var firm = await db.Firms.SingleAsync();
            Assert.Equal("Başarılı Firma", firm.Title);

            var auditLogs = await db.AuditLogs
                .Where(a => a.TableName == "Firms" && a.Action == Domain.Enums.AuditAction.INSERT)
                .ToListAsync();
            Assert.NotEmpty(auditLogs);
        }

        await connection.CloseAsync();
    }

    /// <summary>
    /// SQLite in-memory şema oluşturma. HasColumnType("nvarchar(max)") gibi
    /// SQL Server'a özgü tanımlar SQLite'ta desteklenmez; bu yüzden şemayı
    /// oluşturduktan sonra EF Core'un geçerli modeli SQLite uyumlu olacak şekilde
    /// tablo bazlı oluşturuyoruz.
    /// </summary>
    private static async Task CreateSchemaAsync(SqliteConnection connection, ICurrentUser currentUser)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new SqliteCompatibleContext(options, currentUser);
        await db.Database.EnsureCreatedAsync();
    }

    private static async Task SeedUserAsync(SqliteConnection connection, ICurrentUser currentUser)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options, currentUser);
        db.Users.Add(new User { UserId = 1, UserName = "test.user", FullName = "Test Kullanıcı", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// EF Core model'ini SQLite ile uyumlu hale getiren DbContext alt sınıfı.
    /// nvarchar(max) gibi SQL Server'a özgü kolon tiplerini kaldırır.
    /// </summary>
    private sealed class SqliteCompatibleContext : AppDbContext
    {
        public SqliteCompatibleContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
            : base(options, currentUser) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // nvarchar(max) gibi SQL Server'a özgü tip tanımlarını kaldır;
            // SQLite tüm metin kolonlarını TEXT olarak saklar.
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

    /// <summary>
    /// İkinci SaveChanges çağrısında (audit flush) hata fırlatan interceptor.
    /// Birinci çağrı (asıl INSERT) normal geçer, ikinci çağrı patlar.
    /// </summary>
    private sealed class BombOnSecondSaveInterceptor : SaveChangesInterceptor
    {
        private int _saveCallCount;
        private bool _armed;

        public void Arm()
        {
            _armed = true;
            _saveCallCount = 0;
        }

        public void Disarm()
        {
            _armed = false;
            _saveCallCount = 0;
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (_armed && Interlocked.Increment(ref _saveCallCount) >= 2)
            {
                throw new InvalidOperationException("Simulated audit write failure for testing.");
            }

            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_armed && Interlocked.Increment(ref _saveCallCount) >= 2)
            {
                throw new InvalidOperationException("Simulated audit write failure for testing.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class NoOpHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
