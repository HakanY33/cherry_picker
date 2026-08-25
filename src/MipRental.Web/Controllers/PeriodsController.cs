using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Services;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Web.Common;
using MipRental.Web.Models.Periods;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

[Authorize(Policy = PolicyNames.CanClosePeriod)]
public class PeriodsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly PeriodLockService _lockService;

    public PeriodsController(AppDbContext db, ICurrentUser currentUser, PeriodLockService lockService)
    {
        _db = db;
        _currentUser = currentUser;
        _lockService = lockService;
    }

    public async Task<IActionResult> Index(int? year)
    {
        var query = _db.Periods.Include(p => p.ClosedByUser).Include(p => p.ReopenedByUser).AsQueryable();
        if (year is not null)
        {
            query = query.Where(p => p.Year == year);
        }

        var items = await query.OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).ToListAsync();
        var years = await _db.Periods.Select(p => p.Year).Distinct().OrderByDescending(y => y).ToListAsync();

        return View(new PeriodIndexViewModel { Items = items, Year = year, YearOptions = years });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(int id)
    {
        var period = await _db.Periods.FindAsync(id);
        if (period is null)
        {
            return NotFound();
        }

        if (period.Status == PeriodStatus.CLOSED)
        {
            TempData[TempDataKeys.ErrorMessage] = "Dönem zaten kapalı.";
            return RedirectToAction(nameof(Index));
        }

        // Kapanış = dönemin kapanması + o döneme ait onaylı kayıtların kilitlenmesi.
        // İkisi tek transaction içinde; bkz. PeriodLockService.
        var lockedCount = await _lockService.CloseAsync(period, _currentUser.UserId);

        TempData[TempDataKeys.SuccessMessage] =
            $"{PeriodStatusDisplay.GetMonthName(period.Month)} {period.Year} dönemi kapatıldı, {lockedCount} onaylı kayıt kilitlendi.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Reopen(int id)
    {
        var period = await _db.Periods.FindAsync(id);
        if (period is null)
        {
            return NotFound();
        }

        if (period.Status != PeriodStatus.CLOSED)
        {
            TempData[TempDataKeys.ErrorMessage] = "Sadece kapalı dönemler yeniden açılabilir.";
            return RedirectToAction(nameof(Index));
        }

        return View(new PeriodReopenViewModel { PeriodId = period.PeriodId, Year = period.Year, Month = period.Month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(int id, PeriodReopenViewModel model)
    {
        if (id != model.PeriodId)
        {
            return NotFound();
        }

        var period = await _db.Periods.FindAsync(id);
        if (period is null)
        {
            return NotFound();
        }

        if (period.Status != PeriodStatus.CLOSED)
        {
            TempData[TempDataKeys.ErrorMessage] = "Sadece kapalı dönemler yeniden açılabilir.";
            return RedirectToAction(nameof(Index));
        }

        // Gerekçe zorunlu: boş ya da sadece boşluktan oluşan bir metin kabul edilmez.
        if (string.IsNullOrWhiteSpace(model.ReopenReason))
        {
            ModelState.AddModelError(nameof(model.ReopenReason), "Dönemi yeniden açmak için gerekçe zorunludur.");
        }

        if (!ModelState.IsValid)
        {
            model.Year = period.Year;
            model.Month = period.Month;
            return View(model);
        }

        // Yeniden açmak kapanışı geri alır: kilitli kayıtlar da APPROVED'a döner.
        var unlockedCount = await _lockService.ReopenAsync(period, _currentUser.UserId, model.ReopenReason!);

        TempData[TempDataKeys.SuccessMessage] =
            $"{PeriodStatusDisplay.GetMonthName(period.Month)} {period.Year} dönemi, gerekçesiyle birlikte yeniden açıldı; " +
            $"{unlockedCount} kaydın kilidi kaldırıldı.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateNextYear()
    {
        var maxYear = await _db.Periods.Select(p => (int?)p.Year).MaxAsync();
        var nextYear = (maxYear ?? DateTime.UtcNow.Year) + 1;

        var exists = await _db.Periods.AnyAsync(p => p.Year == nextYear);
        if (exists)
        {
            TempData[TempDataKeys.ErrorMessage] = $"{nextYear} yılı dönemleri zaten oluşturulmuş.";
            return RedirectToAction(nameof(Index));
        }

        for (var month = 1; month <= 12; month++)
        {
            _db.Periods.Add(new Period { Year = nextYear, Month = month, Status = PeriodStatus.OPEN });
        }
        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = $"{nextYear} yılı için 12 dönem oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }
}
