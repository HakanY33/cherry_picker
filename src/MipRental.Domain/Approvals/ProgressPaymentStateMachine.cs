using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;
using MipRental.Domain.Security;

namespace MipRental.Domain.Approvals;

/// <summary>
/// Hakediş durum geçişlerinin TEK KAYNAĞI. Çalışma kaydı ve talep makineleriyle
/// aynı desen: controller/servis <see cref="ProgressPayment.Status"/>'a doğrudan
/// atama YAPMAZ, her geçiş buradan geçer ve geçiş izni + yetki birlikte kontrol
/// edilir.
///
/// Sınıf SAFTIR: veritabanı, oturum, DateTime.UtcNow yok — karar için gereken her
/// şey parametre gelir.
///
/// Dönem kontrolü BİLİNÇLİ OLARAK yok. Hakediş kapanmış bir dönemin ödeme
/// belgesidir; dönem kapandıktan sonra da onaylanabilmesi gerekir. Kilit
/// çalışma kayıtlarını korur, hakedişin kendisini değil.
/// </summary>
public static class ProgressPaymentStateMachine
{
    public static readonly IReadOnlyDictionary<ProgressPaymentStatus, IReadOnlySet<ProgressPaymentStatus>> AllowedTransitions =
        new Dictionary<ProgressPaymentStatus, IReadOnlySet<ProgressPaymentStatus>>
        {
            [ProgressPaymentStatus.DRAFT] = Set(ProgressPaymentStatus.PENDING_BUDGET_MANAGER),
            // DRAFT'a dönüş = GERİ ÇEKME (B8). Bütçe hatayı fark ettiğinde hakedişi
            // yöneticiden geri alabilir; bu geçişte mail token'ları da iptal edilir,
            // yoksa mail kutusundaki eski bağlantı çalışmaya devam ederdi.
            [ProgressPaymentStatus.PENDING_BUDGET_MANAGER] = Set(
                ProgressPaymentStatus.APPROVED,
                ProgressPaymentStatus.REJECTED,
                ProgressPaymentStatus.DRAFT),

            // Onaylanan hakediş ödemeye esastır; reddedilen hakediş de yeniden
            // canlandırılmaz — düzeltme, reddi gerekçesiyle bırakıp yeni dönem
            // kapanışında yeni hakediş açmaktır.
            [ProgressPaymentStatus.APPROVED] = Set(),
            [ProgressPaymentStatus.REJECTED] = Set()
        };

    public static bool IsAllowed(ProgressPaymentStatus from, ProgressPaymentStatus to) =>
        AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>
    /// DRAFT -> PENDING_BUDGET_MANAGER. Bütçe hakedişi onaylar ve yöneticiye
    /// gönderir; bu geçiş Bütçe'nin İMZASIDIR, sonrasında karar yöneticinindir.
    /// </summary>
    public static void SendToManager(ProgressPayment payment, TransitionActor actor, string? budgetNote, DateTime nowUtc)
    {
        EnsureTransitionAllowed(payment, ProgressPaymentStatus.PENDING_BUDGET_MANAGER);
        EnsureBudget(actor, "yöneticiye gönderebilir");

        payment.BudgetNote = string.IsNullOrWhiteSpace(budgetNote) ? null : budgetNote.Trim();
        payment.BudgetApprovedByUserId = actor.UserId;
        payment.BudgetApprovedAt = nowUtc;
        payment.Status = ProgressPaymentStatus.PENDING_BUDGET_MANAGER;
    }

    /// <summary>
    /// PENDING_BUDGET_MANAGER -> APPROVED. Kararı Bütçe Yöneticisi verir; mail
    /// üzerinden gelen karar da bu metottan geçer (aktör token'ın bağlı olduğu
    /// kullanıcıdır), ekrandan gelen karar da. İki yol tek makinede birleşir.
    /// </summary>
    public static void Approve(ProgressPayment payment, TransitionActor actor, string? note, DateTime nowUtc)
    {
        EnsureTransitionAllowed(payment, ProgressPaymentStatus.APPROVED);
        EnsureBudgetManager(actor, "onaylayabilir");

        payment.ManagerNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        payment.ManagerApprovedByUserId = actor.UserId;
        payment.ManagerApprovedAt = nowUtc;
        payment.Status = ProgressPaymentStatus.APPROVED;
    }

    /// <summary>
    /// PENDING_BUDGET_MANAGER -> DRAFT. Bütçe hakedişi geri çeker.
    ///
    /// Bütçe'nin imzası (onaylayan + zaman) SİLİNİR: geri çekilen hakediş yeniden
    /// gönderilirken yeniden imzalanır. Silinen değerler denetim izinde durur.
    /// Token iptali burada değil çağıranda: makine veritabanına dokunmaz.
    /// </summary>
    public static void Withdraw(ProgressPayment payment, TransitionActor actor)
    {
        EnsureTransitionAllowed(payment, ProgressPaymentStatus.DRAFT);
        EnsureBudget(actor, "geri çekebilir");

        payment.BudgetApprovedByUserId = null;
        payment.BudgetApprovedAt = null;
        payment.Status = ProgressPaymentStatus.DRAFT;
    }

    /// <summary>PENDING_BUDGET_MANAGER -> REJECTED. Gerekçe ZORUNLU.</summary>
    public static void Reject(ProgressPayment payment, TransitionActor actor, string? reason, DateTime nowUtc)
    {
        EnsureTransitionAllowed(payment, ProgressPaymentStatus.REJECTED);
        EnsureBudgetManager(actor, "reddedebilir");

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ProgressPaymentStateTransitionException(
                "Red gerekçesi zorunludur; boş bırakılamaz.");
        }

        payment.RejectionReason = reason.Trim();
        payment.ManagerApprovedByUserId = actor.UserId;
        payment.ManagerApprovedAt = nowUtc;
        payment.Status = ProgressPaymentStatus.REJECTED;
    }

    private static void EnsureTransitionAllowed(ProgressPayment payment, ProgressPaymentStatus to)
    {
        if (IsAllowed(payment.Status, to))
        {
            return;
        }

        throw new ProgressPaymentStateTransitionException(
            $"Hakediş \"{ProgressPaymentStatusLabels.Get(payment.Status)}\" durumundan " +
            $"\"{ProgressPaymentStatusLabels.Get(to)}\" durumuna geçemez.");
    }

    // Bütçe MIP personelidir: firma kullanıcısının rolü olsa bile hakedişi
    // yürütemez (kural 7'nin yetki tarafı).
    private static void EnsureBudget(TransitionActor actor, string verb)
    {
        if (!actor.IsMipStaff || !actor.IsInRole(RoleCodes.Budget))
        {
            throw new ApprovalAuthorizationException(
                $"Hakedişi yalnızca Bütçe {verb}.");
        }
    }

    private static void EnsureBudgetManager(TransitionActor actor, string verb)
    {
        if (!actor.IsMipStaff || !actor.IsInRole(RoleCodes.BudgetManager))
        {
            throw new ApprovalAuthorizationException(
                $"Hakedişi yalnızca Bütçe Yöneticisi {verb}.");
        }
    }

    private static IReadOnlySet<ProgressPaymentStatus> Set(params ProgressPaymentStatus[] statuses) =>
        new HashSet<ProgressPaymentStatus>(statuses);
}
