using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Abstractions;

namespace MipRental.Tests;

/// <summary>
/// Gerçek transaction ve raw SQL kullanan akışlar (Submit, onay, revizyon)
/// InMemory provider ile test edilemez; SQLite kullanılır. SQL Server'a özgü
/// "nvarchar(max)" gibi kolon tipleri SQLite'ta geçersiz olduğu için modelden
/// temizlenir — davranışı değil sadece kolon tipini etkiler.
/// </summary>
internal sealed class SqliteTestContext : AppDbContext
{
    public SqliteTestContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

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
