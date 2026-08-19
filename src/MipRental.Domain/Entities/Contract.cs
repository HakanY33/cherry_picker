using MipRental.Domain.Enums;

namespace MipRental.Domain.Entities;

public class Contract
{
    public int ContractId { get; set; }
    public int FirmId { get; set; }
    public string ContractNo { get; set; } = null!;
    public string? Title { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Currency { get; set; } = "TRY";
    public ContractStatus Status { get; set; } = ContractStatus.ACTIVE;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }

    public Firm Firm { get; set; } = null!;
    public User? CreatedByUser { get; set; }
    public ICollection<ContractLine> ContractLines { get; set; } = new List<ContractLine>();
    public ICollection<WorkRecord> WorkRecords { get; set; } = new List<WorkRecord>();
}
