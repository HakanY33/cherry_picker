using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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
/// ADIM 11 — EKİPMAN MÜDÜRLÜĞÜ EKRANLARI.
///
/// YETKİ İKİ KATMANLI ve ikisi de gereklidir:
///   1. Sınıf seviyesi CanViewEquipmentRequests — EQUIPMENT_MANAGER ve
///      EQUIPMENT_VIEWER listeleri görür.
///   2. Action seviyesi CanDecideEquipmentRequest — kararı YALNIZCA
///      EQUIPMENT_MANAGER verir. EQUIPMENT_VIEWER için ayrı ekran yazılmadı;
///      aynı ekranda butonlar policy ile çizilmiyor VE POST sunucuda düşüyor.
///      Butonu gizlemek tek başına yetki değildir.
///
/// FİYAT YOK: Ekipman Müdürlüğü'nün her iki rolü de tutar görmez; bu
/// controller hiçbir yerde para alanı okumaz. → [[Fiyat Gizliliği]]
/// </summary>
[Authorize(Policy = PolicyNames.CanViewEquipmentRequests)]
public class EquipmentRequestsController : Controller
{
    private readonly AppDbContext _db;
    private readonly RequestFlowService _flow;
    private readonly NotificationQueue _notifications;
    private readonly IAuthorizationService _authorization;
    private readonly RequestToWorkRecordService _derivation;

    public EquipmentRequestsController(
        AppDbContext db,
        RequestFlowService flow,
        NotificationQueue notifications,
        IAuthorizationService authorization,
        RequestToWorkRecordService derivation)
    {
        _db = db;
        _flow = flow;
        _notifications = notifications;
        _authorization = authorization;
        _derivation = derivation;
    }

    // ---------------------------------------------------------------
    // 2.1 Onay bekleyen talepler
    // ---------------------------------------------------------------

    public async Task<IActionResult> Index(int? departmentId = null, int? locationId = null, DateOnly? from = null, DateOnly? to = null)
    {
        var query = _db.Requests.AsNoTracking().Where(r => r.Status == RequestStatus.PENDING_EQUIPMENT);

        if (departmentId is not null)
        {
            query = query.Where(r => r.DepartmentId == departmentId);
        }

        if (locationId is not null)
        {
            query = query.Where(r => r.LocationId == locationId);
        }

        if (from is not null)
        {
            query = query.Where(r => r.RequestedDate >= from);
        }

        if (to is not null)
        {
            query = query.Where(r => r.RequestedDate <= to);
        }

        // Geçmiş tarih uyarısı sunucu saatinin YEREL gününe göre: "bugün"
        // ekrandaki takvim günüdür, UTC günü değil.
        var today = DateOnly.FromDateTime(DateTime.Now);

        // En yakın tarih üstte: bu ekran bir "yapılacaklar" listesidir, sırası
        // aciliyettir. Geçmişte kalan talepler zaten en üste düşer.
        var items = await query
            .OrderBy(r => r.RequestedDate).ThenBy(r => r.RequestedStartTime)
            .Select(r => new EquipmentRequestRow
            {
                RequestId = r.RequestId,
                DocumentNo = r.DocumentNo,
                RequestedDate = r.RequestedDate,
                RequestedStartTime = r.RequestedStartTime,
                DepartmentName = r.Department.Name,
                LocationDisplay = r.Location != null ? r.Location.FullPath ?? r.Location.Name : r.LocationText,
                ServiceDisplay = r.RequestLines
                    .OrderBy(l => l.LineNo)
                    .Select(l => l.ServiceVariant != null
                        ? l.ServiceCategory.Name + " — " + l.ServiceVariant.Name
                        : l.ServiceCategory.Name)
                    .FirstOrDefault(),
                RequesterName = r.RequestedByUser.FullName,
                WorkDescription = r.WorkDescription,
                IsPastDue = r.RequestedDate < today
            })
            .ToListAsync();

        return View(new EquipmentRequestsViewModel
        {
            Items = items,
            CanDecide = await CanDecideAsync(),
            DepartmentId = departmentId,
            LocationId = locationId,
            From = from,
            To = to,
            DepartmentOptions = await _db.Departments.AsNoTracking()
                .Where(d => d.IsActive)
                .OrderBy(d => d.Name)
                .Select(d => new SelectListItem(d.Name, d.DepartmentId.ToString(), d.DepartmentId == departmentId))
                .ToListAsync(),
            LocationOptions = await _db.Locations.AsNoTracking()
                .Where(l => l.IsActive)
                .OrderBy(l => l.FullPath ?? l.Name)
                .Select(l => new SelectListItem(l.FullPath ?? l.Name, l.LocationId.ToString(), l.LocationId == locationId))
                .ToListAsync()
        });
    }

    // ---------------------------------------------------------------
    // 2.2 Talep detay ve karar
    // ---------------------------------------------------------------

    public async Task<IActionResult> Details(int id)
    {
        var request = await _db.Requests.AsNoTracking()
            .Where(r => r.RequestId == id)
            .Select(r => new
            {
                r.RequestId,
                r.DocumentNo,
                r.Status,
                RequesterName = r.RequestedByUser.FullName,
                RequesterPosition = r.RequestedByUser.Position,
                DepartmentName = r.Department.Name,
                r.IssueDate,
                r.RequestedDate,
                r.RequestedStartTime,
                r.RequestedEndTime,
                LocationDisplay = r.Location != null ? r.Location.FullPath ?? r.Location.Name : r.LocationText,
                r.WorkDescription,
                Line = r.RequestLines.OrderBy(l => l.LineNo).Select(l => new
                {
                    l.ServiceId,
                    ServiceName = l.ServiceCategory.Name,
                    l.VariantId,
                    VariantName = l.ServiceVariant != null ? l.ServiceVariant.Name : null
                }).FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        if (request is null)
        {
            return NotFound();
        }

        return View(new EquipmentRequestDetailsViewModel
        {
            RequestId = request.RequestId,
            DocumentNo = request.DocumentNo,
            Status = request.Status,
            RequesterName = request.RequesterName,
            RequesterPosition = request.RequesterPosition,
            DepartmentName = request.DepartmentName,
            IssueDate = request.IssueDate,
            RequestedDate = request.RequestedDate,
            RequestedStartTime = request.RequestedStartTime,
            RequestedEndTime = request.RequestedEndTime,
            LocationDisplay = request.LocationDisplay,
            WorkDescription = request.WorkDescription,
            ServiceId = request.Line?.ServiceId,
            ServiceName = request.Line?.ServiceName,
            VariantId = request.Line?.VariantId,
            VariantName = request.Line?.VariantName,
            CanDecide = await CanDecideAsync(),
            VariantOptions = await BuildVariantOptionsAsync(request.Line?.ServiceId, request.Line?.VariantId),
            FirmOptions = await BuildFirmOptionsAsync(request.Line?.ServiceId, request.RequestedDate)
        });
    }

    /// <summary>
    /// Onay: gerekirse tarih/saat ve varyant düzeltilir, firma atanır, talep
    /// PENDING_FIRM'e geçer.
    ///
    /// DÜZENLENEBİLEN ALANLARIN TAM LİSTESİ <see cref="EquipmentApprovalModel"/>
    /// içindedir. Lokasyon, iş tanımı ve talep eden bilgileri o modelde YOKTUR;
    /// POST gövdesine elle eklenseler bile bağlanacak bir alan bulunmadığı için
    /// değişmezler.
    ///
    /// Yapılan her düzenleme denetim izine düşer (AuditSaveChangesInterceptor
    /// alan bazlı yazar) ve talep açana ayrıca bildirilir.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PolicyNames.CanDecideEquipmentRequest)]
    public async Task<IActionResult> Approve(int id, EquipmentApprovalModel model)
    {
        var request = await _db.Requests.Include(r => r.RequestLines).FirstOrDefaultAsync(r => r.RequestId == id);
        if (request is null)
        {
            return NotFound();
        }

        if (model.FirmId is not int firmId)
        {
            TempData[TempDataKeys.ErrorMessage] = "Talebin yönlendirileceği firma seçilmelidir.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var line = request.RequestLines.OrderBy(l => l.LineNo).FirstOrDefault();

        // Firma listesi ekranda zaten sınırlı; sunucuda TEKRAR doğrulanır —
        // POST elle kurulmuş olabilir ve sözleşmesiz firmaya yönlendirilen iş
        // fiyatı olmayan bir çalışma kaydı doğururdu.
        var allowedFirmIds = await ActiveContractFirmIdsAsync(line?.ServiceId, model.RequestedDate ?? request.RequestedDate);
        if (!allowedFirmIds.Contains(firmId))
        {
            TempData[TempDataKeys.ErrorMessage] =
                "Seçilen firmanın bu hizmet için talep edilen tarihte aktif sözleşmesi yok; talep bu firmaya yönlendirilemez.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var edits = new List<string>();

        if (model.RequestedDate is DateOnly newDate && newDate != request.RequestedDate)
        {
            edits.Add($"tarih {TrFormat.Date(request.RequestedDate)} → {TrFormat.Date(newDate)}");
            request.RequestedDate = newDate;
        }

        if (model.RequestedStartTime != request.RequestedStartTime)
        {
            edits.Add($"başlangıç saati {FormatTime(request.RequestedStartTime)} → {FormatTime(model.RequestedStartTime)}");

            // Bitiş saati, tahmini süreyi koruyacak şekilde birlikte kayar:
            // yalnızca başlangıcı değiştirmek işi kısaltmış/uzatmış gibi gösterirdi.
            if (request.RequestedStartTime is TimeOnly oldStart && request.RequestedEndTime is TimeOnly oldEnd
                && model.RequestedStartTime is TimeOnly newStart)
            {
                request.RequestedEndTime = newStart.Add(oldEnd - oldStart);
            }

            request.RequestedStartTime = model.RequestedStartTime;
        }

        if (line is not null && model.VariantId != line.VariantId)
        {
            edits.Add("hizmet varyantı değiştirildi");
            line.VariantId = model.VariantId;
        }

        try
        {
            var period = await _flow.GetPeriodAsync(request.RequestedDate);
            var actor = await _flow.GetActorAsync();

            request.FirmId = firmId;
            RequestStateMachine.ApproveByEquipment(request, period, actor, DateTime.UtcNow);

            var firmTitle = await _db.Firms.AsNoTracking()
                .Where(f => f.FirmId == firmId).Select(f => f.Title).FirstOrDefaultAsync();

            // ENVANTER BOŞLUĞU (Adım 16 B): bu geçişte sıradaki karar FİRMANINDIR
            // ama firmaya hiçbir şey gitmiyordu — talebi ekranında görmesini
            // bekliyorduk. Artık haber ona da düşüyor.
            await _notifications.QueueRequestEventAsync(request,
                NotificationQueue.Templates.RequestEquipmentApproved,
                NotificationQueue.Subject(request.DocumentNo, "Talep Ekipman Müdürlüğü tarafından onaylandı"),
                $"{request.DocumentNo} numaralı talep Ekipman Müdürlüğü tarafından onaylandı ve " +
                $"\"{firmTitle}\" firmasına yönlendirildi. " +
                $"Talep edilen tarih: {TrFormat.Date(request.RequestedDate)}. " +
                "Firma yetkilisi: uygulamada \"Bekleyen Talepler\" ekranından işi kabul edip " +
                "operatör ve plaka atamanız bekleniyor. Talep açan: yapmanız gereken bir şey yok.",
                toRequester: true, toFirm: true);

            // Düzenleme AYRI bir bildirim: "onaylandı" ile "saatin değişti"
            // farklı haberlerdir, ikincisi tek satırda kaybolmamalı.
            if (edits.Count > 0)
            {
                await _notifications.QueueRequestEventAsync(request,
                    NotificationQueue.Templates.RequestEquipmentEdited,
                    NotificationQueue.Subject(request.DocumentNo, "Talebinizde düzenleme yapıldı"),
                    $"{request.DocumentNo} numaralı talebiniz onaylanırken şu alanlar düzenlendi: " +
                    $"{string.Join(", ", edits)}. Uygulamadan talebin son hâlini görebilirsiniz.",
                    toRequester: true);
            }

            await _db.SaveChangesAsync();
            TempData[TempDataKeys.SuccessMessage] =
                $"{request.DocumentNo} onaylandı ve \"{firmTitle}\" firmasının onayına gönderildi.";
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
    [Authorize(Policy = PolicyNames.CanDecideEquipmentRequest)]
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

            RequestStateMachine.RejectByEquipment(request, period, actor, reason, DateTime.UtcNow);

            await _notifications.QueueRequestEventAsync(request,
                NotificationQueue.Templates.RequestEquipmentRejected,
                NotificationQueue.Subject(request.DocumentNo, "Talebiniz reddedildi"),
                $"{request.DocumentNo} numaralı talebiniz Ekipman Müdürlüğü tarafından reddedildi. " +
                $"Gerekçe: {reason} İhtiyaç sürüyorsa uygulamadan yeni talep açabilirsiniz.",
                toRequester: true);

            await _db.SaveChangesAsync();
            TempData[TempDataKeys.SuccessMessage] = $"{request.DocumentNo} reddedildi.";
        }
        catch (Exception ex) when (IsBusinessRuleFailure(ex))
        {
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------------------------------------------------------------
    // ADIM 16 — SÜRE İTİRAZI HAKEMLİĞİ
    //
    // Ekipman Müdürlüğü işi ÖNCEDEN onaylayan taraftır; süre tartışmasında da
    // hakem odur. İki çıkış vardır ve üçüncüsü yoktur: saatler düzeltilip
    // onaylanır (CONFIRMED) ya da iş faturalanmayacak olarak kapatılır
    // (CANCELLED). İtiraz askıda bırakılamaz.
    //
    // Karar EQUIPMENT_MANAGER'ındır; EQUIPMENT_VIEWER listeyi görür, POST'ları
    // CanDecideEquipmentRequest policy'siyle sunucuda düşer.
    // ---------------------------------------------------------------

    /// <summary>İtiraz edilmiş talepler — ayrı liste, ayrı iş.</summary>
    public async Task<IActionResult> Disputed()
    {
        var items = await _db.Requests.AsNoTracking()
            .Where(r => r.Status == RequestStatus.DISPUTED)
            .OrderBy(r => r.ConfirmationDecisionAt)
            .Select(r => new DisputedRequestRow
            {
                RequestId = r.RequestId,
                DocumentNo = r.DocumentNo,
                RequestedDate = r.RequestedDate,
                RequesterName = r.RequestedByUser.FullName,
                DepartmentName = r.Department.Name,
                LocationDisplay = r.Location != null ? r.Location.FullPath ?? r.Location.Name : r.LocationText,
                FirmTitle = r.Firm != null ? r.Firm.Title : null,
                ActualStartTime = r.ActualStartTime,
                ActualEndTime = r.ActualEndTime,
                DisputeReason = r.DisputeReason,
                DisputedAt = r.ConfirmationDecisionAt
            })
            .ToListAsync();

        return View(new DisputedRequestsViewModel { Items = items, CanDecide = await CanDecideAsync() });
    }

    /// <summary>Hakem ekranı: operatörün saatleri, itiraz gerekçesi, düzeltme formu.</summary>
    public async Task<IActionResult> Dispute(int id)
    {
        var canDecide = await CanDecideAsync();

        var model = await _db.Requests.AsNoTracking()
            .Where(r => r.RequestId == id)
            .Select(r => new DisputeResolutionViewModel
            {
                RequestId = r.RequestId,
                DocumentNo = r.DocumentNo,
                Status = r.Status,
                RequesterName = r.RequestedByUser.FullName,
                RequesterPosition = r.RequestedByUser.Position,
                DepartmentName = r.Department.Name,
                RequestedDate = r.RequestedDate,
                LocationDisplay = r.Location != null ? r.Location.FullPath ?? r.Location.Name : r.LocationText,
                WorkDescription = r.WorkDescription,
                ServiceDisplay = r.RequestLines
                    .OrderBy(l => l.LineNo)
                    .Select(l => l.ServiceVariant != null
                        ? l.ServiceCategory.Name + " — " + l.ServiceVariant.Name
                        : l.ServiceCategory.Name)
                    .FirstOrDefault(),
                FirmTitle = r.Firm != null ? r.Firm.Title : null,
                AssignedOperatorName = r.AssignedOperatorName,
                AssignedLicensePlate = r.AssignedLicensePlate,
                ActualStartTime = r.ActualStartTime,
                ActualEndTime = r.ActualEndTime,
                DisputeReason = r.DisputeReason,
                DisputedAt = r.ConfirmationDecisionAt,
                CanDecide = canDecide
            })
            .FirstOrDefaultAsync();

        return model is null ? NotFound() : View(model);
    }

    /// <summary>
    /// DISPUTED -> CONFIRMED. Saatler düzeltilebilir; boş bırakılan alan
    /// DEĞİŞMEZ. Ekrandan YEREL saat gelir, veritabanına UTC yazılır.
    ///
    /// Düzeltme alan bazlı denetim izine düşer: kimin hangi saati neyle
    /// değiştirdiği AuditLogs'ta eski/yeni değeriyle durur.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PolicyNames.CanDecideEquipmentRequest)]
    public async Task<IActionResult> ResolveDispute(int id, DateTime? actualStart, DateTime? actualEnd)
    {
        var request = await _db.Requests.FirstOrDefaultAsync(r => r.RequestId == id);
        if (request is null)
        {
            return NotFound();
        }

        var previousStart = request.ActualStartTime;
        var previousEnd = request.ActualEndTime;

        try
        {
            var period = await _flow.GetPeriodAsync(request.RequestedDate);
            var actor = await _flow.GetActorAsync();

            RequestStateMachine.ResolveDispute(
                request, period, actor, ToUtc(actualStart), ToUtc(actualEnd), DateTime.UtcNow);

            var corrected = request.ActualStartTime != previousStart || request.ActualEndTime != previousEnd;
            var correction = corrected
                ? $"Saatler düzeltildi: {TrFormat.DateTimeLocal(request.ActualStartTime!.Value)} - " +
                  $"{TrFormat.DateTimeLocal(request.ActualEndTime!.Value)}. "
                : "Operatörün girdiği saatler değiştirilmedi. ";

            await _notifications.QueueRequestEventAsync(request,
                NotificationQueue.Templates.RequestDisputeResolved,
                NotificationQueue.Subject(request.DocumentNo, "Süre itirazı karara bağlandı"),
                $"{request.DocumentNo} numaralı talebin süre itirazı Ekipman Müdürlüğü tarafından " +
                $"karara bağlandı. {correction}" +
                $"Geçerli süre: {TrFormat.Duration(request.ActualEndTime!.Value - request.ActualStartTime!.Value)}. " +
                "Çalışma kaydı bu saatlerle oluşturuldu; yapmanız gereken bir şey yok.",
                toRequester: true, toFirm: true);

            await _db.SaveChangesAsync();
        }
        catch (Exception ex) when (IsBusinessRuleFailure(ex))
        {
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
            return RedirectToAction(nameof(Dispute), new { id });
        }

        // Türetme ikinci commit'te: patlarsa karar kaybolmaz.
        await _derivation.TryDeriveAsync(id);

        TempData[TempDataKeys.SuccessMessage] = $"{request.DocumentNo} için itiraz karara bağlandı.";
        return RedirectToAction(nameof(Disputed));
    }

    /// <summary>
    /// DISPUTED -> CANCELLED: "bu iş faturalanmayacak". Gerekçe ZORUNLU.
    /// Bu talepten çalışma kaydı TÜRETİLMEZ.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PolicyNames.CanDecideEquipmentRequest)]
    public async Task<IActionResult> CancelDisputed(int id, string? reason)
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

            RequestStateMachine.CancelDisputed(request, period, actor, reason, DateTime.UtcNow);

            await _notifications.QueueRequestEventAsync(request,
                NotificationQueue.Templates.RequestDisputeCancelled,
                NotificationQueue.Subject(request.DocumentNo, "Talep faturalanmayacak olarak kapatıldı"),
                $"{request.DocumentNo} numaralı talep, süre itirazı sonucunda Ekipman Müdürlüğü " +
                $"tarafından faturalanmayacak olarak kapatıldı. Gerekçe: {reason} " +
                "Bu işten çalışma kaydı oluşturulmayacaktır.",
                toRequester: true, toFirm: true);

            await _db.SaveChangesAsync();
            TempData[TempDataKeys.SuccessMessage] = $"{request.DocumentNo} faturalanmayacak olarak kapatıldı.";
            return RedirectToAction(nameof(Disputed));
        }
        catch (Exception ex) when (IsBusinessRuleFailure(ex))
        {
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
            return RedirectToAction(nameof(Dispute), new { id });
        }
    }

    /// <summary>
    /// Ekrandan gelen saat YERELDİR (datetime-local), veritabanı UTC tutar.
    /// Kind belirtilmemiş değer yerel kabul edilir — uygulamanın her yerindeki
    /// varsayımın aynısı.
    /// </summary>
    private static DateTime? ToUtc(DateTime? localValue) =>
        localValue is null ? null : DateTime.SpecifyKind(localValue.Value, DateTimeKind.Local).ToUniversalTime();

    /// <summary>Varyant değişince firma listesi de değişebilir; htmx ile tazelenir.</summary>
    [HttpGet]
    public async Task<IActionResult> FirmOptions(int? serviceId, DateOnly? requestedDate) =>
        PartialView("_FirmOptions",
            await BuildFirmOptionsAsync(serviceId, requestedDate ?? DateOnly.FromDateTime(DateTime.Now)));

    // ---------------------------------------------------------------

    private async Task<bool> CanDecideAsync() =>
        (await _authorization.AuthorizeAsync(User, PolicyNames.CanDecideEquipmentRequest)).Succeeded;

    private async Task<List<SelectListItem>> BuildVariantOptionsAsync(int? serviceId, int? selectedVariantId) =>
        serviceId is null or 0
            ? new List<SelectListItem>()
            : await _db.ServiceVariants.AsNoTracking()
                .Where(v => v.ServiceId == serviceId && v.IsActive)
                .OrderBy(v => v.Name)
                .Select(v => new SelectListItem(v.Name, v.VariantId.ToString(), v.VariantId == selectedVariantId))
                .ToListAsync();

    private async Task<List<SelectListItem>> BuildFirmOptionsAsync(int? serviceId, DateOnly requestedDate)
    {
        var firmIds = await ActiveContractFirmIdsAsync(serviceId, requestedDate);
        return await _db.Firms.AsNoTracking()
            .Where(f => f.IsActive && firmIds.Contains(f.FirmId))
            .OrderBy(f => f.Title)
            .Select(f => new SelectListItem(f.Title, f.FirmId.ToString()))
            .ToListAsync();
    }

    /// <summary>
    /// O hizmet için, talep edilen TARİHTE aktif sözleşmesi olan firmalar.
    ///
    /// Tarih koşulu CLAUDE.md kural 3'ün aynısıdır: doğru sözleşme satırı işin
    /// yapılacağı tarihe göre seçilir. Bugün aktif ama gelecek ay bitecek bir
    /// sözleşme, gelecek aya açılan bir talebi karşılayamaz.
    ///
    /// Varyant koşulu BİLİNÇLİ OLARAK yok: Ekipman Müdürlüğü varyantı zaten
    /// değiştirebiliyor, listeyi istenen varyanta kilitlemek "başka kapasite
    /// atayabilir" kuralını ortadan kaldırırdı.
    /// </summary>
    private async Task<List<int>> ActiveContractFirmIdsAsync(int? serviceId, DateOnly requestedDate)
    {
        if (serviceId is null or 0)
        {
            return new List<int>();
        }

        return await _db.ContractLines.AsNoTracking()
            .Where(l => l.ServiceId == serviceId
                && l.IsActive
                && l.ValidFrom <= requestedDate
                && (l.ValidTo == null || l.ValidTo >= requestedDate)
                && l.Contract.Status == ContractStatus.ACTIVE
                && l.Contract.StartDate <= requestedDate
                && l.Contract.EndDate >= requestedDate)
            .Select(l => l.Contract.FirmId)
            .Distinct()
            .ToListAsync();
    }

    private static string FormatTime(TimeOnly? value) => value is null ? "—" : TrFormat.Time(value.Value);

    private static bool IsBusinessRuleFailure(Exception ex) =>
        ex is RequestStateTransitionException
            or ApprovalAuthorizationException
            or PeriodGuardException
            or ImmutabilityViolationException;
}
