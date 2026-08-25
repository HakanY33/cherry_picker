using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Entities;
using MipRental.Web.Common;
using MipRental.Web.Models.Firms;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

[Authorize(Policy = PolicyNames.CanManageMaster)]
public class FirmsController : Controller
{
    private readonly AppDbContext _db;

    public FirmsController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(int page = 1, string? search = null, bool showInactive = false)
    {
        var query = _db.Firms.AsQueryable();
        if (!showInactive)
        {
            query = query.Where(f => f.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(f => f.Code.Contains(search) || f.Title.Contains(search));
        }

        var model = await query.OrderBy(f => f.Title).ToPagedListAsync(page, search, showInactive);
        return View(model);
    }

    public IActionResult Create() => View(new FirmFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(FirmFormViewModel model)
    {
        await ValidateCodeUniqueAsync(model);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        _db.Firms.Add(new Firm
        {
            Code = model.Code,
            Title = model.Title,
            TaxNumber = model.TaxNumber,
            TaxOffice = model.TaxOffice,
            Iban = model.Iban,
            Phone = model.Phone,
            Email = model.Email,
            Address = model.Address,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Kayıt oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var firm = await _db.Firms.FindAsync(id);
        if (firm is null)
        {
            return NotFound();
        }

        return View(ToFormViewModel(firm));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, FirmFormViewModel model)
    {
        if (id != model.FirmId)
        {
            return NotFound();
        }

        var firm = await _db.Firms.FindAsync(id);
        if (firm is null)
        {
            return NotFound();
        }

        await ValidateCodeUniqueAsync(model);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        firm.Code = model.Code;
        firm.Title = model.Title;
        firm.TaxNumber = model.TaxNumber;
        firm.TaxOffice = model.TaxOffice;
        firm.Iban = model.Iban;
        firm.Phone = model.Phone;
        firm.Email = model.Email;
        firm.Address = model.Address;
        firm.IsActive = model.IsActive;

        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Kayıt güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateCodeUniqueAsync(FirmFormViewModel model)
    {
        var codeTaken = await _db.Firms.AnyAsync(f => f.Code == model.Code && f.FirmId != model.FirmId);
        if (codeTaken)
        {
            ModelState.AddModelError(nameof(model.Code), "Bu firma kodu zaten kullanılıyor.");
        }
    }

    private static FirmFormViewModel ToFormViewModel(Firm firm) => new()
    {
        FirmId = firm.FirmId,
        Code = firm.Code,
        Title = firm.Title,
        TaxNumber = firm.TaxNumber,
        TaxOffice = firm.TaxOffice,
        Iban = firm.Iban,
        Phone = firm.Phone,
        Email = firm.Email,
        Address = firm.Address,
        IsActive = firm.IsActive
    };
}
