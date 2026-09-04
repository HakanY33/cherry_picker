using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Email;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Enums;
using MipRental.Web.Common;
using MipRental.Web.Models.EmailHealth;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

/// <summary>
/// ADIM 15 — MAİL SAĞLIK KONTROLÜ. Bu ekran MIP IT'nin devreye alırken
/// kullanacağı ekrandır: ayar okundu mu, kuyrukta ne var, ne hata aldı.
///
/// ADMIN'e kapalı (CanManageMaster). ŞİFRE HİÇBİR ŞEKİLDE GÖSTERİLMEZ —
/// view model'de öyle bir alan yok, yalnızca "tanımlı mı" bilgisi var.
/// </summary>
[Authorize(Policy = PolicyNames.CanManageMaster)]
public class EmailHealthController : Controller
{
    private const int RecentCount = 20;

    private readonly AppDbContext _db;
    private readonly EmailOptions _options;
    private readonly IEmailSender _sender;

    public EmailHealthController(AppDbContext db, EmailOptions options, IEmailSender sender)
    {
        _db = db;
        _options = options;
        _sender = sender;
    }

    public async Task<IActionResult> Index()
    {
        var counts = await _db.Notifications.AsNoTracking()
            .GroupBy(n => n.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);

        var recent = await _db.Notifications.AsNoTracking()
            .OrderByDescending(n => n.NotificationId)
            .Take(RecentCount)
            .Select(n => new NotificationRow
            {
                NotificationId = n.NotificationId,
                Recipient = n.Email,
                Subject = n.Subject,
                Status = n.Status,
                RetryCount = n.RetryCount,
                CreatedAt = n.CreatedAt,
                SentAt = n.SentAt,
                NextAttemptAt = n.NextAttemptAt,
                LastError = n.LastError
            })
            .ToListAsync();

        return View(new EmailHealthViewModel
        {
            SenderEnabled = _sender.IsEnabled,
            ConfigEnabledFlag = _options.Enabled,
            Host = _options.Host,
            Port = _options.Port,
            UseStartTls = _options.UseStartTls,
            UserName = _options.UserName,
            HasPassword = !string.IsNullOrWhiteSpace(_options.Password),
            FromAddress = _options.FromAddress,
            FromDisplayName = _options.FromDisplayName,
            AllowExternalRecipients = _options.AllowExternalRecipients,
            TestModeRecipient = _options.TestModeRecipient,
            InternalDomain = _options.InternalDomain,
            QueueIntervalSeconds = _options.QueueIntervalSeconds,
            MaxRetryCount = _options.MaxRetryCount,
            QueuedCount = counts.GetValueOrDefault(NotificationStatus.QUEUED),
            SendingCount = counts.GetValueOrDefault(NotificationStatus.SENDING),
            SentCount = counts.GetValueOrDefault(NotificationStatus.SENT),
            FailedCount = counts.GetValueOrDefault(NotificationStatus.FAILED),
            SkippedExternalCount = counts.GetValueOrDefault(NotificationStatus.SKIPPED_EXTERNAL),
            Recent = recent
        });
    }

    /// <summary>
    /// Deneme maili. Sistemdeki TEK yer alıcı adresinin kullanıcı girdisinden
    /// geldiği yerdir; bilinçlidir ve ADMIN'e kapalıdır — devreye alırken
    /// "gerçekten çıkıyor mu" sorusunun başka cevabı yok. Kuyruğa yazılmaz,
    /// doğrudan gönderilir: kuyruk çalışmıyorsa da sonuç görülsün.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTest(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            TempData[TempDataKeys.ErrorMessage] = "Deneme maili için bir adres girin.";
            return RedirectToAction(nameof(Index));
        }

        if (!_sender.IsEnabled)
        {
            TempData[TempDataKeys.ErrorMessage] =
                "Mail yapılandırması kapalı ya da eksik; deneme maili gönderilemez. " +
                "Host ve gönderen adresi tanımlanıp Enabled true yapılmalı (bkz. docs/EMAIL-SETUP.md).";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await _sender.SendAsync(new EmailMessage
            {
                To = address.Trim(),
                Subject = "MIP Hizmet Kiralama — deneme e-postası",
                HtmlBody = EmailTemplates.Render(
                    "TEST",
                    "Deneme e-postası",
                    "Bu bir deneme e-postasıdır. Bu mesajı görüyorsanız mail yapılandırması çalışıyor demektir.")
            });

            TempData[TempDataKeys.SuccessMessage] = $"Deneme maili gönderildi: {address.Trim()}";
        }
        catch (Exception ex)
        {
            // Hata mesajı EKRANDA gösterilir: MIP IT'nin ihtiyacı olan bilgi tam
            // olarak budur (yanlış port, TLS, kimlik doğrulama...).
            TempData[TempDataKeys.ErrorMessage] = $"Deneme maili gönderilemedi: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }
}
