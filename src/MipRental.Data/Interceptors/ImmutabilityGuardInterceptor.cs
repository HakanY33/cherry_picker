using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;

namespace MipRental.Data.Interceptors;

/// <summary>
/// CLAUDE.md kural 1: onaylanmış hiçbir mali kayıt UPDATE veya DELETE edilmez.
/// Düzeltme = yeni versiyon (RevisionOfId) + gerekçe.
///
/// İki katmanlı korur:
/// 1. Genel: EntityState.Deleted reddedilir (fiziksel silme sistemde hiçbir yerde
///    yok; bu son bir güvenlik ağı). TEK İSTİSNA: UserRole — bkz. DeletableEntities.
/// 2. Özel: Status = APPROVED veya LOCKED olan WorkRecord ve ona bağlı
///    WorkRecordLine'lar güncellenemez/eklenemez. İki istisna:
///    - WorkRecord.IntegrationStatus alanı (Faz 2'de Oracle gönderim durumu değişecek),
///    - Dönem kapanış/açılışının APPROVED <-> LOCKED geçişi; orada da SADECE
///      Status alanı değişebilir, başka alana dokunulamaz.
///
/// Controller'da değil, burada (SaveChanges seviyesinde) uygulanır. Stateless
/// olduğu için tüm DbContext'ler arasında tek bir singleton örnek paylaşılabilir.
/// </summary>
public class ImmutabilityGuardInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<string> AllowedFieldsOnApprovedWorkRecord =
        new(StringComparer.Ordinal) { nameof(WorkRecord.IntegrationStatus), "UpdatedAt" };

    /// <summary>
    /// Fiziksel silmeye izin verilen TEK tip: kullanıcı-rol eşlemesi.
    ///
    /// CLAUDE.md kural 1 "onaylanmış MALİ KAYIT" der; UserRole mali kayıt değil,
    /// bir ERİŞİM eşlemesidir. Rol geri alınabilmelidir (kullanıcı görev değiştirir,
    /// işten ayrılır) ve tablonun kendi kolonu yoktur — satırın gidişi eşlemenin
    /// bitişidir. Kim ne zaman aldı bilgisi ayrıca AuditLogs'a yazılır.
    ///
    /// Liste BİLEREK tek elemanlıdır: buraya yeni bir tip eklemek, "hiçbir mali
    /// kayıt silinmez" güvencesini zayıflatır. Mali veya belge niteliğindeki hiçbir
    /// entity buraya eklenmemelidir.
    /// </summary>
    private static readonly HashSet<Type> DeletableEntities = [typeof(UserRole)];

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

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Deleted && !DeletableEntities.Contains(entry.Metadata.ClrType))
            {
                var tableName = entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name;
                throw new ImmutabilityViolationException(
                    $"\"{tableName}\" tablosunda kayıt silinemez; hiçbir kayıt fiziksel olarak silinmez.");
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<WorkRecord>())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var originalStatus = (WorkRecordStatus)entry.OriginalValues[nameof(WorkRecord.Status)]!;
            if (originalStatus is not (WorkRecordStatus.APPROVED or WorkRecordStatus.LOCKED))
            {
                continue;
            }

            var currentStatus = (WorkRecordStatus)entry.CurrentValues[nameof(WorkRecord.Status)]!;

            // Dönem kapanış/açılış geçişine izin verilen TEK yol: Status alanı
            // APPROVED <-> LOCKED arasında gidiyor ve BAŞKA HİÇBİR alan değişmiyor.
            // Aynı SaveChanges'te tutar/tarih gibi bir alana da dokunulmuşsa geçiş
            // meşru sayılmaz — kilit açmak, kaydı düzenlemenin arka kapısı olamaz.
            if (IsPeriodLockTransition(originalStatus, currentStatus))
            {
                var alsoChanged = entry.Properties.FirstOrDefault(p =>
                    p.IsModified &&
                    p.Metadata.Name != nameof(WorkRecord.Status) &&
                    !AllowedFieldsOnApprovedWorkRecord.Contains(p.Metadata.Name));

                if (alsoChanged is null)
                {
                    continue;
                }

                throw new ImmutabilityViolationException(
                    $"Dönem kilidi değişirken çalışma kaydının ({entry.Entity.DocumentNo}) " +
                    $"\"{alsoChanged.Metadata.Name}\" alanı da değiştirilemez.");
            }

            var offendingField = entry.Properties.FirstOrDefault(p =>
                p.IsModified && !AllowedFieldsOnApprovedWorkRecord.Contains(p.Metadata.Name));

            if (offendingField is not null)
            {
                throw new ImmutabilityViolationException(
                    originalStatus == WorkRecordStatus.LOCKED
                        ? $"Kapalı döneme ait kilitli çalışma kaydı ({entry.Entity.DocumentNo}) değiştirilemez. Önce dönemin yeniden açılması gerekir."
                        : $"Onaylanmış çalışma kaydı ({entry.Entity.DocumentNo}) değiştirilemez. Düzeltme için yeni versiyon (RevisionOfId) oluşturun.");
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<WorkRecordLine>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var parentStatus = ResolveParentWorkRecordStatus(context, entry);
            if (parentStatus == WorkRecordStatus.LOCKED)
            {
                throw new ImmutabilityViolationException(
                    "Kapalı döneme ait kilitli çalışma kaydının satırları değiştirilemez veya eklenemez. Önce dönemin yeniden açılması gerekir.");
            }

            if (parentStatus == WorkRecordStatus.APPROVED)
            {
                throw new ImmutabilityViolationException(
                    "Onaylanmış çalışma kaydının satırları değiştirilemez veya eklenemez. Düzeltme için yeni versiyon (RevisionOfId) oluşturun.");
            }
        }
    }

    private static bool IsPeriodLockTransition(WorkRecordStatus original, WorkRecordStatus current) =>
        (original == WorkRecordStatus.APPROVED && current == WorkRecordStatus.LOCKED) ||
        (original == WorkRecordStatus.LOCKED && current == WorkRecordStatus.APPROVED);

    /// <summary>
    /// Satırın bağlı olduğu çalışma kaydının VERİTABANINDAKİ durumu.
    /// Kayıt aynı SaveChanges içinde yeni oluşturuluyorsa null döner.
    /// </summary>
    private static WorkRecordStatus? ResolveParentWorkRecordStatus(DbContext context, EntityEntry<WorkRecordLine> entry)
    {
        var workRecordEntry = entry.Reference(l => l.WorkRecord).TargetEntry;
        if (workRecordEntry is not null)
        {
            if (workRecordEntry.State == EntityState.Added)
            {
                // Aynı SaveChanges çağrısında yeni oluşturuluyor (ör. geçmiş veri aktarımı);
                // mevcut/onaylı bir kaydı mutasyona uğratmıyor, bu yüzden engellenmez.
                return null;
            }

            // Modified durumundaysa kendi kuralı yukarıda ayrıca değerlendirilir;
            // burada bize gereken satırın bağlı olduğu kaydın MEVCUT (DB'deki) durumu.
            return workRecordEntry.State == EntityState.Modified
                ? (WorkRecordStatus)workRecordEntry.OriginalValues[nameof(WorkRecord.Status)]!
                : workRecordEntry.Entity.Status;
        }

        var workRecordId = entry.Entity.WorkRecordId;
        return context.Set<WorkRecord>().AsNoTracking()
            .Where(w => w.WorkRecordId == workRecordId)
            .Select(w => (WorkRecordStatus?)w.Status)
            .FirstOrDefault();
    }
}
