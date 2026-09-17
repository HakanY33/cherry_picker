using Microsoft.EntityFrameworkCore;
using MipRental.Data.Services;
using MipRental.Domain.Abstractions;
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

    // Adım 16 — süre teyidi hatırlatması ve eskalasyonu.
    public const string ConfirmationReminderTemplate = "REQ_CONFIRM_REMINDER";
    public const string ConfirmationEscalationTemplate = "REQ_CONFIRM_ESCALATION";

    private readonly AppDbContext _db;
    private readonly NotificationQueue _notifications;
    private readonly EmailOptions _options;

    public ApprovalReminderScheduler(AppDbContext db, NotificationQueue notifications, EmailOptions options)
    {
        _db = db;
        _notifications = notifications;
        _options = options;
    }

    /// <summary>
    /// Zamanı gelen hatırlatma ve eskalasyonları kuyruğa yazar; üretilen
    /// bildirim satırı sayısını döner.
    /// </summary>
    public async Task<int> RunAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        // Karar verilmiş adım hiç sorguya girmez: hatırlatma da eskalasyon da
        // yalnızca AÇIK adımlar içindir.
        var queuedTotal = await RunDurationConfirmationAsync(utcNow, cancellationToken);

        var open = await _db.Approvals.IgnoreQueryFilters()
            .Include(a => a.ApprovalFlowStep)
            .Where(a => a.Decision == null
                     && a.DocumentType == DocumentType.WORK_RECORD
                     && a.ApprovalFlowStep != null)
            .ToListAsync(cancellationToken);

        if (open.Count == 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            return queuedTotal;
        }

        var documentNumbers = await DocumentNumbersAsync(open, cancellationToken);
        var queued = queuedTotal;

        foreach (var approval in open)
        {
            var step = approval.ApprovalFlowStep!;
            var documentNo = documentNumbers.GetValueOrDefault(approval.DocumentId, "-");

            // Hatırlatma: adım başına BİR KEZ (ReminderSentAt damgası).
            if (ApprovalEscalationCalculator.IsReminderDue(approval, step, utcNow))
            {
                queued += await QueueForRoleAsync(step.RoleId, ReminderTemplate,
                    NotificationQueue.Subject(documentNo, $"Hatırlatma: \"{step.Name}\" onayınızı bekliyor"),
                    $"{documentNo} numaralı çalışma kaydı \"{step.Name}\" adımında " +
                    $"{Waiting(approval, utcNow)} beklemektedir. Uygulamada \"Onayımı Bekleyenler\" " +
                    "ekranından inceleyip karar verebilirsiniz.",
                    approval, cancellationToken);

                approval.ReminderSentAt = utcNow;
            }

            // Eskalasyon: adım başına BİR KEZ (EscalationSentAt damgası).
            if (approval.EscalationSentAt is null
                && ApprovalEscalationCalculator.IsEscalationDue(approval, step, utcNow))
            {
                var escalationRoleId = await EscalationRoleIdAsync(step, cancellationToken);

                queued += await QueueForRoleAsync(escalationRoleId, EscalationTemplate,
                    NotificationQueue.Subject(documentNo, "Eskalasyon: onay süresi aşıldı"),
                    $"{documentNo} numaralı çalışma kaydı \"{step.Name}\" adımında " +
                    $"{Waiting(approval, utcNow)} karara bağlanmadı ve eskalasyon süresi aşıldı. " +
                    "Sistem hiçbir koşulda kendiliğinden onaylamaz; adımın sahibiyle görüşülmesi gerekiyor.",
                    approval, cancellationToken);

                approval.EscalationSentAt = utcNow;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return queued;
    }

    /// <summary>
    /// ADIM 16 — SÜRE TEYİDİ HATIRLATMASI VE ESKALASYONU.
    ///
    /// Teyit bekleyen (COMPLETED) talepler taranır. Süresi geçene önce
    /// hatırlatma gider, daha da gecikirse Ekipman Müdürlüğü'ne eskale edilir.
    /// OTOMATİK TEYİT YOKTUR (CLAUDE.md kural 5): hiçbir talep kendiliğinden
    /// CONFIRMED olmaz, yalnızca insanlar dürtülür — talep yerinde bekler.
    ///
    /// "Bir kez gönder" garantisi AYRI SÜTUNLA değil KUYRUĞUN KENDİSİYLE
    /// veriliyor: aynı talep için aynı şablondan ikinci satır yazılmaz. Onay
    /// adımındaki ReminderSentAt damgasının burada karşılığı yok çünkü teyidin
    /// Approvals satırı da yok — iki sütun açmak, cevabı zaten elimizde olan bir
    /// soruyu ikinci kez saklamak olurdu.
    ///
    /// FİYAT GİZLİLİĞİ: gövdede tutar geçmez; alıcılar zaten fiyat görmeyen
    /// rollerdir (talep açan ve Ekipman Müdürlüğü).
    /// </summary>
    private async Task<int> RunDurationConfirmationAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var reminderHours = _options.ConfirmationReminderHours;
        var escalationHours = _options.ConfirmationEscalationHours;

        if (reminderHours <= 0 && escalationHours <= 0)
        {
            return 0;
        }

        var pending = await _db.Requests.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.Status == RequestStatus.COMPLETED && r.ActualEndTime != null)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return 0;
        }

        var ids = pending.Select(r => r.RequestId).ToList();
        var alreadySent = await _db.Notifications.AsNoTracking()
            .Where(n => n.DocumentType == DocumentType.REQUEST
                     && n.DocumentId != null && ids.Contains(n.DocumentId.Value)
                     && (n.TemplateCode == ConfirmationReminderTemplate
                      || n.TemplateCode == ConfirmationEscalationTemplate))
            .Select(n => new { n.DocumentId, n.TemplateCode })
            .ToListAsync(cancellationToken);

        var sentKeys = alreadySent
            .Select(n => (n.DocumentId!.Value, n.TemplateCode))
            .ToHashSet();

        var queued = 0;

        foreach (var request in pending)
        {
            var waitingSince = request.ActualEndTime!.Value;
            var waiting = utcNow > waitingSince ? utcNow - waitingSince : TimeSpan.Zero;

            if (reminderHours > 0
                && waiting >= TimeSpan.FromHours(reminderHours)
                && sentKeys.Add((request.RequestId, ConfirmationReminderTemplate)))
            {
                queued += await _notifications.QueueRequestEventAsync(request,
                    ConfirmationReminderTemplate,
                    NotificationQueue.Subject(request.DocumentNo, "Hatırlatma: süre teyidiniz bekleniyor"),
                    $"{request.DocumentNo} numaralı talebinizde gerçekleşen süre {Waiting(waiting)} " +
                    "teyidinizi bekliyor. Teyit vermediğiniz sürece bu işten çalışma kaydı oluşmaz. " +
                    "Uygulamada \"Taleplerim\" ekranından talebi açıp süreyi onaylayın ya da " +
                    "gerekçesiyle itiraz edin.",
                    toRequester: true, cancellationToken: cancellationToken);
            }

            if (escalationHours > 0
                && waiting >= TimeSpan.FromHours(escalationHours)
                && sentKeys.Add((request.RequestId, ConfirmationEscalationTemplate)))
            {
                queued += await _notifications.QueueRequestEventAsync(request,
                    ConfirmationEscalationTemplate,
                    NotificationQueue.Subject(request.DocumentNo, "Eskalasyon: süre teyidi verilmedi"),
                    $"{request.DocumentNo} numaralı talepte gerçekleşen süre {Waiting(waiting)} teyit " +
                    "edilmedi ve eskalasyon süresi aşıldı. Talep açanla görüşülmesi gerekiyor; " +
                    "teyit gelmeden bu iş hakedişe giremez. Sistem hiçbir koşulda kendiliğinden " +
                    "teyit vermez.",
                    toEquipment: true, cancellationToken: cancellationToken);
            }
        }

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
    private static string Waiting(Approval approval, DateTime utcNow) =>
        Waiting(ApprovalEscalationCalculator.WaitingFor(approval, utcNow));

    private static string Waiting(TimeSpan span) =>
        span.TotalDays >= 1
            ? $"{(int)span.TotalDays} gün {span.Hours} saattir"
            : $"{(int)span.TotalHours} saattir";
}
