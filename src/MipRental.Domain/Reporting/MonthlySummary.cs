using MipRental.Domain.Enums;

namespace MipRental.Domain.Reporting;

/// <summary>
/// Bir firmanın bir dönemdeki aylık icmali. Ekran, PDF ve CSV'nin ORTAK kaynağıdır —
/// üç yerde ayrı ayrı toplam hesaplanmaz, hepsi bu nesneyi okur.
///
/// İcmale yalnızca APPROVED ve LOCKED kayıtlar girer; DRAFT / SUBMITTED / PENDING /
/// REJECTED / CANCELLED girmez (bkz. MonthlySummaryService).
/// </summary>
public sealed class MonthlySummary
{
    public required int PeriodId { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required PeriodStatus PeriodStatus { get; init; }

    public required int FirmId { get; init; }
    public required string FirmCode { get; init; }
    public required string FirmTitle { get; init; }

    /// <summary>İcmaldeki kayıtların bağlı olduğu sözleşme numaraları (genelde tek).</summary>
    public required IReadOnlyList<string> ContractNumbers { get; init; }

    /// <summary>Hizmet tipi filtresi uygulandıysa hizmetin adı; uygulanmadıysa null.</summary>
    public int? FilteredServiceId { get; init; }
    public string? FilteredServiceName { get; init; }

    /// <summary>
    /// ADIM 9 — FİYAT GİZLİLİĞİ: bu icmal para bilgisi TAŞIYOR MU?
    /// false ise satırlarda Pricing null, ara toplamlar/genel toplam null ve
    /// Mobilizations boştur — miktar var, tutar yok. Ekran, PDF ve Excel'in
    /// üçü de aynı nesneden beslendiği için kural tek yerde uygulanır.
    /// </summary>
    public required bool IncludesPricing { get; init; }

    public required IReadOnlyList<MonthlySummaryServiceGroup> ServiceGroups { get; init; }

    /// <summary>
    /// Mobilizasyon (sefer başı nakliye) bedelleri. KAYIT seviyesinde bir bedeldir,
    /// satır tutarlarına dahil DEĞİLDİR; bu yüzden ayrı kalem olarak listelenir.
    /// Çok satırlı bir kayıtta da yalnızca bir kez yer alır.
    ///
    /// Tek taşıdığı bilgi tutar olduğu için fiyatsız icmalde BOŞTUR.
    /// </summary>
    public required IReadOnlyList<MonthlySummaryMobilization> Mobilizations { get; init; }

    /// <summary>Satır tutarlarının toplamı (mobilizasyon HARİÇ). Fiyatsız icmalde null.</summary>
    public decimal? LinesTotal { get; init; }

    /// <summary>Mobilizasyon bedellerinin toplamı. Fiyatsız icmalde null.</summary>
    public decimal? MobilizationTotal { get; init; }

    /// <summary>LinesTotal + MobilizationTotal. Fiyatsız icmalde null.</summary>
    public decimal? GrandTotal => LinesTotal is null && MobilizationTotal is null
        ? null
        : (LinesTotal ?? 0m) + (MobilizationTotal ?? 0m);

    public required string Currency { get; init; }

    /// <summary>
    /// Kayıtlar birden fazla para biriminde fiyatlanmışsa true. Bu durumda tek bir
    /// toplam anlamlı değildir; ekran/PDF toplam yerine uyarı gösterir.
    /// </summary>
    public required bool HasMixedCurrency { get; init; }

    /// <summary>İcmale giren kayıt sayısı (satır değil, KAYIT).</summary>
    public required int RecordCount { get; init; }

    /// <summary>Birim bazında toplam miktar. Saat ile adet toplanamayacağı için ayrıştırılır.</summary>
    public required IReadOnlyList<MonthlySummaryQuantityTotal> QuantityTotals { get; init; }

    /// <summary>
    /// Aynı dönem+firmada henüz karara bağlanmamış (DRAFT / SUBMITTED / PENDING /
    /// REVISION_REQUESTED) kayıt sayısı. İcmale GİRMEZLER; kullanıcı bunu bilmeli.
    /// </summary>
    public required int PendingRecordCount { get; init; }

    public bool IsEmpty => RecordCount == 0;
}

public sealed class MonthlySummaryServiceGroup
{
    public required int ServiceId { get; init; }
    public required string ServiceName { get; init; }
    public required ServiceUnit Unit { get; init; }
    public required IReadOnlyList<MonthlySummaryLine> Lines { get; init; }

    /// <summary>Bu hizmetin satır tutarları ara toplamı. Fiyatsız icmalde null.</summary>
    public decimal? SubtotalAmount { get; init; }

    /// <summary>Bu hizmetin faturalanan miktar ara toplamı.</summary>
    public required decimal SubtotalBillableQuantity { get; init; }
}

public sealed class MonthlySummaryLine
{
    public required int WorkRecordId { get; init; }
    public required string DocumentNo { get; init; }
    public required DateOnly WorkDate { get; init; }
    public string? Location { get; init; }

    public required int ServiceId { get; init; }
    public required string ServiceName { get; init; }
    public string? VariantName { get; init; }

    public required decimal RawQuantity { get; init; }
    public required decimal BillableQuantity { get; init; }
    public required ServiceUnit Unit { get; init; }

    /// <summary>Para bilgisi. Fiyatsız icmalde null — alan hiç bulunmaz.</summary>
    public MonthlySummaryLinePricing? Pricing { get; init; }

    public required WorkRecordStatus Status { get; init; }

    /// <summary>Son onayı veren kişi ve tarihi (CSV'de ayrı sütun olarak isteniyor).</summary>
    public string? ApprovedByName { get; init; }
    public DateTime? ApprovedAt { get; init; }
}

public sealed class MonthlySummaryLinePricing
{
    public required decimal UnitPrice { get; init; }
    public required decimal SurchargeAmount { get; init; }
    public required decimal LineAmount { get; init; }
    public required string Currency { get; init; }
}

public sealed class MonthlySummaryMobilization
{
    public required int WorkRecordId { get; init; }
    public required string DocumentNo { get; init; }
    public required DateOnly WorkDate { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}

public sealed class MonthlySummaryQuantityTotal
{
    public required ServiceUnit Unit { get; init; }
    public required decimal TotalBillableQuantity { get; init; }
}
