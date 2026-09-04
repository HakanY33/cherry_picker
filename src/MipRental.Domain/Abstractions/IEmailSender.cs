namespace MipRental.Domain.Abstractions;

/// <summary>
/// Tek bir e-postanın gönderimi. Uygulamaları Web katmanındadır
/// (SmtpEmailSender / NoOpEmailSender); kuyruk işleyici yalnızca bu arayüzü
/// bilir, hangisinin bağlı olduğunu bilmez.
/// </summary>
public interface IEmailSender
{
    /// <summary>Yapılandırma aktif mi? Sağlık ekranı bunu gösterir.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Gönderir. Başarısızlıkta İSTİSNA FIRLATIR — kuyruk işleyici hatayı
    /// yakalayıp deneme sayısını artırır ve mesajı kaydeder.
    /// </summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Gönderilecek e-posta. Alıcı adresi VERİTABANINDAN gelir, kullanıcı girdisinden değil.</summary>
public sealed class EmailMessage
{
    public required string To { get; init; }
    public required string Subject { get; init; }

    /// <summary>HTML gövde (EmailTemplates üretir).</summary>
    public required string HtmlBody { get; init; }

    /// <summary>
    /// Gövde hassas veri (magic link ham token'ı) içeriyor mu? İçeriyorsa
    /// gönderici bu maili HİÇBİR SEVİYEDE loglamaz — konu satırı bile.
    /// </summary>
    public bool ContainsSecret { get; init; }
}
