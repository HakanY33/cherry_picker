using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Entities;
using MipRental.Web.Common;
using MipRental.Web.Models.Contracts;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

// CLAUDE.md Adım 4 MUTLAK KURAL: var olan bir fiyat satırının UnitPrice'ı ve kural
// alanları ASLA UPDATE edilmez. Bu yüzden burada klasik bir "Edit" aksiyonu YOK:
// - UpdatePrice: eski satırı ValidTo ile kapatır, yeni satırı açar (versiyonlama).
// - Correct: sadece bu satıra bağlı hiçbir WorkRecordLine yoksa, yazım hatası
//   düzeltmesi olarak yerinde günceller.
[Authorize(Policy = PolicyNames.CanManageContract)]
public class ContractLinesController : Controller
{
    private readonly AppDbContext _db;

    public ContractLinesController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Create(int contractId)
    {
        var contract = await _db.Contracts.FindAsync(contractId);
        if (contract is null)
        {
            return NotFound();
        }

        var model = new ContractLineFormViewModel
        {
            ContractId = contractId,
            Currency = contract.Currency,
            ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        await PopulateOptionsAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ContractLineFormViewModel model)
    {
        var contract = await _db.Contracts.FindAsync(model.ContractId);
        if (contract is null)
        {
            return NotFound();
        }

        ValidateDateRange(model.ValidFrom, model.ValidTo, nameof(model.ValidTo));
        if (ModelState.IsValid)
        {
            await ValidateNoConflictAsync(model.ContractId, model.ServiceId, model.VariantId, model.ValidFrom, model.ValidTo, excludeLineId: null);
        }

        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model);
            return View(model);
        }

        _db.ContractLines.Add(new ContractLine
        {
            ContractId = model.ContractId,
            ServiceId = model.ServiceId,
            VariantId = model.VariantId,
            UnitPrice = model.UnitPrice,
            Currency = model.Currency,
            MinBillableQuantity = model.MinBillableQuantity,
            RoundingRule = model.RoundingRule,
            DayThresholdHours = model.DayThresholdHours,
            DailyPrice = model.DailyPrice,
            MobilizationFee = model.MobilizationFee,
            MaxQuantityPerRecord = model.MaxQuantityPerRecord,
            ValidFrom = model.ValidFrom,
            ValidTo = model.ValidTo,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Fiyat satırı eklendi.";
        return RedirectToAction("Details", "Contracts", new { id = model.ContractId });
    }

    public async Task<IActionResult> Correct(int id)
    {
        var line = await _db.ContractLines.FirstOrDefaultAsync(l => l.ContractLineId == id);
        if (line is null)
        {
            return NotFound();
        }

        if (await HasLinkedWorkRecordsAsync(id))
        {
            TempData[TempDataKeys.ErrorMessage] = "Bu fiyata bağlı çalışma kaydı var, düzeltilemez; fiyat güncellemesi yapın.";
            return RedirectToAction("Details", "Contracts", new { id = line.ContractId });
        }

        var model = new ContractLineFormViewModel
        {
            ContractLineId = line.ContractLineId,
            ContractId = line.ContractId,
            ServiceId = line.ServiceId,
            VariantId = line.VariantId,
            UnitPrice = line.UnitPrice,
            Currency = line.Currency,
            MinBillableQuantity = line.MinBillableQuantity,
            RoundingRule = line.RoundingRule,
            DayThresholdHours = line.DayThresholdHours,
            DailyPrice = line.DailyPrice,
            MobilizationFee = line.MobilizationFee,
            MaxQuantityPerRecord = line.MaxQuantityPerRecord,
            ValidFrom = line.ValidFrom,
            ValidTo = line.ValidTo
        };
        await PopulateOptionsAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Correct(int id, ContractLineFormViewModel model)
    {
        if (id != model.ContractLineId)
        {
            return NotFound();
        }

        var line = await _db.ContractLines.FirstOrDefaultAsync(l => l.ContractLineId == id);
        if (line is null)
        {
            return NotFound();
        }

        if (await HasLinkedWorkRecordsAsync(id))
        {
            TempData[TempDataKeys.ErrorMessage] = "Bu fiyata bağlı çalışma kaydı var, düzeltilemez; fiyat güncellemesi yapın.";
            return RedirectToAction("Details", "Contracts", new { id = line.ContractId });
        }

        ValidateDateRange(model.ValidFrom, model.ValidTo, nameof(model.ValidTo));
        if (ModelState.IsValid)
        {
            await ValidateNoConflictAsync(line.ContractId, model.ServiceId, model.VariantId, model.ValidFrom, model.ValidTo, excludeLineId: id);
        }

        if (!ModelState.IsValid)
        {
            model.ContractId = line.ContractId;
            await PopulateOptionsAsync(model);
            return View(model);
        }

        line.ServiceId = model.ServiceId;
        line.VariantId = model.VariantId;
        line.UnitPrice = model.UnitPrice;
        line.Currency = model.Currency;
        line.MinBillableQuantity = model.MinBillableQuantity;
        line.RoundingRule = model.RoundingRule;
        line.DayThresholdHours = model.DayThresholdHours;
        line.DailyPrice = model.DailyPrice;
        line.MobilizationFee = model.MobilizationFee;
        line.MaxQuantityPerRecord = model.MaxQuantityPerRecord;
        line.ValidFrom = model.ValidFrom;
        line.ValidTo = model.ValidTo;

        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Fiyat satırı düzeltildi.";
        return RedirectToAction("Details", "Contracts", new { id = line.ContractId });
    }

    public async Task<IActionResult> UpdatePrice(int id)
    {
        var line = await _db.ContractLines
            .Include(l => l.ServiceCategory)
            .Include(l => l.ServiceVariant)
            .FirstOrDefaultAsync(l => l.ContractLineId == id);
        if (line is null)
        {
            return NotFound();
        }

        if (line.ValidTo is not null)
        {
            TempData[TempDataKeys.ErrorMessage] = "Fiyat güncellemesi yalnızca süresiz (açık uçlu) satırlar için yapılabilir.";
            return RedirectToAction("Details", "Contracts", new { id = line.ContractId });
        }

        var model = new ContractLineUpdatePriceViewModel
        {
            ContractLineId = line.ContractLineId,
            ContractId = line.ContractId,
            ServiceName = line.ServiceCategory.Name,
            VariantName = line.ServiceVariant?.Name,
            CurrentUnitPrice = line.UnitPrice,
            Currency = line.Currency,
            CurrentValidFrom = line.ValidFrom,
            NewValidFrom = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePrice(int id, ContractLineUpdatePriceViewModel model)
    {
        if (id != model.ContractLineId)
        {
            return NotFound();
        }

        var line = await _db.ContractLines
            .Include(l => l.ServiceCategory)
            .Include(l => l.ServiceVariant)
            .FirstOrDefaultAsync(l => l.ContractLineId == id);
        if (line is null)
        {
            return NotFound();
        }

        if (line.ValidTo is not null)
        {
            TempData[TempDataKeys.ErrorMessage] = "Fiyat güncellemesi yalnızca süresiz (açık uçlu) satırlar için yapılabilir.";
            return RedirectToAction("Details", "Contracts", new { id = line.ContractId });
        }

        if (model.NewValidFrom <= line.ValidFrom)
        {
            ModelState.AddModelError(nameof(model.NewValidFrom), "Yeni geçerlilik başlangıcı, mevcut satırın başlangıç tarihinden sonra olmalıdır.");
        }

        if (ModelState.IsValid)
        {
            await ValidateNoConflictAsync(line.ContractId, line.ServiceId, line.VariantId, model.NewValidFrom, null, excludeLineId: line.ContractLineId);
        }

        if (!ModelState.IsValid)
        {
            model.ServiceName = line.ServiceCategory.Name;
            model.VariantName = line.ServiceVariant?.Name;
            model.CurrentUnitPrice = line.UnitPrice;
            model.Currency = line.Currency;
            model.CurrentValidFrom = line.ValidFrom;
            return View(model);
        }

        // Eski satır kapatılır, yeni satır açılır — tek SaveChangesAsync çağrısı
        // içindeki tüm değişiklikler EF Core tarafından zaten tek transaction'da yürütülür.
        line.ValidTo = model.NewValidFrom.AddDays(-1);

        _db.ContractLines.Add(new ContractLine
        {
            ContractId = line.ContractId,
            ServiceId = line.ServiceId,
            VariantId = line.VariantId,
            UnitPrice = model.NewUnitPrice,
            Currency = line.Currency,
            MinBillableQuantity = line.MinBillableQuantity,
            RoundingRule = line.RoundingRule,
            DayThresholdHours = line.DayThresholdHours,
            DailyPrice = line.DailyPrice,
            MobilizationFee = line.MobilizationFee,
            MaxQuantityPerRecord = line.MaxQuantityPerRecord,
            ValidFrom = model.NewValidFrom,
            ValidTo = null,
            IsActive = true
        });

        await _db.SaveChangesAsync();

        var formattedDate = model.NewValidFrom.ToDateTime(TimeOnly.MinValue).ToString("d MMMM yyyy", new CultureInfo("tr-TR"));
        TempData[TempDataKeys.SuccessMessage] = $"{formattedDate} tarihinden itibaren yeni fiyat geçerli, öncesi değişmedi.";
        return RedirectToAction("Details", "Contracts", new { id = line.ContractId });
    }

    // Hizmet seçimi değiştiğinde varyant listesini htmx ile anlık tazeler.
    [HttpGet]
    public async Task<IActionResult> VariantOptions(int? serviceId)
    {
        var variants = serviceId is null
            ? new List<ServiceVariant>()
            : await _db.ServiceVariants.Where(v => v.ServiceId == serviceId && v.IsActive).OrderBy(v => v.Name).ToListAsync();

        var items = variants.Select(v => new SelectListItem(v.Name, v.VariantId.ToString(), false)).ToList();
        return PartialView("_VariantOptions", items);
    }

    private void ValidateDateRange(DateOnly validFrom, DateOnly? validTo, string fieldName)
    {
        if (validTo is not null && validTo < validFrom)
        {
            ModelState.AddModelError(fieldName, "Geçerlilik bitiş tarihi başlangıçtan önce olamaz.");
        }
    }

    // Aynı sözleşmede aynı (ServiceId, VariantId) için tarih aralıkları çakışamaz.
    // Süresiz (ValidTo = null) bir satır varken yeni satır eklenmesi engellenir;
    // bu durumda kullanıcı "Fiyat Güncelle" akışını kullanmalıdır.
    private async Task ValidateNoConflictAsync(int contractId, int serviceId, int? variantId, DateOnly validFrom, DateOnly? validTo, int? excludeLineId)
    {
        var candidates = await _db.ContractLines
            .Include(l => l.ServiceCategory)
            .Where(l => l.ContractId == contractId && l.ServiceId == serviceId && l.VariantId == variantId
                && (excludeLineId == null || l.ContractLineId != excludeLineId))
            .ToListAsync();

        var openEnded = candidates.FirstOrDefault(l => l.ValidTo is null);
        if (openEnded is not null)
        {
            ModelState.AddModelError(string.Empty,
                $"\"{openEnded.ServiceCategory.Name}\" için süresiz (açık uçlu) bir fiyat satırı zaten var ({openEnded.ValidFrom:dd.MM.yyyy} itibarıyla). Yeni satır eklemek yerine \"Fiyat Güncelle\" akışını kullanın.");
            return;
        }

        var conflicting = candidates.FirstOrDefault(l => DateRangeHelper.Overlaps(validFrom, validTo, l.ValidFrom, l.ValidTo));
        if (conflicting is not null)
        {
            var range = conflicting.ValidTo is null
                ? $"{conflicting.ValidFrom:dd.MM.yyyy} - süresiz"
                : $"{conflicting.ValidFrom:dd.MM.yyyy} - {conflicting.ValidTo:dd.MM.yyyy}";
            ModelState.AddModelError(string.Empty,
                $"Bu tarih aralığı, \"{conflicting.ServiceCategory.Name}\" için mevcut fiyat satırı ({range}) ile çakışıyor.");
        }
    }

    private async Task<bool> HasLinkedWorkRecordsAsync(int contractLineId) =>
        await _db.WorkRecordLines.AnyAsync(w => w.ContractLineId == contractLineId);

    private async Task PopulateOptionsAsync(ContractLineFormViewModel model)
    {
        var services = await _db.ServiceCategories.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
        model.ServiceOptions = services
            .Select(s => new SelectListItem(s.Name, s.ServiceId.ToString(), s.ServiceId == model.ServiceId))
            .ToList();

        var variants = model.ServiceId > 0
            ? await _db.ServiceVariants.Where(v => v.ServiceId == model.ServiceId && v.IsActive).OrderBy(v => v.Name).ToListAsync()
            : new List<ServiceVariant>();
        model.VariantOptions = variants
            .Select(v => new SelectListItem(v.Name, v.VariantId.ToString(), v.VariantId == model.VariantId))
            .ToList();

        model.RoundingRuleOptions = RoundingRuleDisplay.ToSelectList(model.RoundingRule);
    }
}
