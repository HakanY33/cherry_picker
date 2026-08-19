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
