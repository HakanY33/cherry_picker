using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MipRental.Domain.Enums;

namespace MipRental.Data.Services;

// Belge numarası (ör. "WR-2026-00042") üretir. CLAUDE.md: numara sadece
// DRAFT -> SUBMITTED geçişinde verilir; bu servis "ne zaman çağrılacağına"
// karışmaz, sadece çağrıldığı anda eşzamanlılık güvenli bir sonraki numarayı
// döner.
//
// Eşzamanlılık: "oku, artır, yaz" YETERSİZ — iki istek aynı LastNumber'ı okuyup
// aynı numarayı üretebilir. Bunun yerine artırma işlemi UPDATE ile veritabanı
// tarafında, tek bir satır kilidi altında yapılır: UPDATE ... WHERE ... satırı
// bulduğu anda o satır için X (exclusive) kilit alır ve transaction commit
// olana kadar tutar; aynı satıra eşzamanlı ikinci bir UPDATE bu kilit boşalana
// kadar bloke olur. Böylece iki paralel istek asla aynı numarayı almaz.
public class DocumentNumberService
{
    private readonly AppDbContext _db;

    public DocumentNumberService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<string> IssueNumberAsync(DocumentType type, int year, CancellationToken cancellationToken = default)
    {
        var ownsTransaction = _db.Database.CurrentTransaction is null;
        var transaction = ownsTransaction ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;

        try
        {
            await EnsureSeriesExistsAsync(type, year, cancellationToken);

            // Tek atomik UPDATE: satır kilidi bu ifade sırasında alınır ve
            // (kendi açtığımız ya da dışarıdan gelen) transaction commit
            // olana kadar tutulur.
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE DocumentSeries SET LastNumber = LastNumber + 1 WHERE DocumentType = {type.ToString()} AND Year = {year}",
                cancellationToken);

            // Aynı transaction içindeki bu okuma, az önce yazdığımız değeri
            // görür; başka hiçbir oturum bu satırı bizim kilidimiz açılana
            // kadar değiştiremeyeceği için numara çakışması mümkün değildir.
            var series = await _db.DocumentSeries.AsNoTracking()
                .Where(s => s.DocumentType == type && s.Year == year)
                .Select(s => new { s.Prefix, s.Padding, s.LastNumber })
                .SingleAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            var number = series.LastNumber.ToString(CultureInfo.InvariantCulture).PadLeft(series.Padding, '0');
            return $"{series.Prefix}-{year}-{number}";
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    // İlgili yılın serisi yoksa otomatik oluşturur. Aynı anda iki istek de
    // "yok" görüp INSERT deneyebilir; UQ_Series (DocumentType, Year) unique
    // index'i ikincisini reddeder, bunu zararsızca yutuyoruz (seri zaten var).
    private async Task EnsureSeriesExistsAsync(DocumentType type, int year, CancellationToken cancellationToken)
    {
        var exists = await _db.DocumentSeries.AsNoTracking()
            .AnyAsync(s => s.DocumentType == type && s.Year == year, cancellationToken);
        if (exists)
        {
            return;
        }

        try
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO DocumentSeries (DocumentType, Prefix, Year, LastNumber, Padding) VALUES ({type.ToString()}, {PrefixFor(type)}, {year}, 0, 5)",
                cancellationToken);
        }
        catch (DbException)
        {
            var stillMissing = !await _db.DocumentSeries.AsNoTracking()
                .AnyAsync(s => s.DocumentType == type && s.Year == year, cancellationToken);
            if (stillMissing)
            {
                throw;
            }
            // Aynı anda başka bir istek zaten oluşturmuş; devam.
        }
    }

    private static string PrefixFor(DocumentType type) => type switch
    {
        DocumentType.WORK_RECORD => "WR",
        DocumentType.REQUEST => "CPR",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Bilinmeyen belge tipi.")
    };
}
