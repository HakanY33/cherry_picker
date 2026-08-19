using MipRental.Domain.Enums;

namespace MipRental.Domain.Entities;

public class IntegrationQueue
{
    public long QueueId { get; set; }
    public string TargetSystem { get; set; } = "ORACLE";
    public DocumentType DocumentType { get; set; }
    public int DocumentId { get; set; }
    public string? Payload { get; set; }
    public IntegrationQueueStatus Status { get; set; } = IntegrationQueueStatus.PENDING;
    public string? ExternalRef { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
