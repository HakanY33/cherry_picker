using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Entities;
using MipRental.Web.Common;
using MipRental.Web.Models.Departments;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

[Authorize(Policy = PolicyNames.CanManageMaster)]
public class DepartmentsController : Controller
{
    private readonly AppDbContext _db;

    public DepartmentsController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(int page = 1, string? search = null, bool showInactive = false)
    {
        var query = _db.Departments.Include(d => d.ParentDepartment).AsQueryable();
        if (!showInactive)
        {
            query = query.Where(d => d.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(d => d.Code.Contains(search) || d.Name.Contains(search));
        }

        var model = await query.OrderBy(d => d.Name).ToPagedListAsync(page, search, showInactive);
        return View(model);
    }

    public async Task<IActionResult> Create()
    {
        var model = new DepartmentFormViewModel();
        await PopulateParentOptionsAsync(model, excludeId: null);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DepartmentFormViewModel model)
    {
        await ValidateAsync(model, isCreate: true);
        if (!ModelState.IsValid)
        {
            await PopulateParentOptionsAsync(model, excludeId: null);
            return View(model);
        }

        _db.Departments.Add(new Department
        {
            Code = model.Code,
            Name = model.Name,
            ParentDepartmentId = model.ParentDepartmentId,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Kayıt oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var department = await _db.Departments.FindAsync(id);
        if (department is null)
        {
            return NotFound();
        }

        var model = new DepartmentFormViewModel
        {
            DepartmentId = department.DepartmentId,
            Code = department.Code,
            Name = department.Name,
            ParentDepartmentId = department.ParentDepartmentId,
            IsActive = department.IsActive
        };
        await PopulateParentOptionsAsync(model, excludeId: id);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, DepartmentFormViewModel model)
    {
        if (id != model.DepartmentId)
        {
            return NotFound();
        }

        var department = await _db.Departments.FindAsync(id);
        if (department is null)
        {
            return NotFound();
        }

        await ValidateAsync(model, isCreate: false);
        if (!ModelState.IsValid)
        {
            await PopulateParentOptionsAsync(model, excludeId: id);
            return View(model);
        }

        department.Code = model.Code;
        department.Name = model.Name;
        department.ParentDepartmentId = model.ParentDepartmentId;
        department.IsActive = model.IsActive;

        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Kayıt güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateAsync(DepartmentFormViewModel model, bool isCreate)
    {
        var codeTaken = await _db.Departments.AnyAsync(d => d.Code == model.Code && d.DepartmentId != model.DepartmentId);
        if (codeTaken)
        {
            ModelState.AddModelError(nameof(model.Code), "Bu departman kodu zaten kullanılıyor.");
        }

        if (!isCreate && model.ParentDepartmentId is not null)
        {
            var allDepartments = await _db.Departments.AsNoTracking().ToListAsync();
            if (TreeHelper.WouldCreateCycle(allDepartments, model.DepartmentId, model.ParentDepartmentId, d => d.DepartmentId, d => d.ParentDepartmentId))
            {
                ModelState.AddModelError(nameof(model.ParentDepartmentId), "Bir departman kendisinin veya alt departmanının altına taşınamaz.");
            }
        }
    }

    private async Task PopulateParentOptionsAsync(DepartmentFormViewModel model, int? excludeId)
    {
        var departments = await _db.Departments
            .Where(d => excludeId == null || d.DepartmentId != excludeId)
            .OrderBy(d => d.Name)
            .ToListAsync();

        model.ParentOptions = departments
            .Select(d => new SelectListItem(d.Name, d.DepartmentId.ToString(), d.DepartmentId == model.ParentDepartmentId))
            .ToList();
    }
}
