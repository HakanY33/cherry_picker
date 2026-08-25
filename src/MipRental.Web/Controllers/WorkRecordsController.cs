using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Approvals;
using MipRental.Data.Pricing;
using MipRental.Data.Services;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Approvals;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;
using MipRental.Domain.Pricing;
using MipRental.Web.Common;
using MipRental.Web.Documents;
using MipRental.Web.Models.WorkRecords;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

// CLAUDE.md Adım 6 Bölüm B: alt yüklenici çalışma kaydı girişi + MIP görünümü.
// Not: ayrı bir "Edit" (taslak düzenleme) ekranı BİLİNÇLİ OLARAK yok — Bölüm A'daki
// ImmutabilityGuardInterceptor TÜM entity'lerde DELETE'i genel olarak engelliyor,
// yani DRAFT aşamasında bile kaydedilmiş bir satır asla silinemez. Bu yüzden "satır
// çıkar" ancak henüz kaydedilmemiş (formda yeni eklenmiş) satırlar için mümkün;
// var olan bir taslağı satır bazında düzenlemek ayrı bir ürün kararı gerektirir
// (yumuşak silme/IsRemoved gibi). Rapor'da ayrıca not edilmiştir.
[Authorize]
public class WorkRecordsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ContractLineResolver _resolver;
    private readonly DocumentNumberService _documentNumberService;
    private readonly ApprovalService _approvalService;
    private readonly WorkRecordRevisionService _revisionService;
    private readonly DocumentGenerator _documents;

    public WorkRecordsController(
        AppDbContext db,
        ICurrentUser currentUser,
        ContractLineResolver resolver,
        DocumentNumberService documentNumberService,
        ApprovalService approvalService,
        WorkRecordRevisionService revisionService,
        DocumentGenerator documents)
    {
        _db = db;
        _currentUser = currentUser;
        _resolver = resolver;
        _documents = documents;
        _documentNumberService = documentNumberService;
        _approvalService = approvalService;
        _revisionService = revisionService;
    }

    // ---------------------------------------------------------------
    // B1 (liste) + B5 (MIP görünümü): DbContext query filter firma
    // kullanıcısını zaten kendi firmasına sabitliyor; MIP personeli
    // (FirmId=null) için filtre tüm firmaları döner.
    // ---------------------------------------------------------------
    public async Task<IActionResult> Index(int page = 1, int? firmId = null, int? periodId = null, WorkRecordStatus? status = null)
    {
        var query = _db.WorkRecords.Include(w => w.Firm).Include(w => w.Period).AsQueryable();

        if (_currentUser.IsMipStaff && firmId is not null)
        {
            query = query.Where(w => w.FirmId == firmId);
        }

        if (periodId is not null)
        {
            query = query.Where(w => w.PeriodId == periodId);
        }

        if (status is not null)
        {
            query = query.Where(w => w.Status == status);
        }

        page = page < 1 ? 1 : page;
        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(w => w.WorkDate).ThenByDescending(w => w.WorkRecordId)
            .Skip((page - 1) * PagingHelper.PageSize)
            .Take(PagingHelper.PageSize)
            .ToListAsync();

        var model = new WorkRecordIndexViewModel
        {
            Items = items,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling(totalCount / (double)PagingHelper.PageSize),
            ShowFirmFilter = _currentUser.IsMipStaff,
            FirmId = firmId,
            PeriodId = periodId,
            Status = status,
            FirmOptions = _currentUser.IsMipStaff ? await BuildFirmOptionsAsync(firmId) : new List<SelectListItem>(),
            PeriodOptions = await BuildPeriodOptionsAsync(periodId, onlyOpen: false)
        };
        return View(model);
    }

    /// <summary>
    /// Çalışma kaydının PDF formu.
    ///
    /// Her indirişte YENİ BİR SÜRÜM üretilir: eski GeneratedDocuments kaydı
    /// silinmez, dosyası diskte kalır ve o kâğıdın doğrulama kodu çalışmaya devam
    /// eder. Firma izolasyonu WorkRecords üzerindeki global filtreyle uygulanır —
    /// başka firmanın kaydı için sorgu boş döner ve NotFound alınır.
    /// </summary>
    public async Task<IActionResult> Pdf(int id)
    {
        var exists = await _db.WorkRecords.AsNoTracking().AnyAsync(w => w.WorkRecordId == id);
        if (!exists)
        {
            return NotFound();
        }

        var result = await _documents.GenerateWorkRecordFormAsync(id, BuildVerificationUrl);
        return File(result.Content, "application/pdf", result.FileName);
    }

    private string BuildVerificationUrl(string code) =>
        Url.Action(nameof(VerificationController.Index), "Verification", new { code }, Request.Scheme)
        ?? $"{Request.Scheme}://{Request.Host}/Dogrula/{code}";

    public async Task<IActionResult> Details(int id)
    {
        var record = await _db.WorkRecords
            .Include(w => w.Firm)
            .Include(w => w.Contract)
            .Include(w => w.Period)
            .Include(w => w.Location)
            .Include(w => w.Equipment)
            .Include(w => w.EnteredByUser)
            .Include(w => w.WorkRecordLines).ThenInclude(l => l.ServiceCategory)
            .Include(w => w.WorkRecordLines).ThenInclude(l => l.ServiceVariant)
            .FirstOrDefaultAsync(w => w.WorkRecordId == id);
        if (record is null)
        {
            return NotFound();
        }

        var lineIds = record.WorkRecordLines.Select(l => l.WorkRecordLineId).ToList();
        var auditEntries = await _db.AuditLogs
            .Where(a => (a.TableName == "WorkRecords" && a.RecordId == id)
                     || (a.TableName == "WorkRecordLines" && lineIds.Contains(a.RecordId)))
            .OrderByDescending(a => a.OccurredAt)
            .Take(200)
            .ToListAsync();

        // RequestedByUser/WitnessedByUser her zaman MIP personelidir (FirmId = null).
        // User entity'sindeki firma izolasyon filtresi bir firma kullanıcısının bu
        // kayıtları normal Include ile görmesini engeller (kural 7); isim görüntülemek
        // firma verisi sızdırmadığı için burada bilinçli olarak bypass ediyoruz.
        //
        // Filtre bypass edildiği için kısıtlamayı BURADA açıkça tekrar kuruyoruz:
        //   - u.FirmId == null  -> sadece MIP personeli; kaydın alanı (bozuk veri ya da
        //     ileride değişecek bir kural yüzünden) başka bir firmanın kullanıcısını
        //     gösterse bile o kullanıcının adı DÖNMEZ.
        //   - Select(Id, FullName) -> tam User entity'si (PasswordHash, Email, Phone,
        //     FirmId, ExternalId...) hiç materyalize edilmez; SQL yalnızca iki kolon çeker.
        var mipStaffIds = new[] { record.RequestedByUserId, record.WitnessedByUserId }
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
        var mipStaffNames = mipStaffIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.FirmId == null && mipStaffIds.Contains(u.UserId))
                .Select(u => new { u.UserId, u.FullName })
                .ToDictionaryAsync(u => u.UserId, u => u.FullName);

        // Revizyon zinciri: "Bu kayıt X'in 2. versiyonudur" + önceki/sonraki versiyona link.
        var previousVersion = record.RevisionOfId is int previousId
            ? await _db.WorkRecords.AsNoTracking()
                .Where(w => w.WorkRecordId == previousId)
                .Select(w => new WorkRecordVersionLink { WorkRecordId = w.WorkRecordId, DocumentNo = w.DocumentNo, Status = w.Status })
                .FirstOrDefaultAsync()
            : null;

        var nextVersion = await _db.WorkRecords.AsNoTracking()
            .Where(w => w.RevisionOfId == record.WorkRecordId)
            .Select(w => new WorkRecordVersionLink { WorkRecordId = w.WorkRecordId, DocumentNo = w.DocumentNo, Status = w.Status })
            .FirstOrDefaultAsync();

        var model = new WorkRecordDetailsViewModel
        {
            WorkRecord = record,
            AuditEntries = auditEntries,
            RequestedByName = record.RequestedByUserId is int requestedById ? mipStaffNames.GetValueOrDefault(requestedById) : null,
            WitnessedByName = record.WitnessedByUserId is int witnessedById ? mipStaffNames.GetValueOrDefault(witnessedById) : null,
            ApprovalHistory = await _approvalService.GetHistoryAsync(id),
            CanDecide = await _approvalService.CanCurrentUserDecideAsync(id),
            PreviousVersion = previousVersion,
            NextVersion = nextVersion,
            VersionNumber = WorkRecordRevisionService.VersionOf(record.DocumentNo),
            RootDocumentNo = WorkRecordRevisionService.BaseDocumentNo(record.DocumentNo)
        };
        return View(model);
    }

    // ---------------------------------------------------------------
    // B1: yeni taslak kayıt (sadece firma kullanıcısı).
    // ---------------------------------------------------------------
    [Authorize(Policy = PolicyNames.FirmUser)]
    public async Task<IActionResult> Create()
    {
        var model = new WorkRecordFormViewModel
        {
            WorkDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Lines = new List<WorkRecordLineFormViewModel> { new() { Index = 0 } }
        };
        await PopulateOptionsAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PolicyNames.FirmUser)]
    public async Task<IActionResult> Create(WorkRecordFormViewModel model)
    {
        NormalizeLines(model);
        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model);
            return View(model);
        }

        // Firma kullanıcısı hangi FirmId'yi POST ederse etsin (tampered olsa dahi)
        // sunucu HER ZAMAN oturumdaki firmayı kullanır — başka firma adına kayıt
        // oluşturulamaz.
        var firmId = _currentUser.FirmId!.Value;

        var contractId = await _db.Contracts
            .Where(c => c.FirmId == firmId && c.Status == ContractStatus.ACTIVE)
            .Select(c => (int?)c.ContractId)
            .FirstOrDefaultAsync();
        if (contractId is null)
        {
            ModelState.AddModelError(string.Empty, "Firmanızın aktif bir sözleşmesi bulunamadı; kayıt oluşturulamaz.");
            await PopulateOptionsAsync(model);
            return View(model);
        }

        var serviceIds = model.Lines.Select(l => l.ServiceId).Distinct().ToList();
        var unitsByService = await _db.ServiceCategories
            .Where(s => serviceIds.Contains(s.ServiceId))
            .ToDictionaryAsync(s => s.ServiceId, s => s.Unit);

        var record = new WorkRecord
        {
            // Gerçek belge numarası sadece SUBMITTED'da verilir (A1); taslağın
            // benzersiz-ama-geçici bir DocumentNo'ya ihtiyacı var (kolon NOT NULL + UNIQUE,
            // maxlength 30 — "DRAFT-" + 24 hex karakter = 30).
            DocumentNo = $"DRAFT-{Guid.NewGuid():N}"[..30],
            Status = WorkRecordStatus.DRAFT,
            FirmId = firmId,
            ContractId = contractId.Value,
            PeriodId = model.PeriodId,
            WorkDate = model.WorkDate,
            StartTime = model.StartTime,
            EndTime = model.EndTime,
            SpansMidnight = model.SpansMidnight,
            LocationId = model.LocationId,
            LocationText = model.LocationText,
            WorkDescription = model.WorkDescription,
            RequestedByUserId = model.RequestedByUserId,
            WitnessedByUserId = model.WitnessedByUserId,
            OperatorName = model.OperatorName,
            EquipmentId = model.EquipmentId,
            LicensePlate = model.LicensePlate,
            PersonnelCount = model.PersonnelCount,
            ExternalReceiptNo = model.ExternalReceiptNo,
            ExternalReceiptDate = model.ExternalReceiptDate,
            EnteredByUserId = _currentUser.UserId
        };

        foreach (var line in model.Lines.Where(l => l.ServiceId > 0))
        {
            record.WorkRecordLines.Add(new WorkRecordLine
            {
                LineNo = record.WorkRecordLines.Count + 1,
                ServiceId = line.ServiceId,
                VariantId = line.VariantId,
                RawQuantity = line.Quantity ?? 0,
                BillableQuantity = 0,
                Unit = unitsByService.GetValueOrDefault(line.ServiceId, ServiceUnit.HOUR),
                UnitPriceSnapshot = 0,
                LineAmount = 0,
                Currency = "TRY"
            });
        }

        try
        {
            _db.WorkRecords.Add(record);
            await _db.SaveChangesAsync();
        }
        catch (PeriodGuardException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateOptionsAsync(model);
            return View(model);
        }

        TempData[TempDataKeys.SuccessMessage] = "Taslak kayıt oluşturuldu.";
        return RedirectToAction(nameof(Details), new { id = record.WorkRecordId });
    }

    // Satır ekleme: htmx ile, sayfa yenilenmeden yeni boş bir satır satırı döner.
    [HttpGet]
    [Authorize(Policy = PolicyNames.FirmUser)]
    public async Task<IActionResult> AddLine(int index)
    {
        var model = new WorkRecordLineFormViewModel { Index = index };
        await PopulateLineOptionsAsync(model);
        return PartialView("_LineRow", model);
    }

    // Hizmet seçimi değiştiğinde o satırın varyant listesini htmx ile tazeler.
    [HttpGet]
    [Authorize(Policy = PolicyNames.FirmUser)]
    public async Task<IActionResult> VariantOptions(int? serviceId, int index)
    {
        var variants = serviceId is null
            ? new List<ServiceVariant>()
            : await _db.ServiceVariants.Where(v => v.ServiceId == serviceId && v.IsActive).OrderBy(v => v.Name).ToListAsync();

        var items = variants.Select(v => new SelectListItem(v.Name, v.VariantId.ToString(), false)).ToList();
        ViewData["LineIndex"] = index;
        return PartialView("_VariantOptions", items);
    }

    // ---------------------------------------------------------------
    // B2: canlı fiyat önizleme. Fiyat bulunamazsa/hesap hatası olursa
    // hata metni Türkçe olarak gösterilir; sayfa hiç patlamaz.
    // ---------------------------------------------------------------
    [HttpPost]
    [Authorize(Policy = PolicyNames.FirmUser)]
    public async Task<IActionResult> PreviewLine(LinePreviewRequest request)
    {
        if (request.WorkDate is null || request.ServiceId is null || request.ServiceId == 0)
        {
            return PartialView("_LinePreview", LinePreviewViewModel.Empty);
        }

        var firmId = _currentUser.FirmId!.Value;

        ContractLine contractLine;
        try
        {
            contractLine = await _resolver.ResolveAsync(firmId, request.ServiceId.Value, request.VariantId, request.WorkDate.Value);
        }
        catch (PricingException ex)
        {
            return PartialView("_LinePreview", LinePreviewViewModel.Failed(ex.Message));
        }

        var pricingRequest = new PricingRequest
        {
            ContractLine = contractLine,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            SpansMidnight = request.SpansMidnight,
            Quantity = request.Quantity
        };

        try
        {
            var result = PricingCalculator.Calculate(pricingRequest);
            return PartialView("_LinePreview", LinePreviewViewModel.Succeeded(result));
        }
        catch (PricingException ex)
        {
            return PartialView("_LinePreview", LinePreviewViewModel.Failed(ex.Message));
        }
    }

    // ---------------------------------------------------------------
    // B3 + B4: gönderim. Tek transaction, mükerrer uyarısı, snapshot yazımı,
    // belge numarası. Durum değişimi WorkRecordStateMachine üzerinden yapılır
    // (Adım 7: controller'da doğrudan Status ataması YOK) ve gönderimin hemen
    // ardından ilk onay adımı açılıp kayıt PENDING'e geçer.
    // ---------------------------------------------------------------
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PolicyNames.FirmUser)]
    public async Task<IActionResult> Submit(int id, bool confirmDuplicate = false)
    {
        var record = await _db.WorkRecords
            .Include(w => w.WorkRecordLines)
            .FirstOrDefaultAsync(w => w.WorkRecordId == id);
        if (record is null)
        {
            return NotFound();
        }

        if (record.Status != WorkRecordStatus.DRAFT)
        {
            TempData[TempDataKeys.ErrorMessage] = "Sadece taslak kayıtlar gönderilebilir; bu kayıt zaten gönderilmiş.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var missingFields = FindMissingRequiredFields(record);
        if (missingFields.Count > 0)
        {
            TempData[TempDataKeys.ErrorMessage] = "Gönderim için eksik alanlar var: " + string.Join(", ", missingFields);
            return RedirectToAction(nameof(Details), new { id });
        }

        // B3 adım 2: dönem açık mı. PeriodGuardInterceptor SaveChanges seviyesinde
        // zaten bunu garanti eder (hangi yoldan gelirse gelsin); burada AYRICA
        // kontrol ediyoruz ki kullanıcı ham bir exception yerine düzgün bir
        // Türkçe mesajla Details ekranına dönsün.
        var period = await _db.Periods.AsNoTracking().SingleAsync(p => p.PeriodId == record.PeriodId);
        if (period.Status == PeriodStatus.CLOSED)
        {
            TempData[TempDataKeys.ErrorMessage] =
                $"{PeriodStatusDisplay.GetMonthName(period.Month)} {period.Year} dönemi kapalıdır, gönderilemez.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // B4: mükerrer kayıt uyarısı — engellemez, kullanıcı onaylayabilir.
        // Yerini yeni versiyona bırakmış kayıtlar (IsSuperseded) hariç tutulur:
        // bir revizyon, tanımı gereği selefiyle aynı tarih/plaka/saati taşır ve
        // her revizyonda gereksiz mükerrer uyarısı çıkardı.
        if (!confirmDuplicate)
        {
            var isDuplicate = await _db.WorkRecords
                .Where(w => w.WorkRecordId != record.WorkRecordId
                    && !w.IsSuperseded
                    && w.FirmId == record.FirmId
                    && w.WorkDate == record.WorkDate
                    && w.LicensePlate == record.LicensePlate
                    && w.StartTime == record.StartTime)
                .AnyAsync();

            if (isDuplicate)
            {
                return View("ConfirmDuplicate", record.WorkRecordId);
            }
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var lineResults = new List<PricingResult>();

            foreach (var line in record.WorkRecordLines.OrderBy(l => l.LineNo))
            {
                ContractLine contractLine;
                try
                {
                    contractLine = await _resolver.ResolveAsync(record.FirmId, line.ServiceId, line.VariantId, record.WorkDate);
                }
                catch (PricingException ex)
                {
                    await transaction.RollbackAsync();
                    TempData[TempDataKeys.ErrorMessage] = ex.Message;
                    return RedirectToAction(nameof(Details), new { id });
                }

                var pricingRequest = new PricingRequest
                {
                    ContractLine = contractLine,
                    ApplicableSurcharges = Array.Empty<ContractLineSurcharge>(),
                    StartTime = record.StartTime,
                    EndTime = record.EndTime,
                    SpansMidnight = record.SpansMidnight,
                    Quantity = contractLine.ServiceCategory.Unit == ServiceUnit.HOUR ? null : line.RawQuantity
                };

                PricingResult result;
                try
                {
                    result = PricingCalculator.Calculate(pricingRequest);
                }
                catch (PricingException ex)
                {
                    await transaction.RollbackAsync();
                    TempData[TempDataKeys.ErrorMessage] = $"{line.LineNo}. satır: {ex.Message}";
                    return RedirectToAction(nameof(Details), new { id });
                }

                line.ContractLineId = contractLine.ContractLineId;
                line.RawQuantity = result.RawQuantity;
                line.BillableQuantity = result.BillableQuantity;
                line.Unit = result.Unit;
                line.UnitPriceSnapshot = result.UnitPriceApplied;
                line.PricingRuleSnapshot = result.PricingRuleSnapshot;
                line.SurchargeAmount = result.SurchargeAmount;
                // Mobilizasyon bedeli satıra YAZILMAZ — kayıt seviyesinde bir kez uygulanır.
                line.LineAmount = result.LineAmount;
                line.Currency = contractLine.Currency;

                lineResults.Add(result);
            }

            RecordTotalResult recordTotal;
            try
            {
                recordTotal = RecordTotalCalculator.Calculate(lineResults);
            }
            catch (PricingException ex)
            {
                await transaction.RollbackAsync();
                TempData[TempDataKeys.ErrorMessage] = ex.Message;
                return RedirectToAction(nameof(Details), new { id });
            }

            record.MobilizationFee = recordTotal.MobilizationFee;
            record.TotalAmount = recordTotal.TotalAmount;
            record.Currency = recordTotal.Currency;

            // Revizyon kayıtları belge numaralarını oluşturulurken alır
            // (WR-2026-00042-R2) ve seriden YENİ numara çekmez; seri sayacı
            // gerçek iş sayısını saymaya devam eder.
            if (record.RevisionOfId is null)
            {
                record.DocumentNo = await _documentNumberService.IssueNumberAsync(DocumentType.WORK_RECORD, record.WorkDate.Year);
            }

            record.SubmittedAt = DateTime.UtcNow;

            // Adım 7: Status'a doğrudan atama YOK — geçiş durum makinesinden.
            // Gönderim ve ilk onay adımının açılması tek transaction: kayıt
            // "gönderildi ama kimseye düşmedi" durumunda kalamaz.
            var actor = await _approvalService.GetActorAsync();
            WorkRecordStateMachine.Submit(record, period, actor);
            await _approvalService.SendToApprovalAsync(record, period);

            if (confirmDuplicate)
            {
                _db.AuditLogs.Add(new AuditLog
                {
                    TableName = "WorkRecords",
                    RecordId = record.WorkRecordId,
                    Action = AuditAction.UPDATE,
                    FieldName = "DuplicateWarningConfirmed",
                    NewValue = "true",
                    Reason = "Mükerrer kayıt uyarısı gösterildi, kullanıcı onayladı ve gönderime devam etti.",
                    UserId = _currentUser.UserId,
                    OccurredAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex) when (ex is PeriodGuardException
                                or WorkRecordStateTransitionException
                                or ApprovalAuthorizationException
                                or ApprovalFlowException)
        {
            // Yarış durumu (kontrolümüzden sonra dönem kapatılmış olabilir), izinsiz
            // geçiş ya da eksik onay akışı tanımı: hepsi kullanıcıya Türkçe mesajla
            // döner, kayıt DRAFT kalır.
            await transaction.RollbackAsync();
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        TempData[TempDataKeys.SuccessMessage] = $"Kayıt {record.DocumentNo} numarasıyla gönderildi ve onaya düştü.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------------------------------------------------------------
    // Adım 7.5: revizyon = YENİ VERSİYON. Eski kayıt değiştirilmez.
    // ---------------------------------------------------------------
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PolicyNames.FirmUser)]
    public async Task<IActionResult> Revise(int id)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var revision = await _revisionService.CreateRevisionAsync(id);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData[TempDataKeys.SuccessMessage] =
                $"{revision.DocumentNo} numaralı yeni versiyon oluşturuldu. Düzeltmeyi yapıp tekrar gönderebilirsiniz.";
            return RedirectToAction(nameof(Details), new { id = revision.WorkRecordId });
        }
        catch (Exception ex) when (ex is PeriodGuardException
                                or WorkRecordStateTransitionException
                                or ApprovalAuthorizationException
                                or ApprovalFlowException)
        {
            await transaction.RollbackAsync();
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    // ---------------------------------------------------------------
    // İptal: DRAFT -> CANCELLED, yine durum makinesi üzerinden.
    // ---------------------------------------------------------------
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PolicyNames.FirmUser)]
    public async Task<IActionResult> Cancel(int id)
    {
        var record = await _db.WorkRecords.FirstOrDefaultAsync(w => w.WorkRecordId == id);
        if (record is null)
        {
            return NotFound();
        }

        var period = await _db.Periods.AsNoTracking().SingleAsync(p => p.PeriodId == record.PeriodId);

        try
        {
            var actor = await _approvalService.GetActorAsync();
            WorkRecordStateMachine.Cancel(record, period, actor);
            await _db.SaveChangesAsync();
            TempData[TempDataKeys.SuccessMessage] = "Kayıt iptal edildi.";
        }
        catch (Exception ex) when (ex is PeriodGuardException
                                or WorkRecordStateTransitionException
                                or ApprovalAuthorizationException)
        {
            _db.ChangeTracker.Clear();
            TempData[TempDataKeys.ErrorMessage] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private static List<string> FindMissingRequiredFields(WorkRecord record)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(record.WorkDescription)) missing.Add("İş Tanımı");
        if (string.IsNullOrWhiteSpace(record.OperatorName)) missing.Add("Operatör Adı");
        if (string.IsNullOrWhiteSpace(record.LicensePlate)) missing.Add("Plaka");
        if (record.StartTime is null) missing.Add("Başlangıç Saati");
        if (record.EndTime is null) missing.Add("Bitiş Saati");
        if (record.PersonnelCount is null or <= 0) missing.Add("Personel Sayısı");
        if (record.LocationId is null && string.IsNullOrWhiteSpace(record.LocationText)) missing.Add("Lokasyon");
        if (record.RequestedByUserId is null) missing.Add("Talep Eden MIP Personeli");
        if (record.WitnessedByUserId is null) missing.Add("Saha Yetkilisi");
        if (string.IsNullOrWhiteSpace(record.ExternalReceiptNo)) missing.Add("Dış Fiş No");
        if (record.ExternalReceiptDate is null) missing.Add("Dış Fiş Tarihi");
        if (record.WorkRecordLines.Count == 0 || record.WorkRecordLines.Any(l => l.ServiceId <= 0)) missing.Add("Hizmet Satırı");

        return missing;
    }

    private static void NormalizeLines(WorkRecordFormViewModel model)
    {
        model.Lines = model.Lines.Where(l => l.ServiceId > 0).ToList();
        if (model.Lines.Count == 0)
        {
            model.Lines.Add(new WorkRecordLineFormViewModel { Index = 0 });
        }
    }

    private async Task PopulateOptionsAsync(WorkRecordFormViewModel model)
    {
        model.PeriodOptions = await BuildPeriodOptionsAsync(model.PeriodId, onlyOpen: true);

        var locations = await _db.Locations.Where(l => l.IsActive).OrderBy(l => l.FullPath ?? l.Name).ToListAsync();
        model.LocationOptions = locations
            .Select(l => new SelectListItem(l.FullPath ?? l.Name, l.LocationId.ToString(), l.LocationId == model.LocationId))
            .ToList();

        // User entity'sinde firma izolasyon filtresi var (kural 7): bir firma kullanıcısı
        // normalde SADECE kendi firmasının kullanıcılarını görebilir. MIP personeli
        // (FirmId = null) hiçbir firmaya ait değildir; "talep eden / saha yetkilisi"
        // listesi için bu filtreyi bilinçli olarak bypass ediyoruz — sızdırılan tek şey
        // MIP personelinin adı, firma verisi değil.
        //
        // u.FirmId == null koşulu bu bypass'ın TEK güvenlik sınırıdır: başka bir firmanın
        // kullanıcısı bu listeye asla giremez. Select ile de tam User entity'si yerine
        // (PasswordHash, Email, Phone, ExternalId... dahil) yalnızca Id + FullName çekilir.
        var mipStaff = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.FirmId == null && u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new { u.UserId, u.FullName })
            .ToListAsync();
        model.RequestedByOptions = mipStaff
            .Select(u => new SelectListItem(u.FullName, u.UserId.ToString(), u.UserId == model.RequestedByUserId))
            .ToList();
        model.WitnessedByOptions = mipStaff
            .Select(u => new SelectListItem(u.FullName, u.UserId.ToString(), u.UserId == model.WitnessedByUserId))
            .ToList();

        var equipment = await _db.Equipment.Where(e => e.IsActive).OrderBy(e => e.LicensePlate).ToListAsync();
        model.EquipmentOptions = equipment
            .Select(e => new SelectListItem(e.LicensePlate ?? e.Description ?? $"#{e.EquipmentId}", e.EquipmentId.ToString(), e.EquipmentId == model.EquipmentId))
            .ToList();

        foreach (var line in model.Lines)
        {
            await PopulateLineOptionsAsync(line);
        }
    }

    private async Task PopulateLineOptionsAsync(WorkRecordLineFormViewModel line)
    {
        var services = await _db.ServiceCategories.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
        line.ServiceOptions = services
            .Select(s => new SelectListItem(s.Name, s.ServiceId.ToString(), s.ServiceId == line.ServiceId))
            .ToList();

        var variants = line.ServiceId > 0
            ? await _db.ServiceVariants.Where(v => v.ServiceId == line.ServiceId && v.IsActive).OrderBy(v => v.Name).ToListAsync()
            : new List<ServiceVariant>();
        line.VariantOptions = variants
            .Select(v => new SelectListItem(v.Name, v.VariantId.ToString(), v.VariantId == line.VariantId))
            .ToList();
    }

    private async Task<List<SelectListItem>> BuildFirmOptionsAsync(int? selectedFirmId)
    {
        var firms = await _db.Firms.Where(f => f.IsActive || f.FirmId == selectedFirmId).OrderBy(f => f.Title).ToListAsync();
        return firms
            .Select(f => new SelectListItem(f.Title, f.FirmId.ToString(), f.FirmId == selectedFirmId))
            .ToList();
    }

    private async Task<List<SelectListItem>> BuildPeriodOptionsAsync(int? selectedPeriodId, bool onlyOpen)
    {
        var query = _db.Periods.AsQueryable();
        if (onlyOpen)
        {
            query = query.Where(p => p.Status != PeriodStatus.CLOSED);
        }

        var periods = await query.OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).ToListAsync();
        return periods
            .Select(p => new SelectListItem(
                $"{PeriodStatusDisplay.GetMonthName(p.Month)} {p.Year}", p.PeriodId.ToString(), p.PeriodId == selectedPeriodId))
            .ToList();
    }
}
