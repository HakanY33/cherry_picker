using MipRental.Domain.Enums;

namespace MipRental.Web.Common;

/// <summary>
/// DURUM ROZETLERİNİN TEK RENK KAYNAĞI.
///
/// Sistemde beş ayrı durum kümesi var (çalışma kaydı, talep, hakediş, sözleşme,
/// dönem) ve her biri kendi ekranında ayrı ayrı renklendiriliyordu: aynı anlam
/// bir ekranda sarı, diğerinde gri görünüyordu. Renk mantığı artık burada, tek
/// yerde ve ANLAMA göre tanımlı:
///
///   bekleyen / taslak      → gri       (henüz süreç başlamadı)
///   işlemde                → mavi      (sıra ilerliyor, biri üzerinde çalışıyor)
///   onaylı / tamamlandı    → yeşil
///   reddedilen / iptal     → kırmızı
///   kilitli                → koyu gri  (dönem kapandı, kayıt dondu)
///
/// Etiketler burada TEKRARLANMAZ: Domain'deki *StatusLabels sözlüklerinden gelir
/// ki durum makinesinin hata mesajı ile ekrandaki ad ayrışmasın.
/// </summary>
public static class StatusBadge
{
    private const string Waiting = "bg-secondary";
    private const string Active = "bg-primary";
    private const string Done = "bg-success";
    private const string Failed = "bg-danger";
    private const string Locked = "bg-dark";

    public static string Class(WorkRecordStatus status) => status switch
    {
        WorkRecordStatus.DRAFT => Waiting,

        // Revizyon istenen kayıt da SÜREÇTEDİR: reddedilmedi, düzeltilip geri gelecek.
        WorkRecordStatus.SUBMITTED or WorkRecordStatus.PENDING or WorkRecordStatus.REVISION_REQUESTED => Active,

        WorkRecordStatus.APPROVED => Done,
        WorkRecordStatus.REJECTED or WorkRecordStatus.CANCELLED => Failed,
        WorkRecordStatus.LOCKED => Locked,
        _ => Waiting
    };

    public static string Class(RequestStatus status) => status switch
    {
        RequestStatus.DRAFT => Waiting,
        RequestStatus.SUBMITTED
            or RequestStatus.PENDING_EQUIPMENT
            or RequestStatus.PENDING_FIRM
            or RequestStatus.SCHEDULED
            or RequestStatus.IN_PROGRESS => Active,
        RequestStatus.COMPLETED => Done,
        RequestStatus.REJECTED_BY_EQUIPMENT
            or RequestStatus.REJECTED_BY_FIRM
            or RequestStatus.CANCELLED => Failed,
        _ => Waiting
    };

    /// <summary>
    /// Talep açana gösterilen SADELEŞTİRİLMİŞ etiketin rengi. Gerçek duruma göre
    /// renklendirmek yanıltıyordu: SCHEDULED satırında etiket "Onaylandı" yazıp
    /// rozet mavi (işlemde) çıkıyordu. Renk, kullanıcının OKUDUĞU etiketi izler.
    /// </summary>
    public static string SummaryClass(RequestStatus status) => status switch
    {
        RequestStatus.DRAFT => Waiting,
        RequestStatus.SUBMITTED or RequestStatus.PENDING_EQUIPMENT or RequestStatus.PENDING_FIRM => Waiting,
        RequestStatus.SCHEDULED or RequestStatus.IN_PROGRESS => Done,     // ekranda "Onaylandı"
        RequestStatus.COMPLETED => Done,                                  // ekranda "Tamamlandı"
        RequestStatus.REJECTED_BY_EQUIPMENT
            or RequestStatus.REJECTED_BY_FIRM
            or RequestStatus.CANCELLED => Failed,
        _ => Waiting
    };

    public static string Class(ProgressPaymentStatus status) => status switch
    {
        ProgressPaymentStatus.DRAFT => Waiting,
        ProgressPaymentStatus.PENDING_BUDGET_MANAGER => Active,
        ProgressPaymentStatus.APPROVED => Done,
        ProgressPaymentStatus.REJECTED => Failed,
        _ => Waiting
    };

    public static string Class(ContractStatus status) => status switch
    {
        ContractStatus.DRAFT => Waiting,
        ContractStatus.ACTIVE => Done,

        // Süresi dolmuş sözleşme reddedilmiş değil, ARTIK KULLANILMIYOR:
        // kapalı dönemle aynı anlam, aynı renk.
        ContractStatus.EXPIRED => Locked,
        ContractStatus.TERMINATED => Failed,
        _ => Waiting
    };

    public static string Class(PeriodStatus status) => status switch
    {
        PeriodStatus.OPEN => Done,
        PeriodStatus.CLOSED => Locked,
        PeriodStatus.REOPENED => Active,
        _ => Waiting
    };

    /// <summary>
    /// Bildirim kuyruğu (Adım 15). "Atlandı" hata değildir ama gönderilmemiştir:
    /// kapalı dönem/kilit gibi, sürecin dışında kalan bir durum — koyu gri.
    /// </summary>
    public static string Class(NotificationStatus status) => status switch
    {
        NotificationStatus.QUEUED => Waiting,
        NotificationStatus.SENDING => Active,
        NotificationStatus.SENT => Done,
        NotificationStatus.FAILED => Failed,
        NotificationStatus.SKIPPED_EXTERNAL => Locked,
        _ => Waiting
    };

    public static string Class(ApprovalDecision decision) => decision switch
    {
        ApprovalDecision.APPROVED => Done,
        ApprovalDecision.REJECTED => Failed,
        ApprovalDecision.REVISION_REQUESTED => Active,
        _ => Waiting
    };
}
