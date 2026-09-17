using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Reporting;
using MipRental.Domain.Abstractions;
using MipRental.Web.Common;
using MipRental.Web.Documents;
using MipRental.Web.Models.EquipmentOperations;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

/// <summary>
/// ADIM 18 — EKİPMAN MÜDÜRLÜĞÜ AYLIK OPERASYON TABLOSU.
///
/// Hakediş ekranının (SummariesController) YERİNE GEÇMEZ, yanında durur.
/// Fark tek cümlede: hakediş Bütçe'nin mali belgesidir, bu tablo Ekipman
/// Müdürlüğü'nün operasyon dökümüdür. Bu controller HİÇBİR para alanı okumaz —
/// ne sorguda, ne modelde, ne çıktıda. → [[Fiyat Gizliliği]]
///
/// YETKİ: EQUIPMENT_MANAGER, EQUIPMENT_VIEWER, ADMIN. Firma rollerinin hiçbiri
/// yok ve olmamalı: tablo BÜTÜN firmaların işlerini tek listede gösterir
/// (kural 7). Policy tek başına da yeterli değil sayılmaz — AppDbContext'teki
/// firma filtresi ikinci duvardır ve bir firma kullanıcısı buraya bir şekilde
/// girse bile yalnızca kendi kayıtlarını görürdü.
/// </summary>
[Authorize(Policy = PolicyNames.CanViewEquipmentOperations)]
public class EquipmentOperationsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public EquipmentOperationsController(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index(int? periodId, int? departmentId, int? variantId)
    {
        var model = new EquipmentOperationViewModel
        {
            PeriodId = periodId,
            DepartmentId = departmentId,
            VariantId = variantId,
            PeriodOptions = await BuildPeriodOptionsAsync(periodId),
            DepartmentOptions = await BuildDepartmentOptionsAsync(departmentId),
            VariantOptions = await BuildVariantOptionsAsync(variantId)
        };

        if (periodId is not null)
        {
            model.Table = await BuildTableAsync(periodId.Value, departmentId, variantId);
        }

        return View(model);
    }

    /// <summary>
    /// Tablonun Excel (.xlsx) çıktısı. Arşivlenmez ve belge numarası almaz:
    /// bu resmî bir belge değil, Ekipman Müdürlüğü'nün MIP'e verdiği formun
    /// doldurulmuş hâlidir — kaynağı her an yeniden üretilebilir.
    /// </summary>
    public async Task<IActionResult> Excel(int periodId, int? departmentId, int? variantId)
    {
        var table = await BuildTableAsync(periodId, departmentId, variantId);

        return File(
            EquipmentOperationExcelBuilder.Build(table),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            EquipmentOperationExcelBuilder.BuildFileName(table));
    }

    private async Task<EquipmentOperationTable> BuildTableAsync(int periodId, int? departmentId, int? variantId)
    {
        var period = await _db.Periods.AsNoTracking()
            .FirstOrDefaultAsync(p => p.PeriodId == periodId)
            ?? throw new InvalidOperationException($"Dönem bulunamadı (PeriodId = {periodId}).");

        // Hangi kayıtlar girer: SADECE onaylanmış ve kilitlenmiş olanlar. Liste
        // hakediş icmaliyle AYNI kaynaktan okunur (MonthlySummaryService) — iki
        // çıktının "onaylı" tanımı ayrışırsa aynı dönem için iki farklı gerçek olurdu.
        // IsSuperseded: yerine yeni versiyon geçmiş kayıt iki kez listelenmesin.
        var query = _db.WorkRecords.AsNoTracking()
            .Where(w => w.PeriodId == periodId
                     && !w.IsSuperseded
                     && MonthlySummaryService.IncludedStatuses.Contains(w.Status));

        if (departmentId is not null)
        {
            // Filtre, E sütununun gösterdiği değerin AYNISI üzerinden çalışır;
            // ekranda "Ekipman Bakım" yazan satır, o departman seçilince elenemez.
            query = query.Where(w => (w.DepartmentId ?? w.RequestedByUser!.DepartmentId) == departmentId);
        }

        if (variantId is not null)
        {
            query = query.Where(w => w.WorkRecordLines.Any(l => l.VariantId == variantId));
        }

        var records = await query
            .OrderBy(w => w.WorkDate)
            .ThenBy(w => w.ExternalReceiptNo)
            .Select(w => new
            {
                w.WorkDate,
                // Lokasyonun KENDİ adı; FullPath değil (bkz. EquipmentOperationRow).
                LocationName = w.Location != null ? w.Location.Name : null,
                w.LocationText,
                w.WorkDescription,
                DepartmentName = w.Department != null
                    ? w.Department.Name
                    : (w.RequestedByUser != null && w.RequestedByUser.Department != null
                        ? w.RequestedByUser.Department.Name
                        : null),
                VariantNames = w.WorkRecordLines
                    .OrderBy(l => l.LineNo)
                    .Select(l => l.ServiceVariant != null ? l.ServiceVariant.Name : null)
                    .ToList(),
                w.StartTime,
                w.EndTime,
                w.ExternalReceiptNo
            })
            .ToListAsync();

        var periodLabel = FormatPeriod(period.Year, period.Month);

        return new EquipmentOperationTable
        {
            PeriodId = period.PeriodId,
            Year = period.Year,
            Month = period.Month,
            Header = await BuildHeaderAsync(),
            PendingCount = await _db.WorkRecords.AsNoTracking()
                .CountAsync(w => w.PeriodId == periodId
                              && !w.IsSuperseded
                              && MonthlySummaryService.PendingStatuses.Contains(w.Status)),
            Rows = records.Select(r => new EquipmentOperationRow
            {
                PeriodLabel = periodLabel,
                WorkDate = r.WorkDate,
                Location = r.LocationName ?? r.LocationText,
                WorkDescription = r.WorkDescription,
                DepartmentName = r.DepartmentName,
                // Aynı varyanttan birden çok satır varsa bir kez yazılır; farklı
                // varyantlar virgülle. Boş (varyantsız) satırlar atlanır.
                VariantNames = string.Join(", ", r.VariantNames
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()),
                StartTime = r.StartTime,
                EndTime = r.EndTime,
                ReceiptNo = r.ExternalReceiptNo
            }).ToList()
        };
    }

    /// <summary>"2026 / 01" — MIP'in formundaki dönem biçimi.</summary>
    public static string FormatPeriod(int year, int month) => $"{year} / {month:00}";

    /// <summary>
    /// Formu hazırlayan = oturumdaki kullanıcı. Görev ve departman ICurrentUser'da
    /// YOK (orada yalnızca yetki için gerekenler durur), bu yüzden kullanıcı
    /// satırı buradan okunuyor.
    /// </summary>
    private async Task<EquipmentOperationFormHeader> BuildHeaderAsync()
    {
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.UserId == _currentUser.UserId)
            .Select(u => new
            {
                u.FullName,
                u.Position,
                DepartmentName = u.Department != null ? u.Department.Name : null
            })
            .FirstOrDefaultAsync();

        return new EquipmentOperationFormHeader
        {
            FullName = user?.FullName ?? _currentUser.FullName,
            Position = user?.Position,
            DepartmentName = user?.DepartmentName,
            // Düzenleme tarihi BUGÜNDÜR, dönemin ayı değil: form ne zaman
            // hazırlandıysa o. Yerel tarih — veritabanındaki UTC damgalarla
            // karıştırılmamalı, bu alan kaydedilmiyor.
            IssueDate = DateOnly.FromDateTime(DateTime.Now)
        };
    }

    private async Task<List<SelectListItem>> BuildPeriodOptionsAsync(int? selected)
    {
        var periods = await _db.Periods.AsNoTracking()
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .Select(p => new { p.PeriodId, p.Year, p.Month })
            .ToListAsync();

        return periods
            .Select(p => new SelectListItem(
                $"{TrFormat.PeriodName(p.Year, p.Month)} ({FormatPeriod(p.Year, p.Month)})",
                p.PeriodId.ToString(),
                p.PeriodId == selected))
            .ToList();
    }

    private async Task<List<SelectListItem>> BuildDepartmentOptionsAsync(int? selected) =>
        await _db.Departments.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new SelectListItem(d.Name, d.DepartmentId.ToString(), d.DepartmentId == selected))
            .ToListAsync();

    private async Task<List<SelectListItem>> BuildVariantOptionsAsync(int? selected) =>
        await _db.ServiceVariants.AsNoTracking()
            .Where(v => v.IsActive)
            .OrderBy(v => v.Name)
            .Select(v => new SelectListItem(v.Name, v.VariantId.ToString(), v.VariantId == selected))
            .ToListAsync();
}
