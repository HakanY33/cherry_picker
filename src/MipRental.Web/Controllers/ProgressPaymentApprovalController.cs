using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MipRental.Data;
using MipRental.Data.Services;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;
using MipRental.Web.Common;
using MipRental.Web.Models.ProgressPayments;

namespace MipRental.Web.Controllers;

/// <summary>
/// /Onay/{token} — Bütçe Yöneticisi'nin MAİLDEN gelen hakediş onayı (ADR-015).
///
/// EN KRİTİK KURAL: GET ONAYLAMAZ.
/// Outlook, Microsoft Defender ve kurumsal mail tarayıcıları maildeki
/// bağlantıları güvenlik taraması için ÖNCEDEN AÇAR. Bağlantıya girmek onay
/// verseydi, mail sunucusu belgeyi kimse görmeden onaylardı — hakediş kimsenin
/// bakmadığı bir ay için ödeme emrine dönerdi. Bu yüzden GET yalnızca ÖZET
/// SAYFASINI gösterir; karar ayrı bir POST isteğidir ve antiforgery ister.
///
/// [AllowAnonymous]: Bütçe Yöneticisi'nin oturum açması beklenmiyor. Güvenliği
/// kimlik değil token sağlar: 32 bayt rastgele, hash'lenerek saklanan, tek
/// kullanımlık, 7 gün geçerli ve TEK BİR hakedişe bağlı (B9).
/// </summary>
[AllowAnonymous]
[Route("Onay")]
public class ProgressPaymentApprovalController : Controller
{
    private readonly AppDbContext _db;
    private readonly ApprovalTokenService _tokens;
    private readonly ProgressPaymentService _payments;

    public ProgressPaymentApprovalController(
        AppDbContext db, ApprovalTokenService tokens, ProgressPaymentService payments)
    {
        _db = db;
        _tokens = tokens;
        _payments = payments;
    }

    /// <summary>
    /// ÖZET SAYFASI. HİÇBİR DURUM DEĞİŞİKLİĞİ YAPMAZ: token tüketilmez,
    /// hakediş durumu değişmez, tek satır bile yazılmaz.
    /// </summary>
    [HttpGet("{token?}")]
    public async Task<IActionResult> Index(string? token)
    {
        var result = await _tokens.ResolveAsync(token, DateTime.UtcNow);
        return Render(result, token, errorMessage: null);
    }

    /// <summary>
    /// KARAR BURADA VERİLİR. Token tüketilir; IP, tarayıcı ve zaman kaydedilir.
    /// İş kuralı ihlalinde (boş red gerekçesi, araya giren durum değişikliği)
    /// token TÜKENMEZ — kullanıcı düzeltip tekrar deneyebilir.
    /// </summary>
    [HttpPost("{token}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(string token, string decision, string? note, string? reason)
    {
        var result = await _tokens.ResolveAsync(token, DateTime.UtcNow);
        if (result.Status != ApprovalTokenStatus.Valid || result.Token is null)
        {
            return Render(result, token, errorMessage: null);
        }

        var approve = string.Equals(decision, "approve", StringComparison.Ordinal);

        try
        {
            await _payments.DecideByTokenAsync(
                result.Token,
                approve,
                approve ? note : reason,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString());

            await _db.SaveChangesAsync();
        }
        catch (Exception ex) when (ex is ProgressPaymentStateTransitionException or ApprovalAuthorizationException)
        {
            _db.ChangeTracker.Clear();

            // Token hâlâ geçerli: sayfayı hata mesajıyla yeniden çiziyoruz.
            var retry = await _tokens.ResolveAsync(token, DateTime.UtcNow);
            return Render(retry, token, ex.Message);
        }

        var payment = result.Token.ProgressPayment;
        return View("Done", new ApprovalLinkDoneViewModel
        {
            PeriodName = TrFormat.PeriodName(payment.Period.Year, payment.Period.Month),
            FirmTitle = payment.Firm.Title,
            Status = payment.Status
        });
    }

    private IActionResult Render(ApprovalTokenResult result, string? token, string? errorMessage)
    {
        // Geçersiz token ile "hiç olmayan" token AYNI cevabı alır ve hiçbir
        // ayrıntı sızdırmaz: hangi hakediş, hangi firma, hangi kullanıcı — yok.
        if (result.Status == ApprovalTokenStatus.Invalid || result.Token is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return View("Invalid");
        }

        var payment = result.Token.ProgressPayment;
        var periodName = TrFormat.PeriodName(payment.Period.Year, payment.Period.Month);

        switch (result.Status)
        {
            case ApprovalTokenStatus.Used:
                return View("Used", new ApprovalLinkUsedViewModel
                {
                    PeriodName = periodName,
                    FirmTitle = payment.Firm.Title,
                    Status = payment.Status,
                    DecidedAt = payment.ManagerApprovedAt
                });

            // B8 — geri çekilen hakedişin token'ı iptal edilmiştir. Kullanıcıya
            // "geçersiz bağlantı" değil, olan biteni söyleyen sayfa gösterilir.
            case ApprovalTokenStatus.Revoked:
                return NotPending(periodName, payment);

            case ApprovalTokenStatus.Expired:
                return View("Expired", periodName);
        }

        if (payment.Status != ProgressPaymentStatus.PENDING_BUDGET_MANAGER)
        {
            return NotPending(periodName, payment);
        }

        return View("Index", new ApprovalLinkViewModel
        {
            Token = token!,
            PeriodName = periodName,
            FirmTitle = payment.Firm.Title,
            RecordCount = payment.RecordCount,
            TotalAmount = payment.TotalAmount,
            Currency = payment.Currency,
            BudgetNote = payment.BudgetNote,
            ExpiresAt = result.Token.ExpiresAt,
            ErrorMessage = errorMessage
        });
    }

    private IActionResult NotPending(string periodName, ProgressPayment payment) =>
        View("NotPending", new ApprovalLinkNotPendingViewModel
        {
            PeriodName = periodName,
            FirmTitle = payment.Firm.Title,
            Status = payment.Status
        });
}
