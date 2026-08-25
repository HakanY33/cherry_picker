using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Entities;
using MipRental.Web.Common;
using MipRental.Web.Models.ServiceVariants;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

[Authorize(Policy = PolicyNames.CanManageMaster)]
public class ServiceVariantsController : Controller
{
    private readonly AppDbContext _db;

    public ServiceVariantsController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(int page = 1, string? search = null, bool showInactive = false)
    {
        var query = _db.ServiceVariants.Include(v => v.ServiceCategory).AsQueryable();
        if (!showInactive)
        {
            query = query.Where(v => v.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(v => v.Code.Contains(search) || v.Name.Contains(search) || v.ServiceCategory.Name.Contains(search));
        }

        var model = await query.OrderBy(v => v.ServiceCategory.Name).ThenBy(v => v.Name).ToPagedListAsync(page, search, showInactive);
        return View(model);
    }

    public async Task<IActionResult> Create()
    {
        var model = new ServiceVariantFormViewModel();
        await PopulateServiceOptionsAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ServiceVariantFormViewModel model)
    {
        await ValidateCodeUniqueAsync(model);
        if (!ModelState.IsValid)
        {
            await PopulateServiceOptionsAsync(model);
            return View(model);
        }

        _db.ServiceVariants.Add(new ServiceVariant
        {
            ServiceId = model.ServiceId,
            Code = model.Code,
            Name = model.Name,
            Capacity = model.Capacity,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Kayıt oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var variant = await _db.ServiceVariants.Include(v => v.ServiceCategory).FirstOrDefaultAsync(v => v.VariantId == id);
        if (variant is null)
        {
            return NotFound();
        }

        return View(new ServiceVariantFormViewModel
        {
            VariantId = variant.VariantId,
            ServiceId = variant.ServiceId,
            ServiceName = variant.ServiceCategory.Name,
            Code = variant.Code,
            Name = variant.Name,
            Capacity = variant.Capacity,
            IsActive = variant.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ServiceVariantFormViewModel model)
    {
        if (id != model.VariantId)
        {
            return NotFound();
        }

        var variant = await _db.ServiceVariants.Include(v => v.ServiceCategory).FirstOrDefaultAsync(v => v.VariantId == id);
        if (variant is null)
        {
            return NotFound();
        }

        // Kural: "ServiceVariant sadece kendi ServiceId'sine ait olabilir" — bir varyant
        // oluşturulduktan sonra başka bir hizmet tanımına taşınamaz; geçmiş Contract/
        // WorkRecord satırlarındaki ServiceId+VariantId eşleşmesi bu şekilde korunur.
        // Formda alan yok; POST'ta tampered bir değer gelse bile mevcut ServiceId geçerli olur.
        model.ServiceId = variant.ServiceId;
        model.ServiceName = variant.ServiceCategory.Name;

        await ValidateCodeUniqueAsync(model);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        variant.Code = model.Code;
        variant.Name = model.Name;
        variant.Capacity = model.Capacity;
        variant.IsActive = model.IsActive;

        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Kayıt güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateCodeUniqueAsync(ServiceVariantFormViewModel model)
    {
        var codeTaken = await _db.ServiceVariants.AnyAsync(v =>
            v.ServiceId == model.ServiceId && v.Code == model.Code && v.VariantId != model.VariantId);
        if (codeTaken)
        {
            ModelState.AddModelError(nameof(model.Code), "Bu hizmet tanımı için bu varyant kodu zaten kullanılıyor.");
        }
    }

    private async Task PopulateServiceOptionsAsync(ServiceVariantFormViewModel model)
    {
        var services = await _db.ServiceCategories.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
        model.ServiceOptions = services
            .Select(s => new SelectListItem(s.Name, s.ServiceId.ToString(), s.ServiceId == model.ServiceId))
            .ToList();
    }
}
