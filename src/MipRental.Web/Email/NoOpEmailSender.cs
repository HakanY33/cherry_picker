using MipRental.Domain.Abstractions;

namespace MipRental.Web.Email;

/// <summary>
/// Mail yapılandırması yoksa ya da kapalıysa devreye giren gönderici.
/// HİÇBİR ŞEY GÖNDERMEZ, yalnızca loglar.
///
/// <see cref="IsEnabled"/> false olduğu için kuyruk işleyici hiç çalışmaz:
/// bildirimler QUEUED kalır, uygulama sorunsuz çalışmaya devam eder. MIP ayarı
/// vermeden de sistem ayakta kalmalı — bu sınıf o güvencedir.
/// </summary>
public sealed class NoOpEmailSender : IEmailSender
{
    private readonly ILogger<NoOpEmailSender> _logger;

    public NoOpEmailSender(ILogger<NoOpEmailSender> logger)
    {
        _logger = logger;
    }

    public bool IsEnabled => false;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // GÖVDE VE KONU LOGLANMAZ: magic link maili ham token taşır ve bu sınıf
        // hangi mailin geldiğini bilmez. Yalnızca "gönderilmedi" bilgisi düşer.
        _logger.LogInformation(
            "Mail yapılandırması kapalı; e-posta gönderilmedi (alıcı gizlendi). Şablon uzunluğu={Length} bayt.",
            message.HtmlBody.Length);

        return Task.CompletedTask;
    }
}
