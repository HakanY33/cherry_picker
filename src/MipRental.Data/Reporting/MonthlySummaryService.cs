using Microsoft.EntityFrameworkCore;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Enums;
using MipRental.Domain.Reporting;

namespace MipRental.Data.Reporting;

/// <summary>
/// Aylık icmali veritabanından kurar. Ekran, PDF ve CSV aynı buradan besleniyor.
///
/// CLAUDE.md kural 7: firma izolasyonu AppDbContext'teki global query filter ile
/// zaten uygulanır. Buna EK OLARAK istenen firmayı bir firma kullanıcısının
/// isteyip isteyemeyeceğini burada AÇIKÇA da kontrol ediyoruz — "sorgu boş dönsün"
/// yetmez, başka firmanın icmalini isteyen çağrı hata almalıdır.
/// </summary>
public sealed class MonthlySummaryService
{
    // Not: bu iki liste DİZİDİR, HashSet değil. EF Core, sorgu içinde
    // IReadOnlySet<T>.Contains çağrısını SQL'e çeviremiyor; dizi Contains'i
    // ise IN (...) olarak çeviriyor. Filtreleme veritabanında yapılsın diye
    // (kayıtları belleğe çekip elemek yerine) dizi kullanılıyor.

    /// <summary>İcmale giren durumlar. Başka hiçbir durum icmale girmez.</summary>
    public static readonly WorkRecordStatus[] IncludedStatuses =
        [WorkRecordStatus.APPROVED, WorkRecordStatus.LOCKED];

    /// <summary>Henüz karara bağlanmamış, "onay bekliyor" uyarısında sayılan durumlar.</summary>
    public static readonly WorkRecordStatus[] PendingStatuses =
    [
        WorkRecordStatus.DRAFT,
        WorkRecordStatus.SUBMITTED,
        WorkRecordStatus.PENDING,
        WorkRecordStatus.REVISION_REQUESTED
    ];

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public MonthlySummaryService(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Firma kullanıcısının SADECE kendi firmasının icmalini isteyebildiğini doğrular.
    /// Global filtre veriyi zaten gizler; bu kontrol "gizli boş sayfa" yerine açık
    /// bir yetki hatası üretmek içindir.
    /// </summary>
    public bool CanAccessFirm(int firmId) => _currentUser.FirmId is null || _currentUser.FirmId == firmId;

    public async Task<MonthlySummary> BuildAsync(
        int periodId, int firmId, int? serviceId = null, CancellationToken cancellationToken = default)
    {
        if (!CanAccessFirm(firmId))
        {
            throw new UnauthorizedAccessException("Başka bir firmanın icmali görüntülenemez.");
        }

        // ADIM 9 — FİYAT GİZLİLİĞİ. İcmal ekran/PDF/Excel'in ORTAK kaynağı olduğu
        // için kural burada BİR KEZ uygulanır: yetkisiz kullanıcıya kurulan icmalde
        // tutar alanları hiç doldurulmaz (null), mobilizasyon listesi boş kalır.
        // Üç ayrı yerde "tutarı gizle" yazmak, birini unutma riski demekti.
        var includesPricing = _currentUser.CanSeePricing;

        var period = await _db.Periods.AsNoTracking()
            .FirstOrDefaultAsync(p => p.PeriodId == periodId, cancellationToken)
            ?? throw new InvalidOperationException($"Dönem bulunamadı (PeriodId = {periodId}).");

        // Firms'te firma izolasyon filtresi yok; firma kullanıcısı için yukarıdaki
        // CanAccessFirm zaten kendi firması dışını engelledi.
        var firm = await _db.Firms.AsNoTracking()
            .FirstOrDefaultAsync(f => f.FirmId == firmId, cancellationToken)
            ?? throw new InvalidOperationException($"Firma bulunamadı (FirmId = {firmId}).");

        var service = serviceId is null
            ? null
            : await _db.ServiceCategories.AsNoTracking().FirstOrDefaultAsync(s => s.ServiceId == serviceId, cancellationToken);

        // IsSuperseded: yerine yeni versiyon geçmiş kayıt icmalde İKİ KEZ sayılmasın.
        // Bugünkü akışta onaylı bir kayıt zaten superseded olamaz (revizyon yalnızca
        // REVISION_REQUESTED durumundan üretilir), ama toplam bu varsayıma bağlı kalmamalı.
        var recordQuery = _db.WorkRecords.AsNoTracking()
            .Where(w => w.PeriodId == periodId
                     && w.FirmId == firmId
                     && !w.IsSuperseded
                     && IncludedStatuses.Contains(w.Status));

        var records = await recordQuery
            .Select(w => new
            {
                w.WorkRecordId,
                w.DocumentNo,
                w.WorkDate,
                w.Status,
                w.MobilizationFee,
                w.Currency,
                w.ApprovedAt,
                ContractNo = w.Contract.ContractNo,
                LocationName = w.Location != null ? w.Location.Name : null,
                w.LocationText,
                Lines = w.WorkRecordLines
                    .OrderBy(l => l.LineNo)
                    .Select(l => new
                    {
                        l.ServiceId,
                        ServiceName = l.ServiceCategory.Name,
                        ServiceUnit = l.ServiceCategory.Unit,
                        VariantName = l.ServiceVariant != null ? l.ServiceVariant.Name : null,
                        l.RawQuantity,
                        l.BillableQuantity,
                        l.Unit,
                        l.UnitPriceSnapshot,
                        l.SurchargeAmount,
                        l.LineAmount,
                        l.Currency
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        // Son onayı veren kişiler tek sorguda. Approvals firma izolasyon filtresine
        // takılmaz çünkü ilgili WorkRecord zaten yukarıdaki filtreden geçti.
        var recordIds = records.Select(r => r.WorkRecordId).ToList();
        var approverNames = new Dictionary<int, string>();
        if (recordIds.Count > 0)
        {
            var decisions = await _db.Approvals.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.DocumentType == DocumentType.WORK_RECORD
                         && recordIds.Contains(a.DocumentId)
                         && a.Decision == ApprovalDecision.APPROVED
                         && a.DecidedByUser != null)
                .OrderByDescending(a => a.DecidedAt)
                .Select(a => new { a.DocumentId, Name = a.DecidedByUser!.FullName })
                .ToListAsync(cancellationToken);

            // Çok adımlı akışta birden fazla APPROVED kararı olur; icmalde
            // SON onaylayanı gösteriyoruz (yukarıda DecidedAt'e göre azalan sıralı).
            foreach (var decision in decisions)
            {
                approverNames.TryAdd(decision.DocumentId, decision.Name);
            }
        }

        var flatLines = new List<MonthlySummaryLine>();
        foreach (var record in records)
        {
            var location = record.LocationName ?? record.LocationText;
            foreach (var line in record.Lines)
            {
                if (serviceId is not null && line.ServiceId != serviceId)
                {
                    continue;
                }

                flatLines.Add(new MonthlySummaryLine
                {
                    WorkRecordId = record.WorkRecordId,
                    DocumentNo = record.DocumentNo,
                    WorkDate = record.WorkDate,
                    Location = location,
                    ServiceId = line.ServiceId,
                    ServiceName = line.ServiceName,
                    VariantName = line.VariantName,
                    RawQuantity = line.RawQuantity,
                    BillableQuantity = line.BillableQuantity,
                    Unit = line.Unit,
                    Pricing = includesPricing
                        ? new MonthlySummaryLinePricing
                        {
                            UnitPrice = line.UnitPriceSnapshot,
                            SurchargeAmount = line.SurchargeAmount,
                            LineAmount = line.LineAmount,
                            Currency = line.Currency
                        }
                        : null,
                    Status = record.Status,
                    ApprovedByName = approverNames.GetValueOrDefault(record.WorkRecordId),
                    ApprovedAt = record.ApprovedAt
                });
            }
        }

        // Hizmet filtresi varken, o hizmetten hiç satırı olmayan kayıt icmale girmez —
        // dolayısıyla mobilizasyon bedeli de girmez.
        var includedRecordIds = flatLines.Select(l => l.WorkRecordId).ToHashSet();

        // Mobilizasyon kaleminin TEK taşıdığı bilgi tutardır; fiyatsız icmalde
        // listelenecek bir şey kalmaz.
        var mobilizations = includesPricing
            ? records
                .Where(r => includedRecordIds.Contains(r.WorkRecordId) && r.MobilizationFee is > 0m)
                .OrderBy(r => r.WorkDate).ThenBy(r => r.DocumentNo)
                .Select(r => new MonthlySummaryMobilization
                {
                    WorkRecordId = r.WorkRecordId,
                    DocumentNo = r.DocumentNo,
                    WorkDate = r.WorkDate,
                    Amount = r.MobilizationFee!.Value,
                    Currency = r.Currency ?? "TRY"
                })
                .ToList()
            : new List<MonthlySummaryMobilization>();

        var serviceGroups = flatLines
            .GroupBy(l => new { l.ServiceId, l.ServiceName, l.Unit })
            .OrderBy(g => g.Key.ServiceName, StringComparer.CurrentCulture)
            .Select(g => new MonthlySummaryServiceGroup
            {
                ServiceId = g.Key.ServiceId,
                ServiceName = g.Key.ServiceName,
                Unit = g.Key.Unit,
                Lines = g.OrderBy(l => l.WorkDate).ThenBy(l => l.DocumentNo, StringComparer.Ordinal).ToList(),
                SubtotalAmount = includesPricing ? g.Sum(l => l.Pricing!.LineAmount) : null,
                SubtotalBillableQuantity = g.Sum(l => l.BillableQuantity)
            })
            .ToList();

        // Para birimi listesi HAM veriden kurulur: fiyatsız icmalde satırlarda
        // Pricing yoktur ama "karışık para birimi" uyarısı yine doğru olmalı.
        var currencies = records
            .Where(r => includedRecordIds.Contains(r.WorkRecordId))
            .SelectMany(r => r.Lines.Select(l => l.Currency))
            .Concat(records
                .Where(r => includedRecordIds.Contains(r.WorkRecordId) && r.MobilizationFee is > 0m)
                .Select(r => r.Currency ?? "TRY"))
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var pendingCount = await _db.WorkRecords.AsNoTracking()
            .CountAsync(w => w.PeriodId == periodId
                          && w.FirmId == firmId
                          && !w.IsSuperseded
                          && PendingStatuses.Contains(w.Status), cancellationToken);

        return new MonthlySummary
        {
            PeriodId = period.PeriodId,
            Year = period.Year,
            Month = period.Month,
            PeriodStatus = period.Status,
            FirmId = firm.FirmId,
            FirmCode = firm.Code,
            FirmTitle = firm.Title,
            ContractNumbers = records
                .Where(r => includedRecordIds.Contains(r.WorkRecordId))
                .Select(r => r.ContractNo)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToList(),
            FilteredServiceId = serviceId,
            FilteredServiceName = service?.Name,
            IncludesPricing = includesPricing,
            ServiceGroups = serviceGroups,
            Mobilizations = mobilizations,
            LinesTotal = includesPricing ? flatLines.Sum(l => l.Pricing!.LineAmount) : null,
            MobilizationTotal = includesPricing ? mobilizations.Sum(m => m.Amount) : null,
            Currency = currencies.Count > 0 ? currencies[0] : "TRY",
            HasMixedCurrency = currencies.Count > 1,
            RecordCount = includedRecordIds.Count,
            QuantityTotals = flatLines
                .GroupBy(l => l.Unit)
                .OrderBy(g => g.Key)
                .Select(g => new MonthlySummaryQuantityTotal
                {
                    Unit = g.Key,
                    TotalBillableQuantity = g.Sum(l => l.BillableQuantity)
                })
                .ToList(),
            PendingRecordCount = pendingCount
        };
    }
}
