using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Data.Interceptors;

/// <summary>
/// Her INSERT/UPDATE için alan bazlı AuditLog satırı üretir. CLAUDE.md kural 1/2:
/// mali kayıtlar asla sessizce değişmez, her değişikliğin izi kalır.
///
/// INSERT audit'i asıl kayıtla atomik olarak tek transaction içinde yazılır.
/// Dışarıdan açılmış bir transaction varsa ona katılır, yoksa kendi açar.
/// Audit yazımı başarısız olursa asıl kayıt da geri döner.
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    // Framework'ün kendi yönettiği zaman damgaları; iş anlamı taşımadıkları için
    // her UPDATE'te ayrı bir "alan değişti" satırı üretmiyoruz.
    private static readonly HashSet<string> ExcludedFields = new(StringComparer.Ordinal) { "CreatedAt", "UpdatedAt" };
    private const string MaskedFieldName = "PasswordHash";
    private const string MaskedValue = "***";

    private readonly ICurrentUser _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;

    // Bir SaveChanges çağrısı içinde tespit edilen, henüz identity Id'si atanmamış
    // (Added durumundaki) satırlar için bekleyen alan listesi. SavingChanges'te
    // doldurulur, fiziksel INSERT tamamlandıktan sonra SavedChanges'te (Id'ler
    // artık bilindiği için) ayrı, küçük bir SaveChanges ile AuditLog'a yazılır.
    private readonly List<PendingInsertField> _pendingInsertFields = new();

    // Kendi açtığımız transaction'ı takip eder. Dışarıdan açılmış bir transaction
    // varsa bu null kalır ve commit/rollback dışarıya bırakılır.
    private IDbContextTransaction? _ownedTransaction;

    // İç SaveChanges çağrısı sırasında (audit flush) re-entry'yi engellemek için
    // kullanılan bayrak. Audit flush sırasında CaptureUpdateAudits ve
    // EnsureTransaction tekrar çağrılmamalıdır.
    private bool _isFlushing;

    public AuditSaveChangesInterceptor(ICurrentUser currentUser, IHttpContextAccessor httpContextAccessor)
    {
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (!_isFlushing)
        {
            CaptureUpdateAudits(eventData.Context);
            EnsureTransaction(eventData.Context);
        }
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (!_isFlushing)
        {
            CaptureUpdateAudits(eventData.Context);
            EnsureTransaction(eventData.Context);
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        FlushPendingInsertAudits(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await FlushPendingInsertAuditsAsync(eventData.Context, cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        RollbackOwnedTransaction();
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        RollbackOwnedTransaction();
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    /// <summary>
    /// INSERT bekleyen alanlar varsa ve henüz dışarıdan açılmış bir transaction
    /// yoksa, asıl kayıt + audit yazımını atomik yapmak için yeni bir transaction açar.
    /// InMemoryDatabase gibi transaction desteklemeyen provider'larda atlanır.
    /// </summary>
    private void EnsureTransaction(DbContext? context)
    {
        if (context is null || _pendingInsertFields.Count == 0)
        {
            return;
        }

        if (context.Database.CurrentTransaction is not null)
        {
            // Dışarıdan açılmış transaction var; ona katılıyoruz.
            _ownedTransaction = null;
            return;
        }

        try
        {
            _ownedTransaction = context.Database.BeginTransaction();
        }
        catch (InvalidOperationException)
        {
            // Provider transaction desteklemiyorsa (ör. InMemoryDatabase),
            // sessizce geç — atomiklik garantisi olmaz ama fonksiyonellik bozulmaz.
            _ownedTransaction = null;
        }
    }

    private void CaptureUpdateAudits(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        _pendingInsertFields.Clear();
        var utcNow = DateTime.UtcNow;
        var userId = ResolveUserId();
        var ipAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (IsAuditLog(entry) || entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var tableName = entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name;

            if (entry.State == EntityState.Added)
            {
                // Identity Id henüz DB'den dönmedi; alanları şimdilik biriktir,
                // RecordId'yi SavedChanges'te doldurup ayrıca kaydedeceğiz.
                foreach (var property in entry.Properties)
                {
                    if (property.Metadata.IsPrimaryKey() || ExcludedFields.Contains(property.Metadata.Name))
                    {
                        continue;
                    }

                    _pendingInsertFields.Add(new PendingInsertField(entry, tableName, property.Metadata.Name, property.CurrentValue));
                }
            }
            else
            {
                foreach (var property in entry.Properties)
                {
                    if (!property.IsModified || ExcludedFields.Contains(property.Metadata.Name))
                    {
                        continue;
                    }

                    if (Equals(property.OriginalValue, property.CurrentValue))
                    {
                        continue;
                    }

                    var recordId = GetRecordId(entry);

                    context.Add(new AuditLog
                    {
                        TableName = tableName,
                        RecordId = recordId,
                        Action = AuditAction.UPDATE,
                        FieldName = property.Metadata.Name,
                        OldValue = FormatFieldValue(property.Metadata.Name, property.OriginalValue),
                        NewValue = FormatFieldValue(property.Metadata.Name, property.CurrentValue),
                        UserId = userId,
                        IpAddress = ipAddress,
                        OccurredAt = utcNow
                    });
                }
            }
        }
    }

    private void FlushPendingInsertAudits(DbContext? context)
    {
        if (context is null || _pendingInsertFields.Count == 0)
        {
            CommitOwnedTransaction();
            return;
        }

        try
        {
            var logs = BuildInsertAuditLogs(context);
            _pendingInsertFields.Clear();
            if (logs.Count == 0)
            {
                CommitOwnedTransaction();
                return;
            }

            context.AddRange(logs);
            _isFlushing = true;
            try
            {
                context.SaveChanges();
            }
            finally
            {
                _isFlushing = false;
            }
            CommitOwnedTransaction();
        }
        catch
        {
            RollbackOwnedTransaction();
            throw;
        }
    }

    private async Task FlushPendingInsertAuditsAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null || _pendingInsertFields.Count == 0)
        {
            CommitOwnedTransaction();
            return;
        }

        try
        {
            var logs = BuildInsertAuditLogs(context);
            _pendingInsertFields.Clear();
            if (logs.Count == 0)
            {
                CommitOwnedTransaction();
                return;
            }

            context.AddRange(logs);
            _isFlushing = true;
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                _isFlushing = false;
            }
            CommitOwnedTransaction();
        }
        catch
        {
            RollbackOwnedTransaction();
            throw;
        }
    }

    private void CommitOwnedTransaction()
    {
        if (_ownedTransaction is null)
        {
            return;
        }

        _ownedTransaction.Commit();
        _ownedTransaction.Dispose();
        _ownedTransaction = null;
    }

    private void RollbackOwnedTransaction()
    {
        if (_ownedTransaction is null)
        {
            return;
        }

        try
        {
            _ownedTransaction.Rollback();
        }
        finally
        {
            _ownedTransaction.Dispose();
            _ownedTransaction = null;
        }
    }

    private List<AuditLog> BuildInsertAuditLogs(DbContext context)
    {
        var utcNow = DateTime.UtcNow;
        var userId = ResolveUserId();
        var ipAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        var logs = new List<AuditLog>(_pendingInsertFields.Count);

        foreach (var field in _pendingInsertFields)
        {
            var recordId = GetRecordId(field.Entry);

            logs.Add(new AuditLog
            {
                TableName = field.TableName,
                RecordId = recordId,
                Action = AuditAction.INSERT,
                FieldName = field.FieldName,
                OldValue = null,
                NewValue = FormatFieldValue(field.FieldName, field.Value),
                UserId = userId,
                IpAddress = ipAddress,
                OccurredAt = utcNow
            });
        }

        return logs;
    }

    private int? ResolveUserId() => _currentUser.UserId > 0 ? _currentUser.UserId : null;

    private static bool IsAuditLog(EntityEntry entry) => entry.Metadata.ClrType == typeof(AuditLog);

    // Not: bileşik anahtarlı tablolarda (ör. UserRoles) sadece ilk anahtar
    // kolonu RecordId'ye yazılır; AuditLog.RecordId tek int kolon olduğu için
    // tam bileşik anahtarı temsil edemez.
    private static int GetRecordId(EntityEntry entry)
    {
        var keyProperty = entry.Metadata.FindPrimaryKey()!.Properties[0];
        var value = entry.Property(keyProperty.Name).CurrentValue;
        return value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static string? FormatFieldValue(string fieldName, object? value)
    {
        if (string.Equals(fieldName, MaskedFieldName, StringComparison.Ordinal))
        {
            return value is null ? null : MaskedValue;
        }

        return FormatValue(value);
    }

    private static string? FormatValue(object? value) => value switch
    {
        null => null,
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("O", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("O", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    private sealed record PendingInsertField(EntityEntry Entry, string TableName, string FieldName, object? Value);
}
