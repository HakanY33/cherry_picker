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

    /// <summary>
    /// "Bitirdim" diyen operatörün KULLANICI kimliği (Adım 16).
    ///
    /// AssignedOperatorName serbest metindir (firma yetkilisi yazar) ve kimlik
    /// taşımaz. Türeyen çalışma kaydının "kaydı giren" alanı buradan gelir:
    /// türetme artık teyitle, yani MIP personelinin tıklamasıyla tetikleniyor;
    /// oturumdaki kullanıcıyı yazsaydık kaydı firma adına bir MIP kullanıcısı
    /// girmiş görünür ve firma ekranında MIP personelinin adı belirirdi.
    /// </summary>
    public int? CompletedByUserId { get; set; }

    // --- Gerekçeler. Red ve iptal için ZORUNLU (RequestStateMachine zorlar) ---
    public string? RejectionReason { get; set; }
    public string? CancellationReason { get; set; }

    /// <summary>Talep açanın süre itirazı gerekçesi. İtirazda ZORUNLU (Adım 16).</summary>
    public string? DisputeReason { get; set; }

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

    /// <summary>
    /// Talep açanın süre kararı — TEYİT veya İTİRAZ (Adım 16). Aynı karar
    /// noktasının iki sonucu, tek damga: hangisi olduğu Status'ta yazılı.
    /// </summary>
    public DateTime? ConfirmationDecisionAt { get; set; }

    /// <summary>
    /// Ekipman Müdürlüğü'nün itiraz hakemliği kararı — onay veya
    /// "faturalanmayacak". İptalde CancelledAt de dolar; ikisi ayrı sorulara
    /// cevap verir: "hakem ne zaman karar verdi" ve "talep ne zaman iptal oldu".
    /// </summary>
    public DateTime? DisputeResolvedAt { get; set; }

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
