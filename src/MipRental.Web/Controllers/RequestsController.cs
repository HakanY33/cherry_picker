using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Services;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Approvals;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;
using MipRental.Web.Common;
using MipRental.Web.Models.Requests;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

/// <summary>
/// ADIM 11 — TALEP AÇANIN EKRANLARI (REQUESTER).
///
/// Üç sınır bu controller'da birden geçerlidir:
///   1. Policy: yalnızca REQUESTER rolü girer.
///   2. Sahiplik: HER sorgu RequestedByUserId == oturumdaki kullanıcı ile
///      sınırlanır. Talep açan MIP personelidir, dolayısıyla firma izolasyon
///      query filter'ı (kural 7) ona hiçbir şey kısıtlamaz — sahiplik sınırını
///      burada açıkça kurmak zorundayız.
///   3. Durum geçişleri: Status'a DOĞRUDAN atama yoktur, hepsi
///      RequestStateMachine üzerinden.
///
/// FİYAT YOK: bu controller hiçbir action'da tutar okumaz, hiçbir view model'de
/// para alanı yoktur. → [[Fiyat Gizliliği]]
/// </summary>
[Authorize(Policy = PolicyNames.CanCreateRequest)]
public class RequestsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly RequestFlowService _flow;
    private readonly DocumentNumberService _documentNumbers;
    private readonly NotificationQueue _notifications;

    public RequestsController(
        AppDbContext db,
        ICurrentUser currentUser,
        RequestFlowService flow,
        DocumentNumberService documentNumbers,
        NotificationQueue notifications)
    {
        _db = db;
        _currentUser = currentUser;
        _flow = flow;
        _documentNumbers = documentNumbers;
        _notifications = notifications;
    }

    // ---------------------------------------------------------------
    // 1.2 Taleplerim
    // ---------------------------------------------------------------

    public async Task<IActionResult> Index(int page = 1, string? status = null, DateOnly? from = null, DateOnly? to = null)
    {
        var query = OwnRequests();

        // Filtre SADELEŞTİRİLMİŞ etiketle yapılır ("Bekliyor"), gerçek durumla
        // değil: talep açana iç adımları göstermiyorsak, o adımlara göre filtre
        // de sunmamalıyız. Etiket, arkasındaki gerçek durum kümesine çevrilir.
        var statuses = RequestStatusDisplay.StatusesFor(status);
        if (statuses.Count > 0)
        {
            query = query.Where(r => statuses.Contains(r.Status));
        }

        if (from is not null)
        {
            query = query.Where(r => r.RequestedDate >= from);
        }

        if (to is not null)
        {
            query = query.Where(r => r.RequestedDate <= to);
        }

        page = page < 1 ? 1 : page;
        var totalCount = await query.CountAsync();

        // Sunucu tarafı sayfalama: entity değil DTO projeksiyonu.
        var items = await query
            .OrderByDescending(r => r.RequestedDate).ThenByDescending(r => r.RequestId)
            .Skip((page - 1) * PagingHelper.PageSize)
            .Take(PagingHelper.PageSize)
            .Select(r => new MyRequestRow
            {
                RequestId = r.RequestId,
                DocumentNo = r.DocumentNo,
                Status = r.Status,
                RequestedDate = r.RequestedDate,
                RequestedStartTime = r.RequestedStartTime,
                LocationDisplay = r.Location != null ? r.Location.FullPath ?? r.Location.Name : r.LocationText,
                ServiceDisplay = r.RequestLines
                    .OrderBy(l => l.LineNo)
                    .Select(l => l.ServiceVariant != null
                        ? l.ServiceCategory.Name + " — " + l.ServiceVariant.Name
                        : l.ServiceCategory.Name)
                    .FirstOrDefault(),
                WorkDescription = r.WorkDescription
            })
            .ToListAsync();

        return View(new MyRequestsViewModel
        {
            Items = items,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling(totalCount / (double)PagingHelper.PageSize),
            Status = status,
            From = from,
            To = to
        });
    }

    public async Task<IActionResult> Details(int id)
    {
        var model = await OwnRequests()
            .Where(r => r.RequestId == id)
            .Select(r => new RequestDetailsViewModel
            {
                RequestId = r.RequestId,
                DocumentNo = r.DocumentNo,
                Status = r.Status,
                RequesterName = r.RequestedByUser.FullName,
                RequesterPosition = r.RequestedByUser.Position,
                DepartmentName = r.Department.Name,
                IssueDate = r.IssueDate,
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
                FirmTitle = r.Firm != null ? r.Firm.Title : null,
                AssignedOperatorName = r.AssignedOperatorName,
                AssignedLicensePlate = r.AssignedLicensePlate,
                RejectionReason = r.RejectionReason,
                CancellationReason = r.CancellationReason
            })
            .FirstOrDefaultAsync();

        if (model is null)
        {
            return NotFound();
        }

        model.History = await BuildHistoryAsync(id, model);
        return View(model);
    }

    // ---------------------------------------------------------------
    // 1.1 Yeni talep
    // ---------------------------------------------------------------

    public async Task<IActionResult> Create()
    {
        var model = new RequestFormViewModel { RequestedDate = DateOnly.FromDateTime(DateTime.Now).AddDays(1) };
        await PopulateAsync(model);
        return View(model);
    }

    /// <summary>
    /// Tek form, İKİ buton: "Kaydet" taslak bırakır, "Gönder" onay zincirini
    /// başlatır. Zorunlu alan kontrolü SADECE gönderimde uygulanır — taslağın
    /// tanımı zaten "henüz tamamlanmamış talep"tir.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RequestFormViewModel model, string? action)
    {
        var submit = string.Equals(action, "submit", StringComparison.Ordinal);

        // Departman OTURUMDAN alınır, modelden DEĞİL: form ne gönderirse
        // göndersin başka departman adına talep açılamaz.
        if (_currentUser.DepartmentId is not int departmentId)
        {
            ModelState.AddModelError(string.Empty,
                "Kullanıcınıza bir departman tanımlı değil; talep açabilmek için yöneticinizle görüşün.");
            await PopulateAsync(model);
            return View(model);
        }

        if (submit)
        {
            foreach (var missing in FindMissingRequiredFields(model))
            {
                ModelState.AddModelError(string.Empty, missing);
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateAsync(model);
            return View(model);
        }

        var requestedDate = model.RequestedDate!.Value;
        var request = new Request
        {
            // Gerçek belge numarası yalnızca gönderimde verilir; taslağın
            // benzersiz-ama-geçici bir DocumentNo'ya ihtiyacı var (NOT NULL +
            // UNIQUE, maxlength 30). Çalışma kaydındaki desenin aynısı.
            DocumentNo = $"DRAFT-{Guid.NewGuid():N}"[..30],
            Status = RequestStatus.DRAFT,
            RequestedByUserId = _currentUser.UserId,
            DepartmentId = departmentId,
            IssueDate = DateOnly.FromDateTime(DateTime.Now),
            RequestedDate = requestedDate,
            RequestedStartTime = model.RequestedStartTime,
            RequestedEndTime = EstimateEndTime(model.RequestedStartTime, model.EstimatedHours),
            LocationId = model.LocationId,
            WorkDescription = model.WorkDescription
        };

        if (model.ServiceId is int serviceId)
        {
            request.RequestLines.Add(new RequestLine
            {
                LineNo = 1,
                ServiceId = serviceId,
                VariantId = model.VariantId,
                EstimatedQuantity = model.EstimatedHours
            });
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            _db.Requests.Add(request);
            await _db.SaveChangesAsync();

            if (submit)
            {
                var period = await _flow.GetPeriodAsync(requestedDate);
                var actor = await _flow.GetActorAsync();

                request.DocumentNo = await _documentNumbers.IssueNumberAsync(DocumentType.REQUEST, requestedDate.Year);

                // Gönderim ve ilk onay adımının açılması TEK transaction:
                // talep "gönderildi ama kimseye düşmedi" durumunda kalamaz.
                RequestStateMachine.Submit(request, period, actor, DateTime.UtcNow);
                RequestStateMachine.SendToEquipment(request, period, actor);

                await _notifications.QueueRequestEventAsync(request,
                    NotificationQueue.Templates.RequestSubmitted,
                    $"Yeni talep: {request.DocumentNo}",
                    $"{request.DocumentNo} numaralı talep Ekipman Müdürlüğü onayını bekliyor. " +
                    $"Talep edilen tarih: {TrFormat.Date(request.RequestedDate)}.",
                    toEquipment: true);

                await _db.SaveChangesAsync();
            }

            await transaction.CommitAsync();
        }
        catch (Exception ex) when (IsBusinessRuleFailure(ex))
        {
            await transaction.RollbackAsync();
            _db.ChangeTracker.Clear();
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateAsync(model);
            return View(model);
        }

        TempData[TempDataKeys.SuccessMessage] = submit
            ? $"Talep {request.DocumentNo} numarasıyla gönderildi ve Ekipman Müdürlüğü onayına düştü."
            : "Talep taslak olarak kaydedildi.";
        return RedirectToAction(nameof(Details), new { id = request.RequestId });
    }

    /// <summary>
    /// Taslağı gönderime alır. Ayrı bir action: taslak kaydedildikten sonra
    /// detay ekranından da gönderilebilmeli.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id)
    {
        var request = await OwnRequests(tracked: true)
            .Include(r => r.RequestLines)
            .FirstOrDefaultAsync(r => r.RequestId == id);
        if (request is null)
        {
            return NotFound();
        }

        var missing = FindMissingRequiredFields(request);
        if (missing.Count > 0)
        {
            TempData[TempDataKeys.ErrorMessage] = "Gönderim için eksik alanlar var: " + string.Join(", ", missing);
            return RedirectToAction(nameof(Details), new { id });
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var period = await _flow.GetPeriodAsync(request.RequestedDate);
            var actor = await _flow.GetActorAsync();

            request.DocumentNo = await _documentNumbers.IssueNumberAsync(DocumentType.REQUEST, request.RequestedDate.Year);
            RequestStateMachine.Submit(request, period, actor, DateTime.UtcNow);
            RequestStateMachine.SendToEquipment(request, period, actor);

            await _notifications.QueueRequestEventAsync(request,
                NotificationQueue.Templates.RequestSubmitted,
                $"Yeni talep: {request.DocumentNo}",
                $"{request.DocumentNo} numaralı talep Ekipman Müdürlüğü onayını bekliyor. " +
                $"Talep edilen tarih: {TrFormat.Date(request.RequestedDate)}.",
                toEquipment: true);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData[TempDataKeys.SuccessMessage] = $"Talep {request.DocumentNo} numarasıyla gönderildi.";
        }
        catch (Exception ex) when (IsBusinessRuleFailure(ex))
        {
            await transaction.RollbackAsync();
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>DRAFT ve SCHEDULED'da iptal. Gerekçe ZORUNLU (durum makinesi zorlar).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? reason)
    {
        var request = await OwnRequests(tracked: true).FirstOrDefaultAsync(r => r.RequestId == id);
        if (request is null)
        {
            return NotFound();
        }

        try
        {
            var period = await _flow.GetPeriodAsync(request.RequestedDate);
            var actor = await _flow.GetActorAsync();

            RequestStateMachine.Cancel(request, period, actor, reason, DateTime.UtcNow);

            // İptal ilgili TARAFLARA duyurulur: ekipman her hâlükârda,
            // firma yalnızca talep ona yönlendirilmişse (DRAFT'ta firma yok).
            await _notifications.QueueRequestEventAsync(request,
                NotificationQueue.Templates.RequestCancelled,
                $"İptal edildi: {request.DocumentNo}",
                $"{request.DocumentNo} numaralı talep, talebi açan kişi tarafından iptal edildi. Gerekçe: {reason}",
                toEquipment: true, toFirm: true);

            await _db.SaveChangesAsync();
            TempData[TempDataKeys.SuccessMessage] = "Talep iptal edildi.";
        }
        catch (Exception ex) when (IsBusinessRuleFailure(ex))
        {
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Hizmet seçimi değişince varyant listesini htmx ile tazeler.</summary>
    [HttpGet]
    public async Task<IActionResult> VariantOptions(int? serviceId)
    {
        return PartialView("_VariantOptions", await BuildVariantOptionsAsync(serviceId, null));
    }

    // ---------------------------------------------------------------

    /// <summary>
    /// SAHİPLİK SINIRI. Talep açan MIP personelidir; firma izolasyon filtresi
    /// ona hiçbir kaydı gizlemez, bu yüzden "sadece kendi taleplerim" kuralını
    /// burada tek bir yerde kuruyoruz ve tüm action'lar buradan geçiyor.
    /// </summary>
    private IQueryable<Request> OwnRequests(bool tracked = false)
    {
        var query = tracked ? _db.Requests : _db.Requests.AsNoTracking();
        return query.Where(r => r.RequestedByUserId == _currentUser.UserId);
    }

    private async Task<List<RequestStatusHistoryRow>> BuildHistoryAsync(int requestId, RequestDetailsViewModel model)
    {
        // Denetim izi durum değişikliklerini zaten alan bazlı tutuyor; ayrı bir
        // geçmiş tablosu aynı bilgiyi ikinci kez saklardı.
        var entries = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.TableName == "Requests" && a.RecordId == requestId && a.FieldName == nameof(Domain.Entities.Request.Status))
            .OrderBy(a => a.OccurredAt)
            .Select(a => new { a.OccurredAt, a.OldValue, a.NewValue, a.UserId })
            .ToListAsync();

        var userIds = entries.Where(e => e.UserId is not null).Select(e => e.UserId!.Value).Distinct().ToList();

        // Kararı KİMİN verdiği iki farklı şekilde yazılır ve bu bilinçlidir:
        //   MIP personeli    -> kişinin adı  (aynı kurumun içi; kim onayladığı bilinir)
        //   Firma kullanıcısı -> FİRMANIN adı (talep açan için karar veren taraf
        //                        firmadır; alt yüklenici çalışanının adı MIP'i
        //                        ilgilendirmez ve bu ekrana taşınmaz)
        //
        // Projeksiyon bu ayrımı SORGUDA yapar: firma kullanıcısının FullName
        // alanı modele hiç girmez.
        var names = userIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => userIds.Contains(u.UserId))
                .Select(u => new
                {
                    u.UserId,
                    DisplayName = u.FirmId == null ? u.FullName : (u.Firm!.Title ?? "Firma")
                })
                .ToDictionaryAsync(u => u.UserId, u => u.DisplayName);

        return entries
            .Where(e => Enum.TryParse<RequestStatus>(e.NewValue, out _))
            .Select(e =>
            {
                var to = Enum.Parse<RequestStatus>(e.NewValue!);
                return new RequestStatusHistoryRow
                {
                    OccurredAt = e.OccurredAt,
                    From = Enum.TryParse<RequestStatus>(e.OldValue, out var from) ? from : null,
                    To = to,
                    ByName = e.UserId is int id ? names.GetValueOrDefault(id) : null,
                    // Red ve iptal terminal durumlardır: talep başına en fazla
                    // birer tane olabilir, bu yüzden gerekçe kaydın kendisinden
                    // okunabilir ve denetim izinde ayrıca aranması gerekmez.
                    Reason = to is RequestStatus.REJECTED_BY_EQUIPMENT or RequestStatus.REJECTED_BY_FIRM
                        ? model.RejectionReason
                        : to == RequestStatus.CANCELLED ? model.CancellationReason : null
                };
            })
            .ToList();
    }

    /// <summary>Başlangıç + tahmini süre = tahmini bitiş. İkisi de yoksa bitiş de yok.</summary>
    private static TimeOnly? EstimateEndTime(TimeOnly? start, decimal? hours) =>
        start is null || hours is null or <= 0 ? null : start.Value.AddHours((double)hours.Value);

    private static List<string> FindMissingRequiredFields(RequestFormViewModel model)
    {
        var missing = new List<string>();
        if (model.RequestedStartTime is null) missing.Add("Başlangıç saati zorunludur.");
        if (model.LocationId is null) missing.Add("Lokasyon zorunludur.");
        if (string.IsNullOrWhiteSpace(model.WorkDescription)) missing.Add("İş tanımı zorunludur.");
        if (model.ServiceId is null or 0) missing.Add("İstenen hizmet zorunludur.");
        return missing;
    }

    private static List<string> FindMissingRequiredFields(Request request)
    {
        var missing = new List<string>();
        if (request.RequestedStartTime is null) missing.Add("Başlangıç Saati");
        if (request.LocationId is null && string.IsNullOrWhiteSpace(request.LocationText)) missing.Add("Lokasyon");
        if (string.IsNullOrWhiteSpace(request.WorkDescription)) missing.Add("İş Tanımı");
        if (request.RequestLines.Count == 0) missing.Add("İstenen Hizmet");
        return missing;
    }

    private async Task PopulateAsync(RequestFormViewModel model)
    {
        // İsim, görev, departman OTURUMDAN; kullanıcı bunları giremez.
        var me = await _db.Users.AsNoTracking()
            .Where(u => u.UserId == _currentUser.UserId)
            .Select(u => new { u.FullName, u.Position, DepartmentName = u.Department != null ? u.Department.Name : null })
            .FirstOrDefaultAsync();

        model.RequesterName = me?.FullName ?? _currentUser.FullName;
        model.RequesterPosition = me?.Position;
        model.DepartmentName = me?.DepartmentName;
        model.IssueDate = DateOnly.FromDateTime(DateTime.Now);

        // Lokasyon ağacı: FullPath'e göre sıralı liste ağacın kendisidir
        // ("Liman > Rıhtım 3 > İskele A"), ayrı bir ağaç bileşeni gerekmez.
        model.LocationOptions = await _db.Locations.AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.FullPath ?? l.Name)
            .Select(l => new SelectListItem(l.FullPath ?? l.Name, l.LocationId.ToString(), l.LocationId == model.LocationId))
            .ToListAsync();

        model.ServiceOptions = await _db.ServiceCategories.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new SelectListItem(s.Name, s.ServiceId.ToString(), s.ServiceId == model.ServiceId))
            .ToListAsync();

        model.VariantOptions = await BuildVariantOptionsAsync(model.ServiceId, model.VariantId);
    }

    private async Task<List<SelectListItem>> BuildVariantOptionsAsync(int? serviceId, int? selectedVariantId) =>
        serviceId is null or 0
            ? new List<SelectListItem>()
            : await _db.ServiceVariants.AsNoTracking()
                .Where(v => v.ServiceId == serviceId && v.IsActive)
                .OrderBy(v => v.Name)
                .Select(v => new SelectListItem(v.Name, v.VariantId.ToString(), v.VariantId == selectedVariantId))
                .ToListAsync();

    private static bool IsBusinessRuleFailure(Exception ex) =>
        ex is RequestStateTransitionException
            or ApprovalAuthorizationException
            or PeriodGuardException
            or ImmutabilityViolationException;
}
