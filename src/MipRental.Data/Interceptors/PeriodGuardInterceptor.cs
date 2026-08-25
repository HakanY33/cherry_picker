using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;

namespace MipRental.Data.Interceptors;

/// <summary>
/// CLAUDE.md kural 4: kapalı döneme (Period.Status = CLOSED) kayıt girilemez,
/// mevcut kayıt değiştirilemez. Ayrıca WorkRecord.WorkDate, bağlı olduğu
/// Period'un yıl/ay aralığı içinde olmalı (Bölüm A4).
///
/// Controller'da değil, burada (SaveChanges seviyesinde) uygulanır — hangi
/// ekrandan/koddan gelirse gelsin devre dışı bırakılamaz. Stateless olduğu
/// için tüm DbContext'ler arasında tek bir singleton örnek paylaşılabilir.
/// </summary>
public class PeriodGuardInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Validate(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Validate(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Validate(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var periodCache = new Dictionary<int, Period?>();

        Period? ResolvePeriod(int periodId)
        {
            if (periodCache.TryGetValue(periodId, out var cached))
            {
                return cached;
            }

            var tracked = context.ChangeTracker.Entries<Period>()
                .FirstOrDefault(e => e.Entity.PeriodId == periodId)?.Entity;
            var period = tracked ?? context.Set<Period>().AsNoTracking().FirstOrDefault(p => p.PeriodId == periodId);
            periodCache[periodId] = period;
            return period;
        }

        foreach (var entry in context.ChangeTracker.Entries<WorkRecord>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var period = ResolvePeriod(entry.Entity.PeriodId);
            EnsurePeriodOpen(period);
            EnsureWorkDateWithinPeriod(entry.Entity.WorkDate, period);
        }

        foreach (var entry in context.ChangeTracker.Entries<WorkRecordLine>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var periodId = ResolveWorkRecordPeriodId(context, entry);
            if (periodId is null)
            {
                continue; // WorkRecord bulunamadı; FK kısıtı zaten reddedecek.
            }

            EnsurePeriodOpen(ResolvePeriod(periodId.Value));
        }
    }

    private static int? ResolveWorkRecordPeriodId(DbContext context, EntityEntry<WorkRecordLine> entry)
    {
        var workRecordEntry = entry.Reference(l => l.WorkRecord).TargetEntry;
        if (workRecordEntry is not null)
        {
            return workRecordEntry.Entity.PeriodId;
        }

        var workRecordId = entry.Entity.WorkRecordId;
        return context.Set<WorkRecord>().AsNoTracking()
            .Where(w => w.WorkRecordId == workRecordId)
            .Select(w => (int?)w.PeriodId)
            .FirstOrDefault();
    }

    private static void EnsurePeriodOpen(Period? period)
    {
        if (period is { Status: PeriodStatus.CLOSED })
        {
            throw new PeriodGuardException($"{DescribePeriod(period)} dönemi kapalıdır, kayıt girilemez.");
        }
    }

    private static void EnsureWorkDateWithinPeriod(DateOnly workDate, Period? period)
    {
        if (period is null)
        {
            return; // FK kısıtı zaten reddedecek.
        }

        var periodStart = new DateOnly(period.Year, period.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        if (workDate < periodStart || workDate > periodEnd)
        {
            throw new PeriodGuardException(
                $"İş tarihi ({workDate:dd.MM.yyyy}), bağlı olduğu {DescribePeriod(period)} döneminin tarih aralığı dışında.");
        }
    }

    private static string DescribePeriod(Period period)
    {
        var monthName = CultureInfo.GetCultureInfo("tr-TR").DateTimeFormat.GetMonthName(period.Month);
        return $"{monthName} {period.Year}";
    }
}
