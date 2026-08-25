using Microsoft.EntityFrameworkCore;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Data.Services;

/// <summary>
/// Bildirimleri SADECE Notifications tablosuna yazar. GERÇEK E-POSTA GÖNDERMEZ:
/// SMTP yapılandırması yok, arka plan zamanlayıcı yok — kayıtlar QUEUED durumunda
/// bekler, gönderim sonraki adımın işi.
///
/// Kayıtlar çağıranın SaveChanges'ine dahil edilir (ayrı SaveChanges YOK) ki
/// bildirim ile durum değişikliği aynı transaction'da atomik olsun: onay
/// commit olmadıysa bildirim de düşmez.
/// </summary>
public sealed class NotificationQueue
{
    private readonly AppDbContext _db;

    public NotificationQueue(AppDbContext db)
    {
        _db = db;
    }

    public static class Templates
    {
        public const string ApprovalPending = "WR_APPROVAL_PENDING";
        public const string Approved = "WR_APPROVED";
        public const string Rejected = "WR_REJECTED";
        public const string RevisionRequested = "WR_REVISION_REQUESTED";
        public const string LineObjected = "WR_LINE_OBJECTED";
    }

    /// <summary>
    /// Sıradaki onay adımının rolündeki MIP kullanıcılarına "onayınızı bekliyor".
    /// Rolde kimse yoksa bildirim düşmez — onay yine de bekler (otomatik onay YOK).
    /// </summary>
    public async Task<int> QueueApprovalPendingAsync(
        WorkRecord record, ApprovalFlowStep step, CancellationToken cancellationToken = default)
    {
        // Rol ataması kullanıcı bazlı değil rol bazlı: adımın rolündeki tüm aktif
        // MIP personeline (FirmId = null) haber verilir.
        var recipients = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.IsActive && u.FirmId == null && u.UserRoles.Any(ur => ur.RoleId == step.RoleId))
            .Select(u => new { u.UserId, u.Email })
            .ToListAsync(cancellationToken);

        var subject = $"Onayınızı bekliyor: {record.DocumentNo}";
        var body =
            $"{record.DocumentNo} numaralı çalışma kaydı \"{step.Name}\" adımında onayınızı bekliyor. " +
            $"İş tarihi: {record.WorkDate:dd.MM.yyyy}. Tutar: {record.TotalAmount:N2} {record.Currency}.";

        foreach (var recipient in recipients)
        {
            Enqueue(recipient.UserId, recipient.Email, Templates.ApprovalPending, subject, body, record.WorkRecordId);
        }

        return recipients.Count;
    }

    /// <summary>Karar sonucu alt yükleniciye (kaydı giren kullanıcıya) bildirilir.</summary>
    public async Task QueueDecisionAsync(
        WorkRecord record, ApprovalDecision decision, string? reason, CancellationToken cancellationToken = default)
    {
        var (template, subject, headline) = decision switch
        {
            ApprovalDecision.APPROVED => (Templates.Approved, $"Onaylandı: {record.DocumentNo}",
                $"{record.DocumentNo} numaralı çalışma kaydınız onaylandı."),
            ApprovalDecision.REJECTED => (Templates.Rejected, $"Reddedildi: {record.DocumentNo}",
                $"{record.DocumentNo} numaralı çalışma kaydınız reddedildi."),
            ApprovalDecision.REVISION_REQUESTED => (Templates.RevisionRequested, $"Revizyon istendi: {record.DocumentNo}",
                $"{record.DocumentNo} numaralı çalışma kaydınız için revizyon istendi."),
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Bilinmeyen onay kararı.")
        };

        var body = string.IsNullOrWhiteSpace(reason) ? headline : $"{headline} Gerekçe: {reason}";

        await QueueForRecordOwnerAsync(record, template, subject, body, cancellationToken);
    }

    /// <summary>Satır bazlı itiraz: alt yüklenici HANGİ satıra NEDEN itiraz edildiğini görsün.</summary>
    public async Task QueueLineObjectionAsync(
        WorkRecord record, IReadOnlyCollection<WorkRecordLine> objectedLines, CancellationToken cancellationToken = default)
    {
        var lineList = string.Join("; ", objectedLines.Select(l => $"{l.LineNo}. satır: {l.ObjectionReason}"));
        var subject = $"Satır itirazı: {record.DocumentNo}";
        var body =
            $"{record.DocumentNo} numaralı çalışma kaydınızın {objectedLines.Count} satırına itiraz edildi. {lineList}";

        await QueueForRecordOwnerAsync(record, Templates.LineObjected, subject, body, cancellationToken);
    }

    private async Task QueueForRecordOwnerAsync(
        WorkRecord record, string template, string subject, string body, CancellationToken cancellationToken)
    {
        var recipient = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.UserId == record.EnteredByUserId)
            .Select(u => new { u.UserId, u.Email })
            .FirstOrDefaultAsync(cancellationToken);

        if (recipient is null)
        {
            return;
        }

        Enqueue(recipient.UserId, recipient.Email, template, subject, body, record.WorkRecordId);
    }

    private void Enqueue(int userId, string? email, string template, string subject, string body, int workRecordId)
    {
        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Email = email,
            Channel = NotificationChannel.EMAIL,
            TemplateCode = template,
            Subject = subject,
            Body = body,
            DocumentType = DocumentType.WORK_RECORD,
            DocumentId = workRecordId,
            Status = NotificationStatus.QUEUED,
            CreatedAt = DateTime.UtcNow
        });
    }
}
