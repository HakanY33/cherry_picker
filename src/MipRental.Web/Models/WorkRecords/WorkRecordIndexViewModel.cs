using Microsoft.AspNetCore.Mvc.Rendering;
using MipRental.Domain.Enums;

namespace MipRental.Web.Models.WorkRecords;

public class WorkRecordIndexViewModel
{
    public IReadOnlyList<WorkRecordRowView> Items { get; init; } = Array.Empty<WorkRecordRowView>();
    public int CurrentPage { get; init; }
    public int TotalPages { get; init; }

    // MIP personeli için: tüm firmalar görünür ve firma filtresi sunulur.
    // Firma kullanıcısı için query filter zaten sadece kendi firmasını döner; filtre gizlenir.
    public bool ShowFirmFilter { get; init; }

    /// <summary>
    /// Adım 9: Tutar sütunu yalnızca CanSeePricing yetkisi olana gösterilir.
    /// Satırlarda Pricing null ise sütun HİÇ basılmaz — boş hücre de bir bilgidir.
    /// </summary>
    public bool ShowPricing { get; init; }

    public int? FirmId { get; init; }
    public int? PeriodId { get; init; }
    public WorkRecordStatus? Status { get; init; }

    public List<SelectListItem> FirmOptions { get; init; } = new();
    public List<SelectListItem> PeriodOptions { get; init; } = new();
}

/// <summary>
/// Liste satırı. WorkRecord ENTITY'si taşınmaz: entity TotalAmount/MobilizationFee
/// kolonlarını da beraberinde getirirdi (Adım 9).
/// </summary>
public sealed class WorkRecordRowView
{
    public required int WorkRecordId { get; init; }
    public required string DocumentNo { get; init; }
    public required string FirmTitle { get; init; }
    public required DateOnly WorkDate { get; init; }
    public required int PeriodYear { get; init; }
    public required int PeriodMonth { get; init; }
    public required WorkRecordStatus Status { get; init; }

    /// <summary>Para bilgisi. Yetkisiz kullanıcıda null — alan hiç bulunmaz.</summary>
    public WorkRecordRowPricingView? Pricing { get; init; }
}

public sealed class WorkRecordRowPricingView
{
    public decimal? TotalAmount { get; init; }
    public string? Currency { get; init; }
}
