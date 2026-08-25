using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Web.Common;
using MipRental.Web.Models.Equipment;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

[Authorize(Policy = PolicyNames.CanManageMaster)]
public class EquipmentController : Controller
{
    private readonly AppDbContext _db;

    public EquipmentController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(int page = 1, string? search = null, bool showInactive = false)
    {
        var query = _db.Equipment.Include(e => e.Firm).Include(e => e.ServiceVariant).AsQueryable();
        if (!showInactive)
        {
            query = query.Where(e => e.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(e =>
                (e.LicensePlate != null && e.LicensePlate.Contains(search)) ||
                (e.Description != null && e.Description.Contains(search)) ||
                e.Firm.Title.Contains(search));
        }

        var model = await query.OrderBy(e => e.Firm.Title).ThenBy(e => e.LicensePlate).ToPagedListAsync(page, search, showInactive);
        return View(model);
    }

    public async Task<IActionResult> Create()
    {
        var model = new MipRental.Web.Models.Equipment.EquipmentFormViewModel();
        await PopulateOptionsAsync(model, currentFirmId: null);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MipRental.Web.Models.Equipment.EquipmentFormViewModel model)
    {
        await ValidateFirmIsActiveAsync(model);
        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model, currentFirmId: null);
            return View(model);
        }

        _db.Equipment.Add(new Domain.Entities.Equipment
        {
            FirmId = model.FirmId,
            VariantId = model.VariantId,
            LicensePlate = model.LicensePlate,
            Description = model.Description,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Kayıt oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var equipment = await _db.Equipment.FindAsync(id);
        if (equipment is null)
        {
            return NotFound();
        }

        var model = new MipRental.Web.Models.Equipment.EquipmentFormViewModel
        {
            EquipmentId = equipment.EquipmentId,
            FirmId = equipment.FirmId,
            VariantId = equipment.VariantId,
            LicensePlate = equipment.LicensePlate,
            Description = equipment.Description,
            IsActive = equipment.IsActive
        };
        await PopulateOptionsAsync(model, currentFirmId: equipment.FirmId);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, MipRental.Web.Models.Equipment.EquipmentFormViewModel model)
    {
        if (id != model.EquipmentId)
        {
            return NotFound();
        }

        var equipment = await _db.Equipment.FindAsync(id);
        if (equipment is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model, currentFirmId: equipment.FirmId);
            return View(model);
        }

        equipment.FirmId = model.FirmId;
        equipment.VariantId = model.VariantId;
        equipment.LicensePlate = model.LicensePlate;
        equipment.Description = model.Description;
        equipment.IsActive = model.IsActive;

        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Kayıt güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    // Kural: pasif firma yeni Equipment alamaz. Sadece oluşturma anında uygulanır;
    // mevcut ekipmanın firması sonradan pasife düşerse ekipman kaydı bundan etkilenmez.
    private async Task ValidateFirmIsActiveAsync(MipRental.Web.Models.Equipment.EquipmentFormViewModel model)
    {
        var firm = await _db.Firms.FindAsync(model.FirmId);
        if (firm is null)
        {
            ModelState.AddModelError(nameof(model.FirmId), "Seçilen firma bulunamadı.");
        }
        else if (!firm.IsActive)
        {
            ModelState.AddModelError(nameof(model.FirmId), "Pasif bir firmaya yeni ekipman eklenemez.");
        }
    }

    private async Task PopulateOptionsAsync(MipRental.Web.Models.Equipment.EquipmentFormViewModel model, int? currentFirmId)
    {
        var firms = await _db.Firms
            .Where(f => f.IsActive || f.FirmId == currentFirmId)
            .OrderBy(f => f.Title)
            .ToListAsync();
        model.FirmOptions = firms
            .Select(f => new SelectListItem(f.IsActive ? f.Title : $"{f.Title} (Pasif)", f.FirmId.ToString(), f.FirmId == model.FirmId))
            .ToList();

        var variants = await _db.ServiceVariants.Include(v => v.ServiceCategory)
            .Where(v => v.IsActive)
            .OrderBy(v => v.ServiceCategory.Name).ThenBy(v => v.Name)
            .ToListAsync();
        model.VariantOptions = variants
            .Select(v => new SelectListItem($"{v.ServiceCategory.Name} — {v.Name}", v.VariantId.ToString(), v.VariantId == model.VariantId))
            .ToList();
    }
}
