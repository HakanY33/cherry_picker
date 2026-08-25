using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Reporting;
using MipRental.Domain.Abstractions;
using MipRental.Web.Common;
using MipRental.Web.Documents;
using MipRental.Web.Models.Summaries;

namespace MipRental.Web.Controllers;

/// <summary>
/// Aylık icmal (İcmal) ekranı ve çıktıları.
///
/// YETKİ: MIP personeli her firmanın icmalini görebilir. Firma kullanıcısı
/// SADECE kendi firmasınınkini — bu kontrol MonthlySummaryService.CanAccessFirm'de
/// yapılır ve UI'da firma kutusunu gizlemekle YETİNİLMEZ (CLAUDE.md kural 7).
/// </summary>
[Authorize]
public class SummariesController : Controller
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly MonthlySummaryService _summaries;
    private readonly DocumentGenerator _documents;

    public SummariesController(
        AppDbContext db, ICurrentUser currentUser, MonthlySummaryService summaries, DocumentGenerator documents)
    {
        _db = db;
        _currentUser = currentUser;
        _summaries = summaries;
        _documents = documents;
    }

    public async Task<IActionResult> Index(int? periodId, int? firmId, int? serviceId)
    {
        var effectiveFirmId = ResolveFirmId(firmId);

        var model = new MonthlySummaryViewModel
        {
            PeriodId = periodId,
            FirmId = effectiveFirmId,
            ServiceId = serviceId,
            CanChooseFirm = _currentUser.IsMipStaff,
            PeriodOptions = await BuildPeriodOptionsAsync(periodId),
            FirmOptions = _currentUser.IsMipStaff ? await BuildFirmOptionsAsync(effectiveFirmId) : new List<SelectListItem>(),
            ServiceOptions = await BuildServiceOptionsAsync(serviceId)
        };

        if (periodId is null || effectiveFirmId is null)
        {
            return View(model);
        }

        try
        {
            model.Summary = await _summaries.BuildAsync(periodId.Value, effectiveFirmId.Value, serviceId);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        return View(model);
    }

    /// <summary>Aylık icmal PDF'i. Her indirişte yeni bir GeneratedDocuments SÜRÜMÜ oluşur.</summary>
    public async Task<IActionResult> Pdf(int periodId, int? firmId, int? serviceId)
    {
        var effectiveFirmId = ResolveFirmId(firmId);

        try
        {
            var summary = await _summaries.BuildAsync(periodId, effectiveFirmId!.Value, serviceId);
            var result = await _documents.GenerateMonthlySummaryAsync(summary, BuildVerificationUrl);
            return File(result.Content, "application/pdf", result.FileName);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Aylık icmalin Excel (.xlsx) çıktısı. Arşivlenmez: doğrulanabilir resmî belge
    /// PDF'tir, Excel Bütçe'nin üzerinde çalışacağı bir çalışma dosyasıdır — bu yüzden
    /// tutar SAYI, tarih TARİH hücresi olarak yazılır (bkz. MonthlySummaryExcelBuilder).
    /// </summary>
    public async Task<IActionResult> Excel(int periodId, int? firmId, int? serviceId)
    {
        var effectiveFirmId = ResolveFirmId(firmId);

        try
        {
            var summary = await _summaries.BuildAsync(periodId, effectiveFirmId!.Value, serviceId);
            var bytes = MonthlySummaryExcelBuilder.Build(summary);

            return File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                MonthlySummaryExcelBuilder.BuildFileName(summary));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Hangi firmanın icmali kurulacak?
    ///
    /// Firma kullanıcısı için firma seçilmemişse kendi firması varsayılır (ekranda
    /// firma kutusu zaten yok). Ama BAŞKA bir firma AÇIKÇA istenmişse istek sessizce
    /// kendi firmasına çevrilmez — talep olduğu gibi servise gider ve servis yetki
    /// hatası verir. Sessizce düzeltmek, "başka firmanın icmalini istedim, kendiminki
    /// geldi" gibi yanıltıcı bir davranış olurdu; reddetmek doğru cevaptır.
    /// </summary>
    private int? ResolveFirmId(int? requestedFirmId) =>
        requestedFirmId ?? _currentUser.FirmId;

    private string BuildVerificationUrl(string code) =>
        Url.Action(nameof(VerificationController.Index), "Verification", new { code }, Request.Scheme)
        ?? $"{Request.Scheme}://{Request.Host}/Dogrula/{code}";

    private async Task<List<SelectListItem>> BuildPeriodOptionsAsync(int? selected)
    {
        // Dönem adı ("Ağustos 2026") Türkçe sözlükten gelir; SQL'e çevrilemez,
        // bu yüzden yıl/ay çekilip etiket bellekte kuruluyor.
        var periods = await _db.Periods.AsNoTracking()
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .Select(p => new { p.PeriodId, p.Year, p.Month })
            .ToListAsync();

        return periods
            .Select(p => new SelectListItem(
                TrFormat.PeriodName(p.Year, p.Month),
                p.PeriodId.ToString(),
                p.PeriodId == selected))
            .ToList();
    }

    private async Task<List<SelectListItem>> BuildFirmOptionsAsync(int? selected) =>
        await _db.Firms.AsNoTracking()
            .Where(f => f.IsActive)
            .OrderBy(f => f.Title)
            .Select(f => new SelectListItem(f.Title, f.FirmId.ToString(), f.FirmId == selected))
            .ToListAsync();

    private async Task<List<SelectListItem>> BuildServiceOptionsAsync(int? selected) =>
        await _db.ServiceCategories.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new SelectListItem(s.Name, s.ServiceId.ToString(), s.ServiceId == selected))
            .ToListAsync();
}
