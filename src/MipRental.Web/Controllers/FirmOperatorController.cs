using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Services;
using MipRental.Domain.Approvals;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;
using MipRental.Domain.Pricing;
using MipRental.Web.Common;
using MipRental.Web.Models.Requests;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

/// <summary>
/// ADIM 12 — OPERATÖR EKRANI. Akışın son sahadaki adımı: "Başladım" / "Bitirdim".
///
/// Operatör SADECE işi görür. Ekranda tutar yok, çalışma kaydı yok, taslak yok,
/// gönderim yok: "Bitirdim" dendiğinde arka planda çalışma kaydı türer ama bu
/// operatöre YANSIMAZ — mesaj "İş tamamlandı."dır. Gönderim firma yetkilisinin
/// işidir (ADR-028) ve haber ona düşer.
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
    private readonly RequestToWorkRecordService _derivation;
    private readonly NotificationQueue _notifications;

    public FirmOperatorController(
        AppDbContext db,
        RequestFlowService flow,
        RequestToWorkRecordService derivation,
        NotificationQueue notifications)
    {
        _db = db;
        _flow = flow;
        _derivation = derivation;
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
    /// IN_PROGRESS -> COMPLETED, ardından çalışma kaydının türetilmesi.
    ///
    /// İKİ AYRI COMMIT, bilinçli olarak: önce talep kapanır, sonra türetme
    /// denenir. Türetme patlasa da (kapalı dönem, tanımsız fiyat) işin bittiği
    /// bilgisi kaybolmaz; türetme idempotent olduğu için sonra tekrar denenebilir.
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
            await _db.SaveChangesAsync();
        }
        catch (Exception ex) when (IsBusinessRuleFailure(ex))
        {
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
            return RedirectToAction(nameof(Index));
        }

        try
        {
            // Kayıt DRAFT doğar (ADR-026) ve firma yetkilisine "gönderim bekliyor"
            // bildirimi türetmenin kendi SaveChanges'inde düşer.
            await _derivation.DeriveAsync(id);
        }
        catch (Exception ex) when (ex is PeriodGuardException or PricingException or RequestStateTransitionException)
        {
            // Sebebi çözecek taraf MIP: dönemi açacak ya da eksik fiyatı tanımlayacak
            // olan Ekipman Müdürlüğü. Operatöre teknik detay YANSIMAZ — sahada
            // yapabileceği bir şey yok, işi zaten bitti.
            await _notifications.QueueRequestEventAsync(request,
                NotificationQueue.Templates.RequestDerivationFailed,
                $"Çalışma kaydı oluşturulamadı: {request.DocumentNo}",
                $"{request.DocumentNo} talebinden çalışma kaydı oluşturulamadı: {ex.Message}",
                toEquipment: true);

            await _db.SaveChangesAsync();
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
