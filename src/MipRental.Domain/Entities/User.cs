namespace MipRental.Domain.Entities;

public class User
{
    public int UserId { get; set; }
    public string UserName { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Position { get; set; }
    public int? DepartmentId { get; set; }
    public int? FirmId { get; set; }
    public bool IsFirmAdmin { get; set; }
    public string? ExternalId { get; set; }
    public string? PasswordHash { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Department? Department { get; set; }
    public Firm? Firm { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<Contract> CreatedContracts { get; set; } = new List<Contract>();
    public ICollection<ContractLine> CreatedContractLines { get; set; } = new List<ContractLine>();
    public ICollection<Period> ClosedPeriods { get; set; } = new List<Period>();
    public ICollection<Period> ReopenedPeriods { get; set; } = new List<Period>();
    public ICollection<Request> RequestsCreated { get; set; } = new List<Request>();
    public ICollection<WorkRecord> WorkRecordsRequested { get; set; } = new List<WorkRecord>();
    public ICollection<WorkRecord> WorkRecordsWitnessed { get; set; } = new List<WorkRecord>();
    public ICollection<WorkRecord> WorkRecordsEntered { get; set; } = new List<WorkRecord>();
    public ICollection<Approval> ApprovalsAssigned { get; set; } = new List<Approval>();
    public ICollection<Approval> ApprovalsDecided { get; set; } = new List<Approval>();
    public ICollection<GeneratedDocument> GeneratedDocuments { get; set; } = new List<GeneratedDocument>();
    public ICollection<Attachment> UploadedAttachments { get; set; } = new List<Attachment>();
    public ICollection<AuditLog> AuditLogEntries { get; set; } = new List<AuditLog>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
