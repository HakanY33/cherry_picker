using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Services;
using MipRental.Domain.Approvals;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;
using MipRental.Web.Common;
using MipRental.Web.Models.Requests;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

/// <summary>
/// ADIM 11 — FİRMA YETKİLİSİ EKRANLARI.
///
/// ÜÇ AYRI KORUMA katmanı, üçü de gerekli:
///   1. Firma izolasyonu — Requests üzerindeki global query filter (kural 7).
///      Burada "if (FirmId == ...)" YAZILMAZ; başka firmanın talebi sorgudan
///      hiç dönmez, id elle yazılsa bile NotFound alınır.
///   2. Kimlik gizliliği — view model'lerde talep edenin adı/görevi/departmanı
///      diye bir alan BULUNMAZ (→ [[Aktörler ve Roller]]). Bu ekranların
///      sorguları o kolonları hiç seçmez.
///   3. Durum makinesi — EnsureFirmManager aktörün firmasını kaydınkiyle
///      karşılaştırır; kayıt eline geçse bile işlem yapılamaz.
///
/// FİYAT YOK: firma "kaç saat" bilgisini görür, tutarı görmez. → [[Fiyat Gizliliği]]
/// </summary>
[Authorize(Policy = PolicyNames.CanManageFirmRequests)]
public class FirmRequestsController : Controller
{
    private readonly AppDbContext _db;
    private readonly RequestFlowService _flow;
    private readonly NotificationQueue _notifications;

    public FirmRequestsController(AppDbContext db, RequestFlowService flow, NotificationQueue notifications)
    {
        _db = db;
        _flow = flow;
        _notifications = notifications;
    }

    // ---------------------------------------------------------------
    // 3.1 Bekleyen talepler
    // ---------------------------------------------------------------

    public async Task<IActionResult> Index() =>
        View(new FirmRequestsViewModel { Items = await RowsAsync(RequestStatus.PENDING_FIRM) });

    // ---------------------------------------------------------------
    // 3.3 Planlanan işler
    // ---------------------------------------------------------------

    public async Task<IActionResult> Scheduled() =>
        View(new FirmRequestsViewModel { Items = await RowsAsync(RequestStatus.SCHEDULED) });

    // ---------------------------------------------------------------
    // 3.2 Kabul ekranı
    // ---------------------------------------------------------------

    public async Task<IActionResult> Accept(int id)
    {
        var model = await OwnRequestView(id);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(int id, string? operatorName, string? licensePlate)
    {
        var request = await _db.Requests.FirstOrDefaultAsync(r => r.RequestId == id);
        if (request is null)
        {
            return NotFound();
        }

        try
        {
            var period = await _flow.GetPeriodAsync(request.RequestedDate);
            var actor = await _flow.GetActorAsync();

            // Operatör ve plaka zorunluluğunu durum makinesi zorlar; burada
            // ayrıca kontrol edilmez ki iki ayrı doğruluk kaynağı olmasın.
            RequestStateMachine.AcceptByFirm(request, period, actor, operatorName, licensePlate, DateTime.UtcNow);

            await _notifications.QueueRequestEventAsync(request,
                NotificationQueue.Templates.RequestFirmAccepted,
                $"Kabul edildi: {request.DocumentNo}",
                $"{request.DocumentNo} numaralı talep firma tarafından kabul edildi ve planlandı. " +
                $"Tarih: {TrFormat.Date(request.RequestedDate)}. Operatör: {request.AssignedOperatorName}. " +
                $"Plaka: {request.AssignedLicensePlate}.",
                toRequester: true, toEquipment: true);

            await _db.SaveChangesAsync();
            TempData[TempDataKeys.SuccessMessage] = $"{request.DocumentNo} kabul edildi ve planlandı.";
            return RedirectToAction(nameof(Scheduled));
        }
        catch (Exception ex) when (IsBusinessRuleFailure(ex))
        {
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
            return RedirectToAction(nameof(Accept), new { id });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? reason)
    {
        var request = await _db.Requests.FirstOrDefaultAsync(r => r.RequestId == id);
        if (request is null)
        {
            return NotFound();
        }

        try
        {
            var period = await _flow.GetPeriodAsync(request.RequestedDate);
            var actor = await _flow.GetActorAsync();

            RequestStateMachine.RejectByFirm(request, period, actor, reason, DateTime.UtcNow);

            await _notifications.QueueRequestEventAsync(request,
                NotificationQueue.Templates.RequestFirmRejected,
                $"Firma reddetti: {request.DocumentNo}",
                $"{request.DocumentNo} numaralı talep firma tarafından reddedildi. Gerekçe: {reason}",
                toRequester: true, toEquipment: true);

            await _db.SaveChangesAsync();
            TempData[TempDataKeys.SuccessMessage] = $"{request.DocumentNo} reddedildi.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex) when (IsBusinessRuleFailure(ex))
        {
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
            return RedirectToAction(nameof(Accept), new { id });
        }
    }

    /// <summary>
    /// Madde 0 — planlanmış işte operatör/plaka değişikliği. Araç arızalanır,
    /// operatör değişir; iş yine aynı iştir, DURUM DEĞİŞMEZ. Gerekçe istenmez:
    /// değişiklik zaten alan bazlı denetim izine düşer.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAssignment(int id, string? operatorName, string? licensePlate)
    {
        var request = await _db.Requests.FirstOrDefaultAsync(r => r.RequestId == id);
        if (request is null)
        {
            return NotFound();
        }

        try
        {
            var period = await _flow.GetPeriodAsync(request.RequestedDate);
            var actor = await _flow.GetActorAsync();

            RequestStateMachine.UpdateAssignment(request, period, actor, operatorName, licensePlate);

            await _notifications.QueueRequestEventAsync(request,
                NotificationQueue.Templates.RequestAssignmentChanged,
                $"Operatör/plaka değişti: {request.DocumentNo}",
                $"{request.DocumentNo} numaralı planlanmış iş için atama güncellendi. " +
                $"Operatör: {request.AssignedOperatorName}. Plaka: {request.AssignedLicensePlate}.",
                toRequester: true, toEquipment: true);

            await _db.SaveChangesAsync();
            TempData[TempDataKeys.SuccessMessage] = $"{request.DocumentNo} için operatör ve plaka güncellendi.";
        }
        catch (Exception ex) when (IsBusinessRuleFailure(ex))
        {
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
        }

        return RedirectToAction(nameof(Scheduled));
    }

    // ---------------------------------------------------------------

    /// <summary>
    /// Liste satırları. Firma filtresi SORGUYA yazılmaz — global query filter
    /// zaten kaydı kendi firmasına kısıtlar (kural 7). Talep edenin kimlik
    /// kolonları hiç SELECT edilmez.
    /// </summary>
    private async Task<List<FirmRequestRow>> RowsAsync(RequestStatus status) =>
        await _db.Requests.AsNoTracking()
            .Where(r => r.Status == status)
            .OrderBy(r => r.RequestedDate).ThenBy(r => r.RequestedStartTime)
            .Select(r => new FirmRequestRow
            {
                RequestId = r.RequestId,
                DocumentNo = r.DocumentNo,
                Status = r.Status,
                RequestedDate = r.RequestedDate,
                RequestedStartTime = r.RequestedStartTime,
                RequestedEndTime = r.RequestedEndTime,
                LocationDisplay = r.Location != null ? r.Location.FullPath ?? r.Location.Name : r.LocationText,
                WorkDescription = r.WorkDescription,
                ServiceDisplay = r.RequestLines
                    .OrderBy(l => l.LineNo)
                    .Select(l => l.ServiceVariant != null
                        ? l.ServiceCategory.Name + " — " + l.ServiceVariant.Name
                        : l.ServiceCategory.Name)
                    .FirstOrDefault(),
                AssignedOperatorName = r.AssignedOperatorName,
                AssignedLicensePlate = r.AssignedLicensePlate
            })
            .ToListAsync();

    private async Task<FirmRequestAcceptViewModel?> OwnRequestView(int id) =>
        await _db.Requests.AsNoTracking()
            .Where(r => r.RequestId == id)
            .Select(r => new FirmRequestAcceptViewModel
            {
                RequestId = r.RequestId,
                DocumentNo = r.DocumentNo,
                Status = r.Status,
                RequestedDate = r.RequestedDate,
                RequestedStartTime = r.RequestedStartTime,
                RequestedEndTime = r.RequestedEndTime,
                LocationDisplay = r.Location != null ? r.Location.FullPath ?? r.Location.Name : r.LocationText,
                WorkDescription = r.WorkDescription,
                ServiceDisplay = r.RequestLines
                    .OrderBy(l => l.LineNo)
                    .Select(l => l.ServiceVariant != null
                        ? l.ServiceCategory.Name + " — " + l.ServiceVariant.Name
                        : l.ServiceCategory.Name)
                    .FirstOrDefault(),
                AssignedOperatorName = r.AssignedOperatorName,
                AssignedLicensePlate = r.AssignedLicensePlate
            })
            .FirstOrDefaultAsync();

    private static bool IsBusinessRuleFailure(Exception ex) =>
        ex is RequestStateTransitionException
            or ApprovalAuthorizationException
            or PeriodGuardException
            or ImmutabilityViolationException;
}
