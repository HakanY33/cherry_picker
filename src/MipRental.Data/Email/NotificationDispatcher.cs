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
    /// ADIM 16 B5 — ÖZETLENEBİLİR şablonlar: "sıra sende" tipi, tek tek acil
    /// olmayan bildirimler. Yirmi talebin onayını bekleyen kişiye yirmi mail
    /// gitmesin diye aynı tipteki bildirimler tek mailde toplanabilir.
    ///
    /// Listede OLMAYANLAR bilinçli olarak dışarıda: red, itiraz, eskalasyon,
    /// dönem kilidi ve magic link. Bunların gecikmesinin bedeli var — biri
    /// sahada bekliyor ya da bir karar geri alınmış oluyor.
    ///
    /// Gruplama ALTYAPISI yok: tek bir küme, tek bir ayar, kuyruğun kendi
    /// NextAttemptAt kolonu. Kural karmaşıklaşırsa buradan başlanır.
    /// </summary>
    private static readonly HashSet<string> Digestible = new(StringComparer.Ordinal)
    {
        "WR_APPROVAL_PENDING",
        "WR_DERIVED_PENDING_SUBMIT",
        "WR_REVISION_DRAFTED",
        "REQ_SUBMITTED",
        "REQ_EQUIPMENT_APPROVED",
        "REQ_FIRM_ACCEPTED",
        "REQ_CONFIRM_PENDING"
    };

    /// <summary>
    /// Sırası gelen bildirimleri işler; işlenen satır sayısını döner.
    /// </summary>
    public async Task<int> DispatchQueuedAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        if (!_sender.IsEnabled)
        {
            return 0;
        }

        var digestHours = _options.DigestHours;
        var processed = digestHours > 0 ? await DispatchDigestsAsync(utcNow, digestHours, cancellationToken) : 0;

        var dueQuery = _db.Notifications
            .Where(n => n.Status == NotificationStatus.QUEUED
                     && (n.NextAttemptAt == null || n.NextAttemptAt <= utcNow));

        // Özet açıkken özetlenebilir satırlar yukarıdaki turda ele alındı.
        if (digestHours > 0)
        {
            dueQuery = dueQuery.Where(n => !Digestible.Contains(n.TemplateCode));
        }

        var due = await dueQuery
            .OrderBy(n => n.CreatedAt)
            .Select(n => n.NotificationId)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

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

    /// <summary>
    /// ADIM 16 B5 — günlük özet.
    ///
    /// İki iş yapar:
    ///   1. Yeni gelen özetlenebilir satırların gönderimini ERTELER
    ///      (NextAttemptAt = CreatedAt + DigestHours). Kolon zaten var; yeni bir
    ///      "özet kuyruğu" tablosu açmaya gerek yok.
    ///   2. Süresi dolanları KULLANICI + ŞABLON bazında gruplar ve tek mail
    ///      gönderir. Gruptaki satırların HEPSİ gönderilmiş sayılır; kuyrukta
    ///      "gitmedi" görünen satır kalmaz.
    ///
    /// Bir grup tek bir mail olduğu için tek bir hata da hepsini etkiler: bu
    /// bilinçli. Özet zaten "bunlar birlikte gider" demektir.
    /// </summary>
    private async Task<int> DispatchDigestsAsync(DateTime utcNow, int digestHours, CancellationToken cancellationToken)
    {
        // 1) Henüz zamanlanmamışları ertele.
        var fresh = await _db.Notifications
            .Where(n => n.Status == NotificationStatus.QUEUED && n.NextAttemptAt == null)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var deferred = fresh.Where(n => Digestible.Contains(n.TemplateCode)).ToList();
        if (deferred.Count > 0)
        {
            foreach (var notification in deferred)
            {
                notification.NextAttemptAt = notification.CreatedAt.AddHours(digestHours);
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        // 2) Zamanı gelenleri alıcı + şablon bazında topla.
        var ready = await _db.Notifications
            .Where(n => n.Status == NotificationStatus.QUEUED
                     && n.NextAttemptAt != null && n.NextAttemptAt <= utcNow)
            .OrderBy(n => n.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        // Gruplama anahtarında ADRES de var: alıcı kullanıcı kaydı olmayan
        // (UserId boş) satırlar tek bir kutuda toplanıp yanlış kişiye gitmesin.
        var groups = ready
            .Where(n => Digestible.Contains(n.TemplateCode))
            .GroupBy(n => (n.UserId, n.Email, n.TemplateCode))
            .ToList();

        var processed = 0;

        foreach (var group in groups)
        {
            var items = group.ToList();
            var recipient = items[0].Email?.Trim();
            if (string.IsNullOrWhiteSpace(recipient))
            {
                foreach (var item in items)
                {
                    item.Status = NotificationStatus.FAILED;
                    item.RetryCount = _options.MaxRetryCount;
                    item.LastError = "Alıcı e-posta adresi tanımlı değil.";
                    item.LastAttemptAt = utcNow;
                }

                processed += items.Count;
                continue;
            }

            var heading = EmailTemplates.Heading(group.Key.TemplateCode);
            // Tek satır kalmışsa özet BAŞLIĞI yazılmaz: bildirimin kendi konusu
            // ve gövdesi zaten daha anlaşılır.
            var subject = items.Count == 1
                ? items[0].Subject ?? heading
                : $"[{items.Count} bildirim] {heading}";
            var body = items.Count == 1
                ? items[0].Body ?? string.Empty
                : string.Join("\n\n", items.Select(n => $"- {n.Subject}\n  {n.Body}"));

            try
            {
                await SendDigestAsync(group.Key.TemplateCode, subject, body, recipient, cancellationToken);

                foreach (var item in items)
                {
                    item.Status = NotificationStatus.SENT;
                    item.SentAt = utcNow;
                    item.LastAttemptAt = utcNow;
                    item.NextAttemptAt = null;
                    item.LastError = null;
                }
            }
            catch (Exception ex)
            {
                foreach (var item in items)
                {
                    RecordFailure(item, utcNow, ex);
                }
            }

            processed += items.Count;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return processed;
    }

    private async Task SendDigestAsync(
        string templateCode, string subject, string body, string recipient, CancellationToken cancellationToken)
    {
        // Dış alıcı ve test modu politikaları özet mailde de aynen geçerli.
        if (!_options.AllowExternalRecipients && !IsInternal(recipient))
        {
            throw new InvalidOperationException("Dış alıcıya özet maili gönderilmez.");
        }

        var target = string.IsNullOrWhiteSpace(_options.TestModeRecipient)
            ? recipient
            : _options.TestModeRecipient.Trim();

        await _sender.SendAsync(new EmailMessage
        {
            To = target,
            Subject = subject,
            HtmlBody = EmailTemplates.Render(templateCode, subject, body, _options.AppBaseUrl),
            ContainsSecret = false
        }, cancellationToken);
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
            HtmlBody = EmailTemplates.Render(notification, _options.AppBaseUrl),
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
