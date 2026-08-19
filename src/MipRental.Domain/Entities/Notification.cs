using MipRental.Domain.Enums;

namespace MipRental.Domain.Entities;

public class Notification
{
    public long NotificationId { get; set; }
    public int? UserId { get; set; }
    public string? Email { get; set; }
    public NotificationChannel Channel { get; set; } = NotificationChannel.EMAIL;
    public string TemplateCode { get; set; } = null!;
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public DocumentType? DocumentType { get; set; }
    public int? DocumentId { get; set; }
    public NotificationStatus Status { get; set; } = NotificationStatus.QUEUED;
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }

    public User? User { get; set; }
}
