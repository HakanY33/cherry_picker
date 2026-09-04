namespace MipRental.Domain.Enums;

public enum NotificationStatus
{
    QUEUED,

    /// <summary>
    /// Kuyruk işleyici bu satırı ÜSTLENDİ. İki işleyici (ya da aynı işleyicinin
    /// üst üste binen iki turu) aynı bildirimi göndermesin diye araya konan
    /// kilit: satır tek bir atomik UPDATE ile QUEUED'dan alınır.
    /// </summary>
    SENDING,

    SENT,
    FAILED,

    /// <summary>
    /// Dış alıcı politikası kapalıyken MIP alan adı dışına düşen bildirim.
    /// Hata değildir: mail gönderilmez, kayıt uygulama içinde görünür kalır.
    /// </summary>
    SKIPPED_EXTERNAL
}
