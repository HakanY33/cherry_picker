using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Approvals;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Approvals;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;
using MipRental.Web.Common;
using MipRental.Web.Models.Approvals;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

/// <summary>
/// Adım 7 Bölüm 3: MIP tarafı onay ekranı.
///
/// Yetki iki katmanlıdır ve İKİSİ de gereklidir:
///   1. [Authorize(Policy = CanApprove)] — kullanıcı genel olarak onaylayabilen biri mi
///   2. WorkRecordStateMachine.EnsureApprover — kullanıcı GERÇEKTEN o adımın rolünde mi
/// Sadece politika yeterli değildir: adımın rolü SUPERVISOR ise DEPT_HEAD onaylayamaz.
/// </summary>
[Authorize(Policy = PolicyNames.CanApprove)]
public class ApprovalsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ApprovalService _approvalService;
    private readonly ICurrentUser _currentUser;

    public ApprovalsController(AppDbContext db, ApprovalService approvalService, ICurrentUser currentUser)
    {
        _db = db;
        _approvalService = approvalService;
        _currentUser = currentUser;
    }

    // ---------------------------------------------------------------
    // "Onayımı Bekleyenler"
    // ---------------------------------------------------------------
    public async Task<IActionResult> Index()
    {
        // ADIM 9: onaylayabilmek ile tutarı görebilmek ayrı eksenler. CanApprove
        // Ekipman Müdürlüğü'nü de kapsar; o rol tutarı görmez.
        var canSeePricing = _currentUser.CanSeePricing;

        var pending = await _approvalService.GetPendingForCurrentUserAsync();
        if (pending.Count == 0)
        {
            return View(new PendingApprovalsViewModel { ShowPricing = canSeePricing });
        }

        var recordIds = pending.Select(a => a.DocumentId).Distinct().ToList();
        var records = await _db.WorkRecords.AsNoTracking()
            .Include(w => w.Firm)
            .Where(w => recordIds.Contains(w.WorkRecordId))
            .ToDictionaryAsync(w => w.WorkRecordId);

        var lineCounts = await _db.WorkRecordLines.AsNoTracking()
            .Where(l => recordIds.Contains(l.WorkRecordId))
            .GroupBy(l => l.WorkRecordId)
            .Select(g => new { WorkRecordId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WorkRecordId, x => x.Count);

        var utcNow = DateTime.UtcNow;
        var items = new List<PendingApprovalItem>();

        foreach (var approval in pending)
        {
            if (!records.TryGetValue(approval.DocumentId, out var record))
            {
                continue; // Firma filtresi ya da silinmiş belge; listeye alınmaz.
            }

            var step = approval.ApprovalFlowStep;

            items.Add(new PendingApprovalItem
            {
                ApprovalId = approval.ApprovalId,
                WorkRecordId = record.WorkRecordId,
                DocumentNo = record.DocumentNo,
                FirmTitle = record.Firm.Title,
                WorkDate = record.WorkDate,
                Status = record.Status,
                Pricing = canSeePricing
                    ? new PendingApprovalPricing { TotalAmount = record.TotalAmount, Currency = record.Currency }
                    : null,
                StepNo = approval.StepNo,
                StepName = step?.Name ?? $"{approval.StepNo}. adım",
                AssignedAt = approval.AssignedAt,
                LineCount = lineCounts.GetValueOrDefault(record.WorkRecordId),
                ReminderDueAt = step is null ? null : ApprovalEscalationCalculator.ReminderDueAt(approval, step),
                EscalationDueAt = step is null ? null : ApprovalEscalationCalculator.EscalationDueAt(approval, step),
                IsEscalationDue = step is not null && ApprovalEscalationCalculator.IsEscalationDue(approval, step, utcNow)
            });
        }

        return View(new PendingApprovalsViewModel { Items = items, ShowPricing = canSeePricing });
    }

    // ---------------------------------------------------------------
    // Tekil kararlar
    // ---------------------------------------------------------------

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, string? comment)
    {
        return await ExecuteAsync(id, async () =>
        {
            var outcome = await _approvalService.ApproveAsync(id, comment);
            return outcome.Status == WorkRecordStatus.APPROVED
                ? $"{outcome.Record.DocumentNo} onaylandı. Onay zinciri tamamlandı."
                : $"{outcome.Record.DocumentNo} için \"{outcome.CompletedStep.Name}\" tamamlandı; kayıt \"{outcome.NextStep!.Name}\" adımını bekliyor.";
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? reason)
    {
        return await ExecuteAsync(id, async () =>
        {
            var outcome = await _approvalService.RejectAsync(id, reason);
            return $"{outcome.Record.DocumentNo} reddedildi.";
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestRevision(int id, string? reason)
    {
        return await ExecuteAsync(id, async () =>
        {
            var outcome = await _approvalService.RequestRevisionAsync(id, reason);
            return $"{outcome.Record.DocumentNo} için revizyon istendi.";
        });
    }

    /// <summary>
    /// SATIR BAZLI İTİRAZ: 40 satırlık bir kayıtta tek satır yüzünden tüm ay
    /// beklemesin diye, kaydın tamamı reddedilmeden sadece o satıra itiraz edilir.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ObjectToLine(int id, int lineId, string? reason)
    {
        return await ExecuteAsync(id, async () =>
        {
            var outcome = await _approvalService.ObjectToLineAsync(id, lineId, reason);
            return $"{outcome.Record.DocumentNo} kaydının satırına itiraz edildi; kayıt revizyona gönderildi.";
        });
    }

    // ---------------------------------------------------------------
    // Toplu onay
    //
    // KARAR: her kayıt KENDİ transaction'ında işlenir; biri hata verirse
    // diğerleri etkilenmez ("kısmi başarı"). Gerekçe: toplu onayın tek amacı
    // ay sonu kapanışında hızdır. 40 kaydın 1'i (bayat sözleşme satırı, kapanmış
    // dönem, araya giren başka bir karar) hata verdiğinde 39 geçerli onayı geri
    // almak, onaylayanı tek tek onaylamaya iter ve özelliği anlamsız kılar.
    // Kayıtlar arasında atomiklik gerektiren bir invariant da yoktur: her belgenin
    // kendi Approvals satırı ve kendi denetim izi vardır.
    //
    // Hata veren kayıtlar isim isim raporlanır; sessizce yutulmaz.
    // ---------------------------------------------------------------
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkApprove(int[] workRecordIds, string? comment)
    {
        if (workRecordIds is null || workRecordIds.Length == 0)
        {
            TempData[TempDataKeys.ErrorMessage] = "Onaylanacak kayıt seçilmedi.";
            return RedirectToAction(nameof(Index));
        }

        var result = new BulkApprovalResult();

        foreach (var workRecordId in workRecordIds.Distinct())
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var outcome = await _approvalService.ApproveAsync(workRecordId, comment);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                result.Approved.Add(outcome.Record.DocumentNo);
            }
            catch (Exception ex) when (IsBusinessRuleFailure(ex))
            {
                await transaction.RollbackAsync();

                // Yarım kalan değişiklikler bir SONRAKİ kaydın SaveChanges'ine
                // sızmasın diye change tracker temizlenir.
                _db.ChangeTracker.Clear();

                var documentNo = await _db.WorkRecords.AsNoTracking()
                    .Where(w => w.WorkRecordId == workRecordId)
                    .Select(w => w.DocumentNo)
                    .FirstOrDefaultAsync() ?? $"#{workRecordId}";

                result.Failed.Add($"{documentNo}: {ex.Message}");
            }
        }

        if (result.Approved.Count > 0)
        {
            TempData[TempDataKeys.SuccessMessage] =
                $"{result.Approved.Count} kayıt onaylandı ({string.Join(", ", result.Approved)}).";
        }

        if (result.Failed.Count > 0)
        {
            TempData[TempDataKeys.ErrorMessage] =
                $"{result.Failed.Count} kayıt onaylanamadı — diğerleri etkilenmedi. {string.Join(" | ", result.Failed)}";
        }

        return RedirectToAction(nameof(Index));
    }

    // ---------------------------------------------------------------

    private async Task<IActionResult> ExecuteAsync(int workRecordId, Func<Task<string>> action)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var message = await action();
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData[TempDataKeys.SuccessMessage] = message;
        }
        catch (Exception ex) when (IsBusinessRuleFailure(ex))
        {
            await transaction.RollbackAsync();
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
        }

        return RedirectToAction("Details", "WorkRecords", new { id = workRecordId });
    }

    private static bool IsBusinessRuleFailure(Exception ex) =>
        ex is WorkRecordStateTransitionException
            or ApprovalAuthorizationException
            or ApprovalFlowException
            or PeriodGuardException
            or ImmutabilityViolationException;
}
