using System.Net;
using System.Net.Mail;
using MipRental.Domain.Abstractions;

namespace MipRental.Web.Email;

/// <summary>
/// Gerçek gönderim. Ayarlar appsettings'teki "Email" bölümünden gelir; ŞİFRE
/// oraya yazılmaz (user-secrets / ortam değişkeni) → docs/EMAIL-SETUP.md.
///
/// System.Net.Mail kullanılır: .NET'in kendi sınıfı, YENİ PAKET YOK
/// (CLAUDE.md: istenmeden NuGet paketi eklenmez). MailKit gibi bir kütüphane
/// gerekirse ayrıca karar verilir.
///
/// Bu sınıf yalnızca GÖNDERİR: kime gideceği, test modu ve dış alıcı politikası
/// NotificationDispatcher'da uygulanır — iki yerde iki kural olmasın.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(EmailOptions options, ILogger<SmtpEmailSender> logger)
    {
        _options = options;
        _logger = logger;
    }

    public bool IsEnabled => _options.IsUsable;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseStartTls,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        // Kullanıcı adı boşsa kimlik doğrulamasız (anonim relay) gönderim: kurumsal
        // iç sunucularda yaygın. Şifre yalnızca burada okunur, hiçbir yere yazılmaz.
        if (!string.IsNullOrWhiteSpace(_options.UserName))
        {
            client.Credentials = new NetworkCredential(_options.UserName, _options.Password);
        }
        else
        {
            client.UseDefaultCredentials = false;
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromDisplayName),
            Subject = message.Subject,
            Body = message.HtmlBody,
            IsBodyHtml = true
        };
        mail.To.Add(message.To);

        await client.SendMailAsync(mail, cancellationToken);

        // HASSAS MAİL LOGLANMAZ: magic link gövdesi ham token taşır, konu satırı
        // bile iz bırakmasın. Diğerlerinde de yalnızca "gönderildi" bilgisi düşer.
        if (message.ContainsSecret)
        {
            _logger.LogInformation("Bağlantı içeren e-posta gönderildi (içerik loglanmaz).");
        }
        else
        {
            _logger.LogInformation("E-posta gönderildi: {Subject}", message.Subject);
        }
    }
}
