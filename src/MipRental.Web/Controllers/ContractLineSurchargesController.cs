using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Entities;
using MipRental.Web.Common;
using MipRental.Web.Models.Contracts;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

[Authorize(Policy = PolicyNames.CanManageContract)]
public class ContractLineSurchargesController : Controller
{
    private readonly AppDbContext _db;

    public ContractLineSurchargesController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Create(int contractLineId)
    {
        var line = await _db.ContractLines
            .Include(l => l.ServiceCategory)
            .Include(l => l.ServiceVariant)
            .FirstOrDefaultAsync(l => l.ContractLineId == contractLineId);
        if (line is null)
        {
            return NotFound();
        }

        var model = new ContractLineSurchargeFormViewModel
        {
            ContractLineId = contractLineId,
            ContractId = line.ContractId,
            ContractLineDescription = DescribeLine(line)
        };
        model.SurchargeTypeOptions = SurchargeTypeDisplay.ToSelectList(model.SurchargeType);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ContractLineSurchargeFormViewModel model)
    {
        var line = await _db.ContractLines
            .Include(l => l.ServiceCategory)
            .Include(l => l.ServiceVariant)
            .FirstOrDefaultAsync(l => l.ContractLineId == model.ContractLineId);
        if (line is null)
        {
            return NotFound();
        }

        ValidateAmount(model);
        if (!ModelState.IsValid)
        {
            model.ContractId = line.ContractId;
            model.ContractLineDescription = DescribeLine(line);
            model.SurchargeTypeOptions = SurchargeTypeDisplay.ToSelectList(model.SurchargeType);
            return View(model);
        }

        _db.ContractLineSurcharges.Add(new ContractLineSurcharge
        {
            ContractLineId = model.ContractLineId,
            SurchargeType = model.SurchargeType,
            Multiplier = model.Multiplier,
            FixedAmount = model.FixedAmount,
            AppliesFromHour = model.AppliesFromHour,
            AppliesToHour = model.AppliesToHour,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Ek ücret eklendi.";
        return RedirectToAction("Details", "Contracts", new { id = line.ContractId });
    }

    public async Task<IActionResult> Edit(int id)
    {
        var surcharge = await _db.ContractLineSurcharges
            .Include(s => s.ContractLine).ThenInclude(l => l.ServiceCategory)
            .Include(s => s.ContractLine).ThenInclude(l => l.ServiceVariant)
            .FirstOrDefaultAsync(s => s.SurchargeId == id);
        if (surcharge is null)
        {
            return NotFound();
        }

        var model = new ContractLineSurchargeFormViewModel
        {
            SurchargeId = surcharge.SurchargeId,
            ContractLineId = surcharge.ContractLineId,
            ContractId = surcharge.ContractLine.ContractId,
            SurchargeType = surcharge.SurchargeType,
            Multiplier = surcharge.Multiplier,
            FixedAmount = surcharge.FixedAmount,
            AppliesFromHour = surcharge.AppliesFromHour,
            AppliesToHour = surcharge.AppliesToHour,
            IsActive = surcharge.IsActive,
            ContractLineDescription = DescribeLine(surcharge.ContractLine)
        };
        model.SurchargeTypeOptions = SurchargeTypeDisplay.ToSelectList(model.SurchargeType);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ContractLineSurchargeFormViewModel model)
    {
        if (id != model.SurchargeId)
        {
            return NotFound();
        }

        var surcharge = await _db.ContractLineSurcharges
            .Include(s => s.ContractLine).ThenInclude(l => l.ServiceCategory)
            .Include(s => s.ContractLine).ThenInclude(l => l.ServiceVariant)
            .FirstOrDefaultAsync(s => s.SurchargeId == id);
        if (surcharge is null)
        {
            return NotFound();
        }

        ValidateAmount(model);
        if (!ModelState.IsValid)
        {
            model.ContractId = surcharge.ContractLine.ContractId;
            model.ContractLineDescription = DescribeLine(surcharge.ContractLine);
            model.SurchargeTypeOptions = SurchargeTypeDisplay.ToSelectList(model.SurchargeType);
            return View(model);
        }

        surcharge.SurchargeType = model.SurchargeType;
        surcharge.Multiplier = model.Multiplier;
        surcharge.FixedAmount = model.FixedAmount;
        surcharge.AppliesFromHour = model.AppliesFromHour;
        surcharge.AppliesToHour = model.AppliesToHour;
        surcharge.IsActive = model.IsActive;

        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Ek ücret güncellendi.";
        return RedirectToAction("Details", "Contracts", new { id = surcharge.ContractLine.ContractId });
    }

    private void ValidateAmount(ContractLineSurchargeFormViewModel model)
    {
        if (model.Multiplier is null && model.FixedAmount is null)
        {
            ModelState.AddModelError(string.Empty, "Çarpan veya sabit tutardan en az biri girilmelidir.");
        }
    }

    private static string DescribeLine(ContractLine line) =>
        line.ServiceVariant is null ? line.ServiceCategory.Name : $"{line.ServiceCategory.Name} — {line.ServiceVariant.Name}";
}
