using MipRental.Domain.Enums;

namespace MipRental.Domain.Entities;

public class Period
{
    public int PeriodId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public PeriodStatus Status { get; set; } = PeriodStatus.OPEN;
    public DateTime? ClosedAt { get; set; }
    public int? ClosedBy { get; set; }
    public DateTime? ReopenedAt { get; set; }
    public int? ReopenedBy { get; set; }
    public string? ReopenReason { get; set; }

    public User? ClosedByUser { get; set; }
    public User? ReopenedByUser { get; set; }
    public ICollection<WorkRecord> WorkRecords { get; set; } = new List<WorkRecord>();
}
