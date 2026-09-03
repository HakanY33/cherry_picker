using MipRental.Domain.Enums;

namespace MipRental.Domain.Entities;

public class Request
{
    public int RequestId { get; set; }
    public string DocumentNo { get; set; } = null!;
    public RequestStatus Status { get; set; } = RequestStatus.DRAFT;

    public int RequestedByUserId { get; set; }
    public int DepartmentId { get; set; }
    public int? FirmId { get; set; }

    public DateOnly IssueDate { get; set; }
    public DateOnly RequestedDate { get; set; }
    public TimeOnly? RequestedStartTime { get; set; }
    public TimeOnly? RequestedEndTime { get; set; }
    public int? LocationId { get; set; }
    public string? LocationText { get; set; }
    public string? WorkDescription { get; set; }

    // --- Firma yetkilisi doldurur (PENDING_FIRM -> SCHEDULED) ---
    public string? AssignedOperatorName { get; set; }
    public string? AssignedLicensePlate { get; set; }

    // --- Operatör; SUNUCU saatiyle damgalanır (SCHEDULED -> IN_PROGRESS -> COMPLETED) ---
    // TimeOnly değil DateTime: bunlar "kaçta" değil "ne zaman" bilgisidir ve
    // gece yarısını geçen işte TimeOnly sıralanamaz. Veritabanında UTC.
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }

    // --- Gerekçeler. Red ve iptal için ZORUNLU (RequestStateMachine zorlar) ---
    public string? RejectionReason { get; set; }
    public string? CancellationReason { get; set; }

    // --- Durum geçiş zaman damgaları ---
    //
    // Her duruma bir damga AÇILMADI; her KARAR NOKTASINA bir damga açıldı:
    //   SubmittedAt         talep sahibinin elinden çıktı
    //   EquipmentDecisionAt Ekipman Müdürlüğü karar verdi (onay VEYA red)
    //   FirmDecisionAt      firma karar verdi (kabul VEYA red)
    //   CancelledAt         iptal edildi
    //
    // Onay ve red için ayrı sütun tutulmadı: ikisi aynı karar noktasının iki
    // sonucudur, hangisi olduğu Status'ta zaten yazılıdır — ayrı sütun ikisinin
    // birden dolu olabildiği imkânsız durumlar üretirdi.
    //
    // IN_PROGRESS ve COMPLETED'ın ayrı damgası YOK: ActualStartTime ve
    // ActualEndTime tam olarak o iki anı tutuyor. İkinci bir sütun aynı bilgiyi
    // ikinci kez saklar ve zamanla ayrışır.
    //
    // DRAFT'ın damgası YOK: CreatedAt zaten o an.
    public DateTime? SubmittedAt { get; set; }
    public DateTime? EquipmentDecisionAt { get; set; }
    public DateTime? FirmDecisionAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public User RequestedByUser { get; set; } = null!;
    public Department Department { get; set; } = null!;
    public Firm? Firm { get; set; }
    public Location? Location { get; set; }
    public ICollection<RequestLine> RequestLines { get; set; } = new List<RequestLine>();
    public ICollection<WorkRecord> WorkRecords { get; set; } = new List<WorkRecord>();
}
