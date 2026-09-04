using MipRental.Domain.Enums;

namespace MipRental.Domain.Entities;

/// <summary>
/// HAKEDİŞ — bir dönem + bir firma için ödemeye esas tutarın ANLIK GÖRÜNTÜSÜ.
///
/// Ad seçimi: "Payroll" maaş bordrosudur, bu kayıt personel ücreti değil alt
/// yükleniciye ödenecek hizmet bedelidir; sözleşme literatüründeki karşılığı
/// "progress payment"tır. Tablo adı ProgressPayments, ekranda "Hakediş".
///
/// Neden ayrı bir kayıt: aylık icmal her açıldığında yeniden hesaplanır, hakediş
/// ise DONDURULUR. Hakediş oluştuktan sonra o dönemde yeni bir kayıt onaylanırsa
/// icmal büyür ama hakediş büyümez — yoksa onaylanan tutar ile ödenen tutar
/// ayrışırdı. Hangi çalışma kayıtlarının dahil olduğu
/// <see cref="ProgressPaymentRecord"/> tablosunda satır satır saklanır.
///
/// Bir dönem + bir firma için TEK hakediş olur (UQ_ProgressPayments_Period_Firm).
/// </summary>
public class ProgressPayment
{
    public int ProgressPaymentId { get; set; }

    public int PeriodId { get; set; }
    public int FirmId { get; set; }

    public ProgressPaymentStatus Status { get; set; } = ProgressPaymentStatus.DRAFT;

    /// <summary>Dondurulmuş toplam. Satırlardan yeniden hesaplanmaz.</summary>
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "TRY";

    /// <summary>Hakedişe giren çalışma kaydı sayısı (dondurulmuş).</summary>
    public int RecordCount { get; set; }

    /// <summary>
    /// Hakediş oluşturulurken o dönemde onay bekleyen kayıt sayısı. Engel değil,
    /// KAYIT: "bu hakediş kurulurken 3 kayıt hâlâ onaydaydı" bilgisi sonradan
    /// sorulduğunda cevapsız kalmasın.
    /// </summary>
    public int PendingRecordCountAtCreation { get; set; }

    /// <summary>Bütçe'nin serbest metin notu; Bütçe Yöneticisi'ne mailde de gider.</summary>
    public string? BudgetNote { get; set; }

    public int? BudgetApprovedByUserId { get; set; }
    public DateTime? BudgetApprovedAt { get; set; }

    public int? ManagerApprovedByUserId { get; set; }
    public DateTime? ManagerApprovedAt { get; set; }

    /// <summary>Red gerekçesi. Reddederken ZORUNLU (durum makinesi zorlar).</summary>
    public string? RejectionReason { get; set; }

    /// <summary>Bütçe Yöneticisi'nin karar notu (onayda da doldurulabilir).</summary>
    public string? ManagerNote { get; set; }

    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public Period Period { get; set; } = null!;
    public Firm Firm { get; set; } = null!;
    public User? BudgetApprovedByUser { get; set; }
    public User? ManagerApprovedByUser { get; set; }

    public ICollection<ProgressPaymentRecord> Records { get; set; } = new List<ProgressPaymentRecord>();

    /// <summary>Mail onayı bağlantıları (ADR-015). Hakediş dışına taşmaz (B9).</summary>
    public ICollection<ApprovalToken> ApprovalTokens { get; set; } = new List<ApprovalToken>();
}
