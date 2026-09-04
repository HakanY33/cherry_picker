using MipRental.Domain.Enums;

namespace MipRental.Web.Models.EmailHealth;

/// <summary>
/// Mail sağlık ekranı. MIP IT devreye alırken bakacağı tek ekran.
///
/// ŞİFRE BU MODELE HİÇ GİRMEZ: alan yok, dolayısıyla ekranda gizlenen değil,
/// var olmayan bir değerdir (Adım 9'daki fiyat gizliliğiyle aynı yaklaşım).
/// Yalnızca "tanımlı mı" bilgisi taşınır.
/// </summary>
public class EmailHealthViewModel
{
    public required bool SenderEnabled { get; init; }
    public required bool ConfigEnabledFlag { get; init; }
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public bool UseStartTls { get; init; }
    public string UserName { get; init; } = string.Empty;
    public bool HasPassword { get; init; }
    public string FromAddress { get; init; } = string.Empty;
    public string FromDisplayName { get; init; } = string.Empty;
    public bool AllowExternalRecipients { get; init; }
    public string TestModeRecipient { get; init; } = string.Empty;
    public string InternalDomain { get; init; } = string.Empty;
    public int QueueIntervalSeconds { get; init; }
    public int MaxRetryCount { get; init; }

    public int QueuedCount { get; init; }
    public int SendingCount { get; init; }
    public int SentCount { get; init; }
    public int FailedCount { get; init; }
    public int SkippedExternalCount { get; init; }

    public IReadOnlyList<NotificationRow> Recent { get; init; } = Array.Empty<NotificationRow>();
}

public sealed class NotificationRow
{
    public required long NotificationId { get; init; }
    public string? Recipient { get; init; }
    public string? Subject { get; init; }
    public required NotificationStatus Status { get; init; }
    public required int RetryCount { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? SentAt { get; init; }
    public DateTime? NextAttemptAt { get; init; }
    public string? LastError { get; init; }
}
