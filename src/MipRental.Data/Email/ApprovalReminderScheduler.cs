using Microsoft.EntityFrameworkCore;
using MipRental.Domain.Approvals;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Security;

namespace MipRental.Data.Email;

/// <summary>
/// ADIM 15 — HATIRLATMA VE ESKALASYON TETİKLEYİCİSİ.
///
/// <see cref="ApprovalEscalationCalculator"/> "ne zaman" sorusunu zaten
/// hesaplıyordu ama tetikleyen yoktu. Bu sınıf açık onay adımlarını tarar ve
/// zamanı gelenler için BİLDİRİM ÜRETİR — göndermez; gönderim kuyruk
/// işleyicisinin işidir (NotificationDispatcher).
///
/// CLAUDE.md kural 5: otomatik onay yok. Burada da hiçbir adım kendiliğinden
/// onaylanmaz; yalnızca insanlara hatırlatılır.
///
/// FİYAT GİZLİLİĞİ: gövdeye TUTAR YAZILMAZ. Hatırlatmanın alıcısı adımın
/// rolündeki kişidir ve o rol (ör. EQUIPMENT_MANAGER) tutarı görmez.
/// </summary>
public sealed class ApprovalReminderScheduler
{
    public const string ReminderTemplate = "WR_APPROVAL_REMINDER";
    public const string EscalationTemplate = "WR_APPROVAL_ESCALATION";

    private readonly AppDbContext _db;

    public ApprovalReminderScheduler(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Zamanı gelen hatırlatma ve eskalasyonları kuyruğa yazar; üretilen
    /// bildirim satırı sayısını döner.
    /// </summary>
    public async Task<int> RunAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        // Karar verilmiş adım hiç sorguya girmez: hatırlatma da eskalasyon da
        // yalnızca AÇIK adımlar içindir.
        var open = await _db.Approvals.IgnoreQueryFilters()
            .Include(a => a.ApprovalFlowStep)
            .Where(a => a.Decision == null
                     && a.DocumentType == DocumentType.WORK_RECORD
                     && a.ApprovalFlowStep != null)
            .ToListAsync(cancellationToken);

        if (open.Count == 0)
        {
            return 0;
        }

        var documentNumbers = await DocumentNumbersAsync(open, cancellationToken);
        var queued = 0;

        foreach (var approval in open)
        {
            var step = approval.ApprovalFlowStep!;
            var documentNo = documentNumbers.GetValueOrDefault(approval.DocumentId, "-");

            // Hatırlatma: adım başına BİR KEZ (ReminderSentAt damgası).
            if (ApprovalEscalationCalculator.IsReminderDue(approval, step, utcNow))
            {
                queued += await QueueForRoleAsync(step.RoleId, ReminderTemplate,
                    $"Hatırlatma: {documentNo} onayınızı bekliyor",
                    $"{documentNo} numaralı çalışma kaydı \"{step.Name}\" adımında " +
                    $"{Waiting(approval, utcNow)} beklemektedir. Uygulamadan inceleyip karar verebilirsiniz.",
                    approval, cancellationToken);

                approval.ReminderSentAt = utcNow;
            }

            // Eskalasyon: adım başına BİR KEZ (EscalationSentAt damgası).
            if (approval.EscalationSentAt is null
                && ApprovalEscalationCalculator.IsEscalationDue(approval, step, utcNow))
            {
                var escalationRoleId = await EscalationRoleIdAsync(step, cancellationToken);

                queued += await QueueForRoleAsync(escalationRoleId, EscalationTemplate,
                    $"Eskalasyon: {documentNo} için onay süresi aşıldı",
                    $"{documentNo} numaralı çalışma kaydı \"{step.Name}\" adımında " +
                    $"{Waiting(approval, utcNow)} karara bağlanmadı ve eskalasyon süresi aşıldı.",
                    approval, cancellationToken);

                approval.EscalationSentAt = utcNow;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return queued;
    }

    /// <summary>
    /// Bildirimi o roldeki AKTİF MIP personeline yazar. Alıcı adresi kullanıcı
    /// girdisinden değil Users tablosundan gelir.
    /// </summary>
    private async Task<int> QueueForRoleAsync(
        int? roleId, string template, string subject, string body, Approval approval, CancellationToken cancellationToken)
    {
        if (roleId is not int role)
        {
            return 0;
        }

        var recipients = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.IsActive && u.FirmId == null && u.UserRoles.Any(ur => ur.RoleId == role))
            .Select(u => new { u.UserId, u.Email })
            .ToListAsync(cancellationToken);

        foreach (var recipient in recipients)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = recipient.UserId,
                Email = recipient.Email,
                Channel = NotificationChannel.EMAIL,
                TemplateCode = template,
                Subject = subject,
                Body = body,
                DocumentType = approval.DocumentType,
                DocumentId = approval.DocumentId,
                Status = NotificationStatus.QUEUED,
                CreatedAt = DateTime.UtcNow
            });
        }

        return recipients.Count;
    }

    /// <summary>
    /// Eskalasyon kime gider: akıştaki BİR SONRAKİ adımın rolüne. Son adımsa
    /// ADMIN'e — zincirin üstünde başka kimse yok, ama bildirim kaybolmamalı.
    /// </summary>
    private async Task<int?> EscalationRoleIdAsync(ApprovalFlowStep step, CancellationToken cancellationToken)
    {
        var nextRoleId = await _db.ApprovalFlowSteps.AsNoTracking()
            .Where(s => s.FlowId == step.FlowId && s.StepNo > step.StepNo)
            .OrderBy(s => s.StepNo)
            .Select(s => (int?)s.RoleId)
            .FirstOrDefaultAsync(cancellationToken);

        if (nextRoleId is not null)
        {
            return nextRoleId;
        }

        return await _db.Roles.AsNoTracking()
            .Where(r => r.Code == RoleCodes.Admin)
            .Select(r => (int?)r.RoleId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Dictionary<int, string>> DocumentNumbersAsync(
        List<Approval> approvals, CancellationToken cancellationToken)
    {
        var ids = approvals.Select(a => a.DocumentId).Distinct().ToList();

        return await _db.WorkRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(w => ids.Contains(w.WorkRecordId))
            .Select(w => new { w.WorkRecordId, w.DocumentNo })
            .ToDictionaryAsync(w => w.WorkRecordId, w => w.DocumentNo, cancellationToken);
    }

    /// <summary>"3 gün 4 saat" gibi; gövdede tutar yerine SÜRE bilgisi durur.</summary>
    private static string Waiting(Approval approval, DateTime utcNow)
    {
        var span = ApprovalEscalationCalculator.WaitingFor(approval, utcNow);
        return span.TotalDays >= 1
            ? $"{(int)span.TotalDays} gün {span.Hours} saattir"
            : $"{(int)span.TotalHours} saattir";
    }
}
