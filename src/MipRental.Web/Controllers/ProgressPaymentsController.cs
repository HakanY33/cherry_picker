using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Reporting;
using MipRental.Data.Services;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;
using MipRental.Web.Common;
using MipRental.Web.Models.ProgressPayments;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

/// <summary>
/// ADIM 14 BÖLÜM A — HAKEDİŞ EKRANLARI.
///
/// Üç ayrı policy, ADR-025'in deseni:
///   CanViewProgressPayments   — BUDGET + BUDGET_MANAGER (liste ve detay)
///   CanManageProgressPayment  — BUDGET (oluştur, yöneticiye gönder)
///   CanApproveProgressPayment — BUDGET_MANAGER (onayla/reddet)
/// Butonu gizlemek yeterli değildir: karar action'ları sunucuda da kapalıdır.
///
/// Durum ataması yok: her geçiş ProgressPaymentStateMachine'den geçer.
/// </summary>
[Authorize(Policy = PolicyNames.CanViewProgressPayments)]
public class ProgressPaymentsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ProgressPaymentService _payments;
    private readonly IAuthorizationService _authorization;

    public ProgressPaymentsController(
        AppDbContext db, ProgressPaymentService payments, IAuthorizationService authorization)
    {
        _db = db;
        _payments = payments;
        _authorization = authorization;
    }

    public async Task<IActionResult> Index()
    {
        var canCreate = (await _authorization.AuthorizeAsync(User, PolicyNames.CanManageProgressPayment)).Succeeded;

        return View(new ProgressPaymentIndexViewModel
        {
            Items = await RowsAsync(),
            CanCreate = canCreate,
            PeriodOptions = canCreate ? await BuildPeriodOptionsAsync() : Array.Empty<SelectListItem>(),
            FirmOptions = canCreate ? await BuildFirmOptionsAsync() : Array.Empty<SelectListItem>()
        });
    }

    /// <summary>
    /// A3 — hakediş oluştur. Dahil edilen kayıt listesi burada DONDURULUR.
    /// Onay bekleyen kayıt varsa UYARI verilir ama işlem engellenmez: bekleyen
    /// kayıt bir sonraki hakedişe girer, ay bu yüzden kapanmadan bekleyemez.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PolicyNames.CanManageProgressPayment)]
    public async Task<IActionResult> Create(int periodId, int firmId)
    {
        try
        {
            var payment = await _payments.CreateAsync(periodId, firmId);

            TempData[TempDataKeys.SuccessMessage] =
                $"Hakediş oluşturuldu: {payment.RecordCount} kayıt, {payment.TotalAmount:N2} {payment.Currency}.";

            if (payment.PendingRecordCountAtCreation > 0)
            {
                TempData[TempDataKeys.ErrorMessage] =
                    $"Bu dönemde onay bekleyen {payment.PendingRecordCountAtCreation} kayıt var, hakedişe dahil edilmedi.";
            }

            return RedirectToAction(nameof(Details), new { id = payment.ProgressPaymentId });
        }
        catch (Exception ex) when (ex is ProgressPaymentStateTransitionException
                                or ApprovalAuthorizationException
                                or UnauthorizedAccessException)
        {
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    public async Task<IActionResult> Details(int id)
    {
        var model = await BuildDetailsAsync(id);
        return model is null ? NotFound() : View(model);
    }

    /// <summary>A4 — Bütçe onayı: not eklenir, hakediş Bütçe Yöneticisi'ne gider.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PolicyNames.CanManageProgressPayment)]
    public async Task<IActionResult> SendToManager(int id, string? budgetNote)
    {
        var payment = await _db.ProgressPayments.FirstOrDefaultAsync(p => p.ProgressPaymentId == id);
        if (payment is null)
        {
            return NotFound();
        }

        try
        {
            // Mail bağlantısı MUTLAK adres ister: kullanıcı bu adrese uygulama
            // dışından, oturumsuz gelir.
            var mailed = await _payments.SendToManagerAsync(payment, budgetNote, BuildApprovalUrl);
            await _db.SaveChangesAsync();

            TempData[TempDataKeys.SuccessMessage] = mailed > 0
                ? $"Hakediş onaylandı ve {mailed} Bütçe Yöneticisi'ne onay bağlantısı gönderildi."
                : "Hakediş onaylandı ve Bütçe Yöneticisi onayına gönderildi. (Rolde aktif kullanıcı yok, mail düşmedi.)";
        }
        catch (Exception ex) when (IsBusinessRuleFailure(ex))
        {
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// B5 — YEDEK YOL. Mail kaybolursa Bütçe Yöneticisi arayüzden de karar
    /// verebilir. Karar aynı durum makinesinden geçer; mail yolu ile ekran yolu
    /// arasında iş kuralı farkı yoktur.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PolicyNames.CanApproveProgressPayment)]
    public async Task<IActionResult> Approve(int id, string? note)
    {
        var payment = await _db.ProgressPayments.FirstOrDefaultAsync(p => p.ProgressPaymentId == id);
        if (payment is null)
        {
            return NotFound();
        }

        try
        {
            await _payments.ApproveAsync(payment, note);
            await _db.SaveChangesAsync();

            TempData[TempDataKeys.SuccessMessage] = "Hakediş onaylandı.";
        }
        catch (Exception ex) when (IsBusinessRuleFailure(ex))
        {
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PolicyNames.CanApproveProgressPayment)]
    public async Task<IActionResult> Reject(int id, string? reason)
    {
        var payment = await _db.ProgressPayments.FirstOrDefaultAsync(p => p.ProgressPaymentId == id);
        if (payment is null)
        {
            return NotFound();
        }

        try
        {
            await _payments.RejectAsync(payment, reason);
            await _db.SaveChangesAsync();

            TempData[TempDataKeys.SuccessMessage] = "Hakediş reddedildi.";
        }
        catch (Exception ex) when (IsBusinessRuleFailure(ex))
        {
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// B8 — geri çekme. Bütçe hatayı fark ettiğinde hakedişi yöneticiden geri
    /// alır; mail kutusundaki bağlantılar aynı işlemde İPTAL edilir, yoksa geri
    /// çekilmiş bir hakediş eski bağlantıdan onaylanabilirdi.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PolicyNames.CanManageProgressPayment)]
    public async Task<IActionResult> Withdraw(int id)
    {
        var payment = await _db.ProgressPayments.FirstOrDefaultAsync(p => p.ProgressPaymentId == id);
        if (payment is null)
        {
            return NotFound();
        }

        try
        {
            var revoked = await _payments.WithdrawAsync(payment);
            await _db.SaveChangesAsync();

            TempData[TempDataKeys.SuccessMessage] = revoked > 0
                ? $"Hakediş geri çekildi; {revoked} onay bağlantısı iptal edildi."
                : "Hakediş geri çekildi.";
        }
        catch (Exception ex) when (IsBusinessRuleFailure(ex))
        {
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------------------------------------------------------------

    /// <summary>Mail bağlantısı: https://.../Onay/{token}</summary>
    private string BuildApprovalUrl(string rawToken) =>
        Url.Action(nameof(ProgressPaymentApprovalController.Index), "ProgressPaymentApproval",
            new { token = rawToken }, Request.Scheme)
        ?? $"{Request.Scheme}://{Request.Host}/Onay/{rawToken}";

    private async Task<ProgressPaymentDetailsViewModel?> BuildDetailsAsync(int id)
    {
        var payment = await _db.ProgressPayments.AsNoTracking()
            .Where(p => p.ProgressPaymentId == id)
            .Select(p => new
            {
                Header = new ProgressPaymentRow
                {
                    ProgressPaymentId = p.ProgressPaymentId,
                    Year = p.Period.Year,
                    Month = p.Period.Month,
                    FirmTitle = p.Firm.Title,
                    Status = p.Status,
                    TotalAmount = p.TotalAmount,
                    Currency = p.Currency,
                    RecordCount = p.RecordCount,
                    BudgetApprovedAt = p.BudgetApprovedAt,
                    ManagerApprovedAt = p.ManagerApprovedAt
                },
                p.PeriodId,
                p.FirmId,
                p.BudgetNote,
                p.ManagerNote,
                p.RejectionReason,
                p.PendingRecordCountAtCreation,
                BudgetApprovedByName = p.BudgetApprovedByUser != null ? p.BudgetApprovedByUser.FullName : null,
                ManagerApprovedByName = p.ManagerApprovedByUser != null ? p.ManagerApprovedByUser.FullName : null,
                Records = p.Records
                    .OrderBy(r => r.WorkRecord.WorkDate).ThenBy(r => r.WorkRecord.DocumentNo)
                    .Select(r => new ProgressPaymentRecordRow
                    {
                        WorkRecordId = r.WorkRecordId,
                        DocumentNo = r.WorkRecord.DocumentNo,
                        WorkDate = r.WorkRecord.WorkDate,
                        Status = r.WorkRecord.Status,
                        TotalAmount = r.WorkRecord.TotalAmount,
                        Currency = r.WorkRecord.Currency
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        if (payment is null)
        {
            return null;
        }

        // "Şu an bekleyen" sayısı hakedişin dondurulmuş sayısıyla aynı olmayabilir:
        // aradan geçen sürede yeni kayıt onaylanmış olabilir. İkisi de gösterilir.
        var pendingNow = await _db.WorkRecords.AsNoTracking()
            .CountAsync(w => w.PeriodId == payment.PeriodId
                          && w.FirmId == payment.FirmId
                          && !w.IsSuperseded
                          && MonthlySummaryService.PendingStatuses.Contains(w.Status));

        var canManage = (await _authorization.AuthorizeAsync(User, PolicyNames.CanManageProgressPayment)).Succeeded;
        var canApprove = (await _authorization.AuthorizeAsync(User, PolicyNames.CanApproveProgressPayment)).Succeeded;

        return new ProgressPaymentDetailsViewModel
        {
            Header = payment.Header,
            Records = payment.Records,
            BudgetNote = payment.BudgetNote,
            ManagerNote = payment.ManagerNote,
            RejectionReason = payment.RejectionReason,
            BudgetApprovedByName = payment.BudgetApprovedByName,
            ManagerApprovedByName = payment.ManagerApprovedByName,
            PendingRecordCountAtCreation = payment.PendingRecordCountAtCreation,
            PendingRecordCountNow = pendingNow,
            CanSendToManager = canManage && payment.Header.Status == ProgressPaymentStatus.DRAFT,
            CanWithdraw = canManage && payment.Header.Status == ProgressPaymentStatus.PENDING_BUDGET_MANAGER,
            CanDecide = canApprove && payment.Header.Status == ProgressPaymentStatus.PENDING_BUDGET_MANAGER
        };
    }

    private Task<List<ProgressPaymentRow>> RowsAsync() =>
        _db.ProgressPayments.AsNoTracking()
            .OrderByDescending(p => p.Period.Year).ThenByDescending(p => p.Period.Month).ThenBy(p => p.Firm.Title)
            .Select(p => new ProgressPaymentRow
            {
                ProgressPaymentId = p.ProgressPaymentId,
                Year = p.Period.Year,
                Month = p.Period.Month,
                FirmTitle = p.Firm.Title,
                Status = p.Status,
                TotalAmount = p.TotalAmount,
                Currency = p.Currency,
                RecordCount = p.RecordCount,
                BudgetApprovedAt = p.BudgetApprovedAt,
                ManagerApprovedAt = p.ManagerApprovedAt
            })
            .ToListAsync();

    private async Task<List<SelectListItem>> BuildPeriodOptionsAsync()
    {
        var periods = await _db.Periods.AsNoTracking()
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .Select(p => new { p.PeriodId, p.Year, p.Month })
            .ToListAsync();

        return periods
            .Select(p => new SelectListItem(TrFormat.PeriodName(p.Year, p.Month), p.PeriodId.ToString()))
            .ToList();
    }

    private Task<List<SelectListItem>> BuildFirmOptionsAsync() =>
        _db.Firms.AsNoTracking()
            .Where(f => f.IsActive)
            .OrderBy(f => f.Title)
            .Select(f => new SelectListItem(f.Title, f.FirmId.ToString()))
            .ToListAsync();

    private static bool IsBusinessRuleFailure(Exception ex) =>
        ex is ProgressPaymentStateTransitionException
            or ApprovalAuthorizationException
            or ImmutabilityViolationException
            or PeriodGuardException;
}
