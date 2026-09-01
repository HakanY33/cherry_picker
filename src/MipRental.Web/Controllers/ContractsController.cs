using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Web.Common;
using MipRental.Web.Models.Contracts;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

[Authorize(Policy = PolicyNames.CanManageContract)]
public class ContractsController : Controller
{
    private readonly AppDbContext _db;

    public ContractsController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(int page = 1, int? firmId = null, ContractStatus? status = null)
    {
        var query = _db.Contracts.Include(c => c.Firm).AsQueryable();
        if (firmId is not null)
        {
            query = query.Where(c => c.FirmId == firmId);
        }

        if (status is not null)
        {
            query = query.Where(c => c.Status == status);
        }

        page = page < 1 ? 1 : page;
        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * PagingHelper.PageSize)
            .Take(PagingHelper.PageSize)
            .ToListAsync();

        var model = new ContractIndexViewModel
        {
            Items = items,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling(totalCount / (double)PagingHelper.PageSize),
            FirmId = firmId,
            Status = status,
            FirmOptions = await BuildFirmOptionsAsync(firmId)
        };
        return View(model);
    }

    public async Task<IActionResult> Create()
    {
        var model = new ContractFormViewModel
        {
            Currency = "TRY",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1)
        };
        model.FirmOptions = await BuildFirmOptionsAsync(null);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ContractFormViewModel model)
    {
        await ValidateAsync(model, contractId: null);
        if (!ModelState.IsValid)
        {
            model.FirmOptions = await BuildFirmOptionsAsync(model.FirmId);
            return View(model);
        }

        // Yeni sözleşme her zaman DRAFT başlar; ACTIVE'e geçiş ayrı bir adımdır
        // ve en az bir ContractLine gerektirir (bkz. Activate).
        _db.Contracts.Add(new Contract
        {
            FirmId = model.FirmId,
            ContractNo = model.ContractNo,
            Title = model.Title,
            StartDate = model.StartDate,
            EndDate = model.EndDate,
            Currency = model.Currency,
            Status = ContractStatus.DRAFT,
            Notes = model.Notes
        });
        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Sözleşme oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var contract = await _db.Contracts.FindAsync(id);
        if (contract is null)
        {
            return NotFound();
        }

        var model = ToFormViewModel(contract);
        model.FirmOptions = await BuildFirmOptionsAsync(model.FirmId);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ContractFormViewModel model)
    {
        if (id != model.ContractId)
        {
            return NotFound();
        }

        var contract = await _db.Contracts.FindAsync(id);
        if (contract is null)
        {
            return NotFound();
        }

        await ValidateAsync(model, contractId: id);
        if (!ModelState.IsValid)
        {
            model.Status = contract.Status;
            model.FirmOptions = await BuildFirmOptionsAsync(model.FirmId);
            return View(model);
        }

        contract.FirmId = model.FirmId;
        contract.ContractNo = model.ContractNo;
        contract.Title = model.Title;
        contract.StartDate = model.StartDate;
        contract.EndDate = model.EndDate;
        contract.Currency = model.Currency;
        contract.Notes = model.Notes;

        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Sözleşme güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id, DateOnly? previewDate)
    {
        var contract = await _db.Contracts.Include(c => c.Firm).FirstOrDefaultAsync(c => c.ContractId == id);
        if (contract is null)
        {
            return NotFound();
        }

        var lines = await _db.ContractLines
            .Include(l => l.ServiceCategory)
            .Include(l => l.ServiceVariant)
            .Include(l => l.ContractLineSurcharges)
            .Where(l => l.ContractId == id)
            .OrderBy(l => l.ServiceCategory.Name).ThenBy(l => l.ValidFrom)
            .ToListAsync();

        var lineIds = lines.Select(l => l.ContractLineId).ToList();
        var linkedIds = await _db.WorkRecordLines
            .Where(w => w.ContractLineId != null && lineIds.Contains(w.ContractLineId!.Value))
            .Select(w => w.ContractLineId!.Value)
            .Distinct()
            .ToListAsync();

        var date = previewDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var model = new ContractDetailsViewModel
        {
            Contract = contract,
            Lines = lines,
            LinesWithWorkRecords = linkedIds.ToHashSet(),
            PriceOnDatePreview = new PriceOnDateViewModel
            {
                ContractId = id,
                Date = date,
                Lines = FilterLinesValidOn(lines, date)
            }
        };
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> PriceOnDate(int id, DateOnly date)
    {
        var lines = await _db.ContractLines
            .Include(l => l.ServiceCategory)
            .Include(l => l.ServiceVariant)
            .Where(l => l.ContractId == id)
            .OrderBy(l => l.ServiceCategory.Name)
            .ToListAsync();

        var model = new PriceOnDateViewModel
        {
            ContractId = id,
            Date = date,
            Lines = FilterLinesValidOn(lines, date)
        };
        return PartialView("_PriceOnDate", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(int id)
    {
        var contract = await _db.Contracts.FindAsync(id);
        if (contract is null)
        {
            return NotFound();
        }

        // DRAFT -> ACTIVE normal akış.
        // EXPIRED -> ACTIVE yalnızca bitiş tarihi HENÜZ GEÇMEMİŞSE: bu durum artık
        // üretilemiyor (bkz. Expire), ama eskiden üretilmiş kayıtların düzeltilebilmesi
        // gerekiyor. Süresi gerçekten dolmuş sözleşme geri açılamaz.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var isDraft = contract.Status == ContractStatus.DRAFT;
        var isWronglyExpired = contract.Status == ContractStatus.EXPIRED && contract.EndDate >= today;

        if (!isDraft && !isWronglyExpired)
        {
            TempData[TempDataKeys.ErrorMessage] = contract.Status == ContractStatus.EXPIRED
                ? "Bitiş tarihi geçmiş sözleşme yeniden aktifleştirilemez."
                : "Sadece taslak durumundaki sözleşmeler aktifleştirilebilir.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var hasLine = await _db.ContractLines.AnyAsync(l => l.ContractId == id);
        if (!hasLine)
        {
            TempData[TempDataKeys.ErrorMessage] = "Sözleşme en az bir fiyat satırı olmadan aktifleştirilemez.";
            return RedirectToAction(nameof(Details), new { id });
        }

        contract.Status = ContractStatus.ACTIVE;
        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Sözleşme aktifleştirildi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// "Süresi doldu" SADECE bitiş tarihi GEÇMİŞ sözleşmeler için geçerlidir.
    ///
    /// Eskiden bu kontrol yoktu: bitiş tarihi gelecekte olan bir sözleşme de
    /// EXPIRED işaretlenebiliyordu ve ekranda "Süresi Doldu" rozetiyle görünüyordu.
    /// Sözleşmeyi vaktinden önce sonlandırmanın doğru yolu FESHET'tir; süre dolması
    /// bir KARAR değil, takvimin sonucudur.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Expire(int id)
    {
        var contract = await _db.Contracts.FindAsync(id);
        if (contract is null)
        {
            return NotFound();
        }

        if (contract.Status != ContractStatus.ACTIVE)
        {
            TempData[TempDataKeys.ErrorMessage] = "Sadece aktif sözleşmeler süresi doldu olarak işaretlenebilir.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (contract.EndDate >= today)
        {
            TempData[TempDataKeys.ErrorMessage] =
                $"Sözleşmenin bitiş tarihi ({contract.EndDate:dd.MM.yyyy}) henüz geçmedi, süresi doldu olarak işaretlenemez. " +
                "Sözleşmeyi erken sonlandırmak için \"Feshet\" kullanın.";
            return RedirectToAction(nameof(Details), new { id });
        }

        contract.Status = ContractStatus.EXPIRED;
        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Sözleşme süresi doldu olarak işaretlendi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Terminate(int id) => ChangeStatusAsync(
        id,
        requiredCurrent: ContractStatus.ACTIVE,
        target: ContractStatus.TERMINATED,
        errorMessage: "Sadece aktif sözleşmeler feshedilebilir.",
        successMessage: "Sözleşme feshedildi.");

    private static List<ContractLine> FilterLinesValidOn(IEnumerable<ContractLine> lines, DateOnly date) =>
        lines.Where(l => l.ValidFrom <= date && (l.ValidTo is null || l.ValidTo >= date)).ToList();

    private async Task<IActionResult> ChangeStatusAsync(int id, ContractStatus requiredCurrent, ContractStatus target, string errorMessage, string successMessage)
    {
        var contract = await _db.Contracts.FindAsync(id);
        if (contract is null)
        {
            return NotFound();
        }

        if (contract.Status != requiredCurrent)
        {
            TempData[TempDataKeys.ErrorMessage] = errorMessage;
            return RedirectToAction(nameof(Details), new { id });
        }

        contract.Status = target;
        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = successMessage;
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task ValidateAsync(ContractFormViewModel model, int? contractId)
    {
        if (model.EndDate < model.StartDate)
        {
            ModelState.AddModelError(nameof(model.EndDate), "Bitiş tarihi başlangıç tarihinden önce olamaz.");
        }

        var noTaken = await _db.Contracts.AnyAsync(c =>
            c.FirmId == model.FirmId && c.ContractNo == model.ContractNo && c.ContractId != (contractId ?? -1));
        if (noTaken)
        {
            ModelState.AddModelError(nameof(model.ContractNo), "Bu firma için bu sözleşme numarası zaten kullanılıyor.");
        }
    }

    private static ContractFormViewModel ToFormViewModel(Contract contract) => new()
    {
        ContractId = contract.ContractId,
        FirmId = contract.FirmId,
        ContractNo = contract.ContractNo,
        Title = contract.Title,
        StartDate = contract.StartDate,
        EndDate = contract.EndDate,
        Currency = contract.Currency,
        Notes = contract.Notes,
        Status = contract.Status
    };

    private async Task<List<SelectListItem>> BuildFirmOptionsAsync(int? selectedFirmId)
    {
        var firms = await _db.Firms
            .Where(f => f.IsActive || f.FirmId == selectedFirmId)
            .OrderBy(f => f.Title)
            .ToListAsync();

        return firms
            .Select(f => new SelectListItem(f.IsActive ? f.Title : $"{f.Title} (Pasif)", f.FirmId.ToString(), f.FirmId == selectedFirmId))
            .ToList();
    }
}
