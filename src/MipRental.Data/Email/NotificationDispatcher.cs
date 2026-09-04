using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Data.Email;

/// <summary>
/// ADIM 15 — KUYRUK İŞLEYİCİ. Notifications tablosundaki QUEUED satırları alır,
/// gönderir ve sonucu kaydeder.
///
/// Arka plan servisinden AYRI bir sınıf: hosting'e bağlı olmadan test edilsin
/// (BackgroundService yalnızca zamanlayıcıdır, iş burada).
///
/// MAİL AYARI YOKSA HİÇBİR ŞEY YAPMAZ. Satırlar QUEUED kalır, sistem çalışmaya
/// devam eder — MIP ayarı vermeden de uygulama ayakta kalmalı.
/// </summary>
public sealed class NotificationDispatcher
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _sender;
    private readonly EmailOptions _options;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        AppDbContext db, IEmailSender sender, EmailOptions options, ILogger<NotificationDispatcher> logger)
    {
        _db = db;
        _sender = sender;
        _options = options;
        _logger = logger;
    }

    /// <summary>Bir turda işlenecek azami satır.</summary>
    public const int BatchSize = 50;

    /// <summary>
    /// Sırası gelen bildirimleri işler; işlenen satır sayısını döner.
    /// </summary>
    public async Task<int> DispatchQueuedAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        if (!_sender.IsEnabled)
        {
            return 0;
        }

        var due = await _db.Notifications
            .Where(n => n.Status == NotificationStatus.QUEUED
                     && (n.NextAttemptAt == null || n.NextAttemptAt <= utcNow))
            .OrderBy(n => n.CreatedAt)
            .Select(n => n.NotificationId)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var id in due)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (await ProcessOneAsync(id, utcNow, cancellationToken))
            {
                processed++;
            }
        }

        return processed;
    }

    private async Task<bool> ProcessOneAsync(long id, DateTime utcNow, CancellationToken cancellationToken)
    {
        // KAYIT KİLİDİ: satır tek bir atomik UPDATE ile QUEUED'dan alınır. Aynı
        // bildirimi iki işleyici (ya da üst üste binen iki tur) gönderemez —
        // ikinci UPDATE 0 satır etkiler ve o tur bu satıra dokunmaz.
        var claimed = await _db.Notifications
            .Where(n => n.NotificationId == id && n.Status == NotificationStatus.QUEUED)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Status, NotificationStatus.SENDING)
                .SetProperty(n => n.LastAttemptAt, utcNow), cancellationToken);

        if (claimed == 0)
        {
            return false;
        }

        var notification = await _db.Notifications.FirstAsync(n => n.NotificationId == id, cancellationToken);

        try
        {
            await SendOrSkipAsync(notification, utcNow, cancellationToken);
        }
        catch (Exception ex)
        {
            RecordFailure(notification, utcNow, ex);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task SendOrSkipAsync(Notification notification, DateTime utcNow, CancellationToken cancellationToken)
    {
        // Alıcı adresi VERİTABANINDAN gelir (bildirim satırının kendi alanı);
        // hiçbir yerde kullanıcı girdisinden okunmaz.
        var recipient = notification.Email?.Trim();

        if (string.IsNullOrWhiteSpace(recipient))
        {
            // Tekrar denemek bir şey değiştirmez: kullanıcının adresi yok.
            notification.Status = NotificationStatus.FAILED;
            notification.RetryCount = _options.MaxRetryCount;
            notification.LastError = "Alıcı e-posta adresi tanımlı değil.";
            notification.LastAttemptAt = utcNow;
            return;
        }

        // DIŞ ALICI POLİTİKASI: kapalıysa MIP alan adı dışına mail çıkmaz.
        // Hata değildir — bildirim uygulama içinde görünmeye devam eder.
        if (!_options.AllowExternalRecipients && !IsInternal(recipient))
        {
            notification.Status = NotificationStatus.SKIPPED_EXTERNAL;
            notification.LastAttemptAt = utcNow;
            notification.LastError = null;
            return;
        }

        // TEST MODU: dolu ise TÜM mailler bu adrese gider. Gerçek alıcı satırda
        // olduğu gibi kalır; nereye gittiği LastError değil, kayıt altındadır.
        var target = string.IsNullOrWhiteSpace(_options.TestModeRecipient)
            ? recipient
            : _options.TestModeRecipient.Trim();

        await _sender.SendAsync(new EmailMessage
        {
            To = target,
            Subject = notification.Subject ?? EmailTemplates.Heading(notification.TemplateCode),
            HtmlBody = EmailTemplates.Render(notification),
            ContainsSecret = EmailTemplates.ContainsSecret(notification.TemplateCode)
        }, cancellationToken);

        notification.Status = NotificationStatus.SENT;
        notification.SentAt = utcNow;
        notification.LastAttemptAt = utcNow;
        notification.NextAttemptAt = null;
        notification.LastError = null;
    }

    private void RecordFailure(Notification notification, DateTime utcNow, Exception ex)
    {
        notification.RetryCount++;
        notification.LastAttemptAt = utcNow;
        notification.LastError = Truncate(ex.Message, 500);

        if (notification.RetryCount >= _options.MaxRetryCount)
        {
            // Beşinci denemeden sonra bırakılır; sağlık ekranında FAILED görünür.
            notification.Status = NotificationStatus.FAILED;
            notification.NextAttemptAt = null;
        }
        else
        {
            // Üstel geri çekilme: 1, 2, 4, 8 dakika.
            notification.Status = NotificationStatus.QUEUED;
            notification.NextAttemptAt = utcNow.AddMinutes(Math.Pow(2, notification.RetryCount - 1));
        }

        // GÖVDE LOGLANMAZ: magic link maili ham token taşır. Yalnızca id ve hata.
        _logger.LogWarning(
            "Bildirim gönderilemedi. Id={NotificationId} Deneme={RetryCount} Hata={Error}",
            notification.NotificationId, notification.RetryCount, notification.LastError);
    }

    private bool IsInternal(string address)
    {
        var domain = _options.InternalDomain;
        if (string.IsNullOrWhiteSpace(domain))
        {
            // Gönderen adresi yoksa iç/dış ayrımı yapılamaz; politika kapalıyken
            // güvenli taraf "gönderme"dir.
            return false;
        }

        var at = address.LastIndexOf('@');
        return at >= 0 && string.Equals(address[(at + 1)..].Trim(), domain, StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
