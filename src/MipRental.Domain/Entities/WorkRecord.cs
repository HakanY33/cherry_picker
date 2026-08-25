using MipRental.Domain.Enums;

namespace MipRental.Domain.Entities;

public class WorkRecord
{
    public int WorkRecordId { get; set; }
    public string DocumentNo { get; set; } = null!;
    public WorkRecordStatus Status { get; set; } = WorkRecordStatus.DRAFT;
    public WorkRecordIntegrationStatus IntegrationStatus { get; set; } = WorkRecordIntegrationStatus.NOT_SENT;

    public int? RequestId { get; set; }
    public int FirmId { get; set; }
    public int ContractId { get; set; }
    public int PeriodId { get; set; }

    public DateOnly WorkDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public bool SpansMidnight { get; set; }

    public int? LocationId { get; set; }
    public string? LocationText { get; set; }
    public string? WorkDescription { get; set; }

    public int? RequestedByUserId { get; set; }
    public int? WitnessedByUserId { get; set; }
    public int? DepartmentId { get; set; }

    public string? OperatorName { get; set; }
    public int? EquipmentId { get; set; }
    public string? LicensePlate { get; set; }
    public int? PersonnelCount { get; set; }

    public string? ExternalReceiptNo { get; set; }
    public DateOnly? ExternalReceiptDate { get; set; }

    // Sefer başı nakliye/mobilizasyon bedeli. Satır değil KAYIT seviyesindedir:
    // çok satırlı bir kayıtta da yalnızca bir kez uygulanır. Ayrı kolonda tutulur ki
    // TotalAmount ile satır tutarlarının toplamı arasındaki fark izlenebilir olsun.
    public decimal? MobilizationFee { get; set; }

    // TotalAmount = WorkRecordLines.Sum(LineAmount) + (MobilizationFee ?? 0)
    public decimal? TotalAmount { get; set; }
    public string? Currency { get; set; }

    public int EnteredByUserId { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public int? RevisionOfId { get; set; }
    public string? RevisionReason { get; set; }
    public bool IsSuperseded { get; set; }

    public Request? Request { get; set; }
    public Firm Firm { get; set; } = null!;
    public Contract Contract { get; set; } = null!;
    public Period Period { get; set; } = null!;
    public Location? Location { get; set; }
    public User? RequestedByUser { get; set; }
    public User? WitnessedByUser { get; set; }
    public Department? Department { get; set; }
    public Equipment? Equipment { get; set; }
    public User EnteredByUser { get; set; } = null!;
    public WorkRecord? RevisionOf { get; set; }
    public ICollection<WorkRecord> Revisions { get; set; } = new List<WorkRecord>();
    public ICollection<WorkRecordLine> WorkRecordLines { get; set; } = new List<WorkRecordLine>();
}
