using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Services;
using MipRental.Domain.Approvals;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;
using MipRental.Web.Common;
using MipRental.Web.Models.Requests;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

/// <summary>
/// ADIM 12 — OPERATÖR EKRANI. Akışın son sahadaki adımı: "Başladım" / "Bitirdim".
///
/// Operatör SADECE işi görür. Ekranda tutar yok, çalışma kaydı yok, taslak yok,
/// gönderim yok. Mesaj "İş tamamlandı."dır.
///
/// ADIM 16 — "Bitirdim" ARTIK ÇALIŞMA KAYDI TÜRETMEZ. Bitirmek işin bittiğini
/// söyler, gerçekleşen sürenin doğru olduğunu söylemez; süreyi teyit eden kişi
/// talebi açandır. Türetme teyitle (CONFIRMED) tetiklenir. Operatörün ekranında
/// bu değişiklik görünmez: haber talebi açana düşer, operatöre değil.
///
/// Ayrı controller olmasının sebebi yetki: FirmRequestsController sınıf
/// seviyesinde CanManageFirmRequests ister ve action seviyesindeki bir
/// [Authorize] onu GEVŞETMEZ, üstüne biner. Operatörün ekranı bu yüzden burada.
///
/// Firma izolasyonu Requests üzerindeki global query filter'dan gelir (kural 7);
/// burada "if (FirmId == ...)" yazılmaz. Durum makinesi ayrıca EnsureFirmOperator
/// ile hem rolü hem firmayı doğrular.
/// </summary>
[Authorize(Policy = PolicyNames.CanOperateWork)]
public class FirmOperatorController : Controller
{
    private readonly AppDbContext _db;
    private readonly RequestFlowService _flow;
    private readonly NotificationQueue _notifications;

    public FirmOperatorController(AppDbContext db, RequestFlowService flow, NotificationQueue notifications)
    {
        _db = db;
        _flow = flow;
        _notifications = notifications;
    }

    /// <summary>Planlanan ve devam eden işler. Talep edenin kimlik alanları SELECT edilmez.</summary>
    public async Task<IActionResult> Index() =>
        View(new FirmRequestsViewModel
        {
            Items = await _db.Requests.AsNoTracking()
                .Where(r => r.Status == RequestStatus.SCHEDULED || r.Status == RequestStatus.IN_PROGRESS)
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
                .ToListAsync()
        });

    /// <summary>SCHEDULED -> IN_PROGRESS. Başlangıç saatini durum makinesi damgalar.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(int id)
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

            RequestStateMachine.Start(request, period, actor, DateTime.UtcNow);

            // Talep açan sahada işi bekleyen kişidir: iş başladığında haberi olur.
            await _notifications.QueueRequestEventAsync(request,
                NotificationQueue.Templates.RequestStarted,
                NotificationQueue.Subject(request.DocumentNo, "Talep ettiğiniz iş başladı"),
                $"{request.DocumentNo} numaralı talebinizde iş sahada başladı. " +
                $"Başlangıç: {TrFormat.DateTimeLocal(request.ActualStartTime!.Value)}. " +
                $"Operatör: {request.AssignedOperatorName}. Plaka: {request.AssignedLicensePlate}. " +
                "Yapmanız gereken bir şey yok; iş bitince gerçekleşen süreyi teyit etmeniz istenecek.",
                toRequester: true);

            await _db.SaveChangesAsync();

            TempData[TempDataKeys.SuccessMessage] = "İş başlatıldı.";
        }
        catch (Exception ex) when (IsBusinessRuleFailure(ex))
        {
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// IN_PROGRESS -> COMPLETED. Talep burada BİTMEZ: gerçekleşen süre talebi
    /// açanın teyidine düşer (Adım 16). Çalışma kaydı teyitten sonra türer.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Finish(int id)
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

            RequestStateMachine.Complete(request, period, actor, DateTime.UtcNow);

            // Sıradaki kişi talebi açandır: teyit ondan bekleniyor. Bildirim
            // durum değişikliğiyle AYNI SaveChanges'te yazılır.
            var duration = request.ActualEndTime!.Value - request.ActualStartTime!.Value;
            await _notifications.QueueRequestEventAsync(request,
                NotificationQueue.Templates.RequestConfirmPending,
                NotificationQueue.Subject(request.DocumentNo, "Süre teyidiniz bekleniyor"),
                $"{request.DocumentNo} numaralı talebinizde iş tamamlandı ve gerçekleşen süre " +
                "teyidinize düştü. " +
                $"Başlangıç: {TrFormat.DateTimeLocal(request.ActualStartTime.Value)}. " +
                $"Bitiş: {TrFormat.DateTimeLocal(request.ActualEndTime.Value)}. " +
                $"Gerçekleşen süre: {TrFormat.Duration(duration)}. " +
                "Uygulamada \"Taleplerim\" ekranından talebi açıp süreyi onaylayın; " +
                "süre farklıysa gerekçesiyle itiraz edin. Teyit vermediğiniz sürece " +
                "bu iş için çalışma kaydı oluşmaz.",
                toRequester: true);

            await _db.SaveChangesAsync();
        }
        catch (Exception ex) when (IsBusinessRuleFailure(ex))
        {
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
            return RedirectToAction(nameof(Index));
        }

        TempData[TempDataKeys.SuccessMessage] = "İş tamamlandı.";
        return RedirectToAction(nameof(Index));
    }

    private static bool IsBusinessRuleFailure(Exception ex) =>
        ex is RequestStateTransitionException
            or ApprovalAuthorizationException
            or PeriodGuardException
            or ImmutabilityViolationException;
}
