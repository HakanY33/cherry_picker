using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Entities;
using MipRental.Web.Common;
using MipRental.Web.Models.ServiceCategories;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

[Authorize(Policy = PolicyNames.CanManageMaster)]
public class ServiceCategoriesController : Controller
{
    private readonly AppDbContext _db;

    public ServiceCategoriesController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(int page = 1, string? search = null, bool showInactive = false)
    {
        var query = _db.ServiceCategories.AsQueryable();
        if (!showInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s => s.Code.Contains(search) || s.Name.Contains(search));
        }

        var model = await query.OrderBy(s => s.Name).ToPagedListAsync(page, search, showInactive);
        return View(model);
    }

    public IActionResult Create()
    {
        var model = new ServiceCategoryFormViewModel();
        PopulateUnitOptions(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ServiceCategoryFormViewModel model)
    {
        await ValidateCodeUniqueAsync(model);
        if (!ModelState.IsValid)
        {
            PopulateUnitOptions(model);
            return View(model);
        }

        _db.ServiceCategories.Add(new ServiceCategory
        {
            Code = model.Code,
            Name = model.Name,
            Unit = model.Unit,
            RequiresTimeTracking = model.RequiresTimeTracking,
            RequiresVehicle = model.RequiresVehicle,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Kayıt oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var service = await _db.ServiceCategories.FindAsync(id);
        if (service is null)
        {
            return NotFound();
        }

        var model = new ServiceCategoryFormViewModel
        {
            ServiceId = service.ServiceId,
            Code = service.Code,
            Name = service.Name,
            Unit = service.Unit,
            RequiresTimeTracking = service.RequiresTimeTracking,
            RequiresVehicle = service.RequiresVehicle,
            IsActive = service.IsActive
        };
        PopulateUnitOptions(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ServiceCategoryFormViewModel model)
    {
        if (id != model.ServiceId)
        {
            return NotFound();
        }

        var service = await _db.ServiceCategories.FindAsync(id);
        if (service is null)
        {
            return NotFound();
        }

        await ValidateCodeUniqueAsync(model);
        if (!ModelState.IsValid)
        {
            PopulateUnitOptions(model);
            return View(model);
        }

        service.Code = model.Code;
        service.Name = model.Name;
        service.Unit = model.Unit;
        service.RequiresTimeTracking = model.RequiresTimeTracking;
        service.RequiresVehicle = model.RequiresVehicle;
        service.IsActive = model.IsActive;

        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Kayıt güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateCodeUniqueAsync(ServiceCategoryFormViewModel model)
    {
        var codeTaken = await _db.ServiceCategories.AnyAsync(s => s.Code == model.Code && s.ServiceId != model.ServiceId);
        if (codeTaken)
        {
            ModelState.AddModelError(nameof(model.Code), "Bu hizmet kodu zaten kullanılıyor.");
        }
    }

    private static void PopulateUnitOptions(ServiceCategoryFormViewModel model) =>
        model.UnitOptions = ServiceUnitDisplay.ToSelectList(model.Unit);
}
