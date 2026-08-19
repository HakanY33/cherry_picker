using MipRental.Domain.Enums;

namespace MipRental.Domain.Entities;

public class AuditLog
{
    public long AuditId { get; set; }
    public string TableName { get; set; } = null!;
    public int RecordId { get; set; }
    public AuditAction Action { get; set; }
    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    public int? UserId { get; set; }
    public string? IpAddress { get; set; }
    public DateTime OccurredAt { get; set; }

    public User? User { get; set; }
}
