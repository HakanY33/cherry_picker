using MipRental.Domain.Entities;

namespace MipRental.Domain.Approvals;

/// <summary>
/// CLAUDE.md kural 5: otomatik onay YOKTUR. Onay gelmezse hatırlatma, sonra
/// eskalasyon olur. Bu sınıf SADECE "ne zaman" sorusunu hesaplar — hatırlatmayı
/// kimse burada göndermez, tetikleyici (zamanlayıcı) sonraki adımın işi.
///
/// Saf: DateTime.UtcNow'a bakmaz, "şimdi" parametre olarak gelir.
/// </summary>
public static class ApprovalEscalationCalculator
{
    public static DateTime? ReminderDueAt(Approval approval, ApprovalFlowStep step) =>
        step.ReminderAfterHours is int hours and > 0 ? approval.AssignedAt.AddHours(hours) : null;

    public static DateTime? EscalationDueAt(Approval approval, ApprovalFlowStep step) =>
        step.EscalateAfterHours is int hours and > 0 ? approval.AssignedAt.AddHours(hours) : null;

    /// <summary>
    /// Hatırlatma gönderilmeli mi? Karar verilmiş adıma hatırlatma gitmez ve
    /// aynı adım için hatırlatma yalnızca bir kez üretilir (ReminderSentAt).
    /// </summary>
    public static bool IsReminderDue(Approval approval, ApprovalFlowStep step, DateTime utcNow)
    {
        if (approval.Decision is not null || approval.ReminderSentAt is not null)
        {
            return false;
        }

        return ReminderDueAt(approval, step) is DateTime due && utcNow >= due;
    }

    /// <summary>
    /// Eskalasyon zamanı geldi mi? Karar verilmiş adım eskale edilmez.
    /// </summary>
    public static bool IsEscalationDue(Approval approval, ApprovalFlowStep step, DateTime utcNow)
    {
        if (approval.Decision is not null)
        {
            return false;
        }

        return EscalationDueAt(approval, step) is DateTime due && utcNow >= due;
    }

    /// <summary>Adımın ne kadardır beklediği — ekranda "3 gündür bekliyor" için.</summary>
    public static TimeSpan WaitingFor(Approval approval, DateTime utcNow) =>
        utcNow > approval.AssignedAt ? utcNow - approval.AssignedAt : TimeSpan.Zero;
}
