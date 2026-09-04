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

    /// <summary>Son denemenin zamanı — sağlık ekranında "en son ne zaman denendi".</summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>
    /// Üstel geri çekilme: bu andan önce tekrar denenmez. Boşsa hemen denenir.
    /// </summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>
    /// Son hatanın mesajı (kısaltılmış). MIP IT devreye alırken tek bakacağı yer
    /// burasıdır. Gövde YAZILMAZ: magic link maili burada da sızmamalı.
    /// </summary>
    public string? LastError { get; set; }

    public User? User { get; set; }
}
