using MipRental.Domain.Enums;

namespace MipRental.Web.Common;

/// <summary>
/// Bildirim durumlarının Türkçe karşılığı (mail sağlık ekranı). Renk burada
/// DEĞİL: tek renk kaynağı <see cref="StatusBadge"/>.
/// </summary>
public static class NotificationStatusDisplay
{
    private static readonly IReadOnlyDictionary<NotificationStatus, string> Labels =
        new Dictionary<NotificationStatus, string>
        {
            [NotificationStatus.QUEUED] = "Kuyrukta",
            [NotificationStatus.SENDING] = "Gönderiliyor",
            [NotificationStatus.SENT] = "Gönderildi",
            [NotificationStatus.FAILED] = "Başarısız",
            [NotificationStatus.SKIPPED_EXTERNAL] = "Dış alıcı — atlandı"
        };

    public static string GetLabel(NotificationStatus status) =>
        Labels.TryGetValue(status, out var label) ? label : status.ToString();
}
