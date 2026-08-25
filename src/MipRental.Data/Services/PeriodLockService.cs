using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MipRental.Domain.Approvals;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Data.Services;

/// <summary>
/// Dönem kapatma / yeniden açma işleminin kayıt tarafı.
///
/// Dönem kapatmak sadece Periods.Status'u değiştirmek değildir: o döneme ait
/// ONAYLI kayıtlar da LOCKED'a çekilir. Böylece "kapalı dönem" kuralı tek bir
/// kolona bağlı kalmaz, kaydın kendi durumuna da yazılır ve
/// ImmutabilityGuardInterceptor kaydı kendi başına da korur.
///
/// İki iş TEK TRANSACTION içinde yapılır: yarım kalmış bir kapanış
/// (dönem kapalı ama kayıtlar hâlâ APPROVED) mali olarak tutarsızdır.
///
/// SaveChanges'in iki kez çağrılmasının sebebi PeriodGuardInterceptor'dır:
/// kapalı bir döneme ait WorkRecord değiştirilemez. Bu yüzden sıra önemlidir —
///   kapatırken: ÖNCE kayıtlar kilitlenir, SONRA dönem kapanır,
///   açarken:    ÖNCE dönem açılır, SONRA kayıtların kilidi açılır.
/// Her iki SaveChanges de aynı transaction'da olduğu için aradaki ara durum
/// dışarıdan hiçbir zaman görünmez.
/// </summary>
public sealed class PeriodLockService
{
    private readonly AppDbContext _db;

    public PeriodLockService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Dönemi kapatır ve döneme ait tüm APPROVED kayıtları LOCKED yapar.
    /// Kilitlenen kayıt sayısını döner.
    /// </summary>
    public async Task<int> CloseAsync(Period period, int closedByUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);

        await using var transaction = await BeginTransactionAsync(cancellationToken);

        // 1) Kayıtları kilitle — dönem HENÜZ açık olduğu için PeriodGuard geçer.
        var approved = await _db.WorkRecords
            .Where(w => w.PeriodId == period.PeriodId && w.Status == WorkRecordStatus.APPROVED)
            .ToListAsync(cancellationToken);

        foreach (var record in approved)
        {
            WorkRecordStateMachine.LockForPeriodClose(record, period);
        }

        if (approved.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        // 2) Dönemi kapat.
        period.Status = PeriodStatus.CLOSED;
        period.ClosedBy = closedByUserId;
        period.ClosedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return approved.Count;
    }

    /// <summary>
    /// Dönemi yeniden açar ve döneme ait tüm LOCKED kayıtları APPROVED'a döndürür.
    /// Kilidi açılan kayıt sayısını döner. Gerekçe Periods.ReopenReason'a yazılır;
    /// kayıt bazında ayrıca gerekçe tutulmaz (kaynağı tektir).
    /// </summary>
    public async Task<int> ReopenAsync(Period period, int reopenedByUserId, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Dönemi yeniden açmak için gerekçe zorunludur.", nameof(reason));
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);

        // 1) Dönemi aç — kayıtlara dokunmadan ÖNCE, yoksa PeriodGuard reddeder.
        period.Status = PeriodStatus.REOPENED;
        period.ReopenedBy = reopenedByUserId;
        period.ReopenedAt = DateTime.UtcNow;
        period.ReopenReason = reason;
        await _db.SaveChangesAsync(cancellationToken);

        // 2) Kilitleri aç.
        var locked = await _db.WorkRecords
            .Where(w => w.PeriodId == period.PeriodId && w.Status == WorkRecordStatus.LOCKED)
            .ToListAsync(cancellationToken);

        foreach (var record in locked)
        {
            WorkRecordStateMachine.UnlockForPeriodReopen(record, period);
        }

        if (locked.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return locked.Count;
    }

    /// <summary>
    /// Dışarıdan açılmış bir transaction varsa ona katılır (null döner, commit
    /// dışarıya bırakılır). Provider transaction desteklemiyorsa (InMemory) null
    /// döner — bu yalnızca testlerde olur, SQL Server'da her zaman transaction vardır.
    /// </summary>
    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (_db.Database.CurrentTransaction is not null)
        {
            return null;
        }

        try
        {
            return await _db.Database.BeginTransactionAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
